using IntegratedModManager.Core.Repository;
using IntegratedModManager.Data;

namespace ModFolderCopier.WinUI;

/// <summary>
/// What the Mod workspace shows, kept whether or not the workspace page exists (stage J finish, step 7b, moved from the
/// page and the window unchanged): the repository's characters and Mods from the last scan, the Mod list's view and
/// column widths, the local tag libraries, the status line, and scanning the repository (at startup, after a path
/// change and on refresh). The dashboard, the command palette, the overlay and Default slot seeding read the scan; the
/// workspace page draws it when it is shown.
/// </summary>
internal sealed partial class WorkspaceService(AppServices services)
{
    private readonly AppServices _services = services;
    private Action<string, Exception> _log = (_, _) => { };
    private Func<bool> _saveConfig = () => true;
    private Action<FirstLevelFolderItem> _avatarLoaded = _ => { };

    /// <summary>
    /// Called once by the window: the error log, saving config.ini, the local database with the repository index, and
    /// showing a loaded avatar on the dashboard.
    /// </summary>
    public void Attach(Action<string, Exception> log, Func<bool> saveConfig, AppDataStore dataStore, Action<FirstLevelFolderItem> avatarLoaded)
    {
        _log = log;
        _saveConfig = saveConfig;
        _dataStore = dataStore;
        _avatarLoaded = avatarLoaded;
    }

    /// <summary>The repository's characters (first-level folders) with their Mods, from the last scan.</summary>
    public CharacterListViewModel Characters { get; } = new();

    /// <summary>The Mod list's view, filters and column widths (the view and widths are saved in beta-shell.json).</summary>
    public ModListViewModel ModList { get; } = new();

    /// <summary>Local tags of each repository (saved in beta-shell.json).</summary>
    public Dictionary<string, LocalLibraryState> LocalLibraries { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    private string _status = string.Empty;

    /// <summary>The workspace's status line. Set from anywhere; the workspace page shows it.</summary>
    public string Status
    {
        get => _status;
        set
        {
            _status = value ?? string.Empty;
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? StatusChanged;

    /// <summary>A repository scan is running (the workspace shows its progress bar).</summary>
    public bool IsScanning { get; private set; }

    public event EventHandler? ScanningChanged;

    // A newer scan or closing the window replaces the running one.
    private CancellationTokenSource? _scanCancellation;
    private int _scanGeneration;

    /// <summary>Stops the running scan (the window is closing).</summary>
    public void CancelScan()
    {
        ++_scanGeneration;
        _scanCancellation?.Cancel();
    }

    private string T(string key, params object?[] values) => _services.Language.T(key, values);

    private RepositoryPathsViewModel Paths => _services.Paths;

    private void SetScanning(bool scanning)
    {
        IsScanning = scanning;
        ScanningChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Scans the repository's Mod folder into <see cref="Characters"/>. <paramref name="cleared"/> runs once the old
    /// lists are cleared, <paramref name="loaded"/> once the new ones are in (the workspace page redraws there); the
    /// result line follows. Returns null when the paths are not usable or a newer scan replaced this one.
    /// </summary>
    public async Task<WorkspaceScanResult?> ScanAsync(Action? cleared = null, Action? loaded = null)
    {
        _scanCancellation?.Cancel();
        int generation = ++_scanGeneration;
        Characters.All.Clear();
        Characters.Visible.Clear();
        cleared?.Invoke();

        string sourceDir = Paths.Source.Trim();
        string targetDir = Paths.Target.Trim();
        if (string.IsNullOrWhiteSpace(sourceDir) || !Directory.Exists(sourceDir))
        {
            Status = T("LocalLibrary.ChooseAValidModStorageFolder");
            return null;
        }

        if (!string.IsNullOrWhiteSpace(targetDir) && !Directory.Exists(targetDir))
        {
            Status = T("LocalLibrary.TheTargetFolderDoesNotExist");
            return null;
        }

        _saveConfig();
        string repositoryId = _services.Repositories.Selected?.Id ?? sourceDir;
        using var cancellation = new CancellationTokenSource();
        _scanCancellation = cancellation;
        SetScanning(true);
        try
        {
            var result = await Task.Run(() =>
            {
                var items = LoadFolderItemsUsingIndex(repositoryId, sourceDir, targetDir, cancellation.Token, out int warnings);
                return (items.Items, items.SecondCount, Warnings: warnings);
            }, cancellation.Token);

            if (cancellation.IsCancellationRequested || generation != _scanGeneration
                || !string.Equals(sourceDir, Paths.Source.Trim(), StringComparison.OrdinalIgnoreCase)
                || !string.Equals(targetDir, Paths.Target.Trim(), StringComparison.OrdinalIgnoreCase)) return null;
            Characters.Load(result.Items);
            loaded?.Invoke();
            _ = RefreshCharacterFolderAvatarsAsync();
            if (_services.Repositories.Selected is { SourcePath: { Length: > 0 } scannedSource } scanned
                && string.Equals(Path.GetFullPath(scannedSource).TrimEnd('\\'), Path.GetFullPath(sourceDir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                _ = _services.Slots.SeedDefaultSlotsAsync(scanned, result.Items);
            Status = result.Warnings == 0
                ? T("LocalLibrary.LoadedFirstLevelFoldersAndSecond", Characters.All.Count, result.SecondCount)
                : T("LocalLibrary.LoadedCategoriesAndModsScanCache", Characters.All.Count, result.SecondCount, result.Warnings);
            return new WorkspaceScanResult(Characters.All.Count, result.SecondCount, Failed: false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // A newer refresh/repository owns the display. Do not overwrite it.
            return null;
        }
        catch (Exception ex)
        {
            _log("Refresh repository folders", ex);
            if (generation == _scanGeneration && !cancellation.IsCancellationRequested)
            {
                Status = T("LocalLibrary.RepositoryScanFailedTheExistingIndex");
                return new WorkspaceScanResult(0, 0, Failed: true);
            }
            return null;
        }
        finally
        {
            if (ReferenceEquals(_scanCancellation, cancellation)) _scanCancellation = null;
            if (generation == _scanGeneration) SetScanning(false);
        }
    }
}

/// <summary>What a repository scan found; <see cref="Failed"/> when it could not read the folders.</summary>
internal sealed record WorkspaceScanResult(int CharacterCount, int ModCount, bool Failed);
