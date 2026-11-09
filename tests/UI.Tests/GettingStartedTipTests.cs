using System.Drawing;
using FlaUI.Core.AutomationElements;

namespace IntegratedModManager.UI.Tests;

// The getting-started tips of a first install (J4-b) must stay inside the window at every step: in each language,
// at 1000 x 800, 1400 x 900 and maximized, with the path settings folded (all paths set) or opened (a path missing).
public sealed class GettingStartedTipTests
{
    public static TheoryData<string, int, int, bool> Cases()
    {
        var data = new TheoryData<string, int, int, bool>();
        foreach (string language in new[] { "zh-CN", "en-US", "ru-RU" })
            foreach (bool expanded in new[] { false, true })
                data.Add(language, 1000, 800, expanded);
        foreach (bool expanded in new[] { false, true })
        {
            data.Add("ru-RU", 1400, 900, expanded);
            data.Add("ru-RU", 0, 0, expanded);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void GettingStarted_EveryTipStaysInsideTheWindow(string language, int width, int height, bool pathsExpanded)
    {
        using AppSession app = AppSession.Configured((shell, session) =>
        {
            shell["GettingStartedTipsPending"] = true;
            // A missing game folder opens the path settings by themselves; with every path set they stay folded.
            if (pathsExpanded) shell["Repositories"]![0]!["TargetPath"] = Path.Combine(session.TestData, "Loader", "NoSuchFolder");
        });
        static string Encode(string value) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value));
        string config = Path.Combine(app.AppFolder, "config.ini");
        string code = language switch { "zh-CN" => "zh", "en-US" => "en", _ => language };
        File.WriteAllLines(config, File.ReadAllLines(config).Select(line => line.StartsWith("language=") ? "language=" + Encode(code) : line));
        app.Start();
        SetSize(app, width, height);
        WalkTheTips(app, $"{language}, {width}x{height}, paths {(pathsExpanded ? "open" : "folded")}");
    }

    // The user's case: a real first install, after choosing a game or "later". The dashboard scrolls to the path
    // settings at step 2, which used to open the tip beside them and outside the window.
    [Theory]
    [InlineData(true, 1000, 800)]
    [InlineData(true, 0, 0)]
    [InlineData(false, 1400, 900)]
    public void GettingStarted_FirstInstallTipsStayInsideTheWindow(bool chooseGame, int width, int height)
    {
        using AppSession app = AppSession.FirstInstall().Start();
        if (chooseGame)
        {
            AppSession.Invoke(app.Find("FirstRunGame_genshin", TimeSpan.FromSeconds(20)));
            AppSession.Invoke(app.FindByName(app.Text("FirstRun.Use")));
        }
        else
        {
            AppSession.Invoke(app.FindByName(app.Text("FirstRun.Later"), TimeSpan.FromSeconds(20)));
        }
        SetSize(app, width, height);
        WalkTheTips(app, $"first install ({(chooseGame ? "game" : "later")}), {width}x{height}");
    }

    private static void SetSize(AppSession app, int width, int height)
    {
        if (width == 0) app.MainWindow.Patterns.Window.Pattern.SetWindowVisualState(FlaUI.Core.Definitions.WindowVisualState.Maximized);
        else app.Resize(width, height);
    }

    // Each step's text, Skip and Next buttons span the tip's content: all must lie inside the window.
    private static void WalkTheTips(AppSession app, string context)
    {
        for (int step = 1; step <= 3; step++)
        {
            AutomationElement next = app.Find("GettingStartedNextButton", TimeSpan.FromSeconds(20));
            Thread.Sleep(1200);
            Rectangle window = app.MainWindow.BoundingRectangle;
            Rectangle tip = Rectangle.Empty;
            foreach (string id in new[] { "GettingStartedStepTextBlock", "GettingStartedSkipButton", "GettingStartedNextButton" })
            {
                Rectangle part = app.Find(id).BoundingRectangle;
                Assert.True(!part.IsEmpty && window.Contains(part),
                    $"Step {step} ({context}): {id} at {part} is not inside the window {window}");
                tip = tip.IsEmpty ? part : Rectangle.Union(tip, part);
            }
            if (step == 2)
            {
                // Pointing at the path settings: right above or below them, not over them; or, when there is no room,
                // in the middle of the window pointing at nothing.
                Rectangle paths = app.Find("DashboardPathsExpander").BoundingRectangle;
                int gap = tip.Bottom <= paths.Top ? paths.Top - tip.Bottom : tip.Top >= paths.Bottom ? tip.Top - paths.Bottom : -1;
                bool centred = Math.Abs(tip.Left + tip.Width / 2 - (window.Left + window.Width / 2)) < 120;
                Assert.True(gap is >= 0 and < 260 || centred,
                    $"Step 2 ({context}): the tip {tip} is not next to the path settings {paths}");
            }
            AppSession.Invoke(next);
        }
        Assert.True(app.IsRunning);
        app.AssertNoErrors();
    }
}
