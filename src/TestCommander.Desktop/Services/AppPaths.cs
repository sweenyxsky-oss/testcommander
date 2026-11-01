namespace TestCommander.Services;

public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TestCommander");
    public static string BrowserProfile => Path.Combine(Root, "BrowserProfile");
    public static string Database => Path.Combine(Root, "testcommander.db");
    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(BrowserProfile);
    }
}