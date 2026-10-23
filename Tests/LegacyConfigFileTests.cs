using System.Globalization;
using IntegratedModManager.Core;

namespace IntegratedModManager.Core.Tests;

public sealed class LegacyConfigFileTests
{
    private static string E(string value) => LegacyConfigFile.Encode(value);

    [Fact]
    public void Parse_ReadsEveryFieldOfAFileWrittenByEarlierVersions()
    {
        string text = string.Join("\r\n",
            "source_dir=" + E(@"D:\Mods\库"),
            "target_dir=" + E(@"D:\XXMI\Mods"),
            "launcher_path=" + E(@"D:\XXMI\Launcher.exe"),
            "theme=" + E("dark"),
            "language=" + E("en"),
            "binding=" + string.Join('\t', E(@"D:\Mods\库\A\Mod"), E("Ctrl+1"), E("切换")),
            "binding_auto=" + string.Join('\t', E(@"D:\Mods\库\A\Mod"), E("0"), E("KeySwap"), E("cycle"), E("$swap"), E("mod.ini")),
            "mod_link=" + string.Join('\t', E(@"D:\Mods\库\A\Mod"), E("https://gamebanana.com/mods/1")),
            "mod_origin=" + string.Join('\t', E(@"D:\Mods\库\A\Mod"), E("GameBanana"), E("123"), E("Title"),
                E("https://gamebanana.com/mods/123"), E("https://img/1.png"), E("2026-10-01T08:00:00.0000000+00:00"), E("9"), E("pack.zip")),
            "unknown_key=" + E("ignored"),
            "binding=not-three-fields");

        LegacyConfig config = LegacyConfigFile.Parse(text);

        Assert.Equal(@"D:\Mods\库", config.SourceDirectory);
        Assert.Equal(@"D:\XXMI\Mods", config.TargetDirectory);
        Assert.Equal(@"D:\XXMI\Launcher.exe", config.LauncherPath);
        Assert.True(config.DarkTheme);
        Assert.Equal("en", config.Language);
        ShortcutBinding binding = Assert.Single(config.Bindings[@"d:\mods\库\a\mod"]);
        Assert.Equal(("Ctrl+1", "切换"), (binding.Shortcut, binding.Action));
        Assert.Equal(("KeySwap", "cycle", "$swap", "mod.ini"), (binding.AutoSection, binding.AutoBehavior, binding.AutoTarget, binding.AutoSourceFile));
        Assert.Equal("https://gamebanana.com/mods/1", config.ModLinks[@"D:\Mods\库\A\Mod"]);
        TrackedModOrigin origin = config.ModOrigins[@"D:\Mods\库\A\Mod"];
        Assert.Equal(123, origin.ItemId);
        Assert.Equal("Title", origin.Title);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero), origin.LastKnownUpdatedAt);
        Assert.Equal(("9", "pack.zip"), (origin.DownloadFileId, origin.DownloadFileName));
    }

    [Fact]
    public void Parse_LeavesAbsentScalarsNullAndReadsDamagedFieldsAsEmpty()
    {
        LegacyConfig config = LegacyConfigFile.Parse("target_dir=%%%not-base64\nmod_origin=" + string.Join('\t', E("P"), E("GB"), E("x"), E(""), E(""), E(""), E("")));

        Assert.Null(config.SourceDirectory);
        Assert.Null(config.LauncherPath);
        Assert.Null(config.DarkTheme);
        Assert.Null(config.Language);
        Assert.Equal(string.Empty, config.TargetDirectory);
        Assert.Equal(0, config.ModOrigins["P"].ItemId);
        Assert.Null(config.ModOrigins["P"].LastKnownUpdatedAt);
        Assert.Equal(string.Empty, config.ModOrigins["P"].DownloadFileId);
    }

    [Fact]
    public void Format_WritesTheSameLayoutAsEarlierVersions()
    {
        var config = new LegacyConfig { SourceDirectory = "S", TargetDirectory = "T", LauncherPath = "", DarkTheme = false, Language = "zh" };
        config.Bindings["B"] = [new ShortcutBinding("F1", "Toggle")];
        config.ModLinks["A"] = "https://a";
        config.ModOrigins["A"] = new TrackedModOrigin { Path = "A", SourceSite = "GameBanana", ItemId = 7, Title = "t", ProfileUrl = "p" };

        string expected = string.Join(Environment.NewLine,
            "source_dir=" + E("S"),
            "target_dir=" + E("T"),
            "launcher_path=" + E(""),
            "theme=" + E("light"),
            "language=" + E("zh"),
            "binding=" + string.Join('\t', E("B"), E("F1"), E("Toggle")),
            "mod_link=" + string.Join('\t', E("A"), E("https://a")),
            "mod_origin=" + string.Join('\t', E("A"), E("GameBanana"), E("7"), E("t"), E("p"), E(""), E(""), E(""), E(""))) + Environment.NewLine;

        Assert.Equal(expected, LegacyConfigFile.Format(config));
    }

    [Fact]
    public void FormatThenParse_RoundTripsEveryField()
    {
        var config = new LegacyConfig { SourceDirectory = @"C:\库", TargetDirectory = @"C:\Mods", LauncherPath = @"C:\L.exe", DarkTheme = true, Language = "en" };
        var auto = new ShortcutBinding("Ctrl+2", "Next") { AutoSection = "KeyNext", AutoTarget = "$n", AutoBehavior = "cycle", AutoSourceFile = "a.ini" };
        config.Bindings[@"C:\库\M"] = [new ShortcutBinding("Ctrl+1", "Prev"), auto];
        config.ModOrigins[@"C:\库\M"] = new TrackedModOrigin
        {
            Path = @"C:\库\M", SourceSite = "GameBanana", ItemId = 42, Title = "名", ProfileUrl = "p", PreviewUrl = "v",
            LastKnownUpdatedAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(8)), DownloadFileId = "f", DownloadFileName = "n.zip"
        };

        LegacyConfig read = LegacyConfigFile.Parse(LegacyConfigFile.Format(config));

        Assert.Equal((config.SourceDirectory, config.TargetDirectory, config.LauncherPath, config.DarkTheme, config.Language),
            (read.SourceDirectory, read.TargetDirectory, read.LauncherPath, read.DarkTheme, read.Language));
        List<ShortcutBinding> bindings = read.Bindings[@"C:\库\M"];
        Assert.Equal(["Prev", "Next"], bindings.Select(binding => binding.Action));
        Assert.Equal("KeyNext", bindings[1].AutoSection);
        Assert.False(bindings[0].HasAutoDescription);
        TrackedModOrigin origin = read.ModOrigins[@"C:\库\M"];
        Assert.Equal((42, "名", "v", "f", "n.zip"), (origin.ItemId, origin.Title, origin.PreviewUrl, origin.DownloadFileId, origin.DownloadFileName));
        Assert.Equal(config.ModOrigins[@"C:\库\M"].LastKnownUpdatedAt, origin.LastKnownUpdatedAt);
    }

    [Fact]
    public void Format_AutoDescriptionStaysOnItsBindingWhenAnEmptyBindingIsDropped()
    {
        var config = new LegacyConfig();
        var auto = new ShortcutBinding("F2", "Swap") { AutoSection = "KeySwap", AutoTarget = "$swap" };
        config.Bindings["M"] = [new ShortcutBinding("", " "), auto];

        ShortcutBinding read = Assert.Single(LegacyConfigFile.Parse(LegacyConfigFile.Format(config)).Bindings["M"]);

        Assert.Equal("Swap", read.Action);
        Assert.Equal("KeySwap", read.AutoSection);
        Assert.Equal("$swap", read.AutoTarget);
    }

    [Fact]
    public void Format_DropsEmptyLinksAndOrdersModsByPath()
    {
        var config = new LegacyConfig();
        config.ModLinks["b"] = "https://b";
        config.ModLinks["a"] = "https://a";
        config.ModLinks["c"] = " ";

        string[] linkLines = LegacyConfigFile.Format(config)
            .Split(Environment.NewLine)
            .Where(line => line.StartsWith("mod_link=", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(["mod_link=" + string.Join('\t', E("a"), E("https://a")), "mod_link=" + string.Join('\t', E("b"), E("https://b"))], linkLines);
    }

    [Fact]
    public void FormatAndParse_DoNotDependOnTheCurrentCulture()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            var config = new LegacyConfig();
            config.ModOrigins["M"] = new TrackedModOrigin
            {
                Path = "M", ItemId = 1234567, LastKnownUpdatedAt = new DateTimeOffset(2026, 12, 31, 23, 59, 58, TimeSpan.FromHours(-5))
            };
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            string text = LegacyConfigFile.Format(config);
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");

            TrackedModOrigin read = LegacyConfigFile.Parse(text).ModOrigins["M"];

            Assert.Equal(1234567, read.ItemId);
            Assert.Equal(config.ModOrigins["M"].LastKnownUpdatedAt, read.LastKnownUpdatedAt);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData("source_dir=\n", true)]
    [InlineData("theme=bGlnaHQ=\r\nsource_dir=Uw==", true)]
    [InlineData("theme=bGlnaHQ=", false)]
    [InlineData("   ", false)]
    [InlineData("", false)]
    public void IsValid_RequiresASourceLine(string text, bool expected)
    {
        Assert.Equal(expected, LegacyConfigFile.IsValid(text));
    }
}
