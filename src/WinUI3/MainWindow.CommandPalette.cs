using Microsoft.UI.Xaml;

namespace ModFolderCopier.WinUI;

// The command palette is Controls/CommandPalette since stage J finish, step 8c; the window creates it and opens the
// workspace for a picked character or Mod.
public sealed partial class MainWindow
{
    private CommandPalette _commandPalette = null!;

    private void InitializeCommandPaletteShortcut()
    {
        _commandPalette = new CommandPalette(_services, RootGrid, TitleBarSearchBox, GetAppThemeBrush, NavigateToPrimarySection,
            () =>
            {
                WorkspacePage workspace = EnsureWorkspacePage();
                workspace.OnImportZipClicked(workspace.ImportZipButton, new RoutedEventArgs());
            },
            RefreshListsAsync, OpenDeveloperTools, ShowRepositoryItemFromPalette, ReportUiActionFailure);
        _commandPalette.InitializeShortcut();
    }

    private void OpenCommandPalette() => _commandPalette.Open();

    private bool IsCommandPaletteOpen => _commandPalette.IsOpen;

    // A picked character or Mod: the workspace, where the page clears its searches and selects it.
    private void ShowRepositoryItemFromPalette(string? characterPath, string? modPath)
    {
        // Opening the page without a rescan: the palette searched the lists already in memory, and a
        // rescan would finish later and restore the previous selection over this one.
        if (_shell.CurrentSection != PrimarySection.Repository)
        {
            _shell.CurrentSection = PrimarySection.Repository;
            ApplyShellState(refreshRepository: false);
        }
        EnsureWorkspacePage().ShowItemFromPalette(characterPath, modPath);
    }
}
