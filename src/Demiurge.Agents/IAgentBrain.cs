using Demiurge.Engine;
using Demiurge.Engine.Actions;

namespace Demiurge.Agents;

/// <summary>
/// The agent program: maps what the agent perceives to the action it proposes.
/// Sensor = <see cref="Perception"/> (immutable copy from World.Perceive); actuator = the returned
/// <see cref="AgentAction"/>, which the engine validates and applies or rejects.
///
/// Returns an action, not an AgentDecision: the harness binds it to <c>perception.Self</c>,
/// so a brain cannot act on behalf of another agent — guaranteed by the type.
/// Async because an LLM waits on the network (0.5–5 s); the token is how the tick cuts a slow brain.
/// Concept: Agent and the perception → decision → action loop —
///          docs/CONCEPTS.md#agente-e-o-laço-percepção--decisão--ação
/// </summary>
public interface IAgentBrain
{
    Task<AgentAction> DecideAsync(Perception perception, CancellationToken ct);
}
