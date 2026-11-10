using System.Text.Json;
using Demiurge.Engine;
using Demiurge.Engine.Domain;
using Demiurge.Engine.Events;
using Demiurge.Engine.Tests;
using Demiurge.Harness.Persistence;

namespace Demiurge.Harness.Tests;

public class EventJsonTests
{
    private static (World World, List<WorldEvent> Events) Run(int ticks)
    {
        var (world, genesis) = World.Create(new WorldConfig { Seed = 42 });
        var events = new List<WorldEvent>(genesis.Events);
        for (int i = 0; i < ticks; i++)
            events.AddRange(world.Step(ScriptedPolicy.DecideAll(world)).Events);
        return (world, events);
    }

    [Fact]
    public void Every_event_survives_a_json_round_trip()
    {
        var (_, events) = Run(500); // covers every event type (see EventSourcingTests)

        Assert.All(events, e => Assert.Equal(e, EventJson.DeserializeEvent(EventJson.Serialize(e))));
    }

    [Fact]
    public void Payload_carries_a_type_discriminator()
    {
        var json = EventJson.Serialize(new Worked(1, new AgentId(5), 10, 48, 74));

        Assert.Equal("""{"$type":"Worked","agentId":5,"earned":10,"money":48,"energy":74,"tick":1}""", json);
    }

    [Fact]
    public void Snapshot_survives_a_json_round_trip()
    {
        var (world, _) = Run(300);
        var snapshot = world.ToSnapshot();

        var back = JsonSerializer.Deserialize<WorldSnapshot>(JsonSerializer.Serialize(snapshot, EventJson.Options), EventJson.Options);

        Assert.Equivalent(snapshot, back, strict: true);
    }

    [Fact]
    public void Config_survives_a_json_round_trip()
    {
        var config = new WorldConfig { Seed = ulong.MaxValue, AgentCount = 7, InitialFood = (1, 2) };

        var json = JsonSerializer.Serialize(config, EventJson.Options);
        var back = JsonSerializer.Deserialize<WorldConfig>(json, EventJson.Options)!;

        Assert.Equal(json, JsonSerializer.Serialize(back, EventJson.Options));
        Assert.Equal(2, back.Graph.TravelTicks(PlacesGraph.Home, PlacesGraph.Workplace));
    }
}
