using System.Diagnostics;
using System.Text;
using IntegratedModManager.Core;
using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private const string PersistentStateBridgeFileName = "ZZZ_IntegratedModManager_StateBridge.ini";
    private const string PersistentStateBackupDirectoryName = "IntegratedModManager.StateBackups";
    private readonly List<RememberedModPersistentState> _rememberedModStates = [];
    private string? _persistentUserConfigPath;

    private void RefreshPersistentModStateSection()
    {
        if (PersistentModStateTitleTextBlock is null)
        {
            return;
        }

        PersistentModStateTitleTextBlock.Text = L("跨 Mod 内部状态记忆", "Cross-Mod Internal State Memory");
        PersistentModStateHintTextBlock.Text = L(
            "v3.8.1 实验功能：保存时等待 d3dx_user.ini 稳定后直接读取；离线恢复在游戏关闭时精确合并状态并启动 XXMI；运行中热恢复使用临时 post 桥接，由用户手动按两次 F10。不修改原始 Mod INI。",
            "v3.8.1 lab: capture waits for a stable d3dx_user.ini and reads it directly; offline restore precisely merges state while the game is closed and starts XXMI; runtime hot restore uses a temporary post bridge with two user-initiated F10 reloads. Original Mod INIs are not modified.");
        PersistentModStateBadgeTextBlock.Text = "v3.8.1 · PERSISTENT STATE LAB";
        PersistentUserConfigLabelTextBlock.Text = "d3dx_user.ini";
        PersistentUserConfigPathTextBox.PlaceholderText = L("选择或自动定位 3DMigoto 用户配置", "Choose or detect the 3DMigoto user config");
        PersistentUserConfigPathTextBox.Text = _persistentUserConfigPath ?? string.Empty;
        DetectPersistentUserConfigButton.Content = L("自动定位", "Auto Detect");
        ChoosePersistentUserConfigButton.Content = L("选择文件", "Choose File");
        CaptureSelectedModStateButton.Content = L("记住选中 Mod（无 F10）", "Remember Selected Mod (No F10)");
        OfflineApplySelectedModStateButton.Content = L("离线恢复选中 Mod 并启动", "Offline Restore Selected & Launch");
        HotApplySelectedModStateButton.Content = L("运行中热恢复选中 Mod", "Hot Restore Selected at Runtime");
        CaptureProfileStatesButton.Content = L("保存当前方案状态（无 F10）", "Save Current Profile States (No F10)");
        OfflineApplyProfileStatesButton.Content = L("离线恢复方案并启动", "Offline Restore Profile & Launch");
        HotApplyProfileStatesButton.Content = L("运行中热恢复方案", "Hot Restore Profile at Runtime");

        WorkspaceRepository? repository = GetSelectedRepository();
        SecondLevelFolderItem? selectedMod = GetSelectedSecondLevelItem();
        RememberedModPersistentState? remembered = repository is null || selectedMod is null
            ? null
            : FindRememberedState(repository.Id, GetSourceRelativePath(repository, selectedMod.Path));
        ModConfigurationProfile? profile = GetSelectedConfigurationProfile();
        bool hasUserConfig = File.Exists(_persistentUserConfigPath);
        bool hasLauncher = repository is not null && File.Exists(repository.LauncherPath);
        bool selectedInstalled = repository is not null
            && selectedMod is not null
            && Directory.Exists(Path.Combine(repository.TargetPath, Path.GetFileName(selectedMod.Path)));
        bool hasSelectedState = remembered is { Values.Count: > 0 } && selectedInstalled;
        bool hasProfileState = profile?.PersistentStates?.Any(state => state.Values?.Count > 0) == true;

        CaptureSelectedModStateButton.IsEnabled = hasUserConfig && selectedInstalled;
        OfflineApplySelectedModStateButton.IsEnabled = hasUserConfig && hasLauncher && hasSelectedState;
        HotApplySelectedModStateButton.IsEnabled = hasUserConfig && hasSelectedState;
        CaptureProfileStatesButton.IsEnabled = hasUserConfig && profile is not null;
        OfflineApplyProfileStatesButton.IsEnabled = hasUserConfig && hasLauncher && hasProfileState;
        HotApplyProfileStatesButton.IsEnabled = hasUserConfig && hasProfileState;

        int repositoryStateCount = repository is null
            ? 0
            : _rememberedModStates.Count(state => string.Equals(state.RepositoryId, repository.Id, StringComparison.Ordinal));
        string selectedSummary = remembered is null
            ? L("选中的 Mod 还没有状态记录。", "The selected Mod has no remembered state yet.")
            : L($"选中 Mod 已记住 {remembered.Values.Count} 个持久变量，更新于 {remembered.UpdatedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}。", $"The selected Mod remembers {remembered.Values.Count} persistent variables, updated {remembered.UpdatedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}.");
        PersistentModStateStatusTextBlock.Text = L($"当前仓库已记录 {repositoryStateCount} 个 Mod。{selectedSummary} 保存只读取已稳定的配置文件，不按 F10。", $"This repository remembers {repositoryStateCount} Mods. {selectedSummary} Capture only reads the stable config file and does not press F10.");
    }

    private async void OnChoosePersistentUserConfigClicked(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.Desktop };
        picker.FileTypeFilter.Add(".ini");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        Windows.Storage.StorageFile? file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        if (!string.Equals(file.Name, "d3dx_user.ini", StringComparison.OrdinalIgnoreCase))
        {
            await ShowMessageAsync(L("请选择名为 d3dx_user.ini 的 3DMigoto 用户配置文件。", "Choose the 3DMigoto user config named d3dx_user.ini."), L("文件不匹配", "Unexpected File"));
            return;
        }

        _persistentUserConfigPath = file.Path;
        SaveShellConfig();
        RefreshPersistentModStateSection();
    }

    private async void OnDetectPersistentUserConfigClicked(object sender, RoutedEventArgs e)
    {
        WorkspaceRepository? repository = GetSelectedRepository();
        string? detected = repository is null ? null : DetectPersistentUserConfig(repository);
        if (detected is null)
        {
            await ShowMessageAsync(L("没有在目标 Mods 文件夹同级或启动器附近找到 d3dx_user.ini。请先通过 XXMI 启动一次游戏，或手动选择文件。", "d3dx_user.ini was not found beside the target Mods folder or near the launcher. Start the game through XXMI once, or choose the file manually."), L("未找到用户配置", "User Config Not Found"));
            return;
        }

        _persistentUserConfigPath = detected;
        SaveShellConfig();
        RefreshPersistentModStateSection();
        ShowAppNotification("已定位 d3dx_user.ini。", "d3dx_user.ini detected.");
    }

    private async void OnCaptureSelectedModStateClicked(object sender, RoutedEventArgs e)
    {
        WorkspaceRepository? repository = GetSelectedRepository();
        SecondLevelFolderItem? item = GetSelectedSecondLevelItem();
        if (repository is null || item is null || !File.Exists(_persistentUserConfigPath))
        {
            return;
        }

        string targetModPath = Path.Combine(repository.TargetPath, Path.GetFileName(item.Path));
        if (!Directory.Exists(targetModPath))
        {
            await ShowMessageAsync(L("请先启用选中的 Mod。", "Enable the selected Mod first."), L("无法捕获状态", "Cannot Capture State"));
            return;
        }

        try
        {
            string userConfig = await ReadStableUserConfigAsync(TimeSpan.FromSeconds(5));
            RememberedModPersistentState state = CaptureModState(repository, item.Path, targetModPath, userConfig);
            UpsertRememberedState(state);
            SaveShellConfig();
            RefreshPersistentModStateSection();
            ShowAppNotification($"已记住 {item.Name} 的 {state.Values.Count} 个持久变量，未发送 F10。", $"Remembered {state.Values.Count} persistent variables for {item.Name} without sending F10.");
        }
        catch (Exception ex)
        {
            await ShowMessageAsync(L("捕获持久状态失败：", "Failed to capture persistent state: ") + ex.Message, L("捕获失败", "Capture Failed"));
        }
    }

    private async void OnCaptureProfileStatesClicked(object sender, RoutedEventArgs e)
    {
        WorkspaceRepository? repository = GetSelectedRepository();
        ModConfigurationProfile? profile = GetSelectedConfigurationProfile();
        if (repository is null || profile is null || !File.Exists(_persistentUserConfigPath))
        {
            return;
        }

        try
        {
            string userConfig = await ReadStableUserConfigAsync(TimeSpan.FromSeconds(5));
            var captured = new List<RememberedModPersistentState>();
            foreach (string relativePath in profile.ModRelativePaths)
            {
                string sourcePath = Path.GetFullPath(Path.Combine(repository.SourcePath, relativePath));
                if (!IsPathInsideDirectory(sourcePath, repository.SourcePath))
                {
                    continue;
                }

                string targetPath = Path.Combine(repository.TargetPath, Path.GetFileName(sourcePath));
                if (!Directory.Exists(targetPath))
                {
                    continue;
                }

                try
                {
                    RememberedModPersistentState state = CaptureModState(repository, sourcePath, targetPath, userConfig);
                    captured.Add(state);
                    UpsertRememberedState(state);
                }
                catch (InvalidDataException)
                {
                    // Profiles may include Mods without global persist declarations.
                }
            }

            profile.PersistentStates = captured;
            profile.UpdatedAtUtc = DateTimeOffset.UtcNow;
            SaveShellConfig();
            RefreshConfigurationProfiles();
            ShowAppNotification($"已为方案“{profile.Name}”保存 {captured.Count} 个 Mod 的内部状态，未发送 F10。", $"Saved internal state for {captured.Count} Mods in profile \"{profile.Name}\" without sending F10.");
        }
        catch (Exception ex)
        {
            await ShowMessageAsync(L("保存方案内部状态失败：", "Failed to save profile internal states: ") + ex.Message, L("保存失败", "Save Failed"));
        }
    }

    private async void OnOfflineApplySelectedModStateClicked(object sender, RoutedEventArgs e)
    {
        (WorkspaceRepository? repository, RememberedModPersistentState? state, string name) = GetSelectedPersistentState();
        if (repository is not null && state is not null)
        {
            await ApplyPersistentStatesOfflineAsync(repository, [state], L($"离线恢复 {name}", $"Offline restore {name}"));
        }
    }

    private async void OnHotApplySelectedModStateClicked(object sender, RoutedEventArgs e)
    {
        (WorkspaceRepository? repository, RememberedModPersistentState? state, string name) = GetSelectedPersistentState();
        if (repository is not null && state is not null)
        {
            await ApplyPersistentStatesHotAsync(repository, [state], L($"运行中恢复 {name}", $"Runtime restore {name}"));
        }
    }

    private async void OnOfflineApplyProfileStatesClicked(object sender, RoutedEventArgs e)
    {
        WorkspaceRepository? repository = GetSelectedRepository();
        ModConfigurationProfile? profile = GetSelectedConfigurationProfile();
        if (repository is not null && profile?.PersistentStates is { Count: > 0 })
        {
            await ApplyPersistentStatesOfflineAsync(repository, profile.PersistentStates, L($"离线恢复方案“{profile.Name}”", $"Offline restore profile \"{profile.Name}\""));
        }
    }

    private async void OnHotApplyProfileStatesClicked(object sender, RoutedEventArgs e)
    {
        WorkspaceRepository? repository = GetSelectedRepository();
        ModConfigurationProfile? profile = GetSelectedConfigurationProfile();
        if (repository is not null && profile?.PersistentStates is { Count: > 0 })
        {
            await ApplyPersistentStatesHotAsync(repository, profile.PersistentStates, L($"运行中恢复方案“{profile.Name}”", $"Runtime restore profile \"{profile.Name}\""));
        }
    }

    private (WorkspaceRepository? Repository, RememberedModPersistentState? State, string Name) GetSelectedPersistentState()
    {
        WorkspaceRepository? repository = GetSelectedRepository();
        SecondLevelFolderItem? item = GetSelectedSecondLevelItem();
        if (repository is null || item is null)
        {
            return (repository, null, string.Empty);
        }

        return (repository, FindRememberedState(repository.Id, GetSourceRelativePath(repository, item.Path)), item.Name);
    }

    private RememberedModPersistentState CaptureModState(WorkspaceRepository repository, string sourcePath, string targetModPath, string userConfig)
    {
        IReadOnlyList<PersistentVariableValue> captured = ThreeDmigotoPersistentState.CaptureModState(repository.TargetPath, targetModPath, userConfig);
        if (captured.Count == 0)
        {
            throw new InvalidDataException(L("没有找到属于该 Mod 的持久变量。只有 global persist 声明会被记录；请先在游戏中调整选项，并等待 d3dx_user.ini 更新。", "No persistent variables belonging to this Mod were found. Only global persist declarations are recorded; change an option in game and wait for d3dx_user.ini to update."));
        }

        return new RememberedModPersistentState
        {
            RepositoryId = repository.Id,
            ModRelativePath = GetSourceRelativePath(repository, sourcePath),
            Values = captured.Select(value => new PersistentStateValue { Name = value.Name, Value = value.Value }).ToList(),
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private async Task ApplyPersistentStatesOfflineAsync(WorkspaceRepository repository, IEnumerable<RememberedModPersistentState> states, string actionTitle)
    {
        List<PersistentVariableValue> values = GetInstalledPersistentValues(repository, states);
        if (!ValidateRestoreInputs(repository, values, requireLauncher: true, out string error))
        {
            await ShowMessageAsync(error, L("无法离线恢复", "Cannot Restore Offline"));
            return;
        }

        List<string> runningProcesses = FindRunningTargetProcesses(repository);
        if (runningProcesses.Count > 0)
        {
            await ShowMessageAsync(L($"检测到游戏进程仍在运行：{string.Join(", ", runningProcesses)}。请先完全退出游戏，或使用“运行中热恢复”。", $"Game process is still running: {string.Join(", ", runningProcesses)}. Exit the game completely or use Runtime Hot Restore."), L("游戏正在运行", "Game Is Running"));
            return;
        }

        if (!await ShowConfirmAsync(L($"{actionTitle}\n\n将在游戏关闭状态下精确更新 {values.Count} 个 global persist 变量，保留其他配置，创建带时间戳的备份，验证后启动 XXMI。不会按 F10。", $"{actionTitle}\n\n{values.Count} global persist variables will be precisely updated while the game is closed. Other settings are preserved, a timestamped backup is created, values are verified, and XXMI is started. F10 is not pressed."), L("离线恢复并启动", "Offline Restore and Launch")))
        {
            return;
        }

        try
        {
            string current = await ReadStableUserConfigAsync(TimeSpan.FromSeconds(5));
            string merged = ThreeDmigotoPersistentState.MergeUserConfig(current, values);
            string backupPath = CreateUserConfigBackup();
            WriteTextAtomically(_persistentUserConfigPath!, merged);
            if (!await WaitForPersistedValuesAsync(values, TimeSpan.FromSeconds(2)))
            {
                throw new InvalidDataException(L("写入后的数值验证失败。已保留恢复前备份：", "Value verification failed after writing. The pre-restore backup is available at: ") + backupPath);
            }

            Process.Start(new ProcessStartInfo { FileName = repository.LauncherPath, WorkingDirectory = Path.GetDirectoryName(repository.LauncherPath) ?? AppContext.BaseDirectory, UseShellExecute = true });
            PersistentModStateStatusTextBlock.Text = L($"离线恢复完成：已验证 {values.Count} 个变量并启动 XXMI。备份：{backupPath}", $"Offline restore completed: {values.Count} variables verified and XXMI started. Backup: {backupPath}");
            ShowAppNotification("离线状态恢复完成，已启动 XXMI。", "Offline state restore completed and XXMI was started.");
        }
        catch (Exception ex)
        {
            await ShowMessageAsync(L("离线恢复失败：", "Offline restore failed: ") + ex.Message, L("离线恢复失败", "Offline Restore Failed"));
        }
    }

    private async Task ApplyPersistentStatesHotAsync(WorkspaceRepository repository, IEnumerable<RememberedModPersistentState> states, string actionTitle)
    {
        List<PersistentVariableValue> values = GetInstalledPersistentValues(repository, states);
        if (!ValidateRestoreInputs(repository, values, requireLauncher: false, out string error))
        {
            await ShowMessageAsync(error, L("无法热恢复", "Cannot Hot Restore"));
            return;
        }

        string bridgePath = Path.Combine(repository.TargetPath, PersistentStateBridgeFileName);
        if (File.Exists(bridgePath) && !ThreeDmigotoPersistentState.IsGeneratedBridge(await File.ReadAllTextAsync(bridgePath)))
        {
            await ShowMessageAsync(L($"目标目录中已有同名且不是本程序生成的文件：{bridgePath}", $"A file with the bridge name already exists and was not generated by this app: {bridgePath}"), L("桥接文件冲突", "Bridge File Conflict"));
            return;
        }

        if (!await ShowConfirmAsync(L($"{actionTitle}\n\n将为 {values.Count} 个变量创建临时 post 桥接。程序不会模拟键盘；你需要根据提示回到游戏手动按两次 F10。", $"{actionTitle}\n\nA temporary post bridge will be created for {values.Count} variables. The app will not simulate keyboard input; follow the prompts and manually press F10 twice in game."), L("运行中热恢复", "Runtime Hot Restore")))
        {
            return;
        }

        try
        {
            WriteTextAtomically(bridgePath, ThreeDmigotoPersistentState.BuildBridge(values));
            await ShowMessageAsync(L("第 1 步：桥接文件已创建。\n\n请切换到游戏，手动按一次 F10，等待重载完成，然后回到管理器点击“确定”。", "Step 1: The bridge file is ready.\n\nSwitch to the game, manually press F10 once, wait for reload to finish, then return to the manager and click OK."), L("手动执行第一次 F10", "Perform First F10 Manually"));
            File.Delete(bridgePath);
            await ShowMessageAsync(L("第 2 步：桥接文件已删除。\n\n请再次切换到游戏，手动按一次 F10，等待重载完成，然后回到管理器点击“确定”。", "Step 2: The bridge file has been removed.\n\nSwitch to the game again, manually press F10 once, wait for reload to finish, then return to the manager and click OK."), L("手动执行第二次 F10", "Perform Second F10 Manually"));
            bool verified = await WaitForPersistedValuesAsync(values, TimeSpan.FromSeconds(6));
            PersistentModStateStatusTextBlock.Text = verified
                ? L($"运行中恢复完成，已从 d3dx_user.ini 验证 {values.Count} 个变量。", $"Runtime restore completed and {values.Count} variables were verified in d3dx_user.ini.")
                : L("两次手动重载已结束，但未在 6 秒内验证全部目标值。请检查游戏效果和 F10 按键配置。", "Both manual reloads finished, but not all target values were verified within 6 seconds. Check the game result and the F10 binding.");
            ShowAppNotification(verified ? "运行中状态恢复并验证完成。" : "热恢复流程已结束，但需人工检查结果。", verified ? "Runtime state restored and verified." : "Hot restore finished, but the result needs manual inspection.");
        }
        catch (Exception ex)
        {
            await ShowMessageAsync(L("运行中恢复未完整完成：", "Runtime restore did not fully complete: ") + ex.Message, L("热恢复提示", "Hot Restore Notice"));
        }
        finally
        {
            TryDeleteGeneratedBridge(bridgePath);
        }
    }

    private bool ValidateRestoreInputs(WorkspaceRepository repository, List<PersistentVariableValue> values, bool requireLauncher, out string error)
    {
        if (!Directory.Exists(repository.TargetPath) || !File.Exists(_persistentUserConfigPath))
        {
            error = L("目标 Mods 文件夹或 d3dx_user.ini 无效。", "The target Mods folder or d3dx_user.ini is invalid.");
            return false;
        }

        if (requireLauncher && !File.Exists(repository.LauncherPath))
        {
            error = L("请先设置有效的 XXMI 启动器路径。", "Set a valid XXMI launcher path first.");
            return false;
        }

        if (values.Count == 0)
        {
            error = L("当前已启用 Mod 中没有可恢复的持久变量。", "No persistent variables can be restored for the currently enabled Mods.");
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static List<PersistentVariableValue> GetInstalledPersistentValues(WorkspaceRepository repository, IEnumerable<RememberedModPersistentState> states)
    {
        return states.Where(state => IsRememberedModInstalled(repository, state)).SelectMany(state => state.Values ?? []).Select(value => new PersistentVariableValue(value.Name, value.Value)).ToList();
    }

    private async Task<string> ReadStableUserConfigAsync(TimeSpan timeout)
    {
        string path = _persistentUserConfigPath!;
        Stopwatch stopwatch = Stopwatch.StartNew();
        (long Length, DateTime LastWriteUtc)? previous = null;
        while (stopwatch.Elapsed < timeout)
        {
            try
            {
                var file = new FileInfo(path);
                file.Refresh();
                var current = (Length: file.Length, LastWriteUtc: file.LastWriteTimeUtc);
                if (previous == current)
                {
                    string content = await File.ReadAllTextAsync(path);
                    file.Refresh();
                    if (file.Length == current.Length && file.LastWriteTimeUtc == current.LastWriteUtc)
                    {
                        return content;
                    }
                }

                previous = current;
            }
            catch (IOException)
            {
                previous = null;
            }

            await Task.Delay(350);
        }

        throw new IOException(L("等待 d3dx_user.ini 停止写入超时。请稍后重试。", "Timed out waiting for d3dx_user.ini to become stable. Try again shortly."));
    }

    private async Task<bool> WaitForPersistedValuesAsync(IEnumerable<PersistentVariableValue> expected, TimeSpan timeout)
    {
        var expectedMap = expected.GroupBy(value => value.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.OrdinalIgnoreCase);
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            await Task.Delay(250);
            try
            {
                IReadOnlyDictionary<string, string> actual = ThreeDmigotoPersistentState.ParseUserConfig(await File.ReadAllTextAsync(_persistentUserConfigPath!));
                if (expectedMap.All(pair => actual.TryGetValue(pair.Key, out string? value) && NumericStateValuesEqual(pair.Value, value)))
                {
                    return true;
                }
            }
            catch (IOException)
            {
                // 3DMigoto may be replacing the file; retry until timeout.
            }
        }

        return false;
    }

    private static bool NumericStateValuesEqual(string expected, string actual)
    {
        return double.TryParse(expected, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double left)
            && double.TryParse(actual, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double right)
            && left.Equals(right);
    }

    private string CreateUserConfigBackup()
    {
        string backupDirectory = Path.Combine(Path.GetDirectoryName(_persistentUserConfigPath!)!, PersistentStateBackupDirectoryName);
        Directory.CreateDirectory(backupDirectory);
        string backupPath = Path.Combine(backupDirectory, $"d3dx_user-{DateTime.Now:yyyyMMdd-HHmmss-fff}.ini");
        File.Copy(_persistentUserConfigPath!, backupPath, overwrite: false);
        return backupPath;
    }

    private static void WriteTextAtomically(string targetPath, string content)
    {
        string tempPath = targetPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(tempPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(tempPath, targetPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static List<string> FindRunningTargetProcesses(WorkspaceRepository repository)
    {
        var processNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Endfield" };
        string d3dxPath = Path.Combine(Directory.GetParent(repository.TargetPath)?.FullName ?? string.Empty, "d3dx.ini");
        if (File.Exists(d3dxPath))
        {
            foreach (string line in File.ReadLines(d3dxPath))
            {
                string trimmed = line.Trim();
                if (!trimmed.StartsWith("target", StringComparison.OrdinalIgnoreCase) || !trimmed.Contains('='))
                {
                    continue;
                }

                string name = Path.GetFileNameWithoutExtension(trimmed[(trimmed.IndexOf('=') + 1)..].Trim());
                if (!string.IsNullOrWhiteSpace(name))
                {
                    processNames.Add(name);
                }
            }
        }

        var running = new List<string>();
        foreach (string name in processNames)
        {
            try
            {
                if (Process.GetProcessesByName(name).Length > 0)
                {
                    running.Add(name + ".exe");
                }
            }
            catch
            {
                // If a process disappears while enumerating, continue checking others.
            }
        }

        return running;
    }

    private static void TryDeleteGeneratedBridge(string bridgePath)
    {
        try
        {
            if (File.Exists(bridgePath) && ThreeDmigotoPersistentState.IsGeneratedBridge(File.ReadAllText(bridgePath)))
            {
                File.Delete(bridgePath);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup; never hide the original restore result.
        }
        catch (UnauthorizedAccessException)
        {
            // The next run will recognize the generated marker and retry cleanup.
        }
    }

    private static bool IsRememberedModInstalled(WorkspaceRepository repository, RememberedModPersistentState state)
    {
        if (string.IsNullOrWhiteSpace(state.ModRelativePath))
        {
            return false;
        }

        string sourcePath = Path.GetFullPath(Path.Combine(repository.SourcePath, state.ModRelativePath));
        return IsPathInsideDirectory(sourcePath, repository.SourcePath) && Directory.Exists(Path.Combine(repository.TargetPath, Path.GetFileName(sourcePath)));
    }

    private void UpsertRememberedState(RememberedModPersistentState state)
    {
        RememberedModPersistentState? existing = FindRememberedState(state.RepositoryId, state.ModRelativePath);
        if (existing is not null)
        {
            _rememberedModStates.Remove(existing);
        }

        _rememberedModStates.Add(state);
    }

    private static bool IsValidRememberedState(RememberedModPersistentState state)
    {
        if (state is null || string.IsNullOrWhiteSpace(state.RepositoryId) || string.IsNullOrWhiteSpace(state.ModRelativePath) || state.Values is not { Count: > 0 })
        {
            return false;
        }

        state.Values = state.Values.Where(value => value is not null && !string.IsNullOrWhiteSpace(value.Name) && !string.IsNullOrWhiteSpace(value.Value)).ToList();
        return state.Values.Count > 0;
    }

    private RememberedModPersistentState? FindRememberedState(string repositoryId, string relativePath)
    {
        return _rememberedModStates.FirstOrDefault(state => string.Equals(state.RepositoryId, repositoryId, StringComparison.Ordinal) && string.Equals(state.ModRelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetSourceRelativePath(WorkspaceRepository repository, string sourcePath)
    {
        string fullSource = Path.GetFullPath(sourcePath);
        if (!IsPathInsideDirectory(fullSource, repository.SourcePath))
        {
            throw new InvalidDataException("The selected Mod is outside the repository source directory.");
        }

        return Path.GetRelativePath(repository.SourcePath, fullSource);
    }

    private static string? DetectPersistentUserConfig(WorkspaceRepository repository)
    {
        var candidates = new List<string>();
        AddPersistentUserConfigCandidate(candidates, repository.TargetPath);
        AddPersistentUserConfigCandidate(candidates, Path.GetDirectoryName(repository.TargetPath));
        AddPersistentUserConfigCandidate(candidates, Path.GetDirectoryName(Path.GetDirectoryName(repository.TargetPath)));
        AddPersistentUserConfigCandidate(candidates, Path.GetDirectoryName(repository.LauncherPath));
        AddPersistentUserConfigCandidate(candidates, Path.GetDirectoryName(Path.GetDirectoryName(repository.LauncherPath)));
        return candidates.Select(path => Path.Combine(path, "d3dx_user.ini")).FirstOrDefault(File.Exists);
    }

    private static void AddPersistentUserConfigCandidate(List<string> candidates, string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path) && !candidates.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            candidates.Add(path);
        }
    }
}
