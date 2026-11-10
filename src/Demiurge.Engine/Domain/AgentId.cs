namespace Demiurge.Engine.Domain;

/// <summary>
/// Sequential agent identifier (agent-1, agent-2, ...). Never a Guid: Guid.NewGuid() is not
/// reproducible from a seed. Comparable so the engine can always iterate agents in id order.
/// Concept: Determinism and seed — docs/CONCEPTS.md#determinismo-e-seed
/// </summary>
public readonly record struct AgentId(int Value) : IComparable<AgentId>
{
    public int CompareTo(AgentId other) => Value.CompareTo(other.Value);

    public override string ToString() => $"agent-{Value}";
}
