using IntegratedModManager.Core.Deployment;
using IntegratedModManager.Core.Presets;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;

namespace ModFolderCopier.WinUI;

// The combination preset card of the Mod workspace (stage J finish, step 7: moved from the window unchanged): the
// repository's presets, saving, updating, deleting and restoring one, combination bundles and shared presets. The
// presets themselves and capturing them are in CombinationService.
public sealed partial class WorkspacePage
{
    // The window used to subscribe these to the page's controls (AttachWorkspacePageEvents).
    private void AttachCombinationEvents()
    {
        ConfigurationProfileComboBox.SelectionChanged += OnConfigurationProfileSelectionChanged;
        CreateConfigurationProfileButton.Click += OnCreateConfigurationProfileClicked;
        UpdateConfigurationProfileButton.Click += OnUpdateConfigurationProfileClicked;
        ApplyConfigurationProfileButton.Click += OnApplyConfigurationProfileClicked;
        DeleteConfigurationProfileButton.Click += OnDeleteConfigurationProfileClicked;
        _services.Combinations.BusyChanged += OnCombinationBusyChanged;
    }

    private bool _isApplyingConfigurationProfileSelection;

    // The page's half of a preset operation's busy state (the window locks its navigation and path boxes).
    private void OnCombinationBusyChanged(object? sender, EventArgs e)
    {
        bool busy = _services.Combinations.IsApplying;
        SetBusyState(busy);
        RefreshButton.IsEnabled = !busy;
        ImportZipButton.IsEnabled = !busy;
        RunLauncherButton.IsEnabled = !busy;
        RefreshConfigurationProfiles();
        RefreshPersistentSlotControls();
    }

    internal void RefreshConfigurationProfiles()
    {
        if (ConfigurationProfileComboBox is null)
        {
            return;
        }

        List<ModConfigurationProfile> profiles = [.. _services.Combinations.CurrentRepositoryProfiles()];
        if (!profiles.Any(profile => profile.Id == _services.Combinations.SelectedId))
        {
            _services.Combinations.SelectedId = profiles.FirstOrDefault()?.Id;
        }

        _isApplyingConfigurationProfileSelection = true;
        try
        {
            ConfigurationProfileComboBox.Items.Clear();
            foreach (ModConfigurationProfile profile in profiles)
            {
                ConfigurationProfileComboBox.Items.Add(new ComboBoxItem
                {
                    Content = $"{profile.Name}  ·  {profile.ModRelativePaths.Count} Mod" + (profile.IncludesPersistentState
                        ? T("ConfigurationProfiles.Values", profile.PersistentStates.Sum(state => state.Values.Count)) : T("ConfigurationProfiles.SetOnly")),
                    Tag = profile.Id
                });
            }

            ConfigurationProfileComboBox.PlaceholderText = profiles.Count == 0
                ? T("ConfigurationProfiles.NoProfilesForThisRepository")
                : T("ConfigurationProfiles.SelectAProfile");
            ConfigurationProfileComboBox.SelectedItem = ConfigurationProfileComboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Tag as string, _services.Combinations.SelectedId, StringComparison.Ordinal));
        }
        finally
        {
            _isApplyingConfigurationProfileSelection = false;
        }

        ModConfigurationProfile? selected = _services.Combinations.Selected();
        bool hasRepository = _services.Repositories.Selected is not null && !_services.Combinations.IsApplying && !_services.Slots.IsRestoring;
        CreateConfigurationProfileButton.IsEnabled = hasRepository;
        UpdateConfigurationProfileButton.IsEnabled = selected is not null && hasRepository;
        ApplyConfigurationProfileButton.IsEnabled = selected is not null && hasRepository;
        DeleteConfigurationProfileButton.IsEnabled = selected is not null && hasRepository;
        ConfigurationProfileComboBox.IsEnabled = hasRepository;
        CaptureCombinationStateCheckBox.IsEnabled = hasRepository;
        BrowseCombinationUserIniButton.IsEnabled = hasRepository;
        CombinationUserIniTextBox.IsEnabled = hasRepository;
        CombinationUserIniTextBox.Text = _services.Repositories.Selected?.PersistentUserIniPath is { Length: > 0 } userIni
            ? userIni : SlotService.TryGetDefaultPersistentIniPath(_services.Repositories.Selected) ?? string.Empty;
        ConfigurationProfileSummaryTextBlock.Text = selected is null
            ? T("ConfigurationProfiles.ProfilesOnlyManageModsRecognizedIn")
            : T("ConfigurationProfiles.Summary", selected.Name, selected.ModRelativePaths.Count, selected.PersistentStates.Sum(state => state.Values.Count))
                + (selected.IncludesPersistentState ? T("ConfigurationProfiles.SummaryWithState") : T("ConfigurationProfiles.SummarySetOnly"));

        _services.Install.ShowUndoState();
        RefreshCombinationWorkspaceV4();
        RefreshCombinationBundleActions();
    }

    private async void OnCreateConfigurationProfileClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_services.Combinations.IsApplying || _services.Slots.IsRestoring) return;
            WorkspaceRepository? repository = _services.Repositories.Selected;
            if (repository is null)
            {
                await ShowMessageAsync(T("Common.SelectARepositoryFirst"), T("ConfigurationProfiles.CannotCreateProfile"));
                return;
            }

            string? name = await PromptForTextAsync(
                T("ConfigurationProfiles.NameThisCombinationIfIncludingState"),
                T("ConfigurationProfiles.SaveModCombination"),
                T("ConfigurationProfiles.MyProfile"), T("Common.Save"));
            name = name?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            await SaveCombinationProfileAsync(repository, name, null);
        }
        catch (Exception ex)
        {
            _host.ReportUiActionFailure("OnCreateConfigurationProfileClicked", ex);
        }
    }

    private async void OnUpdateConfigurationProfileClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            ModConfigurationProfile? profile = _services.Combinations.Selected();
            if (profile is null || _services.Combinations.IsApplying || _services.Slots.IsRestoring)
            {
                return;
            }

            WorkspaceRepository? repository = _services.Repositories.Selected;
            if (repository is null) return;
            if (!await ShowDangerConfirmAsync(T("ConfigurationProfiles.ReplaceWithTheEnabledCombinationSave", profile.Name),
                T("ConfigurationProfiles.UpdateCombinationPreset"), T("Dialogs.Replace"))) return;
            await SaveCombinationProfileAsync(repository, profile.Name, profile);
        }
        catch (Exception ex)
        {
            _host.ReportUiActionFailure("OnUpdateConfigurationProfileClicked", ex);
        }
    }

    private async void OnDeleteConfigurationProfileClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            ModConfigurationProfile? profile = _services.Combinations.Selected();
            if (profile is null)
            {
                return;
            }

            if (!await ShowDangerConfirmAsync(
                T("ConfigurationProfiles.DeleteTheProfileThisWillNot", profile.Name),
                T("ConfigurationProfiles.DeleteConfigurationProfile"), T("Dialogs.Delete")))
            {
                return;
            }

            _services.Combinations.Profiles.Remove(profile);
            _services.Combinations.SelectedId = null;
            _host.SaveShellConfig();
            _services.PresetPreviews.DeletePresetPreview(profile.PreviewFile);
            RefreshConfigurationProfiles();
        }
        catch (Exception ex)
        {
            _host.ReportUiActionFailure("OnDeleteConfigurationProfileClicked", ex);
        }
    }

    private void OnConfigurationProfileSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isApplyingConfigurationProfileSelection)
        {
            return;
        }

        _services.Combinations.SelectedId = (ConfigurationProfileComboBox.SelectedItem as ComboBoxItem)?.Tag as string;
        _host.SaveShellConfig();
        RefreshConfigurationProfiles();
    }

    private async void OnApplyConfigurationProfileClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            await RestoreCombinationProfileAsync();
        }
        catch (Exception ex)
        {
            _host.ReportUiActionFailure("OnApplyConfigurationProfileClicked", ex);
        }
    }

    private async Task SaveCombinationProfileAsync(WorkspaceRepository repository, string name, ModConfigurationProfile? existing)
    {
        bool includeState = CaptureCombinationStateCheckBox.IsChecked == true;
        string userIni = CombinationUserIniTextBox.Text.Trim();
        _services.Combinations.SetBusy(true);
        try
        {
            (int mods, int count)? saved = await _services.Combinations.CaptureAsync(repository, name, existing, includeState, userIni,
                () => ShowConfirmAsync(T("CombinationPresets.NoEnabledRepositoryModsWereRecognized"),
                    T("CombinationPresets.EmptyCombination"), T("Common.Save")));
            if (saved is (int mods, int count))
                ShowAppNotification(T("CombinationPresets.Saved", name, mods, count));
        }
        catch (Exception ex)
        {
            _host.LogApplicationIssue("Combination capture", ex);
            await ShowMessageAsync(T("CombinationPresets.CombinationWasNotSavedCheckDeployment")
                + ex.Message, T("CombinationPresets.CombinationSaveFailed"));
        }
        finally { _services.Combinations.SetBusy(false); }
    }

    private async Task RestoreCombinationProfileAsync()
    {
        if (_services.Combinations.IsApplying || _services.Slots.IsRestoring) return;
        ModConfigurationProfile? profile = _services.Combinations.Selected();
        WorkspaceRepository? repository = _services.Repositories.Selected;
        if (profile is null || repository is null) return;
        string userIni = CombinationUserIniTextBox.Text.Trim();
        string? transaction = null, stateBackup = null;
        ModPersistentRuntimeRestorePlan? runtimePlan = null;
        bool started = false, committed = false, adopted = false;
        // The copy records go back with the folders when the restore is rolled back.
        List<CopyDeploymentRecord> copyRecords = _services.Install.CopyRecordsSnapshot();
        _services.Combinations.SetBusy(true);
        try
        {
            // Freeze the selected preset: asynchronous dialogs must not read a mutable profile.
            var request = new CombinationRestoreRequest(repository.SourcePath, repository.TargetPath,
                profile.ModRelativePaths.ToArray(), repository.UseDirectoryLinks, profile.IncludesPersistentState,
                profile.PersistentStates.Select(state => new CombinationPresetState(state.ModRelativePath, state.Values.ToArray())).ToArray(), userIni,
                _services.Install.OwnsCopy(repository));
            CombinationRestorePreview preview = await Task.Run(() => CombinationRestorePreviewService.Prepare(request));
            _services.Install.SaveCopyRecordsIfChanged();
            foreach (var issue in preview.Issues.Where(issue => issue.Error is not null))
                _host.LogApplicationIssue("Combination preview: " + issue.Code, issue.Error!);
            CombinationRestoreChoice choice = await CombinationRestoreDialog.ConfirmAsync(_services, profile.Name, preview,
                _services.PresetPreviews.CreatePresetPreviewImage(profile.PreviewFile, 240, 135), _host.Root.ActualWidth, _host.Root.ActualHeight);
            if (choice == CombinationRestoreChoice.TakeOver)
            {
                // The user recognises the external folders as old copies: record them and show the preview again.
                _services.Install.AdoptCopies(repository, preview.Deployment!.Unowned);
                adopted = true;
                return;
            }
            if (choice != CombinationRestoreChoice.Restore) return;
            if (repository.UseDirectoryLinks
                && !await ConfirmSingleSelectionViolationsAsync(repository, preview.Deployment!.DesiredSources)) return;
            ModCombinationDeploymentPlan deployment = preview.Deployment!;
            runtimePlan = preview.Runtime;

            if (_host.EnableConflictDetection)
            {
                string[] deploymentTargets = deployment.RemoveTargets
                    .Concat(deployment.InstallSources.Select(item => ModDeploymentLayout.DeployedPath(repository.TargetPath, item))).ToArray();
                List<string> conflicts = await Task.Run(() => deployment.InstallSources.SelectMany(source => _services.Install.DetectModFileConflicts(
                    source, repository.TargetPath, deploymentTargets))
                    .Distinct(StringComparer.OrdinalIgnoreCase).Take(100).ToList());
                List<(string? Candidate, ModIniConflictSummary Summary)> iniConflicts = await Task.Run(() => deployment.InstallSources
                    .SelectMany(source => _services.Install.DetectModIniConflictsSafely(source, repository.TargetPath, deploymentTargets)
                        .Select(summary => ((string?)Path.GetFileName(source), summary)))
                    .ToList());
                if ((conflicts.Count > 0 || iniConflicts.Count > 0) && !await _services.Install.ConfirmInstallConflictsAsync(conflicts, iniConflicts)) return;
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
                transaction = await _services.Install.CreateInstallTransactionAsync(T("CombinationPresets.RestoreCombination", profile.Name), repository, affected);
            started = true;
            CopyDeploymentLedger ledger = _services.Install.CopyLedger(repository);
            foreach (string target in deployment.RemoveTargets)
            {
                await Task.Run(() => _services.Install.DeleteDirectoryTreeSafely(target));
                ledger.Forget(target);
            }
            foreach (string source in deployment.InstallSources)
            {
                await DeployModAsync(source, ModDeploymentLayout.DeployedPath(repository.TargetPath, source), repository,
                    new Progress<ProgressInfo>(info => UpdateProgress(info.Percent, T("CombinationPresets.RestoringCombination") + Path.GetFileName(source))));
            }
            _host.SaveShellConfig();
            var actual = await Task.Run(() => ModCombinationDeploymentPolicy.Capture(repository.SourcePath, repository.TargetPath, _services.Install.OwnsCopy(repository)));
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
            if (transaction is not null) _services.Install.CommitInstallTransaction(transaction);
            committed = true;
            await _host.RefreshShortcutDeployedModsAsync();
            UpdateProgress(100, T("CombinationPresets.CombinationAndLoaderParametersVerified"));
            await ShowMessageAsync(T("CombinationPresets.RestoredModsParameters", profile.Name, actual.Count, runtimePlan?.Values.Count ?? 0)
                + (stateBackup is null ? T("CombinationPresets.StateWasUnchangedNoNewState") : T("CombinationPresets.StateBackup") + stateBackup)
                + T("CombinationPresets.RestartTheLoaderAndGameTo"), T("CombinationPresets.CombinationRestored"));
        }
        catch (Exception ex)
        {
            _host.LogApplicationIssue("Combination restore", ex);
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
                    try { await _services.Install.RollbackInstallTransactionAsync(transaction, clearLastTransaction: true); }
                    catch (Exception rollback) { errors.Add(T("CombinationPresets.DeploymentRollbackFailedInspectBackup") + rollback.Message); }
                }
                _services.Install.RestoreCopyRecords(copyRecords);
            }
            await ShowMessageAsync(string.Join("\n", errors) + T("CombinationPresets.KeepTheLoaderClosedAndInspect"), T("CombinationPresets.CombinationRestoreIncomplete"));
        }
        finally
        {
            if (started) await RefreshListsAsync();
            _services.Combinations.SetBusy(false);
        }
        if (adopted) await RestoreCombinationProfileAsync();
    }

    private Button? _exportCombinationBundleButton, _importCombinationBundleButton, _cancelCombinationBundleButton;
    private Button? _exportPresetShareButton, _importPresetShareButton;
    internal CancellationTokenSource? _bundleOperationCancellation;

    private void InitializeCombinationBundleActions()
    {
        // Keep Create in its known owner. Reparenting it before the grid is attached
        // makes Parent unreliable and can throw when it is added to a second owner.
        // Bundle commands span the workspace, not the constrained preset-list footer.
        var actions = new StackPanel { Spacing = 8, Orientation = Orientation.Horizontal };
        _exportCombinationBundleButton = WorkspacePage.RepositoryButton("\uE896", async (_, _) => await ExportCombinationBundleAsync());
        _importCombinationBundleButton = WorkspacePage.RepositoryButton("\uE898", async (_, _) => await ImportCombinationBundleAsync());
        _cancelCombinationBundleButton = WorkspacePage.RepositoryButton("\uE711", (_, _) =>
        {
            _bundleOperationCancellation?.Cancel();
            RefreshCombinationBundleActions();
        });
        _exportPresetShareButton = WorkspacePage.RepositoryButton("\uE72D", async (_, _) => await _host.RunUiActionAsync("Share preset", ExportPresetShareAsync));
        _importPresetShareButton = WorkspacePage.RepositoryButton("\uE8B5", async (_, _) => await _host.RunUiActionAsync("Import shared preset", ImportPresetShareAsync));
        AutomationProperties.SetAutomationId(_exportPresetShareButton, "PresetShareExportButton");
        AutomationProperties.SetAutomationId(_importPresetShareButton, "PresetShareImportButton");
        foreach (Button button in new[] { _exportCombinationBundleButton, _importCombinationBundleButton, _exportPresetShareButton, _importPresetShareButton, _cancelCombinationBundleButton })
        {
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            actions.Children.Add(button);
        }
        Grid.SetColumnSpan(actions, 3);
        _combinationWorkspace!.Children.Add(actions);
        RefreshCombinationBundleActions();
    }

    private void RefreshCombinationBundleActions()
    {
        if (_exportCombinationBundleButton is null) return;
        bool available = !_services.Combinations.IsApplying && !_services.Slots.IsRestoring
            && _services.Repositories.Selected is { SourcePath.Length: > 0 } repository && Directory.Exists(repository.SourcePath);
        SetLabel(_exportCombinationBundleButton, "\uE896", T("CombinationBundles.ExportBundle"));
        SetLabel(_importCombinationBundleButton!, "\uE898", T("CombinationBundles.InstallBundle"));
        SetLabel(_cancelCombinationBundleButton!, "\uE711", T("CombinationBundles.CancelTask"));
        SetLabel(_exportPresetShareButton!, "\uE72D", T("PresetShare.Export"));
        SetLabel(_importPresetShareButton!, "\uE8B5", T("PresetShare.Import"));
        _exportPresetShareButton!.IsEnabled = available && _services.Combinations.Selected() is not null;
        _importPresetShareButton!.IsEnabled = available;
        _exportCombinationBundleButton.IsEnabled = available && _services.Combinations.Selected() is not null;
        _importCombinationBundleButton!.IsEnabled = available;
        _cancelCombinationBundleButton!.Visibility = _bundleOperationCancellation is null ? Visibility.Collapsed : Visibility.Visible;
        _cancelCombinationBundleButton.IsEnabled = _bundleOperationCancellation is { IsCancellationRequested: false };

        static void SetLabel(Button button, string glyph, string text)
        {
            button.Content = WorkspacePage.RepositoryIconLabel(glyph, text);
            ToolTipService.SetToolTip(button, text);
            AutomationProperties.SetName(button, text);
        }
    }

    private IProgress<CombinationBundleProgress> BundleProgress(CancellationToken token, string operation) =>
        new Progress<CombinationBundleProgress>(info =>
        {
            if (!_services.IsClosing && !token.IsCancellationRequested && _bundleOperationCancellation?.Token == token)
                UpdateProgress(info.Percent, operation + (info.RelativePath.Length == 0 ? "" : " · " + info.RelativePath));
        });

    private async Task ExportCombinationBundleAsync()
    {
        if (_services.Combinations.IsApplying || _services.Slots.IsRestoring || _services.Repositories.Selected is not { } repository
            || _services.Combinations.Selected() is not { } profile) return;
        string root = repository.SourcePath, name = profile.Name, game = repository.OnlineGameName;
        bool includeState = profile.IncludesPersistentState;
        // Freeze the saved profile, not live loader values, before any picker/dialog awaits.
        CombinationPresetState[] mods;
        try
        {
            var states = profile.PersistentStates.ToDictionary(state => state.ModRelativePath, StringComparer.OrdinalIgnoreCase);
            if (includeState && !states.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(profile.ModRelativePaths))
                throw new InvalidDataException(T("CombinationBundles.SavedStateAndModListDo"));
            mods = profile.ModRelativePaths.Select(path => new CombinationPresetState(path,
                includeState ? states[path].Values.ToArray() : [])).ToArray();
        }
        catch (Exception ex)
        {
            await ReportBundleErrorAsync(ex, false);
            return;
        }
        await ExportModsAsBundleAsync(root, name, game, includeState, mods, T("CombinationBundles.ExportSavedPresetStateNotCurrent"));
    }

    internal async Task ExportModsAsBundleAsync(string root, string name, string game, bool includeState, CombinationPresetState[] mods, string confirmation)
    {
        using var cancellation = new CancellationTokenSource();
        _bundleOperationCancellation = cancellation;
        _services.Combinations.SetBusy(true);
        try
        {
            if (!await ShowConfirmAsync(confirmation, T("CombinationBundles.ExportBundle"), T("Dialogs.Export"))) return;
            cancellation.Token.ThrowIfCancellationRequested();
            string? folder = await _services.Dialogs.PickFolderAsync();
            cancellation.Token.ThrowIfCancellationRequested();
            if (folder is null || _services.IsClosing) return;
            string output = Path.Combine(folder, $"Combination-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..8]}.imm-preset.zip");
            var progress = BundleProgress(cancellation.Token, T("CombinationBundles.ExportingBundle"));
            await Task.Run(() => CombinationBundleService.Export(root, output, name, game, includeState, mods,
                cancellation.Token, progress: progress));
            await _services.OnlineDownloads.ReportOnlineActionErrorAsync(T("CombinationBundles.BundleCreatedModsSavedValues", mods.Length, mods.Sum(mod => mod.Values.Count), output), T("CombinationBundles.ExportComplete"));
        }
        catch (OperationCanceledException) { if (!_services.IsClosing) ShowAppNotification(T("CombinationBundles.ExportCanceled")); }
        catch (Exception ex) { await ReportBundleErrorAsync(ex, false); }
        finally
        {
            _bundleOperationCancellation = null;
            if (!_services.IsClosing) _services.Combinations.SetBusy(false);
        }
    }

    private async Task ImportCombinationBundleAsync()
    {
        if (_services.Combinations.IsApplying || _services.Slots.IsRestoring || _services.Repositories.Selected is not { } repository) return;
        string root = repository.SourcePath, repositoryId = repository.Id;
        using var cancellation = new CancellationTokenSource();
        _bundleOperationCancellation = cancellation;
        _services.Combinations.SetBusy(true);
        CombinationBundleImport? import = null;
        bool restore = false;
        try
        {
            string? archive = await _services.Dialogs.PickFileAsync([".zip"]);
            cancellation.Token.ThrowIfCancellationRequested();
            if (archive is null || _services.IsClosing) return;
            var validationProgress = BundleProgress(cancellation.Token, T("CombinationBundles.ValidatingBundle"));
            import = await Task.Run(() => CombinationBundleService.PrepareImport(archive, root,
                CombinationBundleService.CreateStagingParent(root), cancellation.Token,
                progress: validationProgress));
            cancellation.Token.ThrowIfCancellationRequested();
            if (_services.IsClosing) return;
            CombinationBundleManifest manifest = import.Manifest;
            var confirmation = await CombinationBundleDialog.ConfirmInstallAsync(_services, import, repository.OnlineGameName);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!confirmation.Install || _services.IsClosing) return;
            if (_services.Repositories.Selected is not { } current || current.Id != repositoryId || current.SourcePath != root)
                throw new IOException(T("CombinationBundles.RepositoryChangedSelectTheBundleAgain"));
            var progress = BundleProgress(cancellation.Token, T("CombinationBundles.InstallingBundle"));
            await Task.Run(() => import.InstallFiles(cancellation.Token, progress));
            cancellation.Token.ThrowIfCancellationRequested();
            var profile = new ModConfigurationProfile
            {
                RepositoryId = repositoryId, Name = _services.Combinations.UniqueName(manifest.Name),
                ModRelativePaths = manifest.Mods.Select(mod => mod.RelativePath.Replace('/', Path.DirectorySeparatorChar)).ToList(),
                IncludesPersistentState = manifest.IncludesPersistentState,
                PersistentStates = manifest.IncludesPersistentState ? manifest.Mods.Select(mod => new ModConfigurationPersistentState
                {
                    ModRelativePath = mod.RelativePath.Replace('/', Path.DirectorySeparatorChar), Values = mod.Values.ToList()
                }).ToList() : []
            };
            string? previousSelection = _services.Combinations.SelectedId;
            _services.Combinations.Profiles.Add(profile);
            _services.Combinations.SelectedId = profile.Id;
            try
            {
                // No await/cancel gap between atomic configuration publication and file ownership commit.
                _host.SaveShellConfig(throwOnError: true);
                import.Complete();
            }
            catch
            {
                _services.Combinations.Profiles.Remove(profile);
                _services.Combinations.SelectedId = previousSelection;
                throw;
            }
            restore = confirmation.Restore;
            if (!_services.IsClosing)
            {
                await RefreshListsAsync();
                ShowAppNotification(T("CombinationBundles.InstalledAndSaved", profile.Name));
            }
        }
        catch (OperationCanceledException) { if (!_services.IsClosing) ShowAppNotification(T("CombinationBundles.InstallCanceled")); }
        catch (Exception ex) { restore = false; await ReportBundleErrorAsync(ex, import?.IsCommitted == true); }
        finally
        {
            import?.Dispose();
            _bundleOperationCancellation = null;
            if (!_services.IsClosing)
            {
                _services.Combinations.SetBusy(false);
                if (import?.CleanupWarnings.Count > 0)
                    await _services.OnlineDownloads.ReportOnlineActionErrorAsync(string.Join("\n", import.CleanupWarnings), T("CombinationBundles.SomeFilesNeedInspection"));
            }
        }
        // Restore is a separate approved transaction: deploy the set, then patch selected d3dx_user.ini keys.
        // Cancelling/failing that step leaves the imported files and saved profile available for retry.
        if (restore && import?.IsCommitted == true && !_services.IsClosing) await RestoreCombinationProfileAsync();
    }

    private async Task ReportBundleErrorAsync(Exception ex, bool committed)
    {
        _host.LogApplicationIssue("Combination bundle", ex);
        await _services.OnlineDownloads.ReportOnlineActionErrorAsync((committed ? T("CombinationBundles.InstalledButRefreshFailed") : T("CombinationBundles.OperationNotFinished")) + "\n" + ex.Message,
            T("CombinationBundles.CombinationBundle"), ex);
    }

    private async Task ExportPresetShareAsync()
    {
        if (_services.Repositories.Selected is not { } repository || _services.Combinations.Selected() is not { } profile) return;
        var states = profile.PersistentStates.ToDictionary(state => state.ModRelativePath, StringComparer.OrdinalIgnoreCase);
        CombinationPresetState[] mods = profile.ModRelativePaths.Select(path => new CombinationPresetState(path,
            profile.IncludesPersistentState && states.TryGetValue(path, out var state) ? state.Values.ToArray() : [])).ToArray();
        string json;
        try { json = PresetShareService.Serialize(profile.Name, repository.OnlineGameName, profile.IncludesPersistentState, mods); }
        catch (PresetShareTooLargeException)
        {
            await ShowMessageAsync(T("PresetShare.ExportTooLarge", PresetShareService.MaxBytes / 1024 / 1024), T("PresetShare.Export"));
            return;
        }
        catch (InvalidDataException ex)
        {
            await ShowMessageAsync(ex.Message, T("PresetShare.Export"));
            return;
        }
        if (await _services.Dialogs.PickFolderAsync() is not { } folder) return;
        string safeName = string.Concat(profile.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
        string path = Path.Combine(folder, (safeName.Length == 0 ? "preset" : safeName) + PresetShareService.FileExtension);
        for (int suffix = 2; File.Exists(path); suffix++) path = Path.Combine(folder, $"{safeName} ({suffix}){PresetShareService.FileExtension}");
        await File.WriteAllTextAsync(path, json, new System.Text.UTF8Encoding(false));
        _services.Workspace.Status = T("PresetShare.Exported", profile.Name, path);
    }

    private async Task ImportPresetShareAsync()
    {
        if (_services.Combinations.IsApplying || _services.Slots.IsRestoring || _services.Repositories.Selected is not { } repository) return;
        if (await _services.Dialogs.PickFileAsync([".json"]) is not { } path) return;
        PresetShareFile file;
        try
        {
            if (new FileInfo(path).Length > PresetShareService.MaxBytes) throw new InvalidDataException(T("PresetShare.TooLarge"));
            file = PresetShareService.Parse(await File.ReadAllTextAsync(path));
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            await ShowMessageAsync(ex.Message, T("PresetShare.Import"));
            return;
        }
        string root = repository.SourcePath;
        string[] available = await Task.Run(() => CombinationService.RepositoryModRelativePaths(root));
        PresetSharePreview preview = PresetShareService.Preview(file, available, repository.OnlineGameName);

        string? chosenName = await PresetShareImportDialog.ShowAsync(_services, file, preview, repository.OnlineGameName, _services.Combinations.UniqueName(file.Name), _host.Root.ActualHeight);
        if (chosenName is null || !ReferenceEquals(_services.Repositories.Selected, repository)) return;

        string name = _services.Combinations.UniqueName(chosenName);
        var profile = new ModConfigurationProfile
        {
            RepositoryId = repository.Id, Name = name,
            ModRelativePaths = preview.Matched.Select(mod => mod.RelativePath.Replace('/', Path.DirectorySeparatorChar)).ToList(),
            IncludesPersistentState = file.IncludesPersistentState,
            PersistentStates = file.IncludesPersistentState ? preview.Matched.Select(mod => new ModConfigurationPersistentState
            {
                ModRelativePath = mod.RelativePath.Replace('/', Path.DirectorySeparatorChar), Values = [.. mod.Values]
            }).ToList() : []
        };
        _services.Combinations.Profiles.Add(profile);
        _services.Combinations.SelectedId = profile.Id;
        // A state slot per Mod under the same name, so each look can also be restored on its own. Never replaces one.
        int slots = 0;
        foreach (PresetShareMod mod in preview.Matched.Where(mod => mod.Values.Count > 0))
        {
            string relative = mod.RelativePath.Replace('/', Path.DirectorySeparatorChar);
            if (_services.Slots.Items.Any(slot => slot.RepositoryId == repository.Id && slot.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                && slot.ModRelativePath.Equals(relative, StringComparison.OrdinalIgnoreCase))) continue;
            _services.Slots.Items.Add(new ModPersistentSlot { RepositoryId = repository.Id, ModRelativePath = relative, Name = name, Values = [.. mod.Values] });
            slots++;
        }
        _host.SaveShellConfig();
        RefreshConfigurationProfiles();
        RefreshPersistentSlotControls();
        _services.Workspace.Status = T("PresetShare.Imported", name, preview.Matched.Count, slots);
    }
}
