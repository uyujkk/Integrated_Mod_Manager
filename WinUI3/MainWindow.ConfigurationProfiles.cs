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
                        ? L($" · {profile.PersistentStates.Sum(state => state.Values.Count)} 参数", $" · {profile.PersistentStates.Sum(state => state.Values.Count)} values") : L(" · 仅组合", " · set only")),
                    Tag = profile.Id
                });
            }

            ConfigurationProfileComboBox.PlaceholderText = profiles.Count == 0
                ? L("当前仓库还没有配置方案", "No profiles for this repository")
                : L("选择配置方案", "Select a profile");
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
            ? L("配置方案只会管理当前仓库中能够识别的 Mod。", "Profiles only manage mods recognized in the current repository.")
            : L(
                $"{selected.Name}：{selected.ModRelativePaths.Count} 个 Mod · {selected.PersistentStates.Sum(state => state.Values.Count)} 个参数。" + (selected.IncludesPersistentState ? "恢复组合后回写参数；请先退出游戏/加载器。" : "旧方案/仅组合，不修改加载器参数。"),
                $"{selected.Name}: {selected.ModRelativePaths.Count} mods · {selected.PersistentStates.Sum(state => state.Values.Count)} parameters. " + (selected.IncludesPersistentState ? "Restores deployment then state. Close game/loader first." : "Legacy/set-only profile; loader state is unchanged."));

        RollbackLastInstallButton.IsEnabled = !string.IsNullOrWhiteSpace(_lastInstallTransactionPath)
            && Directory.Exists(_lastInstallTransactionPath);
        InstallRollbackStatusTextBlock.Text = RollbackLastInstallButton.IsEnabled
            ? L("可撤销最近一次 Mod 部署；组合参数另有 d3dx_user.ini.bak 备份。", "Undo covers Mod deployment; combination parameters have a separate d3dx_user.ini .bak backup.")
            : L("目前没有可撤销的 Mod 操作。", "There is no mod operation to undo.");
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
                await ShowMessageAsync(L("请先选择仓库。", "Select a repository first."), L("无法新建方案", "Cannot Create Profile"));
                return;
            }

            string? name = await PromptForTextAsync(
                L("输入组合名称。若勾选保存参数，请先在游戏里按 F10 并等待文件保存完成。", "Name this combination. If including state, press F10 in game and wait for the file to be saved first."),
                L("保存 Mod 组合", "Save Mod Combination"),
                L("常用方案", "My Profile"));
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
            if (!await ShowConfirmAsync(L($"用当前启用组合覆盖“{profile.Name}”？同时保存参数时，请先按 F10 保存游戏状态。",
                $"Replace '{profile.Name}' with the enabled combination? Save state with F10 first if including parameters."),
                L("更新组合预设", "Update Combination Preset"))) return;
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
                L($"确定删除配置方案“{profile.Name}”吗？这不会删除任何 Mod。", $"Delete the profile \"{profile.Name}\"? This will not delete any mods."),
                L("删除配置方案", "Delete Configuration Profile")))
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
