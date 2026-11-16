using IntegratedModManager.Core.Online;

namespace ModFolderCopier.WinUI;

/// <summary>
/// The sign-in of the GameBanana page the Online page opens for likes (issue #129): where it is kept, closing the
/// page if it is open, and deleting it from Settings. Only the profile folder is touched (GameBananaWeb.DeleteProfile).
/// </summary>
internal sealed class GameBananaWebSession
{
    private Func<Task>? _closeOpenPage;

    public string AppFolder { get; } = AppContext.BaseDirectory;

    public string ProfileFolder => GameBananaWeb.ProfileFolder(AppFolder);

    /// <summary>The open page registers how to close it (and wait for its browser to exit); null when it closes.</summary>
    public void SetOpenPage(Func<Task>? close) => _closeOpenPage = close;

    /// <summary>
    /// Closes the page if it is open, then deletes the saved sign-in. The page's browser can take a moment to let go of
    /// its files, so a file in use is retried for up to ten seconds before the error is passed on.
    /// Returns false when nothing was saved.
    /// </summary>
    public async Task<bool> ClearAsync()
    {
        if (_closeOpenPage is { } close) await close();
        for (int attempt = 0; ; attempt++)
        {
            try { return GameBananaWeb.DeleteProfile(AppFolder); }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && attempt < 40)
            {
                await Task.Delay(250);
            }
        }
    }
}
