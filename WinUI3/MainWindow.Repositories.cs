using System.Diagnostics;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private WorkspaceRepository? GetSelectedRepository()
    {
        return _repositories.FirstOrDefault(repository => repository.Id == _selectedRepositoryId);
    }

    private void SaveSelectedRepositoryFromInputs()
    {
        CaptureOnlineBrowseSession();
        WorkspaceRepository? repository = GetSelectedRepository();
        if (repository is null)
        {
            return;
        }

        repository.SourcePath = SourceTextBox.Text ?? string.Empty;
        repository.TargetPath = TargetTextBox.Text ?? string.Empty;
        repository.LauncherPath = LauncherTextBox.Text ?? string.Empty;
    }

    private void ApplySelectedRepositoryToInputs()
    {
        WorkspaceRepository? repository = GetSelectedRepository();
        if (repository is null)
        {
            _isApplyingRepositoryDeploymentMode = true;
            LinkDeploymentToggleSwitch.IsOn = false;
            _isApplyingRepositoryDeploymentMode = false;
            RefreshPersistentSlotControls();
            return;
        }

        SourceTextBox.Text = repository.SourcePath;
        TargetTextBox.Text = repository.TargetPath;
        LauncherTextBox.Text = repository.LauncherPath;
        RefreshEndfieldBridgeControls();
        RefreshPersistentSlotControls();
        _isApplyingRepositoryDeploymentMode = true;
        try
        {
            LinkDeploymentToggleSwitch.IsOn = repository.UseDirectoryLinks;
        }
        finally
        {
            _isApplyingRepositoryDeploymentMode = false;
        }
    }

    private void SwitchRepository(string repositoryId, bool openManager)
    {
        if (!_repositories.Any(repository => string.Equals(repository.Id, repositoryId, StringComparison.Ordinal)))
        {
            return;
        }

        SaveSelectedRepositoryFromInputs();
        _selectedRepositoryId = repositoryId;
        ResetOnlineRepositoryContext();
        ApplySelectedRepositoryToInputs();
        if (openManager)
        {
            _currentPrimarySection = PrimarySection.Repository;
        }

        ApplyShellState(refreshRepository: openManager);
    }

    private void ResetOnlineRepositoryContext()
    {
        _activeBrowseSessionKey = null;
        _pendingBrowsePosition = null;
        ++_browseRestoreRevision;
        CancelOnlineModListRequest();
        InvalidateOnlineDetailRequest();
        _onlineDownloadEnrichmentVersion++;
        _onlineCurrentPage = 1;
        _onlineTotalCount = 0;
        _onlineTotalPages = 1;
        _onlineCharacterFilter = string.Empty;
        _onlineKnownCharacters.Clear();
        _onlineAuthorId = 0;
        _onlineAuthorName = string.Empty;
        _lastLoadedOnlineConfigKey = null;
        _onlineMods.Clear();
        _activeOnlineDetailMod = null;
        ResetOnlineDetailPaneToPlaceholder();
    }

    private async void OnRepositoryPresetClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not Button { Tag: string presetId })
            {
                return;
            }

            RepositoryPresetInfo? preset = BuiltInRepositoryPresets.FirstOrDefault(item =>
                string.Equals(item.Id, presetId, StringComparison.OrdinalIgnoreCase));
            if (preset is null)
            {
                return;
            }

            WorkspaceRepository? existing = FindRepositoryForPreset(preset);
            if (existing is not null)
            {
                SwitchRepository(existing.Id, openManager: false);
                ShowAppNotification(
                    $"已切换到现有仓库：{existing.Name}",
                    $"Switched to the existing repository: {existing.Name}");
                return;
            }

            SaveSelectedRepositoryFromInputs();
            WorkspaceRepository draftRepository = new()
            {
                Name = _currentLanguage == AppLanguage.ZhCn ? preset.DisplayNameZh : preset.DisplayNameEn,
                OnlineSourceSite = DefaultOnlineSourceSite,
                OnlineGameName = preset.DisplayNameEn,
                OnlineCategoryId = preset.CategoryId,
                WikiUrl = preset.WikiUrl,
                Notes = L("内置游戏仓库预设，可继续填写本地路径和启动器。", "Built-in game repository preset. Configure local paths and a launcher as needed.")
            };

            RepositoryEditorResult? result = await PromptForRepositoryAsync(
                L("预设已经填写在线分类和 Wiki。请补充本地 Mod 存储、目标文件夹及启动器路径。", "The online category and Wiki are prefilled. Add the local mod storage, target folder, and launcher paths."),
                L($"创建{preset.DisplayNameZh}仓库", $"Create {preset.DisplayNameEn} Repository"),
                draftRepository);
            if (result is null || string.IsNullOrWhiteSpace(result.Name))
            {
                return;
            }

            WorkspaceRepository repository = CreateRepositoryFromEditorResult(result);
            _repositories.Add(repository);
            _selectedRepositoryId = repository.Id;
            ResetOnlineRepositoryContext();
            ApplySelectedRepositoryToInputs();
            SaveConfig();
            SaveShellConfig();
            RefreshDashboard();
            RefreshRepositoryActionButtons();
            ShowAppNotification(
                $"已创建仓库：{repository.Name}",
                $"Created repository: {repository.Name}");
        }
        catch (Exception ex)
        {
            ReportUiActionFailure("OnRepositoryPresetClicked", ex);
        }
    }

    private static WorkspaceRepository CreateRepositoryFromEditorResult(RepositoryEditorResult result)
    {
        return new WorkspaceRepository
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = result.Name.Trim(),
            SourcePath = result.SourcePath.Trim(),
            TargetPath = result.TargetPath.Trim(),
            UseDirectoryLinks = result.UseDirectoryLinks,
            LauncherPath = result.LauncherPath.Trim(),
            OnlineSourceSite = result.OnlineSourceSite.Trim(),
            OnlineGameName = result.OnlineGameName.Trim(),
            OnlineCategoryId = result.OnlineCategoryId.Trim(),
            WikiUrl = result.WikiUrl.Trim(),
            Notes = result.Notes.Trim()
        };
    }

    private void OpenRepositoryWiki(WorkspaceRepository repository)
    {
        if (!Uri.TryCreate(repository.WikiUrl, UriKind.Absolute, out Uri? wikiUri))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(wikiUri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowAppNotification(
                $"打开 Wiki 失败：{ex.Message}",
                $"Failed to open the Wiki: {ex.Message}",
                InfoBarSeverity.Error);
        }
    }

    private async void OnAddRepositoryClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            string? repositoryName = await PromptForTextAsync(
                L("输入新仓库名称，后面可以继续补充路径和用途。", "Enter a repository name. You can expand its settings later."),
                L("新建仓库", "Create Repository"),
                L("例如：原神正式 / ZZZ Test", "For example: GI Main / ZZZ Test"));

            if (string.IsNullOrWhiteSpace(repositoryName))
            {
                return;
            }

            SaveSelectedRepositoryFromInputs();

            var repository = new WorkspaceRepository
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = repositoryName.Trim(),
                OnlineSourceSite = DefaultOnlineSourceSite,
                OnlineGameName = DefaultOnlineGameName,
                OnlineCategoryId = DefaultOnlineCategoryId
            };

            _repositories.Add(repository);
            _selectedRepositoryId = repository.Id;
            _onlineCurrentPage = 1;
            _onlineTotalCount = 0;
            _onlineTotalPages = 1;
            _onlineCharacterFilter = string.Empty;
            _onlineKnownCharacters.Clear();
            _lastLoadedOnlineConfigKey = null;
            _onlineMods.Clear();
            _currentPrimarySection = PrimarySection.Repository;
            ApplySelectedRepositoryToInputs();
            SaveConfig();
            ApplyShellState(refreshRepository: false);
            RefreshDashboard();
            StatusTextBlock.Text = L($"已创建仓库：{repository.Name}", $"Created repository: {repository.Name}");
        }
        catch (Exception ex)
        {
            ReportUiActionFailure("OnAddRepositoryClicked", ex);
        }
    }

    private async void OnRenameRepositoryClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            WorkspaceRepository? repository = GetSelectedRepository();
            if (repository is null || _selectedRepositoryId is null)
            {
                return;
            }

            string? repositoryName = await PromptForTextAsync(
                L("输入新的仓库名称。", "Enter a new repository name."),
                L("重命名仓库", "Rename Repository"),
                repository.Name);

            if (string.IsNullOrWhiteSpace(repositoryName))
            {
                return;
            }

            repository.Name = repositoryName.Trim();
            SaveShellConfig();
            RefreshSecondaryNavigation();
            RefreshDashboard();
            RefreshRepositoryActionButtons();
            StatusTextBlock.Text = L($"已重命名仓库：{repository.Name}", $"Renamed repository: {repository.Name}");
        }
        catch (Exception ex)
        {
            ReportUiActionFailure("OnRenameRepositoryClicked", ex);
        }
    }

    private async void OnAddRepositoryDetailsClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveSelectedRepositoryFromInputs();

            WorkspaceRepository draftRepository = new()
            {
                Name = string.Empty,
                SourcePath = SourceTextBox.Text ?? string.Empty,
                TargetPath = TargetTextBox.Text ?? string.Empty,
                LauncherPath = LauncherTextBox.Text ?? string.Empty
            };

            RepositoryEditorResult? result = await PromptForRepositoryAsync(
                L("创建一个新的自定义仓库，并一次性补充它的主要路径信息。", "Create a new custom repository and fill in its primary paths in one step."),
                L("新建仓库", "Create Repository"),
                draftRepository);

            if (result is null || string.IsNullOrWhiteSpace(result.Name))
            {
                return;
            }

            WorkspaceRepository repository = CreateRepositoryFromEditorResult(result);

            _repositories.Add(repository);
            _selectedRepositoryId = repository.Id;
            _currentPrimarySection = PrimarySection.Repository;
            _onlineCurrentPage = 1;
            _onlineTotalCount = 0;
            _onlineTotalPages = 1;
            _onlineCharacterFilter = string.Empty;
            _onlineKnownCharacters.Clear();
            _lastLoadedOnlineConfigKey = null;
            _onlineMods.Clear();
            ApplySelectedRepositoryToInputs();
            SaveConfig();
            ApplyShellState(refreshRepository: false);
            RefreshDashboard();
            StatusTextBlock.Text = L($"已创建仓库：{repository.Name}", $"Created repository: {repository.Name}");
        }
        catch (Exception ex)
        {
            ReportUiActionFailure("OnAddRepositoryDetailsClicked", ex);
        }
    }

    private async void OnEditRepositoryDetailsClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            WorkspaceRepository? repository = GetSelectedRepository();
            if (repository is null || _selectedRepositoryId is null)
            {
                return;
            }

            SaveSelectedRepositoryFromInputs();

            RepositoryEditorResult? result = await PromptForRepositoryAsync(
                L("编辑当前仓库的名称、路径和预留的在线来源信息。", "Edit the current repository name, paths, and reserved online-source fields."),
                L("编辑仓库", "Edit Repository"),
                repository);

            if (result is null || string.IsNullOrWhiteSpace(result.Name))
            {
                return;
            }

            repository.Name = result.Name.Trim();
            repository.SourcePath = result.SourcePath.Trim();
            repository.TargetPath = result.TargetPath.Trim();
            repository.UseDirectoryLinks = result.UseDirectoryLinks;
            repository.LauncherPath = result.LauncherPath.Trim();
            repository.OnlineSourceSite = result.OnlineSourceSite.Trim();
            repository.OnlineGameName = result.OnlineGameName.Trim();
            repository.OnlineCategoryId = result.OnlineCategoryId.Trim();
            repository.WikiUrl = result.WikiUrl.Trim();
            repository.Notes = result.Notes.Trim();
            ResetOnlineRepositoryContext();

            ApplySelectedRepositoryToInputs();
            SaveShellConfig();
            RefreshSecondaryNavigation();
            RefreshDashboard();
            RefreshRepositoryActionButtons();
            StatusTextBlock.Text = L($"已更新仓库：{repository.Name}", $"Updated repository: {repository.Name}");
        }
        catch (Exception ex)
        {
            ReportUiActionFailure("OnEditRepositoryDetailsClicked", ex);
        }
    }

    private async void OnEditOnlineRepositoryClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            WorkspaceRepository? repository = GetSelectedRepository();
            if (repository is null || _selectedRepositoryId is null)
            {
                await ShowMessageAsync(
                    L("请先在左侧选择一个仓库，再编辑它的在线分类配置。", "Select a repository on the left before editing its online category settings."),
                    L("未选择仓库", "No repository selected"));
                return;
            }

            OnlineRepositoryConfigResult? result = await PromptForOnlineRepositoryConfigAsync(repository);
            if (result is null)
            {
                return;
            }

            repository.OnlineSourceSite = result.OnlineSourceSite.Trim();
            repository.OnlineGameName = result.OnlineGameName.Trim();
            repository.OnlineCategoryId = result.OnlineCategoryId.Trim();
            repository.WikiUrl = result.WikiUrl.Trim();
            repository.Notes = result.Notes.Trim();
            ResetOnlineRepositoryContext();

            SaveShellConfig();
            RefreshSettingsPane();
            RefreshOnlinePaneV2();
            ResetOnlineDetailPaneToPlaceholder();
            StatusTextBlock.Text = L($"已更新在线分类配置：{repository.Name}", $"Updated online category settings: {repository.Name}");
        }
        catch (Exception ex)
        {
            ReportUiActionFailure("OnEditOnlineRepositoryClicked", ex);
        }
    }

    private async void OnEditCharacterMappingsClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            WorkspaceRepository? repository = GetSelectedRepository();
            if (repository is null)
            {
                await ShowMessageAsync(
                    L("请先选择一个仓库。", "Select a repository first."),
                    L("未选择仓库", "No Repository Selected"));
                return;
            }

            EnsureOnlineCharacterOptionsForRepository(repository);
            if (!_onlineCharacterOptions.Any(character => character.IsWikiCharacter))
            {
                _ = EnsureOnlineCharacterCatalogAsync(repository, forceReload: false);
                for (int attempt = 0; attempt < 12 && !_onlineCharacterOptions.Any(character => character.IsWikiCharacter); attempt++)
                {
                    await Task.Delay(250);
                }
            }

            List<OnlineCharacterOption> wikiCharacters = _onlineCharacterOptions
                .Where(character => character.IsWikiCharacter)
                .OrderBy(character => character.WikiOrder)
                .ToList();
            if (wikiCharacters.Count == 0)
            {
                await ShowMessageAsync(
                    L("暂时没有读取到 Wiki 角色目录。请检查当前仓库的 Wiki 地址和网络连接，然后刷新在线列表。", "The Wiki character catalog is not available yet. Check this repository's Wiki URL and network connection, then refresh the online list."),
                    L("角色目录不可用", "Character Catalog Unavailable"));
                return;
            }

            List<OnlineCharacterOption> categoryCandidates = _onlineCharacterOptions
                .Where(character => !string.IsNullOrWhiteSpace(character.CategoryId))
                .GroupBy(character => character.CategoryId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(character => character.DisplayNameEn, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            ComboBox wikiCharacterBox = new()
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                PlaceholderText = L("选择 Wiki 角色", "Select a Wiki character")
            };
            foreach (OnlineCharacterOption character in wikiCharacters)
            {
                wikiCharacterBox.Items.Add(new ComboBoxItem
                {
                    Content = $"{character.DisplayNameZh} / {character.DisplayNameEn}",
                    Tag = character
                });
            }

            ComboBox gameBananaCategoryBox = new()
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                PlaceholderText = categoryCandidates.Count == 0
                    ? L("尚未发现分类，可直接填写 ID", "No categories discovered; enter an ID below")
                    : L("选择已发现的 GameBanana 分类", "Select a discovered GameBanana category")
            };
            foreach (OnlineCharacterOption candidate in categoryCandidates)
            {
                gameBananaCategoryBox.Items.Add(new ComboBoxItem
                {
                    Content = $"{candidate.DisplayNameEn} · {candidate.CategoryId}",
                    Tag = candidate
                });
            }

            TextBox categoryIdBox = new()
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                PlaceholderText = L("GameBanana 角色分类 ID", "GameBanana character category ID")
            };
            TextBlock mappingStatus = new()
            {
                Style = (Style)Application.Current.Resources["CaptionTextStyle"],
                TextWrapping = TextWrapping.Wrap
            };

            void SyncSelectedWikiCharacter()
            {
                if (wikiCharacterBox.SelectedItem is not ComboBoxItem { Tag: OnlineCharacterOption character })
                {
                    categoryIdBox.Text = string.Empty;
                    mappingStatus.Text = L("先选择需要修正的 Wiki 角色。", "Select the Wiki character to correct first.");
                    return;
                }

                string mappingKey = GetCharacterMappingKey(character);
                bool hasManualMapping = repository.CharacterCategoryMappings.TryGetValue(mappingKey, out string? mappedCategoryId)
                    && !string.IsNullOrWhiteSpace(mappedCategoryId);
                categoryIdBox.Text = hasManualMapping ? mappedCategoryId! : character.CategoryId;
                gameBananaCategoryBox.SelectedItem = gameBananaCategoryBox.Items
                    .OfType<ComboBoxItem>()
                    .FirstOrDefault(item => item.Tag is OnlineCharacterOption candidate
                        && string.Equals(candidate.CategoryId, categoryIdBox.Text, StringComparison.OrdinalIgnoreCase));
                mappingStatus.Text = hasManualMapping
                    ? L("当前使用手动映射，保存后始终优先采用。", "A manual mapping is active and always takes priority.")
                    : string.IsNullOrWhiteSpace(character.CategoryId)
                        ? L("当前尚未自动匹配到分类。", "No category has been matched automatically.")
                        : L("当前显示自动匹配结果；保存后会固定为手动映射。", "This is an automatic match. Saving will pin it as a manual mapping.");
            }

            wikiCharacterBox.SelectionChanged += (_, _) => SyncSelectedWikiCharacter();
            gameBananaCategoryBox.SelectionChanged += (_, _) =>
            {
                if (gameBananaCategoryBox.SelectedItem is ComboBoxItem { Tag: OnlineCharacterOption candidate })
                {
                    categoryIdBox.Text = candidate.CategoryId;
                }
            };
            wikiCharacterBox.SelectedIndex = 0;

            StackPanel panel = new() { Spacing = 10, MinWidth = 520, MaxWidth = 620 };
            panel.Children.Add(new TextBlock
            {
                Text = L(
                    "当 Wiki 名称与 GameBanana 分类名称不一致时，可在这里固定绑定。手动映射仅作用于当前仓库。",
                    "When Wiki and GameBanana names differ, pin the correct category here. Manual mappings only affect the current repository."),
                TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(CreateLabeledEditor(L("Wiki 角色", "Wiki character"), wikiCharacterBox));
            panel.Children.Add(CreateLabeledEditor(L("已发现分类", "Discovered category"), gameBananaCategoryBox));
            panel.Children.Add(CreateLabeledEditor(L("分类 ID", "Category ID"), categoryIdBox));
            panel.Children.Add(mappingStatus);

            ContentDialog dialog = new()
            {
                Title = L("角色分类映射", "Character Category Mapping"),
                Content = panel,
                PrimaryButtonText = L("保存映射", "Save Mapping"),
                SecondaryButtonText = L("清除映射", "Clear Mapping"),
                CloseButtonText = L("取消", "Cancel"),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = RootGrid.XamlRoot
            };

            ContentDialogResult result = await dialog.ShowAsync();
            if (wikiCharacterBox.SelectedItem is not ComboBoxItem { Tag: OnlineCharacterOption selectedCharacter })
            {
                return;
            }

            string selectedMappingKey = GetCharacterMappingKey(selectedCharacter);
            if (result == ContentDialogResult.Primary)
            {
                string categoryId = categoryIdBox.Text.Trim();
                if (!int.TryParse(categoryId, NumberStyles.None, CultureInfo.InvariantCulture, out int parsedCategoryId)
                    || parsedCategoryId <= 0)
                {
                    await ShowMessageAsync(
                        L("请输入有效的数字分类 ID。", "Enter a valid numeric category ID."),
                        L("分类 ID 无效", "Invalid Category ID"));
                    return;
                }

                repository.CharacterCategoryMappings[selectedMappingKey] = categoryId;
            }
            else if (result == ContentDialogResult.Secondary)
            {
                repository.CharacterCategoryMappings.Remove(selectedMappingKey);
            }
            else
            {
                return;
            }

            SaveShellConfig();
            ResetOnlineRepositoryContext();
            EnsureOnlineCharacterOptionsForRepository(repository);
            _ = EnsureOnlineCharacterCatalogAsync(repository, forceReload: false);
            RefreshSettingsPane();
            RefreshOnlinePaneV2();
            ShowAppNotification("角色分类映射已更新。", "Character category mapping updated.");
        }
        catch (Exception ex)
        {
            ReportUiActionFailure("OnEditCharacterMappingsClicked", ex);
        }
    }

    private async void OnDeleteRepositoryClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            WorkspaceRepository? repository = GetSelectedRepository();
            if (repository is null || _selectedRepositoryId is null)
            {
                return;
            }

            if (_repositories.Count <= 1)
            {
                await ShowMessageAsync(
                    L("至少要保留一个仓库。", "At least one repository must remain."),
                    L("无法删除", "Cannot delete"));
                return;
            }

            bool confirmed = await ShowConfirmAsync(
                L($"确定要删除这个仓库吗？\n\n{repository.Name}\n\n仓库配置会被移除，但不会删除磁盘上的 Mod 文件夹。",
                  $"Delete this repository?\n\n{repository.Name}\n\nThe repository configuration will be removed, but no files on disk will be deleted."),
                L("首次确认", "First confirmation"));
            if (!confirmed)
            {
                return;
            }

            bool confirmedAgain = await ShowConfirmAsync(
                L($"请再次确认删除仓库：{repository.Name}\n\n此操作无法撤销。",
                  $"Please confirm again to delete repository: {repository.Name}\n\nThis action cannot be undone."),
                L("第二次确认", "Second confirmation"));
            if (!confirmedAgain)
            {
                return;
            }

            int currentIndex = _repositories.FindIndex(item => item.Id == repository.Id);
            _repositories.RemoveAll(item => item.Id == repository.Id);

            if (_repositories.Count > 0)
            {
                int nextIndex = Math.Clamp(currentIndex, 0, _repositories.Count - 1);
                _selectedRepositoryId = _repositories[nextIndex].Id;
            }
            else
            {
                _selectedRepositoryId = null;
            }

            if (_currentPrimarySection == PrimarySection.Dashboard)
            {
                _selectedRepositoryId = null;
            }
            else
            {
                ApplySelectedRepositoryToInputs();
            }

            SaveShellConfig();
            ApplyShellState(refreshRepository: _currentPrimarySection == PrimarySection.Repository);
            StatusTextBlock.Text = L($"已删除仓库：{repository.Name}", $"Deleted repository: {repository.Name}");
        }
        catch (Exception ex)
        {
            ReportUiActionFailure("OnDeleteRepositoryClicked", ex);
        }
    }
}
