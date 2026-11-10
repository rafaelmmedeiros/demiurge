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
    public const int RestEnergyGain = 10;
}

/// <summary>
/// Everything needed to create a world. Same config = same world (the seed drives all randomness).
/// Initial ranges are [min, max) and drawn from the seeded RNG.
/// </summary>
public sealed record WorldConfig
{
    public ulong Seed { get; init; } = 42;
    public int AgentCount { get; init; } = 5;

    public (int Min, int Max) InitialHunger { get; init; } = (0, 31);
    public (int Min, int Max) InitialEnergy { get; init; } = (70, 101);
    public (int Min, int Max) InitialFood { get; init; } = (0, 4);
    public (int Min, int Max) InitialMoney { get; init; } = (20, 51);

    public int FoodPrice { get; init; } = 5;
    public int InitialMarketStock { get; init; } = 20;
    public int MarketRestockPerTick { get; init; } = 1;
    public int MarketCapacity { get; init; } = 30;
}
