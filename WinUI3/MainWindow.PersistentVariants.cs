using System.Text;
using IntegratedModManager.Core.Deployment;
using IntegratedModManager.Core.Presets;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

// Extra actions for state slots (named variants of one Mod's persistent values) and whole
// d3dx_user.ini snapshots, behind the "More" button of the state slot card.
public sealed partial class MainWindow
{
    private static ModPersistentSlot? CurrentPersistentSlot(IEnumerable<ModPersistentSlot> slots)
        => slots.Where(slot => slot.ActivatedAtUtc is not null).MaxBy(slot => slot.ActivatedAtUtc);

    private void RefreshPersistentSlotMoreMenu(WorkspaceRepository? repository, string? relative)
    {
        PersistentSlotMoreButton.Content = T("PersistentVariants.More");
        bool idle = !_restoringPersistentRuntime && !_applyingCombinationProfile;
        bool hasSlot = relative is not null && _selectedModPersistentSlotId is not null;
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
                    ReportUiActionFailure("Persistent slot: " + text, exception);
                }
            };
            return item;
        }

        menu.Items.Add(Item(T("PersistentVariants.UpdateSelectedSlot"), hasSlot, UpdateSelectedPersistentSlotAsync));
        menu.Items.Add(Item(T("PersistentVariants.RenameSelectedSlot"), hasSlot, RenameSelectedPersistentSlotAsync));
        menu.Items.Add(Item(T("PersistentVariants.WriteSlotAsModDefaults"), hasSlot, WriteSelectedSlotAsModDefaultsAsync));
        menu.Items.Add(Item(T("PersistentVariants.ImportJasmVariants"), relative is not null, ImportJasmVariantsAsync));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Item(T("PersistentVariants.SaveUserIniSnapshot"), repository is not null, SaveUserIniSnapshotAsync));
        menu.Items.Add(Item(T("PersistentVariants.RestoreUserIniSnapshot"), repository is not null, RestoreUserIniSnapshotAsync));
        menu.Items.Add(Item(T("PersistentVariants.OpenSnapshotFolder"), repository is not null, () =>
        {
            string directory = UserIniSnapshotDirectory(repository!);
            Directory.CreateDirectory(directory);
            OpenDirectory(directory, T("PersistentVariants.SnapshotFolder"));
            return Task.CompletedTask;
        }));
        PersistentSlotMoreButton.Flyout = menu;
        PersistentSlotMoreButton.IsEnabled = repository is not null && idle;
    }

    // The selected slot of the current Mod, with what is needed to capture or write it.
    private (WorkspaceRepository Repository, string ModPath, string Relative, ModPersistentSlot Slot)? SelectedSlotContext()
    {
        WorkspaceRepository? repository = GetSelectedRepository();
        string? modPath = _currentSecondLevelPath;
        string? relative = TryGetCurrentModRelativePath(repository, modPath);
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
        if (!await ShowConfirmAsync(T("PersistentVariants.ReplaceSlotValues", context.Slot.Name, context.Slot.Values.Count, values.Count),
                T("PersistentVariants.UpdateSelectedSlot")))
        {
            return;
        }

        context.Slot.Values = [.. values];
        context.Slot.SavedAtUtc = DateTimeOffset.UtcNow;
        context.Slot.ActivatedAtUtc = DateTimeOffset.UtcNow;
        context.Repository.PersistentUserIniPath = userIni;
        SaveShellConfig();
        RefreshPersistentSlotControls();
        PersistentSlotStatusTextBlock.Text = T("PersistentVariants.SlotUpdated", context.Slot.Name, values.Count);
    }

    private async Task RenameSelectedPersistentSlotAsync()
    {
        if (SelectedSlotContext() is not { } context) return;
        string? name = (await PromptForTextAsync(T("PersistentVariants.EnterANewSlotName"), T("PersistentVariants.RenameSelectedSlot"), context.Slot.Name))?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name == context.Slot.Name) return;
        context.Slot.Name = name;
        SaveShellConfig();
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
        if (!await ShowConfirmAsync(T("PersistentVariants.ConfirmWriteModDefaults", context.Slot.Name, plans[0].ChangedCount, files),
                T("PersistentVariants.WriteSlotAsModDefaults")))
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
                catch (Exception rollback) { LogApplicationIssue("Roll back Mod INI defaults", rollback); }
            }
            await ShowMessageAsync(ex.Message, T("PersistentVariants.CannotWriteModDefaults"));
            return;
        }

        string backupsText = string.Join("\n", applied.SelectMany(item => item.Backups));
        PersistentSlotStatusTextBlock.Text = T("PersistentVariants.ModDefaultsWritten", context.Slot.Name, changed, backupsText);
        ShowAppNotification(T("PersistentVariants.ModDefaultsWrittenShort", context.Slot.Name), T("PersistentVariants.ModDefaultsWrittenShort", context.Slot.Name));
    }

    // Imports JASM's .JASM_Variants.json from the Mod folder (or a chosen file) as state slots.
    private async Task ImportJasmVariantsAsync()
    {
        WorkspaceRepository? repository = GetSelectedRepository();
        string? modPath = _currentSecondLevelPath;
        string? relative = TryGetCurrentModRelativePath(repository, modPath);
        if (repository is null || modPath is null || relative is null) return;

        string file = Path.Combine(modPath, JasmVariantImport.FileName);
        if (!File.Exists(file))
        {
            string? chosen = await PickFileAsync([".json"]);
            if (chosen is null) return;
            file = chosen;
        }

        ImportedVariantSet set;
        try
        {
            string deployedName = ModDeploymentLayout.DeployedName(modPath);
            set = await Task.Run(async () => JasmVariantImport.Parse(
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
            await ShowMessageAsync(T("PersistentVariants.NoVariantMatchedThisMod"), T("PersistentVariants.ImportJasmVariants"));
            return;
        }

        HashSet<string> taken = _modPersistentSlots
            .Where(slot => slot.RepositoryId == repository.Id && string.Equals(slot.ModRelativePath, relative, StringComparison.OrdinalIgnoreCase))
            .Select(slot => slot.Name).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        ModPersistentSlot? active = null;
        foreach (ImportedVariant variant in usable)
        {
            string name = variant.Name;
            for (int suffix = 2; !taken.Add(name); suffix++) name = $"{variant.Name} (JASM {suffix})";
            var slot = new ModPersistentSlot { RepositoryId = repository.Id, ModRelativePath = relative, Name = name, Values = [.. variant.Values] };
            _modPersistentSlots.Add(slot);
            if (string.Equals(variant.Name, set.ActiveName?.Trim(), StringComparison.OrdinalIgnoreCase)) active = slot;
        }
        if (active is not null)
        {
            active.ActivatedAtUtc = now;
            _selectedModPersistentSlotId = active.Id;
        }

        SaveShellConfig();
        RefreshPersistentSlotControls();
        int unmatched = usable.Sum(variant => variant.UnmatchedCount);
        await ShowMessageAsync(T("PersistentVariants.ImportedVariants", usable.Length, set.Variants.Count - usable.Length, unmatched),
            T("PersistentVariants.ImportJasmVariants"));
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
        WorkspaceRepository? repository = GetSelectedRepository();
        if (repository is null || await RequireUserIniPathAsync(T("PersistentVariants.SaveUserIniSnapshot")) is not { } userIni) return;
        string? name = (await PromptForTextAsync(T("PersistentVariants.NameThisSnapshot"), T("PersistentVariants.SaveUserIniSnapshot"),
            DateTime.Now.ToString("yyyy-MM-dd HH.mm", System.Globalization.CultureInfo.InvariantCulture)))?.Trim();
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
        WorkspaceRepository? repository = GetSelectedRepository();
        if (repository is null || await RequireUserIniPathAsync(T("PersistentVariants.RestoreUserIniSnapshot")) is not { } userIni) return;
        IReadOnlyList<UserIniSnapshot> snapshots = UserIniSnapshots.List(UserIniSnapshotDirectory(repository));
        if (snapshots.Count == 0)
        {
            await ShowMessageAsync(T("PersistentVariants.NoSnapshotsYet"), T("PersistentVariants.RestoreUserIniSnapshot"));
            return;
        }

        var choices = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (UserIniSnapshot snapshot in snapshots)
        {
            choices.Items.Add(new ComboBoxItem { Content = $"{snapshot.SavedAt.LocalDateTime:yyyy-MM-dd HH:mm} · {snapshot.Name}", Tag = snapshot });
        }
        choices.SelectedIndex = 0;
        var closed = new CheckBox { Content = T("Common.IHaveFullyClosedTheGame") };
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = T("PersistentVariants.RestoreSnapshotExplanation", userIni), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(choices);
        panel.Children.Add(closed);
        var dialog = new ContentDialog
        {
            Title = T("PersistentVariants.RestoreUserIniSnapshot"),
            Content = panel,
            PrimaryButtonText = T("PersistentSlots.BackUpAndRestore"),
            CloseButtonText = T("Common.Cancel"),
            DefaultButton = ContentDialogButton.Close,
            IsPrimaryButtonEnabled = false,
            RequestedTheme = RootGrid.ActualTheme,
            XamlRoot = RootGrid.XamlRoot
        };
        CheckBox hotReload = CreateHotReloadOption(closed, dialog);
        panel.Children.Add(hotReload);
        closed.Checked += (_, _) => dialog.IsPrimaryButtonEnabled = true;
        closed.Unchecked += (_, _) => dialog.IsPrimaryButtonEnabled = hotReload.IsChecked == true;
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || choices.SelectedItem is not ComboBoxItem { Tag: UserIniSnapshot chosen }) return;

        try
        {
            string backup = await Task.Run(() => UserIniSnapshots.Restore(chosen, userIni, DateTimeOffset.Now));
            PersistentSlotStatusTextBlock.Text = T("PersistentVariants.SnapshotRestored", chosen.Name, backup);
            if (hotReload.IsChecked == true)
                PersistentSlotStatusTextBlock.Text += "\n" + await HotReloadLoaderAsync(userIni);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            await ShowMessageAsync(ex.Message, T("PersistentVariants.RestoreUserIniSnapshot"));
        }
    }
}
