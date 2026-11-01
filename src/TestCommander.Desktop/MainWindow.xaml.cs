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

    public MainWindow()
    {
        InitializeComponent();
        _monitor = new PassiveResponseMonitor(_database);
        _viewModel = new MainViewModel(_database);
        DataContext = _viewModel;
        _monitor.StatusChanged += (_, message) => Dispatcher.Invoke(() => _viewModel.Status = message);
        _monitor.SnapshotCaptured += (_, _) => Dispatcher.InvokeAsync(_viewModel.RefreshAsync);
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
            Browser.CoreWebView2.SourceChanged += (_, _) => UpdateAllowedOrigin();
            _monitor.Attach(Browser.CoreWebView2);
            UpdateAllowedOrigin();
            await _viewModel.RefreshAsync();
            NavigateToAddress();
        }
        catch (Exception ex)
        {
            _viewModel.Status = $"Browser initialization failed: {ex.Message}";
        }
    }

    private void UpdateAllowedOrigin()
    {
        if (Browser.Source is { } source && (source.Scheme == Uri.UriSchemeHttps || source.IsLoopback))
            _monitor.SetAllowedOrigin(source);
        else
            _monitor.SetAllowedOrigin(null);
    }

    private void NavigateToAddress()
    {
        var value = _viewModel.Address.Trim();
        if (string.IsNullOrWhiteSpace(value)) return;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            if (!Uri.TryCreate("https://" + value, UriKind.Absolute, out uri))
            {
                _viewModel.Status = "Enter a valid website address.";
                return;
            }
        }
        if (uri.Scheme is not ("https" or "http"))
        {
            _viewModel.Status = "Only HTTP and HTTPS pages can be opened.";
            return;
        }
        if (uri.Scheme == "http" && !uri.IsLoopback)
        {
            _viewModel.Status = "For safety, non-local sites must use HTTPS.";
            return;
        }
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            _viewModel.Status = "Remove embedded username/password details from the address.";
            return;
        }
        _viewModel.Address = uri.ToString();
        _monitor.SetAllowedOrigin(uri);
        Browser.CoreWebView2?.Navigate(uri.ToString());
    }

    private async void ClearData_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(
            "Delete all locally collected response metadata, village snapshots, queue observations, and troop movement observations?\\n\\nYour browser profile and signed-in session will not be deleted.",
            "Clear local monitoring data",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;
        try
        {
            await _viewModel.ClearObservedDataAsync();
        }
        catch (Exception ex)
        {
            _viewModel.Status = $"Could not clear local data: {ex.Message}";
        }
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

    private void OnClosed(object? sender, EventArgs e)
    {
        if (Browser.CoreWebView2 is { } core) _monitor.Detach(core);
    }
}