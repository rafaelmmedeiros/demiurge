namespace Demiurge.Engine.Domain;

/// <summary>
/// The single food market of the MVP. Restocks a fixed amount per tick up to its capacity.
/// Money paid by buyers accumulates here (a sink for now; an economy can reuse it later).
/// </summary>
public sealed class Market
{
    internal Market(int foodStock, int foodPrice)
    {
        FoodStock = foodStock;
        FoodPrice = foodPrice;
    }

    public int FoodStock { get; internal set; }

    public int FoodPrice { get; internal set; }

    public int Money { get; internal set; }
}
