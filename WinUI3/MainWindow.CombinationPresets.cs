using IntegratedModManager.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private bool _applyingCombinationProfile;

    private void OnOpenCombinationPresetsClicked(object sender, RoutedEventArgs e)
    {
        if (!_applyingCombinationProfile && !_restoringPersistentRuntime)
        {
            NavigateToPrimarySection(PrimarySection.Repository);
            SetRepositoryWorkspaceViewV4(RepositoryWorkspaceView.Presets);
        }
    }

    private void SetCombinationBusy(bool busy)
    {
        _applyingCombinationProfile = busy;
        SetBusyState(busy);
        PrimaryNavBorder.IsHitTestVisible = !busy;
        SecondaryNavPanel.IsHitTestVisible = !busy;
        foreach (Button button in new[] { DashboardNavButton, RepositoryNavButton, OnlineNavButton, UpdatesNavButton, SettingsNavButton })
            button.IsEnabled = !busy;
        foreach (Control control in SecondaryNavPanel.Children.OfType<Control>()) control.IsEnabled = !busy;
        SourceTextBox.IsEnabled = !busy;
        TargetTextBox.IsEnabled = !busy;
        LauncherTextBox.IsEnabled = !busy;
        RefreshConfigurationProfiles();
        RefreshPersistentSlotControls();
    }

    private async Task SaveCombinationProfileAsync(WorkspaceRepository repository, string name, ModConfigurationProfile? existing)
    {
        bool includeState = CaptureCombinationStateCheckBox.IsChecked == true;
        string userIni = CombinationUserIniTextBox.Text.Trim();
        SetCombinationBusy(true);
        try
        {
            List<string> paths = await Task.Run(CaptureCurrentConfigurationProfile);
            if (paths.Count == 0 && !await ShowConfirmAsync(L(
                "当前没有识别到已启用的仓库 Mod。保存空组合？恢复空组合会移除当前仓库中能识别的已部署 Mod。",
                "No enabled repository Mods were recognized. Save an empty set? Restoring it removes recognized deployments from this repository."),
                L("空组合", "Empty Combination"))) return;

            IReadOnlyList<ModPersistentSourceState> captured = includeState
                ? await Task.Run(() => ModPersistentPresetEngine.CaptureCombination(paths.Select(relative =>
                    (Path.Combine(repository.SourcePath, relative), Path.GetFileName(relative))).ToArray(), userIni))
                : [];
            // Validate copy deployments too: their INIs may have changed independently.
            if (captured.Any(state => state.Values.Count > 0) && !repository.UseDirectoryLinks)
                await Task.Run(() => ModPersistentPresetEngine.PlanCombinationRuntimeRestore(captured.Select(state => state with
                {
                    ModDirectory = Path.Combine(repository.TargetPath, state.DeployedRelativePath)
                }).ToArray(), userIni));
            var stillEnabled = await Task.Run(() => ModCombinationDeploymentPolicy.Capture(repository.SourcePath, repository.TargetPath));
            if (!stillEnabled.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(paths))
                throw new IOException(L("部署目录在捕获期间发生变化，未保存组合。", "Deployment changed during capture; the combination was not saved."));

            ModConfigurationProfile profile = existing ?? new ModConfigurationProfile { RepositoryId = repository.Id, Name = name };
            profile.ModRelativePaths = paths;
            profile.IncludesPersistentState = includeState;
            profile.PersistentStates = captured.Select(state => new ModConfigurationPersistentState
            {
                ModRelativePath = Path.GetRelativePath(repository.SourcePath, state.ModDirectory),
                Values = [.. state.Values]
            }).ToList();
            profile.UpdatedAtUtc = DateTimeOffset.UtcNow;
            if (existing is null) _configurationProfiles.Add(profile);
            _selectedConfigurationProfileId = profile.Id;
            if (includeState) repository.PersistentUserIniPath = userIni;
            SaveShellConfig();
            int count = profile.PersistentStates.Sum(state => state.Values.Count);
            ShowAppNotification($"已保存“{name}”：{paths.Count} 个 Mod，{count} 个参数。", $"Saved '{name}': {paths.Count} Mods, {count} parameters.");
        }
        catch (Exception ex)
        {
            LogApplicationIssue("Combination capture", ex);
            await ShowMessageAsync(L("未保存组合。请确认部署目录与加载器状态后重试：\n", "Combination was not saved. Check deployment and loader state, then retry:\n")
                + ex.Message, L("保存组合失败", "Combination Save Failed"));
        }
        finally { SetCombinationBusy(false); }
    }

    private async Task RestoreCombinationProfileAsync()
    {
        if (_applyingCombinationProfile || _restoringPersistentRuntime) return;
        ModConfigurationProfile? profile = GetSelectedConfigurationProfile();
        WorkspaceRepository? repository = GetSelectedRepository();
        if (profile is null || repository is null) return;
        string userIni = CombinationUserIniTextBox.Text.Trim();
        string? transaction = null, stateBackup = null;
        ModPersistentRuntimeRestorePlan? runtimePlan = null;
        bool started = false, committed = false;
        SetCombinationBusy(true);
        try
        {
            ModCombinationDeploymentPlan deployment = await Task.Run(() => ModCombinationDeploymentPolicy.Plan(
                repository.SourcePath, repository.TargetPath, profile.ModRelativePaths, repository.UseDirectoryLinks));
            if (profile.IncludesPersistentState)
            {
                var paths = profile.ModRelativePaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (profile.PersistentStates.Count != paths.Count
                    || !profile.PersistentStates.Select(state => state.ModRelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(paths))
                    throw new InvalidDataException(L("预设的 Mod 列表与参数快照不一致，请重新保存。", "The Mod set and state snapshots differ. Save the preset again."));
                ModPersistentSourceState[] sources = profile.PersistentStates.Select(state => new ModPersistentSourceState(
                    Path.Combine(repository.SourcePath, state.ModRelativePath), Path.GetFileName(state.ModRelativePath), state.Values)).ToArray();
                if (sources.Any(source => source.Values.Count > 0))
                    runtimePlan = await Task.Run(() => ModPersistentPresetEngine.PlanCombinationRuntimeRestore(sources, userIni));
            }
            string changedMods = string.Join("\n", deployment.InstallSources.Select(source => "+ " + Path.GetFileName(source))
                .Concat(deployment.RemoveTargets.Select(target => "− " + Path.GetFileName(target))).Take(20));
            string preview = L($"预设：{profile.Name}\n恢复 {deployment.DesiredSources.Count} 个 Mod；安装 {deployment.InstallSources.Count}，移除 {deployment.RemoveTargets.Count}。\n{changedMods}\n\n",
                $"Preset: {profile.Name}\nRestore {deployment.DesiredSources.Count} Mods; install {deployment.InstallSources.Count}, remove {deployment.RemoveTargets.Count}.\n{changedMods}\n\n");
            preview += runtimePlan is null ? L("没有需要回写的参数；只恢复组合。", "No parameters to write; restore deployment only.")
                : L($"参数文件：{runtimePlan.Edit.Path}\n修改 {runtimePlan.ChangedCount}，补入 {runtimePlan.AddedCount}，已一致 {runtimePlan.UnchangedCount}。\n只写此组合对应的键；其他 Mod 的值、注释和其他段落保留。",
                    $"State file: {runtimePlan.Edit.Path}\nChange {runtimePlan.ChangedCount}, add {runtimePlan.AddedCount}, already equal {runtimePlan.UnchangedCount}.\nOnly this combination's keys are written; other Mods, comments, and sections stay intact.");
            preview += L("\n\n只管理当前仓库可识别的部署，外部/未识别目录保持不动。恢复前建立部署备份与参数 .bak。Mod INI 不会被回写。", "\n\nOnly recognized deployments in this repository are managed; external/unknown folders remain. Deployment and state .bak backups are kept. Mod INIs are never rewritten.");
            if (!await ConfirmCombinationRestoreAsync(preview)) return;

            // Dialog time is not a lock. Repeat preflight and reject stale plans.
            ModCombinationDeploymentPlan fresh = await Task.Run(() => ModCombinationDeploymentPolicy.Plan(
                repository.SourcePath, repository.TargetPath, profile.ModRelativePaths, repository.UseDirectoryLinks));
            if (!fresh.DesiredSources.SequenceEqual(deployment.DesiredSources, StringComparer.OrdinalIgnoreCase)
                || !fresh.InstallSources.SequenceEqual(deployment.InstallSources, StringComparer.OrdinalIgnoreCase)
                || !fresh.RemoveTargets.SequenceEqual(deployment.RemoveTargets, StringComparer.OrdinalIgnoreCase))
                throw new IOException(L("部署目录在确认期间发生变化，请重新预览。", "Deployment changed during confirmation. Prepare a new preview."));
            if (runtimePlan is not null && !File.ReadAllBytes(runtimePlan.Edit.Path).SequenceEqual(runtimePlan.Edit.OriginalBytes))
                throw new IOException(L("加载器参数在确认期间发生变化，未执行恢复。", "Loader parameters changed during confirmation; restore was not started."));

            if (_enableConflictDetection)
            {
                List<string> conflicts = await Task.Run(() => deployment.InstallSources.SelectMany(source => DetectModFileConflicts(
                    source, repository.TargetPath, deployment.RemoveTargets.Concat(deployment.InstallSources.Select(item => Path.Combine(repository.TargetPath, Path.GetFileName(item))))))
                    .Distinct(StringComparer.OrdinalIgnoreCase).Take(100).ToList());
                if (conflicts.Count > 0 && !await ConfirmInstallConflictsAsync(conflicts)) return;
            }
            string[] affected = deployment.RemoveTargets.Concat(deployment.InstallSources.Select(source =>
                Path.Combine(repository.TargetPath, Path.GetFileName(source)))).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (affected.Length > 0)
                transaction = await CreateInstallTransactionAsync(L($"恢复组合：{profile.Name}", $"Restore combination: {profile.Name}"), repository, affected);
            started = true;
            foreach (string target in deployment.RemoveTargets) await Task.Run(() => DeleteDirectoryTreeSafely(target));
            foreach (string source in deployment.InstallSources)
            {
                await DeployModAsync(source, Path.Combine(repository.TargetPath, Path.GetFileName(source)), repository,
                    new Progress<ProgressInfo>(info => UpdateProgress(info.Percent, L("正在恢复组合：", "Restoring combination: ") + Path.GetFileName(source))));
            }
            var actual = await Task.Run(() => ModCombinationDeploymentPolicy.Capture(repository.SourcePath, repository.TargetPath));
            if (!actual.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(profile.ModRelativePaths))
                throw new IOException(L("部署结果与预设不一致，停止参数写入。", "Deployment does not match the preset; parameter write was stopped."));
            if (runtimePlan is not null)
            {
                if (!repository.UseDirectoryLinks)
                    await Task.Run(() => ModPersistentPresetEngine.PlanCombinationRuntimeRestore(runtimePlan.Sources.Select(source => source with
                    {
                        ModDirectory = Path.Combine(repository.TargetPath, source.DeployedRelativePath)
                    }).ToArray(), userIni));
                stateBackup = await Task.Run(() => ModPersistentPresetEngine.ApplyRuntimeRestore(runtimePlan));
            }
            if (transaction is not null) CommitInstallTransaction(transaction);
            committed = true;
            UpdateProgress(100, L("组合与加载器参数已核对", "Combination and loader parameters verified"));
            await ShowMessageAsync(L($"已恢复“{profile.Name}”：{actual.Count} 个 Mod，{runtimePlan?.Values.Count ?? 0} 个参数。\n",
                $"Restored '{profile.Name}': {actual.Count} Mods, {runtimePlan?.Values.Count ?? 0} parameters.\n")
                + (stateBackup is null ? L("参数无改动，没有创建新参数备份。", "State was unchanged; no new state backup was created.") : L("参数备份：", "State backup: ") + stateBackup)
                + L("\n请重新启动加载器和游戏，确认游戏内效果。", "\nRestart the loader and game to verify in-game behavior."), L("组合恢复完成", "Combination Restored"));
        }
        catch (Exception ex)
        {
            LogApplicationIssue("Combination restore", ex);
            var errors = new List<string> { ex.Message };
            if (started && !committed)
            {
                if (stateBackup is not null && runtimePlan is not null)
                {
                    try { await Task.Run(() => ModPersistentPresetEngine.RollbackRuntimeRestore(runtimePlan, stateBackup)); }
                    catch (Exception rollback) { errors.Add(L("参数回滚失败，请检查备份：", "State rollback failed; inspect backup: ") + rollback.Message); }
                }
                if (transaction is not null)
                {
                    try { await RollbackInstallTransactionAsync(transaction, clearLastTransaction: true); }
                    catch (Exception rollback) { errors.Add(L("部署回滚失败，请检查备份：", "Deployment rollback failed; inspect backup: ") + rollback.Message); }
                }
            }
            await ShowMessageAsync(string.Join("\n", errors) + L("\n请保持加载器关闭并检查当前部署/备份。", "\nKeep the loader closed and inspect deployment/backups."), L("组合恢复未完成", "Combination Restore Incomplete"));
        }
        finally
        {
            if (started) await RefreshListsAsync();
            SetCombinationBusy(false);
        }
    }

    private async Task<bool> ConfirmCombinationRestoreAsync(string preview)
    {
        var closed = new CheckBox { Content = L("我已完全退出游戏及 XXMI/EFMI 加载器", "I have fully closed the game and XXMI/EFMI loader") };
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = preview, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(closed);
        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot, Title = L("一键恢复 Mod 组合", "Restore Mod Combination"),
            Content = new ScrollViewer { Content = panel, MaxHeight = 520 },
            PrimaryButtonText = L("备份并恢复组合", "Back Up and Restore"), CloseButtonText = L("取消", "Cancel"),
            IsPrimaryButtonEnabled = false, DefaultButton = ContentDialogButton.Close
        };
        closed.Checked += (_, _) => dialog.IsPrimaryButtonEnabled = true;
        closed.Unchecked += (_, _) => dialog.IsPrimaryButtonEnabled = false;
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
