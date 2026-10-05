using System.Text;
using IntegratedModManager.Core;
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
        PersistentSlotTitleTextBlock.Text = L("Mod 状态槽位", "Mod State Slots");
        PersistentSlotHintTextBlock.Text = L(
            "游戏中按 F10 后保存槽位。恢复时先退出游戏和加载器，再定向写入 d3dx_user.ini；不会自动热重载。",
            "Save after F10. To restore, close the game and loader first; only this slot's d3dx_user.ini keys are written. No automatic hot reload.");
        BrowsePersistentUserIniButton.Content = L("选择文件", "Browse");
        CapturePersistentSlotButton.Content = L("保存新槽位", "Save Slot");
        RestorePersistentRuntimeButton.Content = L("恢复到加载器", "Restore Loader State");
        DeletePersistentSlotButton.Content = L("删除槽位", "Delete Slot");
        OpenCombinationPresetsButton.Content = L("打开组合预设工作区", "Open Combination Workspace");
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
            foreach (ModPersistentSlot slot in slots)
            {
                PersistentSlotComboBox.Items.Add(new ComboBoxItem
                {
                    Content = $"{slot.Name} · {slot.Values.Count}",
                    Tag = slot.Id
                });
            }

            PersistentSlotComboBox.SelectedItem = PersistentSlotComboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Tag as string, _selectedModPersistentSlotId, StringComparison.Ordinal));
            PersistentSlotComboBox.PlaceholderText = L("没有状态槽位", "No saved state slots");
        }
        finally
        {
            _refreshingPersistentSlots = false;
        }

        BrowsePersistentUserIniButton.IsEnabled = repository is not null && !_restoringPersistentRuntime && !_applyingCombinationProfile;
        CapturePersistentSlotButton.IsEnabled = relative is not null && !_restoringPersistentRuntime && !_applyingCombinationProfile;
        DeletePersistentSlotButton.IsEnabled = relative is not null && _selectedModPersistentSlotId is not null && !_restoringPersistentRuntime && !_applyingCombinationProfile;
        RestorePersistentRuntimeButton.IsEnabled = DeletePersistentSlotButton.IsEnabled && !_restoringPersistentRuntime;
        RefreshPersistentSlotValuePreview();
        PersistentSlotStatusTextBlock.Text = relative is null
            ? L("选择一个 Mod 后管理其状态槽位。", "Select a Mod to manage its state slots.")
            : L($"当前 Mod：{Path.GetFileName(modPath)} · {slots.Count} 个槽位",
                $"Current Mod: {Path.GetFileName(modPath)} · {slots.Count} slot(s)");
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
            ? L("保存后将在这里显示该槽位的 INI、变量与数值。", "Saved INI paths, variables, and values will appear here.")
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
        if (_restoringPersistentRuntime || _applyingCombinationProfile) return;
        string? path = await PickFileAsync([".ini"]);
        if (path is null) return;
        if (!string.Equals(Path.GetFileName(path), "d3dx_user.ini", StringComparison.OrdinalIgnoreCase))
        {
            await ShowMessageAsync(L("请选择 d3dx_user.ini。", "Select d3dx_user.ini."),
                L("文件不匹配", "Wrong File"));
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

    private async void OnCapturePersistentSlotClicked(object sender, RoutedEventArgs e)
    {
        if (_restoringPersistentRuntime || _applyingCombinationProfile) return;
        WorkspaceRepository? repository = GetSelectedRepository();
        string? modPath = _currentSecondLevelPath;
        string? relative = TryGetCurrentModRelativePath(repository, modPath);
        if (repository is null || modPath is null || relative is null) return;

        string userIni = PersistentUserIniTextBox.Text.Trim();
        if (!string.Equals(Path.GetFileName(userIni), "d3dx_user.ini", StringComparison.OrdinalIgnoreCase))
        {
            await ShowMessageAsync(L("请选择加载器的 d3dx_user.ini。", "Select the loader's d3dx_user.ini."),
                L("无法捕获", "Cannot Capture"));
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
            await ShowMessageAsync(L("当前 Mod 尚未以同名目录部署到目标文件夹。", "This Mod is not deployed under the same folder name."),
                L("无法捕获", "Cannot Capture"));
            return;
        }

        IReadOnlyList<ModPersistentValue> values;
        try
        {
            values = await Task.Run(() => ModPersistentPresetEngine.Capture(modPath, deployedName, userIni));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or DecoderFallbackException or ArgumentException)
        {
            await ShowMessageAsync(ex.Message, L("捕获失败", "Capture Failed"));
            return;
        }

        if (values.Count == 0)
        {
            await ShowMessageAsync(L("未找到与此 Mod 声明精确匹配的数值型持久变量。请在游戏里按 F10 后确认文件已更新。",
                "No numeric persistent values matched this Mod. Press F10 in game and confirm the file was updated."),
                L("没有可保存状态", "No Supported State"));
            return;
        }

        string? name = await PromptForTextAsync(
            L($"找到 {values.Count} 个变量。输入此状态槽位的名称。", $"Found {values.Count} variables. Name this slot."),
            L("保存内部状态", "Save Internal State"),
            L("状态 1", "State 1"));
        name = name?.Trim();
        if (string.IsNullOrWhiteSpace(name)) return;

        var slot = new ModPersistentSlot
        {
            RepositoryId = repository.Id,
            ModRelativePath = relative,
            Name = name,
            Values = [.. values]
        };
        _modPersistentSlots.Add(slot);
        _selectedModPersistentSlotId = slot.Id;
        repository.PersistentUserIniPath = userIni;
        SaveShellConfig();
        RefreshPersistentSlotControls();
        ShowAppNotification($"已保存 {values.Count} 个变量到“{name}”。这是离线快照，尚未验证游戏内效果。",
            $"Saved {values.Count} variables to '{name}'. This is an offline snapshot; in-game behavior is not verified.");
    }

    private async void OnRestorePersistentRuntimeClicked(object sender, RoutedEventArgs e)
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
                throw new InvalidDataException(L("请先以同名目录部署当前 Mod，再恢复加载器状态。", "Deploy this Mod under its matching folder name before restoring."));
            ModPersistentRuntimeRestorePlan plan = await Task.Run(() =>
                ModPersistentPresetEngine.PlanRuntimeRestore(modPath, deployedName, userIni, slot.Values));
            var closedCheck = new CheckBox
            {
                Content = L("我已完全退出游戏及 XXMI/EFMI 加载器", "I have fully closed the game and XXMI/EFMI loader")
            };
            var content = new StackPanel { Spacing = 12 };
            content.Children.Add(new TextBlock
            {
                Text = L($"槽位：{slot.Name}\n文件：{plan.Edit.Path}\n修改 {plan.ChangedCount} 项，补入 {plan.AddedCount} 项，已一致 {plan.UnchangedCount} 项。\n\n仅处理此 Mod 的完整运行键；其他 Mod 的数据不变。写入前保留 .bak 备份，之后逐项重新读取核对。不会修改 Mod INI，也不会向运行中的游戏推送状态。",
                    $"Slot: {slot.Name}\nFile: {plan.Edit.Path}\nChange {plan.ChangedCount}, add {plan.AddedCount}, already equal {plan.UnchangedCount}.\n\nOnly this Mod's full runtime keys will be restored. Other Mod data stays unchanged. A .bak is kept before writing, then values are read back and checked. Mod INIs and the running game are not modified."),
                TextWrapping = TextWrapping.Wrap
            });
            content.Children.Add(closedCheck);
            var dialog = new ContentDialog
            {
                XamlRoot = RootGrid.XamlRoot,
                Title = L("恢复到加载器（实验）", "Restore Loader State (Experimental)"),
                Content = content,
                PrimaryButtonText = L("备份并恢复", "Back Up and Restore"),
                CloseButtonText = L("取消", "Cancel"),
                IsPrimaryButtonEnabled = false,
                DefaultButton = ContentDialogButton.Close
            };
            closedCheck.Checked += (_, _) => dialog.IsPrimaryButtonEnabled = true;
            closedCheck.Unchecked += (_, _) => dialog.IsPrimaryButtonEnabled = false;
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            string? backup = await Task.Run(() => ModPersistentPresetEngine.ApplyRuntimeRestore(plan));
            repository.PersistentUserIniPath = userIni;
            SaveShellConfig();
            resultText = L($"“{slot.Name}”已逐项核对：修改 {plan.ChangedCount}，补入 {plan.AddedCount}，已一致 {plan.UnchangedCount}。\n"
                + (backup is null ? "无需写入，没有创建新备份。" : $"备份：{backup}") + "\n请重新启动加载器和游戏验证效果。",
                $"'{slot.Name}' verified: changed {plan.ChangedCount}, added {plan.AddedCount}, already equal {plan.UnchangedCount}.\n"
                + (backup is null ? "No write was needed; no new backup created." : $"Backup: {backup}") + "\nRestart the loader and game to verify the result.");
            ShowAppNotification($"已恢复“{slot.Name}”的 {plan.Values.Count} 个加载器变量，并重新读取核对。请重启游戏验证效果。",
                $"Restored and verified {plan.Values.Count} loader variables for '{slot.Name}'. Restart the game to verify.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
            or DecoderFallbackException or ArgumentException or NotSupportedException)
        {
            await ShowMessageAsync(ex.Message, L("恢复失败，请检查备份", "Restore Failed; Check Backups"));
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

    private async void OnDeletePersistentSlotClicked(object sender, RoutedEventArgs e)
    {
        if (_restoringPersistentRuntime) return;
        ModPersistentSlot? slot = GetSelectedPersistentSlot();
        if (slot is null || !await ShowConfirmAsync(
            L($"删除状态槽位“{slot.Name}”？Mod INI 不会被修改。", $"Delete slot '{slot.Name}'? Mod INIs will not change."),
            L("删除状态槽位", "Delete State Slot"))) return;

        _modPersistentSlots.Remove(slot);
        _selectedModPersistentSlotId = null;
        SaveShellConfig();
        RefreshPersistentSlotControls();
    }
}
