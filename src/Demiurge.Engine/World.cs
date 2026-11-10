using Demiurge.Engine.Actions;
using Demiurge.Engine.Domain;
using Demiurge.Engine.Events;
using Demiurge.Engine.Randomness;

namespace Demiurge.Engine;

/// <summary>
/// The simulated world and the only authority over its state. Time advances only through
/// <see cref="Step"/>: one call = one tick = one simulated hour. Nothing changes between ticks.
/// Concepts: Tick-based simulation — docs/CONCEPTS.md#simulação-baseada-em-ticks ;
///           Determinism and seed — docs/CONCEPTS.md#determinismo-e-seed ;
///           Authoritative engine — docs/CONCEPTS.md#engine-autoritativo
/// </summary>
public sealed class World
{
    private readonly List<Agent> _agents;  // always ordered by AgentId: iteration order is part of determinism
    private readonly IRandom _rng;

    private World(WorldConfig config, IRandom rng, List<Agent> agents, Market market)
    {
        Config = config;
        _rng = rng;
        _agents = agents;
        Market = market;
    }

    public WorldConfig Config { get; }

    /// <summary>Current tick. 0 = genesis, before any step.</summary>
    public long Tick { get; private set; }

    public IReadOnlyList<Agent> Agents => _agents;

    public Market Market { get; }

    /// <summary>RNG state; goes into snapshots so a restored world continues the same random sequence.</summary>
    public ulong RngState => _rng.State;

    /// <summary>Creates a world from its config. Returns the genesis report (tick 0).</summary>
    public static (World World, TickReport Genesis) Create(WorldConfig config)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(config.AgentCount);

        var rng = new SplitMix64(config.Seed);
        var agents = new List<Agent>(config.AgentCount);
        var events = new List<WorldEvent>(config.AgentCount + 1);

        for (int i = 1; i <= config.AgentCount; i++)
        {
            var agent = new Agent(
                new AgentId(i),
                hunger: rng.NextInt(config.InitialHunger.Min, config.InitialHunger.Max),
                energy: rng.NextInt(config.InitialEnergy.Min, config.InitialEnergy.Max),
                food: rng.NextInt(config.InitialFood.Min, config.InitialFood.Max),
                money: rng.NextInt(config.InitialMoney.Min, config.InitialMoney.Max));
            agents.Add(agent);
            events.Add(new AgentSpawned(0, agent.Id, agent.Hunger, agent.Energy, agent.Food, agent.Money));
        }

        var market = new Market(config.InitialMarketStock, config.FoodPrice);
        events.Add(new MarketOpened(0, market.FoodStock, market.FoodPrice));

        return (new World(config, rng, agents, market), new TickReport(0, events));
    }

    /// <summary>
    /// What one agent can see now (end of the last tick). An immutable copy: the brain gets no
    /// reference to mutable state.
    /// </summary>
    public Perception Perceive(AgentId id)
    {
        var agent = FindAgent(id) ?? throw new ArgumentException($"Unknown agent {id}.", nameof(id));
        return new Perception(
            Tick, agent.Id, agent.Hunger, agent.Energy, agent.Food, agent.Money,
            Market.FoodStock, Market.FoodPrice, agent.LastRejection);
    }

    /// <summary>Advances one tick with no decisions (every agent idles).</summary>
    public TickReport Step() => Step([]);

    /// <summary>
    /// Advances exactly one tick. Decisions were made by brains looking at the state of the
    /// previous tick. Phases, in fixed order:
    /// 1. resolve decisions (validate → apply or reject), in a seeded shuffled order;
    /// 2. the hour passes: needs decay, starvation;
    /// 3. the market restocks.
    /// </summary>
    public TickReport Step(IReadOnlyList<AgentDecision> decisions)
    {
        Tick++;
        var events = new List<WorldEvent> { new TickStarted(Tick) };

        foreach (var agent in _agents)
            agent.LastRejection = null;

        ResolveDecisions(decisions, events);
        ApplyNeeds(events);
        RestockMarket(events);

        return new TickReport(Tick, events);
    }

    private void ResolveDecisions(IReadOnlyList<AgentDecision> decisions, List<WorldEvent> events)
    {
        // Input order must not matter: brains may answer in any order (parallel, network latency).
        // Sort by agent id first, THEN shuffle with the seed, so the order depends only on the seed.
        var ordered = decisions.OrderBy(d => d.AgentId).ToList();
        _rng.Shuffle(ordered);

        // More than one proposal from the same agent is ambiguous: refuse all of them, deterministically.
        var duplicated = decisions.GroupBy(d => d.AgentId).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();

        foreach (var decision in ordered)
        {
            var agent = FindAgent(decision.AgentId);
            if (agent is null)
            {
                events.Add(new ActionRejected(Tick, decision.AgentId, decision.Action, RejectionReason.UnknownAgent));
                continue;
            }

            if (!agent.IsAlive)
            {
                Reject(agent, decision.Action, RejectionReason.AgentNotAlive, events);
                continue;
            }

            if (duplicated.Contains(agent.Id))
            {
                Reject(agent, decision.Action, RejectionReason.DuplicateDecision, events);
                continue;
            }

            var action = decision.Action;
            if (agent.Energy == 0 && action is not Rest)
            {
                // World rule: an exhausted agent can only rest. The proposal is refused and Rest is applied.
                Reject(agent, action, RejectionReason.Exhausted, events);
                action = new Rest();
            }

            var reason = ActionRules.Validate(agent, action, Market);
            if (reason is { } r)
                Reject(agent, action, r, events);
            else
                events.Add(ActionRules.Apply(Tick, agent, action, Market));
        }
    }

    private void Reject(Agent agent, AgentAction action, RejectionReason reason, List<WorldEvent> events)
    {
        agent.LastRejection = reason;
        events.Add(new ActionRejected(Tick, agent.Id, action, reason));
    }

    private void ApplyNeeds(List<WorldEvent> events)
    {
        foreach (var agent in _agents)
        {
            if (!agent.IsAlive)
                continue;

            agent.Hunger = Math.Min(WorldRules.MaxNeed, agent.Hunger + WorldRules.HungerPerTick);
            agent.Energy = Math.Max(0, agent.Energy - WorldRules.EnergyPerTick);
            events.Add(new NeedsUpdated(Tick, agent.Id, agent.Hunger, agent.Energy));

            agent.TicksStarving = agent.Hunger >= WorldRules.MaxNeed ? agent.TicksStarving + 1 : 0;
            if (agent.TicksStarving >= WorldRules.StarvationTicks)
            {
                agent.IsAlive = false;
                events.Add(new AgentDied(Tick, agent.Id, DeathCause.Starvation));
            }
        }
    }

    private void RestockMarket(List<WorldEvent> events)
    {
        int restocked = Math.Min(Config.MarketCapacity, Market.FoodStock + Config.MarketRestockPerTick);
        if (restocked == Market.FoodStock)
            return;

        Market.FoodStock = restocked;
        events.Add(new MarketRestocked(Tick, Market.FoodStock));
    }

    private Agent? FindAgent(AgentId id) => _agents.Find(a => a.Id == id);
}
