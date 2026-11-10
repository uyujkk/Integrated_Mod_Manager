using System.Text.Json;
using IntegratedModManager.Core.Archives;
using IntegratedModManager.Core.Configuration;
using IntegratedModManager.Core.Online;
using IntegratedModManager.Core.Updates;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private readonly HashSet<string> _modUpdatesInProgress = new(StringComparer.OrdinalIgnoreCase);
    private bool _showingModUpdateNotification;

    private async Task NotifyModUpdatesAvailableAsync()
    {
        if (_showingModUpdateNotification || _onlineView._onlineWindowClosed) return;
        var updates = _trackedModUpdateResults.Where(item => item.HasUpdate).ToArray();
        if (updates.Length == 0) return;
        _showingModUpdateNotification = true;
        try
        {
            if (await ModUpdatesAvailableDialog.ShowAsync(_services, updates.Select(item => item.Title).ToArray()))
            {
                _currentPrimarySection = PrimarySection.Updates;
                ApplyShellState(refreshRepository: false);
            }
        }
        finally { _showingModUpdateNotification = false; }
    }

    private async Task DownloadAndReplaceTrackedModAsync(TrackedModUpdateResult result)
    {
        if (!_modUpdatesInProgress.Add(result.Path)) return;
        DownloadTaskItem? task = null;
        bool committed = false;
        string? work = null;
        try
        {
            WorkspaceRepository? repository = GetSelectedRepository();
            if (repository is null || !_trackedModOrigins.TryGetValue(result.Path, out TrackedModOrigin? origin)
                || origin.ItemId != result.ItemId) throw new IOException(T("ModUpdates.TheTrackedModChangedCheckFor"));
            string repositoryId = repository.Id;
            string repositoryPath = repository.SourcePath;
            ModUpdateSnapshot snapshot = await Task.Run(() => ModUpdateReplacement.Capture(repositoryPath, result.Path));
            OnlineModCard mod = await _onlineView.FetchGameBananaModCardAsyncV2(result.ItemId)
                ?? throw new IOException(T("ModUpdates.CouldNotLoadCurrentDownloadFiles"));
            var candidates = mod.DownloadFiles.Where(file => file.IsSupportedArchive && !file.IsArchived && !string.IsNullOrWhiteSpace(file.DownloadUrl)).ToArray();
            if (candidates.Length == 0) throw new IOException(T("ModUpdates.NoSupportedArchivesAreAvailableReview"));
            if (await ModUpdateReplaceDialog.ChooseAsync(_services, result.Path, candidates, origin.DownloadFileId, OnlinePage.FormatFileSize) is not { } selected) return;
            OnlineModCard effective = OnlinePage.WithSelectedDownloadFile(mod, selected);
            task = CreateDownloadTask(effective, result.Path);
            // Recovery must never replay a replacement as an ordinary separate installation.
            task.SourceIdentity = "mod-update|" + task.SourceIdentity;
            RefreshUpdatesPane();
            work = Path.Combine(AppContext.BaseDirectory, "cache", "mod-updates", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            using IDisposable execution = await _services.Downloads.Scheduler.AcquireAsync(task.Cancellation.Token);
            string archive = _onlineView.EnsureDownloadArchiveExtension(await _onlineView.DownloadOnlineModArchiveAsync(effective, work, task, task.Cancellation.Token));
            if (!IsSupportedArchiveFile(archive)) throw new InvalidDataException(T("ModUpdates.TheDownloadedFileIsNotA"));
            string payload = Path.Combine(work, "payload");
            Directory.CreateDirectory(payload);
            UpdateDownloadTask(task, DownloadTaskState.Extracting, 92, M("ModUpdates.Extracting"), forceRefresh: true);
            await Task.Run(() =>
            {
                ExtractArchiveToDirectory(archive, payload);
                ArchiveImportLayout.HoistSingleTopFolder(payload);
            });
            task.Cancellation.Token.ThrowIfCancellationRequested();
            // Never redirect a captured update to whichever repository is now visible.
            if (!_services.Repositories.Items.Any(item => item.Id == repositoryId && string.Equals(item.SourcePath, repositoryPath, StringComparison.OrdinalIgnoreCase))
                || !_trackedModOrigins.TryGetValue(result.Path, out var current) || !ReferenceEquals(current, origin))
                throw new IOException(T("ModUpdates.TheOriginalRepositoryOrTrackingRecord"));
            string backupRoot = Path.Combine(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryPath)))!, ".imm-mod-update-backups");
            task.ReplacementCommitting = true;
            UpdateDownloadTask(task, DownloadTaskState.Extracting, 98, M("ModUpdates.BackingUp"), forceRefresh: true);
            ModUpdateReplacementResult replaced = await Task.Run(() => ModUpdateReplacement.Replace(snapshot, payload, backupRoot,
                JsonSerializer.Serialize(origin), task.Cancellation.Token));
            committed = true;
            origin.LastKnownUpdatedAt = mod.UpdatedAt;
            origin.DownloadFileId = selected.FileId;
            origin.DownloadFileName = selected.FileName;
            origin.Title = mod.Title;
            origin.ProfileUrl = mod.ProfileUrl;
            origin.PreviewUrl = mod.PreviewUrl;
            _modLinks[result.Path] = origin.ProfileUrl;
            result.HasUpdate = false;
            result.LastKnownUpdatedAt = origin.LastKnownUpdatedAt;
            result.Status = M("ModUpdates.Replaced");
            result.BackupPath = replaced.BackupPath;
            int keptImages = replaced.PreservedImages.Count;
            SetModUpdateStatus(keptImages > 0
                ? M("ModUpdates.UpdatedWithBackupAndImages", origin.Title, keptImages)
                : M("ModUpdates.UpdatedWithBackup", origin.Title));
            UpdateDownloadTask(task, DownloadTaskState.Completed, 100, M("ModUpdates.ReplacedBackup", replaced.BackupPath), forceRefresh: true);
            if (!SaveConfig()) throw new IOException(T("ModUpdates.UpdatedFilesWereReplacedButTracking"));
            SaveShellConfig(throwOnError: true);
            if (GetSelectedRepository()?.Id == repositoryId) await RefreshListsAsync();
            ShowAppNotification(T("ModUpdates.Updated", origin.Title));
        }
        catch (OperationCanceledException) when (!committed)
        {
            if (task is not null) UpdateDownloadTask(task, DownloadTaskState.Canceled, task.Progress, M("ModUpdates.Canceled"), forceRefresh: true);
        }
        catch (Exception ex)
        {
            LogApplicationIssueWithStack("Tracked Mod update", ex);
            if (task is not null && !committed) UpdateDownloadTask(task, DownloadTaskState.Failed, task.Progress, M("ModUpdates.Failed", ex.Message), forceRefresh: true);
            if (!_onlineView._onlineWindowClosed) await ShowMessageAsync(
                (committed ? T("ModUpdates.ReplacedButSaveFailed") : T("ModUpdates.StoppedOriginalsKept")) + ex.Message,
                T("ModUpdates.ModUpdate"));
        }
        finally
        {
            _modUpdatesInProgress.Remove(result.Path);
            // Retain failed downloads for diagnosis; successful work is an owned cache folder.
            if (committed && work is not null) { try { Directory.Delete(work, true); } catch (Exception ex) { LogApplicationIssue("Clean Mod update cache", ex); } }
            if (!_onlineView._onlineWindowClosed) RefreshUpdatesPane();
        }
    }
}
