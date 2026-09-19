using System.Diagnostics;
using System.Runtime.InteropServices;
using IntegratedModManager.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private const byte VirtualKeyF10 = 0x79;
    private const uint KeyEventKeyUp = 0x0002;
    private readonly string _modStateBackupPath = Path.Combine(AppContext.BaseDirectory, "backups", "mod-states");
    private readonly string _modStatePresetPath = Path.Combine(AppContext.BaseDirectory, "presets", "mod-states");

    [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")]
    private static extern IntPtr GetHotReloadForegroundWindow();

    [DllImport("user32.dll", EntryPoint = "keybd_event")]
    private static extern void SendHotReloadKey(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);

    private IEnumerable<ModStatePreset> GetCurrentRepositoryModStatePresets()
    {
        return _modStatePresets
            .Where(profile => string.Equals(profile.RepositoryId, _selectedRepositoryId, StringComparison.Ordinal))
            .OrderBy(profile => profile.Name, StringComparer.CurrentCultureIgnoreCase);
    }

    private ModStatePreset? GetSelectedModStatePreset()
    {
        return _modStatePresets.FirstOrDefault(profile =>
            string.Equals(profile.Id, _selectedModStatePresetId, StringComparison.Ordinal)
            && string.Equals(profile.RepositoryId, _selectedRepositoryId, StringComparison.Ordinal));
    }

    private void RefreshModStatePresetSection()
    {
        if (ModStatePresetComboBox is null)
        {
            return;
        }

        ModStatePresetsTitleTextBlock.Text = L("跨 Mod 内部状态预设", "Cross-Mod Internal State Presets");
        ModStatePresetsHintTextBlock.Text = L(
            "实验功能：把完整 d3dx_user.ini 原样保存为跨 Mod 预设，不依赖任何 Mod 的快捷键或变量写法。恢复前必须关闭游戏和 XXMI，程序会先校验快照并备份当前文件。",
            "Experimental: preserve the complete d3dx_user.ini as a cross-mod preset without depending on any mod's shortcuts or variable layout. Close the game and XXMI before restoring; the snapshot is verified and the current file is backed up first.");
        ModStateTestBadgeTextBlock.Text = L("3.8.1 热注入测试", "3.8.1 HOT INJECTION LAB");
        D3dxUserIniLabelTextBlock.Text = "d3dx_user.ini";
        D3dxUserIniPathTextBox.PlaceholderText = L("选择或自动定位 3DMigoto 用户状态文件", "Select or detect the 3DMigoto user state file");
        D3dxUserIniPathTextBox.Text = _d3dxUserIniPath ?? string.Empty;
        AutoDetectD3dxUserIniButton.Content = L("自动定位", "Auto Detect");
        SelectD3dxUserIniButton.Content = L("选择文件", "Choose File");
        ModStatePresetLabelTextBlock.Text = L("内部状态预设", "Internal state preset");
        SaveModStatePresetButton.Content = L("保存当前状态", "Save Current State");
        ApplyModStatePresetButton.Content = L("热注入并重载", "Hot Inject & Reload");
        DeleteModStatePresetButton.Content = new FontIcon { Glyph = "\uE74D", FontSize = 16 };
        ToolTipService.SetToolTip(DeleteModStatePresetButton, L("删除内部状态预设", "Delete internal state preset"));

        List<ModStatePreset> profiles = [.. GetCurrentRepositoryModStatePresets()];
        if (!profiles.Any(profile => profile.Id == _selectedModStatePresetId))
        {
            _selectedModStatePresetId = profiles.FirstOrDefault()?.Id;
        }

        _isApplyingModStatePresetSelection = true;
        try
        {
            ModStatePresetComboBox.Items.Clear();
            foreach (ModStatePreset profile in profiles)
            {
                ModStatePresetComboBox.Items.Add(new ComboBoxItem
                {
                    Content = $"{profile.Name}  ·  {profile.IncludedModFolderNames.Count} Mod / {FormatFileSize(profile.SnapshotSizeBytes)}",
                    Tag = profile.Id
                });
            }

            ModStatePresetComboBox.PlaceholderText = profiles.Count == 0
                ? L("当前仓库还没有内部状态预设", "No internal state presets for this repository")
                : L("选择内部状态预设", "Select an internal state preset");
            ModStatePresetComboBox.SelectedItem = ModStatePresetComboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Tag as string, _selectedModStatePresetId, StringComparison.Ordinal));
        }
        finally
        {
            _isApplyingModStatePresetSelection = false;
        }

        ModStatePreset? selected = GetSelectedModStatePreset();
        bool canScan = GetSelectedRepository() is WorkspaceRepository repository
            && Directory.Exists(repository.TargetPath)
            && File.Exists(_d3dxUserIniPath);
        SaveModStatePresetButton.IsEnabled = canScan;
        ApplyModStatePresetButton.IsEnabled = selected is not null
            && File.Exists(_d3dxUserIniPath)
            && TryGetPresetSnapshotPath(selected, out string? snapshotPath)
            && File.Exists(snapshotPath);
        DeleteModStatePresetButton.IsEnabled = selected is not null;

        if (selected is null)
        {
            ModStatePresetSummaryTextBlock.Text = L(
                "先运行一次游戏并让 3DMigoto 保存状态，再选择 d3dx_user.ini。保存时会完整复制该文件，不读取或推断 Mod 快捷键。",
                "Run the game once so 3DMigoto saves its state, then select d3dx_user.ini. Saving copies the entire file without reading or inferring mod shortcuts.");
            return;
        }

        ModStatePresetSummaryTextBlock.Text = L(
            $"{selected.Name}：完整状态文件 {FormatFileSize(selected.SnapshotSizeBytes)}，保存时目标文件夹包含 {selected.IncludedModFolderNames.Count} 个 Mod，更新于 {selected.UpdatedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}。",
            $"{selected.Name}: complete state file {FormatFileSize(selected.SnapshotSizeBytes)}, with {selected.IncludedModFolderNames.Count} mods in the target folder when saved; updated {selected.UpdatedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}.");
    }

    private async void OnSelectD3dxUserIniClicked(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.Desktop
        };
        picker.FileTypeFilter.Add(".ini");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        Windows.Storage.StorageFile? file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        if (!string.Equals(file.Name, "d3dx_user.ini", StringComparison.OrdinalIgnoreCase))
        {
            await ShowMessageAsync(
                L("请选择名为 d3dx_user.ini 的 3DMigoto 用户状态文件。", "Choose the 3DMigoto user state file named d3dx_user.ini."),
                L("文件不匹配", "Unexpected File"));
            return;
        }

        _d3dxUserIniPath = file.Path;
        SaveShellConfig();
        RefreshModStatePresetSection();
    }

    private async void OnAutoDetectD3dxUserIniClicked(object sender, RoutedEventArgs e)
    {
        WorkspaceRepository? repository = GetSelectedRepository();
        string? detected = repository is null ? null : DetectD3dxUserIni(repository);
        if (string.IsNullOrWhiteSpace(detected))
        {
            await ShowMessageAsync(
                L("没有在目标文件夹或启动器附近找到 d3dx_user.ini。请先运行一次游戏并保存状态，或手动选择文件。", "d3dx_user.ini was not found near the target folder or launcher. Run the game once so it saves state, or choose the file manually."),
                L("未找到状态文件", "State File Not Found"));
            return;
        }

        _d3dxUserIniPath = detected;
        SaveShellConfig();
        RefreshModStatePresetSection();
        ShowAppNotification("已定位 d3dx_user.ini。", "d3dx_user.ini detected.");
    }

    private static string? DetectD3dxUserIni(WorkspaceRepository repository)
    {
        var candidates = new List<string>();
        AddCandidateDirectory(candidates, repository.TargetPath);
        AddCandidateDirectory(candidates, Path.GetDirectoryName(repository.TargetPath));
        AddCandidateDirectory(candidates, Path.GetDirectoryName(Path.GetDirectoryName(repository.TargetPath)));
        AddCandidateDirectory(candidates, Path.GetDirectoryName(repository.LauncherPath));
        AddCandidateDirectory(candidates, Path.GetDirectoryName(Path.GetDirectoryName(repository.LauncherPath)));

        return candidates
            .Select(directory => Path.Combine(directory, "d3dx_user.ini"))
            .FirstOrDefault(File.Exists);
    }

    private static void AddCandidateDirectory(List<string> candidates, string? directory)
    {
        if (!string.IsNullOrWhiteSpace(directory)
            && Directory.Exists(directory)
            && !candidates.Contains(directory, StringComparer.OrdinalIgnoreCase))
        {
            candidates.Add(directory);
        }
    }

    private async void OnSaveModStatePresetClicked(object sender, RoutedEventArgs e)
    {
        WorkspaceRepository? repository = GetSelectedRepository();
        string userStatePath = _d3dxUserIniPath ?? string.Empty;
        if (repository is null || !Directory.Exists(repository.TargetPath) || !File.Exists(userStatePath))
        {
            await ShowMessageAsync(
                L("仓库目标文件夹或 d3dx_user.ini 无效。", "The repository target folder or d3dx_user.ini is invalid."),
                L("无法保存状态", "Cannot Save State"));
            return;
        }

        byte[] stateFile;
        try
        {
            stateFile = await File.ReadAllBytesAsync(userStatePath);
        }
        catch (Exception ex)
        {
            await ShowMessageAsync(
                L("读取完整状态文件失败：", "Failed to read the complete state file: ") + ex.Message,
                L("读取失败", "Read Failed"));
            return;
        }

        if (stateFile.Length == 0)
        {
            await ShowMessageAsync(
                L("d3dx_user.ini 是空文件。请进入游戏设置好各个 Mod，按 F10 或正常退出后再保存。", "d3dx_user.ini is empty. Configure the mods in game, press F10 or exit normally, then save again."),
                L("状态文件为空", "State File Is Empty"));
            return;
        }

        List<string> includedMods = Directory.EnumerateDirectories(repository.TargetPath)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        string? name = await PromptForTextAsync(
            L($"将完整保存 {FormatFileSize(stateFile.LongLength)} 的 d3dx_user.ini；当前目标文件夹有 {includedMods.Count} 个 Mod。输入预设名称。", $"The complete {FormatFileSize(stateFile.LongLength)} d3dx_user.ini will be saved; the target folder currently contains {includedMods.Count} mods. Enter a preset name."),
            L("保存跨 Mod 内部状态", "Save Cross-Mod Internal State"),
            L("常用内部状态", "My Internal State"));
        name = name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var preset = new ModStatePreset
        {
            RepositoryId = repository.Id,
            Name = name,
            D3dxUserIniPath = userStatePath,
            SnapshotFileName = $"{Guid.NewGuid():N}.d3dx_user.ini",
            SnapshotSha256 = CompleteFileSnapshot.ComputeSha256(stateFile),
            SnapshotSizeBytes = stateFile.LongLength,
            IncludedModFolderNames = includedMods,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        try
        {
            Directory.CreateDirectory(_modStatePresetPath);
            if (!TryGetPresetSnapshotPath(preset, out string? snapshotPath))
            {
                throw new InvalidDataException("The preset snapshot name is invalid.");
            }

            CompleteFileSnapshot.WriteAtomically(snapshotPath!, stateFile);
            _modStatePresets.Add(preset);
            _selectedModStatePresetId = preset.Id;
            SaveShellConfig();
            RefreshModStatePresetSection();
            ShowAppNotification($"已完整保存预设文件：{name}", $"Saved the complete preset file: {name}");
        }
        catch (Exception ex)
        {
            if (TryGetPresetSnapshotPath(preset, out string? failedSnapshotPath) && File.Exists(failedSnapshotPath))
            {
                File.Delete(failedSnapshotPath);
            }

            await ShowMessageAsync(
                L("保存完整预设文件失败：", "Failed to save the complete preset file: ") + ex.Message,
                L("保存失败", "Save Failed"));
        }
    }

    private void OnModStatePresetSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isApplyingModStatePresetSelection)
        {
            return;
        }

        _selectedModStatePresetId = (ModStatePresetComboBox.SelectedItem as ComboBoxItem)?.Tag as string;
        ModStatePreset? selected = GetSelectedModStatePreset();
        if (selected is not null && File.Exists(selected.D3dxUserIniPath))
        {
            _d3dxUserIniPath = selected.D3dxUserIniPath;
        }

        SaveShellConfig();
        RefreshModStatePresetSection();
    }

    private async void OnDeleteModStatePresetClicked(object sender, RoutedEventArgs e)
    {
        ModStatePreset? preset = GetSelectedModStatePreset();
        if (preset is null)
        {
            return;
        }

        if (!await ShowConfirmAsync(
            L($"确定删除内部状态预设“{preset.Name}”吗？这不会修改游戏文件。", $"Delete the internal state preset \"{preset.Name}\"? This will not modify game files."),
            L("删除内部状态预设", "Delete Internal State Preset")))
        {
            return;
        }

        if (TryGetPresetSnapshotPath(preset, out string? snapshotPath) && File.Exists(snapshotPath))
        {
            File.Delete(snapshotPath);
        }

        _modStatePresets.Remove(preset);
        _selectedModStatePresetId = null;
        SaveShellConfig();
        RefreshModStatePresetSection();
    }

    private async void OnApplyModStatePresetClicked(object sender, RoutedEventArgs e)
    {
        ModStatePreset? preset = GetSelectedModStatePreset();
        WorkspaceRepository? repository = GetSelectedRepository();
        string userStatePath = _d3dxUserIniPath ?? string.Empty;
        if (preset is null || repository is null || !File.Exists(userStatePath))
        {
            await ShowMessageAsync(
                L("预设、仓库或 d3dx_user.ini 无效。", "The preset, repository, or d3dx_user.ini is invalid."),
                L("无法恢复状态", "Cannot Restore State"));
            return;
        }

        if (!TryGetPresetSnapshotPath(preset, out string? snapshotPath) || !File.Exists(snapshotPath))
        {
            await ShowMessageAsync(
                L("这个预设的完整状态快照不存在，无法恢复。", "The complete state snapshot for this preset is missing and cannot be restored."),
                L("预设文件缺失", "Preset File Missing"));
            return;
        }

        string? activeProcess = FindActiveModRuntimeProcess(repository);
        string runtimeHint = string.IsNullOrWhiteSpace(activeProcess)
            ? L("没有识别到 XXMI/3DMigoto 进程；仍可注入，但可能需要手动回到游戏按 F10。", "No XXMI/3DMigoto process was detected. Injection is still possible, but you may need to return to the game and press F10 manually.")
            : L($"已检测到相关进程：{activeProcess}。", $"Detected related process: {activeProcess}.");
        if (!await ShowConfirmAsync(
            L($"把“{preset.Name}”的完整 d3dx_user.ini（{FormatFileSize(preset.SnapshotSizeBytes)}）热注入到当前环境？\n\n{runtimeHint}\n\n确认后程序会先备份当前文件并写入快照，然后倒计时 3 秒。请在倒计时期间切回游戏，程序会向当前前台窗口发送 F10 请求 3DMigoto 重载。", $"Hot-inject the complete d3dx_user.ini ({FormatFileSize(preset.SnapshotSizeBytes)}) from \"{preset.Name}\" into the current environment?\n\n{runtimeHint}\n\nAfter confirmation, the current file is backed up and the snapshot written, followed by a 3-second countdown. Switch back to the game during the countdown; the app will send F10 to the foreground window to request a 3DMigoto reload."),
            L("热注入完整状态", "Hot Inject Complete State")))
        {
            return;
        }

        try
        {
            byte[] snapshot = await File.ReadAllBytesAsync(snapshotPath!);
            CompleteFileSnapshot.Validate(snapshot, preset.SnapshotSizeBytes, preset.SnapshotSha256);
            byte[] original = await File.ReadAllBytesAsync(userStatePath);

            Directory.CreateDirectory(_modStateBackupPath);
            string backupPath = Path.Combine(
                _modStateBackupPath,
                $"d3dx_user-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.ini");
            File.Copy(userStatePath, backupPath, overwrite: false);
            try
            {
                CompleteFileSnapshot.WriteAtomically(userStatePath, snapshot);
            }
            catch
            {
                CompleteFileSnapshot.WriteAtomically(userStatePath, original);
                throw;
            }

            ShowAppNotification(
                $"已注入状态：{preset.Name}。请在 3 秒内切回游戏。",
                $"State injected: {preset.Name}. Switch to the game within 3 seconds.");

            for (int seconds = 3; seconds >= 1; seconds--)
            {
                ModStatePresetSummaryTextBlock.Text = L(
                    $"完整状态已注入，{seconds} 秒后发送 F10。请立即切回游戏。",
                    $"Complete state injected. F10 will be sent in {seconds} second(s). Switch to the game now.");
                await Task.Delay(1000);
            }

            IntPtr foregroundWindow = GetHotReloadForegroundWindow();
            IntPtr managerWindow = WindowNative.GetWindowHandle(this);
            if (foregroundWindow != IntPtr.Zero && foregroundWindow != managerWindow)
            {
                SendHotReloadKey(VirtualKeyF10, 0, 0, UIntPtr.Zero);
                SendHotReloadKey(VirtualKeyF10, 0, KeyEventKeyUp, UIntPtr.Zero);
                ModStatePresetSummaryTextBlock.Text = L(
                    $"已热注入“{preset.Name}”并向前台窗口发送 F10。原文件备份：{backupPath}",
                    $"Hot-injected \"{preset.Name}\" and sent F10 to the foreground window. Original file backup: {backupPath}");
            }
            else
            {
                ModStatePresetSummaryTextBlock.Text = L(
                    $"已热注入“{preset.Name}”，但没有向管理器窗口发送 F10。请切回游戏手动按 F10。原文件备份：{backupPath}",
                    $"Hot-injected \"{preset.Name}\", but F10 was not sent to the manager window. Return to the game and press F10 manually. Original file backup: {backupPath}");
            }
        }
        catch (Exception ex)
        {
            await ShowMessageAsync(
                L("热注入失败，程序已尝试恢复注入前的状态文件：", "Hot injection failed. The app attempted to restore the state file from before injection: ") + ex.Message,
                L("热注入失败", "Hot Injection Failed"));
        }
    }

    private static string? FindActiveModRuntimeProcess(WorkspaceRepository repository)
    {
        string configuredLauncher = Path.GetFileNameWithoutExtension(repository.LauncherPath);
        string[] knownFragments = ["xxmi", "efmi", "3dmigoto", "3dmloader"];
        foreach (Process process in Process.GetProcesses())
        {
            try
            {
                string name = process.ProcessName;
                if ((!string.IsNullOrWhiteSpace(configuredLauncher)
                        && name.Equals(configuredLauncher, StringComparison.OrdinalIgnoreCase))
                    || knownFragments.Any(fragment => name.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
                {
                    return name;
                }
            }
            catch
            {
                // Some elevated or exiting processes cannot be inspected.
            }
            finally
            {
                process.Dispose();
            }
        }

        return null;
    }

    private bool TryGetPresetSnapshotPath(ModStatePreset preset, out string? snapshotPath)
    {
        snapshotPath = null;
        if (string.IsNullOrWhiteSpace(preset.SnapshotFileName)
            || !string.Equals(Path.GetFileName(preset.SnapshotFileName), preset.SnapshotFileName, StringComparison.Ordinal))
        {
            return false;
        }

        string root = Path.GetFullPath(_modStatePresetPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string candidate = Path.GetFullPath(Path.Combine(root, preset.SnapshotFileName));
        if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        snapshotPath = candidate;
        return true;
    }

}
