using TestCommander.Models;

namespace TestCommander.Services;

/// <summary>
/// Builds compact visual trends from observed snapshots. Missing values remain visible as gaps;
/// no production rate or future stock estimate is inferred.
/// </summary>
public static class ResourceTrendCalculator
{
    private const string Levels = "▁▂▃▄▅▆▇█";

    public static IReadOnlyList<ResourceTrend> Create(IReadOnlyList<VillageSnapshot> history)
    {
        ArgumentNullException.ThrowIfNull(history);
        if (history.Count == 0) return Array.Empty<ResourceTrend>();

        return
        [
            Build("Wood", history.Select(snapshot => snapshot.Wood).ToArray()),
            Build("Clay", history.Select(snapshot => snapshot.Clay).ToArray()),
            Build("Iron", history.Select(snapshot => snapshot.Iron).ToArray()),
            Build("Crop", history.Select(snapshot => snapshot.Crop).ToArray())
        ];
    }

    private static ResourceTrend Build(string name, IReadOnlyList<int?> samples)
    {
        var known = samples.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        if (known.Length == 0)
            return new ResourceTrend(name, "—", 0, "No values observed", "Waiting for data");

        var minimum = known.Min();
        var maximum = known.Max();
        var sparkline = samples.Count < 2
            ? "·"
            : string.Concat(samples.Select(value => ToGlyph(value, minimum, maximum)));

        var range = minimum == maximum
            ? $"Stable at {minimum:N0}"
            : $"{minimum:N0}–{maximum:N0}";

        var first = samples.FirstOrDefault(value => value.HasValue);
        var last = samples.LastOrDefault(value => value.HasValue);
        var delta = last!.Value - first!.Value;
        var change = samples.Count < 2 || known.Length < 2
            ? "Need another sample"
            : $"Δ {delta:+#,0;-#,0;0} across observed samples";

        return new ResourceTrend(name, sparkline, known.Length, range, change);
    }

    private static char ToGlyph(int? value, int minimum, int maximum)
    {
        if (!value.HasValue) return '·';
        if (maximum == minimum) return Levels[3];

        var scaled = (value.Value - minimum) / (double)(maximum - minimum);
        var index = (int)Math.Round(scaled * (Levels.Length - 1), MidpointRounding.AwayFromZero);
        return Levels[Math.Clamp(index, 0, Levels.Length - 1)];
    }
}