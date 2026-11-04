using CommunityToolkit.Mvvm.ComponentModel;
using IntegratedModManager.Core.Updates;

namespace ModFolderCopier.WinUI;

/// <summary>What the Updates page asks the window to do; <see cref="UpdatesActionRequest.Item"/> says on what.</summary>
public enum UpdatesAction
{
    CheckModUpdates,

    /// <summary>Item: the <see cref="TrackedModRow.Source"/> of the row.</summary>
    DownloadAndReplace,

    /// <summary>Item: the profile address.</summary>
    OpenModPage,

    /// <summary>Item: the backup folder kept by an earlier replacement.</summary>
    OpenOldBackup,

    /// <summary>Item: the <see cref="TrackedModRow.Source"/> of the row.</summary>
    RemoveTracking,

    BrowseOnline,
    ClearFinishedDownloads,

    /// <summary>Item: the download task whose button was clicked (cancel, retry or open its folder).</summary>
    DownloadTaskAction,

    RollbackLastInstall,
    OpenCombinationPresets,
    RefreshBackups,

    /// <summary>Item: the backup folder.</summary>
    RestoreBackup
}

public sealed record UpdatesActionRequest(UpdatesAction Action, object? Item = null);

/// <summary>One row of the tracked Mods list: the check result, or the bare origin before any check.</summary>
/// <param name="FailureText">The failed check's message, in the current language; null otherwise.</param>
/// <param name="Source">The window's own record of the Mod, handed back with a request.</param>
public sealed record TrackedModRow(
    string Title,
    string Path,
    string ProfileUrl,
    string? PreviewUrl,
    string? BackupPath,
    DateTimeOffset? LastKnownUpdatedAt,
    DateTimeOffset? LatestUpdatedAt,
    bool HasUpdate,
    TrackedModStatus Status,
    string? FailureText,
    object? Source = null);

/// <summary>One install backup: its description, a line with date, Mods and size, and its folder.</summary>
public sealed record InstallBackupRow(string Path, string Description, string Details);

/// <summary>
/// The Updates page (stage J3, docs/development/UI-Structure-Plan.md): tracked Mods with their filter
/// and search, the download history filter, install safety, rollback and backups. The window checks
/// for updates, downloads and restores, and writes the results here; the page shows them and sends
/// clicks out as <see cref="ActionRequested"/>.
/// </summary>
/// <remarks>Status lines arrive in the current language; the window writes them again after a language switch.</remarks>
public sealed partial class UpdatesViewModel : ObservableObject
{
    public const int UpdateIntervalCount = 4;
    public const double MinimumBackupLimitGb = 0.5;
    public const double MaximumBackupLimitGb = 100;

    private IReadOnlyList<TrackedModRow> _trackedMods = [];
    private TrackedModFilter _trackedModFilter;
    private string _search = string.Empty;
    private readonly HashSet<string> _replacingPaths = new(StringComparer.OrdinalIgnoreCase);
    private bool _isCheckingModUpdates;
    private string _modUpdateStatus = string.Empty;
    private string _trackedSummary = string.Empty;
    private int _modUpdateIntervalIndex;
    private bool _conflictDetection = true;
    private bool _canUndoLastInstall;
    private string _rollbackStatus = string.Empty;
    private double _backupLimitGb = 5;
    private string _backupSummary = string.Empty;
    private IReadOnlyList<InstallBackupRow> _backups = [];
    private DownloadHistoryFilter _historyFilter;

    /// <summary>Raised when the user asks for something only the window can do.</summary>
    public event EventHandler<UpdatesActionRequest>? ActionRequested;

    /// <summary>Raised after the window wrote a new state; the page shows it again.</summary>
    public event EventHandler? Refreshed;

    public void Request(UpdatesAction action, object? item = null) => ActionRequested?.Invoke(this, new UpdatesActionRequest(action, item));

    public void NotifyRefreshed() => Refreshed?.Invoke(this, EventArgs.Empty);

    // Tracked Mods.
    public IReadOnlyList<TrackedModRow> TrackedMods => _trackedMods;

    public void SetTrackedMods(IReadOnlyList<TrackedModRow> rows)
    {
        int updates = AvailableUpdateCount;
        _trackedMods = rows ?? [];
        OnPropertyChanged(nameof(TrackedMods));
        OnPropertyChanged(nameof(VisibleTrackedMods));
        if (updates != AvailableUpdateCount) OnPropertyChanged(nameof(AvailableUpdateCount));
    }

    /// <summary>Tracked Mods with a newer version: the number on the navigation badge.</summary>
    public int AvailableUpdateCount => _trackedMods.Count(row => row.HasUpdate);

    public TrackedModFilter TrackedModFilter
    {
        get => _trackedModFilter;
        set
        {
            if (Enum.IsDefined(value) && SetProperty(ref _trackedModFilter, value)) OnPropertyChanged(nameof(VisibleTrackedMods));
        }
    }

    public string Search
    {
        get => _search;
        set
        {
            if (SetProperty(ref _search, value ?? string.Empty)) OnPropertyChanged(nameof(VisibleTrackedMods));
        }
    }

    public int CountFor(TrackedModFilter filter) => _trackedMods.Count(row => UpdatesListPolicy.Matches(row.Status, filter));

    /// <summary>The rows the filter and search keep, in the policy's order (updates and failures first).</summary>
    public IReadOnlyList<TrackedModRow> VisibleTrackedMods => UpdatesListPolicy.Order(
            _trackedMods.Where(row => UpdatesListPolicy.Matches(row.Status, _trackedModFilter)
                && UpdatesListPolicy.MatchesSearch(row.Title, row.Path, _search)),
            row => row.Status, row => row.Title)
        .ToList();

    /// <summary>Mods being downloaded and replaced: their buttons wait.</summary>
    public bool IsReplacing(string path) => _replacingPaths.Contains(path);

    public void SetReplacing(IEnumerable<string> paths)
    {
        _replacingPaths.Clear();
        foreach (string path in paths) _replacingPaths.Add(path);
    }

    public bool IsCheckingModUpdates
    {
        get => _isCheckingModUpdates;
        set => SetProperty(ref _isCheckingModUpdates, value);
    }

    public string ModUpdateStatus
    {
        get => _modUpdateStatus;
        set => SetProperty(ref _modUpdateStatus, value ?? string.Empty);
    }

    /// <summary>How many Mods are tracked, when they were last checked and how many have updates.</summary>
    public string TrackedSummary
    {
        get => _trackedSummary;
        set => SetProperty(ref _trackedSummary, value ?? string.Empty);
    }

    /// <summary>Manual, daily, every three days, weekly; values outside the list are ignored.</summary>
    public int ModUpdateIntervalIndex
    {
        get => _modUpdateIntervalIndex;
        set
        {
            if (value >= 0 && value < UpdateIntervalCount) SetProperty(ref _modUpdateIntervalIndex, value);
        }
    }

    // Install safety and recovery.
    public bool ConflictDetection
    {
        get => _conflictDetection;
        set => SetProperty(ref _conflictDetection, value);
    }

    /// <summary>There is a last Mod change to undo.</summary>
    public bool CanUndoLastInstall => _canUndoLastInstall;

    public string RollbackStatus => _rollbackStatus;

    public void SetRollback(bool canUndo, string status)
    {
        _canUndoLastInstall = canUndo;
        _rollbackStatus = status ?? string.Empty;
        OnPropertyChanged(nameof(CanUndoLastInstall));
        OnPropertyChanged(nameof(RollbackStatus));
    }

    // Install backups.

    /// <summary>The storage limit for install backups, kept between 0.5 and 100 GB; not-a-number is ignored.</summary>
    public double BackupLimitGb
    {
        get => _backupLimitGb;
        set
        {
            if (!double.IsNaN(value)) SetProperty(ref _backupLimitGb, Math.Clamp(value, MinimumBackupLimitGb, MaximumBackupLimitGb));
        }
    }

    public string BackupSummary => _backupSummary;

    public IReadOnlyList<InstallBackupRow> Backups => _backups;

    public void SetBackups(string summary, IReadOnlyList<InstallBackupRow> backups)
    {
        _backupSummary = summary ?? string.Empty;
        _backups = backups ?? [];
        OnPropertyChanged(nameof(BackupSummary));
        OnPropertyChanged(nameof(Backups));
    }

    // Download history.
    public DownloadHistoryFilter HistoryFilter
    {
        get => _historyFilter;
        set
        {
            if (Enum.IsDefined(value)) SetProperty(ref _historyFilter, value);
        }
    }
}
