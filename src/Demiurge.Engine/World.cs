using Demiurge.Engine.Domain;
using Demiurge.Engine.Events;
using Demiurge.Engine.Randomness;

namespace Demiurge.Engine;

/// <summary>
/// The simulated world. Time advances only through <see cref="Step"/>: one call = one tick
/// = one simulated hour. Nothing changes between ticks.
/// Concepts: Tick-based simulation — docs/CONCEPTS.md#simulação-baseada-em-ticks ;
///           Determinism and seed — docs/CONCEPTS.md#determinismo-e-seed
/// </summary>
public sealed class World
{
    private readonly List<Agent> _agents;  // always ordered by AgentId: iteration order is part of determinism
    private readonly IRandom _rng;

    private World(WorldConfig config, IRandom rng, List<Agent> agents)
    {
        Config = config;
        _rng = rng;
        _agents = agents;
    }

    public WorldConfig Config { get; }

    /// <summary>Current tick. 0 = genesis, before any step.</summary>
    public long Tick { get; private set; }

    public IReadOnlyList<Agent> Agents => _agents;

    /// <summary>RNG state; goes into snapshots so a restored world continues the same random sequence.</summary>
    public ulong RngState => _rng.State;

    /// <summary>Creates a world from its config. Returns the genesis report (tick 0) with the spawn events.</summary>
    public static (World World, TickReport Genesis) Create(WorldConfig config)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(config.AgentCount);

        var rng = new SplitMix64(config.Seed);
        var agents = new List<Agent>(config.AgentCount);
        var events = new List<WorldEvent>(config.AgentCount);

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

        return (new World(config, rng, agents), new TickReport(0, events));
    }

    /// <summary>
    /// Advances exactly one tick. Phase order is fixed; today only the passive needs phase exists.
    /// Next phases (perception, decisions, validate-and-apply in seeded order) plug in here.
    /// </summary>
    public TickReport Step()
    {
        Tick++;
        var events = new List<WorldEvent> { new TickStarted(Tick) };

        ApplyNeeds(events);

        return new TickReport(Tick, events);
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
}
