using IntegratedModManager.Core.Deployment;
using IntegratedModManager.Core.Infrastructure;

namespace ModFolderCopier.WinUI;

// Records of organized imports of existing Mods (stage J finish, step 7b: moved from the workspace page unchanged): where
// they are, which ones were interrupted, and the notice at startup. The workspace page's import commands use them.
internal sealed partial class WorkspaceService
{
    internal string ExistingImportRecordsRoot => Path.Combine(AppContext.BaseDirectory, "backups", "existing-mod-imports");

    internal string[] FindInterruptedImports(string? repositoryPath = null)
    {
        if (!Directory.Exists(ExistingImportRecordsRoot)) return [];
        var records = new List<string>();
        foreach (string path in Directory.EnumerateFiles(ExistingImportRecordsRoot, "*.json").OrderByDescending(File.GetLastWriteTimeUtc).Take(100))
        {
            try
            {
                var record = RecoveryJsonFile.Read<ExistingModImportManifest>(path);
                if (record.Version == 2 && !record.Complete && record.Pending is { Count: > 0 }
                    && (repositoryPath is null || OnlineDownloadService.IsPathInsideDirectory(record.DestinationRoot, repositoryPath))) records.Add(path);
            }
            catch (Exception ex) { _log("Inspect interrupted import record", ex); }
        }
        return records.ToArray();
    }

    internal async Task NotifyInterruptedImportsAsync()
    {
        try
        {
            int count = (await Task.Run(() => FindInterruptedImports())).Length;
            if (count > 0 && !_services.IsClosing)
                _services.Status.Notify(T("ExistingModImport.InterruptedFound", count));
        }
        catch (Exception ex) { _log("Inspect import recovery on startup", ex); }
    }
}
