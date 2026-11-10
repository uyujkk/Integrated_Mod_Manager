using System.ComponentModel;
using System.Diagnostics;
using IntegratedModManager.Core.Deployment;
using IntegratedModManager.Core.Shortcuts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private readonly ShortcutInputService _shortcutInput = new();
    /// <summary>The Shortcuts page and overlay state, shared with ShortcutsPage (stage J5).</summary>
    private ShortcutPageViewModel _shortcuts => _services.Shortcuts;
    private readonly ModShortcutScanCache _shortcutScanCache = new();
    // The overlay: its window, content and look (stage J5b); this window answers its requests (IShortcutOverlayActions).
    private ShortcutOverlayController _overlay = null!;
    private ShortcutsPage? _shortcutsPage;
    // The Mod list and windows are read on first use: the page shown or the overlay opened.
    private bool _shortcutsPrepared;
    private ShortcutPanelHotkey? _shortcutPanelHotkey;
    private bool _refreshingShortcutOptions;
    // Repository-relative paths of Mods enabled in the loader folder; null when it cannot be determined.
    private HashSet<string>? _shortcutDeployedMods;
    private int _shortcutDeployedRevision;

    private void OnShortcutsNavClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            _currentPrimarySection = PrimarySection.Shortcuts;
            ApplyShellState(refreshRepository: false);
        }
        catch (Exception ex) { ReportUiActionFailure(nameof(OnShortcutsNavClicked), ex); }
    }

    private bool IsEnglishUi => !IsChineseUi;

    /// <summary>The page relabels itself by rebuilding from the view model; the overlay is rebuilt here, and the Mods are read again.</summary>
    private void RebuildShortcutPaneForLanguage()
    {
        if (!_shortcutsPrepared) return;
        ++_shortcuts.ScanRevision;
        _overlay.RebuildForLanguage();
        _shortcutsPage?.RebuildForLanguage();
        // Input stays disarmed after the rebuild; the user re-enables it deliberately.
        _shortcuts.InputEnabled = false;
        UpdateShortcutPrivilegeNotice();
        RefreshLoaderAutoSave();
        _ = RefreshShortcutPaneSafelyAsync();
    }

    private Button ShortcutButton(string label, Action action)
    {
        var button = new Button { Content = label };
        button.Click += (_, _) => action();
        return button;
    }

    /// <summary>Reads the windows once, when the page is first shown or the overlay first opened.</summary>
    private void PrepareShortcuts()
    {
        if (_shortcutsPrepared) return;
        _shortcutsPrepared = true;
        StartCharacterFollow();
        UpdateShortcutPrivilegeNotice();
        RefreshLoaderAutoSave();
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
                _ = RunUiActionAsync("Select shortcut Mod", () => LoadShortcutSelectionAsync());
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
                SaveShellConfig();
                RenderShortcutButtons();
                break;
            case nameof(ShortcutPageViewModel.InputMode):
                SaveShellConfig();
                break;
            // The overlay applies its own look (ShortcutOverlayController); the settings are saved here.
            case nameof(ShortcutPageViewModel.OverlayOpacity):
            case nameof(ShortcutPageViewModel.OverlayElementOpacity):
            case nameof(ShortcutPageViewModel.OverlayCompactView):
            case nameof(ShortcutPageViewModel.OverlayAutoContrast):
            case nameof(ShortcutPageViewModel.OverlaySlotSwitchEnabled):
            case nameof(ShortcutPageViewModel.OverlayCompactSlots):
            case nameof(ShortcutPageViewModel.CapturePresetPreviews):
            case nameof(ShortcutPageViewModel.KeepMenuStateVariables):
                SaveShellConfig();
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
                _ = RunUiActionAsync("Send shortcut", () => SendShortcutFromPanelAsync(shortcut));
                break;
            case ShortcutPageAction.SendNumLock: _ = SendNumLockAsync(); break;
            case ShortcutPageAction.RestartElevated: RestartManagerElevated(); break;
            case ShortcutPageAction.SetHotkey when request.Argument is (uint modifiers, uint key): SetShortcutHotkey(modifiers, key); break;
            case ShortcutPageAction.OpenDashboard: OnDashboardNavClicked(this, new RoutedEventArgs()); break;
            // The text colour is saved when its picker closes, not for every colour the picker passes through.
            case ShortcutPageAction.SaveSettings: SaveShellConfig(); break;
            case ShortcutPageAction.EnablePresetPreviews: _ = RunUiActionAsync("Enable preset pictures", EnablePresetPreviewsAsync); break;
            case ShortcutPageAction.ClearPresetPreviewRegion: ClearPresetPreviewRegion(); break;
            case ShortcutPageAction.SelectCharacterRegion: _ = SelectCharacterRegionAsync(); break;
            case ShortcutPageAction.SaveCharacterReference: _ = SaveCharacterReferenceAsync(); break;
            case ShortcutPageAction.RefreshLoaderAutoSave: RefreshLoaderAutoSave(); break;
            case ShortcutPageAction.SetLoaderAutoSave when request.Argument is int seconds:
                _ = RunUiActionAsync("Change loader auto-save interval", () => SetLoaderAutoSaveAsync(seconds));
                break;
            case ShortcutPageAction.DisableHotkey:
                _shortcutPanelHotkey?.Disable();
                _shortcuts.HotkeyEnabled = false;
                _shortcuts.HotkeyRegistered = false;
                _shortcuts.HotkeyRejected = false;
                SaveShellConfig();
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
            SaveShellConfig();
        }
        else
        {
            _shortcuts.HotkeyRejected = true;
        }
    }

    private async Task RefreshShortcutModsAsync()
    {
        if (!_shortcutsPrepared) return;
        string repository = GetSelectedRepository()?.SourcePath ?? string.Empty;
        string? previous = _shortcuts.SelectedMod?.Path ?? _currentSecondLevelPath;
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
        _ = RefreshShortcutDeployedModsAsync();
        LoadCharacterFollowProfile(repository);
        _shortcuts.SelectedMod = options.FirstOrDefault(p => string.Equals(p.Path, previous, StringComparison.OrdinalIgnoreCase)) ?? options.FirstOrDefault();
        if (options.Length == 0) _shortcuts.StatusText = T("ShortcutPanel.NoModsFoundSelectARepository");
    }

    private async Task RefreshShortcutPaneSafelyAsync()
    {
        try { await RefreshShortcutModsAsync(); }
        catch (Exception ex) { if (!_services.IsClosing) _shortcuts.StatusText = ex.Message; }
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
            LogApplicationIssue("Read shortcut panel INI", ex);
            if (!_services.IsClosing && revision == _shortcuts.ScanRevision) _shortcuts.StatusText = ex.Message;
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
                PreferredGameProcesses(GetSelectedRepository()))
            : -1;
        _shortcuts.SelectedTarget = detected >= 0 ? targets[detected] : kept;
        if (detected >= 0)
            _shortcuts.StatusText = T("ShortcutPanel.GameWindowDetected", targets[detected].Label);
    }

    // The process names of the current repository's game, so its window wins over other running games.
    private string[] PreferredGameProcesses(WorkspaceRepository? repository)
    {
        if (repository is null) return [];
        if (_onlineView.IsEndfieldRepository(repository)) return [.. GameWindowDetection.KnownGames[0]];
        RepositoryPresetInfo? preset = BuiltInRepositoryPresets.FirstOrDefault(candidate => IsRepositoryForPreset(repository, candidate));
        return preset?.Id switch
        {
            "genshin" => [.. GameWindowDetection.KnownGames[1]],
            "hsr" => [.. GameWindowDetection.KnownGames[2]],
            "zzz" => [.. GameWindowDetection.KnownGames[3]],
            _ => []
        };
    }

    private DetectedModShortcut[] VisibleShortcuts() => _shortcuts.VisibleShortcuts();

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
            OpenFileLocation(path, sourceFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogApplicationIssue("Show shortcut INI file", ex);
            _shortcuts.StatusText = ex.Message;
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
            if (!_services.IsClosing) _shortcuts.StatusText = ex.Message;
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

    private async Task RefreshShortcutDeployedModsAsync(string? preferredPath = null)
    {
        if (!_shortcutsPrepared) return;
        int revision = ++_shortcutDeployedRevision;
        WorkspaceRepository? repository = GetSelectedRepository();
        string source = repository?.SourcePath ?? string.Empty, target = repository?.TargetPath ?? string.Empty;
        HashSet<string> deployed = new(StringComparer.OrdinalIgnoreCase);
        ShortcutModOption[] fresh = [];
        try
        {
            (fresh, deployed) = await Task.Run(() => (ReadShortcutModOptions(source),
                string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target) ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    : ModCombinationDeploymentPolicy.Capture(source, target, OwnsCopy(repository!)).ToHashSet(StringComparer.OrdinalIgnoreCase)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            LogApplicationIssue("Shortcut deployed Mods", ex); // Unknown deployment clears actions instead of exposing stale ones.
        }
        if (_services.IsClosing || revision != _shortcutDeployedRevision || repository != GetSelectedRepository()
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

    /// <summary>Flush loader memory before capturing; offer an explicit disk-only fallback when input is unavailable.</summary>
    private async Task QuickSaveCombinationFromOverlayAsync()
    {
        if (_savingOverlaySinglePreset || _applyingCombinationProfile || (_shortcuts.SendInProgress && !_restoringPersistentRuntime)) return;
        if (GetSelectedRepository() is not { } repository)
        {
            _shortcuts.StatusText = T("ShortcutPanel.SelectARepositoryInTheMain");
            RefreshShortcutOverlay();
            return;
        }
        _savingOverlaySinglePreset = true;
        if (!await WaitForOverlayCaptureAsync()) { _savingOverlaySinglePreset = false; return; }
        string? userIni = repository.PersistentUserIniPath is { Length: > 0 } configured ? configured : TryGetDefaultPersistentIniPath(repository);
        bool includeState = userIni is not null && File.Exists(userIni);
        string name = T("ShortcutPanel.InGame", DateTime.Now);
        _shortcuts.StatusText = T("ShortcutPanel.SavingCombinationPreset");
        RefreshShortcutOverlay();
        SetCombinationBusy(true);
        try
        {
            CharacterFrame? preview = await CapturePresetPreviewAsync();
            // Without a d3dx_user.ini there is no loader state to flush: save the Mod combination alone, as before.
            if (includeState && !await PrepareOverlayCaptureAsync(userIni!)) return;
            (int Mods, int Parameters)? saved = await CaptureCombinationProfileAsync(repository, name, null, includeState, userIni ?? string.Empty,
                () => Task.FromResult(false));
            string previewNote = string.Empty;
            if (saved is not null && _configurationProfiles.FirstOrDefault(item => item.Id == _selectedConfigurationProfileId) is { } profile)
            {
                profile.PreviewFile = await StorePresetPreviewAsync(repository.Id, profile.Id, preview);
                if (profile.PreviewFile is not null) SaveShellConfig();
                previewNote = PresetPreviewNote(profile.PreviewFile);
            }
            _shortcuts.StatusText = saved is (int mods, int parameters)
                ? T("ShortcutPanel.SavedPresetModsParameters", name, mods, parameters) + previewNote
                : T("ShortcutPanel.NoEnabledModsThePresetWas");
        }
        catch (Exception ex)
        {
            LogApplicationIssue("Overlay combination capture", ex);
            if (!_services.IsClosing) _shortcuts.StatusText = T("ShortcutPanel.PresetNotSaved") + OverlayCaptureError(ex);
        }
        finally
        {
            _savingOverlaySinglePreset = false;
            if (!_services.IsClosing)
            {
                SetCombinationBusy(false);
                RefreshShortcutOverlay(bindingsChanged: true);
            }
        }
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
        catch (Exception ex) { if (!_services.IsClosing) _shortcuts.StatusText = ex.Message; }
        finally { _shortcuts.SendInProgress = false; if (!_services.IsClosing) RefreshShortcutOverlay(); }
    }

    private void CloseShortcutPanel()
    {
        ++_shortcuts.ScanRevision;
        _shortcutPanelHotkey?.Dispose();
        _characterFollowTimer?.Dispose();
        _shortcutInput.Dispose();
        _overlay.Close();
    }

    private void InitializeShortcutPanelHotkey()
    {
        try
        {
            _shortcutPanelHotkey = new ShortcutPanelHotkey(WinRT.Interop.WindowNative.GetWindowHandle(this),
                () => DispatcherQueue.TryEnqueue(_overlay.Toggle));
            if (_shortcuts.HotkeyEnabled)
                _shortcuts.HotkeyRegistered = _shortcutPanelHotkey.TrySet(_shortcuts.HotkeyModifiers, _shortcuts.HotkeyKey);
        }
        catch (Exception ex) { LogApplicationIssue("Shortcut panel hotkey", ex); }
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
            _shortcuts.PrivilegeText = EndfieldBridgeService.CanUse(GetSelectedRepository())
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
            _shortcuts.StatusText = T("ShortcutPanel.AdministratorApprovalWasDeclinedTheManager");
            return;
        }
        catch (Exception ex) { ReportUiActionFailure(nameof(RestartManagerElevated), ex); return; }
        Close();
    }
}
