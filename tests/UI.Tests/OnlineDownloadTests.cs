using System.Diagnostics;
using System.Text.Json.Nodes;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;

namespace IntegratedModManager.UI.Tests;

// Downloading and installing a Mod from the synthetic samples (IMM_ONLINE_FIXTURES serves a 2 KB sample archive; nothing
// goes to the network). Stage J finish, step 5 moves this flow out of the Online page, so it is checked both from the
// page and from the Updates page with the Online page never shown.
public sealed class OnlineDownloadTests
{
    private static AppSession Start(string section, Action<AppSession>? prepare = null)
    {
        AppSession app = AppSession.Configured((shell, _) =>
        {
            shell["CurrentPrimarySection"] = section;
            shell["Repositories"]![0]!["OnlineCategoryId"] = "90001";
            shell["Repositories"]![0]!["OnlineGameName"] = "Sample Game";
        });
        prepare?.Invoke(app);
        Environment.SetEnvironmentVariable("IMM_ONLINE_FIXTURES", Path.Combine(app.TestData, "OnlineFixtures"));
        try { app.Start(); }
        finally { Environment.SetEnvironmentVariable("IMM_ONLINE_FIXTURES", null); }
        return app;
    }

    private static string Installed(AppSession app) => Path.Combine(app.TestData, "ModRepository", "Sample Hero Aster", "Sample Mod 01");

    private static string Journal(AppSession app) => Path.Combine(app.AppFolder, "backups", "download-queue", "tasks.json");

    private static void WaitUntil(Func<bool> condition, string what)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.ElapsedMilliseconds > 20000) throw new TimeoutException(what);
            Thread.Sleep(100);
        }
    }

    private static AutomationElement Visible(AppSession app, string name)
    {
        AutomationElement? found = null;
        WaitUntil(() => (found = app.MainWindow.FindAllDescendants(cf => cf.ByName(name)).FirstOrDefault(element => !element.IsOffscreen)) is not null,
            "visible: " + name);
        return found!;
    }

    private static void AssertInstalledAndRecorded(AppSession app)
    {
        WaitUntil(() => File.Exists(Path.Combine(Installed(app), "mod.ini")), "the sample Mod installed");
        WaitUntil(() => File.Exists(Journal(app)) && JsonNode.Parse(File.ReadAllText(Journal(app)))!["Tasks"]!.AsArray()
            .Any(task => (int)task!["State"]! == 5 && (string?)task["DestinationPath"] == Installed(app)), "a completed task in the download record");
        // Sample mode logs that it is on; anything else is a real error.
        string[] errors = app.Errors().Where(line => !line.Contains("测试样例模式", StringComparison.Ordinal)).ToArray();
        Assert.True(errors.Length == 0, "The app reported:\n" + string.Join("\n", errors));
    }

    [Fact]
    public void Online_DownloadFromTheDetailsInstallsTheSample()
    {
        using AppSession app = Start("online");
        app.Resize(1400, 900);
        Mouse.Click(Visible(app, "Sample Mod 01").GetClickablePoint());
        WaitUntil(() => app.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("OnlineDetailPaneTitleTextBlock"))?.Name == "Sample Mod 01",
            "details for Sample Mod 01");

        AppSession.Invoke(app.Find("DownloadOnlineDetailButton"));
        // The download location dialog preselects the character's new folder.
        AppSession.Invoke(app.Find("PrimaryButton", TimeSpan.FromSeconds(10)));

        AssertInstalledAndRecorded(app);
    }

    // A failed download restored from the record is retried from the Updates page; the Online page is never shown.
    [Fact]
    public void Updates_RetryARestoredDownloadWithoutTheOnlinePage()
    {
        using AppSession app = Start("updates", app =>
        {
            string repository = Path.Combine(app.TestData, "ModRepository");
            string destination = Path.Combine(repository, "Sample Hero Aster");
            Directory.CreateDirectory(destination);
            var file = new JsonObject
            {
                ["FileId"] = "770000", ["FileName"] = "sample-mod-01.zip", ["DownloadUrl"] = "https://files.example.invalid/sample-990100.zip",
                ["FileSizeBytes"] = 2048, ["IsArchived"] = false, ["IsSupportedArchive"] = true, ["Version"] = "1.0"
            };
            var mod = new JsonObject
            {
                ["ItemId"] = 990100, ["Title"] = "Sample Mod 01", ["CharacterName"] = "Sample Hero Aster", ["CategoryId"] = "90011",
                ["RootCategoryName"] = "Skins", ["Author"] = "Sample Author 1", ["FileSizeBytes"] = 2048,
                ["ProfileUrl"] = "https://gamebanana.com/mods/990100", ["DownloadUrl"] = "https://files.example.invalid/sample-990100.zip",
                ["DownloadFileName"] = "sample-mod-01.zip", ["DownloadFileId"] = "770000", ["DownloadFiles"] = new JsonArray(file.DeepClone())
            };
            var task = new JsonObject
            {
                ["Id"] = "0123456789abcdef0123456789abcdef", ["Title"] = "Sample Mod 01", ["SourceIdentity"] = "GameBanana:990100",
                ["DestinationPath"] = destination, ["State"] = 6, ["Progress"] = 40, ["StatusZh"] = "失败", ["StatusEn"] = "Failed",
                ["Request"] = new JsonObject
                {
                    ["Mod"] = mod, ["File"] = file,
                    ["Context"] = new JsonObject { ["RepositoryId"] = "ui-repository", ["RepositoryPath"] = repository, ["RepositoryName"] = "UI repository" },
                    ["EndfieldMetadata"] = false, ["CharacterAliases"] = new JsonArray("Sample Hero Aster"), ["Destination"] = destination
                },
                ["Interrupted"] = false
            };
            Directory.CreateDirectory(Path.GetDirectoryName(Journal(app))!);
            File.WriteAllText(Journal(app), new JsonObject { ["Version"] = 1, ["Tasks"] = new JsonArray(task) }.ToJsonString());
        });
        AppSession.Invoke(app.Find("UpdatesViewDownloads"));
        AppSession.Invoke(app.FindByName(app.Text("DownloadTasks.Retry"), TimeSpan.FromSeconds(10)));
        // A task from the record asks before it downloads again.
        AppSession.Invoke(app.Find("PrimaryButton", TimeSpan.FromSeconds(10)));

        AssertInstalledAndRecorded(app);
        Assert.False(app.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("OnlineTitleTextBlock")) is { IsOffscreen: false },
            "The Online page should not have been shown");
    }
}
