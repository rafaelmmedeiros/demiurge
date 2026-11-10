using Demiurge.Engine.Actions;
using Demiurge.Engine.Domain;

namespace Demiurge.Engine.Tests;

/// <summary>
/// Minimal deterministic policy used only by tests to feed decisions into the engine.
/// Not the RuleBasedBrain (that comes with its own concept): just enough to exercise actions and travel.
/// </summary>
internal static class ScriptedPolicy
{
    public static AgentAction? Decide(Perception p)
    {
        if (p.Location is not AtPlace { Place: var here })
            return null; // in transit: no decision

        return p switch
        {
            { Hunger: >= 60, Food: > 0 } => new Eat(),
            { Hunger: >= 60 } => GoOrDo(here, PlacesGraph.Market, new Buy()),
            { Energy: < 20 } => GoOrDo(here, PlacesGraph.Home, new Rest()),
            _ => GoOrDo(here, PlacesGraph.Workplace, new Work()),
        };
    }

    private static AgentAction GoOrDo(PlaceId here, PlaceId target, AgentAction action) =>
        here == target ? action : new MoveTo(target);

    public static List<AgentDecision> DecideAll(World world) =>
        world.Agents
            .Where(a => a.IsAlive)
            .Select(a => (a.Id, Action: Decide(world.Perceive(a.Id))))
            .Where(d => d.Action is not null)
            .Select(d => new AgentDecision(d.Id, d.Action!))
            .ToList();
}
