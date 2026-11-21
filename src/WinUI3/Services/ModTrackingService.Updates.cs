using System.Text.Json;
using IntegratedModManager.Core.Archives;
using IntegratedModManager.Core.Configuration;
using IntegratedModManager.Core.Online;
using IntegratedModManager.Core.Updates;

namespace ModFolderCopier.WinUI;

// Checking the tracked Mods for updates and replacing one with its newer version (stage J finish, step 5a: moved from
// the window unchanged). The window keeps when to check (the interval) and how the Updates page shows the results.
internal sealed partial class ModTrackingService
{
    private AppServices _services = null!;
    private Action<string, Exception> _log = (_, _) => { };
    private Action<string, Exception> _logWithStack = (_, _) => { };
    private Func<bool> _saveConfig = () => true;
    private Action<bool> _saveShellConfig = _ => { };
    private Action<UiMessage> _setStatus = _ => { };
    private Action _refreshUpdatesPane = () => { };
    private Action _openUpdates = () => { };
    private Func<Task> _refreshWorkspaceListsAsync = () => Task.CompletedTask;
    private Dictionary<string, string> _modLinks = [];

    /// <summary>
    /// Called once by the window: saving config.ini and beta-shell.json, the Updates page's status line and refresh,
    /// opening the Updates page, refreshing the workspace lists, the Mods' links and the error log.
    /// </summary>
    public void Attach(AppServices services, Action<string, Exception> log, Action<string, Exception> logWithStack, Func<bool> saveConfig,
        Action<bool> saveShellConfig, Action<UiMessage> setStatus, Action refreshUpdatesPane, Action openUpdates,
        Func<Task> refreshWorkspaceListsAsync, Dictionary<string, string> modLinks)
    {
        _services = services;
        _log = log;
        _logWithStack = logWithStack;
        _saveConfig = saveConfig;
        _saveShellConfig = saveShellConfig;
        _setStatus = setStatus;
        _refreshUpdatesPane = refreshUpdatesPane;
        _openUpdates = openUpdates;
        _refreshWorkspaceListsAsync = refreshWorkspaceListsAsync;
        _modLinks = modLinks;
    }

    /// <summary>A check is running.</summary>
    public bool IsChecking { get; private set; }

    /// <summary>When the tracked Mods were last checked; saved in beta-shell.json.</summary>
    public DateTimeOffset? LastCheckUtc { get; set; }

    private string T(string key, params object?[] values) => _services.Language.T(key, values);

    private static UiMessage M(string key, params object?[] values) => LanguageService.M(key, values);

    private void ShowAppNotification(string message) => _services.Status.Notify(message);

    private void SaveShellConfig(bool throwOnError = false) => _saveShellConfig(throwOnError);

    private Task ShowMessageAsync(string content, string title) => _services.Dialogs.MessageAsync(title, content);

    /// <summary>The tracked Mods being replaced right now, by folder.</summary>
    public HashSet<string> InProgress { get; } = new(StringComparer.OrdinalIgnoreCase);
    private bool _showingModUpdateNotification;

    public async Task NotifyModUpdatesAvailableAsync()
    {
        if (_showingModUpdateNotification || _services.IsClosing) return;
        var updates = _services.Tracking.Results.Where(item => item.HasUpdate).ToArray();
        if (updates.Length == 0) return;
        _showingModUpdateNotification = true;
        try
        {
            if (await ModUpdatesAvailableDialog.ShowAsync(_services, updates.Select(item => item.Title).ToArray()))
            {
                _openUpdates();
            }
        }
        finally { _showingModUpdateNotification = false; }
    }

    public async Task DownloadAndReplaceTrackedModAsync(TrackedModUpdateResult result)
    {
        if (!InProgress.Add(result.Path)) return;
        DownloadTaskItem? task = null;
        bool committed = false;
        string? work = null;
        try
        {
            WorkspaceRepository? repository = _services.Repositories.Selected;
            if (repository is null || !_services.Tracking.Origins.TryGetValue(result.Path, out TrackedModOrigin? origin)
                || origin.ItemId != result.ItemId) throw new IOException(T("ModUpdates.TheTrackedModChangedCheckFor"));
            string repositoryId = repository.Id;
            string repositoryPath = repository.SourcePath;
            ModUpdateSnapshot snapshot = await Task.Run(() => ModUpdateReplacement.Capture(repositoryPath, result.Path));
            OnlineModCard mod = await _services.OnlineDownloads.FetchGameBananaModCardAsyncV2(result.ItemId)
                ?? throw new IOException(T("ModUpdates.CouldNotLoadCurrentDownloadFiles"));
            var candidates = mod.DownloadFiles.Where(file => file.IsSupportedArchive && !file.IsArchived && !string.IsNullOrWhiteSpace(file.DownloadUrl)).ToArray();
            if (candidates.Length == 0) throw new IOException(T("ModUpdates.NoSupportedArchivesAreAvailableReview"));
            if (await ModUpdateReplaceDialog.ChooseAsync(_services, result.Path, candidates, origin.DownloadFileId, OnlineDownloadService.FormatFileSize) is not { } selected) return;
            OnlineModCard effective = OnlineDownloadService.WithSelectedDownloadFile(mod, selected);
            task = _services.Downloads.CreateTask(effective, result.Path);
            // Recovery must never replay a replacement as an ordinary separate installation.
            task.SourceIdentity = "mod-update|" + task.SourceIdentity;
            _refreshUpdatesPane();
            work = Path.Combine(AppContext.BaseDirectory, "cache", "mod-updates", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            using IDisposable execution = await _services.Downloads.Scheduler.AcquireAsync(task.Cancellation.Token);
            string archive = _services.OnlineDownloads.EnsureDownloadArchiveExtension(await _services.OnlineDownloads.DownloadOnlineModArchiveAsync(effective, work, task, task.Cancellation.Token));
            if (!_services.Install.IsSupportedArchiveFile(archive)) throw new InvalidDataException(T("ModUpdates.TheDownloadedFileIsNotA"));
            string payload = Path.Combine(work, "payload");
            Directory.CreateDirectory(payload);
            _services.Downloads.UpdateTask(task, DownloadTaskState.Extracting, 92, M("ModUpdates.Extracting"), forceRefresh: true);
            await Task.Run(() =>
            {
                _services.Install.ExtractArchiveToDirectory(archive, payload);
                ArchiveImportLayout.HoistSingleTopFolder(payload);
            });
            task.Cancellation.Token.ThrowIfCancellationRequested();
            // Never redirect a captured update to whichever repository is now visible.
            if (!_services.Repositories.Items.Any(item => item.Id == repositoryId && string.Equals(item.SourcePath, repositoryPath, StringComparison.OrdinalIgnoreCase))
                || !_services.Tracking.Origins.TryGetValue(result.Path, out var current) || !ReferenceEquals(current, origin))
                throw new IOException(T("ModUpdates.TheOriginalRepositoryOrTrackingRecord"));
            string backupRoot = Path.Combine(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryPath)))!, ".imm-mod-update-backups");
            task.ReplacementCommitting = true;
            _services.Downloads.UpdateTask(task, DownloadTaskState.Extracting, 98, M("ModUpdates.BackingUp"), forceRefresh: true);
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
            _setStatus(keptImages > 0
                ? M("ModUpdates.UpdatedWithBackupAndImages", origin.Title, keptImages)
                : M("ModUpdates.UpdatedWithBackup", origin.Title));
            _services.Downloads.UpdateTask(task, DownloadTaskState.Completed, 100, M("ModUpdates.ReplacedBackup", replaced.BackupPath), forceRefresh: true);
            if (!_saveConfig()) throw new IOException(T("ModUpdates.UpdatedFilesWereReplacedButTracking"));
            SaveShellConfig(throwOnError: true);
            if (_services.Repositories.Selected?.Id == repositoryId) await _refreshWorkspaceListsAsync();
            ShowAppNotification(T("ModUpdates.Updated", origin.Title));
        }
        catch (OperationCanceledException) when (!committed)
        {
            if (task is not null) _services.Downloads.UpdateTask(task, DownloadTaskState.Canceled, task.Progress, M("ModUpdates.Canceled"), forceRefresh: true);
        }
        catch (Exception ex)
        {
            _logWithStack("Tracked Mod update", ex);
            if (task is not null && !committed) _services.Downloads.UpdateTask(task, DownloadTaskState.Failed, task.Progress, M("ModUpdates.Failed", ex.Message), forceRefresh: true);
            if (!_services.IsClosing) await ShowMessageAsync(
                (committed ? T("ModUpdates.ReplacedButSaveFailed") : T("ModUpdates.StoppedOriginalsKept")) + ex.Message,
                T("ModUpdates.ModUpdate"));
        }
        finally
        {
            InProgress.Remove(result.Path);
            // Retain failed downloads for diagnosis; successful work is an owned cache folder.
            if (committed && work is not null) { try { Directory.Delete(work, true); } catch (Exception ex) { _log("Clean Mod update cache", ex); } }
            if (!_services.IsClosing) _refreshUpdatesPane();
        }
    }

    public async Task CheckTrackedModUpdatesAsync(bool showDialogs)
    {
        if (IsChecking)
        {
            return;
        }

        if (_services.Tracking.Origins.Count == 0)
        {
            _setStatus(M("Updates.NoTrackedOnlineMods"));
            _refreshUpdatesPane();
            if (showDialogs)
            {
                await ShowMessageAsync(
                    T("Updates.DownloadAndExtractAtLeastOne"),
                    T("Updates.NoTrackedMods"));
            }
            return;
        }

        bool deduplicatedTrackedMods = _services.Tracking.Deduplicate();
        if (deduplicatedTrackedMods)
        {
            _saveConfig();
            SaveShellConfig();
        }

        try
        {
            IsChecking = true;
            _services.Updates.CheckError = null;
            _setStatus(M("Updates.CheckingTrackedMods", _services.Tracking.Origins.Count));
            _refreshUpdatesPane();

            _services.Tracking.Results.Clear();
            foreach (TrackedModOrigin origin in _services.Tracking.Origins.Values.OrderBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase).ToArray())
            {
                TrackedModUpdateResult result = await FetchTrackedModUpdateResultAsync(origin);
                _services.Tracking.Results.Add(result);

                if (!string.IsNullOrWhiteSpace(result.PreviewUrl))
                {
                    origin.PreviewUrl = result.PreviewUrl;
                }

                if (!string.IsNullOrWhiteSpace(result.Title))
                {
                    origin.Title = result.Title;
                }

                if (!string.IsNullOrWhiteSpace(result.ProfileUrl))
                {
                    origin.ProfileUrl = result.ProfileUrl;
                    _modLinks[origin.Path] = result.ProfileUrl;
                }
            }

            LastCheckUtc = DateTimeOffset.UtcNow;
            _services.Updates.HasChecked = true;
            _saveConfig();
            SaveShellConfig();

            int updateCount = _services.Tracking.Results.Count(item => item.HasUpdate);
            int failedCount = _services.Tracking.Results.Count(item => item.CheckFailed);
            _setStatus(
                failedCount > 0 ? M("Updates.CheckCompleteWithFailures", updateCount, failedCount)
                : updateCount > 0 ? M("Updates.CheckCompleteWithUpdates", updateCount)
                : M("Updates.CheckCompleteAllCurrent"));

            // The check is over; do not keep showing "Checking..." behind the result dialog.
            IsChecking = false;
            _refreshUpdatesPane();

            if (updateCount > 0)
            {
                await NotifyModUpdatesAvailableAsync();
            }
            else if (showDialogs)
            {
                await ShowMessageAsync(
                    updateCount > 0
                        ? T("Updates.CheckedModsAndFoundUpdates", _services.Tracking.Results.Count, updateCount)
                        : failedCount > 0 ? T("Updates.ModChecksFailedPleaseRetry", failedCount)
                        : T("Updates.CheckedModsAndFoundNoNew", _services.Tracking.Results.Count),
                    T("Updates.UpdateCheckComplete"));
            }
        }
        catch (Exception ex)
        {
            _setStatus(M("Updates.TrackedCheckFailed"));
            _services.Updates.CheckError = T("Updates.FailedToCheckTrackedModUpdates") + ex.Message;
            _refreshUpdatesPane();
            if (showDialogs)
            {
                await ShowMessageAsync(
                    T("Updates.FailedToCheckTrackedModUpdates") + ex.Message,
                    T("Updates.UpdateCheckFailed"));
            }
        }
        finally
        {
            IsChecking = false;
            _refreshUpdatesPane();
        }
    }

    private async Task<TrackedModUpdateResult> FetchTrackedModUpdateResultAsync(TrackedModOrigin origin)
    {
        OnlineModCard? latest = null;
        try
        {
            latest = await _services.OnlineDownloads.FetchGameBananaModCardAsyncV2(origin.ItemId);
        }
        catch (Exception ex)
        {
            _log("Check tracked Mod update", ex);
        }

        if (latest is null)
            return new TrackedModUpdateResult
            {
                Path = origin.Path, ItemId = origin.ItemId, Title = origin.Title,
                ProfileUrl = origin.ProfileUrl, PreviewUrl = origin.PreviewUrl,
                LastKnownUpdatedAt = origin.LastKnownUpdatedAt, CheckFailed = true,
                Status = M("Updates.CheckFailedLatestUnavailable")
            };

        DateTimeOffset? latestUpdatedAt = latest?.UpdatedAt;
        DateTimeOffset? recordedUpdatedAt = origin.LastKnownUpdatedAt;
        bool hasUpdate = latestUpdatedAt.HasValue
            && recordedUpdatedAt.HasValue
            && latestUpdatedAt.Value > recordedUpdatedAt.Value.AddMinutes(1);

        if (!recordedUpdatedAt.HasValue && latestUpdatedAt.HasValue)
        {
            origin.LastKnownUpdatedAt = latestUpdatedAt;
        }

        return new TrackedModUpdateResult
        {
            Path = origin.Path,
            ItemId = origin.ItemId,
            Title = string.IsNullOrWhiteSpace(latest?.Title) ? origin.Title : latest.Title,
            ProfileUrl = string.IsNullOrWhiteSpace(latest?.ProfileUrl) ? origin.ProfileUrl : latest.ProfileUrl,
            PreviewUrl = string.IsNullOrWhiteSpace(latest?.PreviewUrl) ? origin.PreviewUrl : latest.PreviewUrl,
            LastKnownUpdatedAt = recordedUpdatedAt ?? origin.LastKnownUpdatedAt,
            LatestUpdatedAt = latestUpdatedAt ?? recordedUpdatedAt ?? origin.LastKnownUpdatedAt,
            HasUpdate = hasUpdate,
            Status = M(hasUpdate ? "Updates.NewerVersionFound" : "Updates.NoUpdateDetected")
        };
    }
}
