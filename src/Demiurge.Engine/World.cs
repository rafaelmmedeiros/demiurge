using Demiurge.Engine.Actions;
using Demiurge.Engine.Domain;
using Demiurge.Engine.Events;
using Demiurge.Engine.Randomness;

namespace Demiurge.Engine;

/// <summary>
/// The simulated world and the only authority over its state. Time advances only through
/// <see cref="Step"/>: one call = one tick = one simulated hour. Nothing changes between ticks.
/// State changes ONLY in <see cref="Apply"/>: every rule computes events, and <see cref="Emit"/>
/// applies and records them. The live path and the rebuild path run the exact same code.
/// Concepts: Tick-based simulation — docs/CONCEPTS.md#simulação-baseada-em-ticks ;
///           Determinism and seed — docs/CONCEPTS.md#determinismo-e-seed ;
///           Authoritative engine — docs/CONCEPTS.md#engine-autoritativo ;
///           Places graph — docs/CONCEPTS.md#grafo-de-lugares ;
///           Event sourcing — docs/CONCEPTS.md#event-sourcing
/// </summary>
public sealed class World
{
    private readonly List<Agent> _agents = [];  // always ordered by AgentId: iteration order is part of determinism
    private IRandom _rng;

    private World(WorldConfig config)
    {
        Config = config;
        _rng = new SplitMix64(config.Seed);
        Market = new Market(0, config.FoodPrice);
    }

    public WorldConfig Config { get; }

    /// <summary>Current tick. 0 = genesis, before any step.</summary>
    public long Tick { get; private set; }

    public IReadOnlyList<Agent> Agents => _agents;

    public Market Market { get; }

    /// <summary>RNG state; recorded in every TickEnded event and in snapshots.</summary>
    public ulong RngState => _rng.State;

    internal IRandom Rng => _rng;

    // ------------------------------------------------------------------ creation and rebuild

    /// <summary>Creates a world from its config. Returns the genesis report (tick 0).</summary>
    public static (World World, TickReport Genesis) Create(WorldConfig config)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(config.AgentCount);
        if (config.Graph.Find(config.StartPlace) is null)
            throw new ArgumentException($"Start place {config.StartPlace} is not in the graph.", nameof(config));

        var world = new World(config);
        var events = new List<WorldEvent>(config.AgentCount + 2);

        for (int i = 1; i <= config.AgentCount; i++)
        {
            world.Emit(new AgentSpawned(
                0,
                new AgentId(i),
                Hunger: world._rng.NextInt(config.InitialHunger.Min, config.InitialHunger.Max),
                Energy: world._rng.NextInt(config.InitialEnergy.Min, config.InitialEnergy.Max),
                Food: world._rng.NextInt(config.InitialFood.Min, config.InitialFood.Max),
                Money: world._rng.NextInt(config.InitialMoney.Min, config.InitialMoney.Max),
                Place: config.StartPlace), events);
        }

        world.Emit(new MarketOpened(0, config.InitialMarketStock, config.FoodPrice), events);
        world.Emit(new TickEnded(0, world._rng.State), events);

        return (world, new TickReport(0, events));
    }

    /// <summary>Rebuilds a world by applying its full event stream from genesis.</summary>
    public static World FromEvents(WorldConfig config, IEnumerable<WorldEvent> events)
    {
        var world = new World(config);
        foreach (var e in events)
            world.Apply(e);
        return world;
    }

    /// <summary>Rebuilds a world from a snapshot plus the events recorded after it.</summary>
    public static World FromSnapshot(WorldConfig config, WorldSnapshot snapshot, IEnumerable<WorldEvent> eventsAfter)
    {
        var world = new World(config)
        {
            Tick = snapshot.Tick,
            _rng = new SplitMix64(snapshot.RngState),
        };

        foreach (var s in snapshot.Agents.OrderBy(a => a.Id))
        {
            var agent = new Agent(s.Id, s.Hunger, s.Energy, s.Food, s.Money, s.Location)
            {
                IsAlive = s.IsAlive,
                TicksStarving = s.TicksStarving,
                LastRejection = s.LastRejection,
            };
            agent.MessagesHeard.AddRange(s.MessagesHeard);
            agent.TheftsWitnessed.AddRange(s.TheftsWitnessed);
            world._agents.Add(agent);
        }

        world.Market.FoodStock = snapshot.Market.FoodStock;
        world.Market.FoodPrice = snapshot.Market.FoodPrice;
        world.Market.Money = snapshot.Market.Money;

        foreach (var e in eventsAfter)
            world.Apply(e);
        return world;
    }

    public WorldSnapshot ToSnapshot() => new(
        Tick,
        _rng.State,
        _agents.Select(a => new AgentSnapshot(
            a.Id, a.Hunger, a.Energy, a.Food, a.Money, a.IsAlive, a.TicksStarving, a.Location,
            a.LastRejection, a.MessagesHeard.ToList(), a.TheftsWitnessed.ToList())).ToList(),
        new MarketSnapshot(Market.FoodStock, Market.FoodPrice, Market.Money));

    // ------------------------------------------------------------------ perception

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

    // ------------------------------------------------------------------ tick

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
        var events = new List<WorldEvent>();

        Emit(new TickStarted(Tick + 1), events);
        ResolveDecisions(decisions, events);
        ApplyNeeds(events);
        AdvanceTravel(events);
        RestockMarket(events);
        Emit(new TickEnded(Tick, _rng.State), events);

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
            var action = decision.Action;

            RejectionReason? reason =
                agent is null ? RejectionReason.UnknownAgent
                : !agent.IsAlive ? RejectionReason.AgentNotAlive
                : duplicated.Contains(agent.Id) ? RejectionReason.DuplicateDecision
                : agent.Location is InTransit ? RejectionReason.InTransit
                : null;

            if (reason is null && agent!.Energy == 0 && action is not Rest)
            {
                // World rule: an exhausted agent can only rest. The proposal is refused and Rest is applied.
                Emit(new ActionRejected(Tick, agent.Id, action, RejectionReason.Exhausted), events);
                action = new Rest();
            }

            reason ??= ActionRules.Validate(agent!, action, this);

            if (reason is { } r)
                Emit(new ActionRejected(Tick, decision.AgentId, action, r), events);
            else
                foreach (var e in ActionRules.Resolve(Tick, agent!, action, this))
                    Emit(e, events);
        }
    }

    private void ApplyNeeds(List<WorldEvent> events)
    {
        foreach (var agent in _agents.Where(a => a.IsAlive).ToList())
        {
            int hunger = Math.Min(WorldRules.MaxNeed, agent.Hunger + WorldRules.HungerPerTick);
            int energy = Math.Max(0, agent.Energy - WorldRules.EnergyPerTick);
            int starving = hunger >= WorldRules.MaxNeed ? agent.TicksStarving + 1 : 0;

            Emit(new NeedsUpdated(Tick, agent.Id, hunger, energy, starving), events);
            if (starving >= WorldRules.StarvationTicks)
                Emit(new AgentDied(Tick, agent.Id, DeathCause.Starvation), events);
        }
    }

    private void AdvanceTravel(List<WorldEvent> events)
    {
        foreach (var agent in _agents.Where(a => a.IsAlive).ToList())
        {
            if (agent.Location is not InTransit trip)
                continue;

            int remaining = trip.TicksRemaining - 1;
            Emit(remaining == 0
                ? new Arrived(Tick, agent.Id, trip.To)
                : new TravelProgressed(Tick, agent.Id, trip.From, trip.To, remaining), events);
        }
    }

    private void RestockMarket(List<WorldEvent> events)
    {
        int restocked = Math.Min(Config.MarketCapacity, Market.FoodStock + Config.MarketRestockPerTick);
        if (restocked != Market.FoodStock)
            Emit(new MarketRestocked(Tick, restocked), events);
    }

    // ------------------------------------------------------------------ event sourcing core

    /// <summary>Applies an event to the state and records it. The only way the live world changes.</summary>
    private void Emit(WorldEvent e, List<WorldEvent> sink)
    {
        Apply(e);
        sink.Add(e);
    }

    /// <summary>
    /// Applies one event to the state. Pure assignment from the event's values: no rule runs here,
    /// so replaying old events gives the old world even if the rules change later.
    /// </summary>
    private void Apply(WorldEvent e)
    {
        switch (e)
        {
            case AgentSpawned s:
                _agents.Add(new Agent(s.AgentId, s.Hunger, s.Energy, s.Food, s.Money, new AtPlace(s.Place)));
                break;
            case MarketOpened m:
                Market.FoodStock = m.FoodStock;
                Market.FoodPrice = m.FoodPrice;
                break;
            case TickStarted t:
                Tick = t.Tick;
                foreach (var a in _agents)
                {
                    a.LastRejection = null;
                    a.MessagesHeard.Clear();
                    a.TheftsWitnessed.Clear();
                }
                break;
            case TickEnded t:
                _rng = new SplitMix64(t.RngState);
                break;
            case NeedsUpdated n:
                var needy = Get(n.AgentId);
                needy.Hunger = n.Hunger;
                needy.Energy = n.Energy;
                needy.TicksStarving = n.TicksStarving;
                break;
            case AgentDied d:
                Get(d.AgentId).IsAlive = false;
                break;
            case MarketRestocked r:
                Market.FoodStock = r.FoodStock;
                break;
            case TravelStarted ts:
                Get(ts.AgentId).Location = new InTransit(ts.From, ts.To, ts.Ticks);
                break;
            case TravelProgressed tp:
                Get(tp.AgentId).Location = new InTransit(tp.From, tp.To, tp.TicksRemaining);
                break;
            case Arrived ar:
                Get(ar.AgentId).Location = new AtPlace(ar.Place);
                break;
            case Rested r:
                Get(r.AgentId).Energy = r.Energy;
                break;
            case Ate a:
                var eater = Get(a.AgentId);
                eater.Hunger = a.Hunger;
                eater.Food = a.FoodLeft;
                break;
            case Worked w:
                var worker = Get(w.AgentId);
                worker.Money = w.Money;
                worker.Energy = w.Energy;
                break;
            case FoodBought f:
                var buyer = Get(f.AgentId);
                buyer.Money = f.Money;
                buyer.Food = f.Food;
                Market.FoodStock = f.MarketFoodStock;
                Market.Money = f.MarketMoney;
                break;
            case Talked t:
                Get(t.Listener).MessagesHeard.Add(new HeardMessage(t.Speaker, t.Text));
                break;
            case Stole s:
                Get(s.Thief).Food = s.ThiefFood;
                Get(s.Victim).Food = s.VictimFood;
                break;
            case TheftWitnessed w:
                Get(w.Witness).TheftsWitnessed.Add(new WitnessedTheft(w.Thief, w.Victim));
                break;
            case ActionRejected r:
                if (FindAgent(r.AgentId) is { } rejected)
                    rejected.LastRejection = r.Reason;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(e), e, "Unknown event type.");
        }
    }

    // ------------------------------------------------------------------ queries

    internal Agent? FindAgent(AgentId id) => _agents.Find(a => a.Id == id);

    private Agent Get(AgentId id) =>
        FindAgent(id) ?? throw new InvalidOperationException($"Event references unknown agent {id}.");

    /// <summary>Living agents standing at a place (not in transit), in id order.</summary>
    internal IEnumerable<Agent> PresentAt(PlaceId place) =>
        _agents.Where(a => a.IsAlive && a.Location is AtPlace at && at.Place == place);

    /// <summary>True if the target exists, is alive, and stands at the same place as the agent.</summary>
    internal bool IsPresentWith(Agent agent, AgentId targetId) =>
        agent.Location is AtPlace here
        && FindAgent(targetId) is { IsAlive: true, Location: AtPlace there }
        && there.Place == here.Place;
}
