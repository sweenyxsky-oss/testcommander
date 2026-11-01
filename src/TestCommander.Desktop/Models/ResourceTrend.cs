namespace TestCommander.Models;

public sealed record ResourceTrend(
    string Name,
    string Sparkline,
    int SampleCount,
    string RangeLabel,
    string ChangeLabel);