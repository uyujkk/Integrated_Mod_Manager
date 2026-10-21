using IntegratedModManager.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private CancellationTokenSource? _existingModImportCancellation;
    private bool _existingModImportBusy;
    private string ExistingImportRecordsRoot => Path.Combine(AppContext.BaseDirectory, "backups", "existing-mod-imports");

    private void InitializeExistingModImportCommands(StackPanel host)
    {
        var menu = new MenuFlyout();
        var import = new MenuFlyoutItem();
        var undo = new MenuFlyoutItem();
        var recover = new MenuFlyoutItem();
        import.Click += async (_, _) => await RunUiActionAsync("ShowExistingModImportAsync", () => ShowExistingModImportAsync());
        undo.Click += async (_, _) => await RunUiActionAsync("UndoExistingModImportAsync", () => UndoExistingModImportAsync());
        recover.Click += async (_, _) => await RunUiActionAsync("RecoverExistingModImportAsync", () => RecoverExistingModImportAsync());
        menu.Items.Add(import); menu.Items.Add(recover); menu.Items.Add(undo);
        var button = new Button { Content = new FontIcon { Glyph = "\uE8B5", FontSize = 16 }, MinHeight = 34, Flyout = menu };
        ToolTipService.SetToolTip(button, L("整理导入现有 Mod", "Organize existing mods"));
        AutomationProperties.SetName(button, L("整理导入现有 Mod", "Organize existing mods"));
        menu.Opening += (_, _) =>
        {
            import.Text = L("预览并复制导入旧 Mod", "Preview and copy existing mods");
            undo.Text = L("撤销上次整理导入", "Undo last organized import");
            recover.Text = L("检查中断的整理导入", "Check interrupted import");
            import.IsEnabled = !_existingModImportBusy && !_repositoryWorkspaceBusy;
            undo.IsEnabled = import.IsEnabled && !string.IsNullOrWhiteSpace(_onlinePreferences.LastImportManifest)
                && File.Exists(_onlinePreferences.LastImportManifest);
            recover.IsEnabled = import.IsEnabled && HasExistingImportRecords();
        };
        host.Children.Add(button);
    }

    private async Task ShowExistingModImportAsync()
    {
        if (_existingModImportBusy) return;
        WorkspaceRepository? repository = GetSelectedRepository();
        if (repository is null || !Directory.Exists(repository.SourcePath)) return;
        OnlineDownloadContext context = CaptureOnlineDownloadContext(repository);
        string[] characters = Directory.GetDirectories(context.RepositoryPath).OrderBy(Path.GetFileName).ToArray();
        if (characters.Length == 0)
        {
            await ShowMessageAsync(L("请先在仓库中创建一个角色文件夹，再选择导入位置。", "Create a character folder in this repository before importing."), L("缺少目标角色", "Choose a target character"));
            return;
        }
        var sourceBox = new TextBox { Header = L("旧 Mod 库目录", "Existing mod library folder"),
            PlaceholderText = L("粘贴目录路径，或点击“选择源目录”", "Paste a folder path, or choose Browse"), HorizontalAlignment = HorizontalAlignment.Stretch };
        var target = new ComboBox { Header = L("复制到角色目录", "Copy into character folder"), HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (string character in characters) target.Items.Add(new ComboBoxItem { Content = Path.GetFileName(character), Tag = character });
        target.SelectedItem = target.Items.Cast<ComboBoxItem>().FirstOrDefault(item => item.Tag as string == (FirstLevelListView.SelectedItem as FirstLevelFolderItem)?.Path)
            ?? target.Items[0];
        var choice = new ContentDialog { Title = L("选择整理位置", "Choose import destination"), Content = new StackPanel { Spacing = 12, MinWidth = 320, Children =
            { new TextBlock { Text = L("源目录的直接子文件夹将作为独立 Mod；只识别包含 INI 的目录。保留源文件，不覆盖同名目标。", "Immediate subfolders containing INI files are treated as mods. Source files stay in place; existing destinations are never overwritten."), TextWrapping = TextWrapping.Wrap, MaxWidth = 500 }, sourceBox, target } },
            PrimaryButtonText = L("生成预览", "Preview"), SecondaryButtonText = L("选择源目录", "Browse source"), IsPrimaryButtonEnabled = false,
            CloseButtonText = L("取消", "Cancel"), XamlRoot = RootGrid.XamlRoot, RequestedTheme = RootGrid.ActualTheme };
        sourceBox.TextChanged += (_, _) => choice.IsPrimaryButtonEnabled = Directory.Exists(sourceBox.Text.Trim());
        while (true)
        {
            ContentDialogResult result = await choice.ShowAsync();
            if (result == ContentDialogResult.Primary) break;
            if (result != ContentDialogResult.Secondary) return;
            string? selectedSource = await PickFolderAsync();
            if (selectedSource is not null) sourceBox.Text = selectedSource;
        }
        string source = sourceBox.Text.Trim();
        string destination = (string)((ComboBoxItem)target.SelectedItem).Tag;
        _existingModImportBusy = true;
        using var cancellation = new CancellationTokenSource();
        _existingModImportCancellation = cancellation;
        try
        {
            ExistingModImportPlan plan = await Task.Run(() => ExistingModImportService.Preview(source, destination, cancellation.Token));
            if (plan.Items.Length == 0)
            {
                await ShowMessageAsync(L("未找到包含 INI 的直接子目录。请选择旧 Mod 库的上一级目录。", "No immediate subfolders containing INI files were found. Select the parent folder of your old mods."), L("没有可导入项目", "No import candidates"));
                return;
            }
            var previewBody = new StackPanel { Spacing = 8 };
            long bytes = plan.Items.Sum(item => item.Snapshot.Files.Sum(file => file.Length));
            previewBody.Children.Add(new TextBlock { Text = L($"{plan.Items.Length} 个 Mod · {FormatFileSize(bytes)}\n目标：{destination}", $"{plan.Items.Length} mods · {FormatFileSize(bytes)}\nDestination: {destination}"), TextWrapping = TextWrapping.Wrap });
            foreach (ExistingModImportItem item in plan.Items)
                previewBody.Children.Add(new TextBlock { Text = $"{item.SourceName} → {item.DestinationName}" + (item.Conflict is null ? string.Empty : L("（同名冲突，不能执行）", " (conflict; cannot import)")), TextWrapping = TextWrapping.Wrap });
            var preview = new ContentDialog { Title = L("整理导入预览", "Organized import preview"),
                Content = new ScrollViewer { Content = previewBody, MaxHeight = Math.Max(180, RootGrid.ActualHeight - 250), MaxWidth = 620 },
                PrimaryButtonText = L("复制导入", "Copy and import"), IsPrimaryButtonEnabled = plan.Items.All(item => item.Conflict is null),
                CloseButtonText = L("取消", "Cancel"), XamlRoot = RootGrid.XamlRoot, RequestedTheme = RootGrid.ActualTheme };
            if (await preview.ShowAsync() != ContentDialogResult.Primary) return;
            string manifest = Path.Combine(ExistingImportRecordsRoot, Guid.NewGuid().ToString("N") + ".json");
            _onlinePreferences.LastImportManifest = manifest;
            SaveShellConfig();
            var status = new TextBlock { Text = L("正在校验预览并复制文件…", "Validating preview and copying…"), TextWrapping = TextWrapping.Wrap, MaxWidth = 500 };
            var operation = new ContentDialog { Title = L("正在导入", "Importing"), Content = status, CloseButtonText = L("取消任务", "Cancel task"), XamlRoot = RootGrid.XamlRoot, RequestedTheme = RootGrid.ActualTheme };
            operation.CloseButtonClick += (_, args) => { cancellation.Cancel(); args.Cancel = true; status.Text = L("正在取消，请等待当前文件处理结束…", "Canceling; waiting for the current file…"); };
            Task<ContentDialogResult> shown = operation.ShowAsync().AsTask();
            try
            {
                await Task.Run(() => ExistingModImportService.ExecuteAsync(plan, manifest,
                    (done, total) => DispatcherQueue.TryEnqueue(() => status.Text = L($"已复制 {done}/{total} 个 Mod", $"Copied {done}/{total} mods")), cancellation.Token));
            }
            finally { operation.Hide(); await shown; }
            if (GetSelectedRepository()?.Id == context.RepositoryId) await RefreshListsAsync();
            ShowAppNotification("旧 Mod 已复制导入，原目录保持不变。", "Existing mods imported; source folders remain intact.");
        }
        catch (OperationCanceledException)
        {
            if (!_onlineWindowClosed) await ShowMessageAsync(L("导入已取消。已完成的副本保留，可通过“撤销上次整理导入”移除。", "Import canceled. Completed copies remain and can be removed with Undo last organized import."), L("导入已取消", "Import canceled"));
        }
        catch (Exception ex)
        {
            LogApplicationIssue("Organize existing Mods", ex);
            if (!_onlineWindowClosed) await ShowMessageAsync(L("导入未完成：", "Import did not finish: ") + ex.Message
                + L("\n已完成的副本可使用撤销功能处理，源目录保持不变。", "\nUse undo for completed copies. Source folders remain intact."), L("整理导入", "Organized import"));
        }
        finally { _existingModImportCancellation = null; _existingModImportBusy = false; }
    }

    private async Task UndoExistingModImportAsync()
    {
        string? manifest = _onlinePreferences.LastImportManifest;
        WorkspaceRepository? repository = GetSelectedRepository();
        if (_existingModImportBusy || manifest is null || repository is null) return;
        try
        {
            if (!Path.GetFullPath(manifest).StartsWith(Path.GetFullPath(ExistingImportRecordsRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Import record is outside the application records folder.");
            var record = RecoveryJsonFile.Read<ExistingModImportManifest>(manifest);
            if (!IsPathInsideDirectory(record.DestinationRoot, repository.SourcePath))
                throw new InvalidDataException(L("请切回导入时的原仓库再撤销。", "Switch to the original repository before undoing."));
            var dialog = new ContentDialog { Title = L("撤销整理导入", "Undo organized import"),
                Content = new TextBlock { Text = L("仅移除本次复制且内容未改变的 Mod。修改过的副本会保留并报告。源目录不受影响。", "Only unchanged copies from this import are removed. Modified copies are preserved and reported. Source folders are unaffected."), TextWrapping = TextWrapping.Wrap, MaxWidth = 500 },
                PrimaryButtonText = L("撤销导入", "Undo import"), CloseButtonText = L("取消", "Cancel"), DefaultButton = ContentDialogButton.Close,
                XamlRoot = RootGrid.XamlRoot, RequestedTheme = RootGrid.ActualTheme };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            _existingModImportBusy = true;
            ExistingModImportUndoResult result = await Task.Run(() => ExistingModImportService.Undo(manifest, record.DestinationRoot));
            await RefreshListsAsync();
            await ShowMessageAsync(L($"已移除 {result.Removed.Length} 个未修改副本；保留 {result.Preserved.Length} 个已改变或无法访问的副本。", $"Removed {result.Removed.Length} unchanged copies; preserved {result.Preserved.Length} modified or inaccessible copies.")
                + "\n" + string.Join("\n", result.Preserved), L("撤销结果", "Undo result"));
        }
        catch (Exception ex) { await ReportOnlineActionErrorAsync(ex.Message, L("无法撤销导入", "Cannot undo import")); }
        finally { _existingModImportBusy = false; }
    }

    private async Task RecoverExistingModImportAsync()
    {
        WorkspaceRepository? repository = GetSelectedRepository();
        if (_existingModImportBusy || repository is null) return;
        _existingModImportBusy = true;
        try
        {
            var candidates = await Task.Run(() => FindInterruptedImports(repository.SourcePath));
            if (candidates.Length == 0)
            {
                await ShowMessageAsync(L("当前仓库没有待检查的中断导入记录。", "No interrupted imports need review in this repository."), L("导入恢复检查", "Import recovery check"));
                return;
            }
            var records = new ComboBox { Header = L("选择中断记录", "Select an interrupted import"), HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (string candidate in candidates)
                records.Items.Add(new ComboBoxItem { Content = File.GetLastWriteTime(candidate).ToString("yyyy-MM-dd HH:mm:ss") + " · " + Path.GetFileName(candidate), Tag = candidate });
            records.SelectedIndex = 0;
            var dialog = new ContentDialog { Title = L("检查中断的导入", "Review interrupted import"), Content = records,
                PrimaryButtonText = L("核对并补登记", "Check and register"), CloseButtonText = L("取消", "Cancel"),
                XamlRoot = RootGrid.XamlRoot, RequestedTheme = RootGrid.ActualTheme };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            string manifest = (string)((ComboBoxItem)records.SelectedItem).Tag;
            if (!IsPathInsideDirectory(manifest, ExistingImportRecordsRoot)) throw new InvalidDataException("Import record is outside the application records folder.");
            var record = RecoveryJsonFile.Read<ExistingModImportManifest>(manifest);
            if (!IsPathInsideDirectory(record.DestinationRoot, repository.SourcePath))
                throw new InvalidDataException(L("请切回原仓库检查导入。", "Switch to the original repository to check this import."));
            ExistingModImportRecoveryResult result = await Task.Run(() => ExistingModImportService.Recover(manifest, record.DestinationRoot));
            if (result.Recovered.Length > 0) { _onlinePreferences.LastImportManifest = manifest; SaveShellConfig(); }
            await RefreshListsAsync();
            await ShowMessageAsync(L($"补登记 {result.Recovered.Length} 个已完整复制的 Mod；保留 {result.Preserved.Length} 个无法确认或已修改目录；发现 {result.StagingFolders.Length} 个未发布的暂存目录。\n\n补登记的未修改副本可通过撤销处理。暂存目录和无法确认的内容不会自动删除。", $"Registered {result.Recovered.Length} complete copies; preserved {result.Preserved.Length} ambiguous or changed folders; found {result.StagingFolders.Length} unpublished staging folders.\n\nRegistered unchanged copies can be undone. Staging and ambiguous content are never deleted automatically.")
                + "\n" + string.Join("\n", result.Preserved.Concat(result.StagingFolders)), L("导入恢复检查", "Import recovery check"));
        }
        catch (Exception ex) { LogApplicationIssue("Recover organized import", ex); await ReportOnlineActionErrorAsync(ex.Message, L("无法检查导入记录", "Cannot inspect import record")); }
        finally { _existingModImportBusy = false; }
    }

    private string[] FindInterruptedImports(string? repositoryPath = null)
    {
        if (!Directory.Exists(ExistingImportRecordsRoot)) return [];
        var records = new List<string>();
        foreach (string path in Directory.EnumerateFiles(ExistingImportRecordsRoot, "*.json").OrderByDescending(File.GetLastWriteTimeUtc).Take(100))
        {
            try
            {
                var record = RecoveryJsonFile.Read<ExistingModImportManifest>(path);
                if (record.Version == 2 && !record.Complete && record.Pending is { Count: > 0 }
                    && (repositoryPath is null || IsPathInsideDirectory(record.DestinationRoot, repositoryPath))) records.Add(path);
            }
            catch (Exception ex) { LogApplicationIssue("Inspect interrupted import record", ex); }
        }
        return records.ToArray();
    }

    private async Task NotifyInterruptedImportsAsync()
    {
        try
        {
            int count = (await Task.Run(() => FindInterruptedImports())).Length;
            if (count > 0 && !_onlineWindowClosed)
                ShowAppNotification($"发现 {count} 条中断导入记录，请在对应仓库的整理导入菜单检查。", $"Found {count} interrupted import records. Review them from the original repository's import menu.");
        }
        catch (Exception ex) { LogApplicationIssue("Inspect import recovery on startup", ex); }
    }

    private bool HasExistingImportRecords()
    {
        try { return Directory.Exists(ExistingImportRecordsRoot) && Directory.EnumerateFiles(ExistingImportRecordsRoot, "*.json").Any(); }
        catch (Exception ex) { LogApplicationIssue("Find import recovery records", ex); return false; }
    }
}
