using IntegratedModManager.Core.Deployment;
using IntegratedModManager.Core.Presets;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using System.Text;

namespace ModFolderCopier.WinUI;

// The state slot card of the Mod workspace (stage J finish, step 6: moved from the window unchanged): the selected
// Mod's slots, saving, restoring, deleting and the "More" actions (variants and d3dx_user.ini snapshots). The slots
// themselves are in SlotService and their pictures in PresetPreviewService.
public sealed partial class WorkspacePage
{
    // The window used to subscribe these to the page's controls (AttachWorkspacePageEvents).
    private void AttachStateSlotEvents()
    {
        BrowsePersistentUserIniButton.Click += OnBrowsePersistentUserIniClicked;
        PersistentSlotComboBox.SelectionChanged += OnPersistentSlotSelectionChanged;
        CapturePersistentSlotButton.Click += OnCapturePersistentSlotClicked;
        DeletePersistentSlotButton.Click += OnDeletePersistentSlotClicked;
        RestorePersistentRuntimeButton.Click += OnRestorePersistentRuntimeClicked;
        BrowseCombinationUserIniButton.Click += OnBrowsePersistentUserIniClicked;
    }

    private bool _refreshingPersistentSlots;

    internal void RefreshPersistentSlotControls()
    {
        if (PersistentSlotComboBox is null) return;

        WorkspaceRepository? repository = _services.Repositories.Selected;
        string? modPath = _host.CurrentSecondLevelPath;
        string? relative = SlotService.TryGetCurrentModRelativePath(repository, modPath);
        PersistentSlotTitleTextBlock.Text = T("PersistentSlots.ModStateSlots");
        PersistentSlotHintTextBlock.Text = T("PersistentSlots.SaveAfterF10ToRestoreClose");
        BrowsePersistentUserIniButton.Content = T("Common.Browse");
        CapturePersistentSlotButton.Content = T("PersistentSlots.SaveSlot");
        RestorePersistentRuntimeButton.Content = T("PersistentSlots.RestoreLoaderState");
        DeletePersistentSlotButton.Content = T("PersistentSlots.DeleteSlot");
        OpenCombinationPresetsButton.Content = T("Common.OpenCombinationWorkspace");
        PersistentUserIniTextBox.Text = repository?.PersistentUserIniPath is { Length: > 0 } configured
            ? configured
            : SlotService.TryGetDefaultPersistentIniPath(repository) ?? string.Empty;

        List<ModPersistentSlot> slots = repository is null || relative is null
            ? []
            : _services.Slots.Items.Where(slot =>
                string.Equals(slot.RepositoryId, repository.Id, StringComparison.Ordinal)
                && string.Equals(slot.ModRelativePath, relative, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(slot => slot.SavedAtUtc).ToList();
        if (!slots.Any(slot => slot.Id == _services.Slots.SelectedId))
            _services.Slots.SelectedId = slots.FirstOrDefault()?.Id;

        _refreshingPersistentSlots = true;
        try
        {
            PersistentSlotComboBox.Items.Clear();
            ModPersistentSlot? current = SlotService.CurrentPersistentSlot(slots);
            foreach (ModPersistentSlot slot in slots)
            {
                PersistentSlotComboBox.Items.Add(new ComboBoxItem
                {
                    Content = _services.PresetPreviews.PresetPreviewLabel(ReferenceEquals(slot, current)
                        ? T("PersistentVariants.SlotLabelCurrent", slot.Name, slot.Values.Count)
                        : $"{slot.Name} · {slot.Values.Count}", slot.PreviewFile),
                    Tag = slot.Id
                });
            }

            PersistentSlotComboBox.SelectedItem = PersistentSlotComboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Tag as string, _services.Slots.SelectedId, StringComparison.Ordinal));
            PersistentSlotComboBox.PlaceholderText = T("PersistentSlots.NoSavedStateSlots");
        }
        finally
        {
            _refreshingPersistentSlots = false;
        }

        BrowsePersistentUserIniButton.IsEnabled = repository is not null && !_services.Slots.IsRestoring && !_services.Combinations.IsApplying;
        CapturePersistentSlotButton.IsEnabled = relative is not null && !_services.Slots.IsRestoring && !_services.Combinations.IsApplying;
        DeletePersistentSlotButton.IsEnabled = relative is not null && _services.Slots.SelectedId is not null && !_services.Slots.IsRestoring && !_services.Combinations.IsApplying;
        RestorePersistentRuntimeButton.IsEnabled = DeletePersistentSlotButton.IsEnabled && !_services.Slots.IsRestoring;
        RefreshPersistentSlotMoreMenu(repository, relative);
        RefreshPersistentSlotValuePreview();
        _services.Overlay.Refresh(bindingsChanged: true);
        PersistentSlotStatusTextBlock.Text = relative is null
            ? T("PersistentSlots.SelectAModToManageIts")
            : T("PersistentSlots.CurrentModSlotS", Path.GetFileName(modPath), slots.Count);
    }

    private ModPersistentSlot? GetSelectedPersistentSlot() => _services.Slots.Items.FirstOrDefault(slot =>
        string.Equals(slot.Id, _services.Slots.SelectedId, StringComparison.Ordinal));

    private void RefreshPersistentSlotValuePreview()
    {
        if (PersistentSlotValuesTextBlock is null) return;
        ModPersistentSlot? slot = GetSelectedPersistentSlot();
        PersistentSlotPreviewHost.Content = slot is null ? null : _services.PresetPreviews.CreatePresetPreviewEditor(slot.RepositoryId, slot.Id, slot.PreviewFile,
            file => slot.PreviewFile = file, RefreshPersistentSlotControls);
        PersistentSlotValuesTextBlock.Text = slot is null
            ? T("PersistentSlots.SavedINIPathsVariablesAndValues")
            : string.Join(Environment.NewLine, slot.Values.Select(value => $"{value.RelativeIniPath}  ·  {value.Variable} = {value.Value}"));
    }

    private void OnPersistentSlotSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshingPersistentSlots) return;
        _services.Slots.SelectedId = (PersistentSlotComboBox.SelectedItem as ComboBoxItem)?.Tag as string;
        DeletePersistentSlotButton.IsEnabled = _services.Slots.SelectedId is not null;
        RestorePersistentRuntimeButton.IsEnabled = DeletePersistentSlotButton.IsEnabled && !_services.Slots.IsRestoring;
        RefreshPersistentSlotValuePreview();
    }

    private async void OnBrowsePersistentUserIniClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_services.Slots.IsRestoring || _services.Combinations.IsApplying) return;
            string? path = await _services.Dialogs.PickFileAsync([".ini"]);
            if (path is null) return;
            if (!string.Equals(Path.GetFileName(path), "d3dx_user.ini", StringComparison.OrdinalIgnoreCase))
            {
                await ShowMessageAsync(T("PersistentSlots.SelectD3dxUserIni"),
                    T("PersistentSlots.WrongFile"));
                return;
            }

            PersistentUserIniTextBox.Text = path;
            WorkspaceRepository? repository = _services.Repositories.Selected;
            if (repository is not null)
            {
                repository.PersistentUserIniPath = path;
                _host.SaveShellConfig();
            }
            RefreshConfigurationProfiles();
        }
        catch (Exception ex)
        {
            _host.ReportUiActionFailure("OnBrowsePersistentUserIniClicked", ex);
        }
    }

    private async void OnCapturePersistentSlotClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_services.Slots.IsRestoring || _services.Combinations.IsApplying) return;
            WorkspaceRepository? repository = _services.Repositories.Selected;
            string? modPath = _host.CurrentSecondLevelPath;
            string? relative = SlotService.TryGetCurrentModRelativePath(repository, modPath);
            if (repository is null || modPath is null || relative is null) return;

            string userIni = PersistentUserIniTextBox.Text.Trim();
            if (!string.Equals(Path.GetFileName(userIni), "d3dx_user.ini", StringComparison.OrdinalIgnoreCase))
            {
                await ShowMessageAsync(T("PersistentSlots.SelectTheLoaderSD3dxUser"),
                    T("PersistentSlots.CannotCapture"));
                return;
            }

            string deployedName = Path.GetFileName(Path.TrimEndingDirectorySeparator(modPath));
            bool deployed;
            try
            {
                deployed = !string.IsNullOrWhiteSpace(repository.TargetPath)
                    && Directory.Exists(Path.Combine(repository.TargetPath, deployedName));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                deployed = false;
            }
            if (!deployed)
            {
                await ShowMessageAsync(T("PersistentSlots.ThisModIsNotDeployedUnder"),
                    T("PersistentSlots.CannotCapture"));
                return;
            }

            IReadOnlyList<ModPersistentValue> values;
            try
            {
                values = await Task.Run(() => ModPersistentPresetEngine.Capture(modPath, deployedName, userIni));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or DecoderFallbackException or ArgumentException)
            {
                await ShowMessageAsync(ex.Message, T("PersistentSlots.CaptureFailed"));
                return;
            }

            if (values.Count == 0)
            {
                await ShowMessageAsync(T("PersistentSlots.NoNumericPersistentValuesMatchedThis"),
                    T("PersistentSlots.NoSupportedState"));
                return;
            }
            if (await ChooseSlotValuesAsync(values) is not { } chosen) return;
            values = chosen;

            string? name = await PromptForTextAsync(
                T("PersistentSlots.FoundVariablesNameThisSlot", values.Count),
                T("PersistentSlots.SaveInternalState"),
                T("PersistentSlots.State1"), T("Common.Save"));
            name = name?.Trim();
            if (string.IsNullOrWhiteSpace(name)) return;

            await _services.Slots.EnsureDefaultSlotAsync(repository, modPath, relative);
            var slot = new ModPersistentSlot
            {
                RepositoryId = repository.Id,
                ModRelativePath = relative,
                Name = name,
                Values = [.. values],
                ActivatedAtUtc = DateTimeOffset.UtcNow
            };
            _services.Slots.Items.Add(slot);
            _services.Slots.SelectedId = slot.Id;
            repository.PersistentUserIniPath = userIni;
            _host.SaveShellConfig();
            RefreshPersistentSlotControls();
            ShowAppNotification(T("PersistentSlots.OfflineSaved", values.Count, name));
        }
        catch (Exception ex)
        {
            _host.ReportUiActionFailure("OnCapturePersistentSlotClicked", ex);
        }
    }

    private async void OnRestorePersistentRuntimeClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_services.Slots.IsRestoring || _services.Combinations.IsApplying) return;
            WorkspaceRepository? repository = _services.Repositories.Selected;
            string? modPath = _host.CurrentSecondLevelPath;
            string? relative = SlotService.TryGetCurrentModRelativePath(repository, modPath);
            ModPersistentSlot? slot = GetSelectedPersistentSlot();
            if (repository is null || modPath is null || relative is null || slot is null
                || slot.RepositoryId != repository.Id
                || !string.Equals(slot.ModRelativePath, relative, StringComparison.OrdinalIgnoreCase)) return;

            string userIni = PersistentUserIniTextBox.Text.Trim();
            string deployedName = Path.GetFileName(Path.TrimEndingDirectorySeparator(modPath));
            _services.Slots.IsRestoring = true;
            RefreshConfigurationProfiles();
            PersistentSlotComboBox.IsEnabled = false;
            PersistentUserIniTextBox.IsEnabled = false;
            BrowsePersistentUserIniButton.IsEnabled = false;
            CapturePersistentSlotButton.IsEnabled = false;
            DeletePersistentSlotButton.IsEnabled = false;
            RestorePersistentRuntimeButton.IsEnabled = false;
            string? resultText = null;
            try
            {
                if (string.IsNullOrWhiteSpace(repository.TargetPath)
                    || !Directory.Exists(Path.Combine(repository.TargetPath, deployedName)))
                    throw new InvalidDataException(T("PersistentSlots.DeployThisModUnderItsMatching"));
                IReadOnlyList<ModPersistentValue> values = await Task.Run(() => _services.Slots.ValuesToRestore(slot, modPath));
                ModPersistentRuntimeRestorePlan plan = await Task.Run(() =>
                    ModPersistentPresetEngine.PlanRuntimeRestore(modPath, deployedName, userIni, values));
                slot.Values = [.. values];
                (bool restore, bool hotReload) = await LoaderStateRestoreDialog.ConfirmSlotAsync(_services,
                    _services.PresetPreviews.CreatePresetPreviewImage(slot.PreviewFile, 320, 180),
                    T("PersistentSlots.SlotFileChangeAddAlreadyEqual", slot.Name, plan.Edit.Path, plan.ChangedCount, plan.AddedCount, plan.UnchangedCount),
                    _services.Slots.CanHotReloadLoader);
                if (!restore) return;
                string? backup = await Task.Run(() => ModPersistentPresetEngine.ApplyRuntimeRestore(plan));
                slot.ActivatedAtUtc = DateTimeOffset.UtcNow;
                repository.PersistentUserIniPath = userIni;
                _host.SaveShellConfig();
                resultText = T("PersistentSlots.RestoreVerified", slot.Name, plan.ChangedCount, plan.AddedCount, plan.UnchangedCount) + "\n"
                    + (backup is null ? T("PersistentSlots.NoNewBackup") : T("PersistentSlots.BackupAt", backup)) + "\n" + T("PersistentSlots.RestartToVerify");
                ShowAppNotification(T("PersistentSlots.OfflineRestored", slot.Name, plan.Values.Count));
                if (hotReload)
                    resultText += "\n" + await _services.Slots.HotReloadLoaderAsync(userIni);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
                or DecoderFallbackException or ArgumentException or NotSupportedException)
            {
                await ShowMessageAsync(ex.Message, T("PersistentSlots.RestoreFailedCheckBackups"));
            }
            finally
            {
                _services.Slots.IsRestoring = false;
                PersistentSlotComboBox.IsEnabled = true;
                PersistentUserIniTextBox.IsEnabled = true;
                RefreshPersistentSlotControls();
                RefreshConfigurationProfiles();
                if (resultText is not null) PersistentSlotStatusTextBlock.Text = resultText;
            }
        }
        catch (Exception ex)
        {
            _host.ReportUiActionFailure("OnRestorePersistentRuntimeClicked", ex);
        }
    }

    private async void OnDeletePersistentSlotClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_services.Slots.IsRestoring) return;
            ModPersistentSlot? slot = GetSelectedPersistentSlot();
            if (slot is null || !await ShowDangerConfirmAsync(
                T("PersistentSlots.DeleteSlotModINIsWillNot", slot.Name),
                T("PersistentSlots.DeleteStateSlot"), T("Dialogs.Delete"))) return;

            _services.Slots.Items.Remove(slot);
            _services.Slots.SelectedId = null;
            _host.SaveShellConfig();
            _services.PresetPreviews.DeletePresetPreview(slot.PreviewFile);
            RefreshPersistentSlotControls();
        }
        catch (Exception ex)
        {
            _host.ReportUiActionFailure("OnDeletePersistentSlotClicked", ex);
        }
    }

    private void RefreshPersistentSlotMoreMenu(WorkspaceRepository? repository, string? relative)
    {
        PersistentSlotMoreButton.Content = T("PersistentVariants.More");
        bool idle = !_services.Slots.IsRestoring && !_services.Combinations.IsApplying;
        bool hasSlot = relative is not null && _services.Slots.SelectedId is not null;
        var menu = new MenuFlyout { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Bottom };

        MenuFlyoutItem Item(string text, bool enabled, Func<Task> action)
        {
            var item = new MenuFlyoutItem { Text = text, IsEnabled = enabled && idle };
            item.Click += async (_, _) =>
            {
                try
                {
                    await action();
                }
                catch (Exception exception)
                {
                    _host.ReportUiActionFailure("Persistent slot: " + text, exception);
                }
            };
            return item;
        }

        menu.Items.Add(Item(T("PersistentVariants.UpdateSelectedSlot"), hasSlot, UpdateSelectedPersistentSlotAsync));
        menu.Items.Add(Item(T("PersistentVariants.RenameSelectedSlot"), hasSlot, RenameSelectedPersistentSlotAsync));
        menu.Items.Add(Item(T("PersistentVariants.WriteSlotAsModDefaults"), hasSlot, WriteSelectedSlotAsModDefaultsAsync));
        menu.Items.Add(Item(T("PersistentVariants.ImportVariantFile"), relative is not null, ImportVariantFileAsync));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Item(T("PersistentVariants.SaveUserIniSnapshot"), repository is not null, SaveUserIniSnapshotAsync));
        menu.Items.Add(Item(T("PersistentVariants.RestoreUserIniSnapshot"), repository is not null, RestoreUserIniSnapshotAsync));
        menu.Items.Add(Item(T("PersistentVariants.OpenSnapshotFolder"), repository is not null, () =>
        {
            string directory = UserIniSnapshotDirectory(repository!);
            Directory.CreateDirectory(directory);
            _services.Dialogs.OpenDirectory(directory, T("PersistentVariants.SnapshotFolder"));
            return Task.CompletedTask;
        }));
        PersistentSlotMoreButton.Flyout = menu;
        PersistentSlotMoreButton.IsEnabled = repository is not null && idle;
    }

    // The selected slot of the current Mod, with what is needed to capture or write it.
    private (WorkspaceRepository Repository, string ModPath, string Relative, ModPersistentSlot Slot)? SelectedSlotContext()
    {
        WorkspaceRepository? repository = _services.Repositories.Selected;
        string? modPath = _host.CurrentSecondLevelPath;
        string? relative = SlotService.TryGetCurrentModRelativePath(repository, modPath);
        ModPersistentSlot? slot = GetSelectedPersistentSlot();
        return repository is null || modPath is null || relative is null || slot is null
            || slot.RepositoryId != repository.Id
            || !string.Equals(slot.ModRelativePath, relative, StringComparison.OrdinalIgnoreCase)
                ? null
                : (repository, modPath, relative, slot);
    }

    private async Task UpdateSelectedPersistentSlotAsync()
    {
        if (SelectedSlotContext() is not { } context) return;
        string userIni = PersistentUserIniTextBox.Text.Trim();
        string deployedName = ModDeploymentLayout.DeployedName(context.ModPath);
        IReadOnlyList<ModPersistentValue> values;
        try
        {
            values = await Task.Run(() => ModPersistentPresetEngine.Capture(context.ModPath, deployedName, userIni));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or DecoderFallbackException or ArgumentException)
        {
            await ShowMessageAsync(ex.Message, T("PersistentSlots.CaptureFailed"));
            return;
        }

        if (values.Count == 0)
        {
            await ShowMessageAsync(T("PersistentSlots.NoNumericPersistentValuesMatchedThis"), T("PersistentSlots.NoSupportedState"));
            return;
        }
        if (await ChooseSlotValuesAsync(values) is not { } chosen) return;
        values = chosen;
        if (!await ShowDangerConfirmAsync(T("PersistentVariants.ReplaceSlotValues", context.Slot.Name, context.Slot.Values.Count, values.Count),
                T("PersistentVariants.UpdateSelectedSlot"), T("Dialogs.Replace")))
        {
            return;
        }

        context.Slot.Values = [.. values];
        // It now holds captured values, so it no longer follows the Mod's declarations when restored.
        context.Slot.IsModDefault = false;
        context.Slot.SavedAtUtc = DateTimeOffset.UtcNow;
        context.Slot.ActivatedAtUtc = DateTimeOffset.UtcNow;
        context.Repository.PersistentUserIniPath = userIni;
        _host.SaveShellConfig();
        RefreshPersistentSlotControls();
        PersistentSlotStatusTextBlock.Text = T("PersistentVariants.SlotUpdated", context.Slot.Name, values.Count);
    }

    private async Task RenameSelectedPersistentSlotAsync()
    {
        if (SelectedSlotContext() is not { } context) return;
        string? name = (await PromptForTextAsync(T("PersistentVariants.EnterANewSlotName"), T("PersistentVariants.RenameSelectedSlot"), context.Slot.Name, T("Dialogs.Rename")))?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name == context.Slot.Name) return;
        context.Slot.Name = name;
        _host.SaveShellConfig();
        RefreshPersistentSlotControls();
    }

    // Makes the slot's values the defaults in the Mod's own INI files, with backups beside each file.
    // In copy mode the deployed copy is changed too, so the loader sees the same defaults.
    private async Task WriteSelectedSlotAsModDefaultsAsync()
    {
        if (SelectedSlotContext() is not { } context) return;
        string deployedName = ModDeploymentLayout.DeployedName(context.ModPath);
        var directories = new List<string> { context.ModPath };
        string? deployed = string.IsNullOrWhiteSpace(context.Repository.TargetPath)
            ? null : ModDeploymentLayout.DeployedPath(context.Repository.TargetPath, context.ModPath);
        if (deployed is not null && Directory.Exists(deployed) && !DirectoryLinkDeployment.IsDirectoryLink(deployed))
        {
            directories.Add(deployed);
        }

        ModIniDefaultsPlan[] plans;
        try
        {
            plans = await Task.Run(() => directories
                .Select(directory => ModPersistentPresetEngine.PlanModIniDefaults(directory, deployedName, context.Slot.Values))
                .ToArray());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or DecoderFallbackException or ArgumentException)
        {
            await ShowMessageAsync(ex.Message, T("PersistentVariants.CannotWriteModDefaults"));
            return;
        }

        int changed = plans.Sum(plan => plan.ChangedCount);
        if (changed == 0)
        {
            await ShowMessageAsync(T("PersistentVariants.ModDefaultsAlreadyMatch", context.Slot.Name), T("PersistentVariants.WriteSlotAsModDefaults"));
            return;
        }

        string files = string.Join("\n", plans.SelectMany(plan => plan.Edits).Select(edit => edit.Path));
        if (!await ShowDangerConfirmAsync(T("PersistentVariants.ConfirmWriteModDefaults", context.Slot.Name, plans[0].ChangedCount, files),
                T("PersistentVariants.WriteSlotAsModDefaults"), T("Dialogs.Write")))
        {
            return;
        }

        var applied = new List<(ModIniDefaultsPlan Plan, IReadOnlyList<string> Backups)>();
        try
        {
            foreach (ModIniDefaultsPlan plan in plans)
            {
                applied.Add((plan, await Task.Run(() => ModPersistentPresetEngine.ApplyModIniDefaults(plan))));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or DecoderFallbackException)
        {
            // Undo the copies already changed, so the source and the deployed copy never disagree.
            foreach ((ModIniDefaultsPlan plan, IReadOnlyList<string> backups) in applied.AsEnumerable().Reverse())
            {
                try { ModPersistentPresetEngine.RollbackModIniDefaults(plan, backups); }
                catch (Exception rollback) { _host.LogApplicationIssue("Roll back Mod INI defaults", rollback); }
            }
            await ShowMessageAsync(ex.Message, T("PersistentVariants.CannotWriteModDefaults"));
            return;
        }

        string backupsText = string.Join("\n", applied.SelectMany(item => item.Backups));
        PersistentSlotStatusTextBlock.Text = T("PersistentVariants.ModDefaultsWritten", context.Slot.Name, changed, backupsText);
        ShowAppNotification(T("PersistentVariants.ModDefaultsWrittenShort", context.Slot.Name));
    }

    // Imports a compatible variant JSON from the Mod folder (or a chosen file) as state slots.
    private async Task ImportVariantFileAsync()
    {
        WorkspaceRepository? repository = _services.Repositories.Selected;
        string? modPath = _host.CurrentSecondLevelPath;
        string? relative = SlotService.TryGetCurrentModRelativePath(repository, modPath);
        if (repository is null || modPath is null || relative is null) return;

        string file = Path.Combine(modPath, VariantFileImport.FileName);
        if (!File.Exists(file))
        {
            string? chosen = await _services.Dialogs.PickFileAsync([".json"]);
            if (chosen is null) return;
            file = chosen;
        }

        ImportedVariantSet set;
        try
        {
            string deployedName = ModDeploymentLayout.DeployedName(modPath);
            set = await Task.Run(async () => VariantFileImport.Parse(
                await File.ReadAllTextAsync(file), deployedName, ModPersistentPresetEngine.Declarations(modPath, deployedName)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException or DecoderFallbackException)
        {
            await ShowMessageAsync(ex.Message, T("PersistentVariants.ImportFailed"));
            return;
        }

        ImportedVariant[] usable = set.Variants.Where(variant => variant.Values.Count > 0).ToArray();
        if (usable.Length == 0)
        {
            await ShowMessageAsync(T("PersistentVariants.NoVariantMatchedThisMod"), T("PersistentVariants.ImportVariantFile"));
            return;
        }

        HashSet<string> taken = _services.Slots.Items
            .Where(slot => slot.RepositoryId == repository.Id && string.Equals(slot.ModRelativePath, relative, StringComparison.OrdinalIgnoreCase))
            .Select(slot => slot.Name).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        ModPersistentSlot? active = null;
        foreach (ImportedVariant variant in usable)
        {
            string name = variant.Name;
            for (int suffix = 2; !taken.Add(name); suffix++) name = $"{variant.Name} ({suffix})";
            var slot = new ModPersistentSlot { RepositoryId = repository.Id, ModRelativePath = relative, Name = name, Values = [.. variant.Values] };
            _services.Slots.Items.Add(slot);
            if (string.Equals(variant.Name, set.ActiveName?.Trim(), StringComparison.OrdinalIgnoreCase)) active = slot;
        }
        if (active is not null)
        {
            active.ActivatedAtUtc = now;
            _services.Slots.SelectedId = active.Id;
        }

        _host.SaveShellConfig();
        RefreshPersistentSlotControls();
        int unmatched = usable.Sum(variant => variant.UnmatchedCount);
        await ShowMessageAsync(T("PersistentVariants.ImportedVariants", usable.Length, set.Variants.Count - usable.Length, unmatched),
            T("PersistentVariants.ImportVariantFile"));
    }

    private static string UserIniSnapshotDirectory(WorkspaceRepository repository)
    {
        string id = string.Concat(repository.Id.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
        return Path.Combine(AppContext.BaseDirectory, "snapshots", "user-ini", id);
    }

    private async Task<string?> RequireUserIniPathAsync(string title)
    {
        string userIni = PersistentUserIniTextBox.Text.Trim();
        if (string.Equals(Path.GetFileName(userIni), "d3dx_user.ini", StringComparison.OrdinalIgnoreCase) && File.Exists(userIni))
        {
            return userIni;
        }

        await ShowMessageAsync(T("PersistentSlots.SelectTheLoaderSD3dxUser"), title);
        return null;
    }

    private async Task SaveUserIniSnapshotAsync()
    {
        WorkspaceRepository? repository = _services.Repositories.Selected;
        if (repository is null || await RequireUserIniPathAsync(T("PersistentVariants.SaveUserIniSnapshot")) is not { } userIni) return;
        string? name = (await PromptForTextAsync(T("PersistentVariants.NameThisSnapshot"), T("PersistentVariants.SaveUserIniSnapshot"),
            DateTime.Now.ToString("yyyy-MM-dd HH.mm", System.Globalization.CultureInfo.InvariantCulture), T("Common.Save")))?.Trim();
        if (string.IsNullOrWhiteSpace(name)) return;

        try
        {
            UserIniSnapshot snapshot = await Task.Run(() => UserIniSnapshots.Save(userIni, UserIniSnapshotDirectory(repository), name, DateTimeOffset.Now));
            PersistentSlotStatusTextBlock.Text = T("PersistentVariants.SnapshotSaved", snapshot.Name, snapshot.Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            await ShowMessageAsync(ex.Message, T("PersistentVariants.SaveUserIniSnapshot"));
        }
    }

    private async Task RestoreUserIniSnapshotAsync()
    {
        WorkspaceRepository? repository = _services.Repositories.Selected;
        if (repository is null || await RequireUserIniPathAsync(T("PersistentVariants.RestoreUserIniSnapshot")) is not { } userIni) return;
        IReadOnlyList<UserIniSnapshot> snapshots = UserIniSnapshots.List(UserIniSnapshotDirectory(repository));
        if (snapshots.Count == 0)
        {
            await ShowMessageAsync(T("PersistentVariants.NoSnapshotsYet"), T("PersistentVariants.RestoreUserIniSnapshot"));
            return;
        }

        (UserIniSnapshot? chosen, bool hotReload) = await LoaderStateRestoreDialog.ChooseSnapshotAsync(_services, snapshots, userIni, _services.Slots.CanHotReloadLoader);
        if (chosen is null) return;

        try
        {
            string backup = await Task.Run(() => UserIniSnapshots.Restore(chosen, userIni, DateTimeOffset.Now));
            PersistentSlotStatusTextBlock.Text = T("PersistentVariants.SnapshotRestored", chosen.Name, backup);
            if (hotReload)
                PersistentSlotStatusTextBlock.Text += "\n" + await _services.Slots.HotReloadLoaderAsync(userIni);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            await ShowMessageAsync(ex.Message, T("PersistentVariants.RestoreUserIniSnapshot"));
        }
    }

    // Main window: the user sees which menu-state values would be left out and can keep any of them.
    // Returns null when the user cancels.
    private async Task<IReadOnlyList<ModPersistentValue>?> ChooseSlotValuesAsync(IReadOnlyList<ModPersistentValue> values)
    {
        if (_services.Shortcuts.KeepMenuStateVariables) return values;
        var (kept, transient) = TransientVariablePolicy.Split(values);
        if (transient.Count == 0) return values;
        if (await MenuStateDialog.ChooseAsync(_services, transient, kept.Count, Math.Max(160, _host.Root.ActualHeight - 360)) is not { } keepAnyway) return null;
        return values.Where(value => !transient.Contains(value) || keepAnyway.Contains(value)).ToArray();
    }

}
