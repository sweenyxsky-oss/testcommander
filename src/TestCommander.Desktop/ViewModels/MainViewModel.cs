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
    public ObservableCollection<CapturedResponse> RecentResponses { get; } = [];
    public ObservableCollection<VillageResourceOverview> ResourceOverviews { get; } = [];
    public string Status { get => _status; set => Set(ref _status, value); }
    public string Address { get => _address; set => Set(ref _address, value); }
    public string ResourceSummary { get => _resourceSummary; set => Set(ref _resourceSummary, value); }
    public event PropertyChangedEventHandler? PropertyChanged;

    public MainViewModel(DatabaseService database) => _database = database;

    public async Task RefreshAsync()
    {
        RecentResponses.Clear();
        foreach (var response in await _database.GetRecentResponsesAsync())
            RecentResponses.Add(response);

        ResourceOverviews.Clear();
        foreach (var snapshot in await _database.GetLatestSnapshotsAsync())
            ResourceOverviews.Add(VillageResourceCalculator.Create(snapshot));

        ResourceSummary = ResourceOverviews.Count == 0
            ? "No village data captured yet"
            : $"{ResourceOverviews.Count} village(s) • storage utilization from latest captured values";
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new(propertyName));
    }
}