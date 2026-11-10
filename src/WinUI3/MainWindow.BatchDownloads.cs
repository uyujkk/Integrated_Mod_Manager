using IntegratedModManager.Core.Deployment;
using IntegratedModManager.Core.Online;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private sealed record PreparedSavedDownload(OnlineModCard Mod, IReadOnlyCollection<string> Aliases, string? Error);

    private OnlineDuplicateKind GetOnlineDuplicateKind(OnlineModCard mod, OnlineDownloadCandidate? file, OnlineDownloadContext context)
        => OnlineDuplicatePolicy.Classify(file?.FileId, _trackedModOrigins.Values.Where(origin =>
            OnlineInstallationDetectionPolicy.Matches(mod.ItemId, mod.ProfileUrl, origin.ItemId, origin.ProfileUrl)
            && Directory.Exists(origin.Path) && IsPathInsideDirectory(origin.Path, context.RepositoryPath)).Select(origin => origin.DownloadFileId));

    private async Task PrepareSavedBatchAsync(OnlineSavedMod[] saved, WorkspaceRepository repository)
    {
        if (_onlineDownloadActions.IsPreparing || saved.Length == 0) return;
        if (!_services.Downloads.JournalAvailable)
        { await ShowMessageAsync(T("BatchDownloads.TheDownloadRecordIsUnreadableAnd"), T("BatchDownloads.CannotDownloadBatch")); return; }
        var context = CaptureOnlineDownloadContext(repository);
        string contextKey = GetOnlineDetailContextKey();
        bool endfield = IsEndfieldRepository(repository);
        if (!Directory.Exists(context.RepositoryPath))
        { await ShowMessageAsync(T("BatchDownloads.TheRepositoryFolderIsUnavailableCorrect"), T("BatchDownloads.CannotDownloadBatch")); return; }
        using var cancellation = new CancellationTokenSource();
        (ContentDialog progressDialog, TextBlock progress) = BatchDownloadDialogs.Preparing(_services);
        bool prepared = false;
        progressDialog.Closing += (_, _) => { if (!prepared) cancellation.Cancel(); };
        var showing = _services.Dialogs.ShowAsync(progressDialog);
        var rows = new List<PreparedSavedDownload>();
        try
        {
            foreach (var item in saved.DistinctBy(item => GetOnlineDownloadIdentity(item.Mod)))
            {
                cancellation.Token.ThrowIfCancellationRequested();
                progress.Text = T("BatchDownloads.Reading", rows.Count + 1, saved.Length, item.Mod.Title);
                try
                {
                    if (item.RepositoryId != context.RepositoryId || !string.Equals(item.RepositoryPath.TrimEnd('\\', '/'), context.RepositoryPath.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException(T("BatchDownloads.TheSavedRepositoryPathHasChanged"));
                    if (item.Mod.ItemId <= 0)
                        throw new InvalidDataException(T("BatchDownloads.TheSiteEntryIDIsMissing"));
                    var mod = await RefreshOnlineModDownloadInfoAsync(item.Mod, endfield, cancellation.Token, requireDownloadInfo: true);
                    rows.Add(new(mod, GetCharacterFolderAliases(mod).ToArray(), null));
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { rows.Add(new(item.Mod, [], ex.Message)); }
            }
            prepared = true;
        }
        catch (OperationCanceledException) { }
        finally { _services.Dialogs.Close(progressDialog); await showing; }
        if (!prepared || cancellation.IsCancellationRequested || _onlineWindowClosed || contextKey != GetOnlineDetailContextKey()) return;

        string[] folders = Directory.GetDirectories(context.RepositoryPath).Order(StringComparer.CurrentCultureIgnoreCase).Prepend(context.RepositoryPath).ToArray();
        var reviewRows = new List<BatchDownloadDialogs.Row>();
        foreach (var data in rows)
        {
            var candidates = OnlineDownloadSelectionPolicy.OrderForManualSelection(data.Mod.DownloadFiles).Where(candidate => candidate.IsSupportedArchive).ToArray();
            if (candidates.Length == 0 && data.Mod.DownloadFiles.Count == 0 && IsSupportedArchiveFile(data.Mod.DownloadFileName) && !string.IsNullOrWhiteSpace(data.Mod.DownloadUrl))
                candidates = [new OnlineDownloadCandidate { FileId = data.Mod.DownloadFileId, FileName = data.Mod.DownloadFileName, DownloadUrl = data.Mod.DownloadUrl, IsSupportedArchive = true }];
            var files = candidates.Select(candidate => (candidate.FileName + (candidate.IsArchived ? T("BatchDownloads.Archived") : ""), candidate)).ToArray();
            bool active = _onlineDownloadActions.IsActive(context, GetOnlineDownloadIdentity(data.Mod));
            bool enabled = data.Error is null && candidates.Length > 0 && !active;
            bool isChecked = data.Error is null && candidates.Length > 0 && !active;
            var options = folders.Select(path => (path == context.RepositoryPath ? T("BatchDownloads.RepositoryRoot") : Path.GetFileName(path), (object)path)).ToList();
            // Issue #6: reuse a matching character folder, or offer to create one; nothing is created until approval.
            CharacterFolderPlan? folderPlan = data.Error is null ? PlanCharacterFolder(data.Mod, context.RepositoryPath, data.Aliases) : null;
            string? preferred = folderPlan?.Action == CharacterFolderAction.UseExisting ? folderPlan.Path : null;
            int createIndex = -1;
            if (folderPlan?.Action == CharacterFolderAction.Create)
            {
                options.Insert(1, (T("BatchDownloads.New", Path.GetFileName(folderPlan.Path)), new NewCharacterFolderChoice(folderPlan.Path!)));
                createIndex = 1;
            }
            string folderKey = DownloadFolderKey(context, data.Mod);
            bool usesRemembered = false;
            if (_onlinePreferences.RememberDownloadFolders && _onlinePreferences.DownloadFolders.TryGetValue(folderKey, out var remembered) && IsUsableRememberedFolder(remembered, context))
            {
                preferred = remembered;
                usesRemembered = true;
                if (!folders.Contains(remembered, StringComparer.OrdinalIgnoreCase)) options.Add((remembered, remembered));
            }
            int folderIndex = options.FindIndex(option => string.Equals(option.Item2 as string, preferred, StringComparison.OrdinalIgnoreCase));
            if (folderIndex < 0) folderIndex = usesRemembered || createIndex < 0 ? 0 : createIndex;
            string? note = null;
            if (folderPlan is { Action: not CharacterFolderAction.UseExisting })
            {
                string character = GetOnlineCharacterDisplayName(data.Mod.CharacterName);
                note = folderPlan.Action == CharacterFolderAction.Create
                    ? T("BatchDownloads.CreatedAfterApproval", folderPlan.Path)
                    : CharacterFolderReasonText(folderPlan, character);
            }
            reviewRows.Add(new BatchDownloadDialogs.Row(data, data.Mod.Title, data.Mod.Title + (active ? T("BatchDownloads.TaskActive") : ""), data.Error,
                enabled, isChecked, files, options, folderIndex, note));
        }
        if (await BatchDownloadDialogs.ReviewAsync(_services, context.RepositoryName, context.RepositoryPath, reviewRows,
                (key, file) => GetOnlineDuplicateKind(((PreparedSavedDownload)key).Mod, file, context), GetAppThemeBrush("AppNavSelectedBorderBrush"),
                RootGrid.ActualWidth, RootGrid.ActualHeight) is not { } review
            || _onlineWindowClosed || contextKey != GetOnlineDetailContextKey()) return;
        var chosen = review.Rows.Select(plan => (Data: (PreparedSavedDownload)plan.Key, plan.File, Choice: plan.Folder)).ToArray();
        if (chosen.Any(plan => plan.Choice is string folder && !IsUsableRememberedFolder(folder, context)))
        { await ShowMessageAsync(T("BatchDownloads.ADestinationIsNoLongerAvailable"), T("BatchDownloads.DestinationChanged")); return; }
        if (!review.AllowDuplicates && chosen.Any(plan => GetOnlineDuplicateKind(plan.Data.Mod, plan.File, context) != OnlineDuplicateKind.None)) return;
        // Only approved rows create folders; Mods of the same character share one creation.
        var created = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var choice in chosen.Select(plan => plan.Choice).OfType<NewCharacterFolderChoice>().DistinctBy(choice => choice.Path, StringComparer.OrdinalIgnoreCase))
                created[choice.Path] = CharacterFolderPlanner.CreatePlannedFolder(context.RepositoryPath, choice.Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ShowMessageAsync(T("BatchDownloads.CouldNotCreateACharacterFolder") + ex.Message, T("BatchDownloads.DestinationChanged"));
            return;
        }
        var approved = chosen.Select(plan => (plan.Data, plan.File,
            Folder: plan.Choice is NewCharacterFolderChoice choice ? created[choice.Path] : (string)plan.Choice)).ToArray();
        UiCrashDiagnostics.RecordAction("Batch download confirmed");
        var tasks = new List<Task>();
        foreach (var plan in approved)
        {
            if (_onlineWindowClosed || contextKey != GetOnlineDetailContextKey()) break;
            tasks.Add(DownloadAndExtractOnlineModAsync(plan.Data.Mod, plan.File, refreshDownloadInfo: false,
                capturedContext: context, capturedEndfieldMetadata: endfield, capturedCharacterAliases: plan.Data.Aliases,
                confirmedDestination: plan.Folder, batchConfirmed: true));
        }
        await Task.WhenAll(tasks);
    }
}
