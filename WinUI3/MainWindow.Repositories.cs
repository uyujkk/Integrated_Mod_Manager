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
                Notes = T("Repositories.BuiltInGameRepositoryPresetConfigure")
            };

            RepositoryEditorResult? result = await PromptForRepositoryAsync(
                T("Repositories.TheOnlineCategoryAndWikiAre"),
                T("Repositories.CreateRepository2", preset.DisplayNameZh, preset.DisplayNameEn),
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
                T("Repositories.EnterARepositoryNameYouCan"),
                T("Repositories.CreateRepository"),
                T("Repositories.ForExampleGIMainZZZTest"));

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
            StatusTextBlock.Text = T("Repositories.CreatedRepository", repository.Name);
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
                T("Repositories.EnterANewRepositoryName"),
                T("Repositories.RenameRepository"),
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
            StatusTextBlock.Text = T("Repositories.RenamedRepository", repository.Name);
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
                T("Repositories.CreateANewCustomRepositoryAnd"),
                T("Repositories.CreateRepository"),
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
            StatusTextBlock.Text = T("Repositories.CreatedRepository", repository.Name);
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
                T("Repositories.EditTheCurrentRepositoryNamePaths"),
                T("Repositories.EditRepository"),
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
            StatusTextBlock.Text = T("Repositories.UpdatedRepository", repository.Name);
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
                    T("Repositories.SelectARepositoryOnTheLeft"),
                    T("Common.NoRepositorySelected"));
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
            StatusTextBlock.Text = T("Repositories.UpdatedOnlineCategorySettings", repository.Name);
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
                    T("Repositories.SelectARepositoryFirst"),
                    T("Repositories.NoRepositorySelected"));
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
                    T("Repositories.TheWikiCharacterCatalogIsNot"),
                    T("Repositories.CharacterCatalogUnavailable"));
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
                PlaceholderText = T("Repositories.SelectAWikiCharacter")
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
                    ? T("Repositories.NoCategoriesDiscoveredEnterAnID")
                    : T("Repositories.SelectADiscoveredGameBananaCategory")
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
                PlaceholderText = T("Repositories.GameBananaCharacterCategoryID")
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
                    mappingStatus.Text = T("Repositories.SelectTheWikiCharacterToCorrect");
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
                    ? T("Repositories.AManualMappingIsActiveAnd")
                    : string.IsNullOrWhiteSpace(character.CategoryId)
                        ? T("Repositories.NoCategoryHasBeenMatchedAutomatically")
                        : T("Repositories.ThisIsAnAutomaticMatchSaving");
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
                Text = T("Repositories.WhenWikiAndGameBananaNamesDiffer"),
                TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(CreateLabeledEditor(T("Repositories.WikiCharacter"), wikiCharacterBox));
            panel.Children.Add(CreateLabeledEditor(T("Repositories.DiscoveredCategory"), gameBananaCategoryBox));
            panel.Children.Add(CreateLabeledEditor(T("Common.CategoryID"), categoryIdBox));
            panel.Children.Add(mappingStatus);

            ContentDialog dialog = new()
            {
                Title = T("Common.CharacterCategoryMapping"),
                Content = panel,
                PrimaryButtonText = T("Repositories.SaveMapping"),
                SecondaryButtonText = T("Repositories.ClearMapping"),
                CloseButtonText = T("Common.Cancel"),
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
                        T("Repositories.EnterAValidNumericCategoryID"),
                        T("Repositories.InvalidCategoryID"));
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
                    T("Repositories.AtLeastOneRepositoryMustRemain"),
                    T("Repositories.CannotDelete"));
                return;
            }

            bool confirmed = await ShowConfirmAsync(
                T("Repositories.DeleteThisRepositoryTheRepositoryConfiguration", repository.Name),
                T("Repositories.FirstConfirmation"));
            if (!confirmed)
            {
                return;
            }

            bool confirmedAgain = await ShowConfirmAsync(
                T("Repositories.PleaseConfirmAgainToDeleteRepository", repository.Name),
                T("Repositories.SecondConfirmation"));
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
            StatusTextBlock.Text = T("Repositories.DeletedRepository", repository.Name);
        }
        catch (Exception ex)
        {
            ReportUiActionFailure("OnDeleteRepositoryClicked", ex);
        }
    }
}
