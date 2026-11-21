using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace IntegratedModManager.UI.Tests;

// The Settings page evens out its two columns on a wide window and keeps one column in its own order on a narrow one.
public sealed class SettingsLayoutTests
{
    // The side column's groups that may move, in their order: each one's title and last card.
    private static readonly (string Title, string LastCard)[] MovableGroups =
    [
        ("SettingsPlaceholderTitleTextBlock", "ClearGameBananaSignInButton"),
        ("DiagnosticsTitleTextBlock", "FeedbackDiscordSettingsCard"),
        ("ProjectResourcesTitleTextBlock", "AboutExpander"),
    ];

    private static void ScrollToEnd(AppSession app)
    {
        AutomationElement scroller = app.Find("SettingsScrollViewer");
        if (scroller.Patterns.Scroll.IsSupported && scroller.Patterns.Scroll.Pattern.VerticallyScrollable.Value)
            scroller.Patterns.Scroll.Pattern.SetScrollPercent(-1, 100);
        Thread.Sleep(800);
    }

    // Opening a card must not move its group to the other column (it used to jump away from under the pointer).
    [Fact]
    public void Settings_OpeningACardKeepsItsGroupInPlace()
    {
        using AppSession app = AppSession.Configured((shell, _) => shell["CurrentPrimarySection"] = "settings").Start();
        app.Find("AboutExpander", TimeSpan.FromSeconds(10));
        app.MainWindow.Patterns.Window.Pattern.SetWindowVisualState(WindowVisualState.Maximized);
        Thread.Sleep(1500);
        ScrollToEnd(app);
        var before = app.Find("AboutExpander").BoundingRectangle;
        // Opened and closed through the Expander inside the card, and checked to be open, so the test cannot pass
        // on a click that opened nothing.
        app.Expand("AboutExpander");
        Thread.Sleep(900);
        Assert.Equal(ExpandCollapseState.Expanded, AppSession.ExpandState(app.Find("AboutExpander")));
        var opened = app.Find("AboutExpander").BoundingRectangle;
        Assert.True(opened.Left == before.Left && Math.Abs(opened.Top - before.Top) < 4, $"The opened group moved: {before} -> {opened}");
        app.Collapse("AboutExpander");
        Thread.Sleep(1500);
        var closed = app.Find("AboutExpander").BoundingRectangle;
        Assert.True(closed.Left == before.Left && Math.Abs(closed.Top - before.Top) < 4, $"The closed group moved: {before} -> {closed}");
        app.AssertNoErrors();
    }

    [Fact]
    public void Settings_WideColumnsEndTogetherAndNarrowKeepsItsOrder()
    {
        using AppSession app = AppSession.Configured((shell, _) => shell["CurrentPrimarySection"] = "settings").Start();
        app.Find("AboutExpander", TimeSpan.FromSeconds(10));

        // Maximized, the main column ends far above the side column unless a side group moves under it. Which groups
        // move depends on their heights at that width (a new card or longer text changes the best choice), so this
        // checks what the balance promises rather than naming a group.
        app.MainWindow.Patterns.Window.Pattern.SetWindowVisualState(WindowVisualState.Maximized);
        Thread.Sleep(1500);
        ScrollToEnd(app);
        var releaseNotes = app.Find("ReleaseNotesExpander").BoundingRectangle;
        var groups = MovableGroups
            .Select(group => (Top: app.Find(group.Title).BoundingRectangle.Top, Card: app.Find(group.LastCard).BoundingRectangle))
            .ToArray();
        var moved = groups.Where(group => group.Card.Left == releaseNotes.Left).ToArray();
        var kept = groups.Where(group => group.Card.Left != releaseNotes.Left).ToArray();
        Assert.NotEmpty(moved);
        Assert.All(moved, group => Assert.True(group.Top > releaseNotes.Bottom, "Moved groups come after the main column's own groups"));
        Assert.True(moved.Zip(moved.Skip(1)).All(pair => pair.First.Card.Bottom < pair.Second.Top), "Moved groups keep their order");
        double mainEnd = moved.Max(group => group.Card.Bottom);
        double sideEnd = kept.Length > 0 ? kept.Max(group => group.Card.Bottom) : app.Find("OpenGitHubButton").BoundingRectangle.Bottom;
        // Without the move, the side column would end lower by the moved groups' height and the main column at the
        // release notes.
        double unbalanced = sideEnd + moved.Sum(group => group.Card.Bottom - group.Top) - releaseNotes.Bottom;
        Assert.True(Math.Abs(mainEnd - sideEnd) < unbalanced,
            $"The columns should end closer together than without the move: main {mainEnd}, side {sideEnd}, without {unbalanced}");

        // One column: every side group back in its own order, after the main column.
        app.MainWindow.Patterns.Window.Pattern.SetWindowVisualState(WindowVisualState.Normal);
        app.Resize(1000, 800);
        Thread.Sleep(1500);
        ScrollToEnd(app);
        var feedback = app.Find("FeedbackDiscordSettingsCard").BoundingRectangle;
        var about = app.Find("AboutExpander").BoundingRectangle;
        Assert.Equal(feedback.Left, about.Left);
        Assert.True(about.Top > feedback.Bottom, "The project group should come last again");
        app.AssertNoErrors();
    }
}
