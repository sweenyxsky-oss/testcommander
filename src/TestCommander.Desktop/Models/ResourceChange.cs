namespace TestCommander.Models;

public sealed record ResourceChange(string Name, int Delta)
{
    public string DisplayLabel => $"{Name}: {(Delta > 0 ? "+" : string.Empty)}{Delta:N0}";
}