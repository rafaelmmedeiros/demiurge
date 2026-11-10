using Demiurge.Engine.Actions;

namespace Demiurge.Engine.Tests;

/// <summary>
/// Minimal deterministic policy used only by tests to feed decisions into the engine.
/// Not the RuleBasedBrain (that comes with its own concept): just enough to exercise actions.
/// </summary>
internal static class ScriptedPolicy
{
    public static AgentAction Decide(Perception p) => p switch
    {
        { Hunger: >= 60, Food: > 0 } => new Eat(),
        { Hunger: >= 60 } => new Buy(),
        { Energy: < 20 } => new Rest(),
        _ => new Work(),
    };

    public static List<AgentDecision> DecideAll(World world) =>
        world.Agents
            .Where(a => a.IsAlive)
            .Select(a => new AgentDecision(a.Id, Decide(world.Perceive(a.Id))))
            .ToList();
}
