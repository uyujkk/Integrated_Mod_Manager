namespace ModFolderCopier.WinUI;

public sealed class ModInstallTransaction
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Description { get; set; } = string.Empty;

    public string RepositoryId { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public List<ModInstallBackupEntry> Entries { get; set; } = [];
}
