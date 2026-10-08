using IntegratedModManager.Core;

namespace ModFolderCopier.WinUI;

// Keeps the existing SQLite schema and public IndexedModFolder compatibility.
// Core scanning knows neither SQLite nor the legacy WinUI namespace.
public sealed class RepositoryIndexStoreAdapter(AppDataStore store) : IRepositoryIndexStore
{
    public IReadOnlyList<RepositoryModIndexEntry> Read(string repositoryId, string sourceRoot)
        => store.ReadModIndex(repositoryId, sourceRoot).Select(folder => new RepositoryModIndexEntry(
            folder.FirstLevelPath, folder.ModPath, folder.FolderStampUtc,
            folder.FileCount, folder.TotalBytes, folder.Files.ToArray())).ToArray();

    public void Replace(string repositoryId, string sourceRoot, IReadOnlyList<RepositoryModIndexEntry> folders)
        => store.ReplaceModIndex(repositoryId, sourceRoot, folders.Select(folder => new IndexedModFolder
        {
            FirstLevelPath = folder.FirstLevelPath,
            ModPath = folder.ModPath,
            FolderStampUtc = folder.FolderStampUtc,
            FileCount = folder.FileCount,
            TotalBytes = folder.TotalBytes,
            Files = folder.Files.ToList()
        }).ToArray());
}
