using IntegratedModManager.Core.Updates;

namespace ModFolderCopier.WinUI;

// The listed Mods' deployment states and the welcome line, in the current language (moved from the window, step 8c).
internal sealed partial class WorkspaceService
{
    /// <summary>Each listed Mod's deployment state in the current language.</summary>
    public void RefreshCopyStates()
    {
        string targetDir = _services.Paths.Target.Trim();
        foreach (FirstLevelFolderItem first in Characters.Visible)
        {
            foreach (SecondLevelFolderItem second in first.Children)
            {
                second.State = FolderCopyState(targetDir, second.Path);
            }
        }
    }

    /// <summary>The welcome line, before a scan found anything.</summary>
    public void SetDefaultStatus()
    {
        Status = T("Localization.ChooseAModStorageFolderAnd", ApplicationReleasePolicy.VersionLabel);
    }
}
