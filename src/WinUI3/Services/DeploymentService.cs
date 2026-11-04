namespace ModFolderCopier.WinUI;

/// <summary>
/// Deployment state shared across pages: where installs keep their backups. Stage J7 moves the
/// deploy, remove and install-transaction logic here.
/// </summary>
internal sealed class DeploymentService(string baseDirectory)
{
    public string InstallBackupPath { get; } = Path.Combine(baseDirectory, "backups", "mod-installs");
}
