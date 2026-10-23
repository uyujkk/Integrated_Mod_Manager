using System.Text;
using IntegratedModManager.Core.Archives;
using IntegratedModManager.Core.Configuration;
using IntegratedModManager.Core.Deployment;
using IntegratedModManager.Core.Infrastructure;
using IntegratedModManager.Core.Layout;
using IntegratedModManager.Core.Online;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.System;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private async Task<OnlineModCard> RefreshOnlineModDownloadInfoAsync(OnlineModCard mod, bool? useEndfieldMetadata = null,
        CancellationToken cancellationToken = default, bool requireDownloadInfo = false)
    {
        try
        {
            OnlineModCard? latestMod = await FetchGameBananaModCardAsyncV2(
                mod.ItemId,
                useEndfieldMetadata: useEndfieldMetadata ?? IsEndfieldRepository(GetSelectedRepository()),
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

    private static OnlineModCard WithSelectedDownloadFile(OnlineModCard mod, OnlineDownloadCandidate selectedFile)
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
        WorkspaceRepository? initialRepository = GetSelectedRepository();
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
            LogApplicationIssue("Prepare online download", ex);
            await ReportOnlineActionErrorAsync(T("OnlineDownloadFlow.FailedToPrepareDownload") + ex.Message,
                T("Common.OnlineDownloadFailed"));
        }
        finally
        {
            action.Dispose();
            RefreshOnlineDetailActionState();
            if (!_onlineWindowClosed) RefreshOnlinePaneV2();
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

        DownloadTaskItem downloadTask = CreateDownloadTask(effectiveMod, selectedFolder);
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
            using IDisposable execution = await _onlineDownloadScheduler.AcquireAsync(downloadTask.Cancellation.Token);
            _foregroundDownloadTaskId = downloadTask.Id;
            SetOnlineInstallBusy(true);
            installBusy = true;
            // The picker is finished; a different visible Mod can now start its own task.
            RefreshOnlineDetailActionState();
            UpdateDownloadTask(downloadTask, DownloadTaskState.Preparing, 0, "正在读取下载信息...", "Reading download information...", forceRefresh: true);
            downloadTask.Cancellation.Token.ThrowIfCancellationRequested();
            OnlineModDetails details = await GetOnlineModDetailsAsync(effectiveMod, downloadTask.Cancellation.Token);

            SetOnlineDownloadProgress(true, 0, "准备下载...", "Preparing download...", isIndeterminate: true);
            string downloadStatusZh = $"正在下载 {effectiveMod.Title}，完成后会自动解压到你选择的文件夹中。";
            string downloadStatusEn = $"Downloading {effectiveMod.Title}. It will be extracted into the folder you selected.";
            if (!string.IsNullOrWhiteSpace(details.AccessRequirementSummary))
            {
                downloadStatusZh += $" 页面文字提到了可能的额外访问要求：{details.AccessRequirementSummary}";
                downloadStatusEn += $" The page text mentions possible extra access requirements: {details.AccessRequirementSummary}";
            }

            SetOnlineStatus(downloadStatusZh, downloadStatusEn);

            string archivePath = await DownloadOnlineModArchiveAsync(effectiveMod, selectedFolder, downloadTask, downloadTask.Cancellation.Token);
            session.RegisterArchive(archivePath);
            string resolvedArchivePath = EnsureDownloadArchiveExtension(archivePath);
            session.RegisterArchive(resolvedArchivePath);
            downloadTask.ArchivePath = resolvedArchivePath;
            if (!IsSupportedArchiveFile(resolvedArchivePath))
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
            UpdateDownloadTask(downloadTask, DownloadTaskState.Extracting, 92, "正在解压并写入 Mod 信息...", "Extracting and writing mod metadata...", forceRefresh: true);
            downloadTask.Cancellation.Token.ThrowIfCancellationRequested();
            await Task.Run(() =>
            {
                ExtractArchiveToDirectory(resolvedArchivePath, extractFolder);
                // The extraction folder is already this Mod's own folder; an archive that
                // brings its own top-level folder would otherwise be nested twice.
                ArchiveImportLayout.HoistSingleTopFolder(extractFolder);
            });
            downloadTask.Cancellation.Token.ThrowIfCancellationRequested();
            PreparedOnlineInstallMetadata prepared = await session.PrepareAndCommitAsync(
                token => PrepareOnlineInstallMetadataAsync(extractFolder, effectiveMod, details, token),
                metadata => CommitOnlineInstallMetadata(extractFolder, metadata),
                downloadTask.Cancellation.Token);
            UpdateDownloadTask(downloadTask, DownloadTaskState.Completed, 100,
                prepared.PersistenceWarning is null ? $"已完成：{extractFolder}" : $"已安装，但配置保存失败：{extractFolder}",
                prepared.PersistenceWarning is null ? $"Completed: {extractFolder}" : $"Installed, but config could not be saved: {extractFolder}",
                forceRefresh: true);

            // A failed refresh/notification must never turn a committed installation into a
            // failed task or delete it. A changed repository must not receive the old selection.
            try
            {
                WorkspaceRepository? repository = GetSelectedRepository();
                if (repository?.Id == context.RepositoryId
                    && string.Equals(repository.SourcePath, context.RepositoryPath, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(context.RepositoryPath)
                    && IsPathInsideDirectory(extractFolder, context.RepositoryPath))
                {
                    await RefreshListsAsync();
                    repository = GetSelectedRepository();
                    if (repository?.Id == context.RepositoryId
                        && string.Equals(repository.SourcePath, context.RepositoryPath, StringComparison.OrdinalIgnoreCase))
                        SelectSecondLevelByPath(extractFolder);
                }
                SetOnlineStatus($"已下载并解压到：{extractFolder}", $"Downloaded and extracted to: {extractFolder}");
                RefreshUpdatesPane();
                ShowAppNotification($"下载完成：{effectiveMod.Title}", $"Download completed: {effectiveMod.Title}");
                if (prepared.PersistenceWarning is not null)
                    await ShowMessageAsync(T("OnlineDownloadFlow.TheModIsInstalledButIts"),
                        T("OnlineDownloadFlow.TrackingRecordNotSaved"));
            }
            catch (Exception refreshException)
            {
                LogApplicationIssue("Refresh after online installation", refreshException);
            }
        }
        catch (OperationCanceledException)
        {
            CleanupOnlineDownload(session, canceled: true);
            UpdateDownloadTask(downloadTask, DownloadTaskState.Canceled, downloadTask.Progress, "任务已取消", "Task canceled", forceRefresh: true);
            SetOnlineStatus("下载任务已取消。", "The download task was canceled.");
        }
        catch (Exception ex)
        {
            CleanupOnlineDownload(session, canceled: false);
            UpdateDownloadTask(downloadTask, DownloadTaskState.Failed, downloadTask.Progress, $"失败：{ex.Message}", $"Failed: {ex.Message}", forceRefresh: true);
            SetOnlineStatus("下载或解压在线 Mod 失败。", "Failed to download or extract the online mod.");
            string message = ex.Message;
            if (ex is InvalidDataException or IOException)
            {
                if (message.Contains("End of Central Directory", StringComparison.OrdinalIgnoreCase))
                {
                    message = T("OnlineDownloadFlow.TheDownloadedFileIsNotA");
                }
            }

            string failedArchiveName = Path.GetFileName(downloadTask.ArchivePath);
            UpdateDownloadTask(downloadTask, DownloadTaskState.Failed, downloadTask.Progress,
                $"{failedArchiveName}\n失败原因：{message}", $"{failedArchiveName}\nReason: {message}", forceRefresh: true);
            ShowAppNotification($"下载失败：{effectiveMod.Title}。可在任务中心查看原因并重试。",
                $"Download failed: {effectiveMod.Title}. See the task center for details and retry.", InfoBarSeverity.Error);
        }
        finally
        {
            if (!_onlineWindowClosed && string.Equals(_foregroundDownloadTaskId, downloadTask.Id, StringComparison.Ordinal))
            {
                _foregroundDownloadTaskId = null;
                SetOnlineDownloadProgress(false, 0, string.Empty, string.Empty);
            }
            if (!_onlineWindowClosed && !_downloadTasks.Any(task => DownloadProgressPolicy.IsActive(task.State)))
            {
                _foregroundDownloadTaskId = null;
                SetOnlineDownloadProgress(false, 0, string.Empty, string.Empty);
            }
            if (installBusy && !_onlineWindowClosed) SetOnlineInstallBusy(false);
            try { if (!_onlineWindowClosed) RefreshOnlinePaneV2(); }
            catch (Exception refreshException) { LogApplicationIssue("Refresh online task UI", refreshException); }
        }
    }

    private async Task<string> DownloadOnlineModArchiveAsync(
        OnlineModCard mod,
        string destinationFolder,
        DownloadTaskItem downloadTask,
        CancellationToken cancellationToken)
    {
        var service = new OnlineArchiveDownloadService(_httpClient);
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
                    string statusZh = $"正在下载：{currentSize} / {totalSize}";
                    string statusEn = $"Downloading: {currentSize} / {totalSize}";
                    if (string.Equals(_foregroundDownloadTaskId, downloadTask.Id, StringComparison.Ordinal))
                        SetOnlineDownloadProgress(true, progress.Percent, statusZh, statusEn,
                            isIndeterminate: progress.TotalBytes <= 0);
                    UpdateDownloadTask(downloadTask, DownloadTaskState.Downloading,
                        Math.Min(90, progress.Percent * 0.9), statusZh, statusEn, forceRefresh: true);
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

    private string EnsureDownloadArchiveExtension(string archivePath)
    {
        if (IsSupportedArchiveFile(archivePath))
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

    private static string FormatFileSize(long bytes)
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
        WorkspaceRepository? repository = GetSelectedRepository();
        string repositoryPath = repository?.SourcePath ?? string.Empty;

        foreach (TrackedModOrigin origin in _trackedModOrigins.Values)
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

    private bool DeduplicateTrackedModOrigins()
    {
        IReadOnlyList<string> stalePaths = OnlineTrackingScopePolicy.FindStaleDuplicatePaths(
            _trackedModOrigins.Values.Select(origin => new OnlineTrackedInstallation(
                origin.Path, GetTrackedModIdentity(origin.ItemId, origin.ProfileUrl),
                Directory.Exists(origin.Path), origin.LastKnownUpdatedAt)),
            _repositories.Select(repository => repository.SourcePath));
        foreach (string path in stalePaths)
        {
            _trackedModOrigins.Remove(path);
            _trackedModUpdateResults.RemoveAll(result => string.Equals(result.Path, path, StringComparison.CurrentCultureIgnoreCase));
        }
        return stalePaths.Count > 0;
    }

    private async Task RemoveTrackedModRecordAsync(TrackedModUpdateResult result)
    {
        bool confirmed = await ShowConfirmAsync(
            T("OnlineDownloadFlow.RemoveTheUpdateTrackingRecordFor", result.Title),
            T("OnlineDownloadFlow.RemoveTracking"));
        if (!confirmed)
        {
            return;
        }

        _trackedModOrigins.Remove(result.Path);
        _trackedModUpdateResults.RemoveAll(item => string.Equals(item.Path, result.Path, StringComparison.CurrentCultureIgnoreCase));
        SaveConfig();
        SaveShellConfig();
        SetModUpdateStatus(
            $"已移除 {result.Title} 的追踪记录。",
            $"Removed the tracking record for {result.Title}.");
        RefreshUpdatesPane();
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
            if (!string.IsNullOrWhiteSpace(FindPreviewImage(existingFiles)))
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
                if (imageBytes is null || imageBytes.Length == 0 || imageBytes.Length > MaxOnlinePreviewImageBytes)
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
            LogApplicationIssue("Preview image download", ex);
            if (!string.IsNullOrWhiteSpace(temporaryPath) && File.Exists(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (Exception cleanupException)
                {
                    LogApplicationIssue("Preview image cleanup", cleanupException);
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
        Uri? cachedUri = TryGetExistingCachedOnlineImageUri(imageUrl);
        if (cachedUri is { IsFile: true } && File.Exists(cachedUri.LocalPath))
        {
            FileInfo cachedFile = new(cachedUri.LocalPath);
            if (cachedFile.Length > 0 && cachedFile.Length <= MaxOnlinePreviewImageBytes)
            {
                return await File.ReadAllBytesAsync(cachedUri.LocalPath, cancellationToken);
            }
        }

        return await DownloadOnlineImageBytesWithRetryAsync(imageUri, cancellationToken);
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

    private static string GetOnlinePreviewImageExtension(Uri imageUri, byte[] imageBytes)
    {
        string extension = Path.GetExtension(imageUri.AbsolutePath).ToLowerInvariant();
        if (ImageExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
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

    private static bool IsPathInsideDirectory(string candidatePath, string parentPath)
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

            DialogViewport viewport = DialogViewportPolicy.Selection(RootGrid.ActualWidth, RootGrid.ActualHeight);
            ComboBox folderComboBox = new()
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MinHeight = 40
            };
            folderComboBox.Items.Add(new ComboBoxItem
            {
                Content = T("OnlineDownloadFlow.UseTheRepositoryRootFolder"),
                Tag = repository.SourcePath
            });

            int suggestedIndex = 0;
            if (plan.Action == CharacterFolderAction.Create)
            {
                folderComboBox.Items.Add(new ComboBoxItem
                {
                    Content = T("OnlineDownloadFlow.NewCharacterFolderRecommended", Path.GetFileName(plan.Path)),
                    Tag = new NewCharacterFolderChoice(plan.Path!)
                });
                suggestedIndex = 1;
            }

            foreach (string childFolder in childFolders)
            {
                bool isSuggested = !string.IsNullOrWhiteSpace(suggestedFolder)
                    && string.Equals(childFolder, suggestedFolder, StringComparison.OrdinalIgnoreCase);
                folderComboBox.Items.Add(new ComboBoxItem
                {
                    Content = isSuggested
                        ? T("OnlineDownloadFlow.Recommended", Path.GetFileName(childFolder))
                        : Path.GetFileName(childFolder),
                    Tag = childFolder
                });
                if (isSuggested)
                {
                    suggestedIndex = folderComboBox.Items.Count - 1;
                }
            }

            folderComboBox.SelectedIndex = suggestedIndex;

            string characterDisplayName = GetOnlineCharacterDisplayName(mod.CharacterName);
            string destinationHint = plan.Action switch
            {
                CharacterFolderAction.UseExisting => T("OnlineDownloadFlow.RecognizedAndPreselected", characterDisplayName, Path.GetFileName(suggestedFolder)),
                CharacterFolderAction.Create => T("OnlineDownloadFlow.ThisRepositoryHasNoFolderFor", characterDisplayName, plan.Path),
                _ => CharacterFolderReasonText(plan, characterDisplayName)
            };

            ContentDialog dialog = new()
            {
                Title = T("OnlineDownloadFlow.ChooseDownloadLocation"),
                Content = new ScrollViewer
                {
                    MaxHeight = viewport.ContentHeight,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = new StackPanel
                    {
                        Width = Math.Min(620, viewport.MaxWidth - 48),
                        Spacing = 10,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = T("OnlineDownloadFlow.CurrentModRepositoryFolderASeparate", repository.SourcePath, destinationHint),
                                TextWrapping = TextWrapping.Wrap
                            },
                            folderComboBox
                        }
                    }
                },
                PrimaryButtonText = T("OnlineDownloadFlow.UseSelectedFolder"),
                SecondaryButtonText = T("OnlineDownloadFlow.ChooseAnotherLocation"),
                CloseButtonText = T("Common.Cancel"),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = RootGrid.XamlRoot,
                RequestedTheme = RootGrid.ActualTheme
            };
            dialog.Resources["ContentDialogMaxWidth"] = viewport.MaxWidth;

            ContentDialogResult result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                object? tag = (folderComboBox.SelectedItem as ComboBoxItem)?.Tag;
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

        return await PickFolderAsync();
    }
}
