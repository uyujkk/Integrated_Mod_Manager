using System.Text;
using IntegratedModManager.Core.Presets;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private string? _selectedModPersistentSlotId;
    private bool _refreshingPersistentSlots;
    private bool _restoringPersistentRuntime;

    // The portable prototype ships synthetic fixtures, never real user configuration.
    // Resolve paths at first run so moving/extracting the package remains safe.
    private static WorkspaceRepository? TryCreatePersistentDemoRepository()
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

    private void RefreshPersistentSlotControls()
    {
        if (PersistentSlotComboBox is null) return;

        WorkspaceRepository? repository = GetSelectedRepository();
        string? modPath = _currentSecondLevelPath;
        string? relative = TryGetCurrentModRelativePath(repository, modPath);
        PersistentSlotTitleTextBlock.Text = T("PersistentSlots.ModStateSlots");
        PersistentSlotHintTextBlock.Text = T("PersistentSlots.SaveAfterF10ToRestoreClose");
        BrowsePersistentUserIniButton.Content = T("Common.Browse");
        CapturePersistentSlotButton.Content = T("PersistentSlots.SaveSlot");
        RestorePersistentRuntimeButton.Content = T("PersistentSlots.RestoreLoaderState");
        DeletePersistentSlotButton.Content = T("PersistentSlots.DeleteSlot");
        OpenCombinationPresetsButton.Content = T("Common.OpenCombinationWorkspace");
        PersistentUserIniTextBox.Text = repository?.PersistentUserIniPath is { Length: > 0 } configured
            ? configured
            : TryGetDefaultPersistentIniPath(repository) ?? string.Empty;

        List<ModPersistentSlot> slots = repository is null || relative is null
            ? []
            : _modPersistentSlots.Where(slot =>
                string.Equals(slot.RepositoryId, repository.Id, StringComparison.Ordinal)
                && string.Equals(slot.ModRelativePath, relative, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(slot => slot.SavedAtUtc).ToList();
        if (!slots.Any(slot => slot.Id == _selectedModPersistentSlotId))
            _selectedModPersistentSlotId = slots.FirstOrDefault()?.Id;

        _refreshingPersistentSlots = true;
        try
        {
            PersistentSlotComboBox.Items.Clear();
            ModPersistentSlot? current = CurrentPersistentSlot(slots);
            foreach (ModPersistentSlot slot in slots)
            {
                PersistentSlotComboBox.Items.Add(new ComboBoxItem
                {
                    Content = ReferenceEquals(slot, current)
                        ? T("PersistentVariants.SlotLabelCurrent", slot.Name, slot.Values.Count)
                        : $"{slot.Name} · {slot.Values.Count}",
                    Tag = slot.Id
                });
            }

            PersistentSlotComboBox.SelectedItem = PersistentSlotComboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Tag as string, _selectedModPersistentSlotId, StringComparison.Ordinal));
            PersistentSlotComboBox.PlaceholderText = T("PersistentSlots.NoSavedStateSlots");
        }
        finally
        {
            _refreshingPersistentSlots = false;
        }

        BrowsePersistentUserIniButton.IsEnabled = repository is not null && !_restoringPersistentRuntime && !_applyingCombinationProfile;
        CapturePersistentSlotButton.IsEnabled = relative is not null && !_restoringPersistentRuntime && !_applyingCombinationProfile;
        DeletePersistentSlotButton.IsEnabled = relative is not null && _selectedModPersistentSlotId is not null && !_restoringPersistentRuntime && !_applyingCombinationProfile;
        RestorePersistentRuntimeButton.IsEnabled = DeletePersistentSlotButton.IsEnabled && !_restoringPersistentRuntime;
        RefreshPersistentSlotMoreMenu(repository, relative);
        RefreshPersistentSlotValuePreview();
        RefreshShortcutOverlay(bindingsChanged: true);
        PersistentSlotStatusTextBlock.Text = relative is null
            ? T("PersistentSlots.SelectAModToManageIts")
            : T("PersistentSlots.CurrentModSlotS", Path.GetFileName(modPath), slots.Count);
    }

    private static string? TryGetCurrentModRelativePath(WorkspaceRepository? repository, string? modPath)
    {
        if (repository is null || string.IsNullOrWhiteSpace(modPath)
            || string.IsNullOrWhiteSpace(repository.SourcePath)
            || !Directory.Exists(modPath) || !Directory.Exists(repository.SourcePath)) return null;

        try
        {
            string full = Path.GetFullPath(modPath);
            string sourceRoot = Path.GetFullPath(repository.SourcePath);
            if (!IsPathInsideDirectory(full, sourceRoot)) return null;
            string relative = Path.GetRelativePath(sourceRoot, full);
            return relative is "." or ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                ? null : relative;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static string? TryGetDefaultPersistentIniPath(WorkspaceRepository? repository)
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

    private ModPersistentSlot? GetSelectedPersistentSlot() => _modPersistentSlots.FirstOrDefault(slot =>
        string.Equals(slot.Id, _selectedModPersistentSlotId, StringComparison.Ordinal));

    private void RefreshPersistentSlotValuePreview()
    {
        if (PersistentSlotValuesTextBlock is null) return;
        ModPersistentSlot? slot = GetSelectedPersistentSlot();
        PersistentSlotValuesTextBlock.Text = slot is null
            ? T("PersistentSlots.SavedINIPathsVariablesAndValues")
            : string.Join(Environment.NewLine, slot.Values.Select(value => $"{value.RelativeIniPath}  ·  {value.Variable} = {value.Value}"));
    }

    private void OnPersistentSlotSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshingPersistentSlots) return;
        _selectedModPersistentSlotId = (PersistentSlotComboBox.SelectedItem as ComboBoxItem)?.Tag as string;
        DeletePersistentSlotButton.IsEnabled = _selectedModPersistentSlotId is not null;
        RestorePersistentRuntimeButton.IsEnabled = DeletePersistentSlotButton.IsEnabled && !_restoringPersistentRuntime;
        RefreshPersistentSlotValuePreview();
    }

    private async void OnBrowsePersistentUserIniClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_restoringPersistentRuntime || _applyingCombinationProfile) return;
            string? path = await PickFileAsync([".ini"]);
            if (path is null) return;
            if (!string.Equals(Path.GetFileName(path), "d3dx_user.ini", StringComparison.OrdinalIgnoreCase))
            {
                await ShowMessageAsync(T("PersistentSlots.SelectD3dxUserIni"),
                    T("PersistentSlots.WrongFile"));
                return;
            }

            PersistentUserIniTextBox.Text = path;
            WorkspaceRepository? repository = GetSelectedRepository();
            if (repository is not null)
            {
                repository.PersistentUserIniPath = path;
                SaveShellConfig();
            }
            RefreshConfigurationProfiles();
        }
        catch (Exception ex)
        {
            ReportUiActionFailure("OnBrowsePersistentUserIniClicked", ex);
        }
    }

    private async void OnCapturePersistentSlotClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_restoringPersistentRuntime || _applyingCombinationProfile) return;
            WorkspaceRepository? repository = GetSelectedRepository();
            string? modPath = _currentSecondLevelPath;
            string? relative = TryGetCurrentModRelativePath(repository, modPath);
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

            string? name = await PromptForTextAsync(
                T("PersistentSlots.FoundVariablesNameThisSlot", values.Count),
                T("PersistentSlots.SaveInternalState"),
                T("PersistentSlots.State1"));
            name = name?.Trim();
            if (string.IsNullOrWhiteSpace(name)) return;

            var slot = new ModPersistentSlot
            {
                RepositoryId = repository.Id,
                ModRelativePath = relative,
                Name = name,
                Values = [.. values],
                ActivatedAtUtc = DateTimeOffset.UtcNow
            };
            _modPersistentSlots.Add(slot);
            _selectedModPersistentSlotId = slot.Id;
            repository.PersistentUserIniPath = userIni;
            SaveShellConfig();
            RefreshPersistentSlotControls();
            ShowAppNotification($"已保存 {values.Count} 个变量到“{name}”。这是离线快照，尚未验证游戏内效果。",
                $"Saved {values.Count} variables to '{name}'. This is an offline snapshot; in-game behavior is not verified.");
        }
        catch (Exception ex)
        {
            ReportUiActionFailure("OnCapturePersistentSlotClicked", ex);
        }
    }

    private async void OnRestorePersistentRuntimeClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_restoringPersistentRuntime || _applyingCombinationProfile) return;
            WorkspaceRepository? repository = GetSelectedRepository();
            string? modPath = _currentSecondLevelPath;
            string? relative = TryGetCurrentModRelativePath(repository, modPath);
            ModPersistentSlot? slot = GetSelectedPersistentSlot();
            if (repository is null || modPath is null || relative is null || slot is null
                || slot.RepositoryId != repository.Id
                || !string.Equals(slot.ModRelativePath, relative, StringComparison.OrdinalIgnoreCase)) return;

            string userIni = PersistentUserIniTextBox.Text.Trim();
            string deployedName = Path.GetFileName(Path.TrimEndingDirectorySeparator(modPath));
            _restoringPersistentRuntime = true;
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
                ModPersistentRuntimeRestorePlan plan = await Task.Run(() =>
                    ModPersistentPresetEngine.PlanRuntimeRestore(modPath, deployedName, userIni, slot.Values));
                var closedCheck = new CheckBox
                {
                    Content = T("Common.IHaveFullyClosedTheGame")
                };
                var content = new StackPanel { Spacing = 12 };
                content.Children.Add(new TextBlock
                {
                    Text = T("PersistentSlots.SlotFileChangeAddAlreadyEqual", slot.Name, plan.Edit.Path, plan.ChangedCount, plan.AddedCount, plan.UnchangedCount),
                    TextWrapping = TextWrapping.Wrap
                });
                content.Children.Add(closedCheck);
                var dialog = new ContentDialog
                {
                    XamlRoot = RootGrid.XamlRoot,
                    Title = T("PersistentSlots.RestoreLoaderState"),
                    Content = content,
                    PrimaryButtonText = T("PersistentSlots.BackUpAndRestore"),
                    CloseButtonText = T("Common.Cancel"),
                    IsPrimaryButtonEnabled = false,
                    DefaultButton = ContentDialogButton.Close
                };
                CheckBox hotReload = CreateHotReloadOption(closedCheck, dialog);
                content.Children.Add(hotReload);
                closedCheck.Checked += (_, _) => dialog.IsPrimaryButtonEnabled = true;
                closedCheck.Unchecked += (_, _) => dialog.IsPrimaryButtonEnabled = hotReload.IsChecked == true;
                if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
                string? backup = await Task.Run(() => ModPersistentPresetEngine.ApplyRuntimeRestore(plan));
                slot.ActivatedAtUtc = DateTimeOffset.UtcNow;
                repository.PersistentUserIniPath = userIni;
                SaveShellConfig();
                resultText = L($"“{slot.Name}”已逐项核对：修改 {plan.ChangedCount}，补入 {plan.AddedCount}，已一致 {plan.UnchangedCount}。\n"
                    + (backup is null ? "无需写入，没有创建新备份。" : $"备份：{backup}") + "\n请重新启动加载器和游戏验证效果。",
                    $"'{slot.Name}' verified: changed {plan.ChangedCount}, added {plan.AddedCount}, already equal {plan.UnchangedCount}.\n"
                    + (backup is null ? "No write was needed; no new backup created." : $"Backup: {backup}") + "\nRestart the loader and game to verify the result.");
                ShowAppNotification($"已恢复“{slot.Name}”的 {plan.Values.Count} 个加载器变量，并重新读取核对。请重启游戏验证效果。",
                    $"Restored and verified {plan.Values.Count} loader variables for '{slot.Name}'. Restart the game to verify.");
                if (hotReload.IsChecked == true)
                    resultText += "\n" + await HotReloadLoaderAsync(userIni);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
                or DecoderFallbackException or ArgumentException or NotSupportedException)
            {
                await ShowMessageAsync(ex.Message, T("PersistentSlots.RestoreFailedCheckBackups"));
            }
            finally
            {
                _restoringPersistentRuntime = false;
                PersistentSlotComboBox.IsEnabled = true;
                PersistentUserIniTextBox.IsEnabled = true;
                RefreshPersistentSlotControls();
                RefreshConfigurationProfiles();
                if (resultText is not null) PersistentSlotStatusTextBlock.Text = resultText;
            }
        }
        catch (Exception ex)
        {
            ReportUiActionFailure("OnRestorePersistentRuntimeClicked", ex);
        }
    }

    private async void OnDeletePersistentSlotClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_restoringPersistentRuntime) return;
            ModPersistentSlot? slot = GetSelectedPersistentSlot();
            if (slot is null || !await ShowConfirmAsync(
                T("PersistentSlots.DeleteSlotModINIsWillNot", slot.Name),
                T("PersistentSlots.DeleteStateSlot"))) return;

            _modPersistentSlots.Remove(slot);
            _selectedModPersistentSlotId = null;
            SaveShellConfig();
            RefreshPersistentSlotControls();
        }
        catch (Exception ex)
        {
            ReportUiActionFailure("OnDeletePersistentSlotClicked", ex);
        }
    }
}
