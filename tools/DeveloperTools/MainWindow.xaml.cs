using IntegratedModManager.DeveloperTools.Pages;
using IntegratedModManager.DeveloperTools.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace IntegratedModManager.DeveloperTools;

/// <summary>
/// The shell: the app folder being inspected, the tools' language and theme, the status bar and one page at a time.
/// Each page is created the first time it is shown and then kept, like the app's pages.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly ToolContext _context;
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly Dictionary<string, UserControl> _pages = [];
    private IToolPage? _current;
    private bool _applying;

    internal MainWindow(ToolContext context)
    {
        _context = context;
        InitializeComponent();
        _context.WindowHandle = WindowNative.GetWindowHandle(this);
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        TryApplyWindowPresentation();
        _context.StatusChanged += SetStatus;
        _refreshTimer.Tick += async (_, _) => await RefreshAsync(showStatus: false);

        _applying = true;
        foreach ((string code, string name) in _context.Strings.Languages)
            LanguageComboBox.Items.Add(new ComboBoxItem { Content = name, Tag = code });
        LanguageComboBox.SelectedItem = LanguageComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == _context.Strings.Current);
        AutoRefreshToggle.IsOn = _context.Settings.AutoRefresh;
        _applying = false;

        ApplyTheme();
        ApplyLanguage();
        RootPathTextBox.Text = _context.RootPath;
        _context.Reload();
        if (_context.Settings.AutoRefresh) _refreshTimer.Start();

        NavigationViewItem start = Navigation.MenuItems.OfType<NavigationViewItem>()
            .FirstOrDefault(item => string.Equals((string)item.Tag, _context.Options.Page, StringComparison.OrdinalIgnoreCase)) ?? OverviewNav;
        Navigation.SelectedItem = start;
    }

    private string T(string key, params object?[] values) => _context.T(key, values);

    private void TryApplyWindowPresentation()
    {
        try
        {
            // AppWindow works in physical pixels: 1440 × 960 DIP at the window's scale.
            double scale = GetDpiForWindow(WindowNative.GetWindowHandle(this)) / 96.0;
            AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(1440 * scale), (int)(960 * scale)));
            AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.PreferredMinimumWidth = 960;
                presenter.PreferredMinimumHeight = 640;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or System.Runtime.InteropServices.COMException)
        {
            // Window sizing is cosmetic.
        }
    }

    private void ApplyLanguage()
    {
        Title = T("App.Title");
        TitleTextBlock.Text = T("App.Title");
        OverviewNav.Content = T("Nav.Overview");
        RepositoriesNav.Content = T("Nav.Repositories");
        RecordsNav.Content = T("Nav.Records");
        LogsNav.Content = T("Nav.Logs");
        CachesNav.Content = T("Nav.Caches");
        ModKeysNav.Content = T("Nav.ModKeys");
        TranslationsNav.Content = T("Nav.Translations");
        FilesNav.Content = T("Nav.Files");
        DiagnosticsNav.Content = T("Nav.Diagnostics");
        RootLabel.Text = T("Shell.AppFolder");
        BrowseRootButton.Content = T("Shell.Browse");
        RefreshButton.Content = T("Shell.Refresh");
        AutoRefreshToggle.OnContent = T("Shell.AutoRefreshOn");
        AutoRefreshToggle.OffContent = T("Shell.AutoRefreshOff");
        ToolTipService.SetToolTip(AutoRefreshToggle, T("Shell.AutoRefreshHint"));
        ToolTipService.SetToolTip(LanguageComboBox, T("Shell.Language"));
        ToolTipService.SetToolTip(ThemeComboBox, T("Shell.Theme"));

        _applying = true;
        string theme = _context.Options.Theme ?? _context.Settings.Theme;
        ThemeComboBox.Items.Clear();
        foreach ((string tag, string key) in new[] { ("system", "Shell.ThemeSystem"), ("light", "Shell.ThemeLight"), ("dark", "Shell.ThemeDark") })
            ThemeComboBox.Items.Add(new ComboBoxItem { Content = T(key), Tag = tag });
        ThemeComboBox.SelectedItem = ThemeComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == theme) ?? ThemeComboBox.Items[0];
        _applying = false;

        foreach (UserControl page in _pages.Values) ((IToolPage)page).ApplyLanguage();
    }

    private void ApplyTheme()
    {
        string theme = _context.Options.Theme ?? _context.Settings.Theme;
        RootGrid.RequestedTheme = theme switch { "light" => ElementTheme.Light, "dark" => ElementTheme.Dark, _ => ElementTheme.Default };
    }

    private async void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem { Tag: string tag }) return;
        if (!_pages.TryGetValue(tag, out UserControl? page))
        {
            page = tag switch
            {
                "repositories" => new RepositoriesPage(_context),
                "records" => new RecordsPage(_context),
                "logs" => new LogsPage(_context),
                "caches" => new CachesPage(_context),
                "modkeys" => new ModKeysPage(_context),
                "translations" => new TranslationsPage(_context),
                "files" => new FilesPage(_context),
                "diagnostics" => new DiagnosticsPage(_context),
                _ => new OverviewPage(_context)
            };
            _pages[tag] = page;
            PageHost.Children.Add(page);
        }
        foreach (UIElement child in PageHost.Children) child.Visibility = ReferenceEquals(child, page) ? Visibility.Visible : Visibility.Collapsed;
        _current = (IToolPage)page;
        await RefreshPageAsync(_current);
    }

    private async Task RefreshPageAsync(IToolPage page)
    {
        try { await page.RefreshAsync(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException)
        {
            SetStatus(T("Status.RefreshFailed", ex.Message), InfoBarSeverity.Error);
        }
    }

    private async Task RefreshAsync(bool showStatus)
    {
        RootPathTextBox.Text = _context.RootPath;
        _context.Reload();
        if (_current is not null) await RefreshPageAsync(_current);
        if (_context.ShellError is not null) SetStatus(T("Status.ShellUnreadable", _context.ShellError), InfoBarSeverity.Warning);
        else if (showStatus) SetStatus(T("Status.Refreshed"), InfoBarSeverity.Success);
    }

    private async void OnRefreshClicked(object sender, RoutedEventArgs e) => await RefreshAsync(showStatus: true);

    private async void OnBrowseRootClicked(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, _context.WindowHandle);
        Windows.Storage.StorageFolder? folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;
        _context.SetRoot(folder.Path);
        await RefreshAsync(showStatus: true);
    }

    private void OnAutoRefreshToggled(object sender, RoutedEventArgs e)
    {
        if (_applying) return;
        _context.Settings.AutoRefresh = AutoRefreshToggle.IsOn;
        if (AutoRefreshToggle.IsOn) _refreshTimer.Start(); else _refreshTimer.Stop();
        _context.SaveSettings();
    }

    private async void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_applying || LanguageComboBox.SelectedItem is not ComboBoxItem { Tag: string code }) return;
        _context.Strings.Use(code);
        if (_context.Options.Language is null)
        {
            _context.Settings.Language = code;
            _context.SaveSettings();
        }
        ApplyLanguage();
        if (_current is not null) await RefreshPageAsync(_current);
    }

    private async void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_applying || ThemeComboBox.SelectedItem is not ComboBoxItem { Tag: string tag }) return;
        if (_context.Options.Theme is null)
        {
            _context.Settings.Theme = tag;
            _context.SaveSettings();
        }
        RootGrid.RequestedTheme = tag switch { "light" => ElementTheme.Light, "dark" => ElementTheme.Dark, _ => ElementTheme.Default };
        // Colours set in code (found, missing, sendable) come from the theme: draw the page again.
        if (_current is not null) await RefreshPageAsync(_current);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);

    private void SetStatus(string message, InfoBarSeverity severity)
    {
        StatusInfoBar.Message = message;
        StatusInfoBar.Severity = severity;
        StatusInfoBar.IsOpen = true;
    }
}
