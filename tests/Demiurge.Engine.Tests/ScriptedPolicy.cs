using Demiurge.Engine.Actions;
using Demiurge.Engine.Domain;

namespace Demiurge.Engine.Tests;

/// <summary>
/// Minimal deterministic policy used only by tests to feed decisions into the engine.
/// Not the RuleBasedBrain (that comes with its own concept): just enough to exercise every action,
/// so long runs produce every event type (travel, talk, theft, witnesses, rejections, deaths).
/// </summary>
internal static class ScriptedPolicy
{
    private static readonly AgentId Thief = new(3);

    public static AgentAction? Decide(Perception p)
    {
        if (p.Location is not AtPlace { Place: var here })
            return null; // in transit: no decision

        if (p.Self == Thief && p.Food == 0 && p.PeoplePresent.Count > 0)
            return new Steal(p.PeoplePresent[0]);
        if (p.Tick % 10 == 0 && p.PeoplePresent.Count > 0)
            return new Talk(p.PeoplePresent[0], $"tick {p.Tick}: tudo bem?");

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
