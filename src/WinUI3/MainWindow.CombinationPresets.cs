using IntegratedModManager.Core.Deployment;
using IntegratedModManager.Core.Presets;
using IntegratedModManager.Core.Repository;
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
            _workspaceView.SetRepositoryWorkspaceViewV4(RepositoryWorkspaceView.Presets);
        }
    }

    private void SetCombinationBusy(bool busy)
    {
        _applyingCombinationProfile = busy;
        _workspaceView.SetBusyState(busy);
        ShellNavigationView.IsHitTestVisible = !busy;
        foreach (NavigationViewItem item in new[] { DashboardNavButton, RepositoryNavButton, OnlineNavButton, UpdatesNavButton, SettingsNavButton })
            item.IsEnabled = !busy;
        Paths.ArePathsEditable = !busy;
        _workspaceView.RefreshButton.IsEnabled = !busy;
        _workspaceView.ImportZipButton.IsEnabled = !busy;
        _workspaceView.RunLauncherButton.IsEnabled = !busy;
        RefreshConfigurationProfiles();
        RefreshPersistentSlotControls();
    }

    private async Task SaveCombinationProfileAsync(WorkspaceRepository repository, string name, ModConfigurationProfile? existing)
    {
        bool includeState = _workspaceView.CaptureCombinationStateCheckBox.IsChecked == true;
        string userIni = _workspaceView.CombinationUserIniTextBox.Text.Trim();
        SetCombinationBusy(true);
        try
        {
            (int mods, int count)? saved = await CaptureCombinationProfileAsync(repository, name, existing, includeState, userIni,
                () => ShowConfirmAsync(T("CombinationPresets.NoEnabledRepositoryModsWereRecognized"),
                    T("CombinationPresets.EmptyCombination"), T("Common.Save")));
            if (saved is (int mods, int count))
                ShowAppNotification(T("CombinationPresets.Saved", name, mods, count));
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
        var stillEnabled = await Task.Run(() => ModCombinationDeploymentPolicy.Capture(repository.SourcePath, repository.TargetPath, OwnsCopy(repository)));
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
        string userIni = _workspaceView.CombinationUserIniTextBox.Text.Trim();
        string? transaction = null, stateBackup = null;
        ModPersistentRuntimeRestorePlan? runtimePlan = null;
        bool started = false, committed = false, adopted = false;
        // The copy records go back with the folders when the restore is rolled back.
        List<CopyDeploymentRecord> copyRecords = CopyRecordsSnapshot();
        SetCombinationBusy(true);
        try
        {
            // Freeze the selected preset: asynchronous dialogs must not read a mutable profile.
            var request = new CombinationRestoreRequest(repository.SourcePath, repository.TargetPath,
                profile.ModRelativePaths.ToArray(), repository.UseDirectoryLinks, profile.IncludesPersistentState,
                profile.PersistentStates.Select(state => new CombinationPresetState(state.ModRelativePath, state.Values.ToArray())).ToArray(), userIni,
                OwnsCopy(repository));
            CombinationRestorePreview preview = await Task.Run(() => CombinationRestorePreviewService.Prepare(request));
            SaveCopyRecordsIfChanged();
            foreach (var issue in preview.Issues.Where(issue => issue.Error is not null))
                LogApplicationIssue("Combination preview: " + issue.Code, issue.Error!);
            CombinationRestoreChoice choice = await CombinationRestoreDialog.ConfirmAsync(_services, profile.Name, preview,
                CreatePresetPreviewImage(profile.PreviewFile, 240, 135), RootGrid.ActualWidth, RootGrid.ActualHeight);
            if (choice == CombinationRestoreChoice.TakeOver)
            {
                // The user recognises the external folders as old copies: record them and show the preview again.
                AdoptCopies(repository, preview.Deployment!.Unowned);
                adopted = true;
                return;
            }
            if (choice != CombinationRestoreChoice.Restore) return;
            if (repository.UseDirectoryLinks
                && !await _workspaceView.ConfirmSingleSelectionViolationsAsync(repository, preview.Deployment!.DesiredSources)) return;
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
            CopyDeploymentLedger ledger = CopyLedger(repository);
            foreach (string target in deployment.RemoveTargets)
            {
                await Task.Run(() => DeleteDirectoryTreeSafely(target));
                ledger.Forget(target);
            }
            foreach (string source in deployment.InstallSources)
            {
                await _workspaceView.DeployModAsync(source, ModDeploymentLayout.DeployedPath(repository.TargetPath, source), repository,
                    new Progress<ProgressInfo>(info => _workspaceView.UpdateProgress(info.Percent, T("CombinationPresets.RestoringCombination") + Path.GetFileName(source))));
            }
            SaveShellConfig();
            var actual = await Task.Run(() => ModCombinationDeploymentPolicy.Capture(repository.SourcePath, repository.TargetPath, OwnsCopy(repository)));
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
            await RefreshShortcutDeployedModsAsync();
            _workspaceView.UpdateProgress(100, T("CombinationPresets.CombinationAndLoaderParametersVerified"));
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
                RestoreCopyRecords(copyRecords);
            }
            await ShowMessageAsync(string.Join("\n", errors) + T("CombinationPresets.KeepTheLoaderClosedAndInspect"), T("CombinationPresets.CombinationRestoreIncomplete"));
        }
        finally
        {
            if (started) await _workspaceView.RefreshListsAsync();
            SetCombinationBusy(false);
        }
        if (adopted) await RestoreCombinationProfileAsync();
    }
}
