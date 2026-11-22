using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using IntegratedModManager.Core.Deployment;
using IntegratedModManager.Core.Shortcuts;
using Microsoft.UI.Dispatching;
using Path = System.IO.Path;

namespace ModFolderCopier.WinUI;

/// <summary>
/// The Shortcuts page's and the overlay's work (stage J finish, step 8b, moved from the window unchanged): reading the
/// repository's Mods and their bindings, the target window, sending keys, the global hotkey, the privilege notice and
/// restarting as administrator. The page and the overlay show <see cref="ShortcutPageViewModel"/> and ask for work
/// through it and through <see cref="IShortcutOverlayActions"/>, which this service answers. Its other parts: saving
/// presets from the overlay, character follow, the slot pictures and the overlay's requests.
/// </summary>
internal sealed partial class ShortcutPanelService(AppServices services)
{
    private readonly AppServices _services = services;
    private ShortcutPageViewModel _shortcuts => _services.Shortcuts;
    private nint _windowHandle;
    private DispatcherQueue _dispatcher = null!;
    private Action _activate = () => { };
    private Action _closeWindow = () => { };
    private Action _openDashboard = () => { };
    private Func<string?> _currentModPath = () => null;
    private Action _refreshSlotControls = () => { };
    private Func<ShortcutsPage?> _page = () => null;
    private Action<string, Exception> _log = (_, _) => { };
    private Action<string, Exception> _reportFailure = (_, _) => { };
    private Func<string, Func<Task>, Task> _runUiAction = (_, action) => action();
    private readonly ShortcutInputService _shortcutInput = new();
    private readonly ModShortcutScanCache _shortcutScanCache = new();
    // The overlay: its window, content and look (stage J5b); this service answers its requests (IShortcutOverlayActions).
    private ShortcutOverlayController _overlay = null!;
    // The Mod list and windows are read on first use: the page shown or the overlay opened.
    private bool _shortcutsPrepared;
    private ShortcutPanelHotkey? _shortcutPanelHotkey;
    private bool _refreshingShortcutOptions;
    // Repository-relative paths of Mods enabled in the loader folder; null when it cannot be determined.
    private HashSet<string>? _shortcutDeployedMods;
    private int _shortcutDeployedRevision;

    /// <summary>
    /// Called once by the window: its handle (for the hotkey) and dispatcher, bringing it to the front, closing it, the
    /// dashboard, the workspace's selected Mod, redrawing the workspace's slot card, the Shortcuts page once it exists,
    /// the error log, reporting a failed action and running a UI action. Creates the overlay.
    /// </summary>
    public void Attach(nint windowHandle, DispatcherQueue dispatcher, Action activate, Action closeWindow, Action openDashboard,
        Func<string?> currentModPath, Action refreshSlotControls, Func<ShortcutsPage?> page, Action<string, Exception> log,
        Action<string, Exception> reportFailure, Func<string, Func<Task>, Task> runUiAction)
    {
        _windowHandle = windowHandle;
        _dispatcher = dispatcher;
        _activate = activate;
        _closeWindow = closeWindow;
        _openDashboard = openDashboard;
        _currentModPath = currentModPath;
        _refreshSlotControls = refreshSlotControls;
        _page = page;
        _log = log;
        _reportFailure = reportFailure;
        _runUiAction = runUiAction;
        _overlay = new ShortcutOverlayController(_shortcuts, _services.Language, _services.Appearance, this, dispatcher);
        _services.Overlay = _overlay;
        _shortcuts.ActionRequested += OnShortcutPageRequested;
    }

    /// <summary>The overlay's controller, for the slot and picture services.</summary>
    public ShortcutOverlayController OverlayController => _overlay;

    /// <summary>Sends keys to the target window; the slot service sends the reload key through it.</summary>
    public ShortcutInputService Input => _shortcutInput;

    /// <summary>Set while the overlay saves a preset or shows a picture layer.</summary>
    public bool SavingFromOverlay => _savingOverlaySinglePreset;

    // After the settings are loaded, so loading them is not taken for the user's changes.
    public void ListenToState() => _shortcuts.PropertyChanged += OnShortcutStateChanged;

    private string T(string key, params object?[] values) => _services.Language.T(key, values);

    /// <summary>The page relabels itself by rebuilding from the view model; the overlay is rebuilt here, and the Mods are read again.</summary>
    public void RebuildForLanguage()
    {
        if (!_shortcutsPrepared) return;
        ++_shortcuts.ScanRevision;
        _overlay.RebuildForLanguage();
        _page()?.RebuildForLanguage();
        // Input stays disarmed after the rebuild; the user re-enables it deliberately.
        _shortcuts.InputEnabled = false;
        UpdateShortcutPrivilegeNotice();
        _services.Slots.RefreshLoaderAutoSave();
        _ = RefreshSafelyAsync();
    }

    /// <summary>Reads the windows once, when the page is first shown or the overlay first opened.</summary>
    public void Prepare()
    {
        if (_shortcutsPrepared) return;
        _shortcutsPrepared = true;
        StartCharacterFollow();
        UpdateShortcutPrivilegeNotice();
        _services.Slots.RefreshLoaderAutoSave();
        RefreshShortcutTargets();
    }

    // Stage J5a: what the page and the overlay change in the view model, applied here.
    private void OnShortcutStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ShortcutPageViewModel.SelectedMod):
                if (_refreshingShortcutOptions) return;
                if (!_characterAutoSelecting)
                {
                    _shortcuts.CharacterFollowEnabled = false;
                    if (SelectedShortcutRole() is string role && _shortcuts.SelectedMod is ShortcutModOption selected)
                        _characterPreferredMods[role] = selected.Path;
                }
                _ = _runUiAction("Select shortcut Mod", () => LoadShortcutSelectionAsync());
                break;
            case nameof(ShortcutPageViewModel.SelectedTarget):
                // A newly selected target always requires deliberate re-arming (the view model disarms input).
                UpdateShortcutPrivilegeNotice();
                RefreshShortcutOverlay();
                break;
            case nameof(ShortcutPageViewModel.InputEnabled):
                RefreshShortcutOverlay();
                break;
            case nameof(ShortcutPageViewModel.HideUnsupported):
                _services.Configuration.SaveShell();
                RenderShortcutButtons();
                break;
            case nameof(ShortcutPageViewModel.InputMode):
                _services.Configuration.SaveShell();
                break;
            // The overlay applies its own look (ShortcutOverlayController); the settings are saved here.
            case nameof(ShortcutPageViewModel.OverlayOpacity):
            case nameof(ShortcutPageViewModel.OverlayElementOpacity):
            case nameof(ShortcutPageViewModel.OverlayCompactView):
            case nameof(ShortcutPageViewModel.OverlayPlacements):
            case nameof(ShortcutPageViewModel.OverlayCompactFontSize):
            case nameof(ShortcutPageViewModel.OverlayAutoContrast):
            case nameof(ShortcutPageViewModel.OverlaySlotSwitchEnabled):
            case nameof(ShortcutPageViewModel.OverlayCompactSlots):
            case nameof(ShortcutPageViewModel.CapturePresetPreviews):
            case nameof(ShortcutPageViewModel.KeepMenuStateVariables):
                _services.Configuration.SaveShell();
                break;
            case nameof(ShortcutPageViewModel.CharacterFollowEnabled):
                OnCharacterFollowToggled();
                break;
        }
    }

    private void OnShortcutPageRequested(object? sender, ShortcutPageRequest request)
    {
        switch (request.Action)
        {
            case ShortcutPageAction.RefreshWindows: RefreshShortcutTargets(); break;
            case ShortcutPageAction.ReloadIni: _ = RefreshOverlayFromDiskAsync(); break;
            case ShortcutPageAction.OpenIni: ShowShortcutIniFile(); break;
            case ShortcutPageAction.SendShortcut when request.Argument is DetectedModShortcut shortcut:
                _ = _runUiAction("Send shortcut", () => SendShortcutFromPanelAsync(shortcut));
                break;
            case ShortcutPageAction.SendNumLock: _ = SendNumLockAsync(); break;
            case ShortcutPageAction.RestartElevated: RestartManagerElevated(); break;
            case ShortcutPageAction.SetHotkey when request.Argument is (uint modifiers, uint key): SetShortcutHotkey(modifiers, key); break;
            case ShortcutPageAction.OpenDashboard: _openDashboard(); break;
            // The text colour is saved when its picker closes, not for every colour the picker passes through.
            case ShortcutPageAction.SaveSettings: _services.Configuration.SaveShell(); break;
            case ShortcutPageAction.EnablePresetPreviews: _ = _runUiAction("Enable preset pictures", _services.PresetPreviews.EnablePresetPreviewsAsync); break;
            case ShortcutPageAction.ClearPresetPreviewRegion: _services.PresetPreviews.ClearPresetPreviewRegion(); break;
            case ShortcutPageAction.SelectCharacterRegion: _ = SelectCharacterRegionAsync(); break;
            case ShortcutPageAction.SaveCharacterReference: _ = SaveCharacterReferenceAsync(); break;
            case ShortcutPageAction.RefreshLoaderAutoSave: _services.Slots.RefreshLoaderAutoSave(); break;
            case ShortcutPageAction.SetLoaderAutoSave when request.Argument is int seconds:
                _ = _runUiAction("Change loader auto-save interval", () => _services.Slots.SetLoaderAutoSaveAsync(seconds));
                break;
            case ShortcutPageAction.DisableHotkey:
                _shortcutPanelHotkey?.Disable();
                _shortcuts.HotkeyEnabled = false;
                _shortcuts.HotkeyRegistered = false;
                _shortcuts.HotkeyRejected = false;
                _services.Configuration.SaveShell();
                break;
        }
    }

    // A choice takes effect at once; when it cannot be registered the previous hotkey stays and the page says so.
    private void SetShortcutHotkey(uint modifiers, uint key)
    {
        if (_shortcutPanelHotkey?.TrySet(modifiers, key) == true)
        {
            _shortcuts.HotkeyModifiers = modifiers;
            _shortcuts.HotkeyKey = key;
            _shortcuts.HotkeyEnabled = true;
            _shortcuts.HotkeyRegistered = true;
            _shortcuts.HotkeyRejected = false;
            _services.Configuration.SaveShell();
        }
        else
        {
            _shortcuts.HotkeyRejected = true;
        }
    }

    private async Task RefreshShortcutModsAsync()
    {
        if (!_shortcutsPrepared) return;
        string repository = _services.Repositories.Selected?.SourcePath ?? string.Empty;
        string? previous = _shortcuts.SelectedMod?.Path ?? _currentModPath();
        int revision = ++_shortcuts.ScanRevision;
        _shortcuts.InputEnabled = false;
        _shortcuts.ModsRead = false;
        _shortcuts.LoadedPath = string.Empty;
        _shortcuts.Detected = [];
        RenderShortcutButtons();
        _shortcuts.StatusText = T("ShortcutPanel.ReadingCurrentRepository");
        ShortcutModOption[] options = await Task.Run(() => ReadShortcutModOptions(repository));
        if (_services.IsClosing || revision != _shortcuts.ScanRevision) return;
        // A new list clears the choice, as replacing a ComboBox's items did; the choice below loads the bindings again.
        _shortcuts.ModOptions = options;
        _shortcuts.SelectedMod = null;
        _shortcuts.ModsRead = true;
        _ = RefreshDeployedModsAsync();
        LoadCharacterFollowProfile(repository);
        _shortcuts.SelectedMod = options.FirstOrDefault(p => string.Equals(p.Path, previous, StringComparison.OrdinalIgnoreCase)) ?? options.FirstOrDefault();
        if (options.Length == 0) _shortcuts.StatusText = T("ShortcutPanel.NoModsFoundSelectARepository");
    }

    public async Task RefreshSafelyAsync()
    {
        try { await RefreshShortcutModsAsync(); }
        catch (Exception ex) { if (!_services.IsClosing) _shortcuts.ReportError(ex.Message); }
    }

    private async Task LoadShortcutSelectionAsync(bool force = false)
    {
        int revision = ++_shortcuts.ScanRevision;
        // The path before the bindings: the page reads both when the bindings change.
        _shortcuts.LoadedPath = string.Empty;
        _shortcuts.Detected = [];
        RenderShortcutButtons();
        if (_shortcuts.SelectedMod is not ShortcutModOption mod) { RefreshShortcutOverlay(bindingsChanged: true); return; }
        try
        {
            _shortcuts.StatusText = T("ShortcutPanel.ReadingINI");
            var detected = await Task.Run(() => _shortcutScanCache.Scan(mod.Path, force));
            if (_services.IsClosing || revision != _shortcuts.ScanRevision) return;
            _shortcuts.LoadedPath = mod.Path;
            _shortcuts.Detected = detected;
            _shortcuts.StatusText = T("ShortcutPanel.ReadBindingsInGameConditionsAre", detected.Count);
            RenderShortcutButtons();
        }
        catch (Exception ex)
        {
            _log("Read shortcut panel INI", ex);
            if (!_services.IsClosing && revision == _shortcuts.ScanRevision) _shortcuts.ReportError(ex.Message);
        }
    }

    private void RefreshShortcutTargets()
    {
        ShortcutTarget? previous = _shortcuts.SelectedTarget;
        var targets = ShortcutInputService.ListTargets();
        _shortcuts.Targets = targets;
        ShortcutTarget? kept = targets.FirstOrDefault(t => t.Handle == previous?.Handle && t.StartTicks == previous.StartTicks);
        // Without a (still running) chosen window, pick the game window. Input stays off until the
        // user turns it on: selecting a target never arms it.
        int detected = kept is null
            ? GameWindowDetection.Find(targets.Select(t => new GameWindowCandidate(t.ProcessName, t.Label)).ToArray(),
                PreferredGameProcesses(_services.Repositories.Selected))
            : -1;
        _shortcuts.SelectedTarget = detected >= 0 ? targets[detected] : kept;
        if (detected >= 0)
            _shortcuts.StatusText = T("ShortcutPanel.GameWindowDetected", targets[detected].Label);
    }

    // The process names of the current repository's game, so its window wins over other running games.
    private string[] PreferredGameProcesses(WorkspaceRepository? repository)
    {
        if (repository is null) return [];
        if (_services.OnlineSource.IsEndfield(repository)) return [.. GameWindowDetection.KnownGames[0]];
        RepositoryPresetInfo? preset = BuiltInRepositories.Presets.FirstOrDefault(candidate => RepositoryPresetMatching.IsForPreset(repository, candidate));
        return preset?.Id switch
        {
            "genshin" => [.. GameWindowDetection.KnownGames[1]],
            "hsr" => [.. GameWindowDetection.KnownGames[2]],
            "zzz" => [.. GameWindowDetection.KnownGames[3]],
            _ => []
        };
    }

    // Selects the INI of the first binding in File Explorer; the panel itself never edits Mod files.
    private void ShowShortcutIniFile()
    {
        if (_shortcuts.Detected.Count == 0 || string.IsNullOrEmpty(_shortcuts.LoadedPath)) return;
        string sourceFile = _shortcuts.Detected[0].SourceFile;
        try
        {
            string? path = Directory.EnumerateFiles(_shortcuts.LoadedPath, sourceFile, new EnumerationOptions
            {
                RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint,
                MatchCasing = MatchCasing.CaseInsensitive
            }).OrderBy(file => file, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            _services.Dialogs.OpenFileLocation(path, sourceFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log("Show shortcut INI file", ex);
            _shortcuts.ReportError(ex.Message);
        }
    }

    // The page renders its own cards from the view model; the overlay follows here.
    private void RenderShortcutButtons() => _overlay.Refresh(bindingsChanged: true);

    private async Task SendShortcutFromPanelAsync(DetectedModShortcut shortcut)
    {
        if (_shortcuts.SendInProgress) return;
        _shortcuts.SendInProgress = true;
        try
        {
            if (!_shortcuts.InputEnabled || _shortcuts.SelectedTarget is not ShortcutTarget target)
                throw new InvalidOperationException(T("ShortcutPanel.SelectATargetWindowAndEnable"));
            if (!Directory.Exists(_shortcuts.LoadedPath) || Path.GetFileName(_shortcuts.LoadedPath).StartsWith("DISABLED", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(T("ShortcutPanel.TheModFolderChangedReloadIt"));
            if (!ShortcutKeyPlan.TryCreate(shortcut.RawKey, out var plan)) return;
            int hold = shortcut.Behavior.Equals("hold", StringComparison.OrdinalIgnoreCase)
                ? _shortcuts.HoldMilliseconds
                : _shortcuts.TapMilliseconds;
            string report = await _shortcutInput.SendAsync(target, plan!, hold, _shortcuts.InputMode, _services.Language.Current);
            if (!_services.IsClosing) _shortcuts.StatusText = shortcut.Shortcut + " · " + report;
        }
        catch (Exception ex)
        {
            if (!_services.IsClosing) _shortcuts.ReportError(ex.Message);
        }
        finally { _shortcuts.SendInProgress = false; }
        if (!_services.IsClosing) RefreshShortcutOverlay();
    }

    private static ShortcutModOption[] ReadShortcutModOptions(string root)
    {
        if (!Directory.Exists(root)) return [];
        var enumeration = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        bool Active(string path) => !Path.GetFileName(path).StartsWith("DISABLED", StringComparison.OrdinalIgnoreCase)
            && !Path.GetFileName(path).StartsWith(".", StringComparison.Ordinal);
        return Directory.EnumerateDirectories(root, "*", enumeration).Where(Active)
            .SelectMany(category => Directory.EnumerateDirectories(category, "*", enumeration).Where(Active))
            .Select(path => new ShortcutModOption(path, Path.GetRelativePath(root, path)))
            .Take(3000).OrderBy(option => option.Label, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    /// <summary>Reads again which Mods are enabled in the loader folder; a disabled or replaced Mod is followed to its replacement.</summary>
    public async Task RefreshDeployedModsAsync(string? preferredPath = null)
    {
        if (!_shortcutsPrepared) return;
        int revision = ++_shortcutDeployedRevision;
        WorkspaceRepository? repository = _services.Repositories.Selected;
        string source = repository?.SourcePath ?? string.Empty, target = repository?.TargetPath ?? string.Empty;
        HashSet<string> deployed = new(StringComparer.OrdinalIgnoreCase);
        ShortcutModOption[] fresh = [];
        try
        {
            (fresh, deployed) = await Task.Run(() => (ReadShortcutModOptions(source),
                string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target) ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    : ModCombinationDeploymentPolicy.Capture(source, target, _services.Install.OwnsCopy(repository!)).ToHashSet(StringComparer.OrdinalIgnoreCase)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            _log("Shortcut deployed Mods", ex); // Unknown deployment clears actions instead of exposing stale ones.
        }
        if (_services.IsClosing || revision != _shortcutDeployedRevision || repository != _services.Repositories.Selected
            || source != (repository?.SourcePath ?? string.Empty) || target != (repository?.TargetPath ?? string.Empty)) return;
        ShortcutModOption? current = _shortcuts.SelectedMod;
        // Only a Mod that was enabled and has just been disabled or replaced is followed. The main page also
        // lists Mods that were never enabled; picking one of those must not be undone by a refresh.
        bool wasDeployed = current is not null && _shortcutDeployedMods?.Contains(current.Label) == true;
        _shortcutDeployedMods = deployed;
        _overlay.InvalidateModOptions();
        var previous = _shortcuts.ModOptions as ShortcutModOption[] ?? [];
        // Retain record identity so unchanged lists and selections do not trigger a rescan.
        var byPath = previous.ToDictionary(option => option.Path, StringComparer.OrdinalIgnoreCase);
        var options = fresh.Select(option => byPath.GetValueOrDefault(option.Path) ?? option).ToArray();
        ShortcutModOption? chosen = current is null ? null : options.FirstOrDefault(option => option.Path == current.Path);
        if (current is not null && (chosen is null || (wasDeployed && !IsShortcutModDeployed(chosen))))
        {
            string? role = Path.GetDirectoryName(current.Path);
            chosen = options.FirstOrDefault(option => IsShortcutModDeployed(option)
                    && string.Equals(option.Path, preferredPath, StringComparison.OrdinalIgnoreCase))
                ?? options.FirstOrDefault(option => IsShortcutModDeployed(option)
                    && string.Equals(Path.GetDirectoryName(option.Path), role, StringComparison.OrdinalIgnoreCase));
        }
        _refreshingShortcutOptions = true;
        try
        {
            if (!previous.SequenceEqual(options))
            {
                // A new list clears the choice, as replacing a ComboBox's items did.
                _shortcuts.ModOptions = options;
                _shortcuts.SelectedMod = null;
            }
            _shortcuts.SelectedMod = chosen;
        }
        finally { _refreshingShortcutOptions = false; }
        if (!ReferenceEquals(current, chosen)) await LoadShortcutSelectionAsync();
        if (chosen is null && current is not null) _shortcuts.StatusText = T("ShortcutCapture.CurrentRemoved");
        RefreshShortcutOverlay(bindingsChanged: true);
    }

    private bool IsShortcutModDeployed(ShortcutModOption option) =>
        _shortcutDeployedMods?.Contains(option.Label) != false;

    private void SetShortcutInputFromOverlay(bool enable)
    {
        // Arming needs a target: look for the game window first; the overlay cannot pick other windows.
        if (enable && _shortcuts.SelectedTarget is not ShortcutTarget) RefreshShortcutTargets();
        if (enable && _shortcuts.SelectedTarget is not ShortcutTarget)
            _shortcuts.StatusText = T("ShortcutPanel.SelectTheTargetGameWindowIn");
        else
            _shortcuts.InputEnabled = enable;
        RefreshShortcutOverlay();
    }

    private void RefreshShortcutOverlay(bool bindingsChanged = false) => _overlay.Refresh(bindingsChanged);

    private async Task SendNumLockAsync()
    {
        if (_shortcuts.SendInProgress) return;
        _shortcuts.SendInProgress = true;
        try
        {
            if (!_shortcuts.InputEnabled || _shortcuts.SelectedTarget is not ShortcutTarget target)
                throw new InvalidOperationException(T("ShortcutPanel.SelectATargetWindowAndEnable"));
            _shortcuts.StatusText = await _shortcutInput.SendAsync(target, new ShortcutKeyPlan(new ushort[] { 0x90 }, Array.Empty<ushort>()), 150, ShortcutInputMode.VirtualKey, _services.Language.Current);
        }
        catch (Exception ex) { if (!_services.IsClosing) _shortcuts.ReportError(ex.Message); }
        finally { _shortcuts.SendInProgress = false; if (!_services.IsClosing) RefreshShortcutOverlay(); }
    }

    /// <summary>When the window closes: the hotkey, character follow, the input sender and the overlay.</summary>
    public void Shutdown()
    {
        ++_shortcuts.ScanRevision;
        _shortcutPanelHotkey?.Dispose();
        _characterFollowTimer?.Dispose();
        _shortcutInput.Dispose();
        _overlay.Close();
    }

    /// <summary>Registers the overlay's global hotkey once the settings are loaded.</summary>
    public void InitializeHotkey()
    {
        try
        {
            _shortcutPanelHotkey = new ShortcutPanelHotkey(_windowHandle,
                () => _dispatcher.TryEnqueue(_overlay.Toggle));
            if (_shortcuts.HotkeyEnabled)
                _shortcuts.HotkeyRegistered = _shortcutPanelHotkey.TrySet(_shortcuts.HotkeyModifiers, _shortcuts.HotkeyKey);
        }
        catch (Exception ex) { _log("Shortcut panel hotkey", ex); }
    }

    private void UpdateShortcutPrivilegeNotice()
    {
        ShortcutSecurity own = ShortcutProcessSecurity.Current;
        bool elevated = own.UiAccess || ShortcutPrivilegePolicy.IsElevated(own.Integrity);
        _shortcuts.IsElevated = elevated;
        _shortcuts.PrivilegeWarning = false;
        if (elevated)
        {
            _shortcuts.PrivilegeText = T("ShortcutPanel.TheManagerIsElevatedAndCan");
            return;
        }
        if (_shortcuts.SelectedTarget is not ShortcutTarget target)
        {
            // Only an Endfield repository gets the Endfield-specific explanation.
            _shortcuts.PrivilegeText = EndfieldBridgeService.CanUse(_services.Repositories.Selected)
                ? T("ShortcutPanel.EndfieldRunsAsAdministratorInputFrom")
                : T("ShortcutPanel.ElevatedGameNeedsElevatedManager");
            return;
        }
        ShortcutSecurity destination = ShortcutProcessSecurity.Read(target.ProcessId);
        bool allowed = ShortcutPrivilegePolicy.Evaluate(own.Integrity, own.UiAccess, destination.Integrity, destination.Error) == ShortcutPrivilegeVerdict.Allowed;
        _shortcuts.PrivilegeWarning = !allowed;
        _shortcuts.PrivilegeText = allowed
            ? T("ShortcutPanel.TheSelectedWindowRunsAtThe")
            : T("ShortcutPanel.TheSelectedWindowRunsAsAdministrator");
    }

    private void RestartManagerElevated()
    {
        try
        {
            // The new process waits for this one to exit before taking the single-instance key.
            Process.Start(new ProcessStartInfo(EndfieldBridgeService.LaunchExecutablePath, $"{Program.RestartAfterArgument} {Environment.ProcessId}")
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory
            })?.Dispose();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            _shortcuts.ReportError(T("ShortcutPanel.AdministratorApprovalWasDeclinedTheManager"));
            return;
        }
        catch (Exception ex) { _reportFailure(nameof(RestartManagerElevated), ex); return; }
        _closeWindow();
    }
}
