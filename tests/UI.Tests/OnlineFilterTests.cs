using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using static IntegratedModManager.UI.Tests.OnlineTests;

namespace IntegratedModManager.UI.Tests;

// The Online page's filter row, toolbar buttons and category bar on the synthetic samples (IMM_ONLINE_FIXTURES):
// every filter in use shows as a removable chip above the results, "Clear all" resets them all, the buttons say what
// they do, and a narrow page folds the top-level categories into a drop-down.
public sealed class OnlineFilterTests
{
    private static string Format(AppSession app, string key, params object[] values) => string.Format(app.Text(key), values);

    private static string ChipName(AppSession app, string label) => Format(app, "OnlineFilters.Remove", label);

    private static AutomationElement? Shown(AppSession app, string automationId)
        => app.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(automationId)) is { IsOffscreen: false } element ? element : null;

    private static AutomationElement Subcategory(AppSession app, string name) => AppSession.FindIn(
        app.FindByName(app.Text("OnlineCategories.SubBarName")), cf => cf.ByName(name), name);

    private static void ChooseSort(AppSession app, int index)
    {
        // The box is disabled while a list loads, and a new category can load twice (its subcategories arrive later).
        WaitUntil(() =>
        {
            try { app.Find("OnlineSortComboBox").AsComboBox().Select(index); return true; }
            catch (FlaUI.Core.Exceptions.ElementNotEnabledException) { Thread.Sleep(300); return false; }
        }, "the sort box enabled");
        Thread.Sleep(600);
    }

    private static void ClickMenuItem(AppSession app, AutomationElement opener, string name)
    {
        Mouse.Click(opener.GetClickablePoint());
        Mouse.Click(app.FindByName(name).GetClickablePoint());
        Mouse.MoveTo(new System.Drawing.Point(5, 5));
        Thread.Sleep(600);
    }

    [Fact]
    public void Online_ChipsShowWhatNarrowsTheListAndEachRemovesOnlyItsOwnFilter()
    {
        using AppSession app = StartOnline();
        app.Resize(1400, 900);
        Visible(app, "Sample Mod 01");
        // The default list: nothing to show, nothing to clear.
        Assert.Null(Shown(app, "OnlineActiveFiltersPanel"));
        Assert.Null(Shown(app, "ClearOnlineFiltersButton"));

        AppSession.Invoke(Subcategory(app, "Weapons"));
        Visible(app, "Sample Blade");
        app.Find("OnlineSearchTextBox").AsTextBox().Text = "Blade";
        string category = Format(app, "OnlineFilters.Category", Format(app, "OnlineFilters.CategoryPath", "Skins", "Weapons"));
        string search = Format(app, "OnlineFilters.Search", "Blade");
        WaitUntil(() => Shown(app, "OnlineFilterChip_Category")?.Name == ChipName(app, category)
            && Shown(app, "OnlineFilterChip_Search")?.Name == ChipName(app, search), "the category and search chips");
        Assert.Equal(Format(app, "OnlineLibrary.ThisPageMatches", 1, 2), app.Find("OnlineFilterSummaryTextBlock").Name);
        Assert.NotNull(Shown(app, "ClearOnlineFiltersButton"));

        // The search chip clears the search box and nothing else.
        AppSession.Invoke(app.Find("OnlineFilterChip_Search"));
        WaitUntil(() => Shown(app, "OnlineFilterChip_Search") is null, "the search chip gone");
        Assert.Equal(string.Empty, app.Find("OnlineSearchTextBox").AsTextBox().Text);
        Assert.NotNull(Shown(app, "OnlineFilterChip_Category"));
        Visible(app, "Sample Blade");

        // The category chip goes back to the default list, and the row goes with it.
        AppSession.Invoke(app.Find("OnlineFilterChip_Category"));
        Visible(app, "Sample Mod 01");
        WaitUntil(() => Shown(app, "OnlineActiveFiltersPanel") is null, "no filters left");
        AssertNoErrorsBesidesSampleMode(app);
    }

    [Fact]
    public void Online_ClearAllResetsCategorySortAndShowFilter()
    {
        using AppSession app = StartOnline();
        app.Resize(1400, 900);
        Visible(app, "Sample Mod 01");

        AppSession.Invoke(Visible(app, "UI"));
        Visible(app, "Sample Menu");
        ChooseSort(app, 1); // by downloads
        ClickMenuItem(app, app.Find("OnlineWorkFilterButton"), app.Text("AuthorWorkspace.InstalledOnThisPage"));

        string installed = app.Text("OnlineLibrary.InstalledOnPage");
        WaitUntil(() => Shown(app, "OnlineFilterChip_Category") is not null && Shown(app, "OnlineFilterChip_Sort") is not null
            && Shown(app, "OnlineFilterChip_Show") is not null, "category, sort and show chips");
        Assert.Equal(ChipName(app, Format(app, "OnlineFilters.Category", "UI")), app.Find("OnlineFilterChip_Category").Name);
        Assert.Equal(ChipName(app, Format(app, "OnlineFilters.Sort", app.Text("OnlineFeed.SortByDownloads"))), app.Find("OnlineFilterChip_Sort").Name);
        Assert.Equal(ChipName(app, Format(app, "OnlineFilters.ShowOnly", installed)), app.Find("OnlineFilterChip_Show").Name);
        Assert.Equal(Format(app, "OnlineFilters.ShowLabel", installed), app.Find("OnlineWorkFilterButton").Name);

        AutomationElement clear = app.Find("ClearOnlineFiltersButton");
        Assert.Equal(app.Text("OnlineFilters.ClearAll"), clear.Name);
        Assert.Equal(app.Text("OnlineFilters.ClearAllHint"), clear.HelpText);
        AppSession.Invoke(clear);

        // Back to the default category, sort and Show filter, with the row gone.
        Visible(app, "Sample Mod 01");
        WaitUntil(() => Shown(app, "OnlineActiveFiltersPanel") is null, "no filters left");
        Assert.Equal(app.Text("OnlineFeed.SortByHotness"), app.Find("OnlineSortComboBox").AsComboBox().SelectedItem?.Name);
        Assert.Equal(Format(app, "OnlineFilters.ShowLabel", app.Text("OnlineLibrary.AllOnPage")), app.Find("OnlineWorkFilterButton").Name);
        Assert.Null(app.MainWindow.FindFirstDescendant(cf => cf.ByName("Sample Menu")));
        // The remembered category is the default one again.
        WaitUntil(() => app.TryShellConfig()?["OnlinePreferences"]?["CategorySelections"]?.ToJsonString() is { } saved
            && !saved.Contains("90200", StringComparison.Ordinal), "the default category remembered");
        AssertNoErrorsBesidesSampleMode(app);
    }

    [Fact]
    public void Online_ToolbarButtonsSayWhatTheyDo()
    {
        using AppSession app = StartOnline();
        app.Resize(1400, 900);
        Visible(app, "Sample Mod 01");

        string all = app.Text("OnlineLibrary.AllOnPage");
        AutomationElement show = app.Find("OnlineWorkFilterButton");
        Assert.Equal(Format(app, "OnlineFilters.ShowLabel", all), show.Name);
        Assert.Equal(Format(app, "OnlineFilters.ShowHint", all), show.HelpText);

        AutomationElement saved = app.Find("OnlineSavedItemsButton");
        Assert.Equal(app.Text("OnlineLibrary.AuthorsAndSavedMods"), saved.Name);
        Assert.Equal(app.Text("OnlineLibrary.AuthorsAndSavedModsFindAuthors"), saved.HelpText);
        AutomationElement options = app.Find("OnlineDownloadOptionsButton");
        Assert.Equal(app.Text("Common.DownloadOptions"), options.Name);
        Assert.Equal(app.Text("OnlineFilters.DownloadOptionsHint"), options.HelpText);
        // Wide enough: both show their labels beside the icons.
        Assert.NotNull(AppSession.FindIn(saved, cf => cf.ByName(app.Text("OnlineFilters.SavedLabel")), "saved label"));
        Assert.NotNull(AppSession.FindIn(options, cf => cf.ByName(app.Text("Common.DownloadOptions")), "options label"));

        Assert.Equal(Format(app, "OnlineFilters.SortHint", app.Text("OnlineFeed.SortByHotness")), app.Find("OnlineSortComboBox").HelpText);
        Assert.Equal(app.Text("Localization.RefreshOnlineModList"), app.Find("RefreshOnlineButton").HelpText);
        Assert.Equal(app.Text("Localization.PreviousPage"), app.Find("OnlinePrevPageButton").HelpText);
        Assert.Equal(app.Text("Localization.NextPage"), app.Find("OnlineNextPageButton").HelpText);
        Assert.False(string.IsNullOrEmpty(app.Find("OnlineListLayoutButton").HelpText));
        Assert.False(string.IsNullOrEmpty(app.Find("OnlineGridLayoutButton").HelpText));

        // Narrow: the labels go, and the names and tooltips stay.
        app.Resize(670, 530);
        WaitUntil(() => app.Find("OnlineSavedItemsButton").FindFirstDescendant(cf => cf.ByName(app.Text("OnlineFilters.SavedLabel"))) is null
            or { IsOffscreen: true }, "icon-only saved button");
        Assert.Equal(app.Text("OnlineLibrary.AuthorsAndSavedMods"), app.Find("OnlineSavedItemsButton").Name);
        Assert.Equal(app.Text("OnlineFilters.DownloadOptionsHint"), app.Find("OnlineDownloadOptionsButton").HelpText);
        AssertNoErrorsBesidesSampleMode(app);
    }

    [Fact]
    public void Online_NarrowWindowFoldsTopCategoriesIntoTheDropDown()
    {
        using AppSession app = StartOnline();
        app.Resize(1400, 900);
        Visible(app, "Sample Mod 01");
        Assert.Null(Shown(app, "OnlineCategoryDropDownButton"));
        Assert.NotNull(Shown(app, "OnlineCategorySelectorBar"));

        app.Resize(670, 530);
        WaitUntil(() => Shown(app, "OnlineCategoryDropDownButton") is not null && Shown(app, "OnlineCategorySelectorBar") is null,
            "the category drop-down instead of the tabs");
        AutomationElement dropDown = app.Find("OnlineCategoryDropDownButton");
        Assert.Equal(Format(app, "OnlineFilters.Category", "Skins"), dropDown.Name);
        Assert.Equal(Format(app, "OnlineFilters.CategoryHint", "Skins"), dropDown.HelpText);
        // The subcategory chips stay below.
        Assert.NotNull(Shown(app, "OnlineSubcategoryScrollViewer"));

        ClickMenuItem(app, dropDown, "UI");
        Visible(app, "Sample HUD");
        WaitUntil(() => app.Find("OnlineCategoryDropDownButton").Name == Format(app, "OnlineFilters.Category", "UI"), "UI chosen");
        Assert.NotNull(Shown(app, "OnlineFilterChip_Category"));

        // Wide again: the tabs come back with UI chosen.
        app.Resize(1400, 900);
        WaitUntil(() => Shown(app, "OnlineCategorySelectorBar") is not null && Shown(app, "OnlineCategoryDropDownButton") is null, "the tabs again");
        Assert.True(Visible(app, "UI").Patterns.SelectionItem.Pattern.IsSelected.Value);
        AssertNoErrorsBesidesSampleMode(app);
    }
}
