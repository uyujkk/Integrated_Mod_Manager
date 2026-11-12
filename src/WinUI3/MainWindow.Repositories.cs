using System.Diagnostics;
using System.Globalization;
using IntegratedModManager.Core.Online;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private WorkspaceRepository? GetSelectedRepository()
    {
        return _services.Repositories.Selected;
    }

    private void SaveSelectedRepositoryFromInputs()
    {
        _onlineView.CaptureOnlineBrowseSession();
        WorkspaceRepository? repository = GetSelectedRepository();
        if (repository is null)
        {
            return;
        }

        repository.SourcePath = Paths.Source;
        repository.TargetPath = Paths.Target;
        repository.LauncherPath = Paths.Launcher;
    }

    private void ApplySelectedRepositoryToInputs()
    {
        WorkspaceRepository? repository = GetSelectedRepository();
        if (repository is null)
        {
            _isApplyingRepositoryDeploymentMode = true;
            Paths.UseDirectoryLinks = false;
            _isApplyingRepositoryDeploymentMode = false;
            RefreshPersistentSlotControls();
            return;
        }

        Paths.Source = repository.SourcePath;
        Paths.Target = repository.TargetPath;
        Paths.Launcher = repository.LauncherPath;
        RefreshEndfieldBridgeControls();
        RefreshPersistentSlotControls();
        _isApplyingRepositoryDeploymentMode = true;
        try
        {
            Paths.UseDirectoryLinks = repository.UseDirectoryLinks;
        }
        finally
        {
            _isApplyingRepositoryDeploymentMode = false;
        }
    }

    private void SwitchRepository(string repositoryId, bool openManager)
    {
        if (!_services.Repositories.Items.Any(repository => string.Equals(repository.Id, repositoryId, StringComparison.Ordinal)))
        {
            return;
        }

        SaveSelectedRepositoryFromInputs();
        _services.Repositories.SelectedId = repositoryId;
        ResetOnlineRepositoryContext();
        ApplySelectedRepositoryToInputs();
        RefreshTrayStatus();
        if (openManager)
        {
            _currentPrimarySection = PrimarySection.Repository;
        }

        ApplyShellState(refreshRepository: openManager);
    }

    private void ResetOnlineRepositoryContext()
    {
        _onlineView._activeBrowseSessionKey = null;
        _onlineView._pendingBrowsePosition = null;
        ++_onlineView._browseRestoreRevision;
        _onlineView.CancelOnlineModListRequest();
        _onlineView.InvalidateOnlineDetailRequest();
        _onlineView._onlineDownloadEnrichmentVersion++;
        _services.Online.Page.ResetPaging();
        _services.Online.Page.CharacterFilter = string.Empty;
        _services.Online.Page.KnownCharacters.Clear();
        _services.Online.Page.AuthorId = 0;
        _services.Online.Page.AuthorName = string.Empty;
        _onlineView._lastLoadedOnlineConfigKey = null;
        _services.Online.Page.Mods.Clear();
        _onlineView._activeOnlineDetailMod = null;
        _onlineView.ResetOnlineDetailPaneToPlaceholder();
    }

    // A game preset from the dashboard's "New repository" menu.
    private async void CreateRepositoryFromPreset(string presetId)
    {
        try
        {
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
                ShowAppNotification(T("Repositories.SwitchedToExisting", existing.Name));
                return;
            }

            SaveSelectedRepositoryFromInputs();
            WorkspaceRepository draftRepository = new()
            {
                Name = IsChineseUi ? preset.DisplayNameZh : preset.DisplayNameEn,
                OnlineSourceSite = DefaultOnlineSourceSite,
                OnlineGameName = preset.DisplayNameEn,
                OnlineCategoryId = preset.CategoryId,
                WikiUrl = preset.WikiUrl,
                Notes = T("Repositories.BuiltInGameRepositoryPresetConfigure")
            };

            RepositoryEditorResult? result = await RepositoryEditorDialog.ShowAsync(_services, 
                T("Repositories.TheOnlineCategoryAndWikiAre"),
                T("Repositories.CreateRepository2", preset.DisplayNameZh, preset.DisplayNameEn),
                draftRepository);
            if (result is null || string.IsNullOrWhiteSpace(result.Name))
            {
                return;
            }

            WorkspaceRepository repository = CreateRepositoryFromEditorResult(result);
            _services.Repositories.Items.Add(repository);
            _services.Repositories.SelectedId = repository.Id;
            ResetOnlineRepositoryContext();
            ApplySelectedRepositoryToInputs();
            SaveConfig();
            SaveShellConfig();
            RefreshDashboard();
            RefreshRepositoryActionButtons();
            ShowAppNotification(T("Repositories.Created", repository.Name));
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
            AllowMultipleModsPerCharacter = result.AllowMultipleModsPerCharacter,
            LauncherPath = result.LauncherPath.Trim(),
            XxmiImporter = result.XxmiImporter,
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
            ShowAppNotification(T("Repositories.OpenWikiFailed", ex.Message), InfoBarSeverity.Error);
        }
    }

    private async void OnAddRepositoryClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            string? repositoryName = await PromptForTextAsync(
                T("Repositories.EnterARepositoryNameYouCan"),
                T("Repositories.CreateRepository"),
                T("Repositories.ForExampleGIMainZZZTest"), T("Dialogs.Create"));

            if (string.IsNullOrWhiteSpace(repositoryName))
            {
                return;
            }

            SaveSelectedRepositoryFromInputs();

            var repository = new WorkspaceRepository
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = repositoryName.Trim(),
                OnlineSourceSite = DefaultOnlineSourceSite
            };

            _services.Repositories.Items.Add(repository);
            _services.Repositories.SelectedId = repository.Id;
            _services.Online.Page.ResetPaging();
            _services.Online.Page.CharacterFilter = string.Empty;
            _services.Online.Page.KnownCharacters.Clear();
            _onlineView._lastLoadedOnlineConfigKey = null;
            _services.Online.Page.Mods.Clear();
            _currentPrimarySection = PrimarySection.Repository;
            ApplySelectedRepositoryToInputs();
            SaveConfig();
            ApplyShellState(refreshRepository: false);
            RefreshDashboard();
            _workspaceView.StatusTextBlock.Text = T("Repositories.CreatedRepository", repository.Name);
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
            if (repository is null || _services.Repositories.SelectedId is null)
            {
                return;
            }

            string? repositoryName = await PromptForTextAsync(
                T("Repositories.EnterANewRepositoryName"),
                T("Repositories.RenameRepository"),
                repository.Name, T("Dialogs.Rename"));

            if (string.IsNullOrWhiteSpace(repositoryName))
            {
                return;
            }

            repository.Name = repositoryName.Trim();
            SaveShellConfig();
            RefreshDashboard();
            RefreshRepositoryActionButtons();
            _workspaceView.StatusTextBlock.Text = T("Repositories.RenamedRepository", repository.Name);
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
                SourcePath = Paths.Source,
                TargetPath = Paths.Target,
                LauncherPath = Paths.Launcher
            };

            RepositoryEditorResult? result = await RepositoryEditorDialog.ShowAsync(_services, 
                T("Repositories.CreateANewCustomRepositoryAnd"),
                T("Repositories.CreateRepository"),
                draftRepository);

            if (result is null || string.IsNullOrWhiteSpace(result.Name))
            {
                return;
            }

            WorkspaceRepository repository = CreateRepositoryFromEditorResult(result);

            _services.Repositories.Items.Add(repository);
            _services.Repositories.SelectedId = repository.Id;
            _currentPrimarySection = PrimarySection.Repository;
            _services.Online.Page.ResetPaging();
            _services.Online.Page.CharacterFilter = string.Empty;
            _services.Online.Page.KnownCharacters.Clear();
            _onlineView._lastLoadedOnlineConfigKey = null;
            _services.Online.Page.Mods.Clear();
            ApplySelectedRepositoryToInputs();
            SaveConfig();
            ApplyShellState(refreshRepository: false);
            RefreshDashboard();
            _workspaceView.StatusTextBlock.Text = T("Repositories.CreatedRepository", repository.Name);
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
            await EditSelectedRepositoryAsync();
        }
        catch (Exception ex)
        {
            ReportUiActionFailure("OnEditRepositoryDetailsClicked", ex);
        }
    }

    private async Task EditSelectedRepositoryAsync()
    {
        WorkspaceRepository? repository = GetSelectedRepository();
        if (repository is null || _services.Repositories.SelectedId is null)
        {
            return;
        }

        SaveSelectedRepositoryFromInputs();

        RepositoryEditorResult? result = await RepositoryEditorDialog.ShowAsync(_services, 
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
        repository.AllowMultipleModsPerCharacter = result.AllowMultipleModsPerCharacter;
        repository.LauncherPath = result.LauncherPath.Trim();
        repository.XxmiImporter = result.XxmiImporter;
        repository.OnlineSourceSite = result.OnlineSourceSite.Trim();
        repository.OnlineGameName = result.OnlineGameName.Trim();
        repository.OnlineCategoryId = result.OnlineCategoryId.Trim();
        repository.WikiUrl = result.WikiUrl.Trim();
        repository.Notes = result.Notes.Trim();
        ResetOnlineRepositoryContext();

        ApplySelectedRepositoryToInputs();
        SaveShellConfig();
        RefreshDashboard();
        RefreshRepositoryActionButtons();
        _workspaceView.StatusTextBlock.Text = T("Repositories.UpdatedRepository", repository.Name);
    }

    private async void OnEditOnlineRepositoryClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            WorkspaceRepository? repository = GetSelectedRepository();
            if (repository is null || _services.Repositories.SelectedId is null)
            {
                await ShowMessageAsync(
                    T("Repositories.SelectARepositoryOnTheLeft"),
                    T("Common.NoRepositorySelected"));
                return;
            }

            OnlineRepositoryConfigResult? result = await OnlineRepositoryConfigDialog.ShowAsync(_services, repository);
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
            _onlineView.RefreshOnlinePaneV2();
            _onlineView.ResetOnlineDetailPaneToPlaceholder();
            _workspaceView.StatusTextBlock.Text = T("Repositories.UpdatedOnlineCategorySettings", repository.Name);
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

            _onlineView.EnsureOnlineCharacterOptionsForRepository(repository);
            if (!_services.Online.Page.CharacterOptions.Any(character => character.IsWikiCharacter))
            {
                _ = _services.CharacterCatalog.EnsureAsync(repository, forceReload: false);
                for (int attempt = 0; attempt < 12 && !_services.Online.Page.CharacterOptions.Any(character => character.IsWikiCharacter); attempt++)
                {
                    await Task.Delay(250);
                }
            }

            List<OnlineCharacterOption> wikiCharacters = _services.Online.Page.CharacterOptions
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

            List<OnlineCharacterOption> categoryCandidates = _services.Online.Page.CharacterOptions
                .Where(character => !string.IsNullOrWhiteSpace(character.CategoryId))
                .GroupBy(character => character.CategoryId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(character => character.DisplayNameEn, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            (ContentDialogResult result, OnlineCharacterOption? selectedCharacter, string chosenCategoryId) =
                await CharacterCategoryMappingDialog.ShowAsync(_services, repository.CharacterCategoryMappings, wikiCharacters, categoryCandidates);
            if (selectedCharacter is null)
            {
                return;
            }

            string selectedMappingKey = OnlineCharacterCatalogMatcher.MappingKey(selectedCharacter);
            if (result == ContentDialogResult.Primary)
            {
                string categoryId = chosenCategoryId.Trim();
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
            _onlineView.EnsureOnlineCharacterOptionsForRepository(repository);
            _ = _services.CharacterCatalog.EnsureAsync(repository, forceReload: false);
            RefreshSettingsPane();
            _onlineView.RefreshOnlinePaneV2();
            ShowAppNotification(T("Repositories.CategoryMappingUpdated"));
        }
        catch (Exception ex)
        {
            ReportUiActionFailure("OnEditCharacterMappingsClicked", ex);
        }
    }

    // Deleting asks twice; the last repository cannot be deleted.
    private async Task DeleteRepositoryAsync(WorkspaceRepository repository)
    {
        try
        {
            if (_services.Repositories.Items.Count <= 1)
            {
                await ShowMessageAsync(
                    T("Repositories.AtLeastOneRepositoryMustRemain"),
                    T("Repositories.CannotDelete"));
                return;
            }

            bool confirmed = await ShowDangerConfirmAsync(
                T("Repositories.DeleteThisRepositoryTheRepositoryConfiguration", repository.Name),
                T("Repositories.FirstConfirmation"), T("Dialogs.Delete"));
            if (!confirmed)
            {
                return;
            }

            bool confirmedAgain = await ShowDangerConfirmAsync(
                T("Repositories.PleaseConfirmAgainToDeleteRepository", repository.Name),
                T("Repositories.SecondConfirmation"), T("Dialogs.Delete"));
            if (!confirmedAgain)
            {
                return;
            }

            bool wasSelected = repository.Id == _services.Repositories.SelectedId;
            int currentIndex = _services.Repositories.Items.FindIndex(item => item.Id == repository.Id);
            _services.Repositories.Items.RemoveAll(item => item.Id == repository.Id);

            // Deleting another repository from its card keeps the current one selected.
            if (wasSelected)
            {
                _services.Repositories.SelectedId = _services.Repositories.Items.Count > 0
                    ? _services.Repositories.Items[Math.Clamp(currentIndex, 0, _services.Repositories.Items.Count - 1)].Id
                    : null;
                ApplySelectedRepositoryToInputs();
            }

            SaveShellConfig();
            ApplyShellState(refreshRepository: _currentPrimarySection == PrimarySection.Repository);
            _workspaceView.StatusTextBlock.Text = T("Repositories.DeletedRepository", repository.Name);
        }
        catch (Exception ex)
        {
            ReportUiActionFailure("DeleteRepository", ex);
        }
    }
}
