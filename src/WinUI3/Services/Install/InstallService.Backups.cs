using System.Text.Json;
using IntegratedModManager.Core.Deployment;
using IntegratedModManager.Core.Infrastructure;

namespace ModFolderCopier.WinUI;

// The install backups the Updates page lists and restores (stage J finish, step 8a: moved from the window unchanged).
internal sealed partial class InstallService
{
    private Action<bool> _setWorkspaceBusy = _ => { };
    private Action<string, Exception> _reportBackupFailure = (_, _) => { };

    /// <summary>Called once by the window: the workspace's busy state while a backup is restored, and reporting a failed action.</summary>
    public void AttachBackups(Action<bool> setWorkspaceBusy, Action<string, Exception> reportFailure)
    {
        _setWorkspaceBusy = setWorkspaceBusy;
        _reportBackupFailure = reportFailure;
    }

    /// <summary>The Updates page's list of install backups and their total size.</summary>
    public async Task RefreshBackupListAsync()
    {
        Directory.CreateDirectory(services.Deployment.InstallBackupPath);
        List<InstallBackupDisplayItem> backups = await Task.Run(ReadInstallBackups);
        long totalBytes = backups.Sum(item => item.SizeBytes);
        services.Updates.SetBackups(
            T("ProductivityFeatures.BackupsGB", backups.Count, StorageSizeText.Format(totalBytes), services.ShellSettings.InstallBackupLimitGb),
            [.. backups.Take(30).Select(backup => new InstallBackupRow(backup.Path, backup.Description,
                T("ProductivityFeatures.Text", backup.CreatedAt.ToLocalTime(), backup.ModNames, StorageSizeText.Format(backup.SizeBytes))))]);
    }

    private List<InstallBackupDisplayItem> ReadInstallBackups()
    {
        if (!Directory.Exists(services.Deployment.InstallBackupPath))
        {
            return [];
        }

        List<InstallBackupDisplayItem> result = [];
        foreach (string directory in Directory.EnumerateDirectories(services.Deployment.InstallBackupPath))
        {
            try
            {
                string manifestPath = Path.Combine(directory, "transaction.json");
                if (!File.Exists(manifestPath))
                {
                    continue;
                }

                ModInstallTransaction? transaction = JsonSerializer.Deserialize<ModInstallTransaction>(File.ReadAllText(manifestPath));
                if (transaction is null)
                {
                    continue;
                }

                long size = GetDirectorySize(directory);
                string mods = string.Join(", ", transaction.Entries.Select(entry => Path.GetFileName(entry.TargetPath)).Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase).Take(5));
                if (transaction.Entries.Count > 5)
                {
                    mods += T("ProductivityFeatures.AndMore");
                }
                result.Add(new InstallBackupDisplayItem(directory, transaction.Description, transaction.CreatedAtUtc, string.IsNullOrWhiteSpace(mods) ? T("ProductivityFeatures.NoModRecorded") : mods, size));
            }
            catch (Exception ex)
            {
                _log("Read install backup", ex);
            }
        }

        return result.OrderByDescending(item => item.CreatedAt).ToList();
    }

    /// <summary>Restores a backup after a confirmation; the lists are scanned again.</summary>
    public async Task RestoreBackupAsync(string backupPath)
    {
        try
        {
            if (!Directory.Exists(backupPath))
            {
                return;
            }

            if (!await services.Dialogs.DangerConfirmAsync(T("ProductivityFeatures.RestoreInstallBackup"), T("ProductivityFeatures.RestoringThisBackupReplacesTheCurrent"), T("Dialogs.Restore")))
            {
                return;
            }

            _setWorkspaceBusy(true);
            try
            {
                await RollbackInstallTransactionAsync(backupPath, clearLastTransaction: true);
                await services.Workspace.RefreshListsAsync();
                await RefreshBackupListAsync();
                services.Status.Notify(T("ProductivityFeatures.BackupRestored"));
            }
            catch (Exception ex)
            {
                await services.Dialogs.MessageAsync(T("ProductivityFeatures.RestoreFailed"), T("ProductivityFeatures.FailedToRestoreBackup") + ex.Message);
            }
            finally
            {
                _setWorkspaceBusy(false);
            }
        }
        catch (Exception ex)
        {
            _reportBackupFailure("OnRestoreInstallBackupClicked", ex);
        }
    }

    private sealed record InstallBackupDisplayItem(
        string Path,
        string Description,
        DateTimeOffset CreatedAt,
        string ModNames,
        long SizeBytes);
}
