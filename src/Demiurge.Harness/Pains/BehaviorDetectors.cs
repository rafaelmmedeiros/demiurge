using Demiurge.Engine;
using Demiurge.Engine.Actions;
using Demiurge.Engine.Domain;
using Demiurge.Engine.Events;

namespace Demiurge.Harness.Pains;

// Behavior detectors: where the brain is weak. They read only events, decisions and the snapshot,
// so they measure any brain the same way. Thresholds are first guesses, calibrated by measurement.
// Concept: Pain detectors — docs/CONCEPTS.md#harness-observabilidade-e-detectores-de-dor

/// <summary>STARVATION_PARADOX: died of hunger while able to buy food (money ≥ price and stock ≥ 1).</summary>
public sealed class StarvationParadoxDetector : IPainDetector
{
    public IEnumerable<PainSignal> Inspect(TickContext ctx)
    {
        foreach (var death in ctx.Report.Events.OfType<AgentDied>().Where(d => d.Cause == DeathCause.Starvation))
        {
            var agent = ctx.After.Agents.First(a => a.Id == death.AgentId);
            if (agent.Money >= ctx.After.Market.FoodPrice && ctx.After.Market.FoodStock >= 1)
                yield return new PainSignal(
                    PainCodes.StarvationParadox, PainCategory.Behavior, 5, ctx.Tick, agent.Id,
                    $"{agent.Id} starved at tick {ctx.Tick} with money {agent.Money} (price {ctx.After.Market.FoodPrice}), market stock {ctx.After.Market.FoodStock}.");
        }
    }
}

/// <summary>RESOURCE_COLLAPSE: ≥ 50% of the initial population dead (sev 5), or money Gini among the living &gt; 0.6 (sev 3).</summary>
public sealed class ResourceCollapseDetector : IPainDetector
{
    public const double MortalityThreshold = 0.5;
    public const double GiniThreshold = 0.6;

    private readonly EdgeTrigger<string> _trigger = new();

    public IEnumerable<PainSignal> Inspect(TickContext ctx)
    {
        var agents = ctx.After.Agents;
        int dead = agents.Count(a => !a.IsAlive);
        double mortality = agents.Count == 0 ? 0 : dead / (double)agents.Count;
        if (_trigger.Rising("mortality", mortality >= MortalityThreshold))
            yield return new PainSignal(
                PainCodes.ResourceCollapse, PainCategory.Behavior, 5, ctx.Tick, null,
                $"Mortality {dead}/{agents.Count} ({mortality:P0}) at tick {ctx.Tick}.");

        var money = agents.Where(a => a.IsAlive).Select(a => a.Money).ToList();
        double gini = Gini(money);
        if (_trigger.Rising("gini", money.Count >= 2 && gini > GiniThreshold))
            yield return new PainSignal(
                PainCodes.ResourceCollapse, PainCategory.Behavior, 3, ctx.Tick, null,
                $"Money Gini {gini:F2} among {money.Count} living agents at tick {ctx.Tick}.");
    }

    /// <summary>Gini = Σ|xi − xj| / (2 n² mean). 0 = everyone equal, → 1 = one holds everything.</summary>
    public static double Gini(IReadOnlyList<int> values)
    {
        long sum = values.Sum(v => (long)v);
        if (values.Count == 0 || sum == 0)
            return 0;
        long diffs = 0;
        foreach (var a in values)
            foreach (var b in values)
                diffs += Math.Abs(a - b);
        return diffs / (2.0 * values.Count * sum);
    }
}

/// <summary>
/// GOAL_OSCILLATION (ADR-005): the agent arrives somewhere and leaves again without doing anything
/// there. Observable proxy for "changed goal before finishing it" — goals are internal to a brain,
/// arrivals and actions are not. Fires when, among ≥ 5 arrivals resolved in the last 50 ticks,
/// more than 40% were wasted.
/// </summary>
public sealed class GoalOscillationDetector : IPainDetector
{
    public const int Window = 50;
    public const int MinArrivals = 5;
    public const double WastedThreshold = 0.4;

    private readonly Dictionary<AgentId, PlaceId> _pendingArrival = [];
    private readonly Dictionary<AgentId, Queue<(long Tick, bool Wasted)>> _resolved = [];
    private readonly EdgeTrigger<AgentId> _trigger = new();

    public IEnumerable<PainSignal> Inspect(TickContext ctx)
    {
        foreach (var e in ctx.Report.Events)
        {
            switch (e)
            {
                case TravelStarted t when _pendingArrival.Remove(t.AgentId):
                    Resolve(t.AgentId, ctx.Tick, wasted: true);
                    break;
                case Arrived a:
                    _pendingArrival[a.AgentId] = a.Place;
                    break;
                default:
                    if (Actor(e) is { } actor && _pendingArrival.Remove(actor))
                        Resolve(actor, ctx.Tick, wasted: false);
                    break;
            }
        }

        foreach (var agent in ctx.After.Agents)
        {
            if (!_resolved.TryGetValue(agent.Id, out var arrivals))
                continue;
            while (arrivals.Count > 0 && arrivals.Peek().Tick <= ctx.Tick - Window)
                arrivals.Dequeue();

            int wasted = arrivals.Count(a => a.Wasted);
            bool oscillating = agent.IsAlive && arrivals.Count >= MinArrivals
                               && wasted / (double)arrivals.Count > WastedThreshold;
            if (_trigger.Rising(agent.Id, oscillating))
                yield return new PainSignal(
                    PainCodes.GoalOscillation, PainCategory.Behavior, 3, ctx.Tick, agent.Id,
                    $"{agent.Id}: {wasted} of {arrivals.Count} arrivals in the last {Window} ticks left without acting (tick {ctx.Tick}).");
        }
    }

    private void Resolve(AgentId id, long tick, bool wasted)
    {
        if (!_resolved.TryGetValue(id, out var q))
            _resolved[id] = q = new Queue<(long, bool)>();
        q.Enqueue((tick, wasted));
    }

    /// <summary>The agent that performed an accepted action, for action events.</summary>
    private static AgentId? Actor(WorldEvent e) => e switch
    {
        Worked w => w.AgentId,
        FoodBought f => f.AgentId,
        Rested r => r.AgentId,
        Ate a => a.AgentId,
        Talked t => t.Speaker,
        Stole s => s.Thief,
        _ => null,
    };
}

/// <summary>
/// REPETITION_LOOP: the same action proposed for 8 consecutive ticks without any resource improving
/// (hunger down, or energy, money or food up) from one tick to the next.
/// </summary>
public sealed class RepetitionLoopDetector : IPainDetector
{
    public const int Threshold = 8;

    private readonly Dictionary<AgentId, (AgentAction Action, long Tick, int Run)> _runs = [];
    private readonly Dictionary<AgentId, AgentSnapshot> _previous = [];
    private readonly EdgeTrigger<AgentId> _trigger = new();

    public IEnumerable<PainSignal> Inspect(TickContext ctx)
    {
        var decided = ctx.Decisions.ToDictionary(d => d.AgentId, d => d.Action);

        foreach (var agent in ctx.After.Agents)
        {
            bool improved = _previous.TryGetValue(agent.Id, out var before) && Improved(before, agent);
            _previous[agent.Id] = agent;

            if (!decided.TryGetValue(agent.Id, out var action))
            {
                _runs.Remove(agent.Id);
                _trigger.Rising(agent.Id, false);
                continue;
            }

            int run = _runs.TryGetValue(agent.Id, out var last) && last.Action == action && last.Tick == ctx.Tick - 1 && !improved
                ? last.Run + 1
                : 1;
            _runs[agent.Id] = (action, ctx.Tick, run);

            if (_trigger.Rising(agent.Id, run >= Threshold))
                yield return new PainSignal(
                    PainCodes.RepetitionLoop, PainCategory.Behavior, 3, ctx.Tick, agent.Id,
                    $"{agent.Id} proposed {action} for {run} consecutive ticks without improving any resource (tick {ctx.Tick}).");
        }
    }

    private static bool Improved(AgentSnapshot before, AgentSnapshot now) =>
        now.Hunger < before.Hunger || now.Energy > before.Energy || now.Money > before.Money || now.Food > before.Food;
}

/// <summary>ACTION_REJECTION_RATE: more than 20% of an agent's last 50 decisions rejected (min. 10 decisions).</summary>
public sealed class ActionRejectionRateDetector : IPainDetector
{
    public const int Window = 50;
    public const int MinDecisions = 10;
    public const double Threshold = 0.2;

    private readonly Dictionary<AgentId, Queue<bool>> _outcomes = [];
    private readonly EdgeTrigger<AgentId> _trigger = new();

    public IEnumerable<PainSignal> Inspect(TickContext ctx)
    {
        var rejected = ctx.Report.Events.OfType<ActionRejected>().Select(r => r.AgentId).ToHashSet();
        var decidedNow = ctx.Decisions.Select(d => d.AgentId).ToHashSet();

        foreach (var agent in ctx.After.Agents.Where(a => decidedNow.Contains(a.Id)))
        {
            if (!_outcomes.TryGetValue(agent.Id, out var q))
                _outcomes[agent.Id] = q = new Queue<bool>();
            q.Enqueue(rejected.Contains(agent.Id));
            if (q.Count > Window)
                q.Dequeue();

            int count = q.Count(r => r);
            double rate = count / (double)q.Count;
            if (_trigger.Rising(agent.Id, q.Count >= MinDecisions && rate > Threshold))
                yield return new PainSignal(
                    PainCodes.ActionRejectionRate, PainCategory.Behavior, 2, ctx.Tick, agent.Id,
                    $"{agent.Id}: {count} of its last {q.Count} decisions rejected ({rate:P0}) at tick {ctx.Tick}; last reason {agent.LastRejection}.");
        }
    }
}

/// <summary>NO_SOCIAL_CONTACT: 48 ticks (2 simulated days) without talking or being talked to, while others are alive.</summary>
public sealed class NoSocialContactDetector : IPainDetector
{
    public const int Threshold = 48;

    private readonly Dictionary<AgentId, long> _lastContact = [];
    private readonly EdgeTrigger<AgentId> _trigger = new();

    public IEnumerable<PainSignal> Inspect(TickContext ctx)
    {
        foreach (var talk in ctx.Report.Events.OfType<Talked>())
        {
            _lastContact[talk.Speaker] = ctx.Tick;
            _lastContact[talk.Listener] = ctx.Tick;
        }

        int alive = ctx.After.Agents.Count(a => a.IsAlive);
        foreach (var agent in ctx.After.Agents)
        {
            long last = _lastContact.TryGetValue(agent.Id, out var t) ? t : 0;
            long silent = ctx.Tick - last;
            bool isolated = agent.IsAlive && alive >= 2 && silent >= Threshold;
            if (_trigger.Rising(agent.Id, isolated))
                yield return new PainSignal(
                    PainCodes.NoSocialContact, PainCategory.Behavior, 2, ctx.Tick, agent.Id,
                    $"{agent.Id}: no conversation for {silent} ticks with {alive - 1} other agents alive (tick {ctx.Tick}).");
        }
    }
}

public static class PainDetectors
{
    /// <summary>A fresh set of the default detectors (they are stateful: one set per world).</summary>
    public static IReadOnlyList<IPainDetector> CreateDefault() =>
    [
        new TickExceptionDetector(),
        new TickOverrunDetector(),
        new StarvationParadoxDetector(),
        new ResourceCollapseDetector(),
        new GoalOscillationDetector(),
        new RepetitionLoopDetector(),
        new ActionRejectionRateDetector(),
        new NoSocialContactDetector(),
    ];
}
