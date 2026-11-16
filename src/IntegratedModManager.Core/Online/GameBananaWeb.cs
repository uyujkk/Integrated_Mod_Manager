using System.Globalization;

namespace IntegratedModManager.Core.Online;

/// <summary>
/// The GameBanana page the Online page opens inside the manager so a signed-in user can like a Mod themselves
/// (issue #129). The manager only shows the site's own page: it calls no private API, never reads or fills in account
/// details, and keeps the sign-in in its own profile folder that Settings can clear.
/// </summary>
public static class GameBananaWeb
{
    /// <summary>The embedded page's browser profile, in the app's cache folder (user data: kept by updates, never packaged).</summary>
    public const string ProfileFolderName = "gamebanana-webview2";

    /// <summary>GameBanana itself: https on the default port, no user name or password in the address, and exactly
    /// gamebanana.com or www.gamebanana.com (so not images.gamebanana.com, gamebanana.com.example or lookalike names).</summary>
    public static bool IsGameBanana(Uri? uri)
        => uri is { IsAbsoluteUri: true } && uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && uri.UserInfo.Length == 0
            && uri.IdnHost is "gamebanana.com" or "www.gamebanana.com";

    /// <summary>
    /// The Mod's own page, rebuilt as https://gamebanana.com/mods/&lt;id&gt; when the card's profile address is that
    /// Mod's page on GameBanana; null for anything else (another item, another kind of page, another site).
    /// </summary>
    public static Uri? ModPage(int itemId, string? profileUrl)
    {
        if (itemId <= 0 || !Uri.TryCreate(profileUrl, UriKind.Absolute, out Uri? url) || !IsGameBanana(url)) return null;
        string path = "/mods/" + itemId.ToString(CultureInfo.InvariantCulture);
        return string.Equals(url.AbsolutePath.TrimEnd('/'), path, StringComparison.OrdinalIgnoreCase)
            ? new Uri("https://gamebanana.com" + path)
            : null;
    }

    /// <summary>
    /// Whether the embedded page may go to this address at the top level. GameBanana's sign-in (checked 2026-11-16 on
    /// /members/account/login and /members/account/signup) is a user name, a password, an optional two-factor code and
    /// a Cloudflare Turnstile check that loads in a frame; it offers no sign-in with other sites and never leaves
    /// gamebanana.com at the top level. So only GameBanana is allowed; frames (the Turnstile check) are not top-level
    /// navigations and are not affected. Links to other sites open nowhere; the page's own button opens the browser.
    /// </summary>
    public static bool AllowsTopLevelNavigation(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out Uri? target) && IsGameBanana(target);

    /// <summary>The profile folder for an app folder (the folder that holds the window's executable).</summary>
    public static string ProfileFolder(string appFolder) => Path.Combine(appFolder, "cache", ProfileFolderName);

    /// <summary>
    /// Deletes the saved sign-in: the profile folder directly in the app's cache folder and nothing else. Links inside
    /// it are removed as links and never followed. Returns false when there was nothing to delete. A file still in use
    /// (the page's browser has not exited yet) throws <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/>,
    /// so the caller can wait and try again.
    /// </summary>
    public static bool DeleteProfile(string appFolder)
    {
        string profile = Path.GetFullPath(ProfileFolder(appFolder));
        string cache = Path.GetFullPath(Path.Combine(appFolder, "cache"));
        if (!string.Equals(Path.GetDirectoryName(profile), cache, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The profile folder must be directly in the cache folder.");
        if ((File.Exists(cache) || Directory.Exists(cache)) && (File.GetAttributes(cache) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("The cache folder is a link; the profile is not deleted through it.");
        if (File.Exists(profile)) throw new IOException("The profile path is a file.");
        if (!Directory.Exists(profile)) return false;
        if ((File.GetAttributes(profile) & FileAttributes.ReparsePoint) != 0)
        {
            Directory.Delete(profile);
            return true;
        }
        DeleteTree(new DirectoryInfo(profile));
        return true;
    }

    private static void DeleteTree(DirectoryInfo directory)
    {
        foreach (FileSystemInfo entry in directory.EnumerateFileSystemInfos())
        {
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                if (entry is DirectoryInfo) Directory.Delete(entry.FullName);
                else File.Delete(entry.FullName);
            }
            else if (entry is DirectoryInfo child)
            {
                DeleteTree(child);
            }
            else
            {
                if ((entry.Attributes & FileAttributes.ReadOnly) != 0) entry.Attributes &= ~FileAttributes.ReadOnly;
                entry.Delete();
            }
        }
        directory.Delete();
    }
}
