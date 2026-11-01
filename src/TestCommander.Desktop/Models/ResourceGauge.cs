namespace TestCommander.Models;

public sealed record ResourceGauge(string Name, int? Stock, int? Capacity)
{
    public bool HasCapacity => Stock.HasValue && Capacity is > 0;
    public bool IsOverCapacity => HasCapacity && Stock!.Value > Capacity!.Value;
    public double UtilizationPercent => HasCapacity
        ? Math.Clamp(Stock!.Value / (double)Capacity!.Value * 100d, 0d, 100d)
        : 0d;
    public int? RemainingCapacity => HasCapacity
        ? Math.Max(0, Capacity!.Value - Stock!.Value)
        : null;
    public string StockLabel => Stock?.ToString("N0") ?? "Not detected";
    public string CapacityLabel => Capacity is > 0 ? $"of {Capacity.Value:N0}" : "capacity unknown";
    public string UtilizationLabel => !Stock.HasValue
        ? "Stock not detected"
        : Capacity is not > 0
            ? "Capacity not detected"
            : IsOverCapacity
                ? "Over capacity"
                : $"{UtilizationPercent:0.0}% full";
}