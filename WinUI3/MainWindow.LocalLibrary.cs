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
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private void AnimateImageReveal(UIElement image)
    {
        if (ShouldReduceAnimations)
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
            await ShowImageViewerAsync(PreviewImage.Source, T("LocalLibrary.ModPreview"));
        }
        catch (Exception ex)
        {
            ReportUiActionFailure("OnPreviewImageTapped", ex);
        }
    }

    private async void OnPickSourceClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            string? picked = await PickFolderAsync();
            if (!string.IsNullOrEmpty(picked))
            {
                SourceTextBox.Text = picked;
                SaveConfig();
                await RefreshListsAsync();
            }
        }
        catch (Exception ex)
        {
            ReportUiActionFailure("OnPickSourceClicked", ex);
        }
    }

    private async void OnPickTargetClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            string? picked = await PickFolderAsync();
            if (!string.IsNullOrEmpty(picked))
            {
                TargetTextBox.Text = picked;
                SaveConfig();
                await RefreshListsAsync();
            }
        }
        catch (Exception ex)
        {
            ReportUiActionFailure("OnPickTargetClicked", ex);
        }
    }

    private async void OnPickLauncherClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            string? picked = await PickFileAsync([".exe", ".bat", ".cmd"]);
            if (!string.IsNullOrEmpty(picked))
            {
                LauncherTextBox.Text = picked;
                SaveConfig();
            }
        }
        catch (Exception ex)
        {
            ReportUiActionFailure("OnPickLauncherClicked", ex);
        }
    }

    private void OnOpenSourceClicked(object sender, RoutedEventArgs e) => OpenDirectory(SourceTextBox.Text, T("LocalLibrary.SourceFolder"));

    private void OnOpenTargetClicked(object sender, RoutedEventArgs e) => OpenDirectory(TargetTextBox.Text, T("LocalLibrary.TargetFolder"));

    private void OnOpenLauncherClicked(object sender, RoutedEventArgs e) => OpenFileLocation(LauncherTextBox.Text, T("LocalLibrary.Launcher"));

    private async void OnRunLauncherClicked(object sender, RoutedEventArgs e) => await RunUiActionAsync("OnRunLauncherClicked", RunRepositoryLauncherAsync);

    private async void OnRefreshClicked(object sender, RoutedEventArgs e) => await RunUiActionAsync("OnRefreshClicked", RefreshListsAsync);

    private async void OnImportZipClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            FirstLevelFolderItem? item = FirstLevelListView.SelectedItem as FirstLevelFolderItem;
            if (item is null)
            {
                await ShowMessageAsync(
                    T("LocalLibrary.SelectAFirstLevelCategoryFirst"),
                    T("LocalLibrary.NoCategorySelected"));
                return;
            }

            var picker = new FileOpenPicker();
            foreach (string extension in SupportedArchiveExtensions
                .Select(value => Path.GetExtension(value)!)
                .Where(extension => !string.IsNullOrWhiteSpace(extension))
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                picker.FileTypeFilter.Add(extension!);
            }
            picker.SuggestedStartLocation = PickerLocationId.Downloads;
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            StorageFile? file = await picker.PickSingleFileAsync();
            if (file is not null)
            {
                await ImportArchiveToSecondLevelFolderAsync(file.Path, item);
            }
        }
        catch (Exception ex)
        {
            LogApplicationIssue("Open archive picker", ex);
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
            ReportUiActionFailure("OnToggleCopyClicked", ex);
        }
    }

    private void OnFirstLevelSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        PopulateSecondLevelList(FirstLevelListView.SelectedItem as FirstLevelFolderItem);
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
        var menu = new MenuFlyout();
        var openItem = new MenuFlyoutItem
        {
            Text = T("LocalLibrary.OpenFolder"),
            Icon = new FontIcon { Glyph = "\uE838" }
        };
        openItem.Click += (_, _) => OpenDirectory(item.Path, T("LocalLibrary.FirstLevelFolder"));
        menu.Items.Add(openItem);

        var renameItem = new MenuFlyoutItem
        {
            Text = T("LocalLibrary.Rename"),
            Icon = new FontIcon { Glyph = "\uE70F" }
        };
        renameItem.Click += OnRenameFirstLevelClicked;
        menu.Items.Add(renameItem);
        AddCharacterAvatarMenuItems(menu, item);
        menu.ShowAt(element);
        e.Handled = true;
    }

    private async void OnCreateFirstLevelClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            string sourceDir = (SourceTextBox.Text ?? string.Empty).Trim();
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
                string.Empty);
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
            ReportUiActionFailure("OnCreateFirstLevelClicked", ex);
        }
    }

    private async void OnRenameFirstLevelClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            FirstLevelFolderItem? item = FirstLevelListView.SelectedItem as FirstLevelFolderItem;
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
                item.Name);
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            string trimmed = name.Trim();
            if (string.Equals(trimmed, item.Name, StringComparison.CurrentCulture))
            {
                return;
            }

            try
            {
                string? parentDir = Path.GetDirectoryName(item.Path);
                if (string.IsNullOrWhiteSpace(parentDir))
                {
                    throw new InvalidOperationException(T("LocalLibrary.CouldNotDetermineTheParentDirectory"));
                }

                string newPath = Path.Combine(parentDir, trimmed);
                if (Directory.Exists(newPath))
                {
                    await ShowMessageAsync(
                        T("LocalLibrary.TheTargetNameAlreadyExistsChoose"),
                        T("LocalLibrary.RenameFailed"));
                    return;
                }

                var tagLibrary = SelectedLocalLibrary();
                string oldRoleRelative = WorkspaceIdentity.Relative(GetSelectedRepository()!.SourcePath, item.Path);
                Directory.Move(item.Path, newPath);
                tagLibrary?.RenameCharacter(oldRoleRelative, WorkspaceIdentity.Relative(GetSelectedRepository()!.SourcePath, newPath));
                SaveShellConfig();
                await RefreshListsAsync();
                SelectFirstLevelByPath(newPath);
                StatusTextBlock.Text = T("LocalLibrary.RenamedFirstLevelFolderTo", trimmed);
            }
            catch (Exception ex)
            {
                await ShowMessageAsync(
                    T("LocalLibrary.FailedToRenameTheFirstLevel") + ex.Message,
                    T("LocalLibrary.RenameFailed"));
            }
        }
        catch (Exception ex)
        {
            ReportUiActionFailure("OnRenameFirstLevelClicked", ex);
        }
    }

    private async void OnSecondLevelSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (_syncingRepositorySelection) return;
            _repositorySelectedMod = SecondLevelListView.SelectedItem as SecondLevelFolderItem;
            try
            {
                SecondLevelFolderItem? item = GetSelectedSecondLevelItem();
                ApplySecondLevelSelectionState(item);
                if (item is not null
                    && await TryImportShortcutBindingsFromModFilesAsync(item.Path) > 0
                    && string.Equals(_currentSecondLevelPath, item.Path, StringComparison.CurrentCultureIgnoreCase))
                {
                    LoadBindingsForCurrentMod(item);
                    StatusTextBlock.Text = T("LocalLibrary.ShortcutsWereDetectedFromTheMod");
                }
            }
            catch (Exception ex)
            {
                LogApplicationIssue("Mod selection", ex);
                ClearPreview();
                StatusTextBlock.Text = T("LocalLibrary.CouldNotReadTheSelectedMod", ex.Message);
            }
        }
        catch (Exception ex)
        {
            ReportUiActionFailure("OnSecondLevelSelectionChanged", ex);
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
            ReportUiActionFailure("OnSecondLevelDoubleTapped", ex);
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
        toggleItem.Click += async (_, _) => await RunUiActionAsync("ToggleDirectoryCopyAsync", () => ToggleDirectoryCopyAsync(item));
        menu.Items.Add(toggleItem);

        var openItem = new MenuFlyoutItem
        {
            Text = T("LocalLibrary.OpenModFolder"),
            Icon = new FontIcon { Glyph = "\uE838" }
        };
        openItem.Click += (_, _) => OpenDirectory(item.Path, T("Common.ModFolder"));
        menu.Items.Add(openItem);
        menu.Items.Add(new MenuFlyoutSeparator());

        var tagsItem = new MenuFlyoutItem { Text = T("LocalLibrary.EditTags") };
        tagsItem.Click += async (_, _) => await RunUiActionAsync("EditLocalTagsAsync", () => EditLocalTagsAsync(item));
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

            string loaderRoot = (GetSelectedRepository()?.TargetPath ?? TargetTextBox.Text ?? string.Empty).Trim();
            IReadOnlyList<string> deployedLinks = [];
            try
            {
                if (!string.IsNullOrWhiteSpace(loaderRoot))
                {
                    deployedLinks = await Task.Run(() => DirectoryLinkDeployment.FindLinksTargeting(item.Path, loaderRoot));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                LogApplicationIssue("Find deployed links before delete", ex);
            }

            string linkNote = deployedLinks.Count == 0
                ? string.Empty
                : T("LocalLibrary.LinkSInTheLoaderFolder", deployedLinks.Count, string.Join("\n", deployedLinks));
            bool confirmed = await ShowConfirmAsync(
                T("LocalLibrary.AreYouSureYouWantTo", item.Name) + linkNote,
                T("LocalLibrary.ConfirmDelete"));
            if (!confirmed)
            {
                return;
            }

            try
            {
                string selectedFirstLevelPath = (FirstLevelListView.SelectedItem as FirstLevelFolderItem)?.Path ?? string.Empty;
                // Remove the deployment links first: deleting only the source would leave
                // links that break conflict checks for every later installation.
                foreach (string link in deployedLinks)
                {
                    if (string.Equals(DirectoryLinkDeployment.TryGetStoredTarget(link), Path.GetFullPath(item.Path).TrimEnd(Path.DirectorySeparatorChar),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        DirectoryLinkDeployment.RemoveLink(link);
                    }
                }

                _trackedModOrigins.Remove(item.Path);
                _modLinks.Remove(item.Path);
                DeleteDirectoryTreeSafely(item.Path);
                SaveConfig();
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
            ReportUiActionFailure("OnDeleteSecondLevelClicked", ex);
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
        border.BorderBrush = GetAppThemeBrush(isActive ? "AppNavSelectedBorderBrush" : "AppCardBorderBrush");
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
            ReportUiActionFailure("OnPreviewDrop", ex);
        }
    }

    private void OnSecondLevelDragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.None;
        if (sender is Border border)
        {
            SetDropTargetHighlight(border, false);
        }

        if (FirstLevelListView.SelectedItem is null)
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

            FirstLevelFolderItem? item = FirstLevelListView.SelectedItem as FirstLevelFolderItem;
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
                .FirstOrDefault(file => IsSupportedArchiveFile(file.Path));

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
            ReportUiActionFailure("OnSecondLevelDrop", ex);
        }
    }

    private async Task RefreshListsAsync()
    {
        using var timing = MeasureRepositoryUi(RepositoryPerformanceOperation.Refresh, _allFirstLevelItems.Sum(first => first.Children.Count));
        _folderRefreshCancellation?.Cancel();
        int generation = ++_folderRefreshGeneration;
        string? previousMod = _currentSecondLevelPath;
        string? previousCategory = (FirstLevelListView.SelectedItem as FirstLevelFolderItem)?.Path;
        _allFirstLevelItems.Clear();
        _repositorySelectedMod = null;
        _firstLevelItems.Clear();
        _secondLevelItems.Clear();
        _currentSecondLevelPath = null;
        RefreshPersistentSlotControls();
        FirstCountTextBlock.Text = "0";
        SecondCountTextBlock.Text = "0";
        CurrentFolderTextBlock.Text = StateNotSelectedText;
        CurrentStateTextBlock.Text = StateNotSelectedText;
        SetStateColor(CurrentStateTextBlock.Text);
        LoadDefaultShortcutTemplate();
        LoadLinkForCurrentMod(null);
        ClearPreview();
        UpdateProgress(0, T("Common.NoActiveCopyTask"));

        string sourceDir = (SourceTextBox.Text ?? string.Empty).Trim();
        string targetDir = (TargetTextBox.Text ?? string.Empty).Trim();
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

        SaveConfig();
        string repositoryId = GetSelectedRepository()?.Id ?? sourceDir;
        using var cancellation = new CancellationTokenSource();
        _folderRefreshCancellation = cancellation;
        try
        {
            var loaded = await Task.Run(() =>
            {
                var items = LoadFolderItemsUsingIndex(repositoryId, sourceDir, targetDir,
                    cancellation.Token, out int warnings);
                return (items.Items, items.SecondCount, Warnings: warnings);
            }, cancellation.Token);

            if (cancellation.IsCancellationRequested || generation != _folderRefreshGeneration
                || !string.Equals(sourceDir, (SourceTextBox.Text ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase)
                || !string.Equals(targetDir, (TargetTextBox.Text ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase)) return;
            _allFirstLevelItems.Clear();
            _allFirstLevelItems.AddRange(loaded.Items);
            timing?.SetCounts(loaded.SecondCount);
            ApplyFirstLevelFilter();
            _ = RefreshCharacterFolderAvatarsAsync();
            FirstCountTextBlock.Text = _allFirstLevelItems.Count.ToString();
            SecondCountTextBlock.Text = loaded.SecondCount.ToString();
            StatusTextBlock.Text = loaded.Warnings == 0
                ? T("LocalLibrary.LoadedFirstLevelFoldersAndSecond", _allFirstLevelItems.Count, loaded.SecondCount)
                : T("LocalLibrary.LoadedCategoriesAndModsScanCache", _allFirstLevelItems.Count, loaded.SecondCount, loaded.Warnings);
            if (_firstLevelItems.Count > 0)
            {
                SelectFirstLevelByPath(previousCategory);
                if (!string.IsNullOrEmpty(previousMod)) SelectSecondLevelByPath(previousMod);
                if (FirstLevelListView.SelectedItem is null) FirstLevelListView.SelectedIndex = 0;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // A newer refresh/repository owns the display. Do not overwrite it.
        }
        catch (Exception ex)
        {
            LogApplicationIssue("Refresh repository folders", ex);
            if (generation == _folderRefreshGeneration && !cancellation.IsCancellationRequested)
                StatusTextBlock.Text = T("LocalLibrary.RepositoryScanFailedTheExistingIndex");
        }
        finally
        {
            if (ReferenceEquals(_folderRefreshCancellation, cancellation)) _folderRefreshCancellation = null;
        }
    }

    private void PopulateSecondLevelList(FirstLevelFolderItem? firstItem, bool preserveStatus = false)
    {
        using var timing = MeasureRepositoryUi(RepositoryPerformanceOperation.Populate, firstItem?.Children.Count ?? 0);
        _repositorySelectedMod = null;
        ClearPreview();
        CurrentFolderTextBlock.Text = StateNotSelectedText;
        CurrentStateTextBlock.Text = StateNotSelectedText;
        SetStateColor(CurrentStateTextBlock.Text);
        _currentSecondLevelPath = null;
        LoadDefaultShortcutTemplate();
        LoadLinkForCurrentMod(null);

        if (firstItem is null)
        {
            _secondLevelItems.ReplaceAll([]);
            ApplySecondLevelSelectionState(null, preserveStatus: true);
            RefreshRepositoryModView();
            return;
        }

        _secondLevelItems.ReplaceAll(firstItem.Children);
        RefreshRepositoryModView();

        if (!preserveStatus)
        {
            StatusTextBlock.Text = T("LocalLibrary.CurrentFirstLevelFolder") + firstItem.Path;
        }

        if (_secondLevelItems.Count > 0)
        {
            SecondLevelListView.SelectedIndex = 0;
            ApplySecondLevelSelectionState(_secondLevelItems[0], preserveStatus: true);
        }
    }

    private void ShowSecondLevelDetails(SecondLevelFolderItem? item, bool preserveStatus = false)
    {
        if (item is null)
        {
            CurrentFolderTextBlock.Text = StateNotSelectedText;
            CurrentStateTextBlock.Text = StateNotSelectedText;
            SetStateColor(CurrentStateTextBlock.Text);
            ClearPreview();
            LoadLinkForCurrentMod(null);
            return;
        }

        CurrentFolderTextBlock.Text = item.Name;
        CurrentStateTextBlock.Text = item.State;
        SetStateColor(item.State);
        UpdatePreviewForDirectory(item.Path, item.Files);

        if (!preserveStatus)
        {
            StatusTextBlock.Text = T("LocalLibrary.Viewing") + item.Path;
        }
    }

    private void ApplySecondLevelSelectionState(SecondLevelFolderItem? item, bool preserveStatus = false)
    {
        _currentSecondLevelPath = item?.Path;
        ShowSecondLevelDetails(item, preserveStatus);
        LoadBindingsForCurrentMod(item);
        LoadLinkForCurrentMod(item);
        RefreshPersistentSlotControls();
        SyncRepositorySelectionV4(item);
    }

    private void SetStateColor(string? state)
    {
        if (string.Equals(state, StateCopiedText, StringComparison.CurrentCultureIgnoreCase)
            || string.Equals(state, StateLinkedText, StringComparison.CurrentCultureIgnoreCase))
        {
            CurrentStateTextBlock.Foreground = CopiedBrush;
        }
        else if (string.Equals(state, StateMissingText, StringComparison.CurrentCultureIgnoreCase))
        {
            CurrentStateTextBlock.Foreground = MissingBrush;
        }
        else
        {
            CurrentStateTextBlock.Foreground = _isDarkTheme ? new SolidColorBrush(Colors.WhiteSmoke) : NeutralBrush;
        }
    }

    private async Task ToggleDirectoryCopyAsync(SecondLevelFolderItem item)
    {
        string targetDir = (TargetTextBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(targetDir) || !Directory.Exists(targetDir))
        {
            await ShowMessageAsync(
                T("LocalLibrary.ChooseAValidTargetFolderFirst"),
                T("LocalLibrary.TargetFolderNotSet"));
            return;
        }

        string targetPath = GetTargetDirectoryPath(targetDir, item.Path);
        WorkspaceRepository? repository = GetSelectedRepository();
        if (repository is null)
        {
            await ShowMessageAsync(T("Common.SelectARepositoryFirst"), T("LocalLibrary.CannotContinue"));
            return;
        }
        repository.TargetPath = targetDir;

        if (!await OfferBrokenLinkCleanupAsync(targetDir))
        {
            return;
        }

        bool targetExists = Directory.Exists(targetPath);
        IReadOnlyList<string> siblingLinksToReplace = Array.Empty<string>();
        try
        {
            if (!targetExists && repository.UseDirectoryLinks)
            {
                siblingLinksToReplace = await Task.Run(() =>
                    DirectoryLinkDeployment.FindSiblingLinksToReplace(item.Path, repository.SourcePath, targetDir));
            }

            if (!targetExists && _enableConflictDetection)
            {
                string[] ignoredTargets = [targetPath, .. siblingLinksToReplace];
                List<string> conflicts = await Task.Run(() => DetectModFileConflicts(item.Path, targetDir, ignoredTargets));
                (string? Candidate, ModIniConflictSummary Summary)[] iniConflicts = (await Task.Run(() =>
                        DetectModIniConflictsSafely(item.Path, targetDir, ignoredTargets)))
                    .Select(summary => ((string?)null, summary)).ToArray();
                if ((conflicts.Count > 0 || iniConflicts.Length > 0) && !await ConfirmInstallConflictsAsync(conflicts, iniConflicts))
                {
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            LogApplicationIssue("Mod deployment planning", ex);
            await ShowMessageAsync(
                T("LocalLibrary.CouldNotInspectTheSelectedMod") + ex.Message,
                T("LocalLibrary.CannotContinue"));
            return;
        }

        string? transactionPath = null;
        SetBusyState(true);
        try
        {
            transactionPath = await CreateInstallTransactionAsync(
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
                await Task.Run(() => DeleteDirectoryTreeSafely(targetPath));
                UpdateProgress(100, T("LocalLibrary.RemovalComplete"));
                StatusTextBlock.Text = item.Name + T("LocalLibrary.WasRemovedFromTheTargetFolder");
                ShowAppNotification($"{item.Name} 已从目标文件夹移除。", $"{item.Name} was removed from the target folder.");
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
                string replacedNames = string.Join("、", siblingLinksToReplace.Select(Path.GetFileName));
                StatusTextBlock.Text = repository.UseDirectoryLinks
                    ? siblingLinksToReplace.Count > 0
                        ? T("LocalLibrary.DisconnectedAndLinked", replacedNames, item.Name)
                        : item.Name + T("LocalLibrary.WasLinkedIntoTheTargetFolder")
                    : item.Name + T("LocalLibrary.WasCopiedToTheTargetFolder");
                ShowAppNotification(
                    repository.UseDirectoryLinks
                        ? siblingLinksToReplace.Count > 0
                            ? $"已切换同角色链接：{replacedNames} → {item.Name}"
                            : $"{item.Name} 已链接到目标文件夹。"
                        : $"{item.Name} 已复制到目标文件夹。",
                    repository.UseDirectoryLinks
                        ? siblingLinksToReplace.Count > 0
                            ? $"Switched the same-character link: {replacedNames} → {item.Name}"
                            : $"{item.Name} was linked into the target folder."
                        : $"{item.Name} was copied to the target folder.");
            }

            CommitInstallTransaction(transactionPath);
        }
        catch (Exception ex)
        {
            if (!string.IsNullOrWhiteSpace(transactionPath))
            {
                try
                {
                    await RollbackInstallTransactionAsync(transactionPath, clearLastTransaction: true);
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
            string importedPath = await Task.Run(() => ImportArchiveContents(archivePath, item.Path));

            string importedName = Path.GetFileName(importedPath);
            StatusTextBlock.Text = T("LocalLibrary.InstalledAs", Path.GetFileName(archivePath), item.Name, importedName, importedPath);
            ShowAppNotification($"压缩包已安装到 {item.Name}\\{importedName}。", $"The archive was installed to {item.Name}\\{importedName}.");
            await RefreshListsAsync();
            SelectFirstLevelByPath(item.Path);
            SelectSecondLevelByPath(importedPath);
            await TryImportShortcutBindingsFromModFilesAsync(importedPath);
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
            ShowAppNotification($"{item.Name} 的预览图已更新。", $"The preview image for {item.Name} was updated.");
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
            (List<string> directories, List<string> files) = InspectDirectoryTreeSafely(sourcePath);
            int totalSteps = directories.Count + files.Count + 1;
            int currentStep = 0;

            Directory.CreateDirectory(targetPath);
            currentStep++;
            progress.Report(new ProgressInfo(currentStep * 100 / totalSteps, T("LocalLibrary.CreatingFolder")));

            foreach (string directory in directories)
            {
                string relativePath = Path.GetRelativePath(sourcePath, directory);
                Directory.CreateDirectory(GetSafePathInsideDirectory(targetPath, relativePath));
                currentStep++;
                progress.Report(new ProgressInfo(currentStep * 100 / totalSteps, T("LocalLibrary.CreatingSubfolders")));
            }

            foreach (string file in files)
            {
                string relativePath = Path.GetRelativePath(sourcePath, file);
                string destinationFile = GetSafePathInsideDirectory(targetPath, relativePath);
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

    private async Task DeployModAsync(
        string sourcePath,
        string targetPath,
        WorkspaceRepository repository,
        IProgress<ProgressInfo> progress)
    {
        if (!repository.UseDirectoryLinks)
        {
            await CopyDirectoryWithProgressAsync(sourcePath, targetPath, progress);
            return;
        }

        progress.Report(new ProgressInfo(15, T("LocalLibrary.ValidatingRepositoryPath")));
        string fullSourcePath = Path.GetFullPath(sourcePath);
        if (!IsPathInsideDirectory(fullSourcePath, repository.SourcePath))
        {
            throw new InvalidOperationException(T("LocalLibrary.RefusedToLinkADirectoryOutside"));
        }

        await Task.Run(() => DirectoryLinkDeployment.CreateJunction(targetPath, fullSourcePath));
        progress.Report(new ProgressInfo(100, T("LocalLibrary.DirectoryJunctionCreated")));
    }

    private void UpdateProgress(int percent, string message)
    {
        CopyProgressBar.Value = percent;
        ProgressTextBlock.Text = message;
    }

    private void SetBusyState(bool busy)
    {
        ThemeToggleButton.IsEnabled = !busy;
        LanguageToggleButton.IsEnabled = !busy;
        LinkDeploymentToggleSwitch.IsEnabled = !busy;
        FirstLevelListView.IsEnabled = !busy;
        SecondLevelListView.IsEnabled = !busy;
        OpenModLinkButton.IsEnabled = !busy && !string.IsNullOrWhiteSpace(ModLinkTextBox.Text);
        SetRepositoryWorkspaceBusyV4(busy);
    }

    private void SelectSecondLevelByPath(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        for (int firstIndex = 0; firstIndex < _firstLevelItems.Count; firstIndex++)
        {
            FirstLevelFolderItem first = _firstLevelItems[firstIndex];
            if (first.Children.Any(child => string.Equals(child.Path, path, StringComparison.CurrentCultureIgnoreCase)))
            {
                FirstLevelListView.SelectedIndex = firstIndex;
                for (int secondIndex = 0; secondIndex < _secondLevelItems.Count; secondIndex++)
                {
                    if (string.Equals(_secondLevelItems[secondIndex].Path, path, StringComparison.CurrentCultureIgnoreCase))
                    {
                        SecondLevelFolderItem selected = _secondLevelItems[secondIndex];
                        SecondLevelListView.SelectedItem = selected;
                        if (_repositoryVisibleMods.Contains(selected)) SecondLevelListView.ScrollIntoView(selected);
                        ApplySecondLevelSelectionState(_secondLevelItems[secondIndex], preserveStatus: true);
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

    private static string GetTargetDirectoryPath(string targetDir, string sourceDirectoryPath)
    {
        return Path.Combine(targetDir, Path.GetFileName(sourceDirectoryPath));
    }

    private string GetFolderCopyState(string targetDir, string directoryPath)
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

    private SecondLevelFolderItem? GetSelectedSecondLevelItem()
    {
        return _repositorySelectedMod ?? SecondLevelListView.SelectedItem as SecondLevelFolderItem;
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
                LogApplicationIssue("Mod preview selection", ex);
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
            ReportUiActionFailure("UpdatePreviewForDirectory", ex);
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

        BitmapImage bitmap = await LoadLocalBitmapAsync(imagePath);
        if (requestVersion == _previewImageRequestVersion)
        {
            PreviewImage.Source = bitmap;
            if (GetSelectedSecondLevelItem() is { } selected) selected.CoverImageSource = bitmap;
            PreviewHintTextBlock.Visibility = Visibility.Collapsed;
        }
    }

    private void ClearPreview()
    {
        _previewImageRequestVersion++;
        PreviewImage.Source = null;
        PreviewHintTextBlock.Text = T("LocalLibrary.NoPreviewImageWasFoundFor");
        PreviewHintTextBlock.Visibility = Visibility.Visible;
    }

    private static string? FindPreviewImage(List<string> files)
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

    private static bool IsImageFile(string path)
    {
        string extension = Path.GetExtension(path);
        return !string.IsNullOrEmpty(extension) && ImageExtensions.Contains(extension.ToLowerInvariant());
    }

    private void ApplyFirstLevelFilter()
    {
        string keyword = (FirstLevelSearchTextBox.Text ?? string.Empty).Trim();
        string? selectedPath = FirstLevelListView.SelectedItem is FirstLevelFolderItem selected ? selected.Path : null;

        IEnumerable<FirstLevelFolderItem> filtered = string.IsNullOrWhiteSpace(keyword)
            ? _allFirstLevelItems
            : _allFirstLevelItems.Where(item =>
                item.Name.Contains(keyword, StringComparison.CurrentCultureIgnoreCase));

        _firstLevelItems.ReplaceAll(filtered);

        if (!string.IsNullOrEmpty(selectedPath))
        {
            SelectFirstLevelByPath(selectedPath);
        }

        if (FirstLevelListView.SelectedItem is null && _firstLevelItems.Count > 0)
        {
            FirstLevelListView.SelectedIndex = 0;
        }
    }

    private void SelectFirstLevelByPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        FirstLevelFolderItem? item = _firstLevelItems.FirstOrDefault(first =>
            string.Equals(first.Path, path, StringComparison.CurrentCultureIgnoreCase));
        if (item is not null)
        {
            FirstLevelListView.SelectedItem = item;
        }
    }
}
