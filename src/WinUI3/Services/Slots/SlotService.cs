using IntegratedModManager.Core.Deployment;
using IntegratedModManager.Core.Presets;
using IntegratedModManager.Core.Repository;
using IntegratedModManager.Core.Shortcuts;

namespace ModFolderCopier.WinUI;

/// <summary>
/// State slots (stage J finish, step 6, moved from the window unchanged): the saved slots of each Mod and their Default
/// slots, the relative paths and d3dx_user.ini they use, switching a slot from the overlay with the loader's reload key
/// (F10, sent through the window's shortcut input to the chosen game window only), and the loader's auto-save
/// interval. The workspace's slot card and the overlay show them; the window still owns their controls and dialogs.
/// </summary>
internal sealed partial class SlotService(AppServices services)
{
    private readonly AppServices _services = services;
    private Action<string, Exception> _log = (_, _) => { };
    private Action<string> _trace = _ => { };
    private Action _saveShellConfig = () => { };
    private Action _slotsChanged = () => { };
    private Action<string> _showStatus = _ => { };
    private ShortcutInputService _shortcutInput = null!;
    private Func<ShortcutOverlayController> _overlayAccess = () => null!;
    private Func<bool> _savingOverlayPreset = () => false;
    private Func<bool> _applyingCombination = () => false;

    /// <summary>
    /// Called once by the window: the error log and startup trace, saving beta-shell.json, redrawing the slot card,
    /// the workspace status line, its shortcut input and overlay, and whether an overlay save or a combination restore
    /// is running (both still in the window).
    /// </summary>
    public void Attach(Action<string, Exception> log, Action<string> trace, Action saveShellConfig, Action slotsChanged, Action<string> showStatus,
        ShortcutInputService shortcutInput, Func<ShortcutOverlayController> overlay, Func<bool> savingOverlayPreset, Func<bool> applyingCombination)
    {
        _log = log;
        _trace = trace;
        _saveShellConfig = saveShellConfig;
        _slotsChanged = slotsChanged;
        _showStatus = showStatus;
        _shortcutInput = shortcutInput;
        _overlayAccess = overlay;
        _savingOverlayPreset = savingOverlayPreset;
        _applyingCombination = applyingCombination;
    }

    /// <summary>Every state slot, of every repository and Mod (saved in beta-shell.json).</summary>
    public List<ModPersistentSlot> Items { get; } = [];

    /// <summary>Repository id -> Mods (relative to the repository) already offered a Default slot.</summary>
    public Dictionary<string, HashSet<string>> DefaultSeeds { get; } = new(StringComparer.Ordinal);

    /// <summary>Repositories whose Mods have all been offered a Default slot, including Mods seen before Default slots existed.</summary>
    public HashSet<string> DefaultsBackfilled { get; } = new(StringComparer.Ordinal);

    /// <summary>The slot chosen on the workspace's slot card (the overlay selects a slot it just saved).</summary>
    public string? SelectedId { get; set; }

    /// <summary>A slot is being written to the loader (from the workspace or the overlay).</summary>
    public bool IsRestoring { get; set; }

    /// <summary>The loader auto-save intervals the user changed here, with their original values (saved in beta-shell.json).</summary>
    public Dictionary<string, int> LoaderAutoSaveOriginals { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A character of the repository was renamed (#108): its slots and Default slot seeds name the new folder.</summary>
    public void RenameCharacter(WorkspaceRepository repository, CharacterRenamePlan plan, ReferenceUndoLog log)
    {
        foreach (ModPersistentSlot slot in Items.Where(slot => slot.RepositoryId == repository.Id))
            log.Rewrite(() => slot.ModRelativePath, value => slot.ModRelativePath = value, plan.MapRelative);
        if (DefaultSeeds.TryGetValue(repository.Id, out HashSet<string>? seeds)) log.RewriteAll(seeds, plan.MapRelative);
    }

    private ShortcutPageViewModel _shortcuts => _services.Shortcuts;
    private ShortcutOverlayController _overlay => _overlayAccess();
    private bool _savingOverlaySinglePreset => _savingOverlayPreset();
    private bool _applyingCombinationProfile => _applyingCombination();

    private string T(string key, params object?[] values) => _services.Language.T(key, values);

    private WorkspaceRepository? GetSelectedRepository() => _services.Repositories.Selected;

    private void SaveShellConfig() => _saveShellConfig();

    private void LogApplicationIssue(string operation, Exception exception) => _log(operation, exception);

    private void TraceStartupStage(string stage) => _trace(stage);

    private void RefreshShortcutOverlay(bool bindingsChanged = false) => _services.Overlay.Refresh(bindingsChanged);

    /// <summary>The slot card shows the slots again (the window draws it).</summary>
    private void RefreshPersistentSlotControls() => _slotsChanged();

    private Task<bool> ShowConfirmAsync(string content, string title, string primaryText) => _services.Dialogs.ConfirmAsync(title, content, primaryText);

    // Repository id -> Mods (relative to the repository) already offered a default slot.
    // Repositories whose Mods have all been offered a default slot, including Mods seen before default slots existed.

    // Before the first preset of a Mod is saved. A scan normally got there first; this covers a Mod saved before it.
    internal async Task EnsureDefaultSlotAsync(WorkspaceRepository repository, string modPath, string relative)
    {
        bool known = DefaultSeeds.TryGetValue(repository.Id, out HashSet<string>? seen) && seen.Contains(relative);
        if (DefaultSlotPolicy.ModsNeedingDefault([relative], known ? [relative] : [], DefaultsBackfilled.Contains(repository.Id),
                DefaultSlotMods(repository)).Count == 0) return;
        try
        {
            IReadOnlyList<ModPersistentValue> values = await Task.Run(() =>
                ModPersistentPresetEngine.DeclaredDefaults(modPath, ModDeploymentLayout.DeployedName(modPath)));
            AddDefaultSlot(repository, relative, values);
            (DefaultSeeds.TryGetValue(repository.Id, out HashSet<string>? seeds) ? seeds
                : DefaultSeeds[repository.Id] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)).Add(relative);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            LogApplicationIssue("Read Mod defaults for a default slot", ex);
        }
    }

    internal IEnumerable<string> DefaultSlotMods(WorkspaceRepository repository) => Items
        .Where(slot => slot.IsModDefault && slot.RepositoryId == repository.Id).Select(slot => slot.ModRelativePath);

    internal bool AddDefaultSlot(WorkspaceRepository repository, string relative, IReadOnlyList<ModPersistentValue> values)
    {
        if (values.Count == 0 || DefaultSlotMods(repository).Contains(relative, StringComparer.OrdinalIgnoreCase)) return false;
        Items.Add(new ModPersistentSlot
        {
            RepositoryId = repository.Id,
            ModRelativePath = relative,
            Name = T("PersistentSlots.DefaultSlotName"),
            IsModDefault = true,
            Values = [.. values]
        });
        return true;
    }

    // A default slot restores what the Mod declares now, so it follows a Mod update that changed its defaults.
    // Falls back to the stored values when the Mod cannot be read or declares nothing any more.
    internal IReadOnlyList<ModPersistentValue> ValuesToRestore(ModPersistentSlot slot, string modPath)
    {
        if (!slot.IsModDefault) return slot.Values;
        try
        {
            IReadOnlyList<ModPersistentValue> current = ModPersistentPresetEngine.DeclaredDefaults(modPath, ModDeploymentLayout.DeployedName(modPath));
            return current.Count > 0 ? current : slot.Values;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            LogApplicationIssue("Read current Mod defaults", ex);
            return slot.Values;
        }
    }

    internal async Task SeedDefaultSlotsAsync(WorkspaceRepository repository, IReadOnlyList<FirstLevelFolderItem> characters)
    {
        try
        {
            string[] modPaths = characters.SelectMany(character => character.Children).Select(mod => mod.Path).ToArray();
            string[]? known = DefaultSeeds.TryGetValue(repository.Id, out HashSet<string>? seen) ? [.. seen] : null;
            bool backfilled = DefaultsBackfilled.Contains(repository.Id);
            string[] withDefault = [.. DefaultSlotMods(repository)];
            var found = await Task.Run(() =>
            {
                var mods = modPaths.Select(path => (Path: path, Relative: TryGetCurrentModRelativePath(repository, path)))
                    .Where(mod => mod.Relative is not null).ToArray();
                IReadOnlyList<string> fresh = DefaultSlotPolicy.ModsNeedingDefault(mods.Select(mod => mod.Relative!), known, backfilled, withDefault);
                var defaults = new List<(string Relative, IReadOnlyList<ModPersistentValue> Values)>();
                var unreadable = new List<string>();
                foreach (var mod in mods.Where(mod => fresh.Contains(mod.Relative!, StringComparer.OrdinalIgnoreCase)))
                {
                    try
                    {
                        var values = ModPersistentPresetEngine.DeclaredDefaults(mod.Path, ModDeploymentLayout.DeployedName(mod.Path));
                        if (values.Count > 0) defaults.Add((mod.Relative!, values));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
                    {
                        LogApplicationIssue("Read Mod defaults for a default slot", ex);
                        unreadable.Add(mod.Relative!);
                    }
                }
                return (All: DefaultSlotPolicy.Seen(mods.Select(mod => mod.Relative!), unreadable), Defaults: defaults);
            });
            if (_services.IsClosing || !ReferenceEquals(GetSelectedRepository(), repository)) return;

            bool firstScan = !DefaultSeeds.TryGetValue(repository.Id, out HashSet<string>? seeds) || DefaultsBackfilled.Add(repository.Id);
            seeds ??= DefaultSeeds[repository.Id] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int before = seeds.Count;
            seeds.UnionWith(found.All);
            int created = 0;
            DefaultsBackfilled.Add(repository.Id);
            foreach ((string relative, IReadOnlyList<ModPersistentValue> values) in found.Defaults)
            {
                if (AddDefaultSlot(repository, relative, values)) created++;
            }
            if (firstScan || seeds.Count != before || created > 0) SaveShellConfig();
            if (created > 0) RefreshPersistentSlotControls();
        }
        catch (Exception ex)
        {
            LogApplicationIssue("Create default slots", ex);
        }
    }

    // Called on a language change: untouched default slots take the new language's name.
    internal void RelabelDefaultSlots()
    {
        string[] names = UiText.Languages.Select(language => UiText.Get(language, "PersistentSlots.DefaultSlotName")).Distinct().ToArray();
        string wanted = T("PersistentSlots.DefaultSlotName");
        bool changed = false;
        foreach (ModPersistentSlot slot in Items.Where(slot => slot.IsModDefault))
        {
            var others = Items.Where(other => other != slot && other.RepositoryId == slot.RepositoryId
                && string.Equals(other.ModRelativePath, slot.ModRelativePath, StringComparison.OrdinalIgnoreCase)).Select(other => other.Name);
            if (DefaultSlotPolicy.Relabel(slot.Name, names, wanted, others) is { } name)
            {
                slot.Name = name;
                changed = true;
            }
        }
        if (changed) SaveShellConfig();
    }

    internal const ushort LoaderReloadKey = 0x79; // F10, the 3DMigoto/XXMI default reload_config key.
    internal bool CanHotReloadLoader => _shortcuts.InputEnabled && _shortcuts.SelectedTarget is ShortcutTarget;

    // Sends F10 with d3dx_user.ini kept read-only and describes the outcome.
    internal async Task<string> HotReloadLoaderAsync(string userIni, Action? keySent = null)
    {
        if (!CanHotReloadLoader || _shortcuts.SelectedTarget is not ShortcutTarget target)
        {
            return T("HotReload.NotArmed");
        }

        var plan = new ShortcutKeyPlan([LoaderReloadKey], []);
        LoaderHotReloadResult result = await LoaderHotReload.ReloadAsync(userIni,
            async _ =>
            {
                await _shortcutInput.SendAsync(target, plan, 120, _shortcuts.InputMode, _services.Language.Current);
                keySent?.Invoke();
            },
            LoaderHotReload.DefaultHold);
        return result switch
        {
            { ReloadSent: false } => T("HotReload.KeyNotSent", result.SendError ?? string.Empty),
            { FileKept: false } => T("HotReload.FileOverwritten"),
            _ => T("HotReload.ReloadSent")
        };
    }

    // The checkbox restore dialogs show next to "I have fully closed the game".
    internal ModPersistentSlot[] OverlaySlots(out string? modPath)
    {
        WorkspaceRepository? repository = GetSelectedRepository();
        modPath = _shortcuts.SelectedMod?.Path;
        string? relative = TryGetCurrentModRelativePath(repository, modPath);
        return !_shortcuts.OverlaySlotSwitchEnabled || repository is null || relative is null ? [] : Items
            .Where(slot => slot.RepositoryId == repository.Id && string.Equals(slot.ModRelativePath, relative, StringComparison.OrdinalIgnoreCase))
            .OrderBy(slot => slot.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    // In game, without dialogs: write the slot's keys into d3dx_user.ini, then reload with F10.
    internal async Task SwitchSlotFromOverlayAsync(string slotId, string modPath)
    {
        if (_savingOverlaySinglePreset || _applyingCombinationProfile) return;
        if (IsRestoring || _shortcuts.SendInProgress)
        {
            _shortcuts.StatusText = T("HotReload.StillApplying");
            RefreshShortcutOverlay(bindingsChanged: true);
            return;
        }
        WorkspaceRepository? repository = GetSelectedRepository();
        ModPersistentSlot? slot = Items.FirstOrDefault(item => item.Id == slotId);
        string? userIni = repository?.PersistentUserIniPath is { Length: > 0 } configured ? configured : TryGetDefaultPersistentIniPath(repository);
        if (repository is null || slot is null || userIni is null || !File.Exists(userIni))
        {
            _shortcuts.StatusText = T("HotReload.SetTheUserIniFirst");
            RefreshShortcutOverlay(bindingsChanged: true);
            return;
        }
        if (!CanHotReloadLoader)
        {
            _shortcuts.StatusText = T("HotReload.NotArmed");
            RefreshShortcutOverlay(bindingsChanged: true);
            return;
        }

        IsRestoring = true;
        _shortcuts.SendInProgress = true;
        try
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            string deployedName = ModDeploymentLayout.DeployedName(modPath);
            if (string.IsNullOrWhiteSpace(repository.TargetPath) || !Directory.Exists(ModDeploymentLayout.DeployedPath(repository.TargetPath, modPath)))
                throw new InvalidDataException(T("PersistentSlots.DeployThisModUnderItsMatching"));
            IReadOnlyList<ModPersistentValue> values = await Task.Run(() => ValuesToRestore(slot, modPath));
            ModPersistentRuntimeRestorePlan plan = await Task.Run(() => ModPersistentPresetEngine.PlanRuntimeRestore(modPath, deployedName, userIni, values));
            slot.Values = [.. values];
            await Task.Run(() => ModPersistentPresetEngine.ApplyRuntimeRestore(plan));
            slot.ActivatedAtUtc = DateTimeOffset.UtcNow;
            SaveShellConfig();
            long written = clock.ElapsedMilliseconds;
            // The file stays read-only for a few seconds after the key, so report the switch as soon as the key is out.
            _shortcuts.StatusText = $"{slot.Name} · " + await HotReloadLoaderAsync(userIni, () =>
            {
                TraceStartupStage($"Slot switch: values written {written} ms, reload key sent {clock.ElapsedMilliseconds} ms");
                _shortcuts.StatusText = $"{slot.Name} · " + T("HotReload.Applying");
                _overlay.InvalidateSlots();
                RefreshShortcutOverlay(bindingsChanged: true);
            });
        }
        catch (Exception ex)
        {
            LogApplicationIssue("Overlay slot switch", ex);
            _shortcuts.ReportError(T("HotReload.SwitchFailed", slot.Name) + ex.Message);
        }
        finally
        {
            IsRestoring = false;
            _shortcuts.SendInProgress = false;
            _overlay.InvalidateSlots();
            if (!_services.IsClosing)
            {
                RefreshShortcutOverlay(bindingsChanged: true);
                RefreshPersistentSlotControls();
            }
        }
    }


    // The portable prototype ships synthetic fixtures, never real user configuration.
    // Resolve paths at first run so moving/extracting the package remains safe.
    internal static WorkspaceRepository? TryCreatePersistentDemoRepository()
    {
        string demoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "TestData"));
        string source = Path.Combine(demoRoot, "ModRepository");
        string target = Path.Combine(demoRoot, "Loader", "Mods");
        string userIni = Path.Combine(demoRoot, "Loader", "d3dx_user.ini");
        if (!Directory.Exists(source) || !Directory.Exists(target) || !File.Exists(userIni)) return null;
        return new WorkspaceRepository
        {
            Id = "state-slots-offline-demo",
            Name = "状态槽位离线测试 / Offline Demo",
            SourcePath = source,
            TargetPath = target,
            PersistentUserIniPath = userIni,
            UseDirectoryLinks = false
        };
    }

    internal static string? TryGetCurrentModRelativePath(WorkspaceRepository? repository, string? modPath)
    {
        if (repository is null || string.IsNullOrWhiteSpace(modPath)
            || string.IsNullOrWhiteSpace(repository.SourcePath)
            || !Directory.Exists(modPath) || !Directory.Exists(repository.SourcePath)) return null;

        try
        {
            string full = Path.GetFullPath(modPath);
            string sourceRoot = Path.GetFullPath(repository.SourcePath);
            if (!OnlineDownloadService.IsPathInsideDirectory(full, sourceRoot)) return null;
            string relative = Path.GetRelativePath(sourceRoot, full);
            return relative is "." or ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                ? null : relative;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    internal static string? TryGetDefaultPersistentIniPath(WorkspaceRepository? repository)
    {
        if (string.IsNullOrWhiteSpace(repository?.TargetPath)) return null;
        try
        {
            string? parent = Path.GetDirectoryName(Path.GetFullPath(repository.TargetPath));
            if (parent is null) return null;
            string candidate = Path.Combine(parent, "d3dx_user.ini");
            return File.Exists(candidate) ? candidate : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    internal static ModPersistentSlot? CurrentPersistentSlot(IEnumerable<ModPersistentSlot> slots)
        => slots.Where(slot => slot.ActivatedAtUtc is not null).MaxBy(slot => slot.ActivatedAtUtc);


    // The overlay cannot ask: it leaves menu state out and says how many.
    internal (IReadOnlyList<ModPersistentValue> Values, int Left) FilterSlotValues(IReadOnlyList<ModPersistentValue> values)
    {
        if (_shortcuts.KeepMenuStateVariables) return (values, 0);
        var (kept, transient) = TransientVariablePolicy.Split(values);
        return (kept, transient.Count);
    }

    internal string? LoaderSettingsPath(WorkspaceRepository? repository)
    {
        string? userIni = repository?.PersistentUserIniPath is { Length: > 0 } configured ? configured : TryGetDefaultPersistentIniPath(repository);
        string? folder = string.IsNullOrWhiteSpace(userIni) ? null : Path.GetDirectoryName(userIni);
        return folder is null ? null : Path.Combine(folder, "d3dx.ini");
    }

    // The loader's auto-save interval in its d3dx.ini, shown on the Shortcuts page with the intervals to choose.
    internal void RefreshLoaderAutoSave()
    {
        string? path = LoaderSettingsPath(GetSelectedRepository());
        int? current = null;
        try { current = path is not null && File.Exists(path) ? LoaderAutoSaveSetting.Read(File.ReadAllText(path)) : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { LogApplicationIssue("Read loader auto-save interval", ex); }
        _shortcuts.LoaderAutoSaveNote = current is null ? T("SlotCapture.AutoSaveUnavailable") : T("SlotCapture.AutoSaveNote", path!);
        if (current is null)
        {
            _shortcuts.LoaderAutoSaveChoices = [];
            _shortcuts.LoaderAutoSaveSeconds = null;
            return;
        }
        var choices = LoaderAutoSaveSetting.Choices.Append(current.Value).Distinct().Order()
            .Select(seconds => new LoaderAutoSaveChoice(seconds, T("SlotCapture.Seconds", seconds))).ToList();
        if (LoaderAutoSaveOriginals.TryGetValue(path!, out int original) && original != current)
            choices.Add(new LoaderAutoSaveChoice(original, T("SlotCapture.RestoreOriginal", original)));
        _shortcuts.LoaderAutoSaveChoices = choices;
        _shortcuts.LoaderAutoSaveSeconds = current;
    }

    // Writes the chosen interval into d3dx.ini after the user confirms, keeping a backup and the original value.
    internal async Task SetLoaderAutoSaveAsync(int seconds)
    {
        string? path = LoaderSettingsPath(GetSelectedRepository());
        if (path is null || !File.Exists(path)) { RefreshLoaderAutoSave(); return; }
        string text = await File.ReadAllTextAsync(path);
        int? current = LoaderAutoSaveSetting.Read(text);
        if (current == seconds) return;
        if (current is null || LoaderAutoSaveSetting.Write(text, seconds) is not { } updated
            || !await ShowConfirmAsync(T("SlotCapture.ConfirmAutoSave", path, current, seconds), T("SlotCapture.AutoSaveInterval"), T("Dialogs.Change")))
        {
            RefreshLoaderAutoSave();
            return;
        }
        string backup = $"{path}.imm-autosave-{DateTime.Now:yyyyMMdd-HHmmss}.bak";
        File.Copy(path, backup);
        await File.WriteAllTextAsync(path, updated, new System.Text.UTF8Encoding(false));
        LoaderAutoSaveOriginals.TryAdd(path, current.Value);
        if (LoaderAutoSaveOriginals[path] == seconds) LoaderAutoSaveOriginals.Remove(path);
        SaveShellConfig();
        _showStatus(T("SlotCapture.AutoSaveChanged", seconds, backup));
        RefreshLoaderAutoSave();
    }
}
