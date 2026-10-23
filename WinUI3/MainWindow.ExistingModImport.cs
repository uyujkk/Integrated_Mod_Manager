using IntegratedModManager.Core.Deployment;
using IntegratedModManager.Core.Infrastructure;
using IntegratedModManager.Core.Online;
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
        ToolTipService.SetToolTip(button, T("ExistingModImport.OrganizeExistingMods"));
        AutomationProperties.SetName(button, T("ExistingModImport.OrganizeExistingMods"));
        menu.Opening += (_, _) =>
        {
            import.Text = T("ExistingModImport.PreviewAndCopyExistingMods");
            undo.Text = T("ExistingModImport.UndoLastOrganizedImport");
            recover.Text = T("ExistingModImport.CheckInterruptedImport");
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
            await ShowMessageAsync(T("ExistingModImport.CreateACharacterFolderInThis"), T("ExistingModImport.ChooseATargetCharacter"));
            return;
        }
        var sourceBox = new TextBox { Header = T("ExistingModImport.ExistingModLibraryFolder"),
            PlaceholderText = T("ExistingModImport.PasteAFolderPathOrChoose"), HorizontalAlignment = HorizontalAlignment.Stretch };
        var target = new ComboBox { Header = T("ExistingModImport.CopyIntoCharacterFolder"), HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (string character in characters) target.Items.Add(new ComboBoxItem { Content = Path.GetFileName(character), Tag = character });
        target.SelectedItem = target.Items.Cast<ComboBoxItem>().FirstOrDefault(item => item.Tag as string == (FirstLevelListView.SelectedItem as FirstLevelFolderItem)?.Path)
            ?? target.Items[0];
        var choice = new ContentDialog { Title = T("ExistingModImport.ChooseImportDestination"), Content = new StackPanel { Spacing = 12, MinWidth = 320, Children =
            { new TextBlock { Text = T("ExistingModImport.ImmediateSubfoldersContainingINIFilesAre"), TextWrapping = TextWrapping.Wrap, MaxWidth = 500 }, sourceBox, target } },
            PrimaryButtonText = T("ExistingModImport.Preview"), SecondaryButtonText = T("ExistingModImport.BrowseSource"), IsPrimaryButtonEnabled = false,
            CloseButtonText = T("Common.Cancel"), XamlRoot = RootGrid.XamlRoot, RequestedTheme = RootGrid.ActualTheme };
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
                await ShowMessageAsync(T("ExistingModImport.NoImmediateSubfoldersContainingINIFiles"), T("ExistingModImport.NoImportCandidates"));
                return;
            }
            var previewBody = new StackPanel { Spacing = 8 };
            long bytes = plan.Items.Sum(item => item.Snapshot.Files.Sum(file => file.Length));
            previewBody.Children.Add(new TextBlock { Text = T("ExistingModImport.ModsDestination", plan.Items.Length, FormatFileSize(bytes), destination), TextWrapping = TextWrapping.Wrap });
            foreach (ExistingModImportItem item in plan.Items)
                previewBody.Children.Add(new TextBlock { Text = $"{item.SourceName} → {item.DestinationName}" + (item.Conflict is null ? string.Empty : T("ExistingModImport.ConflictCannotImport")), TextWrapping = TextWrapping.Wrap });
            var preview = new ContentDialog { Title = T("ExistingModImport.OrganizedImportPreview"),
                Content = new ScrollViewer { Content = previewBody, MaxHeight = Math.Max(180, RootGrid.ActualHeight - 250), MaxWidth = 620 },
                PrimaryButtonText = T("ExistingModImport.CopyAndImport"), IsPrimaryButtonEnabled = plan.Items.All(item => item.Conflict is null),
                CloseButtonText = T("Common.Cancel"), XamlRoot = RootGrid.XamlRoot, RequestedTheme = RootGrid.ActualTheme };
            if (await preview.ShowAsync() != ContentDialogResult.Primary) return;
            string manifest = Path.Combine(ExistingImportRecordsRoot, Guid.NewGuid().ToString("N") + ".json");
            _onlinePreferences.LastImportManifest = manifest;
            SaveShellConfig();
            var status = new TextBlock { Text = T("ExistingModImport.ValidatingPreviewAndCopying"), TextWrapping = TextWrapping.Wrap, MaxWidth = 500 };
            var operation = new ContentDialog { Title = T("ExistingModImport.Importing"), Content = status, CloseButtonText = T("ExistingModImport.CancelTask"), XamlRoot = RootGrid.XamlRoot, RequestedTheme = RootGrid.ActualTheme };
            operation.CloseButtonClick += (_, args) => { cancellation.Cancel(); args.Cancel = true; status.Text = T("ExistingModImport.CancelingWaitingForTheCurrentFile"); };
            Task<ContentDialogResult> shown = operation.ShowAsync().AsTask();
            try
            {
                await Task.Run(() => ExistingModImportService.ExecuteAsync(plan, manifest,
                    (done, total) => DispatcherQueue.TryEnqueue(() => status.Text = T("ExistingModImport.CopiedMods", done, total)), cancellation.Token));
            }
            finally { operation.Hide(); await shown; }
            if (GetSelectedRepository()?.Id == context.RepositoryId) await RefreshListsAsync();
            ShowAppNotification("旧 Mod 已复制导入，原目录保持不变。", "Existing mods imported; source folders remain intact.");
        }
        catch (OperationCanceledException)
        {
            if (!_onlineWindowClosed) await ShowMessageAsync(T("ExistingModImport.ImportCanceledCompletedCopiesRemainAnd"), T("ExistingModImport.ImportCanceled"));
        }
        catch (Exception ex)
        {
            LogApplicationIssue("Organize existing Mods", ex);
            if (!_onlineWindowClosed) await ShowMessageAsync(T("ExistingModImport.ImportDidNotFinish") + ex.Message
                + T("ExistingModImport.UseUndoForCompletedCopiesSource"), T("ExistingModImport.OrganizedImport"));
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
            if (!PathSafety.IsStrictlyInside(manifest, ExistingImportRecordsRoot))
                throw new InvalidDataException("Import record is outside the application records folder.");
            var record = RecoveryJsonFile.Read<ExistingModImportManifest>(manifest);
            if (!IsPathInsideDirectory(record.DestinationRoot, repository.SourcePath))
                throw new InvalidDataException(T("ExistingModImport.SwitchToTheOriginalRepositoryBefore"));
            var dialog = new ContentDialog { Title = T("ExistingModImport.UndoOrganizedImport"),
                Content = new TextBlock { Text = T("ExistingModImport.OnlyUnchangedCopiesFromThisImport"), TextWrapping = TextWrapping.Wrap, MaxWidth = 500 },
                PrimaryButtonText = T("ExistingModImport.UndoImport"), CloseButtonText = T("Common.Cancel"), DefaultButton = ContentDialogButton.Close,
                XamlRoot = RootGrid.XamlRoot, RequestedTheme = RootGrid.ActualTheme };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            _existingModImportBusy = true;
            ExistingModImportUndoResult result = await Task.Run(() => ExistingModImportService.Undo(manifest, record.DestinationRoot));
            await RefreshListsAsync();
            await ShowMessageAsync(T("ExistingModImport.RemovedUnchangedCopiesPreservedModifiedOr", result.Removed.Length, result.Preserved.Length)
                + "\n" + string.Join("\n", result.Preserved), T("ExistingModImport.UndoResult"));
        }
        catch (Exception ex) { await ReportOnlineActionErrorAsync(ex.Message, T("ExistingModImport.CannotUndoImport")); }
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
                await ShowMessageAsync(T("ExistingModImport.NoInterruptedImportsNeedReviewIn"), T("ExistingModImport.ImportRecoveryCheck"));
                return;
            }
            var records = new ComboBox { Header = T("ExistingModImport.SelectAnInterruptedImport"), HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (string candidate in candidates)
                records.Items.Add(new ComboBoxItem { Content = File.GetLastWriteTime(candidate).ToString("yyyy-MM-dd HH:mm:ss") + " · " + Path.GetFileName(candidate), Tag = candidate });
            records.SelectedIndex = 0;
            var dialog = new ContentDialog { Title = T("ExistingModImport.ReviewInterruptedImport"), Content = records,
                PrimaryButtonText = T("ExistingModImport.CheckAndRegister"), CloseButtonText = T("Common.Cancel"),
                XamlRoot = RootGrid.XamlRoot, RequestedTheme = RootGrid.ActualTheme };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            string manifest = (string)((ComboBoxItem)records.SelectedItem).Tag;
            if (!IsPathInsideDirectory(manifest, ExistingImportRecordsRoot)) throw new InvalidDataException("Import record is outside the application records folder.");
            var record = RecoveryJsonFile.Read<ExistingModImportManifest>(manifest);
            if (!IsPathInsideDirectory(record.DestinationRoot, repository.SourcePath))
                throw new InvalidDataException(T("ExistingModImport.SwitchToTheOriginalRepositoryTo"));
            ExistingModImportRecoveryResult result = await Task.Run(() => ExistingModImportService.Recover(manifest, record.DestinationRoot));
            if (result.Recovered.Length > 0) { _onlinePreferences.LastImportManifest = manifest; SaveShellConfig(); }
            await RefreshListsAsync();
            await ShowMessageAsync(T("ExistingModImport.RegisteredCompleteCopiesPreservedAmbiguousOr", result.Recovered.Length, result.Preserved.Length, result.StagingFolders.Length)
                + "\n" + string.Join("\n", result.Preserved.Concat(result.StagingFolders)), T("ExistingModImport.ImportRecoveryCheck"));
        }
        catch (Exception ex) { LogApplicationIssue("Recover organized import", ex); await ReportOnlineActionErrorAsync(ex.Message, T("ExistingModImport.CannotInspectImportRecord")); }
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
