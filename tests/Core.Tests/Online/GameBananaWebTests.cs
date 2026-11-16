using IntegratedModManager.Core.Deployment;
using IntegratedModManager.Core.Online;

namespace IntegratedModManager.Core.Tests;

// Issue #129: the GameBanana page the Online page opens inside the manager for likes.
public sealed class GameBananaWebTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "imm-gamebanana-web-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("https://gamebanana.com/mods/123", true)]
    [InlineData("https://www.gamebanana.com/mods/123", true)]
    [InlineData("https://GAMEBANANA.COM/members/account/login", true)]
    [InlineData("https://gamebanana.com:443/mods/1", true)]
    [InlineData("http://gamebanana.com/mods/123", false)]
    [InlineData("https://gamebanana.com:8443/mods/123", false)]
    [InlineData("https://images.gamebanana.com/img/ss/mods/1.jpg", false)]
    [InlineData("https://webfiles.gamebanana.com/js/a.js", false)]
    [InlineData("https://user@gamebanana.com/mods/123", false)]
    [InlineData("https://user:secret@gamebanana.com/mods/123", false)]
    [InlineData("https://gamebanana.com.evil.com/mods/123", false)]
    [InlineData("https://evilgamebanana.com/mods/123", false)]
    [InlineData("https://gamebanana.co/mods/123", false)]
    [InlineData("https://gamebanаna.com/mods/123", false)] // Cyrillic "а"
    [InlineData("https://accounts.google.com/o/oauth2/auth", false)]
    [InlineData("https://challenges.cloudflare.com/turnstile/v0/api.js", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("about:blank", false)]
    [InlineData("file:///C:/Windows/win.ini", false)]
    public void OnlyGameBananaItselfIsAllowedAtTheTopLevel(string url, bool allowed)
    {
        Assert.Equal(allowed, GameBananaWeb.AllowsTopLevelNavigation(url));
        Assert.Equal(allowed, Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && GameBananaWeb.IsGameBanana(uri));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("gamebanana.com/mods/1")]
    [InlineData("/mods/1")]
    public void NothingOrARelativeAddressIsNotAllowed(string? url)
    {
        Assert.False(GameBananaWeb.AllowsTopLevelNavigation(url));
        Assert.False(GameBananaWeb.IsGameBanana(url is null ? null : new Uri(url, UriKind.RelativeOrAbsolute)));
    }

    [Theory]
    [InlineData(123, "https://gamebanana.com/mods/123", "https://gamebanana.com/mods/123")]
    [InlineData(123, "https://gamebanana.com/mods/123/", "https://gamebanana.com/mods/123")]
    [InlineData(123, "https://www.gamebanana.com/mods/123?tab=files#top", "https://gamebanana.com/mods/123")]
    [InlineData(123, "https://gamebanana.com/Mods/123", "https://gamebanana.com/mods/123")]
    [InlineData(123, "https://gamebanana.com/mods/124", null)]
    [InlineData(123, "https://gamebanana.com/mods/1234", null)]
    [InlineData(123, "https://gamebanana.com/wips/123", null)]
    [InlineData(123, "https://gamebanana.com/mods/123/download", null)]
    [InlineData(123, "https://gamebanana.com/members/123", null)]
    [InlineData(123, "http://gamebanana.com/mods/123", null)]
    [InlineData(123, "https://gamebanana.com.evil.com/mods/123", null)]
    [InlineData(123, "https://user@gamebanana.com/mods/123", null)]
    [InlineData(123, "https://images.gamebanana.com/mods/123", null)]
    [InlineData(123, "", null)]
    [InlineData(123, null, null)]
    [InlineData(0, "https://gamebanana.com/mods/0", null)]
    [InlineData(-5, "https://gamebanana.com/mods/-5", null)]
    public void TheModPageIsRebuiltOnlyForThatModsOwnPage(int itemId, string? profileUrl, string? expected)
        => Assert.Equal(expected, GameBananaWeb.ModPage(itemId, profileUrl)?.AbsoluteUri);

    [Fact]
    public void TheProfileIsInTheCacheFolder()
        => Assert.Equal(Path.Combine(@"C:\IMM\WinUI3", "cache", "gamebanana-webview2"), GameBananaWeb.ProfileFolder(@"C:\IMM\WinUI3"));

    [Fact]
    public void ClearingDeletesTheProfileAndNothingElse()
    {
        string profile = GameBananaWeb.ProfileFolder(_folder);
        Directory.CreateDirectory(Path.Combine(profile, "EBWebView", "Default", "Network"));
        File.WriteAllText(Path.Combine(profile, "EBWebView", "Default", "Network", "Cookies"), "session");
        string readOnly = Path.Combine(profile, "EBWebView", "Local State");
        File.WriteAllText(readOnly, "{}");
        File.SetAttributes(readOnly, FileAttributes.ReadOnly);
        File.WriteAllText(Path.Combine(_folder, "cache", "app-index.db"), "index");
        Directory.CreateDirectory(Path.Combine(_folder, "cache", "online-categories"));

        Assert.True(GameBananaWeb.DeleteProfile(_folder));

        Assert.False(Directory.Exists(profile));
        Assert.True(File.Exists(Path.Combine(_folder, "cache", "app-index.db")));
        Assert.True(Directory.Exists(Path.Combine(_folder, "cache", "online-categories")));
        Assert.False(GameBananaWeb.DeleteProfile(_folder));
    }

    [Fact]
    public void ClearingNeverFollowsALinkInsideTheProfile()
    {
        string outside = Path.Combine(_folder, "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "keep.txt"), "keep");
        string profile = GameBananaWeb.ProfileFolder(_folder);
        Directory.CreateDirectory(profile);
        DirectoryLinkDeployment.CreateJunction(Path.Combine(profile, "linked"), outside);

        Assert.True(GameBananaWeb.DeleteProfile(_folder));

        Assert.False(Directory.Exists(profile));
        Assert.True(File.Exists(Path.Combine(outside, "keep.txt")));
    }

    [Fact]
    public void AProfileThatIsALinkIsRemovedAsALink()
    {
        string outside = Path.Combine(_folder, "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "keep.txt"), "keep");
        Directory.CreateDirectory(Path.Combine(_folder, "cache"));
        DirectoryLinkDeployment.CreateJunction(GameBananaWeb.ProfileFolder(_folder), outside);

        Assert.True(GameBananaWeb.DeleteProfile(_folder));

        Assert.False(Directory.Exists(GameBananaWeb.ProfileFolder(_folder)));
        Assert.True(File.Exists(Path.Combine(outside, "keep.txt")));
    }

    [Fact]
    public void ACacheFolderThatIsALinkIsNotEntered()
    {
        string outside = Path.Combine(_folder, "outside");
        Directory.CreateDirectory(Path.Combine(outside, "gamebanana-webview2"));
        Directory.CreateDirectory(_folder);
        DirectoryLinkDeployment.CreateJunction(Path.Combine(_folder, "cache"), outside);

        Assert.Throws<IOException>(() => GameBananaWeb.DeleteProfile(_folder));
        Assert.True(Directory.Exists(Path.Combine(outside, "gamebanana-webview2")));
    }

    [Fact]
    public void AFileInUseIsReportedSoTheCallerCanWait()
    {
        string profile = GameBananaWeb.ProfileFolder(_folder);
        Directory.CreateDirectory(profile);
        string locked = Path.Combine(profile, "Cookies");
        File.WriteAllText(locked, "session");
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.ThrowsAny<IOException>(() => GameBananaWeb.DeleteProfile(_folder));
        }
        Assert.True(GameBananaWeb.DeleteProfile(_folder));
        Assert.False(Directory.Exists(profile));
    }

    public void Dispose()
    {
        if (!Directory.Exists(_folder)) return;
        foreach (string link in Directory.EnumerateDirectories(_folder, "*", SearchOption.AllDirectories)
                     .Where(path => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0).ToArray())
            Directory.Delete(link);
        Directory.Delete(_folder, recursive: true);
    }
}
