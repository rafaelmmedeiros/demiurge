namespace Demiurge.Engine.Domain;

/// <summary>Place identifier. A readable string ("home", "market") so events are self-explanatory.</summary>
public readonly record struct PlaceId(string Value)
{
    public override string ToString() => Value;
}

/// <summary>What a place is for. Action requirements depend on the kind, not on a specific id.</summary>
public enum PlaceKind
{
    Home,
    Workplace,
    Market,
    Square,
}

/// <summary>A node of the places graph. X/Y are display-only (dashboard canvas); the engine never reads them.</summary>
public sealed record Place(PlaceId Id, PlaceKind Kind, int X, int Y);

/// <summary>An undirected edge: travelling between A and B takes <see cref="Ticks"/> ticks.</summary>
public sealed record Edge(PlaceId A, PlaceId B, int Ticks);

/// <summary>
/// The world's space: places connected by edges with a travel cost in ticks. Immutable.
/// MVP rule: the graph is complete (every pair has a direct edge), so no pathfinding is needed.
/// Concept: Places graph — docs/CONCEPTS.md#grafo-de-lugares ; decision: docs/DECISIONS.md ADR-001
/// </summary>
public sealed class PlacesGraph
{
    public static readonly PlaceId Home = new("home");
    public static readonly PlaceId Workplace = new("workplace");
    public static readonly PlaceId Market = new("market");
    public static readonly PlaceId Square = new("square");

    /// <summary>Default MVP map: Home–Workplace and Market–Workplace cost 2 ticks; every other pair costs 1.</summary>
    public static PlacesGraph Default { get; } = new(
        [
            new Place(Home, PlaceKind.Home, 20, 20),
            new Place(Market, PlaceKind.Market, 80, 20),
            new Place(Square, PlaceKind.Square, 50, 50),
            new Place(Workplace, PlaceKind.Workplace, 50, 85),
        ],
        [
            new Edge(Home, Workplace, 2),
            new Edge(Home, Market, 1),
            new Edge(Home, Square, 1),
            new Edge(Workplace, Market, 2),
            new Edge(Workplace, Square, 1),
            new Edge(Market, Square, 1),
        ]);

    private readonly Dictionary<PlaceId, Place> _byId;              // lookups only, never iterated
    private readonly Dictionary<(PlaceId, PlaceId), int> _travel;   // key normalized: (smaller, larger) by ordinal

    public PlacesGraph(IEnumerable<Place> places, IEnumerable<Edge> edges)
    {
        // Stable, culture-independent order: iteration over Places is part of determinism.
        Places = places.OrderBy(p => p.Id.Value, StringComparer.Ordinal).ToList();
        _byId = Places.ToDictionary(p => p.Id);
        _travel = [];

        foreach (var e in edges)
        {
            if (!_byId.ContainsKey(e.A) || !_byId.ContainsKey(e.B))
                throw new ArgumentException($"Edge {e} references an unknown place.");
            if (e.A == e.B || e.Ticks < 1)
                throw new ArgumentException($"Edge {e} must join two different places with at least 1 tick.");
            if (!_travel.TryAdd(Key(e.A, e.B), e.Ticks))
                throw new ArgumentException($"Duplicate edge {e}.");
        }

        for (int i = 0; i < Places.Count; i++)
            for (int j = i + 1; j < Places.Count; j++)
                if (!_travel.ContainsKey(Key(Places[i].Id, Places[j].Id)))
                    throw new ArgumentException($"Graph must be complete: missing edge {Places[i].Id}–{Places[j].Id}.");
    }

    public IReadOnlyList<Place> Places { get; }

    public Place? Find(PlaceId id) => _byId.GetValueOrDefault(id);

    public int TravelTicks(PlaceId a, PlaceId b) => _travel[Key(a, b)];

    private static (PlaceId, PlaceId) Key(PlaceId a, PlaceId b) =>
        string.CompareOrdinal(a.Value, b.Value) <= 0 ? (a, b) : (b, a);
}

/// <summary>Where an agent is: at a place, or on an edge with ticks remaining.</summary>
public abstract record Location;

public sealed record AtPlace(PlaceId Place) : Location;

public sealed record InTransit(PlaceId From, PlaceId To, int TicksRemaining) : Location;
