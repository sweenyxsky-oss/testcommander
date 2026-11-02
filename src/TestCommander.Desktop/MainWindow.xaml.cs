using System.IO;
using System.Windows;
using Microsoft.Win32;
using System.Windows.Input;
using System.Windows.Controls;
using TestCommander.Models;
using Microsoft.Web.WebView2.Core;
using TestCommander.Services;
using TestCommander.ViewModels;

namespace TestCommander;

public partial class MainWindow : Window
{
    private readonly DatabaseService _database = new();
    private readonly PassiveResponseMonitor _monitor;
    private readonly MainViewModel _viewModel;
    private bool _initialized;
    private bool _retentionReady;

    public MainWindow()
    {
        InitializeComponent();
        _monitor = new PassiveResponseMonitor(_database);
        _viewModel = new MainViewModel(_database);
        DataContext = _viewModel;
        _monitor.StatusChanged += (_, message) => Dispatcher.Invoke(() => _viewModel.Status = message);
        _monitor.SnapshotCaptured += (_, _) => Dispatcher.InvokeAsync(RefreshAfterSnapshotCapturedAsync);
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized) return;
        _initialized = true;
        try
        {
            AppPaths.EnsureCreated();
            var environment = await CoreWebView2Environment.CreateAsync(null, AppPaths.BrowserProfile);
            await Browser.EnsureCoreWebView2Async(environment);
            Browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
            Browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            Browser.CoreWebView2.Settings.IsStatusBarEnabled = false;
            Browser.CoreWebView2.NavigationCompleted += (_, args) =>
            {
                _viewModel.Status = args.IsSuccess ? $"Page loaded: {Browser.Source?.Host}" : "Navigation did not complete successfully.";
            };
            Browser.CoreWebView2.NavigationStarting += (_, args) =>
            {
                if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var targetUri))
                {
                    args.Cancel = true;
                    _viewModel.Status = NavigationSecurityPolicy.InvalidAddressMessage;
                }
                else if (!NavigationSecurityPolicy.IsAllowed(targetUri, out var reason))
                {
                    args.Cancel = true;
                    _viewModel.Status = reason ?? "Navigation blocked.";
                }
            };
            Browser.CoreWebView2.SourceChanged += (_, _) => UpdateAllowedOrigin();
            _monitor.Attach(Browser.CoreWebView2);
            UpdateAllowedOrigin();
            await _viewModel.RefreshAsync();
            _retentionReady = true;
            RetentionSelector.SelectedItem = _viewModel.SelectedRetentionOption;
            NavigateToAddress();
        }
        catch (Exception ex)
        {
            _viewModel.Status = $"Browser initialization failed: {ex.Message}";
        }
    }

    private async Task RefreshAfterSnapshotCapturedAsync()
    {
        try
        {
            await _viewModel.RefreshAsync();
        }
        catch (Exception ex)
        {
            _viewModel.Status = $"Dashboard refresh failed: {ex.Message}";
        }
    }

    private void UpdateAllowedOrigin()
    {
        if (Browser.Source is { } source && NavigationSecurityPolicy.IsAllowed(source, out _))
            _monitor.SetAllowedOrigin(source);
        else
            _monitor.SetAllowedOrigin(null);
    }

    private void NavigateToAddress()
    {
        if (!NavigationSecurityPolicy.TryParseUserAddress(_viewModel.Address, out var uri, out var error))
        {
            _viewModel.Status = error ?? NavigationSecurityPolicy.InvalidAddressMessage;
            return;
        }

        _viewModel.Address = uri!.ToString();
        _monitor.SetAllowedOrigin(uri);
        Browser.CoreWebView2?.Navigate(uri.ToString());
    }

    private void Navigate_Click(object sender, RoutedEventArgs e) => NavigateToAddress();

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        Browser.CoreWebView2?.Reload();
        try
        {
            await _viewModel.RefreshAsync();
        }
        catch (Exception ex)
        {
            _viewModel.Status = $"Refresh failed: {ex.Message}";
        }
    }

    private void AddressBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) NavigateToAddress();
    }

    private async void Retention_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_retentionReady || RetentionSelector.SelectedItem is not RetentionOption option) return;
        try
        {
            await _viewModel.SetSnapshotRetentionAsync(option.Days);
        }
        catch (Exception ex)
        {
            _viewModel.Status = $"Could not update snapshot retention: {ex.Message}";
        }
    }

    private async void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export village resource history",
            Filter = "CSV files (*.csv)|*.csv",
            DefaultExt = ".csv",
            AddExtension = true,
            FileName = $"TestCommander-resource-history-{DateTime.Now:yyyyMMdd}.csv",
            OverwritePrompt = true
        };

        if (dialog.ShowDialog(this) != true) return;

        try
        {
            await using var stream = new FileStream(dialog.FileName, FileMode.Create, FileAccess.Write, FileShare.None);
            await _database.ExportSnapshotsCsvAsync(stream);
            _viewModel.Status = $"Export completed: {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            _viewModel.Status = $"CSV export failed: {ex.Message}";
        }
    }

    private async void CreateBackup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Back up TestCommander data",
            Filter = "SQLite database (*.db)|*.db",
            DefaultExt = ".db",
            AddExtension = true,
            FileName = $"TestCommander-backup-{DateTime.Now:yyyyMMdd-HHmmss}.db",
            OverwritePrompt = true
        };

        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var path = await _database.CreateBackupAsync(dialog.FileName);
            _viewModel.Status = $"Database backup created: {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            _viewModel.Status = $"Database backup failed: {ex.Message}";
        }
    }

    private async void RestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select a TestCommander database backup",
            Filter = "SQLite database (*.db)|*.db|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true) return;

        var confirmation = MessageBox.Show(
            this,
            "Restoring replaces the current TestCommander history and settings. " +
            "A separate safety backup of the current database will be created automatically first. Continue?",
            "Confirm database restore",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes) return;

        var core = Browser.CoreWebView2;
        try
        {
            if (core is not null)
                await _monitor.DetachAndWaitAsync(core);

            var safetyBackup = await _database.RestoreBackupAsync(dialog.FileName);
            try
            {
                await _viewModel.ReloadAfterDatabaseRestoreAsync();
                _viewModel.Status = $"Restore completed. Previous data preserved at: {Path.GetFileName(safetyBackup)}";
            }
            catch (Exception refreshException)
            {
                _viewModel.Status = $"Database restore completed, but the dashboard refresh failed. Safety backup: {Path.GetFileName(safetyBackup)}. {refreshException.Message}";
            }
        }
        catch (Exception ex)
        {
            _viewModel.Status = $"Database restore did not complete. The automatic safety backup is retained if it was created. {ex.Message}";
        }
        finally
        {
            if (core is not null)
                _monitor.Attach(core);
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (Browser.CoreWebView2 is { } core) _monitor.Detach(core);
    }
}