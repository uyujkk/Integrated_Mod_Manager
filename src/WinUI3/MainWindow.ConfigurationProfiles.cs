using IntegratedModManager.Core.Deployment;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private IEnumerable<ModConfigurationProfile> GetCurrentRepositoryProfiles()
    {
        return _configurationProfiles
            .Where(profile => string.Equals(profile.RepositoryId, _selectedRepositoryId, StringComparison.Ordinal))
            .OrderBy(profile => profile.Name, StringComparer.CurrentCultureIgnoreCase);
    }

    private ModConfigurationProfile? GetSelectedConfigurationProfile()
    {
        return _configurationProfiles.FirstOrDefault(profile =>
            string.Equals(profile.Id, _selectedConfigurationProfileId, StringComparison.Ordinal)
            && string.Equals(profile.RepositoryId, _selectedRepositoryId, StringComparison.Ordinal));
    }

    private void RefreshConfigurationProfiles()
    {
        if (ConfigurationProfileComboBox is null)
        {
            return;
        }

        List<ModConfigurationProfile> profiles = [.. GetCurrentRepositoryProfiles()];
        if (!profiles.Any(profile => profile.Id == _selectedConfigurationProfileId))
        {
            _selectedConfigurationProfileId = profiles.FirstOrDefault()?.Id;
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
                .FirstOrDefault(item => string.Equals(item.Tag as string, _selectedConfigurationProfileId, StringComparison.Ordinal));
        }
        finally
        {
            _isApplyingConfigurationProfileSelection = false;
        }

        ModConfigurationProfile? selected = GetSelectedConfigurationProfile();
        bool hasRepository = GetSelectedRepository() is not null && !_applyingCombinationProfile && !_restoringPersistentRuntime;
        CreateConfigurationProfileButton.IsEnabled = hasRepository;
        UpdateConfigurationProfileButton.IsEnabled = selected is not null && hasRepository;
        ApplyConfigurationProfileButton.IsEnabled = selected is not null && hasRepository;
        DeleteConfigurationProfileButton.IsEnabled = selected is not null && hasRepository;
        ConfigurationProfileComboBox.IsEnabled = hasRepository;
        CaptureCombinationStateCheckBox.IsEnabled = hasRepository;
        BrowseCombinationUserIniButton.IsEnabled = hasRepository;
        CombinationUserIniTextBox.IsEnabled = hasRepository;
        CombinationUserIniTextBox.Text = GetSelectedRepository()?.PersistentUserIniPath is { Length: > 0 } userIni
            ? userIni : TryGetDefaultPersistentIniPath(GetSelectedRepository()) ?? string.Empty;
        ConfigurationProfileSummaryTextBlock.Text = selected is null
            ? T("ConfigurationProfiles.ProfilesOnlyManageModsRecognizedIn")
            : L(
                $"{selected.Name}：{selected.ModRelativePaths.Count} 个 Mod · {selected.PersistentStates.Sum(state => state.Values.Count)} 个参数。" + (selected.IncludesPersistentState ? "恢复组合后回写参数；请先退出游戏/加载器。" : "旧方案/仅组合，不修改加载器参数。"),
                $"{selected.Name}: {selected.ModRelativePaths.Count} mods · {selected.PersistentStates.Sum(state => state.Values.Count)} parameters. " + (selected.IncludesPersistentState ? "Restores deployment then state. Close game/loader first." : "Legacy/set-only profile; loader state is unchanged."));

        RollbackLastInstallButton.IsEnabled = !string.IsNullOrWhiteSpace(_lastInstallTransactionPath)
            && Directory.Exists(_lastInstallTransactionPath);
        InstallRollbackStatusTextBlock.Text = RollbackLastInstallButton.IsEnabled
            ? T("ConfigurationProfiles.UndoCoversModDeploymentCombinationParameters")
            : T("ConfigurationProfiles.ThereIsNoModOperationTo");
        // With nothing to undo the button would only sit disabled; the line below says why,
        // and the combination shortcut beside it takes the full row.
        bool canUndo = RollbackLastInstallButton.IsEnabled;
        RollbackLastInstallButton.Visibility = canUndo ? Visibility.Visible : Visibility.Collapsed;
        UpdatesRecoveryActionsGrid.ColumnDefinitions[0].Width = canUndo ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        UpdatesRecoveryActionsGrid.ColumnSpacing = canUndo ? 8 : 0;
        RefreshCombinationWorkspaceV4();
        RefreshCombinationBundleActions();
    }

    private List<string> CaptureCurrentConfigurationProfile()
    {
        WorkspaceRepository? repository = GetSelectedRepository();
        if (repository is null) throw new InvalidOperationException("Select a repository first.");
        return [.. ModCombinationDeploymentPolicy.Capture(repository.SourcePath, repository.TargetPath)];
    }

    private async void OnCreateConfigurationProfileClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_applyingCombinationProfile || _restoringPersistentRuntime) return;
            WorkspaceRepository? repository = GetSelectedRepository();
            if (repository is null)
            {
                await ShowMessageAsync(T("Common.SelectARepositoryFirst"), T("ConfigurationProfiles.CannotCreateProfile"));
                return;
            }

            string? name = await PromptForTextAsync(
                T("ConfigurationProfiles.NameThisCombinationIfIncludingState"),
                T("ConfigurationProfiles.SaveModCombination"),
                T("ConfigurationProfiles.MyProfile"));
            name = name?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            await SaveCombinationProfileAsync(repository, name, null);
        }
        catch (Exception ex)
        {
            ReportUiActionFailure("OnCreateConfigurationProfileClicked", ex);
        }
    }

    private async void OnUpdateConfigurationProfileClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            ModConfigurationProfile? profile = GetSelectedConfigurationProfile();
            if (profile is null || _applyingCombinationProfile || _restoringPersistentRuntime)
            {
                return;
            }

            WorkspaceRepository? repository = GetSelectedRepository();
            if (repository is null) return;
            if (!await ShowConfirmAsync(T("ConfigurationProfiles.ReplaceWithTheEnabledCombinationSave", profile.Name),
                T("ConfigurationProfiles.UpdateCombinationPreset"))) return;
            await SaveCombinationProfileAsync(repository, profile.Name, profile);
        }
        catch (Exception ex)
        {
            ReportUiActionFailure("OnUpdateConfigurationProfileClicked", ex);
        }
    }

    private async void OnDeleteConfigurationProfileClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            ModConfigurationProfile? profile = GetSelectedConfigurationProfile();
            if (profile is null)
            {
                return;
            }

            if (!await ShowConfirmAsync(
                T("ConfigurationProfiles.DeleteTheProfileThisWillNot", profile.Name),
                T("ConfigurationProfiles.DeleteConfigurationProfile")))
            {
                return;
            }

            _configurationProfiles.Remove(profile);
            _selectedConfigurationProfileId = null;
            SaveShellConfig();
            RefreshConfigurationProfiles();
        }
        catch (Exception ex)
        {
            ReportUiActionFailure("OnDeleteConfigurationProfileClicked", ex);
        }
    }

    private void OnConfigurationProfileSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isApplyingConfigurationProfileSelection)
        {
            return;
        }

        _selectedConfigurationProfileId = (ConfigurationProfileComboBox.SelectedItem as ComboBoxItem)?.Tag as string;
        SaveShellConfig();
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
            ReportUiActionFailure("OnApplyConfigurationProfileClicked", ex);
        }
    }
}
