using IntegratedModManager.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

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
        RefreshButton.IsEnabled = !busy;
        ImportZipButton.IsEnabled = !busy;
        RunLauncherButton.IsEnabled = !busy;
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
            // Freeze the selected preset: asynchronous dialogs must not read a mutable profile.
            var request = new CombinationRestoreRequest(repository.SourcePath, repository.TargetPath,
                profile.ModRelativePaths.ToArray(), repository.UseDirectoryLinks, profile.IncludesPersistentState,
                profile.PersistentStates.Select(state => new CombinationPresetState(state.ModRelativePath, state.Values.ToArray())).ToArray(), userIni);
            CombinationRestorePreview preview = await Task.Run(() => CombinationRestorePreviewService.Prepare(request));
            foreach (var issue in preview.Issues.Where(issue => issue.Error is not null))
                LogApplicationIssue("Combination preview: " + issue.Code, issue.Error!);
            if (!await ConfirmCombinationRestoreAsync(profile.Name, preview)) return;
            ModCombinationDeploymentPlan deployment = preview.Deployment!;
            runtimePlan = preview.Runtime;

            if (_enableConflictDetection)
            {
                List<string> conflicts = await Task.Run(() => deployment.InstallSources.SelectMany(source => DetectModFileConflicts(
                    source, repository.TargetPath, deployment.RemoveTargets.Concat(deployment.InstallSources.Select(item => Path.Combine(repository.TargetPath, Path.GetFileName(item))))))
                    .Distinct(StringComparer.OrdinalIgnoreCase).Take(100).ToList());
                if (conflicts.Count > 0 && !await ConfirmInstallConflictsAsync(conflicts)) return;
            }
            // No confirmation dialog is a lock. Revalidate AFTER all confirmations.
            CombinationRestorePreview fresh = await Task.Run(() => CombinationRestorePreviewService.Prepare(request));
            if (!CombinationRestorePreviewService.IsStillApproved(preview, fresh))
                throw new IOException(L("部署或参数在确认期间发生变化，未开始恢复。请重新预览。",
                    "Deployment or parameters changed during confirmation; restore was not started. Prepare a new preview."));
            deployment = fresh.Deployment!;
            runtimePlan = fresh.Runtime;
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
            if (!actual.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(deployment.DesiredSources.Select(source => Path.GetRelativePath(request.SourceRoot, source))))
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

    public sealed record CombinationPreviewRow(string Title, string Detail, string ValueText);

    private async Task<bool> ConfirmCombinationRestoreAsync(string name, CombinationRestorePreview preview)
    {
        var closed = new CheckBox { Content = L("我已完全退出游戏及 XXMI/EFMI 加载器", "I have fully closed the game and XXMI/EFMI loader") };
        var panel = new Grid { RowSpacing = 12, MinWidth = 320,
            Height = Math.Clamp(RootGrid.ActualHeight - 260, 280, 580) };
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var runtime = preview.Runtime;
        string summary = preview.CanRestore
            ? L($"{name} · {preview.Deployment!.DesiredSources.Count} 个 Mod · 启用 {preview.Deployment.InstallSources.Count} · 移除 {preview.Deployment.RemoveTargets.Count}",
                $"{name} · {preview.Deployment!.DesiredSources.Count} Mods · enable {preview.Deployment.InstallSources.Count} · remove {preview.Deployment.RemoveTargets.Count}")
            : L($"{name} · {preview.Issues.Count} 个问题需要处理，未开始恢复。", $"{name} · {preview.Issues.Count} issues must be resolved. Restore has not started.");
        summary += runtime is null ? L("\n没有可回写的参数计划。", "\nNo parameter write plan.")
            : L($"\n参数：修改 {runtime.ChangedCount} · 补入 {runtime.AddedCount} · 已一致 {runtime.UnchangedCount}",
                $"\nParameters: change {runtime.ChangedCount} · add {runtime.AddedCount} · unchanged {runtime.UnchangedCount}");
        panel.Children.Add(new TextBlock { Text = summary, TextWrapping = TextWrapping.Wrap });
        var tabs = new TabView { IsAddTabButtonVisible = false, CanReorderTabs = false, CanDragTabs = false };
        Grid.SetRow(tabs, 1);
        panel.Children.Add(tabs);
        AddTab(L($"Mod 清单 ({preview.Mods.Count})", $"Mods ({preview.Mods.Count})"), preview.Mods.Select(mod =>
            new CombinationPreviewRow(mod.ModRelativePath, mod.Path, CombinationActionText(mod.Action))));
        AddTab(L($"参数变化 ({preview.Parameters.Count})", $"Parameters ({preview.Parameters.Count})"), preview.Parameters
            .OrderBy(item => item.Change.Kind).Select(item => new CombinationPreviewRow(
                item.ModRelativePath + " · " + item.Change.Saved.Variable,
                item.Change.Saved.RelativeIniPath + " · " + item.Change.Saved.RuntimeKey,
                (item.Change.PreviousValue ?? L("未记录", "Not recorded")) + " → " + item.Change.Saved.Value
                    + (item.Change.Kind == ModPersistentChangeKind.Unchanged ? L("（已一致）", " (unchanged)") : ""))));
        var checks = preview.Issues.Select(issue => new CombinationPreviewRow(
            CombinationIssueText(issue.Code), issue.Subject, L("阻止恢复", "Restore blocked"))).ToList();
        checks.Add(new(L("保护范围", "Protection scope"), L("只管理当前仓库可识别的部署；外部目录保留。Mod INI 不会被回写。", "Only recognized deployments are managed; external folders remain. Mod INIs are never rewritten."), ""));
        checks.Add(new(L("参数与备份", "Parameters and backups"), runtime?.Edit.Path ?? L("此预览无参数写入计划。", "This preview has no parameter write plan."),
            L("仅回写选定键；有改动时备份。", "Write selected keys only; back up changes.")));
        AddTab(L($"检查结果 ({preview.Issues.Count})", $"Checks ({preview.Issues.Count})"), checks);
        if (!preview.CanRestore) tabs.SelectedIndex = 2;
        closed.IsEnabled = preview.CanRestore;
        Grid.SetRow(closed, 2);
        panel.Children.Add(closed);
        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot, Title = L("恢复前预览", "Restore Preview"),
            Content = panel,
            PrimaryButtonText = L("备份并恢复组合", "Back Up and Restore"), CloseButtonText = L("取消", "Cancel"),
            IsPrimaryButtonEnabled = false, DefaultButton = ContentDialogButton.Close
        };
        dialog.Resources["ContentDialogMaxWidth"] = Math.Clamp(RootGrid.ActualWidth - 100, 360, 960);
        closed.Checked += (_, _) => dialog.IsPrimaryButtonEnabled = preview.CanRestore;
        closed.Unchecked += (_, _) => dialog.IsPrimaryButtonEnabled = false;
        return await dialog.ShowAsync() == ContentDialogResult.Primary;

        void AddTab(string header, IEnumerable<CombinationPreviewRow> rows)
        {
            var items = rows.ToArray();
            if (items.Length == 0) items = [new(L("暂无项目", "No items"), "", "")];
            // ListView virtualizes rows; long presets scroll here, not the whole page.
            var list = new ListView { ItemsSource = items, SelectionMode = ListViewSelectionMode.None,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                ItemTemplate = (DataTemplate)XamlReader.Load("""
                <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                  <Border Padding="8,10" BorderThickness="0,0,0,1" BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}">
                    <StackPanel Spacing="4">
                      <TextBlock Text="{Binding Title}" FontWeight="SemiBold" TextWrapping="Wrap"/>
                      <TextBlock Text="{Binding ValueText}" TextWrapping="Wrap"/>
                      <TextBlock Text="{Binding Detail}" FontSize="12" Opacity="0.7" TextWrapping="Wrap"/>
                    </StackPanel>
                  </Border>
                </DataTemplate>
                """) };
            tabs.TabItems.Add(new TabViewItem { Header = header, IsClosable = false, Content = list });
        }
    }

    private string CombinationActionText(CombinationModAction action) => action switch
    {
        CombinationModAction.Install => L("启用 / 重新部署", "Enable / redeploy"),
        CombinationModAction.Keep => L("保持启用", "Keep enabled"),
        CombinationModAction.Remove => L("移除部署（不删除仓库原文件）", "Remove deployment (keep repository source)"),
        CombinationModAction.PreserveExternal => L("外部 / 未识别目录，保持不动", "External / unrecognized folder, keep untouched"),
        CombinationModAction.Pending => L("待检查部署（请先处理阻断问题）", "Deployment unchecked (resolve blocking issues first)"),
        _ => L("不可恢复", "Unavailable")
    };

    private string CombinationIssueText(CombinationPreviewIssueCode code) => code switch
    {
        CombinationPreviewIssueCode.SourceUnavailable => L("仓库不可访问，请检查路径", "Repository unavailable; check its path"),
        CombinationPreviewIssueCode.TargetUnavailable => L("部署目录不可访问，请检查路径", "Deployment folder unavailable; check its path"),
        CombinationPreviewIssueCode.UnsafeRoots => L("仓库与部署目录须独立，且不能是链接根目录", "Use separate repository/deployment roots, not linked roots"),
        CombinationPreviewIssueCode.UnsafeModPath => L("Mod 路径无效，请重新保存预设", "Invalid Mod path; save the preset again"),
        CombinationPreviewIssueCode.MissingMod => L("找不到保存的 Mod，请还原其目录或重新保存预设", "Saved Mod missing; restore its folder or save the preset again"),
        CombinationPreviewIssueCode.DisabledMod => L("Mod 目录已禁用，请检查后重新保存预设", "Mod folder disabled; check it and save the preset again"),
        CombinationPreviewIssueCode.LinkedSource => L("源 Mod 是链接目录，无法安全确认身份", "Linked source Mod; its identity cannot be safely validated"),
        CombinationPreviewIssueCode.DuplicateMod => L("预设包含重复 Mod，请重新保存", "Duplicate Mod in preset; save it again"),
        CombinationPreviewIssueCode.AmbiguousTargetName => L("多个 Mod 使用相同部署目录名，不能自动匹配", "Multiple Mods share a deployment name; cannot match automatically"),
        CombinationPreviewIssueCode.SnapshotMismatch => L("Mod 清单与参数快照不一致，请重新保存预设", "Mod list and state snapshots differ; save the preset again"),
        CombinationPreviewIssueCode.ParameterMismatch => L("Mod 或已部署副本的参数声明发生变化，请重新保存预设", "Mod or deployed copy declarations changed; save the preset again"),
        CombinationPreviewIssueCode.LoaderStateInvalid => L("加载器参数无法安全读取，请检查 d3dx_user.ini 和重复键", "Cannot safely read loader state; check d3dx_user.ini and duplicate keys"),
        CombinationPreviewIssueCode.DeploymentBlocked => L("部署目录冲突或匹配不明确，请检查同名目录、文件及链接", "Deployment blocked; check ambiguous names, files and links"),
        _ => L("读取失败，请检查权限后重试", "Read failed; check permissions and retry")
    };
}
