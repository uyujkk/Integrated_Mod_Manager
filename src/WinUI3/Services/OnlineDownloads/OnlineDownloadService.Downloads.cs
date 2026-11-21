using IntegratedModManager.Core.Configuration;
using IntegratedModManager.Core.Online;

namespace ModFolderCopier.WinUI;

// Where a download goes, duplicates and restarting a download (moved from OnlinePage).
internal sealed partial class OnlineDownloadService
{
    internal static string DownloadFolderKey(OnlineDownloadContext context, OnlineModCard mod)
        => System.Text.Json.JsonSerializer.Serialize(new[] { context.RepositoryId, context.RepositoryPath.TrimEnd('\\', '/'), mod.CharacterName });

    internal bool IsUsableRememberedFolder(string? path, OnlineDownloadContext context)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(path) && !string.IsNullOrWhiteSpace(context.RepositoryPath)
                && Directory.Exists(path) && (string.Equals(Path.GetFullPath(path).TrimEnd('\\'), Path.GetFullPath(context.RepositoryPath).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
                    || IsPathInsideDirectory(path, context.RepositoryPath));
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException) { return false; }
    }

    internal async Task<string?> ResolveOnlineDownloadFolderAsync(OnlineModCard mod, OnlineDownloadContext context,
        IReadOnlyCollection<string> aliases, string? confirmedDestination)
    {
        // Retry is explicitly routed to its original confirmed destination, never the current picker.
        if (confirmedDestination is not null)
        {
            if (!Directory.Exists(confirmedDestination))
                throw new DirectoryNotFoundException(T("OnlineDownloads.TheOriginalDestinationNoLongerExists"));
            return confirmedDestination;
        }
        string key = DownloadFolderKey(context, mod);
        if (_services.Online.Preferences.RememberDownloadFolders && _services.Online.Preferences.DownloadFolders.TryGetValue(key, out string? remembered)
            && IsUsableRememberedFolder(remembered, context)) return remembered;
        string? selected = await PickDownloadFolderAsync(mod, context, aliases);
        if (_services.Online.Preferences.RememberDownloadFolders && IsUsableRememberedFolder(selected, context))
        {
            _services.Online.Preferences.DownloadFolders[key] = selected!;
            _host.SaveShellConfig();
        }
        return selected;
    }

    internal async Task<bool> ConfirmOnlineDuplicateAsync(OnlineModCard mod, OnlineDownloadCandidate? file, OnlineDownloadContext context)
    {
        TrackedModOrigin[] installed = _services.Tracking.Origins.Values.Where(origin =>
            OnlineInstallationDetectionPolicy.Matches(mod.ItemId, mod.ProfileUrl, origin.ItemId, origin.ProfileUrl)
            && Directory.Exists(origin.Path) && !string.IsNullOrWhiteSpace(context.RepositoryPath)
            && IsPathInsideDirectory(origin.Path, context.RepositoryPath)).ToArray();
        OnlineDuplicateKind kind = OnlineDuplicatePolicy.Classify(file?.FileId, installed.Select(item => item.DownloadFileId));
        if (kind == OnlineDuplicateKind.None) return true;
        string summary = kind switch
        {
            OnlineDuplicateKind.SameFile => T("OnlineDownloads.ThisRepositoryAlreadyContainsThisDownload"),
            OnlineDuplicateKind.OtherFile => T("OnlineDownloads.ThisRepositoryContainsAnotherFileFrom"),
            _ => T("OnlineDownloads.ThisRepositoryContainsThisEntryBut")
        };
        return await OnlineDuplicateDialog.ConfirmAsync(_services, summary, installed.Select(item => item.Path));
    }

    internal async Task RetryOnlineDownloadAsync(DownloadTaskItem task)
    {
        if (task.RetryRequest is not { } request || task.RetryInProgress || DownloadActions.IsPreparing) return;
        task.RetryInProgress = true;
        _host.RefreshDownloadTaskCard(task);
        try
        {
            if (!await ConfirmRestoredDownloadAsync(task, request)) return;
            await DownloadAndExtractOnlineModAsync(request.Mod, request.File, refreshDownloadInfo: true,
                capturedContext: request.Context, capturedEndfieldMetadata: request.EndfieldMetadata,
                capturedCharacterAliases: request.CharacterAliases, confirmedDestination: request.Destination);
        }
        catch (Exception ex) { await ReportOnlineActionErrorAsync(ex.Message, T("OnlineDownloads.CannotRestartDownload"), ex); }
        finally { task.RetryInProgress = false; if (!_services.IsClosing) _host.RefreshDownloadTaskCard(task); }
    }

    internal async Task<bool> ConfirmRestoredDownloadAsync(DownloadTaskItem task, OnlineDownloadRetryRequest request)
    {
        if (request.Mod is null || request.Context is null || request.CharacterAliases is null
            || !_services.Repositories.Items.Any(repository => repository.Id == request.Context.RepositoryId
                && string.Equals(repository.SourcePath, request.Context.RepositoryPath, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException(T("DownloadRecovery.TheOriginalRepositoryWasRemovedOr"));
        if (!task.RestoredFromJournal) return true;
return await RestoredDownloadDialog.ConfirmAsync(_services, request.Context.RepositoryName, request.Destination, request.File?.FileName ?? request.Mod.DownloadFileName);
    }
}

public sealed record OnlineDownloadRetryRequest(OnlineModCard Mod, OnlineDownloadCandidate? File,
    OnlineDownloadContext Context, bool EndfieldMetadata, IReadOnlyCollection<string> CharacterAliases, string Destination);
