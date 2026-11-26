using IntegratedModManager.Core.Repository;

namespace IntegratedModManager.Core.Tests;

public sealed class CommandPaletteSearchTests
{
    private static readonly CommandPaletteEntry Settings = new(CommandPaletteKind.Command, "Settings", "Open page", "nav:Settings");
    private static readonly CommandPaletteEntry Import = new(CommandPaletteKind.Command, "Import Mod", "Command", "cmd:import");
    private static readonly CommandPaletteEntry DemoA = new(CommandPaletteKind.Character, "Demo A", "Character · 2 Mods", "char:A");
    private static readonly CommandPaletteEntry Casual = new(CommandPaletteKind.Mod, "Outfit-Casual", "Demo A · Linked", "mod:A/Outfit-Casual");
    private static readonly CommandPaletteEntry Formal = new(CommandPaletteKind.Mod, "Outfit-Formal", "Demo B", "mod:B/Outfit-Formal");
    private static readonly CommandPaletteEntry Showcase = new(CommandPaletteKind.Mod, "Showcase", "Demo B", "mod:B/Showcase");
    private static readonly CommandPaletteEntry[] All = [Settings, Import, DemoA, Casual, Formal, Showcase];

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyQueryListsOnlyCommands(string? query)
        => Assert.Equal([Settings, Import], CommandPaletteSearch.Rank(All, query, 10));

    [Fact]
    public void TitlePrefixRanksAboveWordStartAboveInnerMatch()
    {
        // "cas": word start in Outfit-Casual, inside Showcase.
        Assert.Equal([Casual, Showcase], CommandPaletteSearch.Rank(All, "cas", 10));
        // "out": prefix of both outfits; ties keep the input order.
        Assert.Equal([Casual, Formal], CommandPaletteSearch.Rank(All, "OUT", 10));
    }

    [Fact]
    public void ExactTitleWinsOverPrefix()
    {
        var exact = new CommandPaletteEntry(CommandPaletteKind.Mod, "Outfit", "Demo C", "mod:C/Outfit");
        Assert.Equal(exact, CommandPaletteSearch.Rank([Casual, exact], "outfit", 10)[0]);
    }

    [Fact]
    public void EveryTermMustMatchTitleOrDetail()
    {
        Assert.Equal([Casual], CommandPaletteSearch.Rank(All, "outfit linked", 10));
        Assert.Empty(CommandPaletteSearch.Rank(All, "outfit missing", 10));
    }

    [Fact]
    public void DetailOnlyMatchesRankLast()
    {
        // "demo" matches Demo A's title, the Mods only through their detail line.
        IReadOnlyList<CommandPaletteEntry> ranked = CommandPaletteSearch.Rank(All, "demo", 10);
        Assert.Equal(DemoA, ranked[0]);
        Assert.Equal(4, ranked.Count);
    }

    [Fact]
    public void MatchesNonLatinTitles()
    {
        var chinese = new CommandPaletteEntry(CommandPaletteKind.Character, "终末地管理员", "角色", "char:zh");
        Assert.Equal([chinese], CommandPaletteSearch.Rank([Casual, chinese], "管理员", 10));
    }

    [Fact]
    public void FeaturesAreFoundByTheirOtherWordsAboveTheirLocation()
    {
        var tags = new CommandPaletteEntry(CommandPaletteKind.Feature, "Edit tags", "Mod workspace › Tags", "feature:tags", "label category", "", 2);
        var filter = new CommandPaletteEntry(CommandPaletteKind.Feature, "Filter by tag", "Mod workspace › Filters › label", "feature:filter", "", "", 2);
        // "label" is another word for tags and only in the filter's location line.
        Assert.Equal([tags, filter], CommandPaletteSearch.Rank([filter, tags], "label", 10));
        // A title match ranks above another word for it.
        var labels = new CommandPaletteEntry(CommandPaletteKind.Feature, "Label printer", "Elsewhere", "feature:labels", Area: 1);
        Assert.Equal([labels, tags, filter], CommandPaletteSearch.Rank([filter, tags, labels], "label", 10));
    }

    [Fact]
    public void EmptyQueryNeverListsFeatures()
    {
        var feature = new CommandPaletteEntry(CommandPaletteKind.Feature, "Edit tags", "Mod workspace", "feature:tags");
        Assert.Equal([Settings, Import], CommandPaletteSearch.Rank([.. All, feature], "", 10));
    }

    [Fact]
    public void DirectoryListsEveryFeatureAreaByArea()
    {
        var online = new CommandPaletteEntry(CommandPaletteKind.Feature, "Save for later", "Online", "feature:save", Area: 3);
        var paths = new CommandPaletteEntry(CommandPaletteKind.Feature, "Paths", "Dashboard", "feature:paths", Area: 1);
        var import = new CommandPaletteEntry(CommandPaletteKind.Feature, "Import", "Mod workspace", "feature:import", Area: 2);
        var newRepository = new CommandPaletteEntry(CommandPaletteKind.Feature, "New repository", "Dashboard", "feature:new", Area: 1);
        Assert.Equal([paths, newRepository, import, online],
            CommandPaletteSearch.Directory([online, Settings, paths, DemoA, import, newRepository, Casual]));
    }

    [Fact]
    public void LimitCapsTheResults()
    {
        Assert.Single(CommandPaletteSearch.Rank(All, "o", 1));
        Assert.Empty(CommandPaletteSearch.Rank(All, "o", 0));
    }
}
