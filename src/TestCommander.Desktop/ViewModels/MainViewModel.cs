using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using TestCommander.Models;
using TestCommander.Services;

namespace TestCommander.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private const int HistorySampleLimit = 24;
    private readonly DatabaseService _database;
    private string _status = "Starting browser…";
    private string _address = "https://www.travian.com/";
    private string _resourceSummary = "No village data captured yet";
    private int? _selectedRetentionDays = 180;
    private bool _retentionInitialized;

    public ObservableCollection<CapturedResponse> RecentResponses { get; } = [];
    public ObservableCollection<VillageResourceOverview> ResourceOverviews { get; } = [];
    public ObservableCollection<RetentionOption> RetentionOptions { get; } =
    [
        new(30, "30 days"),
        new(90, "90 days"),
        new(180, "180 days"),
        new(365, "1 year"),
        new(null, "Never")
    ];

    public string Status { get => _status; set => Set(ref _status, value); }
    public string Address { get => _address; set => Set(ref _address, value); }
    public string ResourceSummary { get => _resourceSummary; set => Set(ref _resourceSummary, value); }
    public int? SelectedRetentionDays { get => _selectedRetentionDays; private set => Set(ref _selectedRetentionDays, value); }
    public event PropertyChangedEventHandler? PropertyChanged;

    public MainViewModel(DatabaseService database) => _database = database;

    public async Task RefreshAsync()
    {
        if (!_retentionInitialized)
        {
            SelectedRetentionDays = await _database.GetSnapshotRetentionDaysAsync();
            await _database.PruneSnapshotsAsync(SelectedRetentionDays);
            _retentionInitialized = true;
        }

        RecentResponses.Clear();
        foreach (var response in await _database.GetRecentResponsesAsync())
            RecentResponses.Add(response);

        ResourceOverviews.Clear();
        var latestSnapshots = await _database.GetLatestSnapshotsAsync();
        foreach (var latest in latestSnapshots)
        {
            var history = await _database.GetRecentSnapshotsAsync(latest.VillageName, HistorySampleLimit);
            var current = history.Count > 0 ? history[^1] : latest;
            var previous = history.Count > 1 ? history[^2] : null;
            ResourceOverviews.Add(VillageResourceCalculator.Create(current, previous, history));
        }

        ResourceSummary = ResourceOverviews.Count == 0
            ? "No village data captured yet"
            : $"{ResourceOverviews.Count} village(s) • trend from up to {HistorySampleLimit} distinct samples • retention: {RetentionLabel(SelectedRetentionDays)}";
    }

    public async Task SetSnapshotRetentionAsync(int? days)
    {
        if (!RetentionOptions.Any(option => option.Days == days))
            throw new ArgumentOutOfRangeException(nameof(days), "Choose one of the available retention periods.");

        if (SelectedRetentionDays == days && _retentionInitialized) return;

        await _database.SetSnapshotRetentionDaysAsync(days);
        var removed = await _database.PruneSnapshotsAsync(days);
        SelectedRetentionDays = days;
        _retentionInitialized = true;
        await RefreshAsync();
        Status = removed > 0
            ? $"Snapshot retention updated. Removed {removed:N0} expired sample(s)."
            : $"Snapshot retention updated: {RetentionLabel(days)}.";
    }

    private static string RetentionLabel(int? days) => days.HasValue ? $"{days.Value} days" : "never";

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new(propertyName));
    }
}