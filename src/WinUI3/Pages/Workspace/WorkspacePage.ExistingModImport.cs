using IntegratedModManager.Core.Deployment;
using IntegratedModManager.Core.Infrastructure;
using IntegratedModManager.Core.Online;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

public sealed partial class WorkspacePage
{
    internal CancellationTokenSource? _existingModImportCancellation;
    private bool _existingModImportBusy;
    private string ExistingImportRecordsRoot => Path.Combine(AppContext.BaseDirectory, "backups", "existing-mod-imports");

    private void InitializeExistingModImportCommands(StackPanel host)
    {
        var menu = new MenuFlyout();
        var import = new MenuFlyoutItem();
        var undo = new MenuFlyoutItem();
        var recover = new MenuFlyoutItem();
        import.Click += async (_, _) => await _host.RunUiActionAsync("ShowExistingModImportAsync", () => ShowExistingModImportAsync());
        undo.Click += async (_, _) => await _host.RunUiActionAsync("UndoExistingModImportAsync", () => UndoExistingModImportAsync());
        recover.Click += async (_, _) => await _host.RunUiActionAsync("RecoverExistingModImportAsync", () => RecoverExistingModImportAsync());
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
            undo.IsEnabled = import.IsEnabled && !string.IsNullOrWhiteSpace(_host.OnlineView._onlinePreferences.LastImportManifest)
                && File.Exists(_host.OnlineView._onlinePreferences.LastImportManifest);
            recover.IsEnabled = import.IsEnabled && HasExistingImportRecords();
        };
        host.Children.Add(button);
    }

    private async Task ShowExistingModImportAsync()
    {
        if (_existingModImportBusy) return;
        WorkspaceRepository? repository = _host.GetSelectedRepository();
        if (repository is null || !Directory.Exists(repository.SourcePath)) return;
        OnlineDownloadContext context = OnlinePage.CaptureOnlineDownloadContext(repository);
        string[] characters = Directory.GetDirectories(context.RepositoryPath).OrderBy(Path.GetFileName).ToArray();
        if (characters.Length == 0)
        {
            await ShowMessageAsync(T("ExistingModImport.CreateACharacterFolderInThis"), T("ExistingModImport.ChooseATargetCharacter"));
            return;
        }
        if (await ExistingModImportDialogs.ChooseAsync(_services, characters, _host.Characters.Selected?.Path, () => _host.PickFolderAsync()) is not { } chosen) return;
        (string source, string destination) = chosen;
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
            long bytes = plan.Items.Sum(item => item.Snapshot.Files.Sum(file => file.Length));
            if (!await ExistingModImportDialogs.PreviewAsync(_services, plan, destination, OnlinePage.FormatFileSize(bytes), _host.Root.ActualHeight)) return;
            string manifest = Path.Combine(ExistingImportRecordsRoot, Guid.NewGuid().ToString("N") + ".json");
            _host.OnlineView._onlinePreferences.LastImportManifest = manifest;
            _host.SaveShellConfig();
            (ContentDialog operation, TextBlock status) = ExistingModImportDialogs.Progress(_services, cancellation);
            Task<ContentDialogResult> shown = _services.Dialogs.ShowAsync(operation);
            try
            {
                await Task.Run(() => ExistingModImportService.ExecuteAsync(plan, manifest,
                    (done, total) => DispatcherQueue.TryEnqueue(() => status.Text = T("ExistingModImport.CopiedMods", done, total)), cancellation.Token));
            }
            finally { _services.Dialogs.Close(operation); await shown; }
            if (_host.GetSelectedRepository()?.Id == context.RepositoryId) await RefreshListsAsync();
            ShowAppNotification(T("ExistingModImport.Imported"));
        }
        catch (OperationCanceledException)
        {
            if (!_host.OnlineView._onlineWindowClosed) await ShowMessageAsync(T("ExistingModImport.ImportCanceledCompletedCopiesRemainAnd"), T("ExistingModImport.ImportCanceled"));
        }
        catch (Exception ex)
        {
            _host.LogApplicationIssue("Organize existing Mods", ex);
            if (!_host.OnlineView._onlineWindowClosed) await ShowMessageAsync(T("ExistingModImport.ImportDidNotFinish") + ex.Message
                + T("ExistingModImport.UseUndoForCompletedCopiesSource"), T("ExistingModImport.OrganizedImport"));
        }
        finally { _existingModImportCancellation = null; _existingModImportBusy = false; }
    }

    private async Task UndoExistingModImportAsync()
    {
        string? manifest = _host.OnlineView._onlinePreferences.LastImportManifest;
        WorkspaceRepository? repository = _host.GetSelectedRepository();
        if (_existingModImportBusy || manifest is null || repository is null) return;
        try
        {
            if (!PathSafety.IsStrictlyInside(manifest, ExistingImportRecordsRoot))
                throw new InvalidDataException("Import record is outside the application records folder.");
            var record = RecoveryJsonFile.Read<ExistingModImportManifest>(manifest);
            if (!OnlinePage.IsPathInsideDirectory(record.DestinationRoot, repository.SourcePath))
                throw new InvalidDataException(T("ExistingModImport.SwitchToTheOriginalRepositoryBefore"));
            if (!await ExistingModImportDialogs.ConfirmUndoAsync(_services)) return;
            _existingModImportBusy = true;
            ExistingModImportUndoResult result = await Task.Run(() => ExistingModImportService.Undo(manifest, record.DestinationRoot));
            await RefreshListsAsync();
            await ShowMessageAsync(T("ExistingModImport.RemovedUnchangedCopiesPreservedModifiedOr", result.Removed.Length, result.Preserved.Length)
                + "\n" + string.Join("\n", result.Preserved), T("ExistingModImport.UndoResult"));
        }
        catch (Exception ex) { await _host.OnlineView.ReportOnlineActionErrorAsync(ex.Message, T("ExistingModImport.CannotUndoImport"), ex); }
        finally { _existingModImportBusy = false; }
    }

    private async Task RecoverExistingModImportAsync()
    {
        WorkspaceRepository? repository = _host.GetSelectedRepository();
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
            if (await ExistingModImportDialogs.ChooseInterruptedAsync(_services, candidates) is not { } manifest) return;
            if (!OnlinePage.IsPathInsideDirectory(manifest, ExistingImportRecordsRoot)) throw new InvalidDataException("Import record is outside the application records folder.");
            var record = RecoveryJsonFile.Read<ExistingModImportManifest>(manifest);
            if (!OnlinePage.IsPathInsideDirectory(record.DestinationRoot, repository.SourcePath))
                throw new InvalidDataException(T("ExistingModImport.SwitchToTheOriginalRepositoryTo"));
            ExistingModImportRecoveryResult result = await Task.Run(() => ExistingModImportService.Recover(manifest, record.DestinationRoot));
            if (result.Recovered.Length > 0) { _host.OnlineView._onlinePreferences.LastImportManifest = manifest; _host.SaveShellConfig(); }
            await RefreshListsAsync();
            await ShowMessageAsync(T("ExistingModImport.RegisteredCompleteCopiesPreservedAmbiguousOr", result.Recovered.Length, result.Preserved.Length, result.StagingFolders.Length)
                + "\n" + string.Join("\n", result.Preserved.Concat(result.StagingFolders)), T("ExistingModImport.ImportRecoveryCheck"));
        }
        catch (Exception ex) { _host.LogApplicationIssue("Recover organized import", ex); await _host.OnlineView.ReportOnlineActionErrorAsync(ex.Message, T("ExistingModImport.CannotInspectImportRecord"), ex); }
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
                    && (repositoryPath is null || OnlinePage.IsPathInsideDirectory(record.DestinationRoot, repositoryPath))) records.Add(path);
            }
            catch (Exception ex) { _host.LogApplicationIssue("Inspect interrupted import record", ex); }
        }
        return records.ToArray();
    }

    internal async Task NotifyInterruptedImportsAsync()
    {
        try
        {
            int count = (await Task.Run(() => FindInterruptedImports())).Length;
            if (count > 0 && !_host.OnlineView._onlineWindowClosed)
                ShowAppNotification(T("ExistingModImport.InterruptedFound", count));
        }
        catch (Exception ex) { _host.LogApplicationIssue("Inspect import recovery on startup", ex); }
    }

    private bool HasExistingImportRecords()
    {
        try { return Directory.Exists(ExistingImportRecordsRoot) && Directory.EnumerateFiles(ExistingImportRecordsRoot, "*.json").Any(); }
        catch (Exception ex) { _host.LogApplicationIssue("Find import recovery records", ex); return false; }
    }
}
