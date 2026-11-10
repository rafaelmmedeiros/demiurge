namespace Demiurge.Engine.Domain;

/// <summary>
/// An entity living in the world. Setters are internal: only the engine mutates state
/// (CLAUDE.md rule 1). Resources are integers: no floating point in the engine, so results
/// cannot drift between platforms.
/// </summary>
public sealed class Agent
{
    internal Agent(AgentId id, int hunger, int energy, int food, int money)
    {
        Id = id;
        Hunger = hunger;
        Energy = energy;
        Food = food;
        Money = money;
    }

    public AgentId Id { get; }

    /// <summary>0–100. 100 for <see cref="WorldRules.StarvationTicks"/> consecutive ticks kills.</summary>
    public int Hunger { get; internal set; }

    /// <summary>0–100.</summary>
    public int Energy { get; internal set; }

    public int Food { get; internal set; }

    public int Money { get; internal set; }

    public bool IsAlive { get; internal set; } = true;

    /// <summary>Consecutive ticks with Hunger at 100.</summary>
    public int TicksStarving { get; internal set; }

    /// <summary>Reason the agent's proposal was refused in the last tick, if it was. Cleared every tick.</summary>
    public Actions.RejectionReason? LastRejection { get; internal set; }
}
