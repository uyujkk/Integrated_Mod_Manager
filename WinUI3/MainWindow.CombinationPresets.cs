using IntegratedModManager.Core.Deployment;
using IntegratedModManager.Core.Presets;
using IntegratedModManager.Core.Repository;
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
            (int mods, int count)? saved = await CaptureCombinationProfileAsync(repository, name, existing, includeState, userIni,
                () => ShowConfirmAsync(T("CombinationPresets.NoEnabledRepositoryModsWereRecognized"),
                    T("CombinationPresets.EmptyCombination")));
            if (saved is (int mods, int count))
                ShowAppNotification($"已保存“{name}”：{mods} 个 Mod，{count} 个参数。", $"Saved '{name}': {mods} Mods, {count} parameters.");
        }
        catch (Exception ex)
        {
            LogApplicationIssue("Combination capture", ex);
            await ShowMessageAsync(T("CombinationPresets.CombinationWasNotSavedCheckDeployment")
                + ex.Message, T("CombinationPresets.CombinationSaveFailed"));
        }
        finally { SetCombinationBusy(false); }
    }

    /// <summary>
    /// Read-only capture of the enabled combination (and loader state) into a preset. Shared by the main
    /// window and the in-game overlay, which cannot show dialogs; returns null when an empty set is declined.
    /// </summary>
    private async Task<(int Mods, int Parameters)?> CaptureCombinationProfileAsync(WorkspaceRepository repository, string name,
        ModConfigurationProfile? existing, bool includeState, string userIni, Func<Task<bool>> confirmEmpty)
    {
        List<string> paths = await Task.Run(CaptureCurrentConfigurationProfile);
        if (paths.Count == 0 && !await confirmEmpty()) return null;

        IReadOnlyList<ModPersistentSourceState> captured = includeState
            ? await Task.Run(() => ModPersistentPresetEngine.CaptureCombination(paths.Select(relative =>
                (Path.Combine(repository.SourcePath, relative), Path.GetFileName(relative))).ToArray(), userIni))
            : [];
        // Validate copy deployments too: their INIs may have changed independently. A deployment that is a
        // link back to its own source Mod has no separate INIs (it may predate switching the repository to copies).
        ModPersistentSourceState[] copies = captured
            .Where(state => !ModDeploymentLinks.IsLinkTo(Path.Combine(repository.TargetPath, state.DeployedRelativePath), state.ModDirectory))
            .Select(state => state with { ModDirectory = Path.Combine(repository.TargetPath, state.DeployedRelativePath) })
            .ToArray();
        if (copies.Any(state => state.Values.Count > 0) && !repository.UseDirectoryLinks)
            await Task.Run(() => ModPersistentPresetEngine.PlanCombinationRuntimeRestore(copies, userIni));
        var stillEnabled = await Task.Run(() => ModCombinationDeploymentPolicy.Capture(repository.SourcePath, repository.TargetPath));
        if (!stillEnabled.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(paths))
            throw new IOException(T("CombinationPresets.DeploymentChangedDuringCaptureTheCombination"));

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
        return (paths.Count, profile.PersistentStates.Sum(state => state.Values.Count));
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
            if (repository.UseDirectoryLinks
                && !await ConfirmSingleSelectionViolationsAsync(repository, preview.Deployment!.DesiredSources)) return;
            ModCombinationDeploymentPlan deployment = preview.Deployment!;
            runtimePlan = preview.Runtime;

            if (_enableConflictDetection)
            {
                string[] deploymentTargets = deployment.RemoveTargets
                    .Concat(deployment.InstallSources.Select(item => ModDeploymentLayout.DeployedPath(repository.TargetPath, item))).ToArray();
                List<string> conflicts = await Task.Run(() => deployment.InstallSources.SelectMany(source => DetectModFileConflicts(
                    source, repository.TargetPath, deploymentTargets))
                    .Distinct(StringComparer.OrdinalIgnoreCase).Take(100).ToList());
                List<(string? Candidate, ModIniConflictSummary Summary)> iniConflicts = await Task.Run(() => deployment.InstallSources
                    .SelectMany(source => DetectModIniConflictsSafely(source, repository.TargetPath, deploymentTargets)
                        .Select(summary => ((string?)Path.GetFileName(source), summary)))
                    .ToList());
                if ((conflicts.Count > 0 || iniConflicts.Count > 0) && !await ConfirmInstallConflictsAsync(conflicts, iniConflicts)) return;
            }
            // No confirmation dialog is a lock. Revalidate AFTER all confirmations.
            CombinationRestorePreview fresh = await Task.Run(() => CombinationRestorePreviewService.Prepare(request));
            if (!CombinationRestorePreviewService.IsStillApproved(preview, fresh))
                throw new IOException(T("CombinationPresets.DeploymentOrParametersChangedDuringConfirmation"));
            deployment = fresh.Deployment!;
            runtimePlan = fresh.Runtime;
            string[] affected = deployment.RemoveTargets.Concat(deployment.InstallSources.Select(source =>
                ModDeploymentLayout.DeployedPath(repository.TargetPath, source))).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (affected.Length > 0)
                transaction = await CreateInstallTransactionAsync(T("CombinationPresets.RestoreCombination", profile.Name), repository, affected);
            started = true;
            foreach (string target in deployment.RemoveTargets) await Task.Run(() => DeleteDirectoryTreeSafely(target));
            foreach (string source in deployment.InstallSources)
            {
                await DeployModAsync(source, ModDeploymentLayout.DeployedPath(repository.TargetPath, source), repository,
                    new Progress<ProgressInfo>(info => UpdateProgress(info.Percent, T("CombinationPresets.RestoringCombination") + Path.GetFileName(source))));
            }
            var actual = await Task.Run(() => ModCombinationDeploymentPolicy.Capture(repository.SourcePath, repository.TargetPath));
            if (!actual.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(deployment.DesiredSources.Select(source => Path.GetRelativePath(request.SourceRoot, source))))
                throw new IOException(T("CombinationPresets.DeploymentDoesNotMatchThePreset"));
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
            UpdateProgress(100, T("CombinationPresets.CombinationAndLoaderParametersVerified"));
            await ShowMessageAsync(T("CombinationPresets.RestoredModsParameters", profile.Name, actual.Count, runtimePlan?.Values.Count ?? 0)
                + (stateBackup is null ? T("CombinationPresets.StateWasUnchangedNoNewState") : T("CombinationPresets.StateBackup") + stateBackup)
                + T("CombinationPresets.RestartTheLoaderAndGameTo"), T("CombinationPresets.CombinationRestored"));
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
                    catch (Exception rollback) { errors.Add(T("CombinationPresets.StateRollbackFailedInspectBackup") + rollback.Message); }
                }
                if (transaction is not null)
                {
                    try { await RollbackInstallTransactionAsync(transaction, clearLastTransaction: true); }
                    catch (Exception rollback) { errors.Add(T("CombinationPresets.DeploymentRollbackFailedInspectBackup") + rollback.Message); }
                }
            }
            await ShowMessageAsync(string.Join("\n", errors) + T("CombinationPresets.KeepTheLoaderClosedAndInspect"), T("CombinationPresets.CombinationRestoreIncomplete"));
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
        var closed = new CheckBox { Content = T("Common.IHaveFullyClosedTheGame") };
        var panel = new Grid { RowSpacing = 12, MinWidth = 320,
            Height = Math.Clamp(RootGrid.ActualHeight - 260, 280, 580) };
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var runtime = preview.Runtime;
        string summary = preview.CanRestore
            ? T("CombinationPresets.ModsEnableRemove", name, preview.Deployment!.DesiredSources.Count, preview.Deployment.InstallSources.Count, preview.Deployment.RemoveTargets.Count)
            : T("CombinationPresets.IssuesMustBeResolvedRestoreHas", name, preview.Issues.Count);
        summary += runtime is null ? T("CombinationPresets.NoParameterWritePlan")
            : T("CombinationPresets.ParametersChangeAddUnchanged", runtime.ChangedCount, runtime.AddedCount, runtime.UnchangedCount);
        panel.Children.Add(new TextBlock { Text = summary, TextWrapping = TextWrapping.Wrap });
        var tabs = new TabView { IsAddTabButtonVisible = false, CanReorderTabs = false, CanDragTabs = false };
        Grid.SetRow(tabs, 1);
        panel.Children.Add(tabs);
        AddTab(T("CombinationPresets.Mods", preview.Mods.Count), preview.Mods.Select(mod =>
            new CombinationPreviewRow(mod.ModRelativePath, mod.Path, CombinationActionText(mod.Action))));
        AddTab(T("CombinationPresets.Parameters", preview.Parameters.Count), preview.Parameters
            .OrderBy(item => item.Change.Kind).Select(item => new CombinationPreviewRow(
                item.ModRelativePath + " · " + item.Change.Saved.Variable,
                item.Change.Saved.RelativeIniPath + " · " + item.Change.Saved.RuntimeKey,
                (item.Change.PreviousValue ?? T("CombinationPresets.NotRecorded")) + " → " + item.Change.Saved.Value
                    + (item.Change.Kind == ModPersistentChangeKind.Unchanged ? T("CombinationPresets.Unchanged") : ""))));
        var checks = preview.Issues.Select(issue => new CombinationPreviewRow(
            CombinationIssueText(issue.Code), issue.Subject, T("CombinationPresets.RestoreBlocked"))).ToList();
        checks.Add(new(T("CombinationPresets.ProtectionScope"), T("CombinationPresets.OnlyRecognizedDeploymentsAreManagedExternal"), ""));
        checks.Add(new(T("CombinationPresets.ParametersAndBackups"), runtime?.Edit.Path ?? T("CombinationPresets.ThisPreviewHasNoParameterWrite"),
            T("CombinationPresets.WriteSelectedKeysOnlyBackUp")));
        AddTab(T("CombinationPresets.Checks", preview.Issues.Count), checks);
        if (!preview.CanRestore) tabs.SelectedIndex = 2;
        closed.IsEnabled = preview.CanRestore;
        Grid.SetRow(closed, 2);
        panel.Children.Add(closed);
        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot, Title = T("CombinationPresets.RestorePreview"),
            Content = panel,
            PrimaryButtonText = T("CombinationPresets.BackUpAndRestore"), CloseButtonText = T("Common.Cancel"),
            IsPrimaryButtonEnabled = false, DefaultButton = ContentDialogButton.Close
        };
        dialog.Resources["ContentDialogMaxWidth"] = Math.Clamp(RootGrid.ActualWidth - 100, 360, 960);
        closed.Checked += (_, _) => dialog.IsPrimaryButtonEnabled = preview.CanRestore;
        closed.Unchecked += (_, _) => dialog.IsPrimaryButtonEnabled = false;
        return await dialog.ShowAsync() == ContentDialogResult.Primary;

        void AddTab(string header, IEnumerable<CombinationPreviewRow> rows)
        {
            var items = rows.ToArray();
            if (items.Length == 0) items = [new(T("CombinationPresets.NoItems"), "", "")];
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
        CombinationModAction.Install => T("CombinationPresets.EnableRedeploy"),
        CombinationModAction.Keep => T("CombinationPresets.KeepEnabled"),
        CombinationModAction.Remove => T("CombinationPresets.RemoveDeploymentKeepRepositorySource"),
        CombinationModAction.PreserveExternal => T("CombinationPresets.ExternalUnrecognizedFolderKeepUntouched"),
        CombinationModAction.Pending => T("CombinationPresets.DeploymentUncheckedResolveBlockingIssuesFirst"),
        _ => T("CombinationPresets.Unavailable")
    };

    private string CombinationIssueText(CombinationPreviewIssueCode code) => code switch
    {
        CombinationPreviewIssueCode.SourceUnavailable => T("CombinationPresets.RepositoryUnavailableCheckItsPath"),
        CombinationPreviewIssueCode.TargetUnavailable => T("CombinationPresets.DeploymentFolderUnavailableCheckItsPath"),
        CombinationPreviewIssueCode.UnsafeRoots => T("CombinationPresets.UseSeparateRepositoryDeploymentRootsNot"),
        CombinationPreviewIssueCode.UnsafeModPath => T("CombinationPresets.InvalidModPathSaveThePreset"),
        CombinationPreviewIssueCode.MissingMod => T("CombinationPresets.SavedModMissingRestoreItsFolder"),
        CombinationPreviewIssueCode.DisabledMod => T("CombinationPresets.ModFolderDisabledCheckItAnd"),
        CombinationPreviewIssueCode.LinkedSource => T("CombinationPresets.LinkedSourceModItsIdentityCannot"),
        CombinationPreviewIssueCode.DuplicateMod => T("CombinationPresets.DuplicateModInPresetSaveIt"),
        CombinationPreviewIssueCode.AmbiguousTargetName => T("CombinationPresets.MultipleModsShareADeploymentName"),
        CombinationPreviewIssueCode.SnapshotMismatch => T("CombinationPresets.ModListAndStateSnapshotsDiffer"),
        CombinationPreviewIssueCode.ParameterMismatch => T("CombinationPresets.ModOrDeployedCopyDeclarationsChanged"),
        CombinationPreviewIssueCode.LoaderStateInvalid => T("CombinationPresets.CannotSafelyReadLoaderStateCheck"),
        CombinationPreviewIssueCode.DeploymentBlocked => T("CombinationPresets.DeploymentBlockedCheckAmbiguousNamesFiles"),
        _ => T("CombinationPresets.ReadFailedCheckPermissionsAndRetry")
    };
}
