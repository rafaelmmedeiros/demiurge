namespace Demiurge.Engine.Domain;

/// <summary>Fixed MVP world rules (docs/context/world.md).</summary>
public static class WorldRules
{
    public const int MaxNeed = 100;
    public const int HungerPerTick = 4;
    public const int EnergyPerTick = 2;
    public const int StarvationTicks = 12;

    public const int WagePerWork = 10;
    public const int WorkEnergyCost = 8;
    public const int MealHungerRelief = 30;
    public const int RestEnergyGainAtHome = 10;
    public const int RestEnergyGainElsewhere = 5;

    public const int MaxMessageLength = 200;
}

/// <summary>
/// Everything needed to create a world. Same config = same world (the seed drives all randomness).
/// Initial ranges are [min, max) and drawn from the seeded RNG.
/// </summary>
public sealed record WorldConfig
{
    public ulong Seed { get; init; } = 42;
    public int AgentCount { get; init; } = 5;

    public IntRange InitialHunger { get; init; } = (0, 31);
    public IntRange InitialEnergy { get; init; } = (70, 101);
    public IntRange InitialFood { get; init; } = (0, 4);
    public IntRange InitialMoney { get; init; } = (20, 51);

    public int FoodPrice { get; init; } = 5;
    public int InitialMarketStock { get; init; } = 20;
    public int MarketRestockPerTick { get; init; } = 1;
    public int MarketCapacity { get; init; } = 30;

    public PlacesGraph Graph { get; init; } = PlacesGraph.Default;
    public PlaceId StartPlace { get; init; } = PlacesGraph.Home;

    /// <summary>Chance, per agent present (victim included), of seeing a theft.</summary>
    public int TheftWitnessChancePercent { get; init; } = 50;
}

/// <summary>Integer range [Min, Max). Implicitly built from a tuple: <c>InitialFood = (0, 4)</c>.</summary>
public readonly record struct IntRange(int Min, int Max)
{
    public static implicit operator IntRange((int Min, int Max) t) => new(t.Min, t.Max);
}
