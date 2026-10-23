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
            await ShowImageViewerAsync(PreviewImage.Source, L("Mod 预览图", "Mod Preview"));
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

    private void OnOpenSourceClicked(object sender, RoutedEventArgs e) => OpenDirectory(SourceTextBox.Text, L("主文件夹", "source folder"));

    private void OnOpenTargetClicked(object sender, RoutedEventArgs e) => OpenDirectory(TargetTextBox.Text, L("目标文件夹", "target folder"));

    private void OnOpenLauncherClicked(object sender, RoutedEventArgs e) => OpenFileLocation(LauncherTextBox.Text, L("启动器", "launcher"));

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
                    L("请先选择一个第一层分类。", "Select a first-level category first."),
                    L("未选择分类", "No category selected"));
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
                L("无法打开压缩包选择器：", "Could not open the archive picker: ") + ex.Message,
                L("导入 Mod 失败", "Import Mod Failed"));
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
                    L("请先选择一个第二层 Mod。", "Select a second-level mod first."),
                    L("未选择 Mod", "No mod selected"));
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
            Text = L("打开文件夹", "Open folder"),
            Icon = new FontIcon { Glyph = "\uE838" }
        };
        openItem.Click += (_, _) => OpenDirectory(item.Path, L("第一层文件夹", "First-level folder"));
        menu.Items.Add(openItem);

        var renameItem = new MenuFlyoutItem
        {
            Text = L("重命名", "Rename"),
            Icon = new FontIcon { Glyph = "\uE70F" }
        };
        renameItem.Click += OnRenameFirstLevelClicked;
        menu.Items.Add(renameItem);
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
                    L("请先设置有效的 Mod 仓库文件夹。", "Set a valid mod storage folder first."),
                    L("路径无效", "Invalid path"));
                return;
            }

            string? name = await PromptForTextAsync(
                L("请输入新的第一层文件夹名称。", "Enter the new first-level folder name."),
                L("新建第一层文件夹", "Create first-level folder"),
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
                        L("同名第一层文件夹已存在。", "A first-level folder with the same name already exists."),
                        L("创建失败", "Create failed"));
                    return;
                }

                Directory.CreateDirectory(targetPath);
                await RefreshListsAsync();
                SelectFirstLevelByPath(targetPath);
                StatusTextBlock.Text = L($"已创建第一层文件夹：{name.Trim()}", $"Created first-level folder: {name.Trim()}");
            }
            catch (Exception ex)
            {
                await ShowMessageAsync(
                    L("创建第一层文件夹失败：", "Failed to create the first-level folder: ") + ex.Message,
                    L("创建失败", "Create failed"));
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
                    L("请先选择一个第一层文件夹。", "Select a first-level folder first."),
                    L("未选择文件夹", "No folder selected"));
                return;
            }

            string? name = await PromptForTextAsync(
                L("请输入新的第一层文件夹名称。", "Enter the new first-level folder name."),
                L("重命名第一层文件夹", "Rename first-level folder"),
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
                    throw new InvalidOperationException(L("无法确定父目录。", "Could not determine the parent directory."));
                }

                string newPath = Path.Combine(parentDir, trimmed);
                if (Directory.Exists(newPath))
                {
                    await ShowMessageAsync(
                        L("目标名称已存在，请换一个名称。", "The target name already exists. Choose a different name."),
                        L("重命名失败", "Rename failed"));
                    return;
                }

                var tagLibrary = SelectedLocalLibrary();
                string oldRoleRelative = WorkspaceIdentity.Relative(GetSelectedRepository()!.SourcePath, item.Path);
                Directory.Move(item.Path, newPath);
                tagLibrary?.RenameCharacter(oldRoleRelative, WorkspaceIdentity.Relative(GetSelectedRepository()!.SourcePath, newPath));
                SaveShellConfig();
                await RefreshListsAsync();
                SelectFirstLevelByPath(newPath);
                StatusTextBlock.Text = L($"已重命名第一层文件夹为：{trimmed}", $"Renamed first-level folder to: {trimmed}");
            }
            catch (Exception ex)
            {
                await ShowMessageAsync(
                    L("重命名第一层文件夹失败：", "Failed to rename the first-level folder: ") + ex.Message,
                    L("重命名失败", "Rename failed"));
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
                    StatusTextBlock.Text = L(
                        "已从 Mod 的 INI 配置自动读取快捷键，可在右侧继续修正。",
                        "Shortcuts were detected from the Mod INI files and can be adjusted on the right.");
                }
            }
            catch (Exception ex)
            {
                LogApplicationIssue("Mod selection", ex);
                ClearPreview();
                StatusTextBlock.Text = L(
                    $"无法读取所选 Mod：{ex.Message}",
                    $"Could not read the selected mod: {ex.Message}");
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
            Text = isInstalled ? L("从目标文件夹移除", "Remove from target") : L("部署到目标文件夹", "Deploy to target"),
            Icon = new FontIcon { Glyph = isInstalled ? "\uE74D" : "\uE8B0" }
        };
        toggleItem.Click += async (_, _) => await RunUiActionAsync("ToggleDirectoryCopyAsync", () => ToggleDirectoryCopyAsync(item));
        menu.Items.Add(toggleItem);

        var openItem = new MenuFlyoutItem
        {
            Text = L("打开 Mod 文件夹", "Open mod folder"),
            Icon = new FontIcon { Glyph = "\uE838" }
        };
        openItem.Click += (_, _) => OpenDirectory(item.Path, L("Mod 文件夹", "Mod folder"));
        menu.Items.Add(openItem);
        menu.Items.Add(new MenuFlyoutSeparator());

        var tagsItem = new MenuFlyoutItem { Text = L("编辑标签…", "Edit tags…") };
        tagsItem.Click += async (_, _) => await RunUiActionAsync("EditLocalTagsAsync", () => EditLocalTagsAsync(item));
        menu.Items.Add(tagsItem);
        var deleteItem = new MenuFlyoutItem
        {
            Text = L("删除 Mod...", "Delete mod..."),
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
                    L("请先选择一个第二层 Mod。", "Select a second-level mod first."),
                    L("未选择 Mod", "No mod selected"));
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
                : L($"\n\n加载目录中指向它的 {deployedLinks.Count} 个联接也会一并移除（只移除联接本身）：\n{string.Join("\n", deployedLinks)}",
                    $"\n\n{deployedLinks.Count} link(s) in the loader folder that point to it will also be removed (only the links themselves):\n{string.Join("\n", deployedLinks)}");
            bool confirmed = await ShowConfirmAsync(
                L($"确定要删除这个第二层 Mod 吗？\n\n{item.Name}\n\n此操作会删除原始文件夹，无法撤销。",
                  $"Are you sure you want to delete this second-level mod?\n\n{item.Name}\n\nThis will delete the original folder and cannot be undone.") + linkNote,
                L("确认删除", "Confirm delete"));
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
                StatusTextBlock.Text = L($"已删除第二层 Mod：{item.Name}", $"Deleted second-level mod: {item.Name}");
            }
            catch (Exception ex)
            {
                await ShowMessageAsync(
                    L("删除第二层 Mod 失败：", "Failed to delete the second-level mod: ") + ex.Message,
                    L("删除失败", "Delete failed"));
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
                    L("请先选择一个第二层 Mod，再把图片拖到预览区。", "Select a second-level mod before dropping an image."),
                    L("未选择 Mod", "No mod selected"));
                return;
            }

            if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                await ShowMessageAsync(
                    L("只支持从资源管理器拖入图片文件。", "Only image files dragged from File Explorer are supported."),
                    L("不支持的拖放内容", "Unsupported drop content"));
                return;
            }

            IReadOnlyList<IStorageItem> droppedItems = await e.DataView.GetStorageItemsAsync();
            StorageFile? imageFile = droppedItems.OfType<StorageFile>().FirstOrDefault(file => IsImageFile(file.Path));
            if (imageFile is null)
            {
                await ShowMessageAsync(
                    L("没有检测到可用的图片文件。", "No supported image file was detected."),
                    L("未找到图片", "No image found"));
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
                    L("请先选择一个第一层分类，再把压缩包拖到这里。", "Select a first-level category before dropping an archive here."),
                    L("未选择分类", "No category selected"));
                return;
            }

            if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                await ShowMessageAsync(
                    L("这里只支持从资源管理器拖入压缩包文件。", "Only archive files dragged from File Explorer are supported here."),
                    L("不支持的拖放内容", "Unsupported drop content"));
                return;
            }

            IReadOnlyList<IStorageItem> droppedItems = await e.DataView.GetStorageItemsAsync();
            StorageFile? archiveFile = droppedItems
                .OfType<StorageFile>()
                .FirstOrDefault(file => IsSupportedArchiveFile(file.Path));

            if (archiveFile is null)
            {
                await ShowMessageAsync(
                    L("没有检测到受支持的压缩包文件。", "No supported archive file was detected."),
                    L("未找到压缩包", "No archive found"));
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
        UpdateProgress(0, L("当前无复制任务", "No active copy task"));

        string sourceDir = (SourceTextBox.Text ?? string.Empty).Trim();
        string targetDir = (TargetTextBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(sourceDir) || !Directory.Exists(sourceDir))
        {
            StatusTextBlock.Text = L("请先选择有效的 Mod 仓库文件夹。", "Choose a valid mod storage folder first.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(targetDir) && !Directory.Exists(targetDir))
        {
            StatusTextBlock.Text = L("目标文件夹不存在，请重新选择。", "The target folder does not exist. Please choose it again.");
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
            FirstCountTextBlock.Text = _allFirstLevelItems.Count.ToString();
            SecondCountTextBlock.Text = loaded.SecondCount.ToString();
            StatusTextBlock.Text = loaded.Warnings == 0
                ? L($"已加载 {_allFirstLevelItems.Count} 个第一层目录，{loaded.SecondCount} 个第二层目录。",
                    $"Loaded {_allFirstLevelItems.Count} first-level folders and {loaded.SecondCount} second-level folders.")
                : L($"已加载 {_allFirstLevelItems.Count} 个分类、{loaded.SecondCount} 个 Mod；扫描或缓存有 {loaded.Warnings} 项问题，请查看诊断日志。",
                    $"Loaded {_allFirstLevelItems.Count} categories and {loaded.SecondCount} mods; {loaded.Warnings} scan/cache issue(s). See diagnostics.");
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
                StatusTextBlock.Text = L("仓库扫描失败，未覆盖已有索引。请检查路径或访问权限后重试。",
                    "Repository scan failed; the existing index was preserved. Check the path/permissions and retry.");
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
            StatusTextBlock.Text = L("当前第一层目录：", "Current first-level folder: ") + firstItem.Path;
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
            StatusTextBlock.Text = L("当前查看：", "Viewing: ") + item.Path;
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
                L("请先选择有效的目标文件夹。", "Choose a valid target folder first."),
                L("未设置目标文件夹", "Target folder not set"));
            return;
        }

        string targetPath = GetTargetDirectoryPath(targetDir, item.Path);
        WorkspaceRepository? repository = GetSelectedRepository();
        if (repository is null)
        {
            await ShowMessageAsync(L("请先选择仓库。", "Select a repository first."), L("无法操作", "Cannot Continue"));
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
                L("无法检查所选 Mod 的目录或链接：", "Could not inspect the selected mod directory or links: ") + ex.Message,
                L("无法操作", "Cannot Continue"));
            return;
        }

        string? transactionPath = null;
        SetBusyState(true);
        try
        {
            transactionPath = await CreateInstallTransactionAsync(
                targetExists
                    ? L($"移除 Mod：{item.Name}", $"Remove mod: {item.Name}")
                    : siblingLinksToReplace.Count > 0
                        ? L($"切换同角色 Mod：{item.Name}", $"Switch same-character mod: {item.Name}")
                    : L($"安装 Mod：{item.Name}", $"Install mod: {item.Name}"),
                repository,
                [targetPath, .. siblingLinksToReplace]);

            if (targetExists)
            {
                UpdateProgress(15, L("正在移除目录...", "Removing folder..."));
                await Task.Run(() => DeleteDirectoryTreeSafely(targetPath));
                UpdateProgress(100, L("移除完成", "Removal complete"));
                StatusTextBlock.Text = item.Name + L(" 已从目标文件夹移除。", " was removed from the target folder.");
                ShowAppNotification($"{item.Name} 已从目标文件夹移除。", $"{item.Name} was removed from the target folder.");
            }
            else
            {
                if (siblingLinksToReplace.Count > 0)
                {
                    UpdateProgress(10, L("正在断开同角色的旧 Mod 链接...", "Disconnecting the previous link for this character..."));
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
                        ? L($"已断开 {replacedNames}，并链接 {item.Name}。", $"Disconnected {replacedNames} and linked {item.Name}.")
                        : item.Name + L(" 已链接到目标文件夹。", " was linked into the target folder.")
                    : item.Name + L(" 已复制到目标文件夹。", " was copied to the target folder.");
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
                L("操作失败，已尝试恢复操作前状态：", "Operation failed. The app attempted to restore the previous state: ") + ex.Message,
                L("错误", "Error"));
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
            StatusTextBlock.Text = L($"已将 {Path.GetFileName(archivePath)} 安装为 {item.Name}\\{importedName}：{importedPath}",
                $"Installed {Path.GetFileName(archivePath)} as {item.Name}\\{importedName}: {importedPath}");
            ShowAppNotification($"压缩包已安装到 {item.Name}\\{importedName}。", $"The archive was installed to {item.Name}\\{importedName}.");
            await RefreshListsAsync();
            SelectFirstLevelByPath(item.Path);
            SelectSecondLevelByPath(importedPath);
            await TryImportShortcutBindingsFromModFilesAsync(importedPath);
        }
        catch (Exception ex)
        {
            await ShowMessageAsync(
                L("导入压缩文件失败：", "Archive import failed: ") + ex.Message,
                L("导入失败", "Import failed"));
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
                throw new InvalidOperationException(L("拖入的文件不是受支持的图片格式。", "The dropped file is not a supported image format."));
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

            StatusTextBlock.Text = item.Name + L(" 的预览图已更新。", "'s preview image was updated.");
            ShowAppNotification($"{item.Name} 的预览图已更新。", $"The preview image for {item.Name} was updated.");
            await RefreshListsAsync();
            SelectSecondLevelByPath(item.Path);
        }
        catch (Exception ex)
        {
            await ShowMessageAsync(
                L("导入预览图失败：", "Preview import failed: ") + ex.Message,
                L("导入失败", "Import failed"));
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
            progress.Report(new ProgressInfo(currentStep * 100 / totalSteps, L("正在创建目录...", "Creating folder...")));

            foreach (string directory in directories)
            {
                string relativePath = Path.GetRelativePath(sourcePath, directory);
                Directory.CreateDirectory(GetSafePathInsideDirectory(targetPath, relativePath));
                currentStep++;
                progress.Report(new ProgressInfo(currentStep * 100 / totalSteps, L("正在创建子目录...", "Creating subfolders...")));
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
                progress.Report(new ProgressInfo(currentStep * 100 / totalSteps, L("正在复制文件...", "Copying files...")));
            }

            progress.Report(new ProgressInfo(100, L("复制完成", "Copy complete")));
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

        progress.Report(new ProgressInfo(15, L("正在验证仓库路径...", "Validating repository path...")));
        string fullSourcePath = Path.GetFullPath(sourcePath);
        if (!IsPathInsideDirectory(fullSourcePath, repository.SourcePath))
        {
            throw new InvalidOperationException(L("拒绝链接仓库之外的目录。", "Refused to link a directory outside the repository."));
        }

        await Task.Run(() => DirectoryLinkDeployment.CreateJunction(targetPath, fullSourcePath));
        progress.Report(new ProgressInfo(100, L("目录联接创建完成", "Directory junction created")));
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
                    PreviewHintTextBlock.Text = L("图片无法读取或格式不受支持。", "The image could not be loaded or is not supported.");
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
                PreviewHintTextBlock.Text = L("当前第二层文件夹未找到可预览图片。", "No preview image was found for the current second-level folder.");
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
        PreviewHintTextBlock.Text = L("当前第二层文件夹未找到可预览图片。", "No preview image was found for the current second-level folder.");
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
