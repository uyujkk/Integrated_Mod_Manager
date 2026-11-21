using System.Text.Json.Nodes;
using IntegratedModManager.Core.Deployment;
using IntegratedModManager.Core.DeveloperTools;
using IntegratedModManager.Core.Online;

namespace IntegratedModManager.Core.Tests.DeveloperTools;

public sealed class DeveloperToolsCoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "imm-devtools-" + Guid.NewGuid().ToString("N"));

    public DeveloperToolsCoreTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { IntegratedModManager.Core.Infrastructure.SafeDirectoryTree.Delete(_root); }
        catch (IOException) { }
    }

    private string Folder(params string[] parts)
    {
        string path = Path.Combine([_root, .. parts]);
        Directory.CreateDirectory(path);
        return path;
    }

    // ---------------------------------------------------------------- location

    [Fact]
    public void Location_IsTheDeveloperToolsFolderBesideTheLauncher()
    {
        Assert.Null(DeveloperToolsLocation.Find(_root));
        string expected = Path.Combine(_root, "DeveloperTools", "IntegratedModManager.DeveloperTools.exe");
        Assert.Equal(expected, DeveloperToolsLocation.ExpectedPath(_root));
        Folder("DeveloperTools");
        File.WriteAllText(expected, "");
        Assert.Equal(expected, DeveloperToolsLocation.Find(_root));
        Assert.Null(DeveloperToolsLocation.Find(""));
    }

    [Fact]
    public void Location_FindsTheAppFolderOfAnInstall()
    {
        Assert.Null(DeveloperToolsLocation.AppFolderForInstallRoot(_root));
        string app = Folder("WinUI3");
        Assert.Equal(app, DeveloperToolsLocation.AppFolderForInstallRoot(_root));
    }

    // ---------------------------------------------------------------- audit

    [Fact]
    public void Audit_ReportsRecordsSlotsAndLinksThatPointNowhere()
    {
        string source = Folder("Repo");
        string target = Folder("Loader");
        Folder("Repo", "Char", "ModA");
        Folder("Loader", "ModA");
        string gone = Folder("Repo", "Char", "Gone");
        DirectoryLinkDeployment.CreateJunction(Path.Combine(target, "Broken"), gone);
        Directory.Delete(gone);
        string previews = Folder("previews");
        File.WriteAllText(Path.Combine(previews, "kept.jpg"), "x");

        var repositories = new[] { new AuditRepository("r1", "Repo", source, target) };
        var records = new List<CopyDeploymentRecord>
        {
            new() { RepositoryId = "r1", SourceRelativePath = Path.Combine("Char", "ModA"), DeployedPath = Path.Combine(target, "ModA") },
            new() { RepositoryId = "r1", SourceRelativePath = Path.Combine("Char", "ModB"), DeployedPath = Path.Combine(target, "ModB") },
            new() { RepositoryId = "r1", SourceRelativePath = "x", DeployedPath = Path.Combine(_root, "Elsewhere") },
            new() { RepositoryId = "old", SourceRelativePath = "y", DeployedPath = Path.Combine(target, "Y") }
        };
        var slots = new List<AuditSlot>
        {
            new("r1", Path.Combine("Char", "ModA"), "Battle", "kept.jpg"),
            new("r1", Path.Combine("Char", "ModA"), "Casual", "lost.jpg"),
            new("r1", Path.Combine("Char", "Missing"), "Old", null),
            new("old", "z", "Orphan", null)
        };

        AuditResult result = StateRecordAudit.Run(repositories, records, slots, previews);

        Assert.Equal(4, result.Records);
        Assert.Equal(4, result.Slots);
        Assert.Equal(1, result.Links);
        AuditFindingKind[] kinds = [.. result.Findings.Select(finding => finding.Kind)];
        Assert.Contains(AuditFindingKind.RecordFolderMissing, kinds);   // ModB was never there
        Assert.Contains(AuditFindingKind.RecordSourceMissing, kinds);   // Char\ModB
        Assert.Contains(AuditFindingKind.RecordOutsideTarget, kinds);   // Elsewhere
        Assert.Contains(AuditFindingKind.RecordForUnknownRepository, kinds);
        Assert.Contains(AuditFindingKind.SlotPreviewMissing, kinds);
        Assert.Contains(AuditFindingKind.SlotModMissing, kinds);
        Assert.Contains(AuditFindingKind.SlotForUnknownRepository, kinds);
        Assert.Contains(AuditFindingKind.BrokenLink, kinds);
        // The healthy record, slot and picture are not reported.
        Assert.DoesNotContain(result.Findings, finding => finding.Path.EndsWith("kept.jpg", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Findings, finding => finding.Kind == AuditFindingKind.RecordFolderMissing && finding.Path.EndsWith("ModA", StringComparison.Ordinal));
    }

    [Fact]
    public void Audit_NeverResolvesAStoredPathOutsideItsRoot()
    {
        string source = Folder("Repo");
        AuditResult result = StateRecordAudit.Run(
            [new AuditRepository("r1", "Repo", source, Folder("Loader"))], [],
            [new AuditSlot("r1", Path.Combine("..", "..", "Windows"), "Escape", Path.Combine("..", "secret.txt"))], Folder("previews"));
        Assert.Empty(result.Findings);
    }

    // ---------------------------------------------------------------- logs

    [Fact]
    public void Logs_GroupContinuationLinesAndClassifyLevels()
    {
        string[] lines =
        [
            "2026-11-16T10:00:00.0000000-05:00 Window created",
            "2026-11-16T10:00:01.0000000-05:00 Online refresh failed: timeout",
            "    at Something()",
            "    at Else()",
            "2026-11-16T10:00:02.0000000-05:00 Low disk space warning",
            "orphan line"
        ];
        List<LogEntry> entries = LogReader.Parse("startup.log", lines, LogLevel.Info);
        Assert.Equal(3, entries.Count);
        Assert.Equal(LogLevel.Info, entries[0].Level);
        Assert.Equal(LogLevel.Error, entries[1].Level);
        Assert.Contains("at Else()", entries[1].Text);
        Assert.Equal(LogLevel.Warning, entries[2].Level);
        Assert.Contains("orphan line", entries[2].Text);

        Assert.Single(LogReader.Filter(entries, LogLevel.Error, null, null));
        Assert.Equal(2, LogReader.Filter(entries, LogLevel.Info, new DateTimeOffset(2026, 11, 16, 10, 0, 1, TimeSpan.FromHours(-5)), null).Count());
        Assert.Single(LogReader.Filter(entries, LogLevel.Info, null, "WINDOW"));
        Assert.All(LogReader.Parse("app-errors.log", ["2026-11-16T10:00:00Z plain"], LogLevel.Error), entry => Assert.Equal(LogLevel.Error, entry.Level));
    }

    [Fact]
    public void Logs_MergeInTimeOrderAndReadOnlyTheTail()
    {
        List<LogEntry> a = LogReader.Parse("a", ["2026-11-16T10:00:02Z second", "2026-11-16T10:00:04Z fourth"], LogLevel.Info);
        List<LogEntry> b = LogReader.Parse("b", ["2026-11-16T10:00:01Z first", "2026-11-16T10:00:03Z third"], LogLevel.Info);
        Assert.Equal(["first", "second", "third", "fourth"], LogReader.Merge([a, b]).Select(entry => entry.Text));

        string path = Path.Combine(_root, "big.log");
        File.WriteAllLines(path, Enumerable.Range(0, 1000).Select(i => $"2026-11-16T10:00:00Z line {i:D4}"));
        string[] tail = LogReader.ReadTail(path, 200);
        Assert.True(tail.Length < 20);
        Assert.StartsWith("2026-11-16T10:00:00Z line", tail[0]);
        Assert.Contains(tail, line => line.EndsWith("line 0999", StringComparison.Ordinal));
        Assert.Empty(LogReader.ReadTail(Path.Combine(_root, "none.log")));
    }

    // ---------------------------------------------------------------- caches

    [Fact]
    public void Cache_ListsSizesAndKeepsWhatHoldsUserData()
    {
        string app = Folder("app");
        File.WriteAllText(Path.Combine(Folder("app", "cache", "online-images"), "a.png"), new string('x', 100));
        File.WriteAllText(Path.Combine(app, "cache", "app-index.db"), "db");
        File.WriteAllText(Path.Combine(Folder("app", "cache", "something-new"), "f"), "x");

        IReadOnlyList<CacheEntry> entries = CacheInventory.Read(app);
        CacheEntry images = entries.Single(entry => entry.Key == "online-images");
        Assert.Equal((CacheKind.Regenerable, 100L, 1), (images.Kind, images.Bytes, images.Files));
        Assert.Equal(CacheKind.Kept, entries.Single(entry => entry.Key == "app-index.db").Kind);
        Assert.Equal(CacheKind.Kept, entries.Single(entry => entry.Key == "something-new").Kind);
        Assert.Equal(CacheKind.GameBananaProfile, entries.Single(entry => entry.Key == GameBananaWeb.ProfileFolderName).Kind);
    }

    [Fact]
    public void Cache_ClearsOnlyRegenerableFoldersWhileTheAppIsClosed()
    {
        string app = Folder("app");
        string images = Folder("app", "cache", "online-images");
        File.WriteAllText(Path.Combine(images, "a.png"), "x");
        File.WriteAllText(Path.Combine(app, "cache", "app-index.db"), "db");

        Assert.Equal(CacheClearResult.AppRunning, CacheInventory.Clear(app, "online-images", appRunning: true));
        Assert.True(File.Exists(Path.Combine(images, "a.png")));
        Assert.Equal(CacheClearResult.NotClearable, CacheInventory.Clear(app, "app-index.db", appRunning: false));
        Assert.Equal(CacheClearResult.NotClearable, CacheInventory.Clear(app, "..\\..\\anything", appRunning: false));
        Assert.Equal(CacheClearResult.Cleared, CacheInventory.Clear(app, "online-images", appRunning: false));
        Assert.False(Directory.Exists(images));
        Assert.True(File.Exists(Path.Combine(app, "cache", "app-index.db")));
        Assert.Equal(CacheClearResult.NothingToClear, CacheInventory.Clear(app, "online-pages", appRunning: false));
    }

    [Fact]
    public void Cache_RefusesALinkedCacheEntryAndNeverTouchesItsTarget()
    {
        string app = Folder("app");
        Folder("app", "cache");
        string outside = Folder("outside");
        File.WriteAllText(Path.Combine(outside, "keep.txt"), "x");
        DirectoryLinkDeployment.CreateJunction(Path.Combine(app, "cache", "online-images"), outside);

        Assert.True(CacheInventory.Read(app).Single(entry => entry.Key == "online-images").IsLink);
        Assert.Equal(CacheClearResult.Link, CacheInventory.Clear(app, "online-images", appRunning: false));
        Assert.True(File.Exists(Path.Combine(outside, "keep.txt")));
    }

    [Fact]
    public void Cache_ClearsTheGameBananaProfileTheAppsWay()
    {
        string app = Folder("app");
        string profile = Folder("app", "cache", GameBananaWeb.ProfileFolderName, "Default");
        File.WriteAllText(Path.Combine(profile, "Cookies"), "x");
        Assert.Equal(CacheClearResult.AppRunning, CacheInventory.Clear(app, GameBananaWeb.ProfileFolderName, appRunning: true));
        Assert.Equal(CacheClearResult.Cleared, CacheInventory.Clear(app, GameBananaWeb.ProfileFolderName, appRunning: false));
        Assert.False(Directory.Exists(GameBananaWeb.ProfileFolder(app)));
    }

    // ---------------------------------------------------------------- languages

    [Fact]
    public void Languages_ReportMissingExtraPlaceholderAndEmptyTexts()
    {
        var languages = new Dictionary<string, Dictionary<string, string>>
        {
            ["en-US"] = new() { ["A"] = "Hello {0}", ["B"] = "Bye", ["C"] = "{0} of {1:N0}" },
            ["zh-CN"] = new() { ["A"] = "你好 {0}", ["B"] = "再见", ["C"] = "{1:N0} 中的 {0}" },
            ["ru-RU"] = new() { ["A"] = "Привет {2}", ["C"] = "", ["D"] = "лишний" }
        };
        LanguageComparison result = LanguageFileComparer.Compare(languages);

        LanguageFileSummary zh = result.Files.Single(file => file.Language == "zh-CN");
        Assert.Equal((0, 0, 0, 0), (zh.Missing, zh.Extra, zh.Placeholders, zh.Empty));
        LanguageFileSummary ru = result.Files.Single(file => file.Language == "ru-RU");
        // "{2}" cannot be filled; leaving placeholders out (C) is allowed, as the zh-CN {1}-before-{0} order is.
        Assert.Equal((1, 1, 1, 1), (ru.Missing, ru.Extra, ru.Placeholders, ru.Empty));
        Assert.Contains(result.Issues, issue => issue is { Language: "ru-RU", Kind: LanguageIssueKind.Missing, Key: "B" });
        Assert.Contains(result.Issues, issue => issue is { Language: "ru-RU", Kind: LanguageIssueKind.Extra, Key: "D" });
        Assert.Equal("{0} {1}", LanguageFileComparer.Placeholders("{1:N0} and {0} and {0}"));
        Assert.Throws<ArgumentException>(() => LanguageFileComparer.Compare(languages, "fr-FR"));
    }

    [Fact]
    public void Languages_TheAppsOwnFilesAgreeWithEnglish()
    {
        string strings = Path.Combine(RepositoryRoot(), "src", "WinUI3", "Strings");
        LanguageComparison result = LanguageFileComparer.Compare(LanguageFileComparer.ReadFolder(strings));
        Assert.DoesNotContain(result.Issues, issue => issue.Kind is LanguageIssueKind.Placeholders or LanguageIssueKind.Extra);
    }

    // ---------------------------------------------------------------- summary

    [Fact]
    public void Summary_CountsWithoutNamesValuesOrPaths()
    {
        JsonNode shell = JsonNode.Parse("""
            {
              "Repositories": [{ "Name": "Secret repo", "SourcePath": "C:\\Users\\me\\Mods" }],
              "CopyDeployments": [{ "Origin": "copied", "DeployedPath": "C:\\Users\\me\\Loader\\A" }, { "Origin": "claimed" }],
              "ModPersistentSlots": [{ "Name": "My slot", "IsModDefault": true, "PreviewFile": "p.jpg" }, { "Name": "Other" }],
              "ConfigurationProfiles": [{ "Name": "Preset name" }],
              "ShortcutOverlayOpacity": 80,
              "OverlayCompactView": true,
              "ShortcutOverlayTextColor": "#FFFFFF",
              "OverlayPlacements": [{ "Display": "\\\\.\\DISPLAY1|3840x2160" }]
            }
            """)!;
        string text = DiagnosticSummary.Build(shell, [new CacheEntry("online-images", "C:\\Users\\me\\cache", CacheKind.Regenerable, true, false, 2048, 3)]);

        Assert.Contains("Copy records: 2 (claimed: 1, copied: 1)", text);
        Assert.Contains("State slots: 2 (Default slots: 1, with a picture: 1)", text);
        Assert.Contains("ShortcutOverlayOpacity: 80", text);
        Assert.Contains("OverlayCompactView: on", text);
        Assert.Contains("OverlayPlacements: 1 display(s)", text);
        Assert.Contains("online-images: 2048 bytes, 3 files", text);
        foreach (string secret in new[] { "Secret repo", "My slot", "Preset name", "Users", "DISPLAY1", "#FFFFFF" })
            Assert.DoesNotContain(secret, text);
    }

    private static string RepositoryRoot()
    {
        string? folder = AppContext.BaseDirectory;
        while (folder is not null && !File.Exists(Path.Combine(folder, "IntegratedModManager.sln")))
            folder = Path.GetDirectoryName(folder);
        return folder ?? throw new InvalidOperationException("Repository root not found.");
    }
}
