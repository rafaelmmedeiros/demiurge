using System.Text.Json;
using Demiurge.Engine;
using Demiurge.Engine.Domain;
using Demiurge.Engine.Events;
using Demiurge.Harness.Pains;
using Demiurge.Harness.Persistence;

namespace Demiurge.Host.Simulation;

/// <summary>
/// Immutable read model of the world for the API and the dashboard, published after every tick.
/// Readers take the latest reference without locks; only the simulation loop builds new ones.
/// Concept: Dashboard and state streaming — docs/CONCEPTS.md#dashboard-e-streaming-de-estado
/// </summary>
public sealed record WorldView(
    long WorldId,
    string Seed,                 // string: a ulong does not fit a JavaScript number
    long Tick,
    bool Paused,
    int TickIntervalMs,
    IReadOnlyList<PlaceView> Places,
    IReadOnlyList<EdgeView> Edges,
    IReadOnlyList<AgentView> Agents,
    MarketView Market,
    IReadOnlyList<Pain> Pains)
{
    public static WorldView From(long worldId, World world, bool paused, int tickIntervalMs, IReadOnlyList<Pain> pains)
    {
        var graph = world.Config.Graph;
        return new WorldView(
            worldId,
            world.Config.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture),
            world.Tick,
            paused,
            tickIntervalMs,
            graph.Places.Select(p => new PlaceView(p.Id.Value, p.Kind.ToString(), p.X, p.Y)).ToList(),
            graph.Edges.Select(e => new EdgeView(e.A.Value, e.B.Value, e.Ticks)).ToList(),
            world.Agents.Select(a => a.Location switch
            {
                AtPlace at => new AgentView(a.Id.Value, a.IsAlive, a.Hunger, a.Energy, a.Food, a.Money, at.Place.Value, null, null, 0, 0),
                InTransit t => new AgentView(a.Id.Value, a.IsAlive, a.Hunger, a.Energy, a.Food, a.Money, null, t.From.Value, t.To.Value,
                    t.TicksRemaining, graph.TravelTicks(t.From, t.To)),
                _ => throw new InvalidOperationException("Unknown location."),
            }).ToList(),
            new MarketView(world.Market.FoodStock, world.Market.FoodPrice),
            pains);
    }
}

public sealed record PlaceView(string Id, string Kind, int X, int Y);

public sealed record EdgeView(string A, string B, int Ticks);

/// <summary>Place is set when the agent stands somewhere; From/To/TicksRemaining/TravelTicks when in transit.</summary>
public sealed record AgentView(
    int Id, bool Alive, int Hunger, int Energy, int Food, int Money,
    string? Place, string? From, string? To, int TicksRemaining, int TravelTicks);

public sealed record MarketView(int FoodStock, int FoodPrice);

/// <summary>What the hub pushes every tick: the new view plus that tick's events (same JSON as the store, with "$type").</summary>
public sealed record TickMessage(WorldView World, IReadOnlyList<JsonElement> Events);

public static class EventViews
{
    public static List<JsonElement> ToJson(IEnumerable<WorldEvent> events) =>
        events.Select(e => JsonSerializer.SerializeToElement(e, EventJson.Options)).ToList();
}
