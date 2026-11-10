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
///           Authoritative engine — docs/CONCEPTS.md#engine-autoritativo ;
///           Places graph — docs/CONCEPTS.md#grafo-de-lugares
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

    internal IRandom Rng => _rng;

    /// <summary>Creates a world from its config. Returns the genesis report (tick 0).</summary>
    public static (World World, TickReport Genesis) Create(WorldConfig config)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(config.AgentCount);
        if (config.Graph.Find(config.StartPlace) is null)
            throw new ArgumentException($"Start place {config.StartPlace} is not in the graph.", nameof(config));

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
                money: rng.NextInt(config.InitialMoney.Min, config.InitialMoney.Max),
                location: new AtPlace(config.StartPlace));
            agents.Add(agent);
            events.Add(new AgentSpawned(0, agent.Id, agent.Hunger, agent.Energy, agent.Food, agent.Money, config.StartPlace));
        }

        var market = new Market(config.InitialMarketStock, config.FoodPrice);
        events.Add(new MarketOpened(0, market.FoodStock, market.FoodPrice));

        return (new World(config, rng, agents, market), new TickReport(0, events));
    }

    /// <summary>
    /// What one agent can see now (end of the last tick). An immutable copy: the brain gets no
    /// reference to mutable state, and sees only its surroundings, not the whole world.
    /// </summary>
    public Perception Perceive(AgentId id)
    {
        var agent = FindAgent(id) ?? throw new ArgumentException($"Unknown agent {id}.", nameof(id));
        IReadOnlyList<AgentId> present = agent.Location is AtPlace at
            ? PresentAt(at.Place).Where(a => a.Id != agent.Id).Select(a => a.Id).ToList()
            : [];

        return new Perception(
            Tick, agent.Id, agent.Hunger, agent.Energy, agent.Food, agent.Money,
            agent.Location, present, agent.MessagesHeard.ToList(), agent.TheftsWitnessed.ToList(),
            Market.FoodStock, Market.FoodPrice, Config.Graph, agent.LastRejection);
    }

    /// <summary>Advances one tick with no decisions (every agent idles).</summary>
    public TickReport Step() => Step([]);

    /// <summary>
    /// Advances exactly one tick. Decisions were made by brains looking at the state of the
    /// previous tick. Phases, in fixed order:
    /// 1. resolve decisions (validate → apply or reject), in a seeded shuffled order;
    /// 2. the hour passes: needs decay, starvation, travel advances;
    /// 3. the market restocks.
    /// </summary>
    public TickReport Step(IReadOnlyList<AgentDecision> decisions)
    {
        Tick++;
        var events = new List<WorldEvent> { new TickStarted(Tick) };

        foreach (var agent in _agents)
        {
            agent.LastRejection = null;
            agent.MessagesHeard.Clear();
            agent.TheftsWitnessed.Clear();
        }

        ResolveDecisions(decisions, events);
        ApplyNeeds(events);
        AdvanceTravel(events);
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

            if (agent.Location is InTransit)
            {
                Reject(agent, decision.Action, RejectionReason.InTransit, events);
                continue;
            }

            var action = decision.Action;
            if (agent.Energy == 0 && action is not Rest)
            {
                // World rule: an exhausted agent can only rest. The proposal is refused and Rest is applied.
                Reject(agent, action, RejectionReason.Exhausted, events);
                action = new Rest();
            }

            var reason = ActionRules.Validate(agent, action, this);
            if (reason is { } r)
                Reject(agent, action, r, events);
            else
                events.AddRange(ActionRules.Apply(Tick, agent, action, this));
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

    private void AdvanceTravel(List<WorldEvent> events)
    {
        foreach (var agent in _agents)
        {
            if (!agent.IsAlive || agent.Location is not InTransit trip)
                continue;

            int remaining = trip.TicksRemaining - 1;
            if (remaining == 0)
            {
                agent.Location = new AtPlace(trip.To);
                events.Add(new Arrived(Tick, agent.Id, trip.To));
            }
            else
            {
                agent.Location = trip with { TicksRemaining = remaining };
                events.Add(new TravelProgressed(Tick, agent.Id, trip.From, trip.To, remaining));
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

    internal Agent? FindAgent(AgentId id) => _agents.Find(a => a.Id == id);

    /// <summary>Living agents standing at a place (not in transit), in id order.</summary>
    internal IEnumerable<Agent> PresentAt(PlaceId place) =>
        _agents.Where(a => a.IsAlive && a.Location is AtPlace at && at.Place == place);

    /// <summary>True if the target exists, is alive, and stands at the same place as the agent.</summary>
    internal bool IsPresentWith(Agent agent, AgentId targetId) =>
        agent.Location is AtPlace here
        && FindAgent(targetId) is { IsAlive: true, Location: AtPlace there }
        && there.Place == here.Place;
}
