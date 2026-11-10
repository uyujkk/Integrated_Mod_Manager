using System.Text;
using IntegratedModManager.Core.Archives;
using IntegratedModManager.Core.Configuration;
using IntegratedModManager.Core.Deployment;
using IntegratedModManager.Core.Infrastructure;
using IntegratedModManager.Core.Online;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.System;

namespace ModFolderCopier.WinUI;

public sealed partial class OnlinePage
{
    private async Task<OnlineModCard> RefreshOnlineModDownloadInfoAsync(OnlineModCard mod, bool? useEndfieldMetadata = null,
        CancellationToken cancellationToken = default, bool requireDownloadInfo = false)
    {
        try
        {
            OnlineModCard? latestMod = await FetchGameBananaModCardAsyncV2(
                mod.ItemId,
                useEndfieldMetadata: useEndfieldMetadata ?? IsEndfieldRepository(_services.Repositories.Selected),
                cancellationToken: cancellationToken);
            if (latestMod is null)
            {
                if (requireDownloadInfo) throw new InvalidDataException("Download file metadata is unavailable.");
                return mod;
            }

            latestMod.Title = string.IsNullOrWhiteSpace(latestMod.Title) ? mod.Title : latestMod.Title;
            if (IsGenericOnlineCharacterName(latestMod.CharacterName)
                && !IsGenericOnlineCharacterName(mod.CharacterName))
            {
                latestMod.CharacterName = mod.CharacterName;
                latestMod.IsCharacterNameInferred = mod.IsCharacterNameInferred;
            }
            else if (string.IsNullOrWhiteSpace(latestMod.CharacterName))
            {
                latestMod.CharacterName = mod.CharacterName;
            }
            latestMod.RootCategoryName = string.IsNullOrWhiteSpace(latestMod.RootCategoryName) ? mod.RootCategoryName : latestMod.RootCategoryName;
            latestMod.Author = string.IsNullOrWhiteSpace(latestMod.Author) ? mod.Author : latestMod.Author;
            if (latestMod.AuthorId <= 0) latestMod.AuthorId = mod.AuthorId;
            latestMod.ProfileUrl = string.IsNullOrWhiteSpace(latestMod.ProfileUrl) ? mod.ProfileUrl : latestMod.ProfileUrl;
            latestMod.PreviewUrl = string.IsNullOrWhiteSpace(latestMod.PreviewUrl) ? mod.PreviewUrl : latestMod.PreviewUrl;
            latestMod.CategoryId = string.IsNullOrWhiteSpace(latestMod.CategoryId) ? mod.CategoryId : latestMod.CategoryId;
            return latestMod;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch when (!requireDownloadInfo)
        {
            return mod;
        }
    }

    internal static OnlineModCard WithSelectedDownloadFile(OnlineModCard mod, OnlineDownloadCandidate selectedFile)
    {
        return new OnlineModCard
        {
            ItemId = mod.ItemId,
            Title = mod.Title,
            CharacterName = mod.CharacterName,
            CategoryId = mod.CategoryId,
            IsCharacterNameInferred = mod.IsCharacterNameInferred,
            RootCategoryName = mod.RootCategoryName,
            Author = mod.Author,
            AuthorId = mod.AuthorId,
            Likes = mod.Likes,
            Views = mod.Views,
            Downloads = mod.Downloads,
            HotnessScore = mod.HotnessScore,
            FileSizeBytes = selectedFile.FileSizeBytes,
            PreviewUrl = mod.PreviewUrl,
            ProfileUrl = mod.ProfileUrl,
            DownloadUrl = selectedFile.DownloadUrl,
            DownloadFileName = selectedFile.FileName,
            DownloadFileId = selectedFile.FileId,
            DownloadFiles = mod.DownloadFiles,
            HasUpdates = mod.HasUpdates,
            UpdatedAt = mod.UpdatedAt
        };
    }

    private async Task DownloadAndExtractOnlineModAsync(
        OnlineModCard mod,
        OnlineDownloadCandidate? selectedFile = null,
        bool refreshDownloadInfo = true,
        OnlineDownloadContext? capturedContext = null,
        bool? capturedEndfieldMetadata = null,
        IReadOnlyCollection<string>? capturedCharacterAliases = null,
        string? confirmedDestination = null, bool batchConfirmed = false)
    {
        WorkspaceRepository? initialRepository = _services.Repositories.Selected;
        OnlineDownloadContext context = capturedContext ?? CaptureOnlineDownloadContext(initialRepository);
        bool useEndfieldMetadata = capturedEndfieldMetadata ?? IsEndfieldRepository(initialRepository);
        IReadOnlyCollection<string> characterAliases = capturedCharacterAliases ?? GetCharacterFolderAliases(mod);
        using OnlineDownloadActionAdapter.Lease? action = _onlineDownloadActions.TryBegin(context, GetOnlineDownloadIdentity(mod));
        if (action is null) return;
        try
        {
            RefreshOnlineDetailActionState();
            await DownloadAndExtractOnlineModCoreAsync(mod, selectedFile, refreshDownloadInfo,
                context, useEndfieldMetadata, characterAliases, action, confirmedDestination, batchConfirmed);
        }
        catch (OperationCanceledException) when (action.Token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _host.LogApplicationIssue("Prepare online download", ex);
            await ReportOnlineActionErrorAsync(T("OnlineDownloadFlow.FailedToPrepareDownload") + ex.Message,
                T("Common.OnlineDownloadFailed"), ex);
        }
        finally
        {
            action.Dispose();
            RefreshOnlineDetailActionState();
            if (!_services.IsClosing) RefreshOnlinePaneV2();
        }
    }

    private async Task DownloadAndExtractOnlineModCoreAsync(
        OnlineModCard mod, OnlineDownloadCandidate? selectedFile, bool refreshDownloadInfo,
        OnlineDownloadContext context, bool useEndfieldMetadata,
        IReadOnlyCollection<string> characterAliases, OnlineDownloadActionAdapter.Lease action,
        string? confirmedDestination = null, bool batchConfirmed = false)
    {
        action.Token.ThrowIfCancellationRequested();
        OnlineModCard effectiveMod = refreshDownloadInfo
            ? await RefreshOnlineModDownloadInfoAsync(mod, useEndfieldMetadata, action.Token)
            : mod;
        action.Token.ThrowIfCancellationRequested();

        if (confirmedDestination is not null && selectedFile is { FileId.Length: > 0 }
            && !effectiveMod.DownloadFiles.Any(file => file.FileId == selectedFile.FileId))
            throw new InvalidOperationException(T("OnlineDownloadFlow.TheOriginalFileIsNoLonger"));

        OnlineDownloadCandidate? effectiveFile = selectedFile is null
            ? OnlineDownloadSelectionPolicy.SelectDefault(effectiveMod.DownloadFiles)
            : effectiveMod.DownloadFiles.FirstOrDefault(file =>
                !string.IsNullOrWhiteSpace(selectedFile.FileId)
                && string.Equals(file.FileId, selectedFile.FileId, StringComparison.Ordinal))
                ?? selectedFile;
        if (effectiveFile is not null)
        {
            effectiveMod = WithSelectedDownloadFile(effectiveMod, effectiveFile);
        }

        if (!IsGenericOnlineCharacterName(effectiveMod.CharacterName))
            characterAliases = characterAliases.Append(effectiveMod.CharacterName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        if (string.IsNullOrWhiteSpace(effectiveMod.DownloadUrl))
        {
            await ShowMessageAsync(
                T("OnlineDownloadFlow.ThisEntryDoesNotHaveA"),
                T("OnlineDownloadFlow.CannotDownload"));
            return;
        }

        if (!batchConfirmed && !await ConfirmOnlineDuplicateAsync(effectiveMod, effectiveFile, context)) return;
        action.Token.ThrowIfCancellationRequested();
        string? selectedFolder = await ResolveOnlineDownloadFolderAsync(effectiveMod, context, characterAliases, confirmedDestination);
        action.Token.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(selectedFolder))
        {
            return;
        }

        DownloadTaskItem downloadTask = _host.CreateDownloadTask(effectiveMod, selectedFolder);
        downloadTask.RetryRequest = new OnlineDownloadRetryRequest(effectiveMod, effectiveFile, context,
            useEndfieldMetadata, characterAliases.ToArray(), selectedFolder);
        action.MarkTaskStarted();
        var session = new OnlineDownloadSession(context, selectedFolder);
        downloadTask.Session = session;
        bool installBusy = false;
        try
        {
            PersistDownloadTasks(required: true);
            RefreshOnlineDetailActionState();
            using IDisposable execution = await _services.Downloads.Scheduler.AcquireAsync(downloadTask.Cancellation.Token);
            _foregroundDownloadTaskId = downloadTask.Id;
            SetOnlineInstallBusy(true);
            installBusy = true;
            // The picker is finished; a different visible Mod can now start its own task.
            RefreshOnlineDetailActionState();
            _host.UpdateDownloadTask(downloadTask, DownloadTaskState.Preparing, 0, M("OnlineDownload.ReadingInformation"), forceRefresh: true);
            downloadTask.Cancellation.Token.ThrowIfCancellationRequested();
            OnlineModDetails details = await GetOnlineModDetailsAsync(effectiveMod, downloadTask.Cancellation.Token);

            SetOnlineDownloadProgress(true, 0, T("OnlineDownload.Preparing"), isIndeterminate: true);
            SetOnlineStatus(string.IsNullOrWhiteSpace(details.AccessRequirementSummary)
                ? M("OnlineDownload.Downloading", effectiveMod.Title)
                : M("OnlineDownload.DownloadingWithRequirements", effectiveMod.Title, details.AccessRequirementSummary));

            string archivePath = await DownloadOnlineModArchiveAsync(effectiveMod, selectedFolder, downloadTask, downloadTask.Cancellation.Token);
            session.RegisterArchive(archivePath);
            string resolvedArchivePath = EnsureDownloadArchiveExtension(archivePath);
            session.RegisterArchive(resolvedArchivePath);
            downloadTask.ArchivePath = resolvedArchivePath;
            if (!_host.IsSupportedArchiveFile(resolvedArchivePath))
            {
                throw new InvalidOperationException(T("OnlineDownloadFlow.TheFileWasDownloadedButIt", Path.GetFileName(resolvedArchivePath)));
            }

            string extractFolder = CreateUniqueExtractionFolder(selectedFolder, effectiveMod.Title, effectiveMod.ItemId);
            // Do not acquire or later delete a pre-existing extraction folder.
            if (File.Exists(extractFolder) || Directory.Exists(extractFolder))
                throw new IOException(T("OnlineDownloadFlow.TheExtractionDestinationAlreadyExistsRetry"));
            Directory.CreateDirectory(extractFolder);
            session.RegisterExtraction(extractFolder);

            downloadTask.DestinationPath = extractFolder;
            _host.UpdateDownloadTask(downloadTask, DownloadTaskState.Extracting, 92, M("OnlineDownload.Extracting"), forceRefresh: true);
            downloadTask.Cancellation.Token.ThrowIfCancellationRequested();
            await Task.Run(() =>
            {
                _host.ExtractArchiveToDirectory(resolvedArchivePath, extractFolder);
                // The extraction folder is already this Mod's own folder; an archive that
                // brings its own top-level folder would otherwise be nested twice.
                ArchiveImportLayout.HoistSingleTopFolder(extractFolder);
            });
            downloadTask.Cancellation.Token.ThrowIfCancellationRequested();
            PreparedOnlineInstallMetadata prepared = await session.PrepareAndCommitAsync(
                token => PrepareOnlineInstallMetadataAsync(extractFolder, effectiveMod, details, token),
                metadata => CommitOnlineInstallMetadata(extractFolder, metadata),
                downloadTask.Cancellation.Token);
            _host.UpdateDownloadTask(downloadTask, DownloadTaskState.Completed, 100,
                M(prepared.PersistenceWarning is null ? "OnlineDownload.CompletedAt" : "OnlineDownload.InstalledConfigNotSaved", extractFolder),
                forceRefresh: true);

            // A failed refresh/notification must never turn a committed installation into a
            // failed task or delete it. A changed repository must not receive the old selection.
            try
            {
                WorkspaceRepository? repository = _services.Repositories.Selected;
                if (repository?.Id == context.RepositoryId
                    && string.Equals(repository.SourcePath, context.RepositoryPath, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(context.RepositoryPath)
                    && IsPathInsideDirectory(extractFolder, context.RepositoryPath))
                {
                    await _host.RefreshListsAsync();
                    repository = _services.Repositories.Selected;
                    if (repository?.Id == context.RepositoryId
                        && string.Equals(repository.SourcePath, context.RepositoryPath, StringComparison.OrdinalIgnoreCase))
                        _host.SelectSecondLevelByPath(extractFolder);
                }
                SetOnlineStatus(M("OnlineDownload.ExtractedTo", extractFolder));
                _host.RefreshUpdatesPane();
                ShowAppNotification(T("OnlineDownload.Completed", effectiveMod.Title));
                if (prepared.PersistenceWarning is not null)
                    await ShowMessageAsync(T("OnlineDownloadFlow.TheModIsInstalledButIts"),
                        T("OnlineDownloadFlow.TrackingRecordNotSaved"));
            }
            catch (Exception refreshException)
            {
                _host.LogApplicationIssue("Refresh after online installation", refreshException);
            }
        }
        catch (OperationCanceledException)
        {
            CleanupOnlineDownload(session, canceled: true);
            _host.UpdateDownloadTask(downloadTask, DownloadTaskState.Canceled, downloadTask.Progress, M("OnlineDownload.TaskCanceled"), forceRefresh: true);
            SetOnlineStatus(M("OnlineDownload.Canceled"));
        }
        catch (Exception ex)
        {
            CleanupOnlineDownload(session, canceled: false);
            _host.UpdateDownloadTask(downloadTask, DownloadTaskState.Failed, downloadTask.Progress, M("OnlineDownload.TaskFailed", ex.Message), forceRefresh: true);
            SetOnlineStatus(M("OnlineDownload.DownloadOrExtractFailed"));
            string message = ex.Message;
            if (ex is InvalidDataException or IOException)
            {
                if (message.Contains("End of Central Directory", StringComparison.OrdinalIgnoreCase))
                {
                    message = T("OnlineDownloadFlow.TheDownloadedFileIsNotA");
                }
            }

            string failedArchiveName = Path.GetFileName(downloadTask.ArchivePath);
            _host.UpdateDownloadTask(downloadTask, DownloadTaskState.Failed, downloadTask.Progress,
                M("OnlineDownload.FailedWithReason", failedArchiveName, message), forceRefresh: true);
            ShowAppNotification(T("OnlineDownload.Failed", effectiveMod.Title), InfoBarSeverity.Error);
        }
        finally
        {
            if (!_services.IsClosing && string.Equals(_foregroundDownloadTaskId, downloadTask.Id, StringComparison.Ordinal))
            {
                _foregroundDownloadTaskId = null;
                SetOnlineDownloadProgress(false, 0, string.Empty);
            }
            if (!_services.IsClosing && !_services.Downloads.Tasks.Any(task => DownloadProgressPolicy.IsActive(task.State)))
            {
                _foregroundDownloadTaskId = null;
                SetOnlineDownloadProgress(false, 0, string.Empty);
            }
            if (installBusy && !_services.IsClosing) SetOnlineInstallBusy(false);
            try { if (!_services.IsClosing) RefreshOnlinePaneV2(); }
            catch (Exception refreshException) { _host.LogApplicationIssue("Refresh online task UI", refreshException); }
        }
    }

    internal async Task<string> DownloadOnlineModArchiveAsync(
        OnlineModCard mod,
        string destinationFolder,
        DownloadTaskItem downloadTask,
        CancellationToken cancellationToken)
    {
        var service = new OnlineArchiveDownloadService(_host.Http);
        try
        {
            OnlineArchiveDownloadResult result = await service.DownloadAsync(
                new(mod.DownloadUrl!, mod.ProfileUrl, mod.DownloadFileName, mod.Title, mod.ItemId, mod.FileSizeBytes),
                destinationFolder,
                progress => DispatcherQueue.TryEnqueue(() =>
                {
                    if (downloadTask.State is not (DownloadTaskState.Preparing or DownloadTaskState.Downloading)) return;
                    downloadTask.HasKnownTotalLength = progress.TotalBytes > 0;
                    string currentSize = FormatFileSize(progress.BytesRead);
                    string totalSize = progress.TotalBytes > 0 ? FormatFileSize(progress.TotalBytes) : "?";
                    UiMessage downloadStatus = M("OnlineDownload.Progress", currentSize, totalSize);
                    if (string.Equals(_foregroundDownloadTaskId, downloadTask.Id, StringComparison.Ordinal))
                        SetOnlineDownloadProgress(true, progress.Percent, T(downloadStatus),
                            isIndeterminate: progress.TotalBytes <= 0);
                    _host.UpdateDownloadTask(downloadTask, DownloadTaskState.Downloading,
                        Math.Min(90, progress.Percent * 0.9), downloadStatus, forceRefresh: true);
                }),
                cancellationToken);
            downloadTask.ArchivePath = result.ArchivePath;
            return result.ArchivePath;
        }
        catch (OnlineArchiveSizeException ex)
        {
            throw new InvalidOperationException(T("OnlineDownloadFlow.DownloadedFileSizeIsIncorrectExpected", FormatFileSize(ex.ExpectedBytes), FormatFileSize(ex.ActualBytes)), ex);
        }
    }

    internal string EnsureDownloadArchiveExtension(string archivePath)
    {
        if (_host.IsSupportedArchiveFile(archivePath))
        {
            return archivePath;
        }

        string detectedExtension = OnlineDownloadFilePolicy.DetectArchiveExtension(archivePath);
        if (string.IsNullOrWhiteSpace(detectedExtension))
        {
            return archivePath;
        }

        return OnlineDownloadFilePolicy.MoveWithoutOverwrite(archivePath, archivePath + detectedExtension);
    }

    internal static string FormatFileSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double size = bytes;
        int unitIndex = 0;
        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return $"{size:0.##} {units[unitIndex]}";
    }

    private static string CreateUniqueExtractionFolder(string parentFolder, string title, int itemId)
    {
        string baseName = SanitizeFileName(title);
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = $"mod-{itemId}";
        }

        string candidate = Path.Combine(parentFolder, baseName);
        if (!Directory.Exists(candidate))
        {
            return candidate;
        }

        int suffix = 2;
        while (true)
        {
            string next = Path.Combine(parentFolder, $"{baseName}-{suffix}");
            if (!Directory.Exists(next))
            {
                return next;
            }

            suffix++;
        }
    }

    private static string GetTrackedModIdentity(int itemId, string? profileUrl)
    {
        return OnlineInstallationDetectionPolicy.BuildIdentity(itemId, profileUrl);
    }

    private TrackedModOrigin? FindInstalledOnlineMod(OnlineModCard mod)
    {
        WorkspaceRepository? repository = _services.Repositories.Selected;
        string repositoryPath = repository?.SourcePath ?? string.Empty;

        foreach (TrackedModOrigin origin in _host.TrackedModOrigins.Values)
        {
            try
            {
                if (!Directory.Exists(origin.Path))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(repositoryPath)
                    && !IsPathInsideDirectory(origin.Path, repositoryPath))
                {
                    continue;
                }

                if (OnlineInstallationDetectionPolicy.Matches(
                    mod.ItemId,
                    mod.ProfileUrl,
                    origin.ItemId,
                    origin.ProfileUrl))
                {
                    return origin;
                }
            }
            catch (Exception)
            {
                // Ignore stale or inaccessible records while rendering the online browser.
            }
        }

        return null;
    }

    internal bool DeduplicateTrackedModOrigins()
    {
        IReadOnlyList<string> stalePaths = OnlineTrackingScopePolicy.FindStaleDuplicatePaths(
            _host.TrackedModOrigins.Values.Select(origin => new OnlineTrackedInstallation(
                origin.Path, GetTrackedModIdentity(origin.ItemId, origin.ProfileUrl),
                Directory.Exists(origin.Path), origin.LastKnownUpdatedAt)),
            _services.Repositories.Items.Select(repository => repository.SourcePath));
        foreach (string path in stalePaths)
        {
            _host.TrackedModOrigins.Remove(path);
            _host.TrackedModUpdateResults.RemoveAll(result => string.Equals(result.Path, path, StringComparison.CurrentCultureIgnoreCase));
        }
        return stalePaths.Count > 0;
    }

    internal async Task RemoveTrackedModRecordAsync(TrackedModUpdateResult result)
    {
        bool confirmed = await ShowDangerConfirmAsync(
            T("OnlineDownloadFlow.RemoveTheUpdateTrackingRecordFor", result.Title),
            T("OnlineDownloadFlow.RemoveTracking"), T("Dialogs.Remove"));
        if (!confirmed)
        {
            return;
        }

        _host.TrackedModOrigins.Remove(result.Path);
        _host.TrackedModUpdateResults.RemoveAll(item => string.Equals(item.Path, result.Path, StringComparison.CurrentCultureIgnoreCase));
        _host.SaveConfig();
        _host.SaveShellConfig();
        _host.SetModUpdateStatus(M("Updates.TrackingRemoved", result.Title));
        _host.RefreshUpdatesPane();
    }

    private async Task<string?> SaveOnlinePreviewImageAsync(
        string modFolder,
        OnlineModCard mod,
        OnlineModDetails? details,
        CancellationToken cancellationToken = default)
    {
        string? temporaryPath = null;
        try
        {
            List<string> existingFiles = Directory.GetFiles(modFolder).ToList();
            if (!string.IsNullOrWhiteSpace(_host.FindPreviewImage(existingFiles)))
            {
                return null;
            }

            List<(string Url, bool IsFallbackThumbnail)> candidateUrls = (details?.ImageUrls ?? [])
                .Where(url => !string.IsNullOrWhiteSpace(url)
                    && !string.Equals(url, mod.PreviewUrl, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .Select(url => (url, false))
                .ToList();
            if (!string.IsNullOrWhiteSpace(mod.PreviewUrl)
                && !candidateUrls.Any(candidate => string.Equals(
                    candidate.Url,
                    mod.PreviewUrl,
                    StringComparison.OrdinalIgnoreCase)))
            {
                candidateUrls.Add((mod.PreviewUrl, true));
            }

            if (candidateUrls.Count == 0)
            {
                return null;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            var downloadedImages = new List<(OnlinePreviewCandidate Candidate, byte[] Bytes)>();
            for (int index = 0; index < candidateUrls.Count; index++)
            {
                (string imageUrl, bool isFallbackThumbnail) = candidateUrls[index];
                if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out Uri? imageUri)
                    || (imageUri.Scheme != Uri.UriSchemeHttps && imageUri.Scheme != Uri.UriSchemeHttp))
                {
                    continue;
                }

                byte[]? imageBytes = await GetOnlinePreviewCandidateBytesAsync(
                    imageUrl,
                    imageUri,
                    timeout.Token);
                if (imageBytes is null || imageBytes.Length == 0 || imageBytes.Length > ImageService.MaxImageBytes)
                {
                    continue;
                }

                (int width, int height) = await TryReadOnlineImageDimensionsAsync(imageBytes);
                if (width <= 0 || height <= 0)
                {
                    continue;
                }

                downloadedImages.Add((new OnlinePreviewCandidate
                {
                    Url = imageUrl,
                    PixelWidth = width,
                    PixelHeight = height,
                    FileSizeBytes = imageBytes.LongLength,
                    IsFallbackThumbnail = isFallbackThumbnail,
                    SourceOrder = index
                }, imageBytes));
            }

            OnlinePreviewCandidate? selected = OnlinePreviewSelectionPolicy.SelectBest(
                downloadedImages.Select(item => item.Candidate));
            if (selected is null)
            {
                return null;
            }

            byte[] selectedBytes = downloadedImages.First(item => ReferenceEquals(item.Candidate, selected)).Bytes;
            Uri selectedUri = new(selected.Url);
            string extension = GetOnlinePreviewImageExtension(selectedUri, selectedBytes);
            string previewPath = Path.Combine(modFolder, "preview" + extension);
            temporaryPath = previewPath + ".download";
            await File.WriteAllBytesAsync(temporaryPath, selectedBytes, timeout.Token);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, previewPath, true);
            temporaryPath = null;
            return selected.Url;
        }
        catch (Exception ex)
        {
            _host.LogApplicationIssue("Preview image download", ex);
            if (!string.IsNullOrWhiteSpace(temporaryPath) && File.Exists(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (Exception cleanupException)
                {
                    _host.LogApplicationIssue("Preview image cleanup", cleanupException);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }
    }

    private async Task<byte[]?> GetOnlinePreviewCandidateBytesAsync(
        string imageUrl,
        Uri imageUri,
        CancellationToken cancellationToken)
    {
        Uri? cachedUri = _services.Images.TryGetExistingCachedUri(imageUrl);
        if (cachedUri is { IsFile: true } && File.Exists(cachedUri.LocalPath))
        {
            FileInfo cachedFile = new(cachedUri.LocalPath);
            if (cachedFile.Length > 0 && cachedFile.Length <= ImageService.MaxImageBytes)
            {
                return await File.ReadAllBytesAsync(cachedUri.LocalPath, cancellationToken);
            }
        }

        return await _services.Images.DownloadImageBytesWithRetryAsync(imageUri, cancellationToken);
    }

    private static async Task<(int Width, int Height)> TryReadOnlineImageDimensionsAsync(byte[] imageBytes)
    {
        try
        {
            using var randomAccessStream = new InMemoryRandomAccessStream();
            using (IOutputStream outputStream = randomAccessStream.GetOutputStreamAt(0))
            using (var writer = new DataWriter(outputStream))
            {
                writer.WriteBytes(imageBytes);
                await writer.StoreAsync();
                writer.DetachStream();
            }

            randomAccessStream.Seek(0);
            BitmapDecoder decoder = await BitmapDecoder.CreateAsync(randomAccessStream);
            if (decoder.PixelWidth > int.MaxValue || decoder.PixelHeight > int.MaxValue)
            {
                return (0, 0);
            }

            return ((int)decoder.PixelWidth, (int)decoder.PixelHeight);
        }
        catch
        {
            return (0, 0);
        }
    }

    private string GetOnlinePreviewImageExtension(Uri imageUri, byte[] imageBytes)
    {
        string extension = Path.GetExtension(imageUri.AbsolutePath).ToLowerInvariant();
        if (_host.ImageExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return extension;
        }

        if (imageBytes.Length >= 8
            && imageBytes[0] == 0x89
            && imageBytes[1] == 0x50
            && imageBytes[2] == 0x4E
            && imageBytes[3] == 0x47)
        {
            return ".png";
        }

        if (imageBytes.Length >= 6
            && imageBytes[0] == (byte)'G'
            && imageBytes[1] == (byte)'I'
            && imageBytes[2] == (byte)'F')
        {
            return ".gif";
        }

        if (imageBytes.Length >= 12
            && imageBytes[0] == (byte)'R'
            && imageBytes[1] == (byte)'I'
            && imageBytes[2] == (byte)'F'
            && imageBytes[3] == (byte)'F'
            && imageBytes[8] == (byte)'W'
            && imageBytes[9] == (byte)'E'
            && imageBytes[10] == (byte)'B'
            && imageBytes[11] == (byte)'P')
        {
            return ".webp";
        }

        if (imageBytes.Length >= 2 && imageBytes[0] == (byte)'B' && imageBytes[1] == (byte)'M')
        {
            return ".bmp";
        }

        return ".jpg";
    }

    internal static bool IsPathInsideDirectory(string candidatePath, string parentPath)
    {
        return PathSafety.IsSameOrInside(candidatePath, parentPath);
    }

    private static string SanitizeFileName(string value)
    {
        char[] invalidChars = Path.GetInvalidFileNameChars();
        StringBuilder builder = new(value.Length);
        foreach (char ch in value)
        {
            builder.Append(invalidChars.Contains(ch) ? '_' : ch);
        }

        return builder.ToString().Trim().Trim('.');
    }

    private async Task<string?> PickDownloadFolderAsync(OnlineModCard mod, OnlineDownloadContext context,
        IReadOnlyCollection<string> characterAliases)
    {
        var repository = new WorkspaceRepository { Id = context.RepositoryId, SourcePath = context.RepositoryPath };
        if (Directory.Exists(repository.SourcePath))
        {
            string[] childFolders = Directory.GetDirectories(repository.SourcePath)
                .OrderBy(path => path, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
            CharacterFolderPlan plan = PlanCharacterFolder(mod, repository.SourcePath, characterAliases);
            string? suggestedFolder = plan.Action == CharacterFolderAction.UseExisting ? plan.Path : null;

            var options = new List<(string Label, object Tag)> { (T("OnlineDownloadFlow.UseTheRepositoryRootFolder"), repository.SourcePath) };
            int suggestedIndex = 0;
            if (plan.Action == CharacterFolderAction.Create)
            {
                options.Add((T("OnlineDownloadFlow.NewCharacterFolderRecommended", Path.GetFileName(plan.Path)), new NewCharacterFolderChoice(plan.Path!)));
                suggestedIndex = 1;
            }

            foreach (string childFolder in childFolders)
            {
                bool isSuggested = !string.IsNullOrWhiteSpace(suggestedFolder)
                    && string.Equals(childFolder, suggestedFolder, StringComparison.OrdinalIgnoreCase);
                options.Add((isSuggested
                    ? T("OnlineDownloadFlow.Recommended", Path.GetFileName(childFolder))
                    : Path.GetFileName(childFolder), childFolder));
                if (isSuggested)
                {
                    suggestedIndex = options.Count - 1;
                }
            }

            string characterDisplayName = GetOnlineCharacterDisplayName(mod.CharacterName);
            string destinationHint = plan.Action switch
            {
                CharacterFolderAction.UseExisting => T("OnlineDownloadFlow.RecognizedAndPreselected", characterDisplayName, Path.GetFileName(suggestedFolder)),
                CharacterFolderAction.Create => T("OnlineDownloadFlow.ThisRepositoryHasNoFolderFor", characterDisplayName, plan.Path),
                _ => CharacterFolderReasonText(plan, characterDisplayName)
            };

            (ContentDialogResult result, object? tag) = await DownloadLocationDialog.ShowAsync(_services, repository.SourcePath, destinationHint, options, suggestedIndex);
            if (result == ContentDialogResult.Primary)
            {
                if (tag is NewCharacterFolderChoice choice)
                {
                    try
                    {
                        return CharacterFolderPlanner.CreatePlannedFolder(repository.SourcePath, choice.Path);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        await ShowMessageAsync(T("OnlineDownloadFlow.CouldNotCreateTheCharacterFolder") + ex.Message,
                            T("OnlineDownloadFlow.DownloadLocation"));
                        return null;
                    }
                }
                return tag as string ?? repository.SourcePath;
            }

            if (result == ContentDialogResult.None)
            {
                return null;
            }
        }

        return await _services.Dialogs.PickFolderAsync();
    }
}
