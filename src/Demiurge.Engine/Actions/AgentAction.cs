using Demiurge.Engine.Domain;

namespace Demiurge.Engine.Actions;

/// <summary>
/// The closed set of actions an agent may PROPOSE. A brain (rules or LLM) can only return one of
/// these types; it never touches state. With an LLM this becomes the JSON schema of the tool call.
/// Concept: Authoritative engine — docs/CONCEPTS.md#engine-autoritativo
/// </summary>
public abstract record AgentAction;

public sealed record Rest : AgentAction;

public sealed record Eat : AgentAction;

public sealed record Work : AgentAction;

public sealed record Buy : AgentAction;

public sealed record MoveTo(PlaceId Destination) : AgentAction;

public sealed record Talk(AgentId Target, string Text) : AgentAction;

public sealed record Steal(AgentId Target) : AgentAction;

/// <summary>What a brain returns: who proposes and what.</summary>
public sealed record AgentDecision(AgentId AgentId, AgentAction Action);

/// <summary>Why the engine refused a proposal. Fed back into the agent's next perception.</summary>
public enum RejectionReason
{
    UnknownAgent,
    AgentNotAlive,
    DuplicateDecision,
    InTransit,
    Exhausted,
    TooTired,
    NoFood,
    InsufficientMoney,
    OutOfStock,
    NotAtRequiredPlace,
    UnknownPlace,
    AlreadyThere,
    InvalidTarget,
    TargetNotPresent,
    EmptyMessage,
    MessageTooLong,
    NothingToSteal,
}
