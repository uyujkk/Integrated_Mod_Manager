namespace IntegratedModManager.Core.Deployment;

public sealed class ModInstallBackupEntry
{
    public string TargetPath { get; set; } = string.Empty;

    public bool ExistedBefore { get; set; }

    public string BackupRelativePath { get; set; } = string.Empty;

    public string? LinkTarget { get; set; }
}
