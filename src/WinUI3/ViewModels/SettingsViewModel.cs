using CommunityToolkit.Mvvm.ComponentModel;

namespace ModFolderCopier.WinUI;

/// <summary>What the Settings page asks the window to do.</summary>
public enum SettingsAction
{
    OpenGitHub,
    CheckUpdates,
    InstallUpdate,
    EditOnlineRepository,
    EditCharacterMappings,
    ExportDiagnostics,
    SubmitDiagnostics,
    CopyFeedbackGroup,
    OpenDiscord,

    /// <summary>Open the address in <see cref="SettingsActionRequest.Argument"/>.</summary>
    OpenLink
}

public sealed record SettingsActionRequest(SettingsAction Action, string? Argument = null);

/// <summary>What the app update button does, which also decides its label.</summary>
public enum AppUpdateInstallAction
{
    /// <summary>This build has no self-update: open the public release page.</summary>
    ViewPublicReleases,

    /// <summary>A newer update package lies next to the app.</summary>
    InstallLocalPackage,

    /// <summary>Nothing checked yet: check, then offer the update.</summary>
    CheckAndGet,

    DownloadAndInstall,

    /// <summary>The latest release is not newer or has no package: show its page.</summary>
    OpenReleasePage
}

/// <summary>
/// The Settings page: the appearance choices, the app update state, the repository's online
/// information and the diagnostics status. The page shows it and turns clicks into
/// <see cref="ActionRequested"/>; the window does the work and writes the results back here, so the
/// page never reaches into the window (docs/development/UI-Structure-Plan.md, stage J2).
/// </summary>
/// <remarks>
/// Text that follows the language (labels, descriptions) is the page's own. The window writes the
/// status lines and release details already in the current language, and writes them again after
/// a language switch.
/// </remarks>
public sealed partial class SettingsViewModel : ObservableObject
{
    /// <summary>Manual, daily, every three days, weekly: the order of the interval list.</summary>
    public const int UpdateIntervalCount = 4;

    // The QQ group users join to report problems and talk about the app.
    public const string FeedbackQqGroup = "1041384622";
    public const string FeedbackDiscordUrl = "https://discord.gg/G9nszEQgrV";

    private bool _selfUpdateEnabled = true;
    private bool _isBusy;
    private bool _isCheckingUpdates;
    private bool _checkOnStartup;
    private int _updateIntervalIndex;
    private bool _minimizeToTray;
    private bool _showTitleBarSearch = true;
    private string _updateStatus = string.Empty;
    private string _releaseTitle = string.Empty;
    private string _releaseMeta = string.Empty;
    private string _releaseNotes = string.Empty;
    private bool _hasReleaseDetails = true;
    private string? _localPackageVersion;
    private bool _hasReleasePage;
    private bool _canDownloadRelease;
    private bool _isDownloadingUpdate;
    private bool _isDownloadProgressKnown;
    private double _downloadProgress;
    private string _downloadText = string.Empty;
    private string _onlineSummary = string.Empty;
    private IReadOnlyList<SettingInfoItem> _onlineInfo = [];
    private string _diagnosticsStatus = string.Empty;

    private string _appVersion = string.Empty;

    public AppearanceSettingsViewModel Appearance { get; } = new();

    /// <summary>The running version, shown under About.</summary>
    public string AppVersion
    {
        get => _appVersion;
        set => SetProperty(ref _appVersion, value ?? string.Empty);
    }

    /// <summary>Raised when the user asks for something only the window can do.</summary>
    public event EventHandler<SettingsActionRequest>? ActionRequested;

    public void Request(SettingsAction action, string? argument = null)
    {
        // A link without an address has nowhere to go.
        if (action == SettingsAction.OpenLink && string.IsNullOrWhiteSpace(argument)) return;
        ActionRequested?.Invoke(this, new SettingsActionRequest(action, argument));
    }

    /// <summary>False in builds without self-update (local test builds): update settings are locked.</summary>
    public bool SelfUpdateEnabled
    {
        get => _selfUpdateEnabled;
        set
        {
            if (!SetProperty(ref _selfUpdateEnabled, value)) return;
            if (!value) CheckOnStartup = false;
            OnPropertyChanged(nameof(CanCheckUpdates));
            OnPropertyChanged(nameof(InstallAction));
        }
    }

    /// <summary>A deployment is running; language and theme wait until it is done.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value)) OnPropertyChanged(nameof(CanChangeAppearance));
        }
    }

    public bool CanChangeAppearance => !_isBusy;

    public bool IsCheckingUpdates
    {
        get => _isCheckingUpdates;
        set
        {
            if (SetProperty(ref _isCheckingUpdates, value)) OnPropertyChanged(nameof(CanCheckUpdates));
        }
    }

    public bool CanCheckUpdates => _selfUpdateEnabled && !_isCheckingUpdates;

    /// <summary>Check for an app update at startup; always off without self-update.</summary>
    public bool CheckOnStartup
    {
        get => _checkOnStartup;
        set => SetProperty(ref _checkOnStartup, value && _selfUpdateEnabled);
    }

    /// <summary>The index in the interval list; values outside it are ignored.</summary>
    public int UpdateIntervalIndex
    {
        get => _updateIntervalIndex;
        set
        {
            if (value >= 0 && value < UpdateIntervalCount) SetProperty(ref _updateIntervalIndex, value);
        }
    }

    public bool MinimizeToTray
    {
        get => _minimizeToTray;
        set => SetProperty(ref _minimizeToTray, value);
    }

    /// <summary>Whether the title bar shows the search box; Ctrl+K opens the palette either way.</summary>
    public bool ShowTitleBarSearch
    {
        get => _showTitleBarSearch;
        set => SetProperty(ref _showTitleBarSearch, value);
    }

    public string UpdateStatus
    {
        get => _updateStatus;
        set
        {
            if (SetProperty(ref _updateStatus, value ?? string.Empty)) OnPropertyChanged(nameof(HasUpdateStatus));
        }
    }

    // Status lines take no room until there is something to report.
    public bool HasUpdateStatus => !string.IsNullOrWhiteSpace(_updateStatus);

    public string ReleaseTitle
    {
        get => _releaseTitle;
        set => SetProperty(ref _releaseTitle, value ?? string.Empty);
    }

    public string ReleaseMeta
    {
        get => _releaseMeta;
        set => SetProperty(ref _releaseMeta, value ?? string.Empty);
    }

    public string ReleaseNotes
    {
        get => _releaseNotes;
        set => SetProperty(ref _releaseNotes, value ?? string.Empty);
    }

    /// <summary>False before the first check, when the details are only placeholders.</summary>
    public bool HasReleaseDetails
    {
        get => _hasReleaseDetails;
        set => SetProperty(ref _hasReleaseDetails, value);
    }

    /// <summary>The version of a newer update package next to the app, if there is one.</summary>
    public string? LocalPackageVersion => _localPackageVersion;

    /// <summary>Sets what the update button can do.</summary>
    /// <param name="localPackageVersion">A newer update package next to the app, or null.</param>
    /// <param name="hasReleasePage">A check found the latest release.</param>
    /// <param name="canDownloadRelease">That release is newer and has a package to download.</param>
    public void SetInstallState(string? localPackageVersion, bool hasReleasePage, bool canDownloadRelease)
    {
        _localPackageVersion = localPackageVersion;
        _hasReleasePage = hasReleasePage;
        _canDownloadRelease = canDownloadRelease;
        OnPropertyChanged(nameof(LocalPackageVersion));
        OnPropertyChanged(nameof(InstallAction));
    }

    public AppUpdateInstallAction InstallAction
        => !_selfUpdateEnabled ? AppUpdateInstallAction.ViewPublicReleases
            : _localPackageVersion is not null ? AppUpdateInstallAction.InstallLocalPackage
            : !_hasReleasePage ? AppUpdateInstallAction.CheckAndGet
            : _canDownloadRelease ? AppUpdateInstallAction.DownloadAndInstall
            : AppUpdateInstallAction.OpenReleasePage;

    /// <summary>An update package is downloading or being verified.</summary>
    public bool IsDownloadingUpdate
    {
        get => _isDownloadingUpdate;
        private set => SetProperty(ref _isDownloadingUpdate, value);
    }

    /// <summary>False while the size is unknown: the bar runs without a value.</summary>
    public bool IsDownloadProgressKnown
    {
        get => _isDownloadProgressKnown;
        private set => SetProperty(ref _isDownloadProgressKnown, value);
    }

    public bool IsDownloadProgressIndeterminate => !_isDownloadProgressKnown;

    /// <summary>0 to 100.</summary>
    public double DownloadProgress
    {
        get => _downloadProgress;
        private set => SetProperty(ref _downloadProgress, value);
    }

    public string DownloadText
    {
        get => _downloadText;
        private set => SetProperty(ref _downloadText, value);
    }

    /// <summary>Shows the download panel with a running bar.</summary>
    public void BeginDownload(string text)
    {
        DownloadProgress = 0;
        SetProgressKnown(false);
        DownloadText = text;
        IsDownloadingUpdate = true;
    }

    /// <summary>
    /// A null percentage means the size is not known: the bar runs and keeps its value. A null text
    /// keeps the current one.
    /// </summary>
    public void ReportDownload(double? percent, string? text)
    {
        if (percent is { } value) DownloadProgress = Math.Clamp(value, 0, 100);
        SetProgressKnown(percent is not null);
        if (text is not null) DownloadText = text;
    }

    /// <summary>Changes the text only: verifying, starting the updater.</summary>
    public void ReportDownloadText(string text) => DownloadText = text;

    /// <summary>Hides the panel after a failed download.</summary>
    public void EndDownload()
    {
        SetProgressKnown(true);
        IsDownloadingUpdate = false;
    }

    private void SetProgressKnown(bool known)
    {
        IsDownloadProgressKnown = known;
        OnPropertyChanged(nameof(IsDownloadProgressIndeterminate));
    }

    /// <summary>What the current repository's online settings are for, or why there are none.</summary>
    public string OnlineSummary
    {
        get => _onlineSummary;
        private set => SetProperty(ref _onlineSummary, value);
    }

    public IReadOnlyList<SettingInfoItem> OnlineInfo => _onlineInfo;

    // One compact table, split into two halves side by side so it stays short; the first half takes the odd item.
    public IReadOnlyList<SettingInfoItem> OnlineInfoFirstHalf => _onlineInfo.Take((_onlineInfo.Count + 1) / 2).ToList();

    public IReadOnlyList<SettingInfoItem> OnlineInfoSecondHalf => _onlineInfo.Skip((_onlineInfo.Count + 1) / 2).ToList();

    public void SetOnlineInfo(string summary, IReadOnlyList<SettingInfoItem> items)
    {
        OnlineSummary = summary ?? string.Empty;
        _onlineInfo = items ?? [];
        OnPropertyChanged(nameof(OnlineInfo));
        OnPropertyChanged(nameof(OnlineInfoFirstHalf));
        OnPropertyChanged(nameof(OnlineInfoSecondHalf));
    }

    public string DiagnosticsStatus
    {
        get => _diagnosticsStatus;
        set
        {
            if (SetProperty(ref _diagnosticsStatus, value ?? string.Empty)) OnPropertyChanged(nameof(HasDiagnosticsStatus));
        }
    }

    public bool HasDiagnosticsStatus => !string.IsNullOrWhiteSpace(_diagnosticsStatus);
}
