using Demiurge.Engine.Domain;
using Demiurge.Engine.Events;

namespace Demiurge.Engine.Tests;

/// <summary>
/// State is a function of the event stream (CLAUDE.md rule 3).
/// Concept: Event sourcing — docs/CONCEPTS.md#event-sourcing
/// </summary>
public class EventSourcingTests
{
    private static readonly WorldConfig Config = new() { Seed = 42 };

    private static (World World, List<WorldEvent> Events) Run(int ticks)
    {
        var (world, genesis) = World.Create(Config);
        var events = new List<WorldEvent>(genesis.Events);
        for (int i = 0; i < ticks; i++)
            events.AddRange(world.Step(ScriptedPolicy.DecideAll(world)).Events);
        return (world, events);
    }

    [Fact]
    public void Scenario_covers_every_event_type()
    {
        var (_, events) = Run(500);
        var produced = events.Select(e => e.GetType()).ToHashSet();
        var all = typeof(WorldEvent).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(WorldEvent)) && !t.IsAbstract);

        // If a new event type is added and never produced here, the rebuild tests below would not cover it.
        Assert.All(all, t => Assert.Contains(t, produced));
    }

    [Fact]
    public void Rebuilding_from_all_events_gives_the_live_state()
    {
        var (live, events) = Run(500);

        var rebuilt = World.FromEvents(Config, events);

        Assert.Equivalent(live.ToSnapshot(), rebuilt.ToSnapshot(), strict: true);
    }

    [Fact]
    public void Rebuilding_from_snapshot_plus_later_events_gives_the_live_state()
    {
        var (world, genesis) = World.Create(Config);
        var later = new List<WorldEvent>();
        WorldSnapshot? snapshot = null;
        for (int i = 1; i <= 500; i++)
        {
            var report = world.Step(ScriptedPolicy.DecideAll(world));
            if (i == 200) snapshot = world.ToSnapshot();
            else if (i > 200) later.AddRange(report.Events);
        }

        var rebuilt = World.FromSnapshot(Config, snapshot!, later);

        Assert.Equivalent(world.ToSnapshot(), rebuilt.ToSnapshot(), strict: true);
    }

    [Fact]
    public void Rebuilt_world_continues_exactly_like_the_original()
    {
        // The RNG state recorded in TickEnded makes the future identical, not just the present.
        var (original, events) = Run(250);
        var rebuilt = World.FromEvents(Config, events);

        for (int i = 0; i < 250; i++)
        {
            var a = original.Step(ScriptedPolicy.DecideAll(original)).Events.Select(e => e.ToString());
            var b = rebuilt.Step(ScriptedPolicy.DecideAll(rebuilt)).Events.Select(e => e.ToString());
            Assert.Equal(a, b);
        }
    }
}
