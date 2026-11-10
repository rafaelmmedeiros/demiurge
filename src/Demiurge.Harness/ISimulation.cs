using Demiurge.Engine;
using Demiurge.Engine.Actions;
using Demiurge.Engine.Domain;
using Demiurge.Engine.Events;

namespace Demiurge.Harness;

/// <summary>
/// A world that can be stepped: persisted (<see cref="SimulationSession"/>) or in memory
/// (<see cref="InMemorySimulation"/>, used by the probe tool and tests). TickRunner works with either.
/// </summary>
public interface ISimulation
{
    World World { get; }

    TickReport Step(IReadOnlyList<AgentDecision> decisions);
}

/// <summary>A world with no persistence: same engine, no SQLite. For batch measurements.</summary>
public sealed class InMemorySimulation : ISimulation
{
    private InMemorySimulation(World world) => World = world;

    public World World { get; }

    public static InMemorySimulation Start(WorldConfig config) => new(World.Create(config).World);

    public TickReport Step(IReadOnlyList<AgentDecision> decisions) => World.Step(decisions);
}
