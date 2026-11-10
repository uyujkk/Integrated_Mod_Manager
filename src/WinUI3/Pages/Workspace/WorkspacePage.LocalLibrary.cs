using IntegratedModManager.Core.Deployment;
using IntegratedModManager.Core.Repository;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace ModFolderCopier.WinUI;

public sealed partial class WorkspacePage
{
    internal void AnimateImageReveal(UIElement image)
    {
        if (_host.ShouldReduceAnimations)
        {
            ElementCompositionPreview.GetElementVisual(image).Opacity = 1f;
            return;
        }

        var visual = ElementCompositionPreview.GetElementVisual(image);
        var compositor = visual.Compositor;
        var animation = compositor.CreateScalarKeyFrameAnimation();
        animation.Duration = TimeSpan.FromMilliseconds(220);
        animation.InsertKeyFrame(0f, 0f);
        animation.InsertKeyFrame(1f, 1f);
        visual.StartAnimation("Opacity", animation);
    }

    private void OnPreviewImageOpened(object sender, RoutedEventArgs e)
    {
        AnimateImageReveal(PreviewImage);
    }

    private async void OnPreviewImageTapped(object sender, TappedRoutedEventArgs e)
    {
        try
        {
            await _host.ShowImageViewerAsync(PreviewImage.Source, T("LocalLibrary.ModPreview"));
        }
        catch (Exception ex)
        {
            _host.ReportUiActionFailure("OnPreviewImageTapped", ex);
        }
    }

    internal async void OnPickSourceClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            string? picked = await _host.PickFolderAsync();
            if (!string.IsNullOrEmpty(picked))
            {
                Paths.Source = picked;
                _host.SaveConfig();
                await RefreshListsAsync();
            }
        }
        catch (Exception ex)
        {
            _host.ReportUiActionFailure("OnPickSourceClicked", ex);
        }
    }

    internal async void OnPickTargetClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            string? picked = await _host.PickFolderAsync();
            if (!string.IsNullOrEmpty(picked))
            {
                Paths.Target = picked;
                _host.SaveConfig();
                await RefreshListsAsync();
            }
        }
        catch (Exception ex)
        {
            _host.ReportUiActionFailure("OnPickTargetClicked", ex);
        }
    }

    internal async void OnPickLauncherClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            string? picked = await _host.PickFileAsync([".exe", ".bat", ".cmd"]);
            if (!string.IsNullOrEmpty(picked))
            {
                Paths.Launcher = picked;
                _host.SaveConfig();
            }
        }
        catch (Exception ex)
        {
            _host.ReportUiActionFailure("OnPickLauncherClicked", ex);
        }
    }

    internal void OnOpenSourceClicked(object sender, RoutedEventArgs e) => _host.OpenDirectory(Paths.Source, T("LocalLibrary.SourceFolder"));

    internal void OnOpenTargetClicked(object sender, RoutedEventArgs e) => _host.OpenDirectory(Paths.Target, T("LocalLibrary.TargetFolder"));

    internal void OnOpenLauncherClicked(object sender, RoutedEventArgs e) => _host.OpenFileLocation(Paths.Launcher, T("LocalLibrary.Launcher"));

    internal async void OnRunLauncherClicked(object sender, RoutedEventArgs e) => await _host.RunUiActionAsync("OnRunLauncherClicked", _host.RunRepositoryLauncherAsync);

    private async void OnRefreshClicked(object sender, RoutedEventArgs e) => await _host.RunUiActionAsync("OnRefreshClicked", RefreshListsAsync);

    internal async void OnImportZipClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            FirstLevelFolderItem? item = _host.Characters.Selected;
            if (item is null)
            {
                await ShowMessageAsync(
                    T("LocalLibrary.SelectAFirstLevelCategoryFirst"),
                    T("LocalLibrary.NoCategorySelected"));
                return;
            }

            string? archive = await _host.PickFileAsync(_host.SupportedArchiveExtensions
                .Select(value => Path.GetExtension(value)!)
                .Where(extension => !string.IsNullOrWhiteSpace(extension))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
            if (archive is not null)
            {
                await ImportArchiveToSecondLevelFolderAsync(archive, item);
            }
        }
        catch (Exception ex)
        {
            _host.LogApplicationIssue("Open archive picker", ex);
            await ShowMessageAsync(
                T("LocalLibrary.CouldNotOpenTheArchivePicker") + ex.Message,
                T("LocalLibrary.ImportModFailed"));
        }
    }

    private async void OnToggleCopyClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var item = GetSelectedSecondLevelItem();
            if (item is null)
            {
                await ShowMessageAsync(
                    T("Common.SelectASecondLevelModFirst"),
                    T("Common.NoModSelected"));
                return;
            }

            await ToggleDirectoryCopyAsync(item);
        }
        catch (Exception ex)
        {
            _host.ReportUiActionFailure("OnToggleCopyClicked", ex);
        }
    }

    private void OnFirstLevelSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _host.Characters.Selected = FirstLevelListView.SelectedItem as FirstLevelFolderItem;
        PopulateSecondLevelList(_host.Characters.Selected);
        UpdateCharacterHeaderActions();
    }

    private void OnFirstLevelSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFirstLevelFilter();
    }

    private void OnFirstLevelItemRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: FirstLevelFolderItem item } element)
        {
            return;
        }

        FirstLevelListView.SelectedItem = item;
        CreateFirstLevelMenu(item).ShowAt(element);
        e.Handled = true;
    }

    // Shared by the character right-click menu and the "More" button in the character list header.
    private MenuFlyout CreateFirstLevelMenu(FirstLevelFolderItem item)
    {
        var menu = new MenuFlyout();
        var openItem = new MenuFlyoutItem
        {
            Text = T("LocalLibrary.OpenFolder"),
            Icon = new FontIcon { Glyph = "\uE838" }
        };
        openItem.Click += (_, _) => _host.OpenDirectory(item.Path, T("LocalLibrary.FirstLevelFolder"));
        menu.Items.Add(openItem);

        var renameItem = new MenuFlyoutItem
        {
            Text = T("LocalLibrary.Rename"),
            Icon = new FontIcon { Glyph = "\uE70F" }
        };
        renameItem.Click += OnRenameFirstLevelClicked;
        menu.Items.Add(renameItem);
        AddCharacterSelectionMenuItem(menu, item);
        AddCharacterAvatarMenuItems(menu, item);
        return menu;
    }

    private async void OnCreateFirstLevelClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            string sourceDir = Paths.Source.Trim();
            if (string.IsNullOrWhiteSpace(sourceDir) || !Directory.Exists(sourceDir))
            {
                await ShowMessageAsync(
                    T("LocalLibrary.SetAValidModStorageFolder"),
                    T("Common.InvalidPath"));
                return;
            }

            string? name = await PromptForTextAsync(
                T("LocalLibrary.EnterTheNewFirstLevelFolder"),
                T("Common.CreateFirstLevelFolder"),
                string.Empty, T("Dialogs.Create"));
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            try
            {
                string targetPath = Path.Combine(sourceDir, name.Trim());
                if (Directory.Exists(targetPath))
                {
                    await ShowMessageAsync(
                        T("LocalLibrary.AFirstLevelFolderWithThe"),
                        T("LocalLibrary.CreateFailed"));
                    return;
                }

                Directory.CreateDirectory(targetPath);
                await RefreshListsAsync();
                SelectFirstLevelByPath(targetPath);
                StatusTextBlock.Text = T("LocalLibrary.CreatedFirstLevelFolder", name.Trim());
            }
            catch (Exception ex)
            {
                await ShowMessageAsync(
                    T("LocalLibrary.FailedToCreateTheFirstLevel") + ex.Message,
                    T("LocalLibrary.CreateFailed"));
            }
        }
        catch (Exception ex)
        {
            _host.ReportUiActionFailure("OnCreateFirstLevelClicked", ex);
        }
    }

    private async void OnRenameFirstLevelClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            FirstLevelFolderItem? item = _host.Characters.Selected;
            if (item is null)
            {
                await ShowMessageAsync(
                    T("LocalLibrary.SelectAFirstLevelFolderFirst"),
                    T("LocalLibrary.NoFolderSelected"));
                return;
            }

            string? name = await PromptForTextAsync(
                T("LocalLibrary.EnterTheNewFirstLevelFolder"),
                T("LocalLibrary.RenameFirstLevelFolder"),
                item.Name, T("Dialogs.Rename"));
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            string trimmed = name.Trim();
            if (string.Equals(trimmed, item.Name, StringComparison.CurrentCulture))
            {
                return;
            }

            WorkspaceRepository repository = _host.GetSelectedRepository()!;
            string? loader = string.IsNullOrWhiteSpace(repository.TargetPath) ? null : repository.TargetPath;
            CharacterRenamePlan plan = await Task.Run(() => CharacterRename.Prepare(item.Path, trimmed, loader));
            if (!plan.CanRename)
            {
                await ShowMessageAsync(_host.CharacterRenameBlockText(plan, repository), T("LocalLibrary.RenameFailed"));
                return;
            }

            try
            {
                // The folder, the links into it and every saved path move together, or none of them do (#108).
                CharacterRename.Execute(plan, _host.CreateCharacterRenameReferences(repository));
                await RefreshListsAsync();
                SelectFirstLevelByPath(plan.NewPath);
                StatusTextBlock.Text = T("LocalLibrary.RenamedFirstLevelFolderTo", trimmed);
            }
            catch (CharacterRenameException ex)
            {
                _host.LogApplicationIssue("Rename character", ex);
                await ShowMessageAsync(
                    ex.RolledBack
                        ? T("LocalLibrary.RenameUndone", ex.Message)
                        : T("LocalLibrary.RenameNotFullyUndone", ex.Message, string.Join(Environment.NewLine, ex.Unrestored)),
                    T("LocalLibrary.RenameFailed"));
                await RefreshListsAsync();
            }
        }
        catch (Exception ex)
        {
            _host.ReportUiActionFailure("OnRenameFirstLevelClicked", ex);
        }
    }

    private async void OnSecondLevelSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (_syncingRepositorySelection) return;
            _modList.SelectedMod = SecondLevelListView.SelectedItem as SecondLevelFolderItem;
            try
            {
                SecondLevelFolderItem? item = GetSelectedSecondLevelItem();
                ApplySecondLevelSelectionState(item);
                if (item is not null
                    && await _host.TryImportShortcutBindingsFromModFilesAsync(item.Path) > 0
                    && string.Equals(_host.CurrentSecondLevelPath, item.Path, StringComparison.CurrentCultureIgnoreCase))
                {
                    LoadBindingsForCurrentMod(item);
                    // The row's shortcut count and the tab header read the bindings just imported.
                    ApplyModRowState(item);
                    UpdateRepositorySelectedMetadataV4();
                    StatusTextBlock.Text = T("LocalLibrary.ShortcutsWereDetectedFromTheMod");
                }
            }
            catch (Exception ex)
            {
                _host.LogApplicationIssue("Mod selection", ex);
                ClearPreview();
                StatusTextBlock.Text = T("LocalLibrary.CouldNotReadTheSelectedMod", ex.Message);
            }
        }
        catch (Exception ex)
        {
            _host.ReportUiActionFailure("OnSecondLevelSelectionChanged", ex);
        }
    }

    private async void OnSecondLevelDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        try
        {
            var item = GetSelectedSecondLevelItem();
            if (item is not null)
            {
                await ToggleDirectoryCopyAsync(item);
            }
        }
        catch (Exception ex)
        {
            _host.ReportUiActionFailure("OnSecondLevelDoubleTapped", ex);
        }
    }

    private void OnSecondLevelItemRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SecondLevelFolderItem item } element)
        {
            return;
        }

        SecondLevelListView.SelectedItem = item;
        var menu = new MenuFlyout();
        bool isInstalled = item.State == StateCopiedText || item.State == StateLinkedText;
        var toggleItem = new MenuFlyoutItem
        {
            Text = isInstalled ? T("LocalLibrary.RemoveFromTarget") : T("LocalLibrary.DeployToTarget"),
            Icon = new FontIcon { Glyph = isInstalled ? "\uE74D" : "\uE8B0" }
        };
        toggleItem.Click += async (_, _) => await _host.RunUiActionAsync("ToggleDirectoryCopyAsync", () => ToggleDirectoryCopyAsync(item));
        menu.Items.Add(toggleItem);

        var openItem = new MenuFlyoutItem
        {
            Text = T("LocalLibrary.OpenModFolder"),
            Icon = new FontIcon { Glyph = "\uE838" }
        };
        openItem.Click += (_, _) => _host.OpenDirectory(item.Path, T("Common.ModFolder"));
        menu.Items.Add(openItem);
        menu.Items.Add(new MenuFlyoutSeparator());

        var tagsItem = new MenuFlyoutItem { Text = T("LocalLibrary.EditTags") };
        tagsItem.Click += async (_, _) => await _host.RunUiActionAsync("EditLocalTagsAsync", () => EditLocalTagsAsync(item));
        menu.Items.Add(tagsItem);
        var deleteItem = new MenuFlyoutItem
        {
            Text = T("LocalLibrary.DeleteMod"),
            Icon = new FontIcon { Glyph = "\uE74D" }
        };
        deleteItem.Click += OnDeleteSecondLevelClicked;
        menu.Items.Add(deleteItem);
        menu.ShowAt(element);
        e.Handled = true;
    }

    // Removes the deployment links first: deleting only the source would leave links that
    // break conflict checks for every later installation.
    private void RemoveModAndItsLinks(SecondLevelFolderItem item, IReadOnlyList<string> deployedLinks)
    {
        foreach (string link in deployedLinks)
        {
            if (string.Equals(DirectoryLinkDeployment.TryGetStoredTarget(link), Path.GetFullPath(item.Path).TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
            {
                DirectoryLinkDeployment.RemoveLink(link);
            }
        }

        _host.TrackedModOrigins.Remove(item.Path);
        _host.ModLinks.Remove(item.Path);
        _host.DeleteDirectoryTreeSafely(item.Path);
    }

    private async void OnDeleteSecondLevelClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            SecondLevelFolderItem? item = GetSelectedSecondLevelItem();
            if (item is null)
            {
                await ShowMessageAsync(
                    T("Common.SelectASecondLevelModFirst"),
                    T("Common.NoModSelected"));
                return;
            }

            string loaderRoot = (_host.GetSelectedRepository()?.TargetPath ?? Paths.Target).Trim();
            // Without the full list of links, deleting the folder could leave links to a missing folder (issue #109):
            // a loader folder that is set but cannot be reached stops the delete like a failed scan.
            LinkScanResult scan = await Task.Run(() => DirectoryLinkDeployment.ScanLinksTargeting(item.Path, loaderRoot));
            if (scan.Status == LinkScanStatus.Unavailable)
            {
                _host.LogApplicationIssue("Find deployed links before delete", new DirectoryNotFoundException(scan.Detail));
                await ShowMessageAsync(T("LocalLibrary.LoaderUnavailableBeforeDelete", scan.Detail, item.Name), T("LocalLibrary.DeleteFailed"));
                return;
            }
            if (scan.Status == LinkScanStatus.Failed)
            {
                _host.LogApplicationIssue("Find deployed links before delete", new IOException(scan.Detail));
                await ShowMessageAsync(T("LocalLibrary.LinksUnknown", item.Name, scan.Detail), T("LocalLibrary.DeleteFailed"));
                return;
            }
            IReadOnlyList<string> deployedLinks = scan.Links;

            string linkNote = deployedLinks.Count == 0
                ? string.Empty
                : T("LocalLibrary.LinkSInTheLoaderFolder", deployedLinks.Count, string.Join("\n", deployedLinks));
            bool confirmed = await ShowDangerConfirmAsync(
                T("LocalLibrary.AreYouSureYouWantTo", item.Name) + linkNote,
                T("LocalLibrary.ConfirmDelete"), T("Dialogs.Delete"));
            if (!confirmed)
            {
                return;
            }

            try
            {
                string selectedFirstLevelPath = (_host.Characters.Selected)?.Path ?? string.Empty;
                RemoveModAndItsLinks(item, deployedLinks);
                _host.SaveConfig();
                await RefreshListsAsync();
                SelectFirstLevelByPath(selectedFirstLevelPath);
                StatusTextBlock.Text = T("LocalLibrary.DeletedSecondLevelMod", item.Name);
            }
            catch (Exception ex)
            {
                await ShowMessageAsync(
                    T("LocalLibrary.FailedToDeleteTheSecondLevel") + ex.Message,
                    T("LocalLibrary.DeleteFailed"));
            }
        }
        catch (Exception ex)
        {
            _host.ReportUiActionFailure("OnDeleteSecondLevelClicked", ex);
        }
    }

    private void OnDropTargetDragLeave(object sender, DragEventArgs e)
    {
        if (sender is Border border)
        {
            SetDropTargetHighlight(border, false);
        }
    }

    private void SetDropTargetHighlight(Border border, bool isActive)
    {
        border.BorderBrush = _host.GetAppThemeBrush(isActive ? "AppNavSelectedBorderBrush" : "AppCardBorderBrush");
        border.BorderThickness = isActive ? new Thickness(2) : new Thickness(1);
    }

    private void OnPreviewDragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.None;
        if (sender is Border border)
        {
            SetDropTargetHighlight(border, false);
        }

        if (GetSelectedSecondLevelItem() is null)
        {
            return;
        }

        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            if (sender is Border activeBorder)
            {
                SetDropTargetHighlight(activeBorder, true);
            }
        }
    }

    private async void OnPreviewDrop(object sender, DragEventArgs e)
    {
        try
        {
            if (sender is Border border)
            {
                SetDropTargetHighlight(border, false);
            }

            var item = GetSelectedSecondLevelItem();
            if (item is null)
            {
                await ShowMessageAsync(
                    T("LocalLibrary.SelectASecondLevelModBefore"),
                    T("Common.NoModSelected"));
                return;
            }

            if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                await ShowMessageAsync(
                    T("LocalLibrary.OnlyImageFilesDraggedFromFile"),
                    T("LocalLibrary.UnsupportedDropContent"));
                return;
            }

            IReadOnlyList<IStorageItem> droppedItems = await e.DataView.GetStorageItemsAsync();
            StorageFile? imageFile = droppedItems.OfType<StorageFile>().FirstOrDefault(file => IsImageFile(file.Path));
            if (imageFile is null)
            {
                await ShowMessageAsync(
                    T("LocalLibrary.NoSupportedImageFileWasDetected"),
                    T("LocalLibrary.NoImageFound"));
                return;
            }

            await ImportPreviewImageAsync(imageFile.Path, item);
        }
        catch (Exception ex)
        {
            _host.ReportUiActionFailure("OnPreviewDrop", ex);
        }
    }

    private void OnSecondLevelDragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.None;
        if (sender is Border border)
        {
            SetDropTargetHighlight(border, false);
        }

        if (_host.Characters.Selected is null)
        {
            return;
        }

        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            if (sender is Border activeBorder)
            {
                SetDropTargetHighlight(activeBorder, true);
            }
        }
    }

    private async void OnSecondLevelDrop(object sender, DragEventArgs e)
    {
        try
        {
            if (sender is Border border)
            {
                SetDropTargetHighlight(border, false);
            }

            FirstLevelFolderItem? item = _host.Characters.Selected;
            if (item is null)
            {
                await ShowMessageAsync(
                    T("LocalLibrary.SelectAFirstLevelCategoryBefore"),
                    T("LocalLibrary.NoCategorySelected"));
                return;
            }

            if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                await ShowMessageAsync(
                    T("LocalLibrary.OnlyArchiveFilesDraggedFromFile"),
                    T("LocalLibrary.UnsupportedDropContent"));
                return;
            }

            IReadOnlyList<IStorageItem> droppedItems = await e.DataView.GetStorageItemsAsync();
            StorageFile? archiveFile = droppedItems
                .OfType<StorageFile>()
                .FirstOrDefault(file => _host.IsSupportedArchiveFile(file.Path));

            if (archiveFile is null)
            {
                await ShowMessageAsync(
                    T("LocalLibrary.NoSupportedArchiveFileWasDetected"),
                    T("LocalLibrary.NoArchiveFound"));
                return;
            }

            await ImportArchiveToSecondLevelFolderAsync(archiveFile.Path, item);
        }
        catch (Exception ex)
        {
            _host.ReportUiActionFailure("OnSecondLevelDrop", ex);
        }
    }

    internal async Task RefreshListsAsync()
    {
        using var timing = _host.MeasureRepositoryUi(RepositoryPerformanceOperation.Refresh, _host.Characters.All.Sum(first => first.Children.Count));
        _host.FolderRefreshCancellation?.Cancel();
        int generation = ++_host.FolderRefreshGeneration;
        string? previousMod = _host.CurrentSecondLevelPath;
        string? previousCategory = (_host.Characters.Selected)?.Path;
        _host.Characters.All.Clear();
        _modList.SelectedMod = null;
        _host.Characters.Visible.Clear();
        _host.SecondLevelItems.Clear();
        _host.CurrentSecondLevelPath = null;
        _host.RefreshPersistentSlotControls();
        FirstCountTextBlock.Text = "0";
        SecondCountTextBlock.Text = "0";
        ShowModSummary(null);
        LoadDefaultShortcutTemplate();
        LoadLinkForCurrentMod(null);
        ClearPreview();
        UpdateProgress(0, T("Common.NoActiveCopyTask"));

        string sourceDir = Paths.Source.Trim();
        string targetDir = Paths.Target.Trim();
        if (string.IsNullOrWhiteSpace(sourceDir) || !Directory.Exists(sourceDir))
        {
            StatusTextBlock.Text = T("LocalLibrary.ChooseAValidModStorageFolder");
            return;
        }

        if (!string.IsNullOrWhiteSpace(targetDir) && !Directory.Exists(targetDir))
        {
            StatusTextBlock.Text = T("LocalLibrary.TheTargetFolderDoesNotExist");
            return;
        }

        _host.SaveConfig();
        string repositoryId = _host.GetSelectedRepository()?.Id ?? sourceDir;
        using var cancellation = new CancellationTokenSource();
        _host.FolderRefreshCancellation = cancellation;
        _workspaceScanning = true;
        UpdateWorkspaceProgress();
        try
        {
            var loaded = await Task.Run(() =>
            {
                var items = _host.LoadFolderItemsUsingIndex(repositoryId, sourceDir, targetDir,
                    cancellation.Token, out int warnings);
                return (items.Items, items.SecondCount, Warnings: warnings);
            }, cancellation.Token);

            if (cancellation.IsCancellationRequested || generation != _host.FolderRefreshGeneration
                || !string.Equals(sourceDir, Paths.Source.Trim(), StringComparison.OrdinalIgnoreCase)
                || !string.Equals(targetDir, Paths.Target.Trim(), StringComparison.OrdinalIgnoreCase)) return;
            _host.Characters.Load(loaded.Items);
            HideWorkspaceScanError();
            timing?.SetCounts(loaded.SecondCount);
            ApplyFirstLevelFilter();
            _ = RefreshCharacterFolderAvatarsAsync();
            if (_host.GetSelectedRepository() is { SourcePath: { Length: > 0 } scannedSource } scanned
                && string.Equals(Path.GetFullPath(scannedSource).TrimEnd('\\'), Path.GetFullPath(sourceDir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                _ = _host.SeedDefaultSlotsAsync(scanned, loaded.Items);
            FirstCountTextBlock.Text = _host.Characters.All.Count.ToString();
            SecondCountTextBlock.Text = loaded.SecondCount.ToString();
            StatusTextBlock.Text = loaded.Warnings == 0
                ? T("LocalLibrary.LoadedFirstLevelFoldersAndSecond", _host.Characters.All.Count, loaded.SecondCount)
                : T("LocalLibrary.LoadedCategoriesAndModsScanCache", _host.Characters.All.Count, loaded.SecondCount, loaded.Warnings);
            if (_host.Characters.Visible.Count > 0)
            {
                SelectFirstLevelByPath(previousCategory);
                if (!string.IsNullOrEmpty(previousMod)) SelectSecondLevelByPath(previousMod);
                if (_host.Characters.Selected is null) FirstLevelListView.SelectedIndex = 0;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // A newer refresh/repository owns the display. Do not overwrite it.
        }
        catch (Exception ex)
        {
            _host.LogApplicationIssue("Refresh repository folders", ex);
            if (generation == _host.FolderRefreshGeneration && !cancellation.IsCancellationRequested)
            {
                StatusTextBlock.Text = T("LocalLibrary.RepositoryScanFailedTheExistingIndex");
                ShowWorkspaceScanError(T("LocalLibrary.RepositoryScanFailedTheExistingIndex"));
            }
        }
        finally
        {
            if (ReferenceEquals(_host.FolderRefreshCancellation, cancellation)) _host.FolderRefreshCancellation = null;
            if (generation == _host.FolderRefreshGeneration)
            {
                _workspaceScanning = false;
                UpdateWorkspaceProgress();
            }
        }
    }

    internal void PopulateSecondLevelList(FirstLevelFolderItem? firstItem, bool preserveStatus = false)
    {
        using var timing = _host.MeasureRepositoryUi(RepositoryPerformanceOperation.Populate, firstItem?.Children.Count ?? 0);
        _modList.SelectedMod = null;
        ClearPreview();
        ShowModSummary(null);
        _host.CurrentSecondLevelPath = null;
        LoadDefaultShortcutTemplate();
        LoadLinkForCurrentMod(null);

        if (firstItem is null)
        {
            _host.SecondLevelItems.ReplaceAll([]);
            ApplySecondLevelSelectionState(null, preserveStatus: true);
            RefreshRepositoryModView();
            return;
        }

        _host.SecondLevelItems.ReplaceAll(firstItem.Children);
        RefreshRepositoryModView();

        if (!preserveStatus)
        {
            StatusTextBlock.Text = T("LocalLibrary.CurrentFirstLevelFolder") + firstItem.Path;
        }

        if (_host.SecondLevelItems.Count > 0)
        {
            SecondLevelListView.SelectedIndex = 0;
            ApplySecondLevelSelectionState(_host.SecondLevelItems[0], preserveStatus: true);
        }
    }

    internal void ShowSecondLevelDetails(SecondLevelFolderItem? item, bool preserveStatus = false)
    {
        if (item is null)
        {
            ShowModSummary(null);
            ClearPreview();
            LoadLinkForCurrentMod(null);
            return;
        }

        ShowModSummary(item);
        UpdatePreviewForDirectory(item.Path, item.Files);

        if (!preserveStatus)
        {
            StatusTextBlock.Text = T("LocalLibrary.Viewing") + item.Path;
        }
    }

    private void ApplySecondLevelSelectionState(SecondLevelFolderItem? item, bool preserveStatus = false)
    {
        _host.CurrentSecondLevelPath = item?.Path;
        ShowSecondLevelDetails(item, preserveStatus);
        LoadBindingsForCurrentMod(item);
        LoadLinkForCurrentMod(item);
        _host.RefreshPersistentSlotControls();
        SyncRepositorySelectionV4(item);
    }

    // The details summary: the Mod's name and its deployment state, coloured by state.
    internal void ShowModSummary(SecondLevelFolderItem? item)
    {
        _modDetails.Show(item, StateNotSelectedText, item is null ? ModStateTone.Neutral
            : RepositoryWorkspacePolicy.StateTone(item.State, StateMissingText, StateCopiedText, StateLinkedText));
        RenderModSummary();
    }

    internal void RenderModSummary()
    {
        CurrentFolderTextBlock.Text = _modDetails.Name;
        CurrentStateTextBlock.Text = _modDetails.State;
        CurrentStateTextBlock.Foreground = _modDetails.Tone switch
        {
            ModStateTone.Deployed => CopiedBrush,
            ModStateTone.NotDeployed => MissingBrush,
            _ => _services.Appearance.IsDark ? new SolidColorBrush(Colors.WhiteSmoke) : NeutralBrush
        };
    }

    internal async Task ToggleDirectoryCopyAsync(SecondLevelFolderItem item)
    {
        string targetDir = Paths.Target.Trim();
        if (string.IsNullOrWhiteSpace(targetDir) || !Directory.Exists(targetDir))
        {
            await ShowMessageAsync(
                T("LocalLibrary.ChooseAValidTargetFolderFirst"),
                T("LocalLibrary.TargetFolderNotSet"));
            return;
        }

        string targetPath = GetTargetDirectoryPath(targetDir, item.Path);
        WorkspaceRepository? repository = _host.GetSelectedRepository();
        if (repository is null)
        {
            await ShowMessageAsync(T("Common.SelectARepositoryFirst"), T("LocalLibrary.CannotContinue"));
            return;
        }
        repository.TargetPath = targetDir;

        // Issue #105: a loader folder inside the repository (or the other way round, or the same folder through a
        // link) would make this Mod's deployed folder its own source; stop before any backup, copy or delete.
        if (FindDeploymentOverlap(repository.SourcePath, targetDir, item.Path, targetPath) is (string source, string target))
        {
            await ShowMessageAsync(T("LocalLibrary.PathsOverlap", source, target), T("LocalLibrary.CannotContinue"));
            return;
        }

        if (!await _host.OfferBrokenLinkCleanupAsync(targetDir))
        {
            return;
        }

        bool targetExists = Directory.Exists(targetPath);
        // Issue #107: a copied folder with this Mod's name that the manager did not make is the user's; say so first.
        if (targetExists && !DirectoryLinkDeployment.IsDirectoryLink(targetPath))
        {
            bool owned = await Task.Run(() => _host.OwnsCopy(repository)(item.Path, targetPath));
            _host.SaveCopyRecordsIfChanged();
            if (!owned && !await ShowDangerConfirmAsync(T("LocalLibrary.RemoveExternalFolder", targetPath), T("LocalLibrary.ExternalFolder"), T("Dialogs.Remove")))
                return;
        }
        IReadOnlyList<string> siblingLinksToReplace = Array.Empty<string>();
        try
        {
            if (!targetExists && repository.UseDirectoryLinks && !AllowsMultipleMods(repository, item.Path))
            {
                siblingLinksToReplace = await Task.Run(() =>
                    DirectoryLinkDeployment.FindSiblingLinksToReplace(item.Path, repository.SourcePath, targetDir));
            }

            if (!targetExists && _host.EnableConflictDetection)
            {
                string[] ignoredTargets = [targetPath, .. siblingLinksToReplace];
                List<string> conflicts = await Task.Run(() => _host.DetectModFileConflicts(item.Path, targetDir, ignoredTargets));
                (string? Candidate, ModIniConflictSummary Summary)[] iniConflicts = (await Task.Run(() =>
                        _host.DetectModIniConflictsSafely(item.Path, targetDir, ignoredTargets)))
                    .Select(summary => ((string?)null, summary)).ToArray();
                if ((conflicts.Count > 0 || iniConflicts.Length > 0) && !await _host.ConfirmInstallConflictsAsync(conflicts, iniConflicts))
                {
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            _host.LogApplicationIssue("Mod deployment planning", ex);
            await ShowMessageAsync(
                T("LocalLibrary.CouldNotInspectTheSelectedMod") + ex.Message,
                T("LocalLibrary.CannotContinue"));
            return;
        }

        string? transactionPath = null;
        SetBusyState(true);
        try
        {
            transactionPath = await _host.CreateInstallTransactionAsync(
                targetExists
                    ? T("LocalLibrary.RemoveMod", item.Name)
                    : siblingLinksToReplace.Count > 0
                        ? T("LocalLibrary.SwitchSameCharacterMod", item.Name)
                    : T("LocalLibrary.InstallMod", item.Name),
                repository,
                [targetPath, .. siblingLinksToReplace]);

            if (targetExists)
            {
                UpdateProgress(15, T("LocalLibrary.RemovingFolder"));
                await Task.Run(() => _host.DeleteDirectoryTreeSafely(targetPath));
                _host.CopyLedger(repository).Forget(targetPath);
                _host.SaveShellConfig();
                UpdateProgress(100, T("LocalLibrary.RemovalComplete"));
                StatusTextBlock.Text = item.Name + T("LocalLibrary.WasRemovedFromTheTargetFolder");
                ShowAppNotification(T("LocalLibrary.RemovedFromTarget", item.Name));
            }
            else
            {
                if (siblingLinksToReplace.Count > 0)
                {
                    UpdateProgress(10, T("LocalLibrary.DisconnectingThePreviousLinkForThis"));
                    await Task.Run(() =>
                    {
                        foreach (string oldLinkPath in siblingLinksToReplace)
                        {
                            DirectoryLinkDeployment.RemoveLink(oldLinkPath);
                        }
                    });
                }

                var progress = new Progress<ProgressInfo>(info => UpdateProgress(info.Percent, info.Message));
                await DeployModAsync(item.Path, targetPath, repository, progress);
                string replacedNames = string.Join(T("Common.ListSeparator"), siblingLinksToReplace.Select(Path.GetFileName));
                StatusTextBlock.Text = repository.UseDirectoryLinks
                    ? siblingLinksToReplace.Count > 0
                        ? T("LocalLibrary.DisconnectedAndLinked", replacedNames, item.Name)
                        : item.Name + T("LocalLibrary.WasLinkedIntoTheTargetFolder")
                    : item.Name + T("LocalLibrary.WasCopiedToTheTargetFolder");
                ShowAppNotification(
                repository.UseDirectoryLinks
                    ? siblingLinksToReplace.Count > 0
                        ? T("LocalLibrary.SwitchedSameCharacterLink", replacedNames, item.Name)
                        : T("LocalLibrary.LinkedIntoTarget", item.Name)
                    : T("LocalLibrary.CopiedToTarget", item.Name));
            }

            _host.CommitInstallTransaction(transactionPath);
            await _host.RefreshShortcutDeployedModsAsync(targetExists ? null : item.Path);
        }
        catch (Exception ex)
        {
            if (!string.IsNullOrWhiteSpace(transactionPath))
            {
                try
                {
                    await _host.RollbackInstallTransactionAsync(transactionPath, clearLastTransaction: true);
                }
                catch (Exception rollbackError)
                {
                    ex = new AggregateException(ex, rollbackError);
                }
            }

            await ShowMessageAsync(
                T("LocalLibrary.OperationFailedTheAppAttemptedTo") + ex.Message,
                T("LocalLibrary.Error"));
        }
        finally
        {
            SetBusyState(false);
            await RefreshListsAsync();
            SelectSecondLevelByPath(item.Path);
        }
    }

    private async Task ImportArchiveToSecondLevelFolderAsync(string archivePath, FirstLevelFolderItem item)
    {
        SetBusyState(true);
        try
        {
            string importedPath = await Task.Run(() => _host.ImportArchiveContents(archivePath, item.Path));

            string importedName = Path.GetFileName(importedPath);
            StatusTextBlock.Text = T("LocalLibrary.InstalledAs", Path.GetFileName(archivePath), item.Name, importedName, importedPath);
            ShowAppNotification(T("LocalLibrary.ArchiveInstalled", item.Name, importedName));
            await RefreshListsAsync();
            SelectFirstLevelByPath(item.Path);
            SelectSecondLevelByPath(importedPath);
            await _host.TryImportShortcutBindingsFromModFilesAsync(importedPath);
        }
        catch (Exception ex)
        {
            await ShowMessageAsync(
                T("LocalLibrary.ArchiveImportFailed") + ex.Message,
                T("LocalLibrary.ImportFailed"));
        }
        finally
        {
            SetBusyState(false);
        }
    }

    private async Task ImportPreviewImageAsync(string imagePath, SecondLevelFolderItem item)
    {
        SetBusyState(true);
        try
        {
            if (!IsImageFile(imagePath))
            {
                throw new InvalidOperationException(T("LocalLibrary.TheDroppedFileIsNotA"));
            }

            string extension = Path.GetExtension(imagePath).ToLowerInvariant();
            await Task.Run(() =>
            {
                Directory.CreateDirectory(item.Path);
                foreach (string existingFile in Directory.GetFiles(item.Path))
                {
                    string baseName = Path.GetFileNameWithoutExtension(existingFile).ToLowerInvariant();
                    if (IsImageFile(existingFile) && (baseName == "preview" || baseName == "cover" || baseName == "thumbnail" || baseName == "image"))
                    {
                        File.Delete(existingFile);
                    }
                }

                string targetPath = Path.Combine(item.Path, "preview" + extension);
                File.Copy(imagePath, targetPath, true);
            });

            StatusTextBlock.Text = item.Name + T("LocalLibrary.SPreviewImageWasUpdated");
            ShowAppNotification(T("LocalLibrary.PreviewUpdated", item.Name));
            await RefreshListsAsync();
            SelectSecondLevelByPath(item.Path);
        }
        catch (Exception ex)
        {
            await ShowMessageAsync(
                T("LocalLibrary.PreviewImportFailed") + ex.Message,
                T("LocalLibrary.ImportFailed"));
        }
        finally
        {
            SetBusyState(false);
        }
    }

    private async Task CopyDirectoryWithProgressAsync(string sourcePath, string targetPath, IProgress<ProgressInfo> progress)
    {
        await Task.Run(() =>
        {
            (List<string> directories, List<string> files) = _host.InspectDirectoryTreeSafely(sourcePath);
            int totalSteps = directories.Count + files.Count + 1;
            int currentStep = 0;

            Directory.CreateDirectory(targetPath);
            currentStep++;
            progress.Report(new ProgressInfo(currentStep * 100 / totalSteps, T("LocalLibrary.CreatingFolder")));

            foreach (string directory in directories)
            {
                string relativePath = Path.GetRelativePath(sourcePath, directory);
                Directory.CreateDirectory(_host.GetSafePathInsideDirectory(targetPath, relativePath));
                currentStep++;
                progress.Report(new ProgressInfo(currentStep * 100 / totalSteps, T("LocalLibrary.CreatingSubfolders")));
            }

            foreach (string file in files)
            {
                string relativePath = Path.GetRelativePath(sourcePath, file);
                string destinationFile = _host.GetSafePathInsideDirectory(targetPath, relativePath);
                string? destinationDir = Path.GetDirectoryName(destinationFile);
                if (!string.IsNullOrEmpty(destinationDir))
                {
                    Directory.CreateDirectory(destinationDir);
                }

                File.Copy(file, destinationFile, true);
                currentStep++;
                progress.Report(new ProgressInfo(currentStep * 100 / totalSteps, T("LocalLibrary.CopyingFiles")));
            }

            progress.Report(new ProgressInfo(100, T("LocalLibrary.CopyComplete")));
        });
    }

    internal async Task DeployModAsync(
        string sourcePath,
        string targetPath,
        WorkspaceRepository repository,
        IProgress<ProgressInfo> progress)
    {
        if (!repository.UseDirectoryLinks)
        {
            await CopyDirectoryWithProgressAsync(sourcePath, targetPath, progress);
            // Issue #107: the manager's own copy, recorded in its settings.
            _host.CopyLedger(repository).Record(sourcePath, targetPath);
            _host.SaveShellConfig();
            return;
        }

        progress.Report(new ProgressInfo(15, T("LocalLibrary.ValidatingRepositoryPath")));
        string fullSourcePath = Path.GetFullPath(sourcePath);
        if (!OnlinePage.IsPathInsideDirectory(fullSourcePath, repository.SourcePath))
        {
            throw new InvalidOperationException(T("LocalLibrary.RefusedToLinkADirectoryOutside"));
        }

        await Task.Run(() => DirectoryLinkDeployment.CreateJunction(targetPath, fullSourcePath));
        progress.Report(new ProgressInfo(100, T("LocalLibrary.DirectoryJunctionCreated")));
    }

    internal void UpdateProgress(int percent, string message)
    {
        CopyProgressBar.Value = percent;
        ProgressTextBlock.Text = message;
    }

    private bool _workspaceScanning;
    private bool _workspaceBusy;

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _workspaceProgressDelay;

    // The progress bar on the toolbar's lower edge while the repository is scanned or a long action runs (stage J7-b).
    // It overlays the edge instead of taking a row, and waits 300 ms so quick scans do not flash it.
    private void UpdateWorkspaceProgress()
    {
        if (!_workspaceScanning && !_workspaceBusy)
        {
            _workspaceProgressDelay?.Stop();
            WorkspaceProgressBar.Visibility = Visibility.Collapsed;
            return;
        }
        if (WorkspaceProgressBar.Visibility == Visibility.Visible) return;
        _workspaceProgressDelay ??= CreateWorkspaceProgressDelay();
        _workspaceProgressDelay.Start();
    }

    private Microsoft.UI.Dispatching.DispatcherQueueTimer CreateWorkspaceProgressDelay()
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(300);
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            if (_workspaceScanning || _workspaceBusy) WorkspaceProgressBar.Visibility = Visibility.Visible;
        };
        return timer;
    }

    private void ShowWorkspaceScanError(string message)
    {
        WorkspaceErrorInfoBar.Message = message;
        if (WorkspaceErrorInfoBar.ActionButton is not Button retry)
        {
            retry = new Button();
            retry.Click += async (_, _) => await _host.RunUiActionAsync("RetryRepositoryScan", RefreshListsAsync);
            WorkspaceErrorInfoBar.ActionButton = retry;
        }
        retry.Content = T("DownloadTasks.Retry");
        WorkspaceErrorInfoBar.Visibility = Visibility.Visible;
        WorkspaceErrorInfoBar.IsOpen = true;
    }

    private void HideWorkspaceScanError()
    {
        WorkspaceErrorInfoBar.IsOpen = false;
        WorkspaceErrorInfoBar.Visibility = Visibility.Collapsed;
    }

    internal void SetBusyState(bool busy)
    {
        _workspaceBusy = busy;
        UpdateWorkspaceProgress();
        SettingsView.IsBusy = busy;
        Paths.IsLinkDeploymentEditable = !busy;
        FirstLevelListView.IsEnabled = !busy;
        SecondLevelListView.IsEnabled = !busy;
        OpenModLinkButton.IsEnabled = !busy && !string.IsNullOrWhiteSpace(ModLinkTextBox.Text);
        SetRepositoryWorkspaceBusyV4(busy);
    }

    internal void SelectSecondLevelByPath(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        for (int firstIndex = 0; firstIndex < _host.Characters.Visible.Count; firstIndex++)
        {
            FirstLevelFolderItem first = _host.Characters.Visible[firstIndex];
            if (first.Children.Any(child => string.Equals(child.Path, path, StringComparison.CurrentCultureIgnoreCase)))
            {
                FirstLevelListView.SelectedIndex = firstIndex;
                for (int secondIndex = 0; secondIndex < _host.SecondLevelItems.Count; secondIndex++)
                {
                    if (string.Equals(_host.SecondLevelItems[secondIndex].Path, path, StringComparison.CurrentCultureIgnoreCase))
                    {
                        SecondLevelFolderItem selected = _host.SecondLevelItems[secondIndex];
                        SecondLevelListView.SelectedItem = selected;
                        if (_modList.VisibleMods.Contains(selected)) SecondLevelListView.ScrollIntoView(selected);
                        ApplySecondLevelSelectionState(_host.SecondLevelItems[secondIndex], preserveStatus: true);
                        return;
                    }
                }
            }
        }
    }

    private static List<string> GetFiles(string directory)
    {
        string[] files = Directory.GetFiles(directory);
        Array.Sort(files, StringComparer.CurrentCultureIgnoreCase);
        return files.ToList();
    }

    // The first overlapping pair: the repository and loader folders, then the Mod and its deployed folder.
    private static (string Source, string Target)? FindDeploymentOverlap(string? sourceRoot, string targetRoot, string modPath, string deployedPath)
    {
        if (!string.IsNullOrWhiteSpace(sourceRoot) && DeploymentPathSeparation.Compare(sourceRoot, targetRoot) != PathOverlap.None)
            return (sourceRoot, targetRoot);
        return DeploymentPathSeparation.Compare(modPath, deployedPath) != PathOverlap.None ? (modPath, deployedPath) : null;
    }

    private static string GetTargetDirectoryPath(string targetDir, string sourceDirectoryPath)
    {
        return ModDeploymentLayout.DeployedPath(targetDir, sourceDirectoryPath);
    }

    internal string GetFolderCopyState(string targetDir, string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(targetDir))
        {
            return StateNotConfiguredText;
        }

        string targetPath = GetTargetDirectoryPath(targetDir, directoryPath);
        if (!Directory.Exists(targetPath))
        {
            return StateMissingText;
        }

        return DirectoryLinkDeployment.IsDirectoryLink(targetPath) ? StateLinkedText : StateCopiedText;
    }

    internal SecondLevelFolderItem? GetSelectedSecondLevelItem()
    {
        return _modList.SelectedMod ?? SecondLevelListView.SelectedItem as SecondLevelFolderItem;
    }

    private async void UpdatePreviewForDirectory(string directoryPath, List<string> files)
    {
        try
        {
            int requestVersion = ++_previewImageRequestVersion;
            try
            {
                List<string> liveFiles = Directory.Exists(directoryPath)
                    ? GetFiles(directoryPath)
                    : files.Where(File.Exists).ToList();
                await SetPreviewImageAsync(FindPreviewImage(liveFiles), requestVersion);
            }
            catch (Exception ex)
            {
                _host.LogApplicationIssue("Mod preview selection", ex);
                if (requestVersion == _previewImageRequestVersion)
                {
                    PreviewImage.Source = null;
                    PreviewHintTextBlock.Text = T("LocalLibrary.TheImageCouldNotBeLoaded");
                    PreviewHintTextBlock.Visibility = Visibility.Visible;
                }
            }
        }
        catch (Exception ex)
        {
            _host.ReportUiActionFailure("UpdatePreviewForDirectory", ex);
        }
    }

    private async Task SetPreviewImageAsync(string? imagePath, int requestVersion)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            if (requestVersion == _previewImageRequestVersion)
            {
                PreviewImage.Source = null;
                PreviewHintTextBlock.Text = T("LocalLibrary.NoPreviewImageWasFoundFor");
                PreviewHintTextBlock.Visibility = Visibility.Visible;
            }
            return;
        }

        BitmapImage bitmap = await ImageService.LoadLocalBitmapAsync(imagePath);
        if (requestVersion == _previewImageRequestVersion)
        {
            PreviewImage.Source = bitmap;
            if (GetSelectedSecondLevelItem() is { } selected) selected.CoverImageSource = bitmap;
            PreviewHintTextBlock.Visibility = Visibility.Collapsed;
        }
    }

    internal void ClearPreview()
    {
        _previewImageRequestVersion++;
        PreviewImage.Source = null;
        PreviewHintTextBlock.Text = T("LocalLibrary.NoPreviewImageWasFoundFor");
        PreviewHintTextBlock.Visibility = Visibility.Visible;
    }

    internal string? FindPreviewImage(List<string> files)
    {
        string? namedPreview = files.FirstOrDefault(file =>
        {
            string name = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
            return IsImageFile(file) && (name == "preview" || name == "cover" || name == "thumbnail" || name == "image");
        });

        if (!string.IsNullOrEmpty(namedPreview))
        {
            return namedPreview;
        }

        string[] images = files
            .Where(IsImageFile)
            .OrderBy(path => path, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        return images.FirstOrDefault();
    }

    private bool IsImageFile(string path)
    {
        string extension = Path.GetExtension(path);
        return !string.IsNullOrEmpty(extension) && _host.ImageExtensions.Contains(extension.ToLowerInvariant());
    }

    internal void ApplyFirstLevelFilter()
    {
        string? selectedPath = _host.Characters.Selected?.Path;
        _host.Characters.ApplyFilter(FirstLevelSearchTextBox.Text);
        SelectFirstLevelByPath(selectedPath);
        if (_host.Characters.Selected is null && _host.Characters.Visible.Count > 0)
        {
            FirstLevelListView.SelectedIndex = 0;
        }
    }

    internal void SelectFirstLevelByPath(string? path)
    {
        if (_host.Characters.FindVisible(path) is { } item)
        {
            FirstLevelListView.SelectedItem = item;
        }
    }
}
