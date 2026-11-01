using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using TestCommander.Models;
using TestCommander.Services;

namespace TestCommander.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly DatabaseService _database;
    private string _status = "Starting browser…";
    private string _address = "https://www.travian.com/";
    private string _resourceSummary = "No village data captured yet";
    private string _queueSummary = "No queue observations yet";
    private string _movementSummary = "No troop movements observed yet";
    public ObservableCollection<CapturedResponse> RecentResponses { get; } = [];
    public ObservableCollection<VillageSnapshot> Villages { get; } = [];
    public string Status { get => _status; set => Set(ref _status, value); }
    public string Address { get => _address; set => Set(ref _address, value); }
    public string ResourceSummary { get => _resourceSummary; set => Set(ref _resourceSummary, value); }
    public string QueueSummary { get => _queueSummary; set => Set(ref _queueSummary, value); }
    public string MovementSummary { get => _movementSummary; set => Set(ref _movementSummary, value); }
    public event PropertyChangedEventHandler? PropertyChanged;

    public MainViewModel(DatabaseService database) => _database = database;

    public async Task ClearObservedDataAsync()
    {
        await _database.ClearObservedDataAsync();
        await RefreshAsync();
        Status = "Local monitoring data cleared. Your browser session was kept.";
    }

    public async Task RefreshAsync()
    {
        RecentResponses.Clear();
        foreach (var response in await _database.GetRecentResponsesAsync()) RecentResponses.Add(response);
        Villages.Clear();
        foreach (var village in await _database.GetLatestSnapshotsAsync()) Villages.Add(village);
        ResourceSummary = Villages.Count == 0 ? "No village data captured yet" : $"{Villages.Count} village snapshot(s) stored locally";
        var queues = await _database.GetRecentQueueSummariesAsync();
        QueueSummary = queues.Count == 0 ? "No queue observations yet" : string.Join(Environment.NewLine, queues);
        var movements = await _database.GetRecentMovementSummariesAsync();
        MovementSummary = movements.Count == 0 ? "No troop movements observed yet" : string.Join(Environment.NewLine, movements);
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new(propertyName));
    }
}