using System.Windows;
using System.Windows.Input;
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
    private int _refreshQueued;

    public MainWindow()
    {
        InitializeComponent();
        _monitor = new PassiveResponseMonitor(_database);
        _viewModel = new MainViewModel(_database);
        DataContext = _viewModel;
        _monitor.StatusChanged += (_, message) => SetStatusSafely(message);
        _monitor.DataCaptured += (_, _) => QueueDashboardRefresh();
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
            var settings = Browser.CoreWebView2.Settings;
            settings.AreDevToolsEnabled = false;
            settings.AreDefaultContextMenusEnabled = true;
            settings.IsStatusBarEnabled = false;
            settings.IsPasswordAutosaveEnabled = false;
            settings.IsGeneralAutofillEnabled = false;
            Browser.CoreWebView2.NavigationStarting += OnNavigationStarting;
            Browser.CoreWebView2.NewWindowRequested += OnNewWindowRequested;
            Browser.CoreWebView2.NavigationCompleted += (_, args) =>
            {
                if (args.IsSuccess)
                    SetStatusSafely($"Page loaded: {Browser.Source?.Host}");
                else
                {
                    UpdateAllowedOrigin();
                    SetStatusSafely("Navigation did not complete successfully.");
                }
            };
            Browser.CoreWebView2.SourceChanged += (_, _) => UpdateAllowedOrigin();
            _monitor.Attach(Browser.CoreWebView2);
            UpdateAllowedOrigin();
            await _viewModel.RefreshAsync();
            NavigateToAddress();
        }
        catch (Exception ex)
        {
            _viewModel.Status = $"Browser initialization failed: {ex.GetType().Name}. Check the WebView2 Runtime installation.";
        }
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs args)
    {
        if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)) ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            args.Cancel = true;
            _viewModel.Status = "Navigation blocked: only HTTPS sites (or local HTTP development addresses) are allowed.";
            return;
        }
        _monitor.SetAllowedOrigin(uri);
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        args.Handled = true;
        _viewModel.Status = "A pop-up was blocked. Open the destination manually if you trust it.";
    }

    private void UpdateAllowedOrigin()
    {
        if (Browser.Source is { } source &&
            (source.Scheme == Uri.UriSchemeHttps || (source.Scheme == Uri.UriSchemeHttp && source.IsLoopback)))
            _monitor.SetAllowedOrigin(source);
        else
            _monitor.SetAllowedOrigin(null);
    }

    private void NavigateToAddress()
    {
        var value = _viewModel.Address.Trim();
        if (string.IsNullOrWhiteSpace(value)) return;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            !Uri.TryCreate("https://" + value, UriKind.Absolute, out uri))
        {
            _viewModel.Status = "Enter a valid website address.";
            return;
        }
        if (uri.Scheme is not ("https" or "http") ||
            (uri.Scheme == "http" && !uri.IsLoopback) ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            _viewModel.Status = "Use an HTTPS address without embedded credentials. HTTP is allowed only for localhost.";
            return;
        }
        _viewModel.Address = uri.ToString();
        _monitor.SetAllowedOrigin(uri);
        Browser.CoreWebView2?.Navigate(uri.ToString());
    }

    private async void ClearData_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(
            "Delete all locally collected response metadata, village snapshots, queue observations, and troop movement observations?\n\nYour browser profile and signed-in session will not be deleted.",
            "Clear local monitoring data", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;
        try { await _viewModel.ClearObservedDataAsync(); }
        catch (Exception ex) { _viewModel.Status = $"Could not clear local data: {ex.GetType().Name}."; }
    }

    private void Navigate_Click(object sender, RoutedEventArgs e) => NavigateToAddress();

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        Browser.CoreWebView2?.Reload();
        try { await _viewModel.RefreshAsync(); }
        catch (Exception ex) { _viewModel.Status = $"Refresh failed: {ex.GetType().Name}."; }
    }

    private void AddressBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) NavigateToAddress();
    }

    private void QueueDashboardRefresh()
    {
        if (Dispatcher.HasShutdownStarted || Interlocked.Exchange(ref _refreshQueued, 1) == 1) return;
        try
        {
            _ = Dispatcher.InvokeAsync(async () =>
            {
                try { await _viewModel.RefreshAsync(); }
                catch (Exception ex) { _viewModel.Status = $"Dashboard refresh failed: {ex.GetType().Name}."; }
                finally { Interlocked.Exchange(ref _refreshQueued, 0); }
            });
        }
        catch (InvalidOperationException)
        {
            Interlocked.Exchange(ref _refreshQueued, 0);
        }
    }

    private void SetStatusSafely(string message)
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
        try { _ = Dispatcher.InvokeAsync(() => _viewModel.Status = message); }
        catch (InvalidOperationException) { /* Window is closing. */ }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (Browser.CoreWebView2 is { } core)
        {
            _monitor.Detach(core);
            core.NavigationStarting -= OnNavigationStarting;
            core.NewWindowRequested -= OnNewWindowRequested;
        }
    }
}
