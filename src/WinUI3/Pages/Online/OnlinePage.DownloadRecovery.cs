namespace ModFolderCopier.WinUI;

public sealed partial class OnlinePage
{
    private async Task<bool> ConfirmRestoredDownloadAsync(DownloadTaskItem task, OnlineDownloadRetryRequest request)
    {
        if (request.Mod is null || request.Context is null || request.CharacterAliases is null
            || !_services.Repositories.Items.Any(repository => repository.Id == request.Context.RepositoryId
                && string.Equals(repository.SourcePath, request.Context.RepositoryPath, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException(T("DownloadRecovery.TheOriginalRepositoryWasRemovedOr"));
        if (!task.RestoredFromJournal) return true;
return await RestoredDownloadDialog.ConfirmAsync(_services, request.Context.RepositoryName, request.Destination, request.File?.FileName ?? request.Mod.DownloadFileName);
    }
}
