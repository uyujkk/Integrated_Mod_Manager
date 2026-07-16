using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI.Core;
using WinRT.Interop;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow : Window
{
    private const string AppVersion = "v3.0.1";
    private const string GitHubRepositoryUrl = "https://github.com/uyujkk/mod-folder-copier-winui3";
    private const string GitHubLatestReleaseApiUrl = "https://api.github.com/repos/uyujkk/mod-folder-copier-winui3/releases/latest";
    private const string DefaultOnlineSourceSite = "GameBanana";
    private const string DefaultOnlineCategoryId = "42770";
    private const int OnlineDisplayPageSize = 20;
    private const int OnlineRawFetchPageSize = 50;
    private const int OnlineRawPageLimit = 80;
    private const int MaxShortcutRows = 10;
    private static readonly string[] SupportedArchiveExtensions = [".zip", ".7z", ".tar", ".gz", ".tgz", ".bz2", ".xz"];

    private enum PrimarySection
    {
        Dashboard,
        Repository,
        Online,
        Updates,
        Settings
    }

    private enum AppLanguage
    {
        ZhCn,
        EnUs
    }

    private enum ShellLayoutMode
    {
        Expanded,
        Compact,
        Minimal
    }

    private enum UpdateCheckInterval
    {
        Manual,
        Daily,
        EveryThreeDays,
        Weekly
    }

    private enum OnlineSortMode
    {
        Hotness,
        Downloads,
        Likes,
        Views,
        Updated
    }

    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp"];
    private static readonly SolidColorBrush CopiedBrush = new(ColorHelper.FromArgb(255, 36, 124, 76));
    private static readonly SolidColorBrush MissingBrush = new(ColorHelper.FromArgb(255, 177, 76, 55));
    private static readonly SolidColorBrush NeutralBrush = new(ColorHelper.FromArgb(255, 60, 60, 60));

    private readonly string _configPath = Path.Combine(AppContext.BaseDirectory, "config.ini");
    private readonly string _shellConfigPath = Path.Combine(AppContext.BaseDirectory, "beta-shell.json");
    private readonly HttpClient _httpClient = CreateHttpClient();
    private readonly ObservableCollection<FirstLevelFolderItem> _firstLevelItems = [];
    private readonly ObservableCollection<SecondLevelFolderItem> _secondLevelItems = [];
    private readonly List<FirstLevelFolderItem> _allFirstLevelItems = [];
    private readonly List<Border> _shortcutKeyBorders = [];
    private readonly List<TextBox> _shortcutKeyBoxes = [];
    private readonly List<TextBox> _shortcutActionBoxes = [];
    private readonly Dictionary<string, List<ShortcutBinding>> _modBindings = new(StringComparer.CurrentCultureIgnoreCase);
    private readonly Dictionary<string, string> _modLinks = new(StringComparer.CurrentCultureIgnoreCase);
    private readonly Dictionary<string, TrackedModOrigin> _trackedModOrigins = new(StringComparer.CurrentCultureIgnoreCase);
    private readonly Dictionary<int, OnlineModDetails> _onlineModDetailsCache = [];
    private readonly List<WorkspaceRepository> _repositories = [];
    private readonly List<OnlineModCard> _onlineMods = [];
    private readonly List<TrackedModUpdateResult> _trackedModUpdateResults = [];

    private bool _isDarkTheme;
    private bool _isCheckingUpdates;
    private bool _isCheckingModUpdates;
    private bool _isLoadingOnlineMods;
    private bool _isLoadingBindings;
    private bool _isLoadingModLink;
    private bool _isApplyingOnlineCharacterSelection;
    private bool _isApplyingUpdateCheckIntervalSelection;
    private bool _isApplyingModUpdateIntervalSelection;
    private int _visibleShortcutRows = 1;
    private TextBox? _activeShortcutKeyBox;
    private string? _currentSecondLevelPath;
    private string? _latestReleaseBody;
    private string? _latestReleasePublishedAt;
    private string? _latestReleaseTag;
    private string? _latestReleaseTitle;
    private string? _latestReleaseUrl;
    private string? _onlineStatusZh;
    private string? _onlineStatusEn;
    private string? _lastLoadedOnlineConfigKey;
    private int _onlineCurrentPage = 1;
    private int _onlineTotalCount;
    private int _onlineTotalPages = 1;
    private string _onlineCharacterFilter = string.Empty;
    private string _onlineSearchText = string.Empty;
    private string? _selectedRepositoryId;
    private string _modUpdateStatusEn = string.Empty;
    private string _modUpdateStatusZh = string.Empty;
    private string _updateStatusEn = string.Empty;
    private string _updateStatusZh = string.Empty;
    private OnlineModCard? _activeOnlineDetailMod;
    private AppLanguage _currentLanguage = AppLanguage.ZhCn;
    private PrimarySection _currentPrimarySection = PrimarySection.Dashboard;
    private ShellLayoutMode _shellLayoutMode = ShellLayoutMode.Expanded;
    private OnlineSortMode _onlineSortMode = OnlineSortMode.Hotness;
    private UpdateCheckInterval _updateCheckInterval = UpdateCheckInterval.Manual;
    private DateTimeOffset? _lastUpdateCheckUtc;
    private UpdateCheckInterval _modUpdateCheckInterval = UpdateCheckInterval.Manual;
    private DateTimeOffset? _lastModUpdateCheckUtc;
    private TextBox? _lastFocusedTextBox;
    private readonly HashSet<string> _onlineKnownCharacters = new(StringComparer.OrdinalIgnoreCase);

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = false;
        FirstLevelListView.ItemsSource = _firstLevelItems;
        SecondLevelListView.ItemsSource = _secondLevelItems;
        RegisterTrackedTextInputs();
        BuildShortcutRows();
        LoadConfig();
        LoadShellConfig();
        ApplyTheme(_isDarkTheme);
        ApplyLanguage();
        InitializeOnlineControls();
        RefreshSettingsPane();
        RefreshSecondaryNavigation();
        ApplyShellState(refreshRepository: false);
        UpdateShellLayout();
        RefreshDashboard();
        _ = CheckForUpdatesIfDueAsync();

        if (Directory.Exists(SourceTextBox.Text))
        {
            _ = RefreshListsAsync();
        }
        else
        {
            SetDefaultStatus();
        }
    }

    private void RegisterTrackedTextInputs()
    {
        AttachTrackedTextInput(SourceTextBox);
        AttachTrackedTextInput(TargetTextBox);
        AttachTrackedTextInput(LauncherTextBox);
        AttachTrackedTextInput(FirstLevelSearchTextBox);
        AttachTrackedTextInput(ModLinkTextBox);
    }

    private void AttachTrackedTextInput(TextBox box)
    {
        box.GotFocus += OnTrackedTextBoxGotFocus;
        box.LostFocus += OnTrackedTextBoxLostFocus;
    }

    private void OnTrackedTextBoxGotFocus(object sender, RoutedEventArgs e)
    {
        _lastFocusedTextBox = sender as TextBox;
    }

    private void OnTrackedTextBoxLostFocus(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(_lastFocusedTextBox, sender))
        {
            _lastFocusedTextBox = null;
        }
    }

    private string L(string zh, string en)
    {
        return BetaLocalization.Resolve(_currentLanguage == AppLanguage.EnUs, zh, en);
    }

    private string StateNotSelectedText => L("未选择", "Not selected");

    private string StateNotConfiguredText => L("未配置", "Not configured");

    private string StateCopiedText => L("已复制", "Copied");

    private string StateMissingText => L("未复制", "Not copied");

    private string NormalizeUiText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return _currentLanguage == AppLanguage.EnUs
            ? value
            : BetaLocalization.NormalizeChineseText(value);
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("IntegratedModManager/3.0");
        return client;
    }

    private static UpdateCheckInterval ParseUpdateCheckInterval(string? value)
    {
        return Enum.TryParse(value, ignoreCase: true, out UpdateCheckInterval interval)
            ? interval
            : UpdateCheckInterval.Manual;
    }

    private static DateTimeOffset? TryParseDateTimeOffset(string? value)
    {
        return DateTimeOffset.TryParse(value, out DateTimeOffset parsed) ? parsed : null;
    }

    private TimeSpan? GetUpdateCheckIntervalTimeSpan()
    {
        return _updateCheckInterval switch
        {
            UpdateCheckInterval.Daily => TimeSpan.FromDays(1),
            UpdateCheckInterval.EveryThreeDays => TimeSpan.FromDays(3),
            UpdateCheckInterval.Weekly => TimeSpan.FromDays(7),
            _ => null
        };
    }

    private bool ShouldAutoCheckForUpdates()
    {
        TimeSpan? interval = GetUpdateCheckIntervalTimeSpan();
        if (interval is null)
        {
            return false;
        }

        return !_lastUpdateCheckUtc.HasValue || DateTimeOffset.UtcNow - _lastUpdateCheckUtc.Value >= interval.Value;
    }

    private async Task CheckForUpdatesIfDueAsync()
    {
        if (ShouldAutoCheckForModUpdates())
        {
            await CheckTrackedModUpdatesAsync(showDialogs: false);
        }
    }

    private TimeSpan? GetModUpdateCheckIntervalTimeSpan()
    {
        return _modUpdateCheckInterval switch
        {
            UpdateCheckInterval.Daily => TimeSpan.FromDays(1),
            UpdateCheckInterval.EveryThreeDays => TimeSpan.FromDays(3),
            UpdateCheckInterval.Weekly => TimeSpan.FromDays(7),
            _ => null
        };
    }

    private bool ShouldAutoCheckForModUpdates()
    {
        TimeSpan? interval = GetModUpdateCheckIntervalTimeSpan();
        if (interval is null)
        {
            return false;
        }

        return !_lastModUpdateCheckUtc.HasValue || DateTimeOffset.UtcNow - _lastModUpdateCheckUtc.Value >= interval.Value;
    }

    private void SetUpdateStatus(string zh, string en)
    {
        _updateStatusZh = zh;
        _updateStatusEn = en;
        UpdateStatusTextBlock.Text = L(zh, en);
    }

    private void SetModUpdateStatus(string zh, string en)
    {
        _modUpdateStatusZh = zh;
        _modUpdateStatusEn = en;
        if (ModUpdateStatusTextBlock is not null)
        {
            ModUpdateStatusTextBlock.Text = L(zh, en);
        }
    }

    private void RefreshSettingsPane()
    {
        SettingsTitleTextBlock.Text = L("应用设置", "Application Settings");
        SettingsSubtitleTextBlock.Text = L("把语言、主题、仓库入口和更新检查集中放在这里。", "Language, theme, repository entry points, and update checks are collected here.");
        SettingsAppearanceTitleTextBlock.Text = L("界面与语言", "Appearance and Language");
        SettingsProjectTitleTextBlock.Text = L("项目链接与软件版本", "Project Links and App Version");
        SettingsProjectHintTextBlock.Text = L("这里保留 GitHub 仓库入口和软件版本检查；Mod 更新请使用左侧的“更新”模块。", "This section keeps the GitHub repository link and app-version checks. Use the Updates section in the left navigation for mod updates.");
        UpdateCheckIntervalLabelTextBlock.Text = L("软件更新频率", "App update interval");
        UpdateCheckIntervalHintTextBlock.Text = L("这里只影响软件版本检查，不影响 Mod 更新检查。Mod 更新频率请到左侧“更新”模块设置。", "This only affects app-version checks, not mod update checks. Configure mod update frequency in the Updates section.");
        SettingsPlaceholderTitleTextBlock.Text = L("仓库在线配置", "Repository Online Config");
        SettingsPlaceholderTextBlock.Text = L("在线 Mod 页面需要的来源站点、分类 ID 和备注都集中放在这里管理。", "Manage the source site, category ID, and notes used by the online mod page here.");
        OpenGitHubButton.Content = L("打开 GitHub 仓库", "Open GitHub Repository");
        CheckUpdatesButton.Content = _isCheckingUpdates ? L("检查中...", "Checking...") : L("检查软件更新", "Check App Updates");

        if (string.IsNullOrWhiteSpace(_updateStatusZh) || string.IsNullOrWhiteSpace(_updateStatusEn))
        {
            SetUpdateStatus($"当前版本：{AppVersion}", $"Current version: {AppVersion}");
        }
        else
        {
            UpdateStatusTextBlock.Text = L(_updateStatusZh, _updateStatusEn);
        }

        PopulateUpdateCheckIntervalOptions();
        RefreshUpdateDetailsView();
    }

    private void InitializeOnlineControls()
    {
        OnlineSearchTextBox.PlaceholderText = L("搜索角色名或 Mod 名称", "Search character or mod name");
        OnlineSortComboBox.Items.Clear();
        OnlineSortComboBox.Items.Add(new ComboBoxItem { Content = L("按热度排序", "Sort by hotness"), Tag = OnlineSortMode.Hotness });
        OnlineSortComboBox.Items.Add(new ComboBoxItem { Content = L("按下载量排序", "Sort by downloads"), Tag = OnlineSortMode.Downloads });
        OnlineSortComboBox.Items.Add(new ComboBoxItem { Content = L("按点赞数排序", "Sort by likes"), Tag = OnlineSortMode.Likes });
        OnlineSortComboBox.Items.Add(new ComboBoxItem { Content = L("按查看数排序", "Sort by views"), Tag = OnlineSortMode.Views });
        OnlineSortComboBox.Items.Add(new ComboBoxItem { Content = L("按更新时间排序", "Sort by updated time"), Tag = OnlineSortMode.Updated });
        OnlineSortComboBox.SelectedItem = OnlineSortComboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => item.Tag is OnlineSortMode sortMode && sortMode == _onlineSortMode)
            ?? OnlineSortComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault();
        PopulateOnlineCharacterOptions();
    }

    private void PopulateOnlineCharacterOptions()
    {
        _isApplyingOnlineCharacterSelection = true;
        try
        {
            string[] characters = _onlineKnownCharacters
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            OnlineCharacterComboBox.Items.Clear();
            OnlineCharacterComboBox.Items.Add(new ComboBoxItem
            {
                Content = L("全部角色", "All characters"),
                Tag = string.Empty
            });

            foreach (string character in characters)
            {
                OnlineCharacterComboBox.Items.Add(new ComboBoxItem
                {
                    Content = character,
                    Tag = character
                });
            }

            ComboBoxItem? selectedItem = OnlineCharacterComboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Tag as string ?? string.Empty, _onlineCharacterFilter, StringComparison.OrdinalIgnoreCase));

            OnlineCharacterComboBox.SelectedItem = selectedItem ?? OnlineCharacterComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault();
        }
        finally
        {
            _isApplyingOnlineCharacterSelection = false;
        }
    }

    private void PopulateUpdateCheckIntervalOptions()
    {
        _isApplyingUpdateCheckIntervalSelection = true;
        try
        {
            UpdateCheckIntervalComboBox.Items.Clear();
            UpdateCheckIntervalComboBox.Items.Add(new ComboBoxItem { Content = L("仅手动检查", "Manual only"), Tag = UpdateCheckInterval.Manual });
            UpdateCheckIntervalComboBox.Items.Add(new ComboBoxItem { Content = L("每天启动时检查", "Check daily on startup"), Tag = UpdateCheckInterval.Daily });
            UpdateCheckIntervalComboBox.Items.Add(new ComboBoxItem { Content = L("每 3 天启动时检查", "Check every 3 days on startup"), Tag = UpdateCheckInterval.EveryThreeDays });
            UpdateCheckIntervalComboBox.Items.Add(new ComboBoxItem { Content = L("每周启动时检查", "Check weekly on startup"), Tag = UpdateCheckInterval.Weekly });

            ComboBoxItem? selectedItem = UpdateCheckIntervalComboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => item.Tag is UpdateCheckInterval interval && interval == _updateCheckInterval);

            UpdateCheckIntervalComboBox.SelectedItem = selectedItem ?? UpdateCheckIntervalComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault();
        }
        finally
        {
            _isApplyingUpdateCheckIntervalSelection = false;
        }
    }

    private void RefreshUpdateDetailsView()
    {
        string latestTag = string.IsNullOrWhiteSpace(_latestReleaseTag) ? L("尚未检查", "Not checked yet") : _latestReleaseTag!;
        string latestTitle = string.IsNullOrWhiteSpace(_latestReleaseTitle)
            ? L("最新版本信息会显示在这里", "Latest release details will appear here")
            : NormalizeUiText(_latestReleaseTitle);

        string publishedText = TryParseDateTimeOffset(_latestReleasePublishedAt) is DateTimeOffset publishedAt
            ? publishedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            : L("未知时间", "Unknown time");

        string lastCheckedText = _lastUpdateCheckUtc.HasValue
            ? _lastUpdateCheckUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            : L("尚未检查", "Not checked yet");

        LatestReleaseTitleTextBlock.Text = L($"最新版本：{latestTag}", $"Latest release: {latestTag}");
        LatestReleaseMetaTextBlock.Text = L(
            $"标题：{latestTitle}\n发布时间：{publishedText}\n上次检查：{lastCheckedText}",
            $"Title: {latestTitle}\nPublished: {publishedText}\nLast checked: {lastCheckedText}");
        UpdateNotesTextBlock.Text = string.IsNullOrWhiteSpace(_latestReleaseBody)
            ? L("检查到新版本后，这里会显示 GitHub Release 的更新内容。", "GitHub release notes will appear here after an update check.")
            : NormalizeUiText(_latestReleaseBody);
    }

    private void PopulateModUpdateIntervalOptions()
    {
        _isApplyingModUpdateIntervalSelection = true;
        try
        {
            ModUpdateIntervalComboBox.Items.Clear();
            ModUpdateIntervalComboBox.Items.Add(new ComboBoxItem { Content = L("仅手动检查", "Manual only"), Tag = UpdateCheckInterval.Manual });
            ModUpdateIntervalComboBox.Items.Add(new ComboBoxItem { Content = L("每天启动时检查", "Check daily on startup"), Tag = UpdateCheckInterval.Daily });
            ModUpdateIntervalComboBox.Items.Add(new ComboBoxItem { Content = L("每 3 天启动时检查", "Check every 3 days on startup"), Tag = UpdateCheckInterval.EveryThreeDays });
            ModUpdateIntervalComboBox.Items.Add(new ComboBoxItem { Content = L("每周启动时检查", "Check weekly on startup"), Tag = UpdateCheckInterval.Weekly });

            ComboBoxItem? selectedItem = ModUpdateIntervalComboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => item.Tag is UpdateCheckInterval interval && interval == _modUpdateCheckInterval);

            ModUpdateIntervalComboBox.SelectedItem = selectedItem ?? ModUpdateIntervalComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault();
        }
        finally
        {
            _isApplyingModUpdateIntervalSelection = false;
        }
    }

    private void RefreshUpdatesPane()
    {
        UpdatesTitleTextBlock.Text = L("Mod 更新", "Mod Updates");
        UpdatesSubtitleTextBlock.Text = L("这里会列出已通过在线安装记录下来的 Mod，并支持自动或手动检查它们是否有新版本。", "This page lists mods tracked through online installs and lets you check whether newer versions are available.");
        TrackedModSettingsTitleTextBlock.Text = L("检查方式", "Check Settings");
        TrackedModSettingsHintTextBlock.Text = L("自动检查会在应用启动时按你设定的频率执行，手动检查会立即刷新所有已记录的在线 Mod。", "Automatic checks run on startup at the selected interval. Manual checks refresh every tracked online mod immediately.");
        ModUpdateIntervalLabelTextBlock.Text = L("检查频率", "Check interval");
        ModUpdateIntervalHintTextBlock.Text = L("只会检查已安装并记录了网站来源 ID 的 Mod。", "Only mods that were installed and saved with a source ID will be checked.");
        CheckModUpdatesButton.Content = _isCheckingModUpdates ? L("检查中...", "Checking...") : L("手动检查更新", "Check Now");
        TrackedModsTitleTextBlock.Text = L("已追踪 Mod", "Tracked Mods");

        if (string.IsNullOrWhiteSpace(_modUpdateStatusZh) || string.IsNullOrWhiteSpace(_modUpdateStatusEn))
        {
            SetModUpdateStatus("尚未检查已追踪 Mod 更新。", "Tracked mod updates have not been checked yet.");
        }
        else
        {
            ModUpdateStatusTextBlock.Text = L(_modUpdateStatusZh, _modUpdateStatusEn);
        }

        string lastCheckedText = _lastModUpdateCheckUtc.HasValue
            ? _lastModUpdateCheckUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            : L("尚未检查", "Not checked yet");
        int trackedCount = _trackedModOrigins.Count;
        int updateCount = _trackedModUpdateResults.Count(item => item.HasUpdate);
        TrackedModsSummaryTextBlock.Text = L(
            $"已追踪 {trackedCount} 个 Mod，上次检查：{lastCheckedText}，发现可更新 {updateCount} 个。",
            $"{trackedCount} tracked mods. Last checked: {lastCheckedText}. Updates available: {updateCount}.");

        PopulateModUpdateIntervalOptions();
        TrackedModsListPanel.Children.Clear();

        IEnumerable<TrackedModUpdateResult> orderedResults = _trackedModUpdateResults
            .OrderByDescending(item => item.HasUpdate)
            .ThenBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase);

        if (!orderedResults.Any())
        {
            if (_trackedModOrigins.Count == 0)
            {
                TrackedModsListPanel.Children.Add(CreateInfoCard(
                    L("还没有可追踪的在线 Mod", "No tracked online mods yet"),
                    L("从在线模块下载并解压到仓库后，这里会自动记录来源链接、图片和远程 ID。", "After downloading and extracting from the online browser, this page will automatically track the source link, preview image, and remote ID.")));
            }
            else
            {
                foreach (TrackedModOrigin origin in _trackedModOrigins.Values.OrderBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase))
                {
                    TrackedModsListPanel.Children.Add(CreateTrackedModUpdateCard(new TrackedModUpdateResult
                    {
                        Path = origin.Path,
                        Title = origin.Title,
                        ProfileUrl = origin.ProfileUrl,
                        PreviewUrl = origin.PreviewUrl,
                        ItemId = origin.ItemId,
                        LastKnownUpdatedAt = origin.LastKnownUpdatedAt,
                        LatestUpdatedAt = origin.LastKnownUpdatedAt,
                        HasUpdate = false,
                        StatusTextZh = "尚未检查",
                        StatusTextEn = "Not checked yet"
                    }));
                }
            }

            return;
        }

        foreach (TrackedModUpdateResult result in orderedResults)
        {
            TrackedModsListPanel.Children.Add(CreateTrackedModUpdateCard(result));
        }
    }

    private void LoadShellConfig()
    {
        _repositories.Clear();

        WorkspaceRepository fallbackRepository = new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "默认仓库",
            SourcePath = SourceTextBox.Text ?? string.Empty,
            TargetPath = TargetTextBox.Text ?? string.Empty,
            LauncherPath = LauncherTextBox.Text ?? string.Empty,
            OnlineSourceSite = DefaultOnlineSourceSite,
            OnlineCategoryId = DefaultOnlineCategoryId
        };

        if (!File.Exists(_shellConfigPath))
        {
            _repositories.Add(fallbackRepository);
            _selectedRepositoryId = fallbackRepository.Id;
            SaveShellConfig();
            return;
        }

        try
        {
            BetaShellConfig? config = JsonSerializer.Deserialize<BetaShellConfig>(File.ReadAllText(_shellConfigPath));
            if (config?.Repositories is { Count: > 0 })
            {
                _repositories.AddRange(config.Repositories.Where(repository => !string.IsNullOrWhiteSpace(repository.Id)));
            }

            if (_repositories.Count == 0)
            {
                _repositories.Add(fallbackRepository);
            }

            _selectedRepositoryId = _repositories.Any(repository => repository.Id == config?.SelectedRepositoryId)
                ? config?.SelectedRepositoryId
                : _repositories[0].Id;

            _currentPrimarySection = config?.CurrentPrimarySection switch
            {
                "repository" => PrimarySection.Repository,
                "online" => PrimarySection.Online,
                "updates" => PrimarySection.Updates,
                "settings" => PrimarySection.Settings,
                _ => PrimarySection.Dashboard
            };
            _updateCheckInterval = ParseUpdateCheckInterval(config?.UpdateCheckInterval);
            _lastUpdateCheckUtc = TryParseDateTimeOffset(config?.LastUpdateCheckUtc);
            _modUpdateCheckInterval = ParseUpdateCheckInterval(config?.ModUpdateCheckInterval);
            _lastModUpdateCheckUtc = TryParseDateTimeOffset(config?.LastModUpdateCheckUtc);
            _latestReleaseTag = config?.LatestReleaseTag;
            _latestReleaseTitle = config?.LatestReleaseTitle;
            _latestReleaseBody = config?.LatestReleaseBody;
            _latestReleasePublishedAt = config?.LatestReleasePublishedAt;
            _latestReleaseUrl = config?.LatestReleaseUrl;
        }
        catch
        {
            _repositories.Add(fallbackRepository);
            _selectedRepositoryId = fallbackRepository.Id;
            _currentPrimarySection = PrimarySection.Dashboard;
            _updateCheckInterval = UpdateCheckInterval.Manual;
            _lastUpdateCheckUtc = null;
            _modUpdateCheckInterval = UpdateCheckInterval.Manual;
            _lastModUpdateCheckUtc = null;
            _latestReleaseTag = null;
            _latestReleaseTitle = null;
            _latestReleaseBody = null;
            _latestReleasePublishedAt = null;
            _latestReleaseUrl = null;
        }

        ApplySelectedRepositoryToInputs();
    }

    private void SaveShellConfig()
    {
        try
        {
            var config = new BetaShellConfig
            {
                CurrentPrimarySection = _currentPrimarySection.ToString().ToLowerInvariant(),
                SelectedRepositoryId = _selectedRepositoryId,
                UpdateCheckInterval = _updateCheckInterval.ToString(),
                LastUpdateCheckUtc = _lastUpdateCheckUtc?.ToString("O"),
                ModUpdateCheckInterval = _modUpdateCheckInterval.ToString(),
                LastModUpdateCheckUtc = _lastModUpdateCheckUtc?.ToString("O"),
                LatestReleaseTag = _latestReleaseTag,
                LatestReleaseTitle = _latestReleaseTitle,
                LatestReleaseBody = _latestReleaseBody,
                LatestReleasePublishedAt = _latestReleasePublishedAt,
                LatestReleaseUrl = _latestReleaseUrl,
                Repositories = [.. _repositories]
            };

            string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_shellConfigPath, json);
        }
        catch
        {
            StatusTextBlock.Text = L("保存仓库配置失败。", "Failed to save repository config.");
        }
    }

    private WorkspaceRepository? GetSelectedRepository()
    {
        return _repositories.FirstOrDefault(repository => repository.Id == _selectedRepositoryId);
    }

    private void SaveSelectedRepositoryFromInputs()
    {
        WorkspaceRepository? repository = GetSelectedRepository();
        if (repository is null)
        {
            return;
        }

        repository.SourcePath = SourceTextBox.Text ?? string.Empty;
        repository.TargetPath = TargetTextBox.Text ?? string.Empty;
        repository.LauncherPath = LauncherTextBox.Text ?? string.Empty;
    }

    private void ApplySelectedRepositoryToInputs()
    {
        WorkspaceRepository? repository = GetSelectedRepository();
        if (repository is null)
        {
            return;
        }

        SourceTextBox.Text = repository.SourcePath;
        TargetTextBox.Text = repository.TargetPath;
        LauncherTextBox.Text = repository.LauncherPath;
    }

    private void RefreshSecondaryNavigation()
    {
        SecondaryNavPanel.Children.Clear();

        if (_currentPrimarySection == PrimarySection.Dashboard)
        {
            SecondaryNavPanel.Children.Add(CreateSecondaryNavButton(
                "all-repositories",
                L("全部仓库", "All Repositories"),
                _selectedRepositoryId is null));
        }

        foreach (WorkspaceRepository repository in _repositories)
        {
            SecondaryNavPanel.Children.Add(CreateSecondaryNavButton(
                repository.Id,
                repository.Name,
                repository.Id == _selectedRepositoryId));
        }
    }

    private Button CreateSecondaryNavButton(string key, string label, bool isSelected)
    {
        var button = new Button
        {
            Content = label,
            Tag = key,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            MinHeight = 44,
            Style = (Style)Application.Current.Resources["SecondaryButtonStyle"],
            UseSystemFocusVisuals = false
        };
        button.Click += OnSecondaryNavButtonClicked;
        ApplySecondaryNavButtonVisual(button, isSelected);

        return button;
    }

    private void ApplySecondaryNavButtonVisual(Button button, bool isSelected)
    {
        Brush backgroundBrush = isSelected
            ? (Brush)Application.Current.Resources["AppSecondarySelectedBrush"]
            : (Brush)Application.Current.Resources["AppSecondaryDefaultBrush"];
        Brush borderBrush = isSelected
            ? (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"]
            : (Brush)Application.Current.Resources["AppCardBorderBrush"];
        Brush foregroundBrush = isSelected
            ? (Brush)Application.Current.Resources["AppSecondarySelectedForegroundBrush"]
            : (Brush)Application.Current.Resources["AppSecondaryDefaultForegroundBrush"];
        Brush disabledForegroundBrush = (Brush)Application.Current.Resources["TextFillColorDisabledBrush"];
        Brush disabledBackgroundBrush = (Brush)Application.Current.Resources["AppSecondaryDefaultBrush"];
        Brush disabledBorderBrush = (Brush)Application.Current.Resources["AppCardBorderBrush"];

        button.Background = backgroundBrush;
        button.BorderBrush = borderBrush;
        button.Foreground = foregroundBrush;

        button.Resources["ButtonBackground"] = backgroundBrush;
        button.Resources["ButtonBackgroundPointerOver"] = backgroundBrush;
        button.Resources["ButtonBackgroundPressed"] = backgroundBrush;
        button.Resources["ButtonBackgroundDisabled"] = disabledBackgroundBrush;

        button.Resources["ButtonBorderBrush"] = borderBrush;
        button.Resources["ButtonBorderBrushPointerOver"] = borderBrush;
        button.Resources["ButtonBorderBrushPressed"] = borderBrush;
        button.Resources["ButtonBorderBrushDisabled"] = disabledBorderBrush;

        button.Resources["ButtonForeground"] = foregroundBrush;
        button.Resources["ButtonForegroundPointerOver"] = foregroundBrush;
        button.Resources["ButtonForegroundPressed"] = foregroundBrush;
        button.Resources["ButtonForegroundDisabled"] = disabledForegroundBrush;
    }

    private void ApplyShellState(bool refreshRepository)
    {
        DashboardScrollViewer.Visibility = _currentPrimarySection == PrimarySection.Dashboard ? Visibility.Visible : Visibility.Collapsed;
        WorkspaceScrollViewer.Visibility = _currentPrimarySection == PrimarySection.Repository ? Visibility.Visible : Visibility.Collapsed;
        OnlineScrollViewer.Visibility = _currentPrimarySection == PrimarySection.Online ? Visibility.Visible : Visibility.Collapsed;
        UpdatesScrollViewer.Visibility = _currentPrimarySection == PrimarySection.Updates ? Visibility.Visible : Visibility.Collapsed;
        SettingsScrollViewer.Visibility = _currentPrimarySection == PrimarySection.Settings ? Visibility.Visible : Visibility.Collapsed;

        RefreshPrimaryNavigationVisuals();
        RefreshSecondaryNavigation();
        ApplyLanguage();
        RefreshSettingsPane();
        UpdateShellLayout();

        if (_currentPrimarySection is PrimarySection.Dashboard or PrimarySection.Repository or PrimarySection.Online)
        {
            ApplySelectedRepositoryToInputs();
        }

        if (refreshRepository && _currentPrimarySection == PrimarySection.Repository)
        {
            _ = RefreshListsAsync();
        }

        RefreshDashboard();
        RefreshOnlinePaneV2();
        RefreshUpdatesPane();
        RefreshRepositoryActionButtons();
        SaveShellConfig();
        RootGrid.Focus(FocusState.Programmatic);
    }

    private void RefreshRepositoryActionButtons()
    {
        bool hasSelectedRepository = GetSelectedRepository() is not null;
        bool canModifySelectedRepository = hasSelectedRepository && _selectedRepositoryId is not null;
        bool canDeleteSelectedRepository = canModifySelectedRepository && _repositories.Count > 1;

        RenameRepositoryButton.IsEnabled = canModifySelectedRepository;
        DeleteRepositoryButton.IsEnabled = canDeleteSelectedRepository;
    }

    private void OnWindowSizeChanged(object sender, Microsoft.UI.Xaml.WindowSizeChangedEventArgs args)
    {
        UpdateShellLayout();
    }

    private void UpdateShellLayout()
    {
        double width = Bounds.Width;
        ShellLayoutMode nextMode = width switch
        {
            < 1120 => ShellLayoutMode.Minimal,
            < 1440 => ShellLayoutMode.Compact,
            _ => ShellLayoutMode.Expanded
        };

        if (PrimaryNavColumn is null
            || SecondaryNavColumn is null
            || SecondaryNavBorder is null
            || PrimaryNavBorder is null
            || DashboardNavButton is null
            || RepositoryNavButton is null
            || OnlineNavButton is null
            || UpdatesNavButton is null
            || SettingsNavButton is null)
        {
            return;
        }

        if (_shellLayoutMode == nextMode)
        {
            return;
        }

        _shellLayoutMode = nextMode;

        switch (_shellLayoutMode)
        {
            case ShellLayoutMode.Minimal:
                PrimaryNavColumn.Width = new GridLength(72);
                SecondaryNavColumn.Width = new GridLength(0);
                SecondaryNavBorder.Visibility = Visibility.Collapsed;
                PrimaryNavBorder.Padding = new Thickness(8, 12, 8, 12);
                DashboardNavButton.HorizontalContentAlignment = HorizontalAlignment.Center;
                RepositoryNavButton.HorizontalContentAlignment = HorizontalAlignment.Center;
                OnlineNavButton.HorizontalContentAlignment = HorizontalAlignment.Center;
                UpdatesNavButton.HorizontalContentAlignment = HorizontalAlignment.Center;
                SettingsNavButton.HorizontalContentAlignment = HorizontalAlignment.Center;
                DashboardNavButton.MinHeight = 42;
                RepositoryNavButton.MinHeight = 42;
                OnlineNavButton.MinHeight = 42;
                UpdatesNavButton.MinHeight = 42;
                SettingsNavButton.MinHeight = 42;
                break;
            case ShellLayoutMode.Compact:
                PrimaryNavColumn.Width = new GridLength(72);
                SecondaryNavColumn.Width = new GridLength(224);
                SecondaryNavBorder.Visibility = Visibility.Visible;
                PrimaryNavBorder.Padding = new Thickness(8, 12, 8, 12);
                DashboardNavButton.HorizontalContentAlignment = HorizontalAlignment.Center;
                RepositoryNavButton.HorizontalContentAlignment = HorizontalAlignment.Center;
                OnlineNavButton.HorizontalContentAlignment = HorizontalAlignment.Center;
                UpdatesNavButton.HorizontalContentAlignment = HorizontalAlignment.Center;
                SettingsNavButton.HorizontalContentAlignment = HorizontalAlignment.Center;
                DashboardNavButton.MinHeight = 42;
                RepositoryNavButton.MinHeight = 42;
                OnlineNavButton.MinHeight = 42;
                UpdatesNavButton.MinHeight = 42;
                SettingsNavButton.MinHeight = 42;
                break;
            default:
                PrimaryNavColumn.Width = new GridLength(72);
                SecondaryNavColumn.Width = new GridLength(240);
                SecondaryNavBorder.Visibility = Visibility.Visible;
                PrimaryNavBorder.Padding = new Thickness(8, 12, 8, 12);
                DashboardNavButton.HorizontalContentAlignment = HorizontalAlignment.Center;
                RepositoryNavButton.HorizontalContentAlignment = HorizontalAlignment.Center;
                OnlineNavButton.HorizontalContentAlignment = HorizontalAlignment.Center;
                UpdatesNavButton.HorizontalContentAlignment = HorizontalAlignment.Center;
                SettingsNavButton.HorizontalContentAlignment = HorizontalAlignment.Center;
                DashboardNavButton.MinHeight = 42;
                RepositoryNavButton.MinHeight = 42;
                OnlineNavButton.MinHeight = 42;
                UpdatesNavButton.MinHeight = 42;
                SettingsNavButton.MinHeight = 42;
                break;
        }

        ApplyPrimaryNavigationContent();
    }

    private void ApplyPrimaryNavigationContent()
    {
        DashboardNavButton.Content = CreatePrimaryNavContent("\uE80F", L("仪表板", "Dashboard"));
        RepositoryNavButton.Content = CreatePrimaryNavContent("\uE8B7", L("仓库", "Repositories"));
        OnlineNavButton.Content = CreatePrimaryNavContent("\uE774", L("在线", "Online"));
        UpdatesNavButton.Content = CreatePrimaryNavContent("\uE895", L("更新", "Updates"));
        SettingsNavButton.Content = CreatePrimaryNavContent("\uE713", L("设置", "Settings"));

        ToolTipService.SetToolTip(DashboardNavButton, L("仪表板", "Dashboard"));
        ToolTipService.SetToolTip(RepositoryNavButton, L("仓库", "Repositories"));
        ToolTipService.SetToolTip(OnlineNavButton, L("在线 Mod", "Online Mods"));
        ToolTipService.SetToolTip(UpdatesNavButton, L("更新", "Updates"));
        ToolTipService.SetToolTip(SettingsNavButton, L("设置", "Settings"));

        AutomationProperties.SetName(DashboardNavButton, L("仪表板", "Dashboard"));
        AutomationProperties.SetName(RepositoryNavButton, L("仓库", "Repositories"));
        AutomationProperties.SetName(OnlineNavButton, L("在线 Mod", "Online Mods"));
        AutomationProperties.SetName(UpdatesNavButton, L("鏇存柊", "Updates"));
        AutomationProperties.SetName(SettingsNavButton, L("设置", "Settings"));
    }

    private object CreatePrimaryNavContent(string glyph, string label)
    {
        var icon = new FontIcon
        {
            Glyph = glyph,
            FontSize = 16,
            Opacity = 0.88
        };
        return icon;
    }

    private void RefreshPrimaryNavigationVisuals()
    {
        ApplyPrimaryButtonVisual(DashboardNavButton, _currentPrimarySection == PrimarySection.Dashboard);
        ApplyPrimaryButtonVisual(RepositoryNavButton, _currentPrimarySection == PrimarySection.Repository);
        ApplyPrimaryButtonVisual(OnlineNavButton, _currentPrimarySection == PrimarySection.Online);
        ApplyPrimaryButtonVisual(UpdatesNavButton, _currentPrimarySection == PrimarySection.Updates);
        ApplyPrimaryButtonVisual(SettingsNavButton, _currentPrimarySection == PrimarySection.Settings);
    }

    private void ApplyPrimaryButtonVisual(Button button, bool isSelected)
    {
        Brush backgroundBrush = isSelected
            ? (Brush)Application.Current.Resources["AppNavSelectedBrush"]
            : (Brush)Application.Current.Resources["AppInsetBackgroundBrush"];
        Brush borderBrush = isSelected
            ? (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"]
            : (Brush)Application.Current.Resources["AppCardBorderBrush"];
        Brush foregroundBrush = isSelected
            ? (Brush)Application.Current.Resources["AppNavSelectedForegroundBrush"]
            : (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
        Brush disabledForegroundBrush = (Brush)Application.Current.Resources["TextFillColorDisabledBrush"];
        Brush disabledBackgroundBrush = (Brush)Application.Current.Resources["AppInsetBackgroundBrush"];
        Brush disabledBorderBrush = (Brush)Application.Current.Resources["AppCardBorderBrush"];

        button.Background = backgroundBrush;
        button.BorderBrush = borderBrush;
        button.Foreground = foregroundBrush;

        button.Resources["ButtonBackground"] = backgroundBrush;
        button.Resources["ButtonBackgroundPointerOver"] = backgroundBrush;
        button.Resources["ButtonBackgroundPressed"] = backgroundBrush;
        button.Resources["ButtonBackgroundDisabled"] = disabledBackgroundBrush;

        button.Resources["ButtonBorderBrush"] = borderBrush;
        button.Resources["ButtonBorderBrushPointerOver"] = borderBrush;
        button.Resources["ButtonBorderBrushPressed"] = borderBrush;
        button.Resources["ButtonBorderBrushDisabled"] = disabledBorderBrush;

        button.Resources["ButtonForeground"] = foregroundBrush;
        button.Resources["ButtonForegroundPointerOver"] = foregroundBrush;
        button.Resources["ButtonForegroundPressed"] = foregroundBrush;
        button.Resources["ButtonForegroundDisabled"] = disabledForegroundBrush;
    }

    private void RefreshDashboard()
    {
        if (DashboardRepoCountTextBlock is null
            || DashboardRepositoriesPanel is null
            || DashboardModCountTextBlock is null
            || DashboardReadyCountTextBlock is null)
        {
            return;
        }

        DashboardRepoCountTextBlock.Text = _repositories.Count.ToString();

        int totalMods = 0;
        int readyRepositories = 0;
        DashboardRepositoriesPanel.Children.Clear();

        IEnumerable<WorkspaceRepository> repositoriesToShow = _selectedRepositoryId is null
            ? _repositories
            : _repositories.Where(repository => repository.Id == _selectedRepositoryId);

        foreach (WorkspaceRepository repository in repositoriesToShow)
        {
            RepositorySnapshot snapshot = BuildRepositorySnapshot(repository);
            totalMods += snapshot.ModCount;
            if (snapshot.IsReady)
            {
                readyRepositories++;
            }

            DashboardRepositoriesPanel.Children.Add(CreateDashboardCard(repository, snapshot));
        }

        DashboardModCountTextBlock.Text = totalMods.ToString();
        DashboardReadyCountTextBlock.Text = readyRepositories.ToString();
    }

    private void RefreshOnlinePane()
    {
        WorkspaceRepository? repository = GetSelectedRepository();
        bool hasRepository = repository is not null;
        bool hasOnlineConfig = hasRepository
            && !string.IsNullOrWhiteSpace(repository!.OnlineSourceSite)
            && !string.IsNullOrWhiteSpace(repository.OnlineCategoryId);

        OnlineRepositoryValueTextBlock.Text = hasRepository ? repository!.Name : L("未选择", "Not selected");
        OnlineSourceValueTextBlock.Text = hasRepository
            ? (string.IsNullOrWhiteSpace(repository!.OnlineSourceSite) ? L("未配置", "Not configured") : repository.OnlineSourceSite)
            : L("未选择", "Not selected");
        OnlineReadyValueTextBlock.Text = hasOnlineConfig ? L("已就绪", "Ready") : L("待配置", "Needs setup");
        OnlineCategoryValueTextBlock.Text = hasRepository
            ? (string.IsNullOrWhiteSpace(repository!.OnlineCategoryId) ? L("当前仓库还没有填写分类 ID。", "This repository does not have a category ID yet.") : repository.OnlineCategoryId)
            : L("先从左侧选择一个仓库。", "Select a repository from the left first.");
        OnlineNotesValueTextBlock.Text = hasRepository
            ? (string.IsNullOrWhiteSpace(repository!.Notes) ? L("当前仓库还没有备注，可用来记录目标游戏、站点分类或安装规则。", "No notes yet. Use this area to record the game, source, or install rules.") : NormalizeUiText(repository.Notes))
            : L("在线页面会按当前仓库的来源配置加载内容。", "The online page will load content based on the selected repository configuration.");

        OnlinePreviewPanel.Children.Clear();

        if (!hasRepository)
        {
            OnlinePreviewPanel.Children.Add(CreateOnlinePreviewCard(
                L("等待选择仓库", "Waiting for repository"),
                L("请先在二级侧边栏选择一个仓库，在线资源库会根据这个仓库的来源设置继续加载。", "Select a repository in the secondary navigation first. The online page will continue from that repository's source settings."),
                L("未开始", "Not started")));
            return;
        }

        if (!hasOnlineConfig)
        {
            OnlinePreviewPanel.Children.Add(CreateOnlinePreviewCard(
                L("补充在线来源", "Add an online source"),
                L("先在当前仓库里填写在线来源站点和分类 ID，后面的列表抓取、筛选和安装逻辑都会复用这组配置。", "Fill in the online source site and category ID for this repository first. Later list fetching, filtering, and install flows will reuse that configuration."),
                L("准备中", "Preparing")));
            OnlinePreviewPanel.Children.Add(CreateOnlinePreviewCard(
                L("在线列表准备中", "Preparing"),
                L("当前仓库已经完成基础映射，在线资源列表会在这里展示。", "The current repository mapping is ready. Online resources will be shown here."),
                L("就绪", "Ready")));
            return;
        }

        OnlinePreviewPanel.Children.Add(CreateOnlinePreviewCard(
            L("数据源映射", "Source mapping"),
            $"{repository!.OnlineSourceSite} / {repository.OnlineCategoryId}",
            L("已配置", "Configured")));
        OnlinePreviewPanel.Children.Add(CreateOnlinePreviewCard(
            L("列表抓取", "List fetching"),
            L("下一步会从仓库配置的站点和分类拉取 Mod 列表，并接入搜索、排序和详情预览。", "Next this page will fetch mods from the configured site and category, then add search, sorting, and detail previews."),
            L("下一步", "Next")));
        OnlinePreviewPanel.Children.Add(CreateOnlinePreviewCard(
            L("安装流程", "Install flow"),
            L("后续会把下载压缩包、导入当前仓库、记录来源链接和远程 ID 串成一个完整流程。", "The next step is to connect download, import into the current repository, and source tracking into a single flow."),
            L("待实现", "Planned")));
    }

    private UIElement CreateOnlinePreviewCard(string title, string body, string status)
    {
        Border border = new()
        {
            Style = (Style)Application.Current.Resources["InsetBorderStyle"],
            Padding = new Thickness(14)
        };

        StackPanel stackPanel = new() { Spacing = 6 };
        stackPanel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        stackPanel.Children.Add(new TextBlock
        {
            Text = body,
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["MutedTextStyle"]
        });
        stackPanel.Children.Add(new TextBlock
        {
            Text = status,
            Style = (Style)Application.Current.Resources["CaptionTextStyle"]
        });

        border.Child = stackPanel;
        return border;
    }

    private void RefreshOnlinePaneV2()
    {
        WorkspaceRepository? repository = GetSelectedRepository();
        bool hasRepository = repository is not null;
        string effectiveSource = GetEffectiveOnlineSource(repository);
        string effectiveCategoryId = GetEffectiveOnlineCategoryId(repository);
        bool isSupportedSource = IsGameBananaSource(effectiveSource);
        bool canLoadOnlineMods = hasRepository && isSupportedSource && !string.IsNullOrWhiteSpace(effectiveCategoryId);
        string configKey = hasRepository ? $"{repository!.Id}|{effectiveSource}|{effectiveCategoryId}|page={_onlineCurrentPage}" : string.Empty;

        if (!string.IsNullOrEmpty(_lastLoadedOnlineConfigKey)
            && !string.Equals(_lastLoadedOnlineConfigKey, configKey, StringComparison.Ordinal)
            && !_isLoadingOnlineMods)
        {
            _onlineMods.Clear();
        }

        OnlineRepositoryValueTextBlock.Text = hasRepository ? repository!.Name : L("未选择", "Not selected");
        OnlineSourceValueTextBlock.Text = hasRepository ? effectiveSource : L("未选择", "Not selected");
        OnlineReadyValueTextBlock.Text = canLoadOnlineMods
            ? (_isLoadingOnlineMods ? L("加载中", "Loading") : L("就绪", "Ready"))
            : L("待配置", "Needs setup");
        OnlineCategoryValueTextBlock.Text = hasRepository
            ? effectiveCategoryId
            : L("先从左侧选择一个仓库。", "Select a repository from the left first.");
        OnlineNotesValueTextBlock.Text = hasRepository
            ? (string.IsNullOrWhiteSpace(repository!.Notes)
                ? L("当前仓库还没有备注，可用来记录目标游戏、来源说明或安装规则。", "No notes yet. Use this area to record the target game, source notes, or install rules.")
                : NormalizeUiText(repository.Notes))
            : L("在线页面会按当前仓库的来源配置加载内容。", "The online page will load content based on the selected repository configuration.");

        RefreshOnlineButton.IsEnabled = canLoadOnlineMods && !_isLoadingOnlineMods;
        OnlineStatusTextBlock.Text = string.IsNullOrWhiteSpace(_onlineStatusZh) || string.IsNullOrWhiteSpace(_onlineStatusEn)
            ? L("点击刷新在线列表后，会从 GameBanana 拉取当前分类下的最新 Mod。", "Click Refresh Online List to fetch the latest mods for the current gameid from GameBanana.")
            : L(_onlineStatusZh!, _onlineStatusEn!);
        List<OnlineModCard> filteredMods = canLoadOnlineMods ? GetFilteredOnlineMods() : [];
        List<OnlineModCard> visibleMods = GetVisibleOnlineMods(filteredMods);
        OnlinePaginationTextBlock.Text = canLoadOnlineMods
            ? L(
                $"第 {_onlineCurrentPage} / {_onlineTotalPages} 页，共 {_onlineTotalCount} 个 Mod",
                $"Page {_onlineCurrentPage} / {_onlineTotalPages}, {_onlineTotalCount} mods total")
            : L("等待在线配置", "Waiting for online config");
        OnlinePrevPageButton.IsEnabled = canLoadOnlineMods && !_isLoadingOnlineMods && _onlineCurrentPage > 1;
        OnlineNextPageButton.IsEnabled = canLoadOnlineMods && !_isLoadingOnlineMods && _onlineCurrentPage < _onlineTotalPages;
        OnlineSearchTextBox.PlaceholderText = L("搜索角色名或 Mod 名称", "Search character or mod name");
        OnlineSearchTextBox.IsEnabled = canLoadOnlineMods && !_isLoadingOnlineMods;
        OnlineCharacterComboBox.IsEnabled = canLoadOnlineMods && !_isLoadingOnlineMods;
        OnlineSortComboBox.IsEnabled = canLoadOnlineMods && !_isLoadingOnlineMods;

        OnlinePreviewPanel.Children.Clear();

        if (!hasRepository)
        {
            OnlinePreviewPanel.Children.Add(CreateOnlinePreviewCard(
                L("等待选择仓库", "Waiting for repository"),
                L("请先在左侧选择一个仓库，在线资源库会根据这个仓库的来源配置继续加载。", "Select a repository from the left first. The online page will continue from that repository's source settings."),
                L("未开始", "Not started")));
            return;
        }

        if (!isSupportedSource)
        {
            OnlinePreviewPanel.Children.Add(CreateOnlinePreviewCard(
                L("来源暂不支持", "Source not supported yet"),
                L("当前在线页面优先支持 GameBanana。你可以把在线来源改成 GameBanana，或继续保留这个字段等待后续站点接入。", "This online page currently supports GameBanana first. Change the source to GameBanana or keep the field for later site integrations."),
                effectiveSource));
            return;
        }

        if (_onlineMods.Count == 0)
        {
            OnlinePreviewPanel.Children.Add(CreateOnlinePreviewCard(
                L("准备加载在线列表", "Ready to load real data"),
                L("这里会根据当前仓库里的 GameBanana 配置拉取真实 Mod 列表，并提供查看详情、打开页面和下载解压入口。", "This page now uses the repository's GameBanana config to load real mods. The first pass shows title, author, updated time, page link, and download entry points."),
                _isLoadingOnlineMods ? L("加载中", "Loading") : L("可以刷新", "Ready to refresh")));
        }
        else
        {
            if (visibleMods.Count == 0)
            {
                OnlinePreviewPanel.Children.Add(CreateOnlinePreviewCard(
                    L("没有匹配的皮肤 Mod", "No matching skin mods"),
                    L("当前页只显示 Skins 分类。你可以翻页，或者尝试用角色名和 Mod 名称搜索。", "This page only shows the Skins category. Try another page, or search by character and mod name."),
                    L("筛选结果为空", "No results")));
            }
            else
            {
                foreach (OnlineModCard mod in visibleMods)
                {
                    OnlinePreviewPanel.Children.Add(CreateOnlineModCardV2(mod));
                }
            }
        }

        if (_currentPrimarySection == PrimarySection.Online && canLoadOnlineMods && configKey != _lastLoadedOnlineConfigKey && !_isLoadingOnlineMods)
        {
            _ = LoadOnlineModsAsync(forceReload: true);
        }
    }

    private bool IsGameBananaSource(string? source)
    {
        return string.IsNullOrWhiteSpace(source)
            || source.Contains("gamebanana", StringComparison.OrdinalIgnoreCase);
    }

    private List<OnlineModCard> GetFilteredOnlineMods()
    {
        IEnumerable<OnlineModCard> query = _onlineMods
            .Where(mod => string.Equals(mod.RootCategoryName, "Skins", StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(_onlineCharacterFilter))
        {
            query = query.Where(mod => string.Equals(mod.CharacterName, _onlineCharacterFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(_onlineSearchText))
        {
            query = query.Where(mod =>
                mod.Title.Contains(_onlineSearchText, StringComparison.OrdinalIgnoreCase)
                || mod.CharacterName.Contains(_onlineSearchText, StringComparison.OrdinalIgnoreCase));
        }

        query = _onlineSortMode switch
        {
            OnlineSortMode.Downloads => query.OrderByDescending(mod => mod.Downloads).ThenByDescending(mod => mod.HotnessScore),
            OnlineSortMode.Likes => query.OrderByDescending(mod => mod.Likes).ThenByDescending(mod => mod.HotnessScore),
            OnlineSortMode.Views => query.OrderByDescending(mod => mod.Views).ThenByDescending(mod => mod.HotnessScore),
            OnlineSortMode.Updated => query.OrderByDescending(mod => mod.UpdatedAt).ThenByDescending(mod => mod.HotnessScore),
            _ => query.OrderByDescending(mod => mod.HotnessScore).ThenByDescending(mod => mod.Downloads)
        };

        return query.ToList();
    }

    private List<OnlineModCard> GetVisibleOnlineMods(List<OnlineModCard> filteredMods)
    {
        return filteredMods
            .ToList();
    }

    private string GetEffectiveOnlineSource(WorkspaceRepository? repository)
    {
        if (repository is null)
        {
            return L("未选择", "Not selected");
        }

        return string.IsNullOrWhiteSpace(repository.OnlineSourceSite)
            ? DefaultOnlineSourceSite
            : repository.OnlineSourceSite.Trim();
    }

    private string GetEffectiveOnlineCategoryId(WorkspaceRepository? repository)
    {
        if (repository is null)
        {
            return string.Empty;
        }

        return string.IsNullOrWhiteSpace(repository.OnlineCategoryId)
            ? DefaultOnlineCategoryId
            : repository.OnlineCategoryId.Trim();
    }

    private void SetOnlineStatus(string zh, string en)
    {
        _onlineStatusZh = zh;
        _onlineStatusEn = en;
        OnlineStatusTextBlock.Text = L(zh, en);
    }

    private void SetOnlineDownloadProgress(bool isVisible, double percent, string zh, string en)
    {
        OnlineDownloadProgressPanel.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        OnlineDownloadProgressBar.Value = Math.Max(0, Math.Min(100, percent));
        OnlineDownloadProgressTextBlock.Text = L(zh, en);
    }

    private async void OnRefreshOnlineModsClicked(object sender, RoutedEventArgs e)
    {
        await LoadOnlineModsAsync(forceReload: true);
    }

    private async Task LoadOnlineModsAsync(bool forceReload)
    {
        WorkspaceRepository? repository = GetSelectedRepository();
        if (repository is null)
        {
            return;
        }

        string source = GetEffectiveOnlineSource(repository);
        string categoryId = GetEffectiveOnlineCategoryId(repository);
        if (!IsGameBananaSource(source) || string.IsNullOrWhiteSpace(categoryId))
        {
            SetOnlineStatus("当前仓库还没有可用的在线来源配置。", "The current repository does not have a usable online source configuration yet.");
            RefreshOnlinePaneV2();
            return;
        }

        string configKey = $"{repository.Id}|{source}|{categoryId}|page={_onlineCurrentPage}";
        if (!forceReload && string.Equals(configKey, _lastLoadedOnlineConfigKey, StringComparison.Ordinal) && _onlineMods.Count > 0)
        {
            RefreshOnlinePaneV2();
            return;
        }

        _isLoadingOnlineMods = true;
        RefreshOnlineButton.Content = L("加载中...", "Loading...");
        RefreshOnlineButton.IsEnabled = false;
        SetOnlineStatus($"正在从 GameBanana 读取分类 {categoryId} 的在线 Mod 列表...", $"Loading online mods from GameBanana for category {categoryId}...");
        RefreshOnlinePaneV2();

        try
        {
            OnlineCategoryPageResult pageResult = await FetchGameBananaCategoryPageAsync(categoryId, _onlineCurrentPage);
            _onlineMods.Clear();
            _onlineMods.AddRange(pageResult.Mods);
            _onlineKnownCharacters.Clear();
            foreach (string character in _onlineMods
                         .Select(mod => mod.CharacterName)
                         .Where(name => !string.IsNullOrWhiteSpace(name)))
            {
                _onlineKnownCharacters.Add(character);
            }
            PopulateOnlineCharacterOptions();
            _onlineTotalCount = pageResult.TotalCount;
            _onlineTotalPages = Math.Max(1, pageResult.TotalPages);

            if (pageResult.Mods.Count == 0)
            {
                _lastLoadedOnlineConfigKey = configKey;
                SetOnlineStatus("GameBanana 已连接，但当前分类没有返回可用的皮肤 Mod。", "GameBanana responded, but the current category returned no skin mods.");
                return;
            }

            _lastLoadedOnlineConfigKey = configKey;

            SetOnlineStatus(
                $"已载入分类第 {_onlineCurrentPage} 页，共 {_onlineTotalCount} 个皮肤 Mod。每页显示 20 个，可继续往后翻页。",
                $"Loaded category page {_onlineCurrentPage}. There are {_onlineTotalCount} skin mods in total, with 20 items per page.");
        }
        catch (Exception ex)
        {
            _onlineMods.Clear();
            _onlineKnownCharacters.Clear();
            _lastLoadedOnlineConfigKey = null;
            SetOnlineStatus("读取在线 Mod 失败，请稍后重试。", "Failed to load online mods. Please try again later.");
            await ShowMessageAsync(
                L("读取 GameBanana 列表失败：", "Failed to load the GameBanana list: ") + ex.Message,
                L("在线加载失败", "Online load failed"));
        }
        finally
        {
            _isLoadingOnlineMods = false;
            RefreshOnlineButton.Content = L("刷新在线列表", "Refresh Online List");
            RefreshOnlinePaneV2();
        }
    }

    private async Task<List<OnlineModCard>> FetchAllGameBananaSkinModsAsync(string gameId)
    {
        List<OnlineModCard> allMods = [];

        for (int page = 1; page <= OnlineRawPageLimit; page++)
        {
            OnlineModPageResult pageResult = await FetchGameBananaModIdsAsync(gameId, page);
            if (pageResult.ModIds.Count == 0)
            {
                break;
            }

            Task<OnlineModCard?>[] tasks = pageResult.ModIds
                .Select(FetchGameBananaModCardAsyncV2)
                .ToArray();

            OnlineModCard?[] results = await Task.WhenAll(tasks);
            allMods.AddRange(results
                .OfType<OnlineModCard>()
                .Where(mod => string.Equals(mod.RootCategoryName, "Skins", StringComparison.OrdinalIgnoreCase)));

            if (pageResult.ModIds.Count < OnlineRawFetchPageSize)
            {
                break;
            }
        }

        return allMods
            .GroupBy(mod => mod.ItemId)
            .Select(group => group.First())
            .ToList();
    }

    private async Task<OnlineCategoryPageResult> FetchGameBananaCategoryPageAsync(string categoryId, int page)
    {
        string requestUrl = $"https://gamebanana.com/apiv11/Mod/Index?_aFilters%5BGeneric_Category%5D={Uri.EscapeDataString(categoryId)}&_nPerpage={OnlineDisplayPageSize}&_nPage={page}";
        using HttpResponseMessage response = await _httpClient.GetAsync(requestUrl);
        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync();
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        List<OnlineModCard> mods = [];
        if (root.ValueKind != JsonValueKind.Object)
        {
            return new OnlineCategoryPageResult(mods, 0, page, OnlineDisplayPageSize);
        }

        JsonElement metadata = root.TryGetProperty("_aMetadata", out JsonElement metadataElement) ? metadataElement : default;
        int totalCount = TryGetInt32Property(metadata, "_nRecordCount");
        int pageSize = Math.Max(1, TryGetInt32Property(metadata, "_nPerpage"));

        if (!root.TryGetProperty("_aRecords", out JsonElement recordsElement) || recordsElement.ValueKind != JsonValueKind.Array)
        {
            return new OnlineCategoryPageResult(mods, totalCount, page, pageSize);
        }

        foreach (JsonElement record in recordsElement.EnumerateArray())
        {
            OnlineModCard? mod = ParseGameBananaCategoryMod(record);
            if (mod is not null)
            {
                mods.Add(mod);
            }
        }

        if (mods.Count > 0)
        {
            Task<OnlineModCard?>[] enrichTasks = mods
                .Select(mod => mod.Downloads > 0 ? Task.FromResult<OnlineModCard?>(mod) : FetchGameBananaModCardAsyncV2(mod.ItemId))
                .ToArray();

            OnlineModCard?[] enrichedMods = await Task.WhenAll(enrichTasks);
            mods = enrichedMods
                .Select((enriched, index) => MergeOnlineModCard(mods[index], enriched))
                .ToList();
        }

        return new OnlineCategoryPageResult(mods, totalCount, page, pageSize);
    }

    private static OnlineModCard MergeOnlineModCard(OnlineModCard baseCard, OnlineModCard? enrichedCard)
    {
        if (enrichedCard is null)
        {
            return baseCard;
        }

        baseCard.Downloads = enrichedCard.Downloads > 0 ? enrichedCard.Downloads : baseCard.Downloads;
        baseCard.Likes = enrichedCard.Likes > 0 ? enrichedCard.Likes : baseCard.Likes;
        baseCard.Views = enrichedCard.Views > 0 ? enrichedCard.Views : baseCard.Views;
        baseCard.HotnessScore = CalculateOnlineHotness(baseCard.Likes, baseCard.Views, baseCard.Downloads);
        baseCard.FileSizeBytes = enrichedCard.FileSizeBytes > 0 ? enrichedCard.FileSizeBytes : baseCard.FileSizeBytes;
        baseCard.DownloadUrl = !string.IsNullOrWhiteSpace(enrichedCard.DownloadUrl) ? enrichedCard.DownloadUrl : baseCard.DownloadUrl;
        baseCard.ProfileUrl = !string.IsNullOrWhiteSpace(enrichedCard.ProfileUrl) ? enrichedCard.ProfileUrl : baseCard.ProfileUrl;
        baseCard.PreviewUrl = !string.IsNullOrWhiteSpace(enrichedCard.PreviewUrl) ? enrichedCard.PreviewUrl : baseCard.PreviewUrl;
        baseCard.HasUpdates = enrichedCard.HasUpdates;
        baseCard.UpdatedAt = enrichedCard.UpdatedAt > baseCard.UpdatedAt ? enrichedCard.UpdatedAt : baseCard.UpdatedAt;
        if (!string.IsNullOrWhiteSpace(enrichedCard.Author))
        {
            baseCard.Author = enrichedCard.Author;
        }

        if (!string.IsNullOrWhiteSpace(enrichedCard.CharacterName))
        {
            baseCard.CharacterName = enrichedCard.CharacterName;
        }

        if (!string.IsNullOrWhiteSpace(enrichedCard.RootCategoryName))
        {
            baseCard.RootCategoryName = enrichedCard.RootCategoryName;
        }

        return baseCard;
    }

    private OnlineModCard? ParseGameBananaCategoryMod(JsonElement record)
    {
        if (record.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        int itemId = TryGetInt32Property(record, "_idRow");
        if (itemId <= 0)
        {
            return null;
        }

        string title = TryGetStringProperty(record, "_sName") ?? $"Mod {itemId}";
        string profileUrl = TryGetStringProperty(record, "_sProfileUrl") ?? $"https://gamebanana.com/mods/{itemId}";
        string author = string.Empty;
        if (record.TryGetProperty("_aSubmitter", out JsonElement submitterElement))
        {
            author = TryGetStringProperty(submitterElement, "_sName") ?? string.Empty;
        }

        string rootCategoryName = string.Empty;
        if (record.TryGetProperty("_aRootCategory", out JsonElement rootCategoryElement))
        {
            rootCategoryName = TryGetStringProperty(rootCategoryElement, "_sName") ?? string.Empty;
        }

        string characterName = string.Empty;
        if (record.TryGetProperty("_aSubCategory", out JsonElement subCategoryElement))
        {
            characterName = TryGetStringProperty(subCategoryElement, "_sName") ?? string.Empty;
        }

        string? previewUrl = null;
        if (record.TryGetProperty("_aPreviewMedia", out JsonElement previewMediaElement)
            && previewMediaElement.ValueKind == JsonValueKind.Object
            && previewMediaElement.TryGetProperty("_aImages", out JsonElement imagesElement)
            && imagesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement imageElement in imagesElement.EnumerateArray())
            {
                string? baseUrl = TryGetStringProperty(imageElement, "_sBaseUrl");
                string? fileName = TryGetStringProperty(imageElement, "_sFile220")
                    ?? TryGetStringProperty(imageElement, "_sFile530")
                    ?? TryGetStringProperty(imageElement, "_sFile100")
                    ?? TryGetStringProperty(imageElement, "_sFile");
                if (!string.IsNullOrWhiteSpace(baseUrl) && !string.IsNullOrWhiteSpace(fileName))
                {
                    previewUrl = $"{baseUrl}/{fileName}";
                    break;
                }
            }
        }

        long updatedEpoch = TryGetInt64Property(record, "_tsDateModified");
        if (updatedEpoch <= 0)
        {
            updatedEpoch = TryGetInt64Property(record, "_tsDateUpdated");
        }
        if (updatedEpoch <= 0)
        {
            updatedEpoch = TryGetInt64Property(record, "_tsDateAdded");
        }

        int likes = TryGetInt32Property(record, "_nLikeCount");
        int views = TryGetInt32Property(record, "_nViewCount");
        int downloads = TryGetInt32Property(record, "_nDownloadCount");
        DateTimeOffset updatedAt = updatedEpoch > 0 ? DateTimeOffset.FromUnixTimeSeconds(updatedEpoch).ToLocalTime() : DateTimeOffset.Now;

        return new OnlineModCard
        {
            ItemId = itemId,
            Title = title,
            CharacterName = string.IsNullOrWhiteSpace(characterName) ? L("未分类角色", "Uncategorized") : characterName,
            RootCategoryName = string.IsNullOrWhiteSpace(rootCategoryName) ? "Skins" : rootCategoryName,
            Author = string.IsNullOrWhiteSpace(author) ? L("未知作者", "Unknown author") : author,
            Likes = likes,
            Views = views,
            Downloads = downloads,
            HotnessScore = CalculateOnlineHotness(likes, views, downloads),
            PreviewUrl = previewUrl,
            ProfileUrl = profileUrl,
            DownloadUrl = $"https://gamebanana.com/mods/download/{itemId}",
            FileSizeBytes = 0,
            HasUpdates = false,
            UpdatedAt = updatedAt
        };
    }

    private async Task<OnlineModPageResult> FetchGameBananaModIdsAsync(string gameId, int page)
    {
        string requestUrl = $"https://api.gamebanana.com/Core/List/New?page={page}&itemtype=Mod&gameid={Uri.EscapeDataString(gameId)}&include_updated=true&perpage={OnlineRawFetchPageSize}&format=json_min";
        using HttpResponseMessage response = await _httpClient.GetAsync(requestUrl);
        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync();
        using JsonDocument document = JsonDocument.Parse(json);

        List<int> modIds = [];
        int totalCount = 0;
        JsonElement root = document.RootElement;
        JsonElement valueElement = root;
        if (root.ValueKind == JsonValueKind.Object)
        {
            totalCount = TryGetInt32Property(root, "Count");
            if (!root.TryGetProperty("value", out valueElement))
            {
                return new OnlineModPageResult(modIds, totalCount, page, OnlineRawFetchPageSize);
            }
        }

        if (valueElement.ValueKind != JsonValueKind.Array)
        {
            return new OnlineModPageResult(modIds, totalCount, page, OnlineRawFetchPageSize);
        }

        foreach (JsonElement item in valueElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Array || item.GetArrayLength() < 2)
            {
                continue;
            }

            string? itemType = item[0].GetString();
            if (!string.Equals(itemType, "Mod", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (item[1].TryGetInt32(out int modId))
            {
                modIds.Add(modId);
            }
        }

        if (totalCount <= 0)
        {
            // GameBanana may omit Count in json_min responses.
            // When a full page comes back, keep the next-page navigation enabled.
            totalCount = modIds.Count == OnlineRawFetchPageSize
                ? (page * OnlineRawFetchPageSize) + 1
                : ((page - 1) * OnlineRawFetchPageSize) + modIds.Count;
        }

        return new OnlineModPageResult(modIds, totalCount, page, OnlineRawFetchPageSize);
    }

    private async Task<OnlineModCard?> FetchGameBananaModCardAsync(int itemId)
    {
        string fields = string.Join(",",
            "name",
            "Category().name",
            "RootCategory().name",
            "likes",
            "views",
            "downloads",
            "Owner().name",
            "mdate",
            "Preview().sSubFeedImageUrl()",
            "Url().sProfileUrl()",
            "Url().sDownloadUrl()",
            "Updates().bSubmissionHasUpdates()");
        string requestUrl = $"https://api.gamebanana.com/Core/Item/Data?itemtype=Mod&itemid={itemId}&fields={Uri.EscapeDataString(fields)}&format=json_min";
        using HttpResponseMessage response = await _httpClient.GetAsync(requestUrl);
        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync();
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        JsonElement dataElement = root;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("value", out JsonElement wrappedValue))
        {
            dataElement = wrappedValue;
        }

        string title = TryGetGameBananaString(dataElement, 0, "name") ?? $"Mod {itemId}";
        string author = TryGetGameBananaString(dataElement, 1, "Owner().name") ?? L("未知作者", "Unknown author");
        long updatedEpoch = TryGetGameBananaInt64(dataElement, 3, "mdate");
        string? previewUrl = TryGetGameBananaString(dataElement, 4, "Preview().sSubFeedImageUrl()");
        string profileUrl = TryGetGameBananaString(dataElement, 5, "Url().sProfileUrl()") ?? $"https://gamebanana.com/mods/{itemId}";
        string? downloadUrl = TryGetGameBananaString(dataElement, 6, "Url().sDownloadUrl()");
        bool hasUpdates = TryGetGameBananaBool(dataElement, 7, "Updates().bSubmissionHasUpdates()");
        DateTimeOffset updatedAt = updatedEpoch > 0 ? DateTimeOffset.FromUnixTimeSeconds(updatedEpoch).ToLocalTime() : DateTimeOffset.Now;

        return new OnlineModCard
        {
            ItemId = itemId,
            Title = title,
            Author = author,
            PreviewUrl = previewUrl,
            ProfileUrl = profileUrl,
            DownloadUrl = downloadUrl,
            HasUpdates = hasUpdates,
            UpdatedAt = updatedAt
        };
    }

    private async Task<OnlineModCard?> FetchGameBananaModCardAsyncV2(int itemId)
    {
        string fields = string.Join(",",
            "name",
            "Category().name",
            "RootCategory().name",
            "likes",
            "views",
            "downloads",
            "Owner().name",
            "mdate",
            "Preview().sSubFeedImageUrl()",
            "Url().sProfileUrl()",
            "Url().sDownloadUrl()",
            "Files().aFiles()",
            "Updates().bSubmissionHasUpdates()");
        string requestUrl = $"https://api.gamebanana.com/Core/Item/Data?itemtype=Mod&itemid={itemId}&fields={Uri.EscapeDataString(fields)}&return_keys=true&format=json_min";
        using HttpResponseMessage response = await _httpClient.GetAsync(requestUrl);
        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync();
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        JsonElement dataElement = root;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("value", out JsonElement wrappedValue))
        {
            dataElement = wrappedValue;
        }

        string title = TryGetStringProperty(dataElement, "name") ?? $"Mod {itemId}";
        string characterName = TryGetStringProperty(dataElement, "Category().name") ?? L("未分类角色", "Uncategorized");
        string rootCategoryName = TryGetStringProperty(dataElement, "RootCategory().name") ?? string.Empty;
        int likes = TryGetInt32Property(dataElement, "likes");
        int views = TryGetInt32Property(dataElement, "views");
        int downloads = TryGetInt32Property(dataElement, "downloads");
        string author = TryGetStringProperty(dataElement, "Owner().name") ?? L("未知作者", "Unknown author");
        long updatedEpoch = TryGetInt64Property(dataElement, "mdate");
        string? previewUrl = TryGetStringProperty(dataElement, "Preview().sSubFeedImageUrl()");
        string profileUrl = TryGetStringProperty(dataElement, "Url().sProfileUrl()") ?? $"https://gamebanana.com/mods/{itemId}";
        string? fallbackDownloadUrl = TryGetStringProperty(dataElement, "Url().sDownloadUrl()");
        bool hasUpdates = TryGetBoolProperty(dataElement, "Updates().bSubmissionHasUpdates()");

        JsonElement filesElement = default;
        string? downloadUrl = fallbackDownloadUrl;
        long fileSizeBytes = 0;
        if (dataElement.ValueKind == JsonValueKind.Object && dataElement.TryGetProperty("Files().aFiles()", out filesElement))
        {
            (downloadUrl, fileSizeBytes) = TryGetPrimaryGameBananaFile(filesElement, fallbackDownloadUrl);
            if (downloads <= 0)
            {
                downloads = SumGameBananaFileDownloads(filesElement);
            }
        }

        DateTimeOffset updatedAt = updatedEpoch > 0 ? DateTimeOffset.FromUnixTimeSeconds(updatedEpoch).ToLocalTime() : DateTimeOffset.Now;

        return new OnlineModCard
        {
            ItemId = itemId,
            Title = title,
            CharacterName = characterName,
            RootCategoryName = rootCategoryName,
            Author = author,
            Likes = likes,
            Views = views,
            Downloads = downloads,
            HotnessScore = CalculateOnlineHotness(likes, views, downloads),
            PreviewUrl = previewUrl,
            ProfileUrl = profileUrl,
            DownloadUrl = downloadUrl,
            FileSizeBytes = fileSizeBytes,
            HasUpdates = hasUpdates,
            UpdatedAt = updatedAt
        };
    }

    private static double CalculateOnlineHotness(int likes, int views, int downloads)
    {
        double downloadPart = Math.Log10(downloads + 1) * 0.55d;
        double likePart = Math.Log10(likes + 1) * 0.30d;
        double viewPart = Math.Log10(views + 1) * 0.15d;
        double weighted = downloadPart + likePart + viewPart;
        double normalized = Math.Min(10d, weighted * 2d);
        return Math.Round(normalized, 2, MidpointRounding.AwayFromZero);
    }

    private static int SumGameBananaFileDownloads(JsonElement filesElement)
    {
        if (filesElement.ValueKind != JsonValueKind.Object)
        {
            return 0;
        }

        int totalDownloads = 0;
        foreach (JsonProperty property in filesElement.EnumerateObject())
        {
            JsonElement fileElement = property.Value;
            if (fileElement.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            totalDownloads += TryGetInt32Property(fileElement, "_nDownloadCount");
        }

        return totalDownloads;
    }

    private static (string? DownloadUrl, long FileSizeBytes) TryGetPrimaryGameBananaFile(JsonElement filesElement, string? fallbackDownloadUrl)
    {
        if (filesElement.ValueKind != JsonValueKind.Object)
        {
            return (fallbackDownloadUrl, 0);
        }

        foreach (JsonProperty fileProperty in filesElement.EnumerateObject())
        {
            JsonElement fileElement = fileProperty.Value;
            if (fileElement.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            bool isArchived = TryGetBoolProperty(fileElement, "_bIsArchived");
            string? fileDownloadUrl = TryGetStringProperty(fileElement, "_sDownloadUrl");
            long fileSizeBytes = TryGetInt64Property(fileElement, "_nFilesize");
            if (!isArchived && !string.IsNullOrWhiteSpace(fileDownloadUrl))
            {
                return (fileDownloadUrl, fileSizeBytes);
            }
        }

        return (fallbackDownloadUrl, 0);
    }

    private static string? TryGetStringProperty(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (element.TryGetProperty(propertyName, out JsonElement propertyElement))
        {
            return propertyElement.GetString();
        }

        return null;
    }

    private static long TryGetInt64Property(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out JsonElement propertyElement))
        {
            return 0;
        }

        return TryReadInt64(propertyElement);
    }

    private static int TryGetInt32Property(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out JsonElement propertyElement))
        {
            return 0;
        }

        long value = TryReadInt64(propertyElement);
        if (value > int.MaxValue)
        {
            return int.MaxValue;
        }

        if (value < int.MinValue)
        {
            return int.MinValue;
        }

        return (int)value;
    }

    private static long TryReadInt64(JsonElement element)
    {
        if (element.TryGetInt64(out long int64Value))
        {
            return int64Value;
        }

        if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out double doubleValue))
        {
            return (long)Math.Round(doubleValue, MidpointRounding.AwayFromZero);
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            string? text = element.GetString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                string normalized = text.Replace(",", string.Empty).Trim();
                if (long.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out long parsedLong))
                {
                    return parsedLong;
                }

                if (double.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsedDouble))
                {
                    return (long)Math.Round(parsedDouble, MidpointRounding.AwayFromZero);
                }
            }
        }

        return 0;
    }

    private static bool TryGetBoolProperty(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        return element.TryGetProperty(propertyName, out JsonElement propertyElement)
            && propertyElement.ValueKind is JsonValueKind.True or JsonValueKind.False
            && propertyElement.GetBoolean();
    }

    private static string? TryGetGameBananaString(JsonElement element, int index, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Array && element.GetArrayLength() > index)
        {
            JsonElement item = element[index];
            return item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString();
        }

        return TryGetStringProperty(element, propertyName);
    }

    private static long TryGetGameBananaInt64(JsonElement element, int index, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Array && element.GetArrayLength() > index)
        {
            JsonElement item = element[index];
            if (item.TryGetInt64(out long value))
            {
                return value;
            }
        }

        return TryGetInt64Property(element, propertyName);
    }

    private static bool TryGetGameBananaBool(JsonElement element, int index, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Array && element.GetArrayLength() > index)
        {
            JsonElement item = element[index];
            if (item.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return item.GetBoolean();
            }
        }

        return TryGetBoolProperty(element, propertyName);
    }

    private async void OnOnlinePrevPageClicked(object sender, RoutedEventArgs e)
    {
        if (_onlineCurrentPage <= 1 || _isLoadingOnlineMods)
        {
            return;
        }

        _onlineCurrentPage--;
        await LoadOnlineModsAsync(forceReload: false);
    }

    private async void OnOnlineNextPageClicked(object sender, RoutedEventArgs e)
    {
        if (_onlineCurrentPage >= _onlineTotalPages || _isLoadingOnlineMods)
        {
            return;
        }

        _onlineCurrentPage++;
        await LoadOnlineModsAsync(forceReload: false);
    }

    private void OnOnlineSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        _onlineSearchText = OnlineSearchTextBox.Text?.Trim() ?? string.Empty;
        RefreshOnlinePaneV2();
    }

    private void OnOnlineCharacterSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isApplyingOnlineCharacterSelection)
        {
            return;
        }

        _onlineCharacterFilter = (OnlineCharacterComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? string.Empty;
        RefreshOnlinePaneV2();
    }

    private void OnOnlineSortSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (OnlineSortComboBox.SelectedItem is ComboBoxItem { Tag: OnlineSortMode sortMode })
        {
            _onlineSortMode = sortMode;
            RefreshOnlinePaneV2();
        }
    }

    private async Task ShowOnlineModDetailsAsync(OnlineModCard mod)
    {
        SetBusyState(true);
        try
        {
            _activeOnlineDetailMod = mod;
            OnlineModDetails details = await GetOnlineModDetailsAsync(mod);
            OnlineModDetails displayDetails = await TryTranslateOnlineModDetailsAsync(details);
            PopulateOnlineDetailPane(mod, details, displayDetails);
            OnlineDetailsSplitView.IsPaneOpen = true;
        }
        catch (Exception ex)
        {
            await ShowMessageAsync(
                L("打开 Mod 详情失败：", "Failed to open mod details: ") + ex.Message,
                L("详情加载失败", "Details load failed"));
        }
        finally
        {
            SetBusyState(false);
        }
    }

    private async Task<OnlineModDetails> GetOnlineModDetailsAsync(OnlineModCard mod)
    {
        if (_onlineModDetailsCache.TryGetValue(mod.ItemId, out OnlineModDetails? cached))
        {
            return cached;
        }

        OnlineModDetails details = await FetchOnlineModDetailsAsync(mod);
        _onlineModDetailsCache[mod.ItemId] = details;
        return details;
    }

    private async Task<OnlineModDetails> FetchOnlineModDetailsAsync(OnlineModCard mod)
    {
        string fields = string.Join(",",
            "name",
            "description",
            "text",
            "screenshots",
            "Preview().sSubFeedImageUrl()");
        string requestUrl = $"https://api.gamebanana.com/Core/Item/Data?itemtype=Mod&itemid={mod.ItemId}&fields={Uri.EscapeDataString(fields)}&return_keys=true&format=json_min";
        using HttpResponseMessage response = await _httpClient.GetAsync(requestUrl);
        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync();
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        JsonElement dataElement = root;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("value", out JsonElement wrappedValue))
        {
            dataElement = wrappedValue;
        }

        string summary = TryGetStringProperty(dataElement, "description") ?? string.Empty;
        string descriptionHtml = TryGetStringProperty(dataElement, "text") ?? string.Empty;
        string screenshotsJson = TryGetStringProperty(dataElement, "screenshots") ?? string.Empty;
        string? previewUrl = TryGetStringProperty(dataElement, "Preview().sSubFeedImageUrl()");
        string plainSummary = StripHtmlToPlainText(summary);
        string plainDescription = StripHtmlToPlainText(descriptionHtml);
        string combinedText = string.Join(
            Environment.NewLine,
            new[] { plainSummary, plainDescription }
                .Where(value => !string.IsNullOrWhiteSpace(value)));

        List<string> imageUrls = ParseScreenshotUrls(screenshotsJson);
        if (!string.IsNullOrWhiteSpace(previewUrl) && !imageUrls.Contains(previewUrl, StringComparer.OrdinalIgnoreCase))
        {
            imageUrls.Insert(0, previewUrl);
        }

        return new OnlineModDetails
        {
            Summary = plainSummary,
            Description = plainDescription,
            ImageUrls = imageUrls,
            AccessRequirementSummary = ExtractAccessRequirementSummary(combinedText),
            ShortcutBindings = ExtractShortcutBindingsFromText(combinedText)
        };
    }

    private async Task<List<ShortcutBinding>> BuildLocalizedShortcutBindingsAsync(List<ShortcutBinding> bindings)
    {
        if (bindings.Count == 0)
        {
            return [];
        }

        if (_currentLanguage != AppLanguage.ZhCn)
        {
            return bindings
                .Take(MaxShortcutRows)
                .Select(binding => new ShortcutBinding(binding.Shortcut, binding.Action))
                .ToList();
        }

        var localized = new List<ShortcutBinding>();
        foreach (ShortcutBinding binding in bindings.Take(MaxShortcutRows))
        {
            string action = binding.Action;
            if (!string.IsNullOrWhiteSpace(action))
            {
                try
                {
                    string translatedAction = await TranslateTextForDisplayAsync(action, "zh-CN");
                    if (!string.IsNullOrWhiteSpace(translatedAction))
                    {
                        action = translatedAction;
                    }
                }
                catch
                {
                }
            }

            localized.Add(new ShortcutBinding(binding.Shortcut, action));
        }

        return localized;
    }

    private static string ExtractAccessRequirementSummary(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        string[] keywords =
        [
            "patreon",
            "subscribe",
            "subscription",
            "paid",
            "payment",
            "paywall",
            "donat",
            "unlock",
            "exclusive",
            "early access",
            "free after",
            "public release",
            "supporter",
            "supporters",
            "boosty",
            "gumroad",
            "ko-fi",
            "kofi",
            "discord",
            "like goal",
            "unlock at",
            "available after"
        ];

        var matches = new List<string>();
        foreach (string rawLine in text.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
        {
            string line = Regex.Replace(rawLine, @"\s+", " ").Trim(' ', '-', '•', '*', ':', '：');
            if (line.Length < 6)
            {
                continue;
            }

            if (keywords.Any(keyword => line.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                && !matches.Contains(line, StringComparer.OrdinalIgnoreCase))
            {
                matches.Add(line);
                if (matches.Count >= 3)
                {
                    break;
                }
            }
        }

        return string.Join(Environment.NewLine, matches);
    }

    private static List<ShortcutBinding> ExtractShortcutBindingsFromText(string text)
    {
        var bindings = new List<ShortcutBinding>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return bindings;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string rawLine in text.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
        {
            string line = Regex.Replace(rawLine, @"\s+", " ").Trim();
            if (line.Length < 4)
            {
                continue;
            }

            if (TryParseShortcutBindingLine(line, out ShortcutBinding? binding))
            {
                string identity = NormalizeShortcut(binding.Shortcut) + "|" + binding.Action.Trim();
                if (seen.Add(identity))
                {
                    bindings.Add(binding);
                    if (bindings.Count >= MaxShortcutRows)
                    {
                        break;
                    }
                }
            }
        }

        return bindings;
    }

    private static bool TryParseShortcutBindingLine(string line, out ShortcutBinding? binding)
    {
        binding = null;
        string normalizedLine = line.Trim().TrimStart('-', '•', '*', '>', ' ');
        if (string.IsNullOrWhiteSpace(normalizedLine))
        {
            return false;
        }

        Match sentenceMatch = Regex.Match(
            normalizedLine,
            @"(?i)\b(?:press|use|hit|key|shortcut|hotkey)\s+(?<shortcut>.+?)\s+(?:to|for)\s+(?<action>.+)$");
        if (sentenceMatch.Success)
        {
            string shortcutCandidate = NormalizeShortcutDisplay(sentenceMatch.Groups["shortcut"].Value);
            string actionCandidate = sentenceMatch.Groups["action"].Value.Trim();
            if (LooksLikeShortcutCandidate(shortcutCandidate) && actionCandidate.Length >= 2)
            {
                binding = new ShortcutBinding(shortcutCandidate, actionCandidate);
                return true;
            }
        }

        foreach (string separator in new[] { " => ", " -> ", " → ", "：", ":", " - ", " – ", " — ", " = " })
        {
            int index = normalizedLine.IndexOf(separator, StringComparison.Ordinal);
            if (index < 0)
            {
                continue;
            }

            string left = normalizedLine[..index].Trim(' ', '-', '•', '*', ':', '：');
            string right = normalizedLine[(index + separator.Length)..].Trim(' ', '-', '•', '*', ':', '：', '>', '=');
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                continue;
            }

            bool leftLooksLikeShortcut = LooksLikeShortcutCandidate(left);
            bool rightLooksLikeShortcut = LooksLikeShortcutCandidate(right);
            if (leftLooksLikeShortcut == rightLooksLikeShortcut)
            {
                continue;
            }

            string shortcut = NormalizeShortcutDisplay(leftLooksLikeShortcut ? left : right);
            string action = (leftLooksLikeShortcut ? right : left).Trim();
            if (!string.IsNullOrWhiteSpace(shortcut) && action.Length >= 2)
            {
                binding = new ShortcutBinding(shortcut, action);
                return true;
            }
        }

        return false;
    }

    private static bool LooksLikeShortcutCandidate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string candidate = value.Trim().Trim('[', ']', '(', ')');
        if (candidate.Length is 0 or > 40)
        {
            return false;
        }

        if (Regex.IsMatch(candidate, @"(?i)\b(author|description|download|preview|updated|version|image|requirement)\b"))
        {
            return false;
        }

        string[] parts = candidate.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        bool hasKey = false;
        foreach (string part in parts)
        {
            string token = NormalizeShortcutToken(part);
            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            if (token is "Ctrl" or "Shift" or "Alt" or "Win")
            {
                continue;
            }

            hasKey = Regex.IsMatch(
                token,
                @"^(?:F(?:[1-9]|1[0-9]|2[0-4])|Num[0-9]|[A-Z0-9]|Esc|Enter|Tab|Space|Left|Right|Up|Down|Home|End|PageUp|PageDown|Insert|Delete|[`~!@#$%^&*()_\-+=\[\]{}\\|;:'"",.<>/?])$");
            if (!hasKey)
            {
                return false;
            }
        }

        return hasKey;
    }

    private static string NormalizeShortcutDisplay(string value)
    {
        string[] parts = value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return string.Empty;
        }

        var normalized = new List<string>();
        foreach (string part in parts)
        {
            string token = NormalizeShortcutToken(part);
            if (!string.IsNullOrWhiteSpace(token))
            {
                normalized.Add(token);
            }
        }

        return normalized.Count == 0 ? string.Empty : string.Join("+", normalized);
    }

    private static string NormalizeShortcutToken(string value)
    {
        string token = value.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            return string.Empty;
        }

        token = token
            .Replace("Arrow", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("Key", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();

        return token.ToLowerInvariant() switch
        {
            "control" => "Ctrl",
            "ctrl" => "Ctrl",
            "shift" => "Shift",
            "alt" => "Alt",
            "menu" => "Alt",
            "win" => "Win",
            "windows" => "Win",
            "escape" => "Esc",
            "esc" => "Esc",
            "return" => "Enter",
            "enter" => "Enter",
            "space" => "Space",
            "spacebar" => "Space",
            "pageup" => "PageUp",
            "page up" => "PageUp",
            "pagedown" => "PageDown",
            "page down" => "PageDown",
            "insert" => "Insert",
            "delete" => "Delete",
            "left" => "Left",
            "right" => "Right",
            "up" => "Up",
            "down" => "Down",
            "home" => "Home",
            "end" => "End",
            "tab" => "Tab",
            "slash" => "/",
            "forward slash" => "/",
            "backslash" => "\\",
            "pipe" => "\\",
            "semicolon" => ";",
            "comma" => ",",
            "period" => ".",
            "dot" => ".",
            "minus" => "-",
            "hyphen" => "-",
            "equal" => "=",
            "equals" => "=",
            "quote" => "'",
            "apostrophe" => "'",
            "backtick" => "`",
            "grave" => "`",
            "left bracket" => "[",
            "right bracket" => "]",
            _ => NormalizeShortcutTokenFallback(token)
        };
    }

    private static string NormalizeShortcutTokenFallback(string token)
    {
        if (token.Length == 1)
        {
            return char.IsLetter(token[0]) ? token.ToUpperInvariant() : token;
        }

        if (Regex.IsMatch(token, @"^(?i)f(?:[1-9]|1[0-9]|2[0-4])$"))
        {
            return token.ToUpperInvariant();
        }

        Match numpadMatch = Regex.Match(token, @"^(?i)(?:num|numpad)\s*([0-9])$");
        if (numpadMatch.Success)
        {
            return "Num" + numpadMatch.Groups[1].Value;
        }

        return char.ToUpperInvariant(token[0]) + token[1..].ToLowerInvariant();
    }

    private UIElement CreateOnlineDetailImage(string? imageUrl)
    {
        if (Uri.TryCreate(imageUrl, UriKind.Absolute, out Uri? imageUri))
        {
            try
            {
                return new Image
                {
                    Stretch = Stretch.UniformToFill,
                    Source = new BitmapImage(imageUri)
                };
            }
            catch
            {
            }
        }

        return new TextBlock
        {
            Text = L("预览图加载失败", "Preview failed to load"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["CaptionTextStyle"]
        };
    }

    private async Task<OnlineModDetails> TryTranslateOnlineModDetailsAsync(OnlineModDetails details)
    {
        if (_currentLanguage != AppLanguage.ZhCn)
        {
            return details;
        }

        try
        {
            string translatedSummary = string.IsNullOrWhiteSpace(details.Summary)
                ? details.Summary
                : await TranslateTextForDisplayAsync(details.Summary, "zh-CN");
            string translatedDescription = string.IsNullOrWhiteSpace(details.Description)
                ? details.Description
                : await TranslateTextForDisplayAsync(details.Description, "zh-CN");
            string translatedRequirement = string.IsNullOrWhiteSpace(details.AccessRequirementSummary)
                ? details.AccessRequirementSummary
                : await TranslateTextForDisplayAsync(details.AccessRequirementSummary, "zh-CN");
            List<ShortcutBinding> translatedBindings = await BuildLocalizedShortcutBindingsAsync(details.ShortcutBindings);

            return new OnlineModDetails
            {
                Summary = string.IsNullOrWhiteSpace(translatedSummary) ? details.Summary : translatedSummary,
                Description = string.IsNullOrWhiteSpace(translatedDescription) ? details.Description : translatedDescription,
                ImageUrls = details.ImageUrls,
                TranslationNote = L("以下为实验性自动翻译，可能不完全准确。", "The content below was translated automatically and may be imperfect."),
                AccessRequirementSummary = string.IsNullOrWhiteSpace(translatedRequirement) ? details.AccessRequirementSummary : translatedRequirement,
                ShortcutBindings = translatedBindings.Count > 0 ? translatedBindings : details.ShortcutBindings
            };
        }
        catch
        {
            return new OnlineModDetails
            {
                Summary = details.Summary,
                Description = details.Description,
                ImageUrls = details.ImageUrls,
                TranslationNote = L("自动翻译暂时不可用，当前显示原文。", "Automatic translation is unavailable right now, so the original text is shown."),
                AccessRequirementSummary = details.AccessRequirementSummary,
                ShortcutBindings = details.ShortcutBindings
            };
        }
    }

    private void PopulateOnlineDetailPane(OnlineModCard mod, OnlineModDetails rawDetails, OnlineModDetails displayDetails)
    {
        OnlineDetailPaneTitleTextBlock.Text = mod.Title;
        OnlineDetailPaneMetaTextBlock.Text = $"{L("角色", "Character")}: {mod.CharacterName}    {L("作者", "Author")}: {mod.Author}";

        var statusParts = new List<string>
        {
            $"{L("分类", "Category")}: {mod.RootCategoryName}",
            $"{L("更新时间", "Updated")}: {mod.UpdatedAt:yyyy-MM-dd HH:mm}",
            $"ID: {mod.ItemId}"
        };

        if (displayDetails.ShortcutBindings.Count > 0)
        {
            statusParts.Add(L(
                $"已识别 {displayDetails.ShortcutBindings.Count} 条快捷键说明",
                $"{displayDetails.ShortcutBindings.Count} shortcut notes detected"));
        }

        OnlineDetailPaneStatusTextBlock.Text = string.Join("    ", statusParts);

        if (!string.IsNullOrWhiteSpace(displayDetails.AccessRequirementSummary))
        {
            OnlineDetailRequirementBorder.Visibility = Visibility.Visible;
            OnlineDetailRequirementTitleTextBlock.Text = L("可能存在额外访问要求", "Possible extra access requirements");
            OnlineDetailRequirementTextBlock.Text = displayDetails.AccessRequirementSummary;
        }
        else
        {
            OnlineDetailRequirementBorder.Visibility = Visibility.Collapsed;
            OnlineDetailRequirementTitleTextBlock.Text = string.Empty;
            OnlineDetailRequirementTextBlock.Text = string.Empty;
        }

        OnlineDetailSummaryTextBlock.Text = string.IsNullOrWhiteSpace(displayDetails.Summary)
            ? L("当前条目没有提供简介。", "This entry does not provide a summary.")
            : displayDetails.Summary;
        OnlineDetailDescriptionTextBlock.Text = displayDetails.Description ?? string.Empty;

        if (string.IsNullOrWhiteSpace(displayDetails.TranslationNote))
        {
            OnlineDetailTranslationNoteTextBlock.Visibility = Visibility.Collapsed;
            OnlineDetailTranslationNoteTextBlock.Text = string.Empty;
        }
        else
        {
            OnlineDetailTranslationNoteTextBlock.Visibility = Visibility.Visible;
            OnlineDetailTranslationNoteTextBlock.Text = displayDetails.TranslationNote;
        }

        if (!string.IsNullOrWhiteSpace(rawDetails.AccessRequirementSummary))
        {
            OnlineDetailDownloadHintTextBlock.Text = L(
                $"下载时会在状态栏提示这条 Mod 可能存在额外访问要求：{rawDetails.AccessRequirementSummary}",
                $"The download status will mention possible extra access requirements for this mod: {rawDetails.AccessRequirementSummary}");
        }
        else
        {
            OnlineDetailDownloadHintTextBlock.Text = L(
                "可以直接在这里打开原页面，或下载并自动解压到你选择的目录。",
                "You can open the original page here or download and extract it into a folder you choose.");
        }

        OpenOnlineDetailPageButton.Content = L("打开原页面", "Open Original Page");
        DownloadOnlineDetailButton.Content = !string.IsNullOrWhiteSpace(mod.DownloadUrl)
            ? L("下载并解压", "Download and Extract")
            : L("暂无下载", "No Download");
        OpenOnlineDetailPageButton.IsEnabled = !string.IsNullOrWhiteSpace(mod.ProfileUrl);
        DownloadOnlineDetailButton.IsEnabled = !string.IsNullOrWhiteSpace(mod.DownloadUrl);

        PopulateOnlineDetailImages(displayDetails.ImageUrls);
    }

    private void ResetOnlineDetailPaneToPlaceholder()
    {
        OnlineDetailPaneTitleTextBlock.Text = L("Mod 详情与预览", "Mod Details and Preview");
        OnlineDetailPaneMetaTextBlock.Text = L("选择左侧列表中的一个在线 Mod，这里会固定显示预览图、简介和下载入口。", "Select an online mod from the list on the left. This panel stays fixed and shows the preview, summary, and download actions.");
        OnlineDetailPaneStatusTextBlock.Text = string.Empty;
        OnlineDetailSummaryTextBlock.Text = string.Empty;
        OnlineDetailDescriptionTextBlock.Text = string.Empty;
        OnlineDetailDownloadHintTextBlock.Text = L("打开原页面和下载并解压按钮会在选择条目后可用。", "Open-page and download-extract actions become available after you select an entry.");
        OnlineDetailRequirementBorder.Visibility = Visibility.Collapsed;
        OnlineDetailRequirementTitleTextBlock.Text = L("可能存在额外访问要求", "Possible extra access requirements");
        OnlineDetailRequirementTextBlock.Text = string.Empty;
        OnlineDetailTranslationNoteTextBlock.Visibility = Visibility.Collapsed;
        OnlineDetailTranslationNoteTextBlock.Text = string.Empty;
        OpenOnlineDetailPageButton.IsEnabled = false;
        DownloadOnlineDetailButton.IsEnabled = false;
        OpenOnlineDetailPageButton.Content = L("打开原页面", "Open Original Page");
        DownloadOnlineDetailButton.Content = L("下载并解压", "Download and Extract");
        PopulateOnlineDetailImages([]);
    }

    private void PopulateOnlineDetailImages(List<string> imageUrls)
    {
        OnlineDetailThumbnailPanel.Children.Clear();
        List<string> usableImages = imageUrls
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Skip(1)
            .ToList();

        if (usableImages.Count == 0)
        {
            OnlineDetailHeroImage.Source = null;
            OnlineDetailHeroImage.Visibility = Visibility.Collapsed;
            OnlineDetailHeroPlaceholderTextBlock.Visibility = Visibility.Visible;
            OnlineDetailHeroPlaceholderTextBlock.Text = L("当前条目没有可用的高清预览图。", "This entry does not provide usable high-resolution preview images.");
            OnlineDetailThumbnailScrollViewer.Visibility = Visibility.Collapsed;
            return;
        }

        ShowOnlineDetailHeroImage(usableImages[0]);

        if (usableImages.Count == 1)
        {
            OnlineDetailThumbnailScrollViewer.Visibility = Visibility.Collapsed;
            return;
        }

        OnlineDetailThumbnailScrollViewer.Visibility = Visibility.Visible;
        foreach (string imageUrl in usableImages.Take(10))
        {
            Button thumbButton = new()
            {
                Width = 112,
                Height = 72,
                Padding = new Thickness(0),
                Style = (Style)Application.Current.Resources["SecondaryButtonStyle"],
                Content = new Border
                {
                    Style = (Style)Application.Current.Resources["InsetBorderStyle"],
                    Child = CreateOnlineDetailImage(imageUrl)
                }
            };
            thumbButton.Click += (_, _) => ShowOnlineDetailHeroImage(imageUrl);
            OnlineDetailThumbnailPanel.Children.Add(thumbButton);
        }
    }

    private void ShowOnlineDetailHeroImage(string? imageUrl)
    {
        if (Uri.TryCreate(imageUrl, UriKind.Absolute, out Uri? imageUri))
        {
            try
            {
                OnlineDetailHeroImage.Source = new BitmapImage(imageUri);
                OnlineDetailHeroImage.Visibility = Visibility.Visible;
                OnlineDetailHeroPlaceholderTextBlock.Visibility = Visibility.Collapsed;
                OnlineDetailHeroPlaceholderTextBlock.Text = string.Empty;
                return;
            }
            catch
            {
            }
        }

        OnlineDetailHeroImage.Source = null;
        OnlineDetailHeroImage.Visibility = Visibility.Collapsed;
        OnlineDetailHeroPlaceholderTextBlock.Visibility = Visibility.Visible;
        OnlineDetailHeroPlaceholderTextBlock.Text = L("预览图加载失败。", "Failed to load the preview image.");
    }

    private async Task<string> TranslateTextForDisplayAsync(string text, string targetLanguage)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        List<string> chunks = SplitTextForTranslation(text, 2500);
        var builder = new StringBuilder();
        for (int i = 0; i < chunks.Count; i++)
        {
            string translatedChunk = await TranslateChunkAsync(chunks[i], targetLanguage);
            if (builder.Length > 0)
            {
                builder.AppendLine();
                builder.AppendLine();
            }

            builder.Append(translatedChunk.Trim());
        }

        return builder.ToString().Trim();
    }

    private async Task<string> TranslateChunkAsync(string text, string targetLanguage)
    {
        string requestUrl = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl={Uri.EscapeDataString(targetLanguage)}&dt=t&q={Uri.EscapeDataString(text)}";
        using HttpResponseMessage response = await _httpClient.GetAsync(requestUrl);
        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync();
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0 || root[0].ValueKind != JsonValueKind.Array)
        {
            return text;
        }

        var builder = new StringBuilder();
        foreach (JsonElement sentence in root[0].EnumerateArray())
        {
            if (sentence.ValueKind == JsonValueKind.Array && sentence.GetArrayLength() > 0 && sentence[0].ValueKind == JsonValueKind.String)
            {
                builder.Append(sentence[0].GetString());
            }
        }

        return string.IsNullOrWhiteSpace(builder.ToString()) ? text : builder.ToString();
    }

    private static List<string> SplitTextForTranslation(string text, int maxChunkLength)
    {
        List<string> chunks = [];
        string[] paragraphs = text.Split(["\r\n\r\n", "\n\n"], StringSplitOptions.None);
        var builder = new StringBuilder();

        foreach (string paragraph in paragraphs)
        {
            string current = paragraph.Trim();
            if (string.IsNullOrWhiteSpace(current))
            {
                continue;
            }

            if (builder.Length > 0 && builder.Length + current.Length + 2 > maxChunkLength)
            {
                chunks.Add(builder.ToString());
                builder.Clear();
            }

            if (current.Length > maxChunkLength)
            {
                if (builder.Length > 0)
                {
                    chunks.Add(builder.ToString());
                    builder.Clear();
                }

                for (int index = 0; index < current.Length; index += maxChunkLength)
                {
                    chunks.Add(current.Substring(index, Math.Min(maxChunkLength, current.Length - index)));
                }
                continue;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
                builder.AppendLine();
            }
            builder.Append(current);
        }

        if (builder.Length > 0)
        {
            chunks.Add(builder.ToString());
        }

        return chunks.Count == 0 ? [text] : chunks;
    }

    private static List<string> ParseScreenshotUrls(string screenshotsJson)
    {
        List<string> urls = [];
        if (string.IsNullOrWhiteSpace(screenshotsJson))
        {
            return urls;
        }

        try
        {
            using JsonDocument screenshotsDocument = JsonDocument.Parse(screenshotsJson);
            if (screenshotsDocument.RootElement.ValueKind != JsonValueKind.Array)
            {
                return urls;
            }

            foreach (JsonElement item in screenshotsDocument.RootElement.EnumerateArray())
            {
                string? fileName = TryGetStringProperty(item, "_sFile800")
                    ?? TryGetStringProperty(item, "_sFile530")
                    ?? TryGetStringProperty(item, "_sFile");
                if (!string.IsNullOrWhiteSpace(fileName))
                {
                    urls.Add("https://images.gamebanana.com/img/ss/mods/" + fileName);
                }
            }
        }
        catch
        {
            return urls;
        }

        return urls;
    }

    private static string StripHtmlToPlainText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string text = value;
        text = Regex.Replace(text, @"<\s*br\s*/?>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<\s*/?(p|div|h\d|li|ul|ol)\b[^>]*>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<[^>]+>", " ");
        text = WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, @"[ \t]+\n", "\n");
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        text = Regex.Replace(text, @"[ \t]{2,}", " ");
        return text.Trim();
    }

    private static bool IsSourceInsideButton(DependencyObject? source)
    {
        DependencyObject? current = source;
        while (current is not null)
        {
            if (current is Button)
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private UIElement CreateOnlineModCard(OnlineModCard mod)
    {
        Border border = new()
        {
            Style = (Style)Application.Current.Resources["InsetBorderStyle"],
            Padding = new Thickness(14)
        };

        Grid rootGrid = new() { ColumnSpacing = 14 };
        rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        Border imageHost = new()
        {
            Style = (Style)Application.Current.Resources["InsetBorderStyle"],
            MinHeight = 68,
            Height = 68
        };

        if (!string.IsNullOrWhiteSpace(mod.PreviewUrl))
        {
            imageHost.Child = new Image
            {
                Stretch = Stretch.UniformToFill,
                Source = new BitmapImage(new Uri(mod.PreviewUrl))
            };
        }
        else
        {
            imageHost.Child = new TextBlock
            {
                Text = L("暂无缩略图", "No preview"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Style = (Style)Application.Current.Resources["CaptionTextStyle"]
            };
        }

        Grid.SetColumn(imageHost, 0);
        rootGrid.Children.Add(imageHost);

        StackPanel contentPanel = new() { Spacing = 8 };
        contentPanel.Children.Add(new TextBlock
        {
            Text = mod.Title,
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        contentPanel.Children.Add(new TextBlock
        {
            Text = $"{L("作者", "Author")}: {mod.Author}",
            Style = (Style)Application.Current.Resources["MutedTextStyle"],
            TextWrapping = TextWrapping.Wrap
        });
        contentPanel.Children.Add(new TextBlock
        {
            Text = $"{L("鏇存柊浜", "Updated")}: {mod.UpdatedAt:yyyy-MM-dd HH:mm}    ID: {mod.ItemId}",
            Style = (Style)Application.Current.Resources["CaptionTextStyle"],
            TextWrapping = TextWrapping.Wrap
        });
        contentPanel.Children.Add(new TextBlock
        {
            Text = mod.HasUpdates ? L("该条目包含更新记录。", "This entry reports update records.") : L("该条目当前没有更新记录标记。", "This entry currently has no update record flag."),
            Foreground = mod.HasUpdates ? CopiedBrush : NeutralBrush
        });

        StackPanel actionsPanel = new() { Orientation = Orientation.Horizontal, Spacing = 10 };
        Button openPageButton = new()
        {
            Content = L("打开页面", "Open Page"),
            Style = (Style)Application.Current.Resources["SecondaryButtonStyle"],
            MinHeight = 36
        };
        openPageButton.Click += async (_, _) => await OpenExternalUrlAsync(mod.ProfileUrl, L("打开页面失败", "Open page failed"));
        actionsPanel.Children.Add(openPageButton);

        Button downloadButton = new()
        {
            Content = L("下载文件", "Download"),
            Style = (Style)Application.Current.Resources["SecondaryButtonStyle"],
            MinHeight = 36,
            IsEnabled = !string.IsNullOrWhiteSpace(mod.DownloadUrl)
        };
        downloadButton.Click += async (_, _) => await DownloadAndExtractOnlineModAsync(mod);
        actionsPanel.Children.Add(downloadButton);

        contentPanel.Children.Add(actionsPanel);
        Grid.SetColumn(contentPanel, 1);
        rootGrid.Children.Add(contentPanel);

        border.Child = rootGrid;
        return border;
    }

    private UIElement CreateOnlineModCardV2(OnlineModCard mod)
    {
        Border border = new()
        {
            Style = (Style)Application.Current.Resources["InsetBorderStyle"],
            Padding = new Thickness(14)
        };
        border.Tapped += async (_, args) =>
        {
            if (IsSourceInsideButton(args.OriginalSource as DependencyObject))
            {
                return;
            }

            await ShowOnlineModDetailsAsync(mod);
        };

        Grid rootGrid = new() { ColumnSpacing = 14 };
        rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        Border imageHost = new()
        {
            Style = (Style)Application.Current.Resources["InsetBorderStyle"],
            MinHeight = 68,
            Height = 68
        };

        if (!string.IsNullOrWhiteSpace(mod.PreviewUrl))
        {
            imageHost.Child = new Image
            {
                Stretch = Stretch.UniformToFill,
                Source = new BitmapImage(new Uri(mod.PreviewUrl))
            };
        }
        else
        {
            imageHost.Child = new TextBlock
            {
                Text = L("暂无缩略图", "No preview"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Style = (Style)Application.Current.Resources["CaptionTextStyle"]
            };
        }

        Grid.SetColumn(imageHost, 0);
        rootGrid.Children.Add(imageHost);

        StackPanel contentPanel = new() { Spacing = 8 };
        contentPanel.Children.Add(new TextBlock
        {
            Text = mod.Title,
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        contentPanel.Children.Add(new TextBlock
        {
            Text = $"{L("角色", "Character")}: {mod.CharacterName}",
            Style = (Style)Application.Current.Resources["MutedTextStyle"],
            TextWrapping = TextWrapping.Wrap
        });
        contentPanel.Children.Add(new TextBlock
        {
            Text = $"{L("作者", "Author")}: {mod.Author}    {L("分类", "Category")}: {mod.RootCategoryName}",
            Style = (Style)Application.Current.Resources["CaptionTextStyle"],
            TextWrapping = TextWrapping.Wrap
        });
        contentPanel.Children.Add(new TextBlock
        {
            Text = $"{L("点赞", "Likes")}: {mod.Likes}    {L("查看", "Views")}: {mod.Views}    {L("下载量", "Downloads")}: {mod.Downloads}",
            Style = (Style)Application.Current.Resources["MutedTextStyle"],
            TextWrapping = TextWrapping.Wrap
        });
        contentPanel.Children.Add(new TextBlock
        {
            Text = $"{L("热度", "Hotness")}: {mod.HotnessScore:F2}    {L("更新时间", "Updated")}: {mod.UpdatedAt:yyyy-MM-dd HH:mm}",
            Foreground = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        contentPanel.Children.Add(new TextBlock
        {
            Text = $"ID: {mod.ItemId}",
            Style = (Style)Application.Current.Resources["CaptionTextStyle"]
        });

        StackPanel actionsPanel = new() { Orientation = Orientation.Horizontal, Spacing = 10 };
        Button openPageButton = new()
        {
            Content = L("打开页面", "Open Page"),
            Style = (Style)Application.Current.Resources["SecondaryButtonStyle"],
            MinHeight = 36
        };
        openPageButton.Click += async (_, _) => await OpenExternalUrlAsync(mod.ProfileUrl, L("打开页面失败", "Open page failed"));
        actionsPanel.Children.Add(openPageButton);

        Button detailsButton = new()
        {
            Content = L("查看详情", "View Details"),
            Style = (Style)Application.Current.Resources["SecondaryButtonStyle"],
            MinHeight = 36
        };
        detailsButton.Click += async (_, _) => await ShowOnlineModDetailsAsync(mod);
        actionsPanel.Children.Add(detailsButton);

        Button downloadButton = new()
        {
            Content = L("下载并解压", "Download and Extract"),
            Style = (Style)Application.Current.Resources["SecondaryButtonStyle"],
            MinHeight = 36,
            IsEnabled = !string.IsNullOrWhiteSpace(mod.DownloadUrl)
        };
        downloadButton.Click += async (_, _) => await DownloadAndExtractOnlineModAsync(mod);
        actionsPanel.Children.Add(downloadButton);

        contentPanel.Children.Add(actionsPanel);
        Grid.SetColumn(contentPanel, 1);
        rootGrid.Children.Add(contentPanel);

        border.Child = rootGrid;
        return border;
    }

    private async Task OpenExternalUrlAsync(string? url, string failureTitle)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            await ShowMessageAsync(
                L("当前条目没有可用链接。", "This entry does not have a usable link."),
                failureTitle);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            await ShowMessageAsync(
                L("打开链接失败：", "Failed to open link: ") + ex.Message,
                failureTitle);
        }
    }

    private UIElement CreateInfoCard(string title, string body)
    {
        return new Border
        {
            Style = (Style)Application.Current.Resources["InsetBorderStyle"],
            Padding = new Thickness(14),
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        FontSize = 18,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        TextWrapping = TextWrapping.Wrap
                    },
                    new TextBlock
                    {
                        Text = body,
                        Style = (Style)Application.Current.Resources["MutedTextStyle"],
                        TextWrapping = TextWrapping.Wrap
                    }
                }
            }
        };
    }

    private UIElement CreateTrackedModUpdateCard(TrackedModUpdateResult result)
    {
        Border border = new()
        {
            Style = (Style)Application.Current.Resources["InsetBorderStyle"],
            Padding = new Thickness(14)
        };

        Grid grid = new() { ColumnSpacing = 14 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        Border previewHost = new()
        {
            Style = (Style)Application.Current.Resources["InsetBorderStyle"],
            MinHeight = 72,
            Height = 72
        };
        if (!string.IsNullOrWhiteSpace(result.PreviewUrl))
        {
            try
            {
                previewHost.Child = new Image
                {
                    Stretch = Stretch.UniformToFill,
                    Source = new BitmapImage(new Uri(result.PreviewUrl))
                };
            }
            catch
            {
                previewHost.Child = new TextBlock
                {
                    Text = L("预览加载失败", "Preview failed"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Style = (Style)Application.Current.Resources["CaptionTextStyle"]
                };
            }
        }
        else
        {
            previewHost.Child = new TextBlock
            {
                Text = L("暂无预览", "No preview"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Style = (Style)Application.Current.Resources["CaptionTextStyle"]
            };
        }

        Grid.SetColumn(previewHost, 0);
        grid.Children.Add(previewHost);

        StackPanel content = new() { Spacing = 6 };
        content.Children.Add(new TextBlock
        {
            Text = result.Title,
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(new TextBlock
        {
            Text = $"{L("本地路径", "Local path")}: {TrimPreviewPath(result.Path)}",
            Style = (Style)Application.Current.Resources["CaptionTextStyle"],
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(new TextBlock
        {
            Text = $"{L("记录时间", "Recorded")}: {FormatDateForDisplay(result.LastKnownUpdatedAt)}    {L("最新时间", "Latest")}: {FormatDateForDisplay(result.LatestUpdatedAt)}",
            Style = (Style)Application.Current.Resources["MutedTextStyle"],
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(new TextBlock
        {
            Text = L(result.StatusTextZh, result.StatusTextEn),
            Foreground = result.HasUpdate ? MissingBrush : CopiedBrush,
            FontWeight = Microsoft.UI.Text.FontWeights.Medium,
            TextWrapping = TextWrapping.Wrap
        });

        StackPanel actions = new() { Orientation = Orientation.Horizontal, Spacing = 10 };
        Button openPageButton = new()
        {
            Content = L("打开页面", "Open Page"),
            Style = (Style)Application.Current.Resources["SecondaryButtonStyle"],
            MinHeight = 36,
            IsEnabled = !string.IsNullOrWhiteSpace(result.ProfileUrl)
        };
        openPageButton.Click += async (_, _) => await OpenExternalUrlAsync(result.ProfileUrl, L("打开页面失败", "Open page failed"));
        actions.Children.Add(openPageButton);

        Button openFolderButton = new()
        {
            Content = L("打开本地目录", "Open Folder"),
            Style = (Style)Application.Current.Resources["SecondaryButtonStyle"],
            MinHeight = 36,
            IsEnabled = Directory.Exists(result.Path)
        };
        openFolderButton.Click += (_, _) =>
        {
            if (Directory.Exists(result.Path))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{result.Path}\"",
                    UseShellExecute = true
                });
            }
        };
        actions.Children.Add(openFolderButton);

        Button removeTrackingButton = new()
        {
            Content = L("删除记录", "Remove Tracking"),
            Style = (Style)Application.Current.Resources["SecondaryButtonStyle"],
            MinHeight = 36
        };
        removeTrackingButton.Click += async (_, _) => await RemoveTrackedModRecordAsync(result);
        actions.Children.Add(removeTrackingButton);

        content.Children.Add(actions);
        Grid.SetColumn(content, 1);
        grid.Children.Add(content);

        border.Child = grid;
        return border;
    }

    private static string FormatDateForDisplay(DateTimeOffset? value)
    {
        return value.HasValue ? value.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "-";
    }

    private async Task CheckTrackedModUpdatesAsync(bool showDialogs)
    {
        if (_isCheckingModUpdates)
        {
            return;
        }

        if (_trackedModOrigins.Count == 0)
        {
            SetModUpdateStatus("还没有可检查更新的在线 Mod。", "There are no tracked online mods to check yet.");
            RefreshUpdatesPane();
            if (showDialogs)
            {
                await ShowMessageAsync(
                    L("请先从在线模块下载并解压至少一个 Mod，系统才会记录来源并检查更新。", "Download and extract at least one mod from the online browser first so the app can track and check updates."),
                    L("没有可检查的 Mod", "No tracked mods"));
            }
            return;
        }

        bool deduplicatedTrackedMods = DeduplicateTrackedModOrigins();
        if (deduplicatedTrackedMods)
        {
            SaveConfig();
            SaveShellConfig();
        }

        try
        {
            _isCheckingModUpdates = true;
            SetModUpdateStatus($"正在检查 {_trackedModOrigins.Count} 个已追踪 Mod 的更新...", $"Checking {_trackedModOrigins.Count} tracked mods for updates...");
            RefreshUpdatesPane();

            _trackedModUpdateResults.Clear();
            foreach (TrackedModOrigin origin in _trackedModOrigins.Values.OrderBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase))
            {
                TrackedModUpdateResult result = await FetchTrackedModUpdateResultAsync(origin);
                _trackedModUpdateResults.Add(result);

                if (!string.IsNullOrWhiteSpace(result.PreviewUrl))
                {
                    origin.PreviewUrl = result.PreviewUrl;
                }

                if (!string.IsNullOrWhiteSpace(result.Title))
                {
                    origin.Title = result.Title;
                }

                if (!string.IsNullOrWhiteSpace(result.ProfileUrl))
                {
                    origin.ProfileUrl = result.ProfileUrl;
                    _modLinks[origin.Path] = result.ProfileUrl;
                }
            }

            _lastModUpdateCheckUtc = DateTimeOffset.UtcNow;
            SaveConfig();
            SaveShellConfig();

            int updateCount = _trackedModUpdateResults.Count(item => item.HasUpdate);
            SetModUpdateStatus(
                updateCount > 0
                    ? $"检查完成，发现 {updateCount} 个 Mod 有更新。"
                    : "检查完成，当前所有已追踪 Mod 都是最新状态。",
                updateCount > 0
                    ? $"Check complete. {updateCount} tracked mods have updates."
                    : "Check complete. All tracked mods are up to date.");

            RefreshUpdatesPane();

            if (showDialogs)
            {
                await ShowMessageAsync(
                    updateCount > 0
                        ? L($"已检查 {_trackedModUpdateResults.Count} 个 Mod，发现 {updateCount} 个有更新。", $"Checked {_trackedModUpdateResults.Count} mods and found {updateCount} updates.")
                        : L($"已检查 {_trackedModUpdateResults.Count} 个 Mod，没有发现新更新。", $"Checked {_trackedModUpdateResults.Count} mods and found no new updates."),
                    L("更新检查完成", "Update check complete"));
            }
        }
        catch (Exception ex)
        {
            SetModUpdateStatus("检查已追踪 Mod 更新失败，请稍后重试。", "Failed to check tracked mod updates. Please try again later.");
            RefreshUpdatesPane();
            if (showDialogs)
            {
                await ShowMessageAsync(
                    L("检查已追踪 Mod 更新失败：", "Failed to check tracked mod updates: ") + ex.Message,
                    L("更新检查失败", "Update check failed"));
            }
        }
        finally
        {
            _isCheckingModUpdates = false;
            RefreshUpdatesPane();
        }
    }

    private async Task<TrackedModUpdateResult> FetchTrackedModUpdateResultAsync(TrackedModOrigin origin)
    {
        OnlineModCard? latest = null;
        try
        {
            latest = await FetchGameBananaModCardAsyncV2(origin.ItemId);
        }
        catch
        {
        }

        DateTimeOffset? latestUpdatedAt = latest?.UpdatedAt;
        DateTimeOffset? recordedUpdatedAt = origin.LastKnownUpdatedAt;
        bool hasUpdate = latestUpdatedAt.HasValue
            && recordedUpdatedAt.HasValue
            && latestUpdatedAt.Value > recordedUpdatedAt.Value.AddMinutes(1);

        if (!recordedUpdatedAt.HasValue && latestUpdatedAt.HasValue)
        {
            origin.LastKnownUpdatedAt = latestUpdatedAt;
        }

        return new TrackedModUpdateResult
        {
            Path = origin.Path,
            ItemId = origin.ItemId,
            Title = string.IsNullOrWhiteSpace(latest?.Title) ? origin.Title : latest.Title,
            ProfileUrl = string.IsNullOrWhiteSpace(latest?.ProfileUrl) ? origin.ProfileUrl : latest.ProfileUrl,
            PreviewUrl = string.IsNullOrWhiteSpace(latest?.PreviewUrl) ? origin.PreviewUrl : latest.PreviewUrl,
            LastKnownUpdatedAt = recordedUpdatedAt ?? origin.LastKnownUpdatedAt,
            LatestUpdatedAt = latestUpdatedAt ?? recordedUpdatedAt ?? origin.LastKnownUpdatedAt,
            HasUpdate = hasUpdate,
            StatusTextZh = hasUpdate ? "检测到新版本，可前往页面查看。" : "当前没有检测到更新。",
            StatusTextEn = hasUpdate ? "A newer version was found. Open the page to review it." : "No update was detected."
        };
    }

    private async Task DownloadAndExtractOnlineModAsync(OnlineModCard mod)
    {
        OnlineModCard effectiveMod = mod;
        try
        {
            OnlineModCard? latestMod = await FetchGameBananaModCardAsyncV2(mod.ItemId);
            if (latestMod is not null)
            {
                if (string.IsNullOrWhiteSpace(latestMod.Title))
                {
                    latestMod.Title = mod.Title;
                }

                if (string.IsNullOrWhiteSpace(latestMod.CharacterName))
                {
                    latestMod.CharacterName = mod.CharacterName;
                }

                if (string.IsNullOrWhiteSpace(latestMod.RootCategoryName))
                {
                    latestMod.RootCategoryName = mod.RootCategoryName;
                }

                if (string.IsNullOrWhiteSpace(latestMod.Author))
                {
                    latestMod.Author = mod.Author;
                }

                if (string.IsNullOrWhiteSpace(latestMod.ProfileUrl))
                {
                    latestMod.ProfileUrl = mod.ProfileUrl;
                }

                if (string.IsNullOrWhiteSpace(latestMod.PreviewUrl))
                {
                    latestMod.PreviewUrl = mod.PreviewUrl;
                }

                effectiveMod = latestMod;
            }
        }
        catch
        {
        }

        if (string.IsNullOrWhiteSpace(effectiveMod.DownloadUrl))
        {
            await ShowMessageAsync(
                L("当前条目没有可用下载链接。", "This entry does not have a usable download link."),
                L("无法下载", "Cannot download"));
            return;
        }

        string? selectedFolder = await PickDownloadFolderAsync();
        if (string.IsNullOrWhiteSpace(selectedFolder))
        {
            return;
        }

        SetBusyState(true);
        try
        {
            OnlineModDetails details = await GetOnlineModDetailsAsync(effectiveMod);

            SetOnlineDownloadProgress(true, 0, "准备下载...", "Preparing download...");
            string downloadStatusZh = $"正在下载 {effectiveMod.Title}，完成后会自动解压到你选择的文件夹中。";
            string downloadStatusEn = $"Downloading {effectiveMod.Title}. It will be extracted into the folder you selected.";
            if (!string.IsNullOrWhiteSpace(details.AccessRequirementSummary))
            {
                downloadStatusZh += $" 页面文字提到了可能的额外访问要求：{details.AccessRequirementSummary}";
                downloadStatusEn += $" The page text mentions possible extra access requirements: {details.AccessRequirementSummary}";
            }

            SetOnlineStatus(downloadStatusZh, downloadStatusEn);

            string archivePath = await DownloadOnlineModArchiveAsync(effectiveMod, selectedFolder);
            string resolvedArchivePath = EnsureDownloadArchiveExtension(archivePath);
            if (!IsSupportedArchiveFile(resolvedArchivePath))
            {
                throw new InvalidOperationException(L(
                    $"文件已下载，但它不是当前可自动解压的压缩包格式：\n{Path.GetFileName(resolvedArchivePath)}",
                    $"The file was downloaded, but it is not a supported archive for automatic extraction:\n{Path.GetFileName(resolvedArchivePath)}"));
            }

            string extractFolder = CreateUniqueExtractionFolder(selectedFolder, effectiveMod.Title, effectiveMod.ItemId);
            Directory.CreateDirectory(extractFolder);

            await Task.Run(() => ExtractArchiveToDirectory(resolvedArchivePath, extractFolder));
            await ApplyTrackedOnlineModMetadataAsync(extractFolder, effectiveMod, details);

            WorkspaceRepository? repository = GetSelectedRepository();
            if (repository is not null
                && !string.IsNullOrWhiteSpace(repository.SourcePath)
                && IsPathInsideDirectory(extractFolder, repository.SourcePath))
            {
                await RefreshListsAsync();
                SelectSecondLevelByPath(extractFolder);
            }

            SetOnlineStatus(
                $"已下载并解压到：{extractFolder}",
                $"Downloaded and extracted to: {extractFolder}");

            await ShowMessageAsync(
                L($"已完成下载并解压：\n{extractFolder}", $"Download and extraction completed:\n{extractFolder}"),
                L("下载完成", "Download completed"));
        }
        catch (Exception ex)
        {
            SetOnlineStatus("下载或解压在线 Mod 失败。", "Failed to download or extract the online mod.");
            string message = ex.Message;
            if (ex is InvalidDataException or IOException)
            {
                if (message.Contains("End of Central Directory", StringComparison.OrdinalIgnoreCase))
                {
                    message = L(
                        "下载到的文件不是有效压缩包，通常是网站返回了下载页面、限流页面，或当前链接需要额外跳转验证。",
                        "The downloaded file is not a valid archive. The site likely returned a download page, a rate-limit page, or a response that requires extra verification.");
                }
            }

            await ShowMessageAsync(
                L("下载或解压失败：", "Download or extraction failed: ") + message,
                L("在线下载失败", "Online download failed"));
        }
        finally
        {
            SetOnlineDownloadProgress(false, 0, string.Empty, string.Empty);
            SetBusyState(false);
            RefreshOnlinePaneV2();
        }
    }

    private async Task<string> DownloadOnlineModArchiveAsync(OnlineModCard mod, string destinationFolder)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, mod.DownloadUrl!);
        request.Headers.Referrer = Uri.TryCreate(mod.ProfileUrl, UriKind.Absolute, out Uri? refererUri)
            ? refererUri
            : new Uri("https://gamebanana.com/");
        request.Headers.TryAddWithoutValidation("Accept", "*/*");

        using HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        string fileName = ResolveDownloadFileName(response, mod);
        string archivePath = Path.Combine(destinationFolder, fileName);

        long totalRead = 0;
        long totalLength = mod.FileSizeBytes > 0 ? mod.FileSizeBytes : (response.Content.Headers.ContentLength ?? 0);
        byte[] buffer = new byte[81920];

        await using (Stream remoteStream = await response.Content.ReadAsStreamAsync())
        {
            await using (FileStream localStream = new(archivePath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                int bytesRead;
                while ((bytesRead = await remoteStream.ReadAsync(buffer.AsMemory(0, buffer.Length))) > 0)
                {
                    await localStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                    totalRead += bytesRead;

                    double percent = totalLength > 0 ? totalRead * 100d / totalLength : 0;
                    string currentSize = FormatFileSize(totalRead);
                    string totalSize = totalLength > 0 ? FormatFileSize(totalLength) : "?";
                    DispatcherQueue.TryEnqueue(() =>
                        SetOnlineDownloadProgress(
                            true,
                            percent,
                            $"正在下载：{currentSize} / {totalSize}",
                            $"Downloading: {currentSize} / {totalSize}"));
                }

                await localStream.FlushAsync();
            }
        }

        if (mod.FileSizeBytes > 0)
        {
            long actualSize = new FileInfo(archivePath).Length;
            if (Math.Abs(actualSize - mod.FileSizeBytes) > 1024)
            {
                throw new InvalidOperationException(L(
                    $"下载文件大小异常，预期约 {FormatFileSize(mod.FileSizeBytes)}，实际只有 {FormatFileSize(actualSize)}。",
                    $"Downloaded file size is incorrect. Expected about {FormatFileSize(mod.FileSizeBytes)}, but only got {FormatFileSize(actualSize)}."));
            }
        }

        EnsureDownloadedFileLooksValid(archivePath, response);

        return archivePath;
    }

    private string EnsureDownloadArchiveExtension(string archivePath)
    {
        if (IsSupportedArchiveFile(archivePath))
        {
            return archivePath;
        }

        string detectedExtension = DetectArchiveExtension(archivePath);
        if (string.IsNullOrWhiteSpace(detectedExtension))
        {
            return archivePath;
        }

        string renamedPath = archivePath + detectedExtension;
        if (File.Exists(renamedPath))
        {
            File.Delete(renamedPath);
        }

        File.Move(archivePath, renamedPath);
        return renamedPath;
    }

    private static string DetectArchiveExtension(string filePath)
    {
        byte[] header = new byte[8];
        using FileStream stream = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        int bytesRead = stream.Read(header, 0, header.Length);

        if (bytesRead >= 4
            && header[0] == 0x50
            && header[1] == 0x4B
            && header[2] is 0x03 or 0x05 or 0x07
            && header[3] is 0x04 or 0x06 or 0x08)
        {
            return ".zip";
        }

        if (bytesRead >= 6
            && header[0] == 0x37
            && header[1] == 0x7A
            && header[2] == 0xBC
            && header[3] == 0xAF
            && header[4] == 0x27
            && header[5] == 0x1C)
        {
            return ".7z";
        }

        if (bytesRead >= 2
            && header[0] == 0x1F
            && header[1] == 0x8B)
        {
            return ".gz";
        }

        return string.Empty;
    }

    private static string FormatFileSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double size = bytes;
        int unitIndex = 0;
        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return $"{size:0.##} {units[unitIndex]}";
    }

    private static string ResolveDownloadFileName(HttpResponseMessage response, OnlineModCard mod)
    {
        string? fileName = response.Content.Headers.ContentDisposition?.FileNameStar
            ?? response.Content.Headers.ContentDisposition?.FileName;
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            return fileName.Trim('"');
        }

        string? pathName = response.RequestMessage?.RequestUri is Uri uri
            ? Path.GetFileName(uri.LocalPath)
            : null;
        if (!string.IsNullOrWhiteSpace(pathName) && Path.HasExtension(pathName))
        {
            return pathName;
        }

        string safeTitle = SanitizeFileName(mod.Title);
        return $"{safeTitle}-{mod.ItemId}.zip";
    }

    private static void EnsureDownloadedFileLooksValid(string archivePath, HttpResponseMessage response)
    {
        string detectedExtension = DetectArchiveExtension(archivePath);
        if (!string.IsNullOrWhiteSpace(detectedExtension))
        {
            return;
        }

        string? mediaType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();
        string preview = ReadFilePreviewText(archivePath, 512);
        if ((mediaType is not null && (mediaType.Contains("text/") || mediaType.Contains("html") || mediaType.Contains("json")))
            || preview.Contains("<html", StringComparison.OrdinalIgnoreCase)
            || preview.Contains("<!doctype", StringComparison.OrdinalIgnoreCase)
            || preview.Contains("too many requests", StringComparison.OrdinalIgnoreCase)
            || preview.Contains("rate limit", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The download returned a web page or text response instead of an archive.");
        }
    }

    private static string ReadFilePreviewText(string filePath, int maxBytes)
    {
        byte[] bytes = File.ReadAllBytes(filePath);
        int length = Math.Min(bytes.Length, maxBytes);
        return Encoding.UTF8.GetString(bytes, 0, length);
    }

    private static string CreateUniqueExtractionFolder(string parentFolder, string title, int itemId)
    {
        string baseName = SanitizeFileName(title);
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = $"mod-{itemId}";
        }

        string candidate = Path.Combine(parentFolder, baseName);
        if (!Directory.Exists(candidate))
        {
            return candidate;
        }

        int suffix = 2;
        while (true)
        {
            string next = Path.Combine(parentFolder, $"{baseName}-{suffix}");
            if (!Directory.Exists(next))
            {
                return next;
            }

            suffix++;
        }
    }

    private async Task ApplyTrackedOnlineModMetadataAsync(string extractFolder, OnlineModCard mod, OnlineModDetails? details)
    {
        string identity = GetTrackedModIdentity(mod.ItemId, mod.ProfileUrl);
        if (!string.IsNullOrWhiteSpace(identity))
        {
            foreach (string duplicatePath in _trackedModOrigins.Values
                .Where(origin =>
                    !string.Equals(origin.Path, extractFolder, StringComparison.CurrentCultureIgnoreCase)
                    && string.Equals(GetTrackedModIdentity(origin.ItemId, origin.ProfileUrl), identity, StringComparison.OrdinalIgnoreCase))
                .Select(origin => origin.Path)
                .ToList())
            {
                _trackedModOrigins.Remove(duplicatePath);
                _trackedModUpdateResults.RemoveAll(result => string.Equals(result.Path, duplicatePath, StringComparison.CurrentCultureIgnoreCase));
            }
        }

        _modLinks[extractFolder] = mod.ProfileUrl;
        _trackedModOrigins[extractFolder] = new TrackedModOrigin
        {
            Path = extractFolder,
            SourceSite = DefaultOnlineSourceSite,
            ItemId = mod.ItemId,
            Title = mod.Title,
            ProfileUrl = mod.ProfileUrl,
            PreviewUrl = mod.PreviewUrl,
            LastKnownUpdatedAt = mod.UpdatedAt
        };

        await SaveOnlinePreviewImageAsync(extractFolder, mod.PreviewUrl);
        if (details is not null)
        {
            await TryImportShortcutBindingsFromOnlineDetailsAsync(extractFolder, details);
        }

        SaveConfig();
        SaveShellConfig();
    }

    private async Task TryImportShortcutBindingsFromOnlineDetailsAsync(string modFolder, OnlineModDetails details)
    {
        if (details.ShortcutBindings.Count == 0)
        {
            return;
        }

        if (_modBindings.TryGetValue(modFolder, out List<ShortcutBinding>? existingBindings)
            && existingBindings.Any(binding =>
                !string.IsNullOrWhiteSpace(binding.Shortcut)
                || !string.IsNullOrWhiteSpace(binding.Action)))
        {
            return;
        }

        List<ShortcutBinding> localizedBindings = await BuildLocalizedShortcutBindingsAsync(details.ShortcutBindings);
        if (localizedBindings.Count == 0)
        {
            return;
        }

        _modBindings[modFolder] = localizedBindings
            .Take(MaxShortcutRows)
            .Select(binding => new ShortcutBinding(binding.Shortcut, binding.Action))
            .ToList();

        if (string.Equals(_currentSecondLevelPath, modFolder, StringComparison.CurrentCultureIgnoreCase))
        {
            SecondLevelFolderItem? selectedItem = GetSelectedSecondLevelItem();
            if (selectedItem is not null && string.Equals(selectedItem.Path, modFolder, StringComparison.CurrentCultureIgnoreCase))
            {
                LoadBindingsForCurrentMod(selectedItem);
            }
        }

        SaveConfig();
    }

    private static string GetTrackedModIdentity(int itemId, string? profileUrl)
    {
        if (itemId > 0)
        {
            return "itemid:" + itemId.ToString(CultureInfo.InvariantCulture);
        }

        if (string.IsNullOrWhiteSpace(profileUrl))
        {
            return string.Empty;
        }

        return profileUrl.Trim().TrimEnd('/').ToLowerInvariant();
    }

    private bool DeduplicateTrackedModOrigins()
    {
        var identityToPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        bool changed = false;

        foreach (TrackedModOrigin origin in _trackedModOrigins.Values
            .OrderByDescending(item => Directory.Exists(item.Path))
            .ThenByDescending(item => item.LastKnownUpdatedAt ?? DateTimeOffset.MinValue)
            .ThenBy(item => item.Path, StringComparer.CurrentCultureIgnoreCase)
            .ToList())
        {
            string identity = GetTrackedModIdentity(origin.ItemId, origin.ProfileUrl);
            if (string.IsNullOrWhiteSpace(identity))
            {
                continue;
            }

            if (identityToPath.TryGetValue(identity, out string? existingPath))
            {
                if (!string.Equals(existingPath, origin.Path, StringComparison.CurrentCultureIgnoreCase))
                {
                    _trackedModOrigins.Remove(origin.Path);
                    _trackedModUpdateResults.RemoveAll(result => string.Equals(result.Path, origin.Path, StringComparison.CurrentCultureIgnoreCase));
                    changed = true;
                }
            }
            else
            {
                identityToPath[identity] = origin.Path;
            }
        }

        return changed;
    }

    private async Task RemoveTrackedModRecordAsync(TrackedModUpdateResult result)
    {
        bool confirmed = await ShowConfirmAsync(
            L($"确定要移除这个 Mod 的更新追踪记录吗？\n\n{result.Title}\n\n这不会删除本地文件夹，只会停止更新检查。",
              $"Remove the update tracking record for this mod?\n\n{result.Title}\n\nThis will not delete the local folder. It only stops update checks."),
            L("移除追踪", "Remove tracking"));
        if (!confirmed)
        {
            return;
        }

        _trackedModOrigins.Remove(result.Path);
        _trackedModUpdateResults.RemoveAll(item => string.Equals(item.Path, result.Path, StringComparison.CurrentCultureIgnoreCase));
        SaveConfig();
        SaveShellConfig();
        SetModUpdateStatus(
            $"已移除 {result.Title} 的追踪记录。",
            $"Removed the tracking record for {result.Title}.");
        RefreshUpdatesPane();
    }

    private async Task SaveOnlinePreviewImageAsync(string modFolder, string? previewUrl)
    {
        if (string.IsNullOrWhiteSpace(previewUrl) || !Uri.TryCreate(previewUrl, UriKind.Absolute, out Uri? previewUri))
        {
            return;
        }

        try
        {
            List<string> existingFiles = Directory.GetFiles(modFolder).ToList();
            if (!string.IsNullOrWhiteSpace(FindPreviewImage(modFolder, existingFiles)))
            {
                return;
            }

            using HttpResponseMessage response = await _httpClient.GetAsync(previewUri);
            response.EnsureSuccessStatusCode();

            string extension = Path.GetExtension(previewUri.AbsolutePath);
            if (string.IsNullOrWhiteSpace(extension) || !ImageExtensions.Contains(extension.ToLowerInvariant()))
            {
                string? mediaType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();
                extension = mediaType switch
                {
                    "image/png" => ".png",
                    "image/webp" => ".webp",
                    "image/gif" => ".gif",
                    "image/bmp" => ".bmp",
                    _ => ".jpg"
                };
            }

            string previewPath = Path.Combine(modFolder, "preview" + extension);
            await using Stream sourceStream = await response.Content.ReadAsStreamAsync();
            await using FileStream fileStream = new(previewPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await sourceStream.CopyToAsync(fileStream);
        }
        catch
        {
        }
    }

    private static bool IsPathInsideDirectory(string candidatePath, string parentPath)
    {
        string normalizedCandidate = Path.GetFullPath(candidatePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string normalizedParent = Path.GetFullPath(parentPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return normalizedCandidate.StartsWith(normalizedParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedCandidate, normalizedParent, StringComparison.OrdinalIgnoreCase);
    }

    private static string SanitizeFileName(string value)
    {
        char[] invalidChars = Path.GetInvalidFileNameChars();
        StringBuilder builder = new(value.Length);
        foreach (char ch in value)
        {
            builder.Append(invalidChars.Contains(ch) ? '_' : ch);
        }

        return builder.ToString().Trim().Trim('.');
    }

    private RepositorySnapshot BuildRepositorySnapshot(WorkspaceRepository repository)
    {
        bool sourceReady;
        bool targetReady;

        try
        {
            sourceReady = !string.IsNullOrWhiteSpace(repository.SourcePath) && Directory.Exists(repository.SourcePath);
        }
        catch (Exception)
        {
            sourceReady = false;
        }

        try
        {
            targetReady = !string.IsNullOrWhiteSpace(repository.TargetPath) && Directory.Exists(repository.TargetPath);
        }
        catch (Exception)
        {
            targetReady = false;
        }

        if (!sourceReady)
        {
            return new RepositorySnapshot(0, 0, targetReady);
        }

        try
        {
            string[] firstLevelDirectories = Directory.GetDirectories(repository.SourcePath);
            int modCount = 0;
            foreach (string firstLevelDirectory in firstLevelDirectories)
            {
                try
                {
                    modCount += Directory.GetDirectories(firstLevelDirectory).Length;
                }
                catch (Exception)
                {
                    // Skip unreadable folders so one bad directory does not crash the dashboard.
                }
            }

            return new RepositorySnapshot(firstLevelDirectories.Length, modCount, sourceReady && targetReady);
        }
        catch (Exception)
        {
            return new RepositorySnapshot(0, 0, targetReady);
        }
    }

    private UIElement CreateDashboardCard(WorkspaceRepository repository, RepositorySnapshot snapshot)
    {
        var border = new Border
        {
            Style = (Style)Application.Current.Resources["InsetBorderStyle"],
            Padding = new Thickness(14)
        };

        var stackPanel = new StackPanel { Spacing = 6 };
        stackPanel.Children.Add(new TextBlock
        {
            Text = repository.Name,
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        stackPanel.Children.Add(new TextBlock
        {
            Text = $"{L("源目录", "Source")}: {TrimPreviewPath(repository.SourcePath)}",
            Opacity = 0.74,
            TextWrapping = TextWrapping.Wrap
        });
        stackPanel.Children.Add(new TextBlock
        {
            Text = $"{L("目标目录", "Target")}: {TrimPreviewPath(repository.TargetPath)}",
            Opacity = 0.74,
            TextWrapping = TextWrapping.Wrap
        });
        stackPanel.Children.Add(new TextBlock
        {
            Text = $"{L("分类数", "Categories")}: {snapshot.FirstLevelCount}    {L("Mod数", "Mods")}: {snapshot.ModCount}",
            FontWeight = Microsoft.UI.Text.FontWeights.Medium
        });
        stackPanel.Children.Add(new TextBlock
        {
            Text = snapshot.IsReady ? L("路径状态：已就绪", "Path status: Ready") : L("路径状态：待处理", "Path status: Needs attention"),
            Foreground = snapshot.IsReady ? CopiedBrush : MissingBrush
        });

        border.Child = stackPanel;
        return border;
    }

    private static string TrimPreviewPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "-";
        }

        return path.Length > 48 ? "..." + path[^45..] : path;
    }

    private void OnDashboardNavClicked(object sender, RoutedEventArgs e)
    {
        SaveSelectedRepositoryFromInputs();
        _currentPrimarySection = PrimarySection.Dashboard;
        ApplyShellState(refreshRepository: false);
    }

    private void OnRepositoryNavClicked(object sender, RoutedEventArgs e)
    {
        SaveSelectedRepositoryFromInputs();
        _currentPrimarySection = PrimarySection.Repository;
        ApplyShellState(refreshRepository: true);
    }

    private void OnOnlineNavClicked(object sender, RoutedEventArgs e)
    {
        SaveSelectedRepositoryFromInputs();
        if (_selectedRepositoryId is null && _repositories.Count > 0)
        {
            _selectedRepositoryId = _repositories[0].Id;
        }

        _currentPrimarySection = PrimarySection.Online;
        ApplyShellState(refreshRepository: false);
    }

    private void OnUpdatesNavClicked(object sender, RoutedEventArgs e)
    {
        SaveSelectedRepositoryFromInputs();
        _currentPrimarySection = PrimarySection.Updates;
        ApplyShellState(refreshRepository: false);
    }

    private void OnSettingsNavClicked(object sender, RoutedEventArgs e)
    {
        SaveSelectedRepositoryFromInputs();
        _currentPrimarySection = PrimarySection.Settings;
        ApplyShellState(refreshRepository: false);
    }

    private void OnSecondaryNavButtonClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string key })
        {
            return;
        }

        SaveSelectedRepositoryFromInputs();
        _selectedRepositoryId = key == "all-repositories" ? null : key;
        _onlineCurrentPage = 1;
        _onlineTotalCount = 0;
        _onlineTotalPages = 1;
        _onlineCharacterFilter = string.Empty;
        _onlineKnownCharacters.Clear();
        _lastLoadedOnlineConfigKey = null;
        _onlineMods.Clear();
        ApplyShellState(refreshRepository: _currentPrimarySection == PrimarySection.Repository);
    }

    private async void OnAddRepositoryClicked(object sender, RoutedEventArgs e)
    {
        string? repositoryName = await PromptForTextAsync(
            L("输入新仓库名称，后面可以继续补充路径和用途。", "Enter a repository name. You can expand its settings later."),
            L("新建仓库", "Create Repository"),
            L("例如：原神正式 / ZZZ Test", "For example: GI Main / ZZZ Test"));

        if (string.IsNullOrWhiteSpace(repositoryName))
        {
            return;
        }

        SaveSelectedRepositoryFromInputs();

        var repository = new WorkspaceRepository
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = repositoryName.Trim(),
            OnlineSourceSite = DefaultOnlineSourceSite,
            OnlineCategoryId = DefaultOnlineCategoryId
        };

        _repositories.Add(repository);
        _selectedRepositoryId = repository.Id;
        _onlineCurrentPage = 1;
        _onlineTotalCount = 0;
        _onlineTotalPages = 1;
        _onlineCharacterFilter = string.Empty;
        _onlineKnownCharacters.Clear();
        _lastLoadedOnlineConfigKey = null;
        _onlineMods.Clear();
        _currentPrimarySection = PrimarySection.Repository;
        ApplySelectedRepositoryToInputs();
        SaveConfig();
        ApplyShellState(refreshRepository: false);
        RefreshDashboard();
        StatusTextBlock.Text = L($"已创建仓库：{repository.Name}", $"Created repository: {repository.Name}");
    }

    private async void OnRenameRepositoryClicked(object sender, RoutedEventArgs e)
    {
        WorkspaceRepository? repository = GetSelectedRepository();
        if (repository is null || _selectedRepositoryId is null)
        {
            return;
        }

        string? repositoryName = await PromptForTextAsync(
            L("输入新的仓库名称。", "Enter a new repository name."),
            L("重命名仓库", "Rename Repository"),
            repository.Name);

        if (string.IsNullOrWhiteSpace(repositoryName))
        {
            return;
        }

        repository.Name = repositoryName.Trim();
        SaveShellConfig();
        RefreshSecondaryNavigation();
        RefreshDashboard();
        RefreshRepositoryActionButtons();
        StatusTextBlock.Text = L($"已重命名仓库：{repository.Name}", $"Renamed repository: {repository.Name}");
    }

    private async void OnAddRepositoryDetailsClicked(object sender, RoutedEventArgs e)
    {
        SaveSelectedRepositoryFromInputs();

        WorkspaceRepository draftRepository = new()
        {
            Name = string.Empty,
            SourcePath = SourceTextBox.Text ?? string.Empty,
            TargetPath = TargetTextBox.Text ?? string.Empty,
            LauncherPath = LauncherTextBox.Text ?? string.Empty
        };

        RepositoryEditorResult? result = await PromptForRepositoryAsync(
            L("创建一个新的自定义仓库，并一次性补充它的主要路径信息。", "Create a new custom repository and fill in its primary paths in one step."),
            L("新建仓库", "Create Repository"),
            draftRepository);

        if (result is null || string.IsNullOrWhiteSpace(result.Name))
        {
            return;
        }

        WorkspaceRepository repository = new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = result.Name.Trim(),
            SourcePath = result.SourcePath.Trim(),
            TargetPath = result.TargetPath.Trim(),
            LauncherPath = result.LauncherPath.Trim(),
            OnlineSourceSite = result.OnlineSourceSite.Trim(),
            OnlineCategoryId = result.OnlineCategoryId.Trim(),
            Notes = result.Notes.Trim()
        };

        _repositories.Add(repository);
        _selectedRepositoryId = repository.Id;
        _currentPrimarySection = PrimarySection.Repository;
        _onlineCurrentPage = 1;
        _onlineTotalCount = 0;
        _onlineTotalPages = 1;
        _onlineCharacterFilter = string.Empty;
        _onlineKnownCharacters.Clear();
        _lastLoadedOnlineConfigKey = null;
        _onlineMods.Clear();
        ApplySelectedRepositoryToInputs();
        SaveConfig();
        ApplyShellState(refreshRepository: false);
        RefreshDashboard();
        StatusTextBlock.Text = L($"已创建仓库：{repository.Name}", $"Created repository: {repository.Name}");
    }

    private async void OnEditRepositoryDetailsClicked(object sender, RoutedEventArgs e)
    {
        WorkspaceRepository? repository = GetSelectedRepository();
        if (repository is null || _selectedRepositoryId is null)
        {
            return;
        }

        SaveSelectedRepositoryFromInputs();

        RepositoryEditorResult? result = await PromptForRepositoryAsync(
            L("编辑当前仓库的名称、路径和预留的在线来源信息。", "Edit the current repository name, paths, and reserved online-source fields."),
            L("编辑仓库", "Edit Repository"),
            repository);

        if (result is null || string.IsNullOrWhiteSpace(result.Name))
        {
            return;
        }

        repository.Name = result.Name.Trim();
        repository.SourcePath = result.SourcePath.Trim();
        repository.TargetPath = result.TargetPath.Trim();
        repository.LauncherPath = result.LauncherPath.Trim();
        repository.OnlineSourceSite = result.OnlineSourceSite.Trim();
        repository.OnlineCategoryId = result.OnlineCategoryId.Trim();
        repository.Notes = result.Notes.Trim();
        _onlineCurrentPage = 1;
        _onlineTotalCount = 0;
        _onlineTotalPages = 1;
        _onlineCharacterFilter = string.Empty;
        _onlineKnownCharacters.Clear();
        _lastLoadedOnlineConfigKey = null;
        _onlineMods.Clear();

        ApplySelectedRepositoryToInputs();
        SaveShellConfig();
        RefreshSecondaryNavigation();
        RefreshDashboard();
        RefreshRepositoryActionButtons();
        StatusTextBlock.Text = L($"已更新仓库：{repository.Name}", $"Updated repository: {repository.Name}");
    }

    private async void OnDeleteRepositoryClicked(object sender, RoutedEventArgs e)
    {
        WorkspaceRepository? repository = GetSelectedRepository();
        if (repository is null || _selectedRepositoryId is null)
        {
            return;
        }

        if (_repositories.Count <= 1)
        {
            await ShowMessageAsync(
                L("至少要保留一个仓库。", "At least one repository must remain."),
                L("无法删除", "Cannot delete"));
            return;
        }

        bool confirmed = await ShowConfirmAsync(
            L($"确定要删除这个仓库吗？\n\n{repository.Name}\n\n仓库配置会被移除，但不会删除磁盘上的 Mod 文件夹。",
              $"Delete this repository?\n\n{repository.Name}\n\nThe repository configuration will be removed, but no files on disk will be deleted."),
            L("首次确认", "First confirmation"));
        if (!confirmed)
        {
            return;
        }

        bool confirmedAgain = await ShowConfirmAsync(
            L($"请再次确认删除仓库：{repository.Name}\n\n此操作无法撤销。",
              $"Please confirm again to delete repository: {repository.Name}\n\nThis action cannot be undone."),
            L("第二次确认", "Second confirmation"));
        if (!confirmedAgain)
        {
            return;
        }

        int currentIndex = _repositories.FindIndex(item => item.Id == repository.Id);
        _repositories.RemoveAll(item => item.Id == repository.Id);

        if (_repositories.Count > 0)
        {
            int nextIndex = Math.Clamp(currentIndex, 0, _repositories.Count - 1);
            _selectedRepositoryId = _repositories[nextIndex].Id;
        }
        else
        {
            _selectedRepositoryId = null;
        }

        if (_currentPrimarySection == PrimarySection.Dashboard)
        {
            _selectedRepositoryId = null;
        }
        else
        {
            ApplySelectedRepositoryToInputs();
        }

        SaveShellConfig();
        ApplyShellState(refreshRepository: _currentPrimarySection == PrimarySection.Repository);
        StatusTextBlock.Text = L($"已删除仓库：{repository.Name}", $"Deleted repository: {repository.Name}");
    }

    private void ApplyLanguage()
    {
        Title = L($"集成化mod管理器 {AppVersion}", $"Integrated Mod Manager {AppVersion}");

        BetaTitleTextBlock.Text = _shellLayoutMode == ShellLayoutMode.Compact
            ? "管理器"
            : L("工作区", "Workspace");
        BetaCaptionTextBlock.Text = L("统一管理仓库切换、在线资源浏览和本地 Mod 操作。", "Manage repository switching, online resources, and local mod actions in one place.");
        ApplyPrimaryNavigationContent();

        SecondaryNavTitleTextBlock.Text = _currentPrimarySection switch
        {
            PrimarySection.Dashboard => L("仓库预览", "Repository Preview"),
            PrimarySection.Repository => L("仓库切换", "Repository Switcher"),
            PrimarySection.Online => L("在线来源", "Online Sources"),
            PrimarySection.Updates => L("更新导航", "Updates Navigation"),
            _ => L("设置导航", "Settings Navigation")
        };
        SecondaryNavHintTextBlock.Text = _currentPrimarySection switch
        {
            PrimarySection.Dashboard => L("查看全部仓库或单个仓库的预览数据。", "Browse summary data for all repositories or a single repository."),
            PrimarySection.Repository => L("从这里切换工作区，主区继续复用现有单仓库逻辑。", "Switch workspaces here while reusing the existing single-repository view."),
            PrimarySection.Online => L("在线页会按当前仓库的来源映射加载后续内容。", "The online page will use the selected repository as its source mapping context."),
            PrimarySection.Updates => L("这里会汇总已追踪 Mod 的更新状态，也可以手动检查或设置自动检查频率。", "This page summarizes tracked mod updates and lets you run checks manually or on a schedule."),
            _ => L("在这里统一管理语言、主题、更新检查和项目链接。", "Manage language, theme, update checks, and project links here.")
        };
        ToolTipService.SetToolTip(AddRepositoryButton, L("新建仓库", "Create repository"));
        ToolTipService.SetToolTip(RenameRepositoryButton, L("编辑当前仓库", "Edit current repository"));
        ToolTipService.SetToolTip(DeleteRepositoryButton, L("删除当前仓库", "Delete current repository"));
        ToolTipService.SetToolTip(OpenGitHubButton, L("打开 GitHub 仓库", "Open GitHub repository"));
        ToolTipService.SetToolTip(CheckUpdatesButton, L("检查软件版本更新", "Check for app updates"));
        ToolTipService.SetToolTip(CheckModUpdatesButton, L("手动检查已追踪 Mod 更新", "Check tracked mod updates now"));
        ToolTipService.SetToolTip(EditOnlineRepositoryButton, L("编辑当前仓库在线配置", "Edit current repository source config"));
        ToolTipService.SetToolTip(RefreshOnlineButton, L("刷新在线 Mod 列表", "Refresh online mod list"));
        ToolTipService.SetToolTip(OnlinePrevPageButton, L("上一页", "Previous page"));
        ToolTipService.SetToolTip(OnlineNextPageButton, L("下一页", "Next page"));
        ToolTipService.SetToolTip(OnlineSearchTextBox, L("搜索角色名或 Mod 名称", "Search character or mod name"));
        ToolTipService.SetToolTip(OnlineSortComboBox, L("切换当前在线列表的排序方式", "Change the sort order for the current online list"));
        AutomationProperties.SetName(AddRepositoryButton, L("新建仓库", "Create repository"));
        AutomationProperties.SetName(RenameRepositoryButton, L("编辑当前仓库", "Edit current repository"));
        AutomationProperties.SetName(DeleteRepositoryButton, L("删除当前仓库", "Delete current repository"));
        AutomationProperties.SetName(OpenGitHubButton, L("打开 GitHub 仓库", "Open GitHub repository"));
        AutomationProperties.SetName(CheckUpdatesButton, L("检查软件版本更新", "Check for app updates"));
        AutomationProperties.SetName(CheckModUpdatesButton, L("手动检查已追踪 Mod 更新", "Check tracked mod updates now"));
        AutomationProperties.SetName(EditOnlineRepositoryButton, L("编辑当前仓库在线配置", "Edit current repository source config"));
        AutomationProperties.SetName(RefreshOnlineButton, L("刷新在线 Mod 列表", "Refresh online mod list"));
        AutomationProperties.SetName(OnlinePrevPageButton, L("上一页", "Previous page"));
        AutomationProperties.SetName(OnlineNextPageButton, L("下一页", "Next page"));
        AutomationProperties.SetName(OnlineSearchTextBox, L("搜索角色名或 Mod 名称", "Search character or mod name"));
        AutomationProperties.SetName(OnlineSortComboBox, L("在线列表排序", "Online list sort"));
        InitializeOnlineControls();

        DashboardTitleTextBlock.Text = L("仓库总览", "Repository Dashboard");
        DashboardSubtitleTextBlock.Text = L("这里会汇总全部仓库的路径状态和预览数据。", "This dashboard summarizes repository path status and preview data.");
        DashboardRepoCountLabelTextBlock.Text = L("仓库数量", "Repositories");
        DashboardModCountLabelTextBlock.Text = L("Mod 总数", "Total Mods");
        DashboardReadyCountLabelTextBlock.Text = L("已就绪路径", "Ready Paths");
        DashboardRepositoriesTitleTextBlock.Text = L("仓库卡片", "Repository Cards");

        OnlineTitleTextBlock.Text = L("在线 Mod 浏览", "Online Mod Browser");
        OnlineSubtitleTextBlock.Text = L("这里保留在线 Mod 列表和右侧预览详情；仓库在线配置已经移动到“设置”页面。", "This page now keeps the online mod list and the right-side preview details. Repository online config has been moved to Settings.");
        OnlineRepositoryLabelTextBlock.Text = L("当前仓库", "Repository");
        OnlineSourceLabelTextBlock.Text = L("在线来源", "Online source");
        OnlineReadyLabelTextBlock.Text = L("准备状态", "Readiness");
        OnlineConfigTitleTextBlock.Text = L("当前仓库映射", "Current repository mapping");
        OnlineConfigHintTextBlock.Text = L("在线页面会优先读取当前仓库的来源站点、分类 ID 和备注说明。你也可以在这里直接编辑当前仓库。", "The online page reads the current repository's source site, category ID, and notes first. You can also edit the current repository directly here.");
        OnlineCategoryLabelTextBlock.Text = L("分类 ID", "Category ID");
        OnlineNotesLabelTextBlock.Text = L("备注", "Notes");
        OnlinePreviewTitleTextBlock.Text = L("在线 Mod 列表", "Online Mod List");
        OnlinePreviewHintTextBlock.Text = L("这里读取的是外网 Mod 站点数据，如果加载较慢或失败，可能需要 VPN。", "This page loads data from an external mod site. If loading is slow or fails, a VPN may be required.");
        EditOnlineRepositoryButton.Content = L("编辑当前仓库在线配置", "Edit current repository source config");

        SettingsTitleTextBlock.Text = L("应用设置", "Application Settings");
        SettingsSubtitleTextBlock.Text = L("在这里配置语言、主题、更新检查和项目链接。", "Configure language, theme, update checks, and project links here.");
        SettingsAppearanceTitleTextBlock.Text = L("界面与语言", "Appearance and Language");
        SettingsProjectTitleTextBlock.Text = L("项目链接与软件版本", "Project Links and App Version");
        SettingsProjectHintTextBlock.Text = L("这里保留 GitHub 仓库入口和软件版本检查；Mod 更新请使用左侧的“更新”模块。", "This section keeps the GitHub repository link and app-version checks. Use the Updates section in the left navigation for mod updates.");
        SettingsPlaceholderTitleTextBlock.Text = L("仓库在线配置", "Repository Online Config");
        SettingsPlaceholderTextBlock.Text = L("在线 Mod 页面需要的来源站点、分类 ID 和备注都集中放在这里管理。", "Manage the source site, category ID, and notes used by the online mod page here.");
        OpenGitHubButton.Content = L("打开 GitHub 仓库", "Open GitHub Repository");
        CheckUpdatesButton.Content = _isCheckingUpdates ? L("检查中...", "Checking...") : L("检查软件更新", "Check App Updates");
        UpdateStatusTextBlock.Text = string.IsNullOrWhiteSpace(UpdateStatusTextBlock.Text)
            ? L($"当前版本：{AppVersion}", $"Current version: {AppVersion}")
            : UpdateStatusTextBlock.Text;

        HeaderTitleTextBlock.Text = L("集成化mod管理器", "Integrated Mod Manager");
        HeaderFrameworkBadgeTextBlock.Text = "WinUI 3";
        HeaderVersionBadgeTextBlock.Text = AppVersion;
        HeaderSubtitleTextBlock.Text = L(
            "管理两层 Mod 文件夹、预览图、ZIP 导入、快捷键说明和启动器入口。",
            "Manage two-level mod folders, preview images, ZIP imports, per-mod shortcut notes, and launcher access.");
        HeaderCaptionTextBlock.Text = L(
            "左侧先选第一层分类，再选第二层 Mod。双击第二层可直接复制或移除。",
            "Select a first-level category first, then a second-level mod. Double-click a mod to copy or remove it.");

        LanguageToggleButton.Content = _currentLanguage == AppLanguage.ZhCn ? "English" : "中文";
        LanguageToggleButton.IsEnabled = true;
        ThemeToggleButton.Content = _isDarkTheme ? L("切换浅色", "Light theme") : L("切换深色", "Dark theme");

        PathSectionTitleTextBlock.Text = L("路径与启动器", "Paths and Launcher");
        PathSectionSubtitleTextBlock.Text = L(
            "把主文件夹、副文件夹和启动器统一放在这里，常用操作会直接使用这些路径。",
            "Keep the source folder, target folder, and launcher together here for quick access.");
        SourceLabelTextBlock.Text = L("源文件夹", "Source");
        TargetLabelTextBlock.Text = L("目标文件夹", "Target");
        LauncherLabelTextBlock.Text = L("启动器", "Launcher");
        PickSourceButton.Content = L("选择", "Browse");
        PickTargetButton.Content = L("选择", "Browse");
        PickLauncherButton.Content = L("选择程序", "Browse EXE");
        OpenSourceButton.Content = L("打开源文件夹", "Open Source");
        OpenTargetButton.Content = L("打开目标文件夹", "Open Target");
        OpenLauncherButton.Content = L("打开启动器位置", "Open Launcher Folder");
        LauncherTextBox.PlaceholderText = L("选择 XXMI Launcher.exe 或其他启动器", "Select XXMI Launcher.exe or another launcher");
        PathHintTextBlock.Text = L(
            "支持刷新目录、导入 ZIP、复制当前第二层文件夹，以及直接运行外部启动器。",
            "Refresh folders, import ZIP files, copy the selected second-level folder, or run the external launcher.");
        RefreshButton.Content = L("刷新目录", "Refresh");
        ImportZipButton.Content = L("导入到当前选中文件夹", "Import To Selected Folder");
        RunLauncherButton.Content = L("运行启动器", "Run Launcher");
        ToggleCopyButton.Content = L("复制当前第二层文件夹", "Copy Selected Mod");

        FirstCountLabelTextBlock.Text = L("第一层文件夹", "First-level folders");
        FirstCountHintTextBlock.Text = L("当前 Mod 仓库中的分类数量", "Number of categories in the source folder");
        SecondCountLabelTextBlock.Text = L("第二层文件夹", "Second-level folders");
        SecondCountHintTextBlock.Text = L("当前扫描到的 Mod 总数", "Total mod folders found in the current source");
        CurrentStateLabelTextBlock.Text = L("当前复制状态", "Copy status");
        CurrentStateHintTextBlock.Text = L("根据目标文件夹中的同名目录判断", "Detected from folders with the same name in the target");
        CurrentFolderLabelTextBlock.Text = L("当前第二层文件夹", "Current second-level folder");
        CurrentFolderHintTextBlock.Text = L("这里显示当前选中的 Mod 名称", "Shows the currently selected mod name");

        FirstLevelSectionTitleTextBlock.Text = L("第一层文件夹", "First-level folders");
        FirstLevelSectionSubtitleTextBlock.Text = L("先选择分类目录", "Choose a category folder first");
        FirstLevelSearchTextBox.PlaceholderText = L("搜索第一层文件夹", "Search first-level folders");
        CreateFirstLevelButton.Content = L("新建第一层文件夹", "New Folder");
        RenameFirstLevelButton.Content = L("重命名当前第一层", "Rename");
        SecondLevelSectionTitleTextBlock.Text = L("第二层文件夹", "Second-level folders");
        SecondLevelSectionSubtitleTextBlock.Text = L("再选择具体 Mod", "Then choose a specific mod");
        DeleteSecondLevelButton.Content = L("删除当前第二层 Mod", "Delete Mod");

        ShortcutSectionTitleTextBlock.Text = L("快捷键与功能", "Shortcut Notes");
        ShortcutSectionSubtitleTextBlock.Text = L("当前选中 Mod 的快捷键说明", "Shortcut notes for the selected mod");
        AddShortcutRowButton.Content = L("新增一行", "Add Row");
        ShortcutHintTextBlock.Text = L(
            "快捷键需要至少两个按键组合。先点选快捷键输入框，再按下组合键即可自动录入；当前窗口聚焦时，会定位并执行对应 Mod。",
            "Shortcuts must use at least a two-key combination. Click a shortcut box first, then press the combination to capture it; when this window is focused, it will locate and run the corresponding mod.");

        PreviewSectionTitleTextBlock.Text = L("默认图片预览", "Image Preview");
        PreviewSectionSubtitleTextBlock.Text = L(
            "优先 preview / cover / thumbnail / image，也支持把图片拖到这里。",
            "Prefers preview / cover / thumbnail / image, and also supports dragging an image here.");
        ModLinkSectionTitleTextBlock.Text = L("Mod 链接", "Mod Link");
        ModLinkSectionSubtitleTextBlock.Text = L(
            "为当前 Mod 绑定一个网页链接，可直接快速访问。",
            "Bind a web link to the current mod for quick access.");
        ModLinkTextBox.PlaceholderText = L("粘贴当前 Mod 对应的网址", "Paste the web link for the current mod");
        PreviewFooterTextBlock.Text = L(
            "预览区会显示当前第二层文件夹的图片，下方链接可一键用默认浏览器打开。",
            "The preview shows the current second-level folder image, and the link below opens in the default browser.");
        OpenModLinkButton.Content = L("快速访问", "Open Link");

        ProgressTextBlock.Text = string.IsNullOrWhiteSpace(ProgressTextBlock.Text)
            ? L("当前无复制任务", "No active copy task")
            : ProgressTextBlock.Text;
        AuthorTextBlock.Text = L("工具作者：uyujkk", "Tool author: uyujkk");

        ApplyShortcutPlaceholders();
        UpdateShortcutRowVisibility();
        UpdateLinkButtonState();
        RefreshLocalizedStates();
        ApplyTextOverrides();
        ApplyActionButtonPresentation();

        if (FirstLevelListView.SelectedItem is FirstLevelFolderItem firstItem)
        {
            string? selectedPath = _currentSecondLevelPath;
            PopulateSecondLevelList(firstItem, preserveStatus: true);
            SelectSecondLevelByPath(selectedPath);
        }
        else if (GetSelectedSecondLevelItem() is SecondLevelFolderItem secondItem)
        {
            ShowSecondLevelDetails(secondItem, preserveStatus: true);
        }
        else
        {
            CurrentFolderTextBlock.Text = StateNotSelectedText;
            CurrentStateTextBlock.Text = StateNotSelectedText;
            SetStateColor(CurrentStateTextBlock.Text);
            ClearPreview();
            SetDefaultStatus();
        }
    }

    private void ApplyTextOverrides()
    {
        PathSectionSubtitleTextBlock.Text = L(
            "把 Mod 存储文件夹、目标文件夹和启动器统一放在这里，常用操作可以直接使用这些路径。",
            "Keep the mod storage folder, target folder, and launcher together here for quick access.");
        SourceLabelTextBlock.Text = L("Mod 存储文件夹", "Mod Storage");
        TargetLabelTextBlock.Text = L("目标文件夹", "Target Folder");
        OpenSourceButton.Content = L("打开 Mod 存储文件夹", "Open Mod Storage");
        OpenTargetButton.Content = L("打开目标文件夹", "Open Target Folder");
        PathHintTextBlock.Text = L(
            "可以刷新目录、导入 ZIP、将当前第二层 Mod 复制到目标文件夹，也可以直接运行外部启动器。",
            "Refresh folders, import ZIP files, copy the selected second-level mod into the target folder, or run the external launcher.");
        ImportZipButton.Content = L("导入到当前选中文件夹", "Import To Selected Folder");

        FirstCountHintTextBlock.Text = L("当前 Mod 存储文件夹中的分类数量", "Number of categories in the mod storage folder");

        ShortcutSectionTitleTextBlock.Text = L("快捷键与描述", "Shortcut and Description");
        ShortcutSectionSubtitleTextBlock.Text = L("为当前选中的 Mod 记录快捷键和描述", "Record a shortcut and description for the selected mod");
        ShortcutHintTextBlock.Text = L(
            "点击快捷键输入框后可直接按键录入，支持单键、组合键和符号键；当前窗口聚焦时，按已绑定的快捷键会定位并执行对应 Mod。",
            "Click a shortcut box and press a key to capture it. Single keys, key combinations, and symbol keys are supported; when this window is focused, the bound shortcut will locate and run the corresponding mod.");
    }

    private void ApplyActionButtonPresentation()
    {
        CreateFirstLevelButton.Content = new FontIcon { Glyph = "\uE710", FontSize = 16 };
        RenameFirstLevelButton.Content = new FontIcon { Glyph = "\uE70F", FontSize = 16 };
        DeleteSecondLevelButton.Content = new FontIcon { Glyph = "\uE74D", FontSize = 16 };

        string createTip = L("新建第一层文件夹", "Create first-level folder");
        string renameTip = L("重命名当前第一层", "Rename first-level folder");
        string deleteTip = L("删除当前第二层 Mod", "Delete selected mod");

        ToolTipService.SetToolTip(CreateFirstLevelButton, createTip);
        ToolTipService.SetToolTip(RenameFirstLevelButton, renameTip);
        ToolTipService.SetToolTip(DeleteSecondLevelButton, deleteTip);

        AutomationProperties.SetName(CreateFirstLevelButton, createTip);
        AutomationProperties.SetName(RenameFirstLevelButton, renameTip);
        AutomationProperties.SetName(DeleteSecondLevelButton, deleteTip);
    }

    private void ApplyShortcutPlaceholders()
    {
        foreach (TextBox box in _shortcutKeyBoxes)
        {
                box.PlaceholderText = L("快捷键", "Shortcut");
        }

        foreach (TextBox box in _shortcutActionBoxes)
        {
            box.PlaceholderText = L("鎻忚堪", "Description");
        }
    }

    private void SetDefaultStatus()
    {
        StatusTextBlock.Text = L(
                    $"请选择 Mod 存储文件夹和目标文件夹。当前版本：{AppVersion}",
            $"Choose a mod storage folder and target folder. Current version: {AppVersion}");
    }

    private void BuildShortcutRows()
    {
        ShortcutRowsPanel.Children.Clear();
        _shortcutKeyBorders.Clear();
        _shortcutKeyBoxes.Clear();
        _shortcutActionBoxes.Clear();

        for (int index = 0; index < MaxShortcutRows; index++)
        {
            var rowGrid = new Grid { ColumnSpacing = 12 };
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });

            var keyBorder = CreateInsetBorder();
            var keyBox = new TextBox
            {
                IsReadOnly = true,
                Style = (Style)Application.Current.Resources["ShortcutInputTextBoxStyle"]
            };
            ConfigureShortcutTextBoxTheme(keyBox);
            AttachTrackedTextInput(keyBox);
            keyBox.TextChanged += OnShortcutTextChanged;
            keyBox.KeyDown += OnShortcutKeyBoxKeyDown;
            keyBox.GotFocus += OnShortcutKeyBoxGotFocus;
            keyBox.LostFocus += OnShortcutKeyBoxLostFocus;
            keyBorder.Child = keyBox;

            var actionBorder = CreateInsetBorder();
            var actionBox = new TextBox
            {
                Style = (Style)Application.Current.Resources["ShortcutInputTextBoxStyle"]
            };
            ConfigureShortcutTextBoxTheme(actionBox);
            AttachTrackedTextInput(actionBox);
            actionBox.TextChanged += OnShortcutTextChanged;
            actionBorder.Child = actionBox;

            Grid.SetColumn(keyBorder, 0);
            Grid.SetColumn(actionBorder, 1);
            rowGrid.Children.Add(keyBorder);
            rowGrid.Children.Add(actionBorder);

            ShortcutRowsPanel.Children.Add(rowGrid);
            _shortcutKeyBorders.Add(keyBorder);
            _shortcutKeyBoxes.Add(keyBox);
            _shortcutActionBoxes.Add(actionBox);
        }

        OnlineTitleTextBlock.Text = L("在线 Mod 浏览", "Online Mod Browser");
        OnlineSubtitleTextBlock.Text = L("这里只保留在线 Mod 列表和右侧详情预览；仓库在线配置已经移到设置页。", "This page now focuses on the online mod list and the right-side detail preview. Repository online config has moved to Settings.");
        OnlineRepositoryLabelTextBlock.Text = L("当前仓库", "Repository");
        OnlineSourceLabelTextBlock.Text = L("在线来源", "Online source");
        OnlineReadyLabelTextBlock.Text = L("准备状态", "Readiness");
        OnlineConfigTitleTextBlock.Text = L("当前仓库映射", "Current repository mapping");
        OnlineConfigHintTextBlock.Text = L("在线页面会优先读取当前仓库的来源站点、分类 ID 和备注说明。未填写时会默认使用 GameBanana / 21842。你也可以在这里直接编辑当前仓库。", "The online page reads the current repository's source site, category ID, and notes first. If omitted, it falls back to GameBanana / 21842. You can also edit the current repository directly here.");
        OnlineCategoryLabelTextBlock.Text = L("分类 ID", "Category ID");
        OnlineNotesLabelTextBlock.Text = L("备注", "Notes");
        OnlinePreviewTitleTextBlock.Text = L("在线 Mod 列表", "Online Mod List");
        OnlinePreviewHintTextBlock.Text = L("当前会从外网 Mod 站点读取列表；如果加载缓慢或失败，可能需要 VPN。详情页支持实验性自动翻译。", "This page loads data from an external mod site. If loading is slow or fails, a VPN may be required. The detail view includes experimental auto-translation.");
        EditOnlineRepositoryButton.Content = L("编辑当前仓库在线配置", "Edit current repository source config");
        RefreshOnlineButton.Content = _isLoadingOnlineMods ? L("加载中...", "Loading...") : L("刷新在线列表", "Refresh Online List");
        OnlinePrevPageButton.Content = L("上一页", "Previous");
        OnlineNextPageButton.Content = L("下一页", "Next");
        OpenOnlineDetailPageButton.Content = L("打开原页面", "Open Original Page");
        DownloadOnlineDetailButton.Content = L("下载并解压", "Download and Extract");
        if (_activeOnlineDetailMod is null)
        {
            ResetOnlineDetailPaneToPlaceholder();
        }
        OnlineDetailRequirementTitleTextBlock.Text = L("可能存在额外访问要求", "Possible extra access requirements");
        ApplyShortcutPlaceholders();
        UpdateShortcutRowVisibility();
        UpdateShortcutKeyFocusVisuals();
    }

    private static Border CreateInsetBorder()
    {
        return new Border
        {
            Style = (Style)Application.Current.Resources["InsetBorderStyle"]
        };
    }

    private static void ConfigureShortcutTextBoxTheme(TextBox box)
    {
        var transparentBrush = new SolidColorBrush(Colors.Transparent);
        box.Resources["TextControlBackground"] = transparentBrush;
        box.Resources["TextControlBackgroundFocused"] = transparentBrush;
        box.Resources["TextControlBackgroundPointerOver"] = transparentBrush;
        box.Resources["TextControlBackgroundDisabled"] = transparentBrush;
        box.Resources["TextControlBackgroundReadOnly"] = transparentBrush;
        box.Resources["TextControlBorderBrush"] = transparentBrush;
        box.Resources["TextControlBorderBrushFocused"] = transparentBrush;
        box.Resources["TextControlBorderBrushPointerOver"] = transparentBrush;
        box.Resources["TextControlBorderBrushDisabled"] = transparentBrush;
        box.Resources["TextControlBorderBrushReadOnly"] = transparentBrush;
        box.Resources["TextControlForegroundReadOnly"] = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
    }

    private void OnShortcutKeyBoxGotFocus(object sender, RoutedEventArgs e)
    {
        _activeShortcutKeyBox = sender as TextBox;
        UpdateShortcutKeyFocusVisuals();
    }

    private void OnShortcutKeyBoxLostFocus(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(_activeShortcutKeyBox, sender))
        {
            _activeShortcutKeyBox = null;
        }

        UpdateShortcutKeyFocusVisuals();
    }

    private void UpdateShortcutKeyFocusVisuals()
    {
        Brush normalBackground = (Brush)Application.Current.Resources["AppInsetBackgroundBrush"];
        Brush normalBorder = (Brush)Application.Current.Resources["AppCardBorderBrush"];
        Brush activeBackground = (Brush)Application.Current.Resources["AppAccentSoftBrush"];
        Brush activeBorder = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
        object? focusedElement = Content is FrameworkElement root && root.XamlRoot is not null
            ? FocusManager.GetFocusedElement(root.XamlRoot)
            : null;

        for (int i = 0; i < _shortcutKeyBorders.Count; i++)
        {
            bool isActive = i < _shortcutKeyBoxes.Count &&
                ReferenceEquals(_shortcutKeyBoxes[i], _activeShortcutKeyBox) &&
                ReferenceEquals(_shortcutKeyBoxes[i], focusedElement);
            _shortcutKeyBorders[i].Background = isActive ? activeBackground : normalBackground;
            _shortcutKeyBorders[i].BorderBrush = isActive ? activeBorder : normalBorder;
            _shortcutKeyBorders[i].BorderThickness = isActive ? new Thickness(2) : new Thickness(1);
        }
    }


    private async void OnPickSourceClicked(object sender, RoutedEventArgs e)
    {
        string? picked = await PickFolderAsync();
        if (!string.IsNullOrEmpty(picked))
        {
            SourceTextBox.Text = picked;
            SaveConfig();
            await RefreshListsAsync();
        }
    }

    private async void OnPickTargetClicked(object sender, RoutedEventArgs e)
    {
        string? picked = await PickFolderAsync();
        if (!string.IsNullOrEmpty(picked))
        {
            TargetTextBox.Text = picked;
            SaveConfig();
            await RefreshListsAsync();
        }
    }

    private async void OnPickLauncherClicked(object sender, RoutedEventArgs e)
    {
        string? picked = await PickFileAsync([".exe", ".bat", ".cmd"]);
        if (!string.IsNullOrEmpty(picked))
        {
            LauncherTextBox.Text = picked;
            SaveConfig();
        }
    }

    private void OnOpenSourceClicked(object sender, RoutedEventArgs e) => OpenDirectory(SourceTextBox.Text, L("主文件夹", "source folder"));

    private void OnOpenTargetClicked(object sender, RoutedEventArgs e) => OpenDirectory(TargetTextBox.Text, L("目标文件夹", "target folder"));

    private void OnOpenLauncherClicked(object sender, RoutedEventArgs e) => OpenFileLocation(LauncherTextBox.Text, L("启动器", "launcher"));

    private async void OnRunLauncherClicked(object sender, RoutedEventArgs e)
    {
        string launcherPath = (LauncherTextBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(launcherPath) || !File.Exists(launcherPath))
        {
            await ShowMessageAsync(
                L("请先设置有效的 XXMI 启动器路径。", "Set a valid launcher path first."),
                L("未设置启动器", "Launcher not set"));
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = launcherPath,
                WorkingDirectory = Path.GetDirectoryName(launcherPath) ?? AppContext.BaseDirectory,
                UseShellExecute = true
            });
            StatusTextBlock.Text = L("已启动外部启动器。", "External launcher started.");
            SaveConfig();
        }
        catch (Exception ex)
        {
            await ShowMessageAsync(
                L("运行启动器失败：", "Failed to run launcher: ") + ex.Message,
                L("启动失败", "Launch failed"));
        }
    }

    private async void OnRefreshClicked(object sender, RoutedEventArgs e) => await RefreshListsAsync();

    private async void OnImportZipClicked(object sender, RoutedEventArgs e)
    {
        FirstLevelFolderItem? item = FirstLevelListView.SelectedItem as FirstLevelFolderItem;
        if (item is null)
        {
            await ShowMessageAsync(
                L("请先选择一个第一层分类。", "Select a first-level category first."),
                L("未选择分类", "No category selected"));
            return;
        }

        var picker = new FileOpenPicker();
        foreach (string extension in SupportedArchiveExtensions)
        {
            picker.FileTypeFilter.Add(extension);
        }
        picker.SuggestedStartLocation = PickerLocationId.Downloads;
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        StorageFile? file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            await ImportArchiveToSelectedFirstLevelFolderAsync(file.Path, item);
        }
    }

    private async void OnToggleCopyClicked(object sender, RoutedEventArgs e)
    {
        var item = GetSelectedSecondLevelItem();
        if (item is null)
        {
            await ShowMessageAsync(
                L("请先选择一个第二层 Mod。", "Select a second-level mod first."),
                L("未选择 Mod", "No mod selected"));
            return;
        }

        await ToggleDirectoryCopyAsync(item);
    }

    private void OnFirstLevelSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        PopulateSecondLevelList(FirstLevelListView.SelectedItem as FirstLevelFolderItem);
    }

    private void OnFirstLevelSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFirstLevelFilter();
    }

    private async void OnCreateFirstLevelClicked(object sender, RoutedEventArgs e)
    {
        string sourceDir = (SourceTextBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(sourceDir) || !Directory.Exists(sourceDir))
        {
            await ShowMessageAsync(
                L("请先设置有效的 Mod 仓库文件夹。", "Set a valid mod storage folder first."),
                L("路径无效", "Invalid path"));
            return;
        }

        string? name = await PromptForTextAsync(
            L("请输入新的第一层文件夹名称。", "Enter the new first-level folder name."),
            L("新建第一层文件夹", "Create first-level folder"),
            string.Empty);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        try
        {
            string targetPath = Path.Combine(sourceDir, name.Trim());
            if (Directory.Exists(targetPath))
            {
                await ShowMessageAsync(
                    L("同名第一层文件夹已存在。", "A first-level folder with the same name already exists."),
                    L("创建失败", "Create failed"));
                return;
            }

            Directory.CreateDirectory(targetPath);
            await RefreshListsAsync();
            SelectFirstLevelByPath(targetPath);
            StatusTextBlock.Text = L($"已创建第一层文件夹：{name.Trim()}", $"Created first-level folder: {name.Trim()}");
        }
        catch (Exception ex)
        {
            await ShowMessageAsync(
                L("创建第一层文件夹失败：", "Failed to create the first-level folder: ") + ex.Message,
                L("创建失败", "Create failed"));
        }
    }

    private async void OnRenameFirstLevelClicked(object sender, RoutedEventArgs e)
    {
        FirstLevelFolderItem? item = FirstLevelListView.SelectedItem as FirstLevelFolderItem;
        if (item is null)
        {
            await ShowMessageAsync(
                L("请先选择一个第一层文件夹。", "Select a first-level folder first."),
                L("未选择文件夹", "No folder selected"));
            return;
        }

        string? name = await PromptForTextAsync(
            L("请输入新的第一层文件夹名称。", "Enter the new first-level folder name."),
            L("重命名第一层文件夹", "Rename first-level folder"),
            item.Name);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        string trimmed = name.Trim();
        if (string.Equals(trimmed, item.Name, StringComparison.CurrentCulture))
        {
            return;
        }

        try
        {
            string? parentDir = Path.GetDirectoryName(item.Path);
            if (string.IsNullOrWhiteSpace(parentDir))
            {
                throw new InvalidOperationException(L("无法确定父目录。", "Could not determine the parent directory."));
            }

            string newPath = Path.Combine(parentDir, trimmed);
            if (Directory.Exists(newPath))
            {
                await ShowMessageAsync(
                    L("目标名称已存在，请换一个名称。", "The target name already exists. Choose a different name."),
                    L("重命名失败", "Rename failed"));
                return;
            }

            Directory.Move(item.Path, newPath);
            await RefreshListsAsync();
            SelectFirstLevelByPath(newPath);
            StatusTextBlock.Text = L($"已重命名第一层文件夹为：{trimmed}", $"Renamed first-level folder to: {trimmed}");
        }
        catch (Exception ex)
        {
            await ShowMessageAsync(
                L("重命名第一层文件夹失败：", "Failed to rename the first-level folder: ") + ex.Message,
                L("重命名失败", "Rename failed"));
        }
    }

    private void OnSecondLevelSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ApplySecondLevelSelectionState(GetSelectedSecondLevelItem());
    }

    private async void OnSecondLevelDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        var item = GetSelectedSecondLevelItem();
        if (item is not null)
        {
            await ToggleDirectoryCopyAsync(item);
        }
    }

    private async void OnDeleteSecondLevelClicked(object sender, RoutedEventArgs e)
    {
        SecondLevelFolderItem? item = GetSelectedSecondLevelItem();
        if (item is null)
        {
            await ShowMessageAsync(
                L("请先选择一个第二层 Mod。", "Select a second-level mod first."),
                L("未选择 Mod", "No mod selected"));
            return;
        }

        bool confirmed = await ShowConfirmAsync(
            L($"确定要删除这个第二层 Mod 吗？\n\n{item.Name}\n\n此操作会删除原始文件夹，无法撤销。",
              $"Are you sure you want to delete this second-level mod?\n\n{item.Name}\n\nThis will delete the original folder and cannot be undone."),
            L("确认删除", "Confirm delete"));
        if (!confirmed)
        {
            return;
        }

        try
        {
            string selectedFirstLevelPath = (FirstLevelListView.SelectedItem as FirstLevelFolderItem)?.Path ?? string.Empty;
            _trackedModOrigins.Remove(item.Path);
            _modLinks.Remove(item.Path);
            Directory.Delete(item.Path, true);
            SaveConfig();
            await RefreshListsAsync();
            SelectFirstLevelByPath(selectedFirstLevelPath);
            StatusTextBlock.Text = L($"已删除第二层 Mod：{item.Name}", $"Deleted second-level mod: {item.Name}");
        }
        catch (Exception ex)
        {
            await ShowMessageAsync(
                L("删除第二层 Mod 失败：", "Failed to delete the second-level mod: ") + ex.Message,
                L("删除失败", "Delete failed"));
        }
    }

    private async void OnAddShortcutRowClicked(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentSecondLevelPath))
        {
            await ShowMessageAsync(
                L("请先选择一个第二层 Mod。", "Select a second-level mod first."),
                L("未选择 Mod", "No mod selected"));
            return;
        }

        if (_visibleShortcutRows >= MaxShortcutRows)
        {
            StatusTextBlock.Text = L("快捷键行数已达到上限 10 行。", "Shortcut rows have reached the 10-row limit.");
            return;
        }

        _visibleShortcutRows++;
        UpdateShortcutRowVisibility();
        _shortcutKeyBoxes[_visibleShortcutRows - 1].Focus(FocusState.Programmatic);
        SaveCurrentModBindings();
    }

    private void OnShortcutTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isLoadingBindings)
        {
            SaveCurrentModBindings();
        }
    }

    private void OnShortcutKeyBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (sender is not TextBox box)
        {
            return;
        }

        if (e.KeyStatus.WasKeyDown || e.KeyStatus.RepeatCount > 1)
        {
            e.Handled = true;
            return;
        }

        if (e.Key is VirtualKey.Back or VirtualKey.Delete or VirtualKey.Escape)
        {
            box.Text = string.Empty;
            e.Handled = true;
            return;
        }

        string shortcut = BuildShortcutFromEvent(e);
        if (string.IsNullOrEmpty(shortcut))
        {
            return;
        }

        box.Text = shortcut;
        box.SelectionStart = box.Text.Length;
        e.Handled = true;
    }

    private void OnModLinkTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isLoadingModLink)
        {
            SaveCurrentModLink();
        }
    }

    private async void OnOpenModLinkClicked(object sender, RoutedEventArgs e)
    {
        string currentLink = (ModLinkTextBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(currentLink))
        {
            await ShowMessageAsync(
                L("请先为当前 Mod 填写一个可访问的网址。", "Enter a valid web link for the current mod first."),
                L("未设置链接", "Link not set"));
            return;
        }

        try
        {
            string normalized = NormalizeLink(currentLink);
            Process.Start(new ProcessStartInfo
            {
                FileName = normalized,
                UseShellExecute = true
            });
            StatusTextBlock.Text = L("已在默认浏览器中打开 Mod 链接。", "Opened the mod link in the default browser.");
        }
        catch (Exception ex)
        {
            await ShowMessageAsync(
                L("打开链接失败：", "Failed to open link: ") + ex.Message,
                L("打开失败", "Open failed"));
        }
    }

    private void OnThemeToggleClicked(object sender, RoutedEventArgs e)
    {
        ApplyTheme(!_isDarkTheme);
        SaveConfig();
    }

    private void OnLanguageToggleClicked(object sender, RoutedEventArgs e)
    {
        _currentLanguage = _currentLanguage == AppLanguage.ZhCn ? AppLanguage.EnUs : AppLanguage.ZhCn;
        ApplyLanguage();
        RefreshSettingsPane();
        RefreshUpdatesPane();
        SaveConfig();
    }

    private void OnUpdateCheckIntervalSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isApplyingUpdateCheckIntervalSelection || UpdateCheckIntervalComboBox.SelectedItem is not ComboBoxItem { Tag: UpdateCheckInterval interval })
        {
            return;
        }

        _updateCheckInterval = interval;
        SaveShellConfig();
        RefreshSettingsPane();
    }

    private void OnModUpdateIntervalSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isApplyingModUpdateIntervalSelection || ModUpdateIntervalComboBox.SelectedItem is not ComboBoxItem { Tag: UpdateCheckInterval interval })
        {
            return;
        }

        _modUpdateCheckInterval = interval;
        SaveShellConfig();
        RefreshUpdatesPane();
    }

    private async void OnCheckModUpdatesClicked(object sender, RoutedEventArgs e)
    {
        await CheckTrackedModUpdatesAsync(showDialogs: true);
    }

    private void OnCloseOnlineDetailsClicked(object sender, RoutedEventArgs e)
    {
        _activeOnlineDetailMod = null;
        OnlineDetailsSplitView.IsPaneOpen = true;
        ResetOnlineDetailPaneToPlaceholder();
    }

    private async void OnOpenOnlineDetailPageClicked(object sender, RoutedEventArgs e)
    {
        if (_activeOnlineDetailMod is null)
        {
            return;
        }

        await OpenExternalUrlAsync(_activeOnlineDetailMod.ProfileUrl, L("打开页面失败", "Open page failed"));
    }

    private async void OnDownloadOnlineDetailClicked(object sender, RoutedEventArgs e)
    {
        if (_activeOnlineDetailMod is null)
        {
            return;
        }

        await DownloadAndExtractOnlineModAsync(_activeOnlineDetailMod);
    }

    private void OnOpenGitHubClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = GitHubRepositoryUrl,
                UseShellExecute = true
            });

            UpdateStatusTextBlock.Text = L("已在默认浏览器中打开 GitHub 仓库。", "Opened the GitHub repository in the default browser.");
        }
        catch (Exception ex)
        {
            _ = ShowMessageAsync(
                L("打开 GitHub 仓库失败：", "Failed to open the GitHub repository: ") + ex.Message,
                L("打开失败", "Open failed"));
        }
    }

    private async void OnCheckUpdatesClicked(object sender, RoutedEventArgs e)
    {
        await CheckForUpdatesAsync(showDialogs: true);
#if false

        if (_isCheckingUpdates)
        {
            return;
        }

        try
        {
            _isCheckingUpdates = true;
            CheckUpdatesButton.IsEnabled = false;
            ApplyLanguage();
            UpdateStatusTextBlock.Text = L("正在连接 GitHub Releases 检查更新...", "Checking GitHub Releases for updates...");

            using HttpResponseMessage response = await _httpClient.GetAsync(GitHubLatestReleaseApiUrl);
            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync();
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            string latestTag = root.TryGetProperty("tag_name", out JsonElement tagElement)
                ? tagElement.GetString() ?? string.Empty
                : string.Empty;
            string releaseUrl = root.TryGetProperty("html_url", out JsonElement urlElement)
                ? urlElement.GetString() ?? GitHubRepositoryUrl
                : GitHubRepositoryUrl;

            if (string.Equals(latestTag, AppVersion, StringComparison.OrdinalIgnoreCase))
            {
                UpdateStatusTextBlock.Text = L($"当前已经是最新版本：{latestTag}", $"You are already on the latest version: {latestTag}");
            }
            else if (!string.IsNullOrWhiteSpace(latestTag))
            {
                UpdateStatusTextBlock.Text = L($"检测到新版本：{latestTag}，当前是 {AppVersion}", $"New version detected: {latestTag}. Current version: {AppVersion}");

                bool openRelease = await ShowConfirmAsync(
                    L($"已检测到 GitHub 新版本：{latestTag}\n\n是否现在打开 Release 页面？", $"A newer GitHub release was found: {latestTag}\n\nOpen the release page now?"),
                    L("发现新版本", "Update available"));

                if (openRelease)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = releaseUrl,
                        UseShellExecute = true
                    });
                }
            }
            else
            {
                UpdateStatusTextBlock.Text = L("GitHub 已返回结果，但没有读到有效版本号。", "GitHub responded, but no valid version tag was found.");
            }
        }
        catch (Exception ex)
        {
            UpdateStatusTextBlock.Text = L("检查更新失败，请稍后重试。", "Update check failed. Please try again later.");
            await ShowMessageAsync(
                L("从 GitHub 检查更新失败：", "Failed to check updates from GitHub: ") + ex.Message,
                L("检查失败", "Check failed"));
        }
        finally
        {
            _isCheckingUpdates = false;
            CheckUpdatesButton.IsEnabled = true;
            ApplyLanguage();
        }
#endif
    }

    private async void OnInstallUpdateClicked(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_latestReleaseUrl))
        {
            await CheckForUpdatesAsync(showDialogs: true);
            return;
        }

        await OpenExternalUrlAsync(_latestReleaseUrl, L("打开更新页面失败", "Failed to open the update page"));
    }

    private async Task CheckForUpdatesAsync(bool showDialogs)
    {
        if (_isCheckingUpdates)
        {
            return;
        }

        try
        {
            _isCheckingUpdates = true;
            CheckUpdatesButton.IsEnabled = false;
            RefreshSettingsPane();
            SetUpdateStatus("正在连接 GitHub Releases 检查更新...", "Checking GitHub Releases for updates...");

            using HttpResponseMessage response = await _httpClient.GetAsync(GitHubLatestReleaseApiUrl);
            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync();
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            string latestTag = root.TryGetProperty("tag_name", out JsonElement tagElement)
                ? tagElement.GetString() ?? string.Empty
                : string.Empty;
            string releaseUrl = root.TryGetProperty("html_url", out JsonElement urlElement)
                ? urlElement.GetString() ?? GitHubRepositoryUrl
                : GitHubRepositoryUrl;
            string releaseName = root.TryGetProperty("name", out JsonElement nameElement)
                ? nameElement.GetString() ?? string.Empty
                : string.Empty;
            string releaseBody = root.TryGetProperty("body", out JsonElement bodyElement)
                ? bodyElement.GetString() ?? string.Empty
                : string.Empty;
            string publishedAt = root.TryGetProperty("published_at", out JsonElement publishedElement)
                ? publishedElement.GetString() ?? string.Empty
                : string.Empty;

            _lastUpdateCheckUtc = DateTimeOffset.UtcNow;
            _latestReleaseTag = latestTag;
            _latestReleaseTitle = string.IsNullOrWhiteSpace(releaseName) ? latestTag : releaseName;
            _latestReleaseBody = string.IsNullOrWhiteSpace(releaseBody) ? string.Empty : releaseBody.Trim();
            _latestReleasePublishedAt = publishedAt;
            _latestReleaseUrl = releaseUrl;
            SaveShellConfig();
            RefreshUpdateDetailsView();

            if (string.Equals(latestTag, AppVersion, StringComparison.OrdinalIgnoreCase))
            {
                SetUpdateStatus($"当前已经是最新版本：{latestTag}", $"You are already on the latest version: {latestTag}");
            }
            else if (!string.IsNullOrWhiteSpace(latestTag))
            {
                SetUpdateStatus($"检测到新版本：{latestTag}，当前是 {AppVersion}", $"New version detected: {latestTag}. Current version: {AppVersion}");

                if (showDialogs)
                {
                    bool openRelease = await ShowConfirmAsync(
                        L($"已检测到 GitHub 新版本：{latestTag}\n\n是否现在打开 Release 页面？", $"A newer GitHub release was found: {latestTag}\n\nOpen the release page now?"),
                        L("发现新版本", "Update available"));

                    if (openRelease)
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = releaseUrl,
                            UseShellExecute = true
                        });
                    }
                }
            }
            else
            {
                SetUpdateStatus("GitHub 返回了结果，但没有读到有效版本号。", "GitHub responded, but no valid version tag was found.");
            }
        }
        catch (Exception ex)
        {
            SetUpdateStatus("检查更新失败，请稍后重试。", "Update check failed. Please try again later.");
            await ShowMessageAsync(
                L("从 GitHub 检查更新失败：", "Failed to check updates from GitHub: ") + ex.Message,
                L("检查失败", "Check failed"));
        }
        finally
        {
            _isCheckingUpdates = false;
            CheckUpdatesButton.IsEnabled = true;
            RefreshSettingsPane();
        }
    }

    private void OnPreviewDragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.None;

        if (GetSelectedSecondLevelItem() is null)
        {
            return;
        }

        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
        }
    }

    private async void OnPreviewDrop(object sender, DragEventArgs e)
    {
        var item = GetSelectedSecondLevelItem();
        if (item is null)
        {
            await ShowMessageAsync(
                L("请先选择一个第二层 Mod，再把图片拖到预览区。", "Select a second-level mod before dropping an image."),
                L("未选择 Mod", "No mod selected"));
            return;
        }

        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            await ShowMessageAsync(
                L("只支持从资源管理器拖入图片文件。", "Only image files dragged from File Explorer are supported."),
                L("不支持的拖放内容", "Unsupported drop content"));
            return;
        }

        IReadOnlyList<IStorageItem> droppedItems = await e.DataView.GetStorageItemsAsync();
        StorageFile? imageFile = droppedItems.OfType<StorageFile>().FirstOrDefault(file => IsImageFile(file.Path));
        if (imageFile is null)
        {
            await ShowMessageAsync(
                L("没有检测到可用的图片文件。", "No supported image file was detected."),
                L("未找到图片", "No image found"));
            return;
        }

        await ImportPreviewImageAsync(imageFile.Path, item);
    }

    private void OnSecondLevelDragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.None;

        if (FirstLevelListView.SelectedItem is null)
        {
            return;
        }

        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
        }
    }

    private async void OnSecondLevelDrop(object sender, DragEventArgs e)
    {
        FirstLevelFolderItem? item = FirstLevelListView.SelectedItem as FirstLevelFolderItem;
        if (item is null)
        {
            await ShowMessageAsync(
                L("请先选择一个第一层分类，再把压缩包拖到这里。", "Select a first-level category before dropping an archive here."),
                L("未选择分类", "No category selected"));
            return;
        }

        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            await ShowMessageAsync(
                L("这里只支持从资源管理器拖入压缩包文件。", "Only archive files dragged from File Explorer are supported here."),
                L("不支持的拖放内容", "Unsupported drop content"));
            return;
        }

        IReadOnlyList<IStorageItem> droppedItems = await e.DataView.GetStorageItemsAsync();
        StorageFile? archiveFile = droppedItems
            .OfType<StorageFile>()
            .FirstOrDefault(file => IsSupportedArchiveFile(file.Path));

        if (archiveFile is null)
        {
            await ShowMessageAsync(
                L("没有检测到受支持的压缩包文件。", "No supported archive file was detected."),
                L("未找到压缩包", "No archive found"));
            return;
        }

        await ImportArchiveToSecondLevelFolderAsync(archiveFile.Path, item);
    }

    private async void OnRootKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (FocusManager.GetFocusedElement(this.Content.XamlRoot) is TextBox)
        {
            return;
        }

        if (e.Key == VirtualKey.F5)
        {
            await RefreshListsAsync();
            e.Handled = true;
            return;
        }

        if (e.Key == VirtualKey.Enter)
        {
            var item = GetSelectedSecondLevelItem();
            if (item is not null)
            {
                await ToggleDirectoryCopyAsync(item);
                e.Handled = true;
                return;
            }
        }

        string shortcut = BuildShortcutFromEvent(e);
        if (!string.IsNullOrEmpty(shortcut) && await TryRunBoundShortcutAsync(shortcut))
        {
            e.Handled = true;
        }
    }

    private void OnRootGridPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (ShouldPreservePointerFocus(e.OriginalSource as DependencyObject))
        {
            return;
        }

        ClearFocusedTextInput();
        (GetCurrentShellFocusTarget() ?? RootGrid).Focus(FocusState.Programmatic);
    }

    private static bool ShouldPreservePointerFocus(DependencyObject? source)
    {
        DependencyObject? current = source;
        while (current is not null)
        {
            if (current is TextBox or Button or ComboBox or ListView or ListViewItem)
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private void ClearFocusedTextInput()
    {
        if (_lastFocusedTextBox is null)
        {
            return;
        }

        try
        {
            _lastFocusedTextBox.Select(0, 0);
        }
        catch
        {
        }
    }

    private UIElement? GetCurrentShellFocusTarget()
    {
        if (_currentPrimarySection == PrimarySection.Repository && _selectedRepositoryId is not null)
        {
            return SecondaryNavPanel.Children.OfType<Button>().FirstOrDefault(button => string.Equals(button.Tag as string, _selectedRepositoryId, StringComparison.Ordinal));
        }

        return _currentPrimarySection switch
        {
            PrimarySection.Dashboard => DashboardNavButton,
            PrimarySection.Repository => RepositoryNavButton,
            PrimarySection.Updates => UpdatesNavButton,
            PrimarySection.Settings => SettingsNavButton,
            _ => OnlineNavButton
        };
    }

    private void ApplyTheme(bool dark)
    {
        _isDarkTheme = dark;
        RootGrid.RequestedTheme = dark ? ElementTheme.Dark : ElementTheme.Light;
        SetStateColor(CurrentStateTextBlock.Text);
        RefreshPrimaryNavigationVisuals();
        RefreshSecondaryNavigation();
        UpdateShortcutKeyFocusVisuals();
        ApplyLanguage();
    }

    private string GetPreferredDownloadStartFolder()
    {
        WorkspaceRepository? repository = GetSelectedRepository();
        if (repository is not null && Directory.Exists(repository.SourcePath))
        {
            return repository.SourcePath;
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    }

    private async Task<string?> PickDownloadFolderAsync()
    {
        WorkspaceRepository? repository = GetSelectedRepository();
        if (repository is not null && Directory.Exists(repository.SourcePath))
        {
            string[] childFolders = Directory.GetDirectories(repository.SourcePath)
                .OrderBy(path => path, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

            ComboBox folderComboBox = new()
            {
                MinWidth = 420,
                MinHeight = 40
            };
            folderComboBox.Items.Add(new ComboBoxItem
            {
                Content = L("直接放到 Mod 仓库根目录", "Use the repository root folder"),
                Tag = repository.SourcePath
            });

            foreach (string childFolder in childFolders)
            {
                folderComboBox.Items.Add(new ComboBoxItem
                {
                    Content = Path.GetFileName(childFolder),
                    Tag = childFolder
                });
            }

            folderComboBox.SelectedIndex = 0;

            ContentDialog dialog = new()
            {
                Title = L("选择下载位置", "Choose download location"),
                Content = new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = L(
                                $"当前仓库的 Mod 仓库目录：\n{repository.SourcePath}\n\n请选择要下载并解压到的文件夹。也可以改为手动选择其他位置。",
                                $"Current mod repository folder:\n{repository.SourcePath}\n\nChoose which folder should receive the download and extraction. You can also choose another location manually."),
                            TextWrapping = TextWrapping.Wrap
                        },
                        folderComboBox
                    }
                },
                PrimaryButtonText = L("使用选中文件夹", "Use selected folder"),
                SecondaryButtonText = L("手动选择其他位置", "Choose another location"),
                CloseButtonText = L("取消", "Cancel"),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = RootGrid.XamlRoot
            };

            ContentDialogResult result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                return (folderComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? repository.SourcePath;
            }

            if (result == ContentDialogResult.None)
            {
                return null;
            }
        }

        return await PickFolderAsync();
    }

    private async Task<string?> PickFolderAsync(string? preferredPath = null)
    {
        var picker = new FolderPicker();
        picker.SuggestedStartLocation = PickerLocationId.Desktop;
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        StorageFolder? folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    private async Task<string?> PickFileAsync(string[] extensions)
    {
        var picker = new FileOpenPicker();
        picker.SuggestedStartLocation = PickerLocationId.Desktop;
        foreach (string extension in extensions)
        {
            picker.FileTypeFilter.Add(extension);
        }

        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        StorageFile? file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    private void OpenDirectory(string? directory, string title)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                _ = ShowMessageAsync(
                    title + L("不存在，请重新选择。", " does not exist. Please choose it again."),
                L("路径无效", "Invalid path"));
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = directory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _ = ShowMessageAsync(
                L("打开目录失败：", "Failed to open folder: ") + ex.Message,
                L("操作失败", "Operation failed"));
        }
    }

    private void OpenFileLocation(string? filePath, string title)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                _ = ShowMessageAsync(
                    title + L("不存在，请重新选择。", " does not exist. Please choose it again."),
                L("路径无效", "Invalid path"));
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = "/select,\"" + filePath + "\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _ = ShowMessageAsync(
                L("打开启动器位置失败：", "Failed to open file location: ") + ex.Message,
                L("操作失败", "Operation failed"));
        }
    }

    private void LoadConfig()
    {
        _modBindings.Clear();
        _modLinks.Clear();
        _trackedModOrigins.Clear();

        if (!File.Exists(_configPath))
        {
            LoadDefaultShortcutTemplate();
            return;
        }

        try
        {
            foreach (string line in File.ReadAllLines(_configPath))
            {
                if (line.StartsWith("source_dir=", StringComparison.Ordinal))
                {
                    SourceTextBox.Text = DecodeValue(line["source_dir=".Length..]);
                }
                else if (line.StartsWith("target_dir=", StringComparison.Ordinal))
                {
                    TargetTextBox.Text = DecodeValue(line["target_dir=".Length..]);
                }
                else if (line.StartsWith("launcher_path=", StringComparison.Ordinal))
                {
                    LauncherTextBox.Text = DecodeValue(line["launcher_path=".Length..]);
                }
                else if (line.StartsWith("theme=", StringComparison.Ordinal))
                {
                    _isDarkTheme = string.Equals(DecodeValue(line["theme=".Length..]), "dark", StringComparison.OrdinalIgnoreCase);
                }
                else if (line.StartsWith("language=", StringComparison.Ordinal))
                {
                    string language = DecodeValue(line["language=".Length..]);
                    _currentLanguage = string.Equals(language, "en", StringComparison.OrdinalIgnoreCase)
                        ? AppLanguage.EnUs
                        : AppLanguage.ZhCn;
                }
                else if (line.StartsWith("binding=", StringComparison.Ordinal))
                {
                    string[] parts = line["binding=".Length..].Split('\t');
                    if (parts.Length == 3)
                    {
                        string modPath = DecodeValue(parts[0]);
                        if (!_modBindings.TryGetValue(modPath, out List<ShortcutBinding>? bindings))
                        {
                            bindings = [];
                            _modBindings[modPath] = bindings;
                        }

                        bindings.Add(new ShortcutBinding(DecodeValue(parts[1]), DecodeValue(parts[2])));
                    }
                }
                else if (line.StartsWith("mod_link=", StringComparison.Ordinal))
                {
                    string[] parts = line["mod_link=".Length..].Split('\t');
                    if (parts.Length == 2)
                    {
                        _modLinks[DecodeValue(parts[0])] = DecodeValue(parts[1]);
                    }
                }
                else if (line.StartsWith("mod_origin=", StringComparison.Ordinal))
                {
                    string[] parts = line["mod_origin=".Length..].Split('\t');
                    if (parts.Length >= 7)
                    {
                        string path = DecodeValue(parts[0]);
                        if (string.IsNullOrWhiteSpace(path))
                        {
                            continue;
                        }

                        int.TryParse(DecodeValue(parts[2]), out int itemId);
                        _trackedModOrigins[path] = new TrackedModOrigin
                        {
                            Path = path,
                            SourceSite = DecodeValue(parts[1]),
                            ItemId = itemId,
                            Title = DecodeValue(parts[3]),
                            ProfileUrl = DecodeValue(parts[4]),
                            PreviewUrl = DecodeValue(parts[5]),
                            LastKnownUpdatedAt = TryParseDateTimeOffset(DecodeValue(parts[6]))
                        };
                    }
                }
            }
        }
        catch
        {
            StatusTextBlock.Text = L("配置读取失败，已忽略旧配置。", "Failed to load config. Older config values were ignored.");
        }

        if (DeduplicateTrackedModOrigins())
        {
            SaveConfig();
        }

        LoadDefaultShortcutTemplate();
        LoadLinkForCurrentMod(null);
    }

    private void SaveConfig()
    {
        try
        {
            SaveSelectedRepositoryFromInputs();

            var lines = new List<string>
            {
                "source_dir=" + EncodeValue(SourceTextBox.Text ?? string.Empty),
                "target_dir=" + EncodeValue(TargetTextBox.Text ?? string.Empty),
                "launcher_path=" + EncodeValue(LauncherTextBox.Text ?? string.Empty),
                "theme=" + EncodeValue(_isDarkTheme ? "dark" : "light"),
                "language=" + EncodeValue(_currentLanguage == AppLanguage.EnUs ? "en" : "zh")
            };

            foreach ((string path, List<ShortcutBinding> bindings) in _modBindings.OrderBy(item => item.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                foreach (ShortcutBinding binding in bindings)
                {
                    if (string.IsNullOrWhiteSpace(binding.Shortcut) && string.IsNullOrWhiteSpace(binding.Action))
                    {
                        continue;
                    }

                    lines.Add("binding=" + EncodeValue(path) + "\t" + EncodeValue(binding.Shortcut) + "\t" + EncodeValue(binding.Action));
                }
            }

            foreach ((string path, string link) in _modLinks.OrderBy(item => item.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(link))
                {
                    lines.Add("mod_link=" + EncodeValue(path) + "\t" + EncodeValue(link));
                }
            }

            foreach ((string path, TrackedModOrigin origin) in _trackedModOrigins.OrderBy(item => item.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                lines.Add("mod_origin="
                    + EncodeValue(path) + "\t"
                    + EncodeValue(origin.SourceSite) + "\t"
                    + EncodeValue(origin.ItemId.ToString()) + "\t"
                    + EncodeValue(origin.Title) + "\t"
                    + EncodeValue(origin.ProfileUrl) + "\t"
                    + EncodeValue(origin.PreviewUrl ?? string.Empty) + "\t"
                    + EncodeValue(origin.LastKnownUpdatedAt?.ToString("O") ?? string.Empty));
            }

            File.WriteAllLines(_configPath, lines);
            SaveShellConfig();
        }
        catch
        {
            StatusTextBlock.Text = L("配置保存失败，但不影响当前使用。", "Failed to save config, but the app can continue.");
        }
    }

    private static string EncodeValue(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    private static string DecodeValue(string value)
    {
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(value));
        }
        catch
        {
            return string.Empty;
        }
    }

    private async Task RefreshListsAsync()
    {
        _firstLevelItems.Clear();
        _secondLevelItems.Clear();
        _currentSecondLevelPath = null;
        FirstCountTextBlock.Text = "0";
        SecondCountTextBlock.Text = "0";
        CurrentFolderTextBlock.Text = StateNotSelectedText;
        CurrentStateTextBlock.Text = StateNotSelectedText;
        CurrentStateTextBlock.Foreground = NeutralBrush;
        LoadDefaultShortcutTemplate();
        LoadLinkForCurrentMod(null);
        ClearPreview();
        UpdateProgress(0, L("当前无复制任务", "No active copy task"));

        string sourceDir = (SourceTextBox.Text ?? string.Empty).Trim();
        string targetDir = (TargetTextBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(sourceDir) || !Directory.Exists(sourceDir))
        {
            StatusTextBlock.Text = L("请先选择有效的 Mod 仓库文件夹。", "Choose a valid mod storage folder first.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(targetDir) && !Directory.Exists(targetDir))
        {
            StatusTextBlock.Text = L("目标文件夹不存在，请重新选择。", "The target folder does not exist. Please choose it again.");
            return;
        }

        SaveConfig();

        await Task.Run(() =>
        {
            var loadedItems = new List<FirstLevelFolderItem>();
            string[] firstDirs = Directory.GetDirectories(sourceDir);
            Array.Sort(firstDirs, StringComparer.CurrentCultureIgnoreCase);

            int secondCount = 0;
            foreach (string firstDir in firstDirs)
            {
                var item = new FirstLevelFolderItem(firstDir);
                string[] secondDirs = Directory.GetDirectories(firstDir);
                Array.Sort(secondDirs, StringComparer.CurrentCultureIgnoreCase);

                foreach (string secondDir in secondDirs)
                {
                    List<string> files = GetFiles(secondDir);
                    string state = GetFolderCopyState(targetDir, secondDir);
                    item.Children.Add(new SecondLevelFolderItem(secondDir, files, state));
                    secondCount++;
                }

                loadedItems.Add(item);
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                _allFirstLevelItems.Clear();
                _allFirstLevelItems.AddRange(loadedItems);
                ApplyFirstLevelFilter();
                FirstCountTextBlock.Text = _allFirstLevelItems.Count.ToString();
                SecondCountTextBlock.Text = secondCount.ToString();
                StatusTextBlock.Text = L(
                    $"已加载 {_allFirstLevelItems.Count} 个第一层目录，{secondCount} 个第二层目录。",
                    $"Loaded {_allFirstLevelItems.Count} first-level folders and {secondCount} second-level folders.");
                if (_firstLevelItems.Count > 0)
                {
                    FirstLevelListView.SelectedIndex = 0;
                }
            });
        });
    }

    private void PopulateSecondLevelList(FirstLevelFolderItem? firstItem, bool preserveStatus = false)
    {
        _secondLevelItems.Clear();
        ClearPreview();
        CurrentFolderTextBlock.Text = StateNotSelectedText;
        CurrentStateTextBlock.Text = StateNotSelectedText;
        CurrentStateTextBlock.Foreground = NeutralBrush;
        _currentSecondLevelPath = null;
        LoadDefaultShortcutTemplate();
        LoadLinkForCurrentMod(null);

        if (firstItem is null)
        {
            return;
        }

        foreach (SecondLevelFolderItem child in firstItem.Children)
        {
            _secondLevelItems.Add(child);
        }

        if (!preserveStatus)
        {
            StatusTextBlock.Text = L("当前第一层目录：", "Current first-level folder: ") + firstItem.Path;
        }

        if (_secondLevelItems.Count > 0)
        {
            SecondLevelListView.SelectedIndex = 0;
            ApplySecondLevelSelectionState(_secondLevelItems[0], preserveStatus: true);
        }
    }

    private void ShowSecondLevelDetails(SecondLevelFolderItem? item, bool preserveStatus = false)
    {
        if (item is null)
        {
            CurrentFolderTextBlock.Text = StateNotSelectedText;
            CurrentStateTextBlock.Text = StateNotSelectedText;
            CurrentStateTextBlock.Foreground = NeutralBrush;
            ClearPreview();
            LoadLinkForCurrentMod(null);
            return;
        }

        CurrentFolderTextBlock.Text = item.Name;
        CurrentStateTextBlock.Text = item.State;
        SetStateColor(item.State);
        UpdatePreviewForDirectory(item.Path, item.Files);

        if (!preserveStatus)
        {
            StatusTextBlock.Text = L("当前查看：", "Viewing: ") + item.Path;
        }
    }

    private void ApplySecondLevelSelectionState(SecondLevelFolderItem? item, bool preserveStatus = false)
    {
        _currentSecondLevelPath = item?.Path;
        ShowSecondLevelDetails(item, preserveStatus);
        LoadBindingsForCurrentMod(item);
        LoadLinkForCurrentMod(item);
    }

    private void SetStateColor(string? state)
    {
        if (string.Equals(state, StateCopiedText, StringComparison.CurrentCultureIgnoreCase))
        {
            CurrentStateTextBlock.Foreground = CopiedBrush;
        }
        else if (string.Equals(state, StateMissingText, StringComparison.CurrentCultureIgnoreCase))
        {
            CurrentStateTextBlock.Foreground = MissingBrush;
        }
        else
        {
            CurrentStateTextBlock.Foreground = _isDarkTheme ? new SolidColorBrush(Colors.WhiteSmoke) : NeutralBrush;
        }
    }

    private async Task ToggleDirectoryCopyAsync(SecondLevelFolderItem item)
    {
        string targetDir = (TargetTextBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(targetDir) || !Directory.Exists(targetDir))
        {
            await ShowMessageAsync(
                L("请先选择有效的目标文件夹。", "Choose a valid target folder first."),
                L("未设置目标文件夹", "Target folder not set"));
            return;
        }

        string targetPath = GetTargetDirectoryPath(targetDir, item.Path);
        SetBusyState(true);
        try
        {
            if (Directory.Exists(targetPath))
            {
                UpdateProgress(15, L("正在移除目录...", "Removing folder..."));
                await Task.Run(() => Directory.Delete(targetPath, true));
                UpdateProgress(100, L("移除完成", "Removal complete"));
                StatusTextBlock.Text = item.Name + L(" 已从目标文件夹移除。", " was removed from the target folder.");
            }
            else
            {
                var progress = new Progress<ProgressInfo>(info => UpdateProgress(info.Percent, info.Message));
                await CopyDirectoryWithProgressAsync(item.Path, targetPath, progress);
                StatusTextBlock.Text = item.Name + L(" 已复制到目标文件夹。", " was copied to the target folder.");
            }
        }
        catch (Exception ex)
        {
            await ShowMessageAsync(
                L("操作失败：", "Operation failed: ") + ex.Message,
                L("错误", "Error"));
        }
        finally
        {
            SetBusyState(false);
            await RefreshListsAsync();
            SelectSecondLevelByPath(item.Path);
        }
    }

    private async Task ImportArchiveToSecondLevelFolderAsync(string archivePath, FirstLevelFolderItem item)
    {
        SetBusyState(true);
        try
        {
            string importedPath = await Task.Run(() => ImportArchiveContents(archivePath, item.Path));

            StatusTextBlock.Text = L($"已将 {Path.GetFileName(archivePath)} 解压到 {item.Name}。", $"Extracted {Path.GetFileName(archivePath)} to {item.Name}.");
            await RefreshListsAsync();
            SelectFirstLevelByPath(item.Path);
            SelectSecondLevelByPath(importedPath);
        }
        catch (Exception ex)
        {
            await ShowMessageAsync(
                L("导入压缩文件失败：", "Archive import failed: ") + ex.Message,
                L("导入失败", "Import failed"));
        }
        finally
        {
            SetBusyState(false);
        }
    }

    private async Task ImportPreviewImageAsync(string imagePath, SecondLevelFolderItem item)
    {
        SetBusyState(true);
        try
        {
            if (!IsImageFile(imagePath))
            {
                throw new InvalidOperationException(L("拖入的文件不是受支持的图片格式。", "The dropped file is not a supported image format."));
            }

            string extension = Path.GetExtension(imagePath).ToLowerInvariant();
            await Task.Run(() =>
            {
                Directory.CreateDirectory(item.Path);
                foreach (string existingFile in Directory.GetFiles(item.Path))
                {
                    string baseName = Path.GetFileNameWithoutExtension(existingFile).ToLowerInvariant();
                    if (IsImageFile(existingFile) && (baseName == "preview" || baseName == "cover" || baseName == "thumbnail" || baseName == "image"))
                    {
                        File.Delete(existingFile);
                    }
                }

                string targetPath = Path.Combine(item.Path, "preview" + extension);
                File.Copy(imagePath, targetPath, true);
            });

            StatusTextBlock.Text = item.Name + L(" 的预览图已更新。", "'s preview image was updated.");
            await RefreshListsAsync();
            SelectSecondLevelByPath(item.Path);
        }
        catch (Exception ex)
        {
            await ShowMessageAsync(
                L("导入预览图失败：", "Preview import failed: ") + ex.Message,
                L("导入失败", "Import failed"));
        }
        finally
        {
            SetBusyState(false);
        }
    }

    private async Task CopyDirectoryWithProgressAsync(string sourcePath, string targetPath, IProgress<ProgressInfo> progress)
    {
        await Task.Run(() =>
        {
            string[] directories = Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories);
            string[] files = Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories);
            int totalSteps = directories.Length + files.Length + 1;
            int currentStep = 0;

            Directory.CreateDirectory(targetPath);
            currentStep++;
            progress.Report(new ProgressInfo(currentStep * 100 / totalSteps, L("正在创建目录...", "Creating folder...")));

            foreach (string directory in directories)
            {
                string relativePath = directory[sourcePath.Length..].TrimStart(Path.DirectorySeparatorChar);
                Directory.CreateDirectory(Path.Combine(targetPath, relativePath));
                currentStep++;
                progress.Report(new ProgressInfo(currentStep * 100 / totalSteps, L("正在创建子目录...", "Creating subfolders...")));
            }

            foreach (string file in files)
            {
                string relativePath = file[sourcePath.Length..].TrimStart(Path.DirectorySeparatorChar);
                string destinationFile = Path.Combine(targetPath, relativePath);
                string? destinationDir = Path.GetDirectoryName(destinationFile);
                if (!string.IsNullOrEmpty(destinationDir))
                {
                    Directory.CreateDirectory(destinationDir);
                }

                File.Copy(file, destinationFile, true);
                currentStep++;
                progress.Report(new ProgressInfo(currentStep * 100 / totalSteps, L("正在复制文件...", "Copying files...")));
            }

            progress.Report(new ProgressInfo(100, L("复制完成", "Copy complete")));
        });
    }

    private void UpdateProgress(int percent, string message)
    {
        CopyProgressBar.Value = percent;
        ProgressTextBlock.Text = message;
    }

    private void SetBusyState(bool busy)
    {
        ThemeToggleButton.IsEnabled = !busy;
        LanguageToggleButton.IsEnabled = !busy;
        FirstLevelListView.IsEnabled = !busy;
        SecondLevelListView.IsEnabled = !busy;
        OpenModLinkButton.IsEnabled = !busy && !string.IsNullOrWhiteSpace(ModLinkTextBox.Text);
    }

    private void LoadDefaultShortcutTemplate()
    {
        _isLoadingBindings = true;
        _activeShortcutKeyBox = null;
        foreach (TextBox box in _shortcutKeyBoxes)
        {
            box.Text = string.Empty;
        }

        foreach (TextBox box in _shortcutActionBoxes)
        {
            box.Text = string.Empty;
        }

        _visibleShortcutRows = 1;
        UpdateShortcutRowVisibility();
        UpdateShortcutKeyFocusVisuals();
        _isLoadingBindings = false;
    }

    private void LoadBindingsForCurrentMod(SecondLevelFolderItem? item)
    {
        _isLoadingBindings = true;
        _activeShortcutKeyBox = null;

        foreach (TextBox box in _shortcutKeyBoxes)
        {
            box.Text = string.Empty;
        }

        foreach (TextBox box in _shortcutActionBoxes)
        {
            box.Text = string.Empty;
        }

        if (item is not null && _modBindings.TryGetValue(item.Path, out List<ShortcutBinding>? bindings) && bindings.Count > 0)
        {
            _visibleShortcutRows = Math.Min(MaxShortcutRows, Math.Max(1, bindings.Count));
            for (int i = 0; i < _visibleShortcutRows; i++)
            {
                _shortcutKeyBoxes[i].Text = bindings[i].Shortcut;
                _shortcutActionBoxes[i].Text = bindings[i].Action;
            }
        }
        else
        {
            _visibleShortcutRows = 1;
        }

        UpdateShortcutRowVisibility();
        UpdateShortcutKeyFocusVisuals();
        _isLoadingBindings = false;
    }

    private void SaveCurrentModBindings()
    {
        if (_isLoadingBindings || string.IsNullOrEmpty(_currentSecondLevelPath))
        {
            return;
        }

        var bindings = new List<ShortcutBinding>();
        for (int i = 0; i < _visibleShortcutRows; i++)
        {
            string shortcut = (_shortcutKeyBoxes[i].Text ?? string.Empty).Trim();
            string action = (_shortcutActionBoxes[i].Text ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(shortcut) || !string.IsNullOrWhiteSpace(action))
            {
                bindings.Add(new ShortcutBinding(shortcut, action));
            }
        }

        if (bindings.Count == 0)
        {
            _modBindings.Remove(_currentSecondLevelPath);
        }
        else
        {
            _modBindings[_currentSecondLevelPath] = bindings;
        }

        SaveConfig();
        UpdateShortcutRowVisibility();
    }

    private void LoadLinkForCurrentMod(SecondLevelFolderItem? item)
    {
        _isLoadingModLink = true;
        ModLinkTextBox.Text = item is not null && _modLinks.TryGetValue(item.Path, out string? link) ? link : string.Empty;
        _isLoadingModLink = false;
        UpdateLinkButtonState();
    }

    private void SaveCurrentModLink()
    {
        if (_isLoadingModLink || string.IsNullOrEmpty(_currentSecondLevelPath))
        {
            UpdateLinkButtonState();
            return;
        }

        string link = (ModLinkTextBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(link))
        {
            _modLinks.Remove(_currentSecondLevelPath);
        }
        else
        {
            _modLinks[_currentSecondLevelPath] = link;
        }

        SaveConfig();
        UpdateLinkButtonState();
    }

    private void UpdateLinkButtonState()
    {
        OpenModLinkButton.IsEnabled = !string.IsNullOrWhiteSpace(ModLinkTextBox.Text);
    }

    private void UpdateShortcutRowVisibility()
    {
        for (int i = 0; i < MaxShortcutRows; i++)
        {
            ShortcutRowsPanel.Children[i].Visibility = i < _visibleShortcutRows ? Visibility.Visible : Visibility.Collapsed;
        }

        ShortcutRowsTextBlock.Text = L($"当前 {_visibleShortcutRows} / {MaxShortcutRows} 行", $"{_visibleShortcutRows} / {MaxShortcutRows} rows");
    }

    private async Task<bool> TryRunBoundShortcutAsync(string shortcut)
    {
        string normalized = NormalizeShortcut(shortcut);
        foreach ((string path, List<ShortcutBinding> bindings) in _modBindings)
        {
            if (bindings.Any(binding => NormalizeShortcut(binding.Shortcut) == normalized))
            {
                SecondLevelFolderItem? secondItem = FindSecondLevelByPath(path);
                if (secondItem is null)
                {
                    continue;
                }

                SelectSecondLevelByPath(secondItem.Path);
                await EnsureDirectoryCopiedAsync(secondItem);
                return true;
            }
        }

        return false;
    }

    private async Task EnsureDirectoryCopiedAsync(SecondLevelFolderItem item)
    {
        string targetDir = (TargetTextBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(targetDir) || !Directory.Exists(targetDir))
        {
            StatusTextBlock.Text = L("快捷键执行失败：请先设置有效的目标文件夹。", "Shortcut failed: choose a valid target folder first.");
            return;
        }

        string targetPath = GetTargetDirectoryPath(targetDir, item.Path);
        if (Directory.Exists(targetPath))
        {
            StatusTextBlock.Text = item.Name + L(" 已通过快捷键定位，当前已经复制。", " was located by shortcut and is already copied.");
            return;
        }

        await ToggleDirectoryCopyAsync(item);
    }

    private void SelectSecondLevelByPath(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        for (int firstIndex = 0; firstIndex < _firstLevelItems.Count; firstIndex++)
        {
            FirstLevelFolderItem first = _firstLevelItems[firstIndex];
            if (first.Children.Any(child => string.Equals(child.Path, path, StringComparison.CurrentCultureIgnoreCase)))
            {
                FirstLevelListView.SelectedIndex = firstIndex;
                for (int secondIndex = 0; secondIndex < _secondLevelItems.Count; secondIndex++)
                {
                    if (string.Equals(_secondLevelItems[secondIndex].Path, path, StringComparison.CurrentCultureIgnoreCase))
                    {
                        SecondLevelListView.SelectedIndex = secondIndex;
                        SecondLevelListView.ScrollIntoView(_secondLevelItems[secondIndex]);
                        ApplySecondLevelSelectionState(_secondLevelItems[secondIndex], preserveStatus: true);
                        return;
                    }
                }
            }
        }
    }

    private static string BuildShortcutFromEvent(KeyRoutedEventArgs e)
    {
        var parts = new List<string>();

        if (IsModifierDown(VirtualKey.Control))
        {
            parts.Add("Ctrl");
        }

        if (IsModifierDown(VirtualKey.Shift))
        {
            parts.Add("Shift");
        }

        if (IsModifierDown(VirtualKey.Menu))
        {
            parts.Add("Alt");
        }

        string key = KeyToString(e.Key);
        if (string.IsNullOrEmpty(key) || key is "Control" or "Shift" or "Alt" or "LeftWindows" or "RightWindows")
        {
            return string.Empty;
        }

        parts.Add(key);
        return string.Join("+", parts);
    }

    private static bool IsModifierDown(VirtualKey key)
    {
        CoreVirtualKeyStates state = InputKeyboardSource.GetKeyStateForCurrentThread(key);
        return (state & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
    }

    private static string KeyToString(VirtualKey key)
    {
        if (key is >= VirtualKey.A and <= VirtualKey.Z)
        {
            return key.ToString().ToUpperInvariant();
        }

        if (key is >= VirtualKey.Number0 and <= VirtualKey.Number9)
        {
            return ((int)key - (int)VirtualKey.Number0).ToString();
        }

        if (key is >= VirtualKey.F1 and <= VirtualKey.F24)
        {
            return key.ToString();
        }

        if (key is >= VirtualKey.NumberPad0 and <= VirtualKey.NumberPad9)
        {
            return "Num" + ((int)key - (int)VirtualKey.NumberPad0);
        }

        int keyCode = (int)key;
        if (keyCode is >= 186 and <= 192 or >= 219 and <= 222)
        {
            return keyCode switch
            {
                186 => ";",
                187 => "=",
                188 => ",",
                189 => "-",
                190 => ".",
                191 => "/",
                192 => "`",
                219 => "[",
                220 => "\\",
                221 => "]",
                222 => "'",
                _ => string.Empty
            };
        }

        return key switch
        {
            VirtualKey.Enter => "Enter",
            VirtualKey.Escape => "Esc",
            VirtualKey.Space => "Space",
            VirtualKey.Left => "Left",
            VirtualKey.Right => "Right",
            VirtualKey.Up => "Up",
            VirtualKey.Down => "Down",
            VirtualKey.Tab => "Tab",
            VirtualKey.Home => "Home",
            VirtualKey.End => "End",
            VirtualKey.PageUp => "PageUp",
            VirtualKey.PageDown => "PageDown",
            VirtualKey.Insert => "Insert",
            VirtualKey.Delete => "Delete",
            VirtualKey.Decimal => ".",
            VirtualKey.Add => "+",
            VirtualKey.Subtract => "-",
            VirtualKey.Multiply => "*",
            VirtualKey.Divide => "/",
            _ => key.ToString()
        };
    }

    private static string NormalizeShortcut(string? shortcut)
    {
        return string.Join("+", (shortcut ?? string.Empty)
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part =>
            {
                if (string.Equals(part, "control", StringComparison.OrdinalIgnoreCase))
                {
                    return "Ctrl";
                }

                if (string.Equals(part, "menu", StringComparison.OrdinalIgnoreCase))
                {
                    return "Alt";
                }

                return part.Length == 1 ? part.ToUpperInvariant() : char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant();
            }));
    }

    private static List<string> GetFiles(string directory)
    {
        string[] files = Directory.GetFiles(directory);
        Array.Sort(files, StringComparer.CurrentCultureIgnoreCase);
        return files.ToList();
    }

    private static string GetTargetDirectoryPath(string targetDir, string sourceDirectoryPath)
    {
        return Path.Combine(targetDir, Path.GetFileName(sourceDirectoryPath));
    }

    private string GetFolderCopyState(string targetDir, string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(targetDir))
        {
            return StateNotConfiguredText;
        }

        return Directory.Exists(GetTargetDirectoryPath(targetDir, directoryPath)) ? StateCopiedText : StateMissingText;
    }

    private void RefreshLocalizedStates()
    {
        string targetDir = (TargetTextBox.Text ?? string.Empty).Trim();
        foreach (FirstLevelFolderItem first in _firstLevelItems)
        {
            foreach (SecondLevelFolderItem second in first.Children)
            {
                second.State = GetFolderCopyState(targetDir, second.Path);
            }
        }
    }

    private SecondLevelFolderItem? FindSecondLevelByPath(string path)
    {
        foreach (FirstLevelFolderItem first in _firstLevelItems)
        {
            foreach (SecondLevelFolderItem second in first.Children)
            {
                if (string.Equals(second.Path, path, StringComparison.CurrentCultureIgnoreCase))
                {
                    return second;
                }
            }
        }

        return null;
    }

    private SecondLevelFolderItem? GetSelectedSecondLevelItem()
    {
        return SecondLevelListView.SelectedItem as SecondLevelFolderItem;
    }

    private void UpdatePreviewForDirectory(string directoryPath, List<string> files)
    {
        List<string> liveFiles = Directory.Exists(directoryPath) ? GetFiles(directoryPath) : files;
        SetPreviewImage(FindPreviewImage(directoryPath, liveFiles));
    }

    private void SetPreviewImage(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            PreviewImage.Source = null;
            PreviewHintTextBlock.Text = L("当前第二层文件夹未找到可预览图片。", "No preview image was found for the current second-level folder.");
            PreviewHintTextBlock.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            PreviewImage.Source = new BitmapImage(new Uri(imagePath));
            PreviewHintTextBlock.Visibility = Visibility.Collapsed;
        }
        catch
        {
            PreviewImage.Source = null;
            PreviewHintTextBlock.Text = L("图片无法读取或格式不受支持。", "The image could not be loaded or is not supported.");
            PreviewHintTextBlock.Visibility = Visibility.Visible;
        }
    }

    private void ClearPreview()
    {
        PreviewImage.Source = null;
        PreviewHintTextBlock.Text = L("当前第二层文件夹未找到可预览图片。", "No preview image was found for the current second-level folder.");
        PreviewHintTextBlock.Visibility = Visibility.Visible;
    }

    private static string? FindPreviewImage(string directoryPath, List<string> files)
    {
        string? namedPreview = files.FirstOrDefault(file =>
        {
            string name = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
            return IsImageFile(file) && (name == "preview" || name == "cover" || name == "thumbnail" || name == "image");
        });

        if (!string.IsNullOrEmpty(namedPreview))
        {
            return namedPreview;
        }

        string[] images = Directory.GetFiles(directoryPath)
            .Where(IsImageFile)
            .OrderBy(path => path, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        return images.FirstOrDefault();
    }

    private static bool IsImageFile(string path)
    {
        string extension = Path.GetExtension(path);
        return !string.IsNullOrEmpty(extension) && ImageExtensions.Contains(extension.ToLowerInvariant());
    }

    private static bool IsSupportedArchiveFile(string path)
    {
        string fileName = Path.GetFileName(path);
        return SupportedArchiveExtensions.Any(extension =>
            fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeLink(string link)
    {
        string trimmed = link.Trim();
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        return "https://" + trimmed;
    }

    private void ApplyFirstLevelFilter()
    {
        string keyword = (FirstLevelSearchTextBox.Text ?? string.Empty).Trim();
        string? selectedPath = FirstLevelListView.SelectedItem is FirstLevelFolderItem selected ? selected.Path : null;

        _firstLevelItems.Clear();

        IEnumerable<FirstLevelFolderItem> filtered = string.IsNullOrWhiteSpace(keyword)
            ? _allFirstLevelItems
            : _allFirstLevelItems.Where(item =>
                item.Name.Contains(keyword, StringComparison.CurrentCultureIgnoreCase));

        foreach (FirstLevelFolderItem item in filtered)
        {
            _firstLevelItems.Add(item);
        }

        if (!string.IsNullOrEmpty(selectedPath))
        {
            SelectFirstLevelByPath(selectedPath);
        }

        if (FirstLevelListView.SelectedItem is null && _firstLevelItems.Count > 0)
        {
            FirstLevelListView.SelectedIndex = 0;
        }
    }

    private void SelectFirstLevelByPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        FirstLevelFolderItem? item = _firstLevelItems.FirstOrDefault(first =>
            string.Equals(first.Path, path, StringComparison.CurrentCultureIgnoreCase));
        if (item is not null)
        {
            FirstLevelListView.SelectedItem = item;
        }
    }

    private async Task ImportArchiveToSelectedFirstLevelFolderAsync(string archivePath, FirstLevelFolderItem item)
    {
        SetBusyState(true);
        try
        {
            await Task.Run(() => ExtractArchiveToDirectory(archivePath, item.Path));

            StatusTextBlock.Text = L($"已将 {Path.GetFileName(archivePath)} 导入到 {item.Name}。", $"Imported {Path.GetFileName(archivePath)} into {item.Name}.");
            await RefreshListsAsync();
            SelectFirstLevelByPath(item.Path);
        }
        catch (Exception ex)
        {
            await ShowMessageAsync(
                L("导入压缩文件失败：", "Archive import failed: ") + ex.Message,
                L("导入失败", "Import failed"));
        }
        finally
        {
            SetBusyState(false);
        }
    }

    private string ImportArchiveContents(string archivePath, string firstLevelDirectory)
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), "ModFolderCopier_Import_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            ExtractArchiveToDirectory(archivePath, tempDirectory);

            string[] rootDirectories = Directory.GetDirectories(tempDirectory);
            string[] rootFiles = Directory.GetFiles(tempDirectory);

            if (rootFiles.Length == 0 && rootDirectories.Length == 1)
            {
                string sourceDirectory = rootDirectories[0];
                string destinationPath = Path.Combine(firstLevelDirectory, Path.GetFileName(sourceDirectory));
                EnsureImportDestinationDoesNotExist(destinationPath);
                Directory.Move(sourceDirectory, destinationPath);
                return destinationPath;
            }

            string importedDirectory = Path.Combine(firstLevelDirectory, BuildImportedFolderName(archivePath));
            EnsureImportDestinationDoesNotExist(importedDirectory);
            Directory.CreateDirectory(importedDirectory);

            foreach (string directory in rootDirectories)
            {
                Directory.Move(directory, Path.Combine(importedDirectory, Path.GetFileName(directory)));
            }

            foreach (string file in rootFiles)
            {
                File.Move(file, Path.Combine(importedDirectory, Path.GetFileName(file)));
            }

            return importedDirectory;
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, true);
            }
        }
    }

    private static void EnsureImportDestinationDoesNotExist(string destinationPath)
    {
        if (Directory.Exists(destinationPath) || File.Exists(destinationPath))
        {
            throw new InvalidOperationException($"\"{Path.GetFileName(destinationPath)}\" already exists.");
        }
    }

    private static string BuildImportedFolderName(string archivePath)
    {
        string fileName = Path.GetFileName(archivePath);

        foreach (string extension in SupportedArchiveExtensions.OrderByDescending(item => item.Length))
        {
            if (fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                fileName = fileName[..^extension.Length];
                break;
            }
        }

        if (fileName.EndsWith(".tar", StringComparison.OrdinalIgnoreCase))
        {
            fileName = fileName[..^4];
        }

        foreach (char invalidChar in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(invalidChar, '_');
        }

        fileName = fileName.Trim();
        return string.IsNullOrWhiteSpace(fileName) ? "ImportedMod" : fileName;
    }

    private void ExtractArchiveToDirectory(string archivePath, string destinationDirectory)
    {
        string extension = Path.GetExtension(archivePath).ToLowerInvariant();
        DispatcherQueue.TryEnqueue(() => UpdateProgress(10, L("正在准备解压...", "Preparing extraction...")));

        if (extension == ".zip")
        {
            using ZipArchive archive = ZipFile.OpenRead(archivePath);
            List<ZipArchiveEntry> entries = archive.Entries
                .Where(entry => !string.IsNullOrEmpty(entry.FullName) && !entry.FullName.EndsWith("/", StringComparison.Ordinal))
                .ToList();

            if (entries.Count == 0)
            {
                throw new InvalidOperationException(L("这个压缩包里没有可解压的文件。", "This archive does not contain extractable files."));
            }

            for (int index = 0; index < entries.Count; index++)
            {
                ZipArchiveEntry entry = entries[index];
                string destinationPath = Path.Combine(destinationDirectory, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                string? destinationFolder = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(destinationFolder))
                {
                    Directory.CreateDirectory(destinationFolder);
                }

                entry.ExtractToFile(destinationPath, true);
                int percent = Math.Min(95, 10 + ((index + 1) * 85 / entries.Count));
                DispatcherQueue.TryEnqueue(() => UpdateProgress(percent, L("正在解压文件...", "Extracting files...")));
            }

            DispatcherQueue.TryEnqueue(() => UpdateProgress(100, L("解压完成", "Extraction complete")));
            return;
        }

        RunTarExtraction(archivePath, destinationDirectory);
        DispatcherQueue.TryEnqueue(() => UpdateProgress(100, L("解压完成", "Extraction complete")));
    }

    private void RunTarExtraction(string archivePath, string destinationDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "tar.exe",
            Arguments = $"-xf \"{archivePath}\" -C \"{destinationDirectory}\"",
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(L("无法启动系统解压工具 tar.exe。", "Unable to start tar.exe."));
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                ? L("当前系统无法解压该格式，建议尝试 ZIP 或 7Z。", "This system could not extract the selected format. Try ZIP or 7Z instead.")
                : error.Trim());
        }
    }

    private async Task ShowMessageAsync(string content, string title)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = content,
            CloseButtonText = L("确定", "OK"),
            XamlRoot = RootGrid.XamlRoot
        };

        await dialog.ShowAsync();
    }

    private async Task<string?> PromptForTextAsync(string content, string title, string defaultValue)
    {
        var textBox = new TextBox
        {
            Text = defaultValue,
            PlaceholderText = L("输入名称", "Enter a name"),
            Margin = new Thickness(0, 12, 0, 0)
        };

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock
        {
            Text = content,
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(textBox);

        var dialog = new ContentDialog
        {
            Title = title,
            Content = panel,
            PrimaryButtonText = L("确定", "OK"),
            CloseButtonText = L("取消", "Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = RootGrid.XamlRoot
        };

        ContentDialogResult result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary ? textBox.Text : null;
    }

    private async Task<RepositoryEditorResult?> PromptForRepositoryAsync(string content, string title, WorkspaceRepository initial)
    {
        TextBox nameBox = new()
        {
            Text = initial.Name,
            PlaceholderText = L("输入仓库名称", "Enter repository name")
        };

        TextBox sourceBox = new()
        {
            Text = initial.SourcePath,
            PlaceholderText = L("第一层 Mod 仓库路径", "First-level mod storage path")
        };

        TextBox targetBox = new()
        {
            Text = initial.TargetPath,
            PlaceholderText = L("目标游戏 Mod 文件夹路径", "Target game mod folder path")
        };

        TextBox launcherBox = new()
        {
            Text = initial.LauncherPath,
            PlaceholderText = L("启动器路径（可选）", "Launcher path (optional)")
        };

        TextBox siteBox = new()
        {
            Text = string.IsNullOrWhiteSpace(initial.OnlineSourceSite) ? DefaultOnlineSourceSite : initial.OnlineSourceSite,
            PlaceholderText = L("在线来源，例如 GameBanana", "Online source, for example GameBanana")
        };

        TextBox categoryBox = new()
        {
            Text = string.IsNullOrWhiteSpace(initial.OnlineCategoryId) ? DefaultOnlineCategoryId : initial.OnlineCategoryId,
            PlaceholderText = L("分类 ID，例如 21842", "Category ID, for example 21842")
        };

        TextBox notesBox = new()
        {
            Text = initial.Notes,
            PlaceholderText = L("备注（可选）", "Notes (optional)"),
            AcceptsReturn = true,
            MinHeight = 72,
            TextWrapping = TextWrapping.Wrap
        };

        StackPanel panel = new() { Spacing = 10, MaxWidth = 520 };
        panel.Children.Add(new TextBlock
        {
            Text = content,
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(CreateLabeledEditor(L("仓库名称", "Repository name"), nameBox));
        panel.Children.Add(CreateLabeledEditor(L("Mod 仓库路径", "Mod storage path"), sourceBox));
        panel.Children.Add(CreateLabeledEditor(L("目标路径", "Target path"), targetBox));
        panel.Children.Add(CreateLabeledEditor(L("启动器路径", "Launcher path"), launcherBox));
        panel.Children.Add(CreateLabeledEditor(L("在线来源", "Online source"), siteBox));
        panel.Children.Add(CreateLabeledEditor(L("分类标识", "Category ID"), categoryBox));
        panel.Children.Add(CreateLabeledEditor(L("备注", "Notes"), notesBox));

        ContentDialog dialog = new()
        {
            Title = title,
            Content = panel,
            PrimaryButtonText = L("保存", "Save"),
            CloseButtonText = L("取消", "Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = RootGrid.XamlRoot
        };

        ContentDialogResult result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return null;
        }

        return new RepositoryEditorResult
        {
            Name = nameBox.Text,
            SourcePath = sourceBox.Text,
            TargetPath = targetBox.Text,
            LauncherPath = launcherBox.Text,
            OnlineSourceSite = siteBox.Text,
            OnlineCategoryId = categoryBox.Text,
            Notes = notesBox.Text
        };
    }

    private static UIElement CreateLabeledEditor(string label, Control editor)
    {
        StackPanel panel = new() { Spacing = 6 };
        panel.Children.Add(new TextBlock
        {
            Text = label
        });
        panel.Children.Add(editor);
        return panel;
    }

    private async Task<bool> ShowConfirmAsync(string content, string title)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock
            {
                Text = content,
                TextWrapping = TextWrapping.Wrap
            },
            PrimaryButtonText = L("确认", "Confirm"),
            CloseButtonText = L("取消", "Cancel"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = RootGrid.XamlRoot
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}

public sealed class RepositoryEditorResult
{
    public string Name { get; set; } = string.Empty;

    public string SourcePath { get; set; } = string.Empty;

    public string TargetPath { get; set; } = string.Empty;

    public string LauncherPath { get; set; } = string.Empty;

    public string OnlineSourceSite { get; set; } = string.Empty;

    public string OnlineCategoryId { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;
}

public sealed class WorkspaceRepository
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "新建仓库";

    public string SourcePath { get; set; } = string.Empty;

    public string TargetPath { get; set; } = string.Empty;

    public string LauncherPath { get; set; } = string.Empty;

    public string OnlineSourceSite { get; set; } = string.Empty;

    public string OnlineCategoryId { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;
}

public sealed class OnlineModCard
{
    public int ItemId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string CharacterName { get; set; } = string.Empty;

    public string RootCategoryName { get; set; } = string.Empty;

    public string Author { get; set; } = string.Empty;

    public int Likes { get; set; }

    public int Views { get; set; }

    public int Downloads { get; set; }

    public double HotnessScore { get; set; }

    public long FileSizeBytes { get; set; }

    public string? PreviewUrl { get; set; }

    public string ProfileUrl { get; set; } = string.Empty;

    public string? DownloadUrl { get; set; }

    public bool HasUpdates { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class OnlineModDetails
{
    public string Summary { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public List<string> ImageUrls { get; set; } = [];

    public string TranslationNote { get; set; } = string.Empty;

    public string AccessRequirementSummary { get; set; } = string.Empty;

    public List<ShortcutBinding> ShortcutBindings { get; set; } = [];
}

public sealed class TrackedModOrigin
{
    public string Path { get; set; } = string.Empty;

    public string SourceSite { get; set; } = string.Empty;

    public int ItemId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string ProfileUrl { get; set; } = string.Empty;

    public string? PreviewUrl { get; set; }

    public DateTimeOffset? LastKnownUpdatedAt { get; set; }
}

public sealed class TrackedModUpdateResult
{
    public string Path { get; set; } = string.Empty;

    public int ItemId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string ProfileUrl { get; set; } = string.Empty;

    public string? PreviewUrl { get; set; }

    public DateTimeOffset? LastKnownUpdatedAt { get; set; }

    public DateTimeOffset? LatestUpdatedAt { get; set; }

    public bool HasUpdate { get; set; }

    public string StatusTextZh { get; set; } = string.Empty;

    public string StatusTextEn { get; set; } = string.Empty;
}

public sealed class OnlineModPageResult
{
    public OnlineModPageResult(List<int> modIds, int totalCount, int page, int pageSize)
    {
        ModIds = modIds;
        TotalCount = totalCount;
        Page = page;
        PageSize = Math.Max(1, pageSize);
    }

    public List<int> ModIds { get; }

    public int TotalCount { get; }

    public int Page { get; }

    public int PageSize { get; }

    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
}

public sealed class OnlineCategoryPageResult
{
    public OnlineCategoryPageResult(List<OnlineModCard> mods, int totalCount, int page, int pageSize)
    {
        Mods = mods;
        TotalCount = totalCount;
        Page = page;
        PageSize = Math.Max(1, pageSize);
    }

    public List<OnlineModCard> Mods { get; }

    public int TotalCount { get; }

    public int Page { get; }

    public int PageSize { get; }

    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
}

public sealed class BetaShellConfig
{
    public string? CurrentPrimarySection { get; set; }

    public string? SelectedRepositoryId { get; set; }

    public string? UpdateCheckInterval { get; set; }

    public string? LastUpdateCheckUtc { get; set; }

    public string? ModUpdateCheckInterval { get; set; }

    public string? LastModUpdateCheckUtc { get; set; }

    public string? LatestReleaseTag { get; set; }

    public string? LatestReleaseTitle { get; set; }

    public string? LatestReleaseBody { get; set; }

    public string? LatestReleasePublishedAt { get; set; }

    public string? LatestReleaseUrl { get; set; }

    public List<WorkspaceRepository> Repositories { get; set; } = [];
}

public readonly record struct RepositorySnapshot(int FirstLevelCount, int ModCount, bool IsReady);

public sealed class FirstLevelFolderItem
{
    public FirstLevelFolderItem(string path)
    {
        Path = path;
        Name = System.IO.Path.GetFileName(path);
    }

    public string Name { get; }

    public string Path { get; }

    public List<SecondLevelFolderItem> Children { get; } = [];

    public int ChildrenCount => Children.Count;
}

public sealed class SecondLevelFolderItem
{
    public SecondLevelFolderItem(string path, List<string> files, string state)
    {
        Path = path;
        Name = System.IO.Path.GetFileName(path);
        Files = files;
        State = state;
    }

    public string Name { get; }

    public string Path { get; }

    public List<string> Files { get; }

    public string State { get; set; }

    public int FilesCount => Files.Count;
}

public sealed class ShortcutBinding
{
    public ShortcutBinding(string shortcut, string action)
    {
        Shortcut = shortcut;
        Action = action;
    }

    public string Shortcut { get; }

    public string Action { get; }
}

public sealed class ProgressInfo
{
    public ProgressInfo(int percent, string message)
    {
        Percent = percent;
        Message = message;
    }

    public int Percent { get; }

    public string Message { get; }
}



