using TestCommander.Models;
using TestCommander.Services;
using Xunit;

namespace TestCommander.Tests;

public sealed class ResourceTrendCalculatorTests
{
    [Fact]
    public void BuildsSeparateTrendForEachResource()
    {
        var history = new[]
        {
            Snapshot(wood: 100, clay: 300, iron: 500, crop: 700),
            Snapshot(wood: 200, clay: 200, iron: 500, crop: 900),
            Snapshot(wood: 300, clay: 100, iron: 500, crop: 800)
        };

        var trends = ResourceTrendCalculator.Create(history);

        Assert.Equal(new[] { "Wood", "Clay", "Iron", "Crop" }, trends.Select(trend => trend.Name));
        Assert.Equal("100–300", trends[0].RangeLabel);
        Assert.Contains("Δ +200", trends[0].ChangeLabel);
        Assert.Contains("Δ -200", trends[1].ChangeLabel);
        Assert.Equal("Stable at 500", trends[2].RangeLabel);
        Assert.Equal(3, trends[3].SampleCount);
    }

    [Fact]
    public void PreservesMissingSamplesAsVisibleGaps()
    {
        var history = new[]
        {
            Snapshot(wood: 100, clay: null, iron: 10, crop: 100),
            Snapshot(wood: null, clay: 200, iron: null, crop: 200),
            Snapshot(wood: 300, clay: 300, iron: 30, crop: null)
        };

        var trends = ResourceTrendCalculator.Create(history);

        Assert.Contains('·', trends.Single(trend => trend.Name == "Wood").Sparkline);
        Assert.Equal(2, trends.Single(trend => trend.Name == "Clay").SampleCount);
        Assert.Equal(2, trends.Single(trend => trend.Name == "Iron").SampleCount);
    }

    [Fact]
    public void DoesNotInventChangeWithOnlyOneKnownSample()
    {
        var trends = ResourceTrendCalculator.Create(new[]
        {
            Snapshot(wood: 100, clay: null, iron: null, crop: null),
            Snapshot(wood: null, clay: null, iron: null, crop: null)
        });

        var wood = trends.Single(trend => trend.Name == "Wood");
        Assert.Equal("Need another sample", wood.ChangeLabel);
        Assert.Equal(1, wood.SampleCount);
    }

    [Fact]
    public void HandlesEmptyHistory()
    {
        Assert.Empty(ResourceTrendCalculator.Create(Array.Empty<VillageSnapshot>()));
    }

    private static VillageSnapshot Snapshot(int? wood, int? clay, int? iron, int? crop) =>
        new("Test village", wood, clay, iron, crop, 1000, 1000, DateTimeOffset.UtcNow);
}