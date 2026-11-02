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
    private readonly object _refreshGate = new();
    private Task? _refreshTask;
    private bool _refreshRequested;
    private string _status = "Starting browser…";
    private string _address = "https://www.travian.com/";
    private string _resourceSummary = "No village data captured yet";
    private RetentionOption _selectedRetentionOption = new(180, "180 days");
    private bool _retentionInitialized;

    public ObservableCollection<CapturedResponse> RecentResponses { get; } = [];
    public ObservableCollection<VillageResourceOverview> ResourceOverviews { get; } = [];
    public ObservableCollection<VillageComparisonRow> VillageComparisons { get; } = [];
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
    public RetentionOption SelectedRetentionOption { get => _selectedRetentionOption; private set => Set(ref _selectedRetentionOption, value); }
    public event PropertyChangedEventHandler? PropertyChanged;

    public MainViewModel(DatabaseService database) => _database = database;

    public Task RefreshAsync()
    {
        TaskCompletionSource? completion = null;
        Task refreshTask;
        lock (_refreshGate)
        {
            // Keep at most one refresh in flight. If another capture arrives mid-refresh,
            // coalesce it into one trailing refresh so the latest database state is shown.
            _refreshRequested = true;
            if (_refreshTask is null)
            {
                completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _refreshTask = completion.Task;
            }
            refreshTask = _refreshTask;
        }

        if (completion is not null)
            _ = RunRefreshLoopAsync(completion);

        return refreshTask;
    }

    private async Task RunRefreshLoopAsync(TaskCompletionSource completion)
    {
        try
        {
            while (true)
            {
                lock (_refreshGate)
                {
                    if (!_refreshRequested)
                    {
                        _refreshTask = null;
                        break;
                    }
                    _refreshRequested = false;
                }

                await RefreshCoreAsync();
            }

            completion.TrySetResult();
        }
        catch (Exception ex)
        {
            lock (_refreshGate)
            {
                _refreshRequested = false;
                _refreshTask = null;
            }
            completion.TrySetException(ex);
        }
    }

    private async Task RefreshCoreAsync()
    {
        if (!_retentionInitialized)
        {
            var retentionDays = await _database.GetSnapshotRetentionDaysAsync();
            var retentionOption = RetentionOptions.FirstOrDefault(option => option.Days == retentionDays)
                ?? RetentionOptions.First(option => option.Days == 180);
            SelectedRetentionOption = retentionOption;
            await _database.PruneSnapshotsAsync(retentionOption.Days);
            await _database.PruneCapturedResponsesAsync();
            _retentionInitialized = true;
        }

        RecentResponses.Clear();
        foreach (var response in await _database.GetRecentResponsesAsync())
            RecentResponses.Add(response);

        ResourceOverviews.Clear();
        VillageComparisons.Clear();
        var latestSnapshots = await _database.GetLatestSnapshotsAsync();
        var batchHistory = await _database.GetRecentSnapshotsBatchAsync(HistorySampleLimit);
        foreach (var latest in latestSnapshots)
        {
            var history = batchHistory.TryGetValue(latest.VillageName, out var list)
                ? list
                : [latest];
            var current = history.Count > 0 ? history[^1] : latest;
            var previous = history.Count > 1 ? history[^2] : null;
            ResourceOverviews.Add(VillageResourceCalculator.Create(current, previous, history));
        }

        foreach (var comparison in VillageComparisonBuilder.Create(ResourceOverviews))
            VillageComparisons.Add(comparison);

        ResourceSummary = ResourceOverviews.Count == 0
            ? "No village data captured yet"
            : $"{ResourceOverviews.Count} village(s) • trend from up to {HistorySampleLimit} distinct samples • retention: {SelectedRetentionOption.Label}";
    }

    public async Task ReloadAfterDatabaseRestoreAsync()
    {
        _retentionInitialized = false;
        await RefreshAsync();
    }

    public async Task SetSnapshotRetentionAsync(int? days)
    {
        if (!RetentionOptions.Any(option => option.Days == days))
            throw new ArgumentOutOfRangeException(nameof(days), "Choose one of the available retention periods.");

        if (SelectedRetentionOption.Days == days && _retentionInitialized) return;

        await _database.SetSnapshotRetentionDaysAsync(days);
        var removed = await _database.PruneSnapshotsAsync(days);
        SelectedRetentionOption = RetentionOptions.First(option => option.Days == days);
        _retentionInitialized = true;
        await RefreshAsync();
        Status = removed > 0
            ? $"Snapshot retention updated. Removed {removed:N0} expired sample(s)."
            : $"Snapshot retention updated: {SelectedRetentionOption.Label}.";
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new(propertyName));
    }
}
