using IntegratedModManager.Core.Presets;

namespace IntegratedModManager.Core.Tests;

public sealed class PresetShareServiceTests
{
    private static readonly ModPersistentValue Swap = new("mod.ini", "$swap", "$\\mods\\skin\\mod.ini\\swap", "2");

    private static string Shared(bool includesState = true) => PresetShareService.Serialize("Evening", "Genshin Impact", includesState,
        [new CombinationPresetState("Alpha\\Skin", [Swap]), new CombinationPresetState("Beta\\Hair", [])]);

    [Fact]
    public void SerializeAndParse_RoundTripWithoutFiles()
    {
        PresetShareFile file = PresetShareService.Parse(Shared());

        Assert.Equal(("Evening", "Genshin Impact", true), (file.Name, file.Game, file.IncludesPersistentState));
        Assert.Equal(["Alpha/Skin", "Beta/Hair"], file.Mods.Select(mod => mod.RelativePath));
        Assert.Equal(Swap, Assert.Single(file.Mods[0].Values));
    }

    [Fact]
    public void Serialize_SetOnlyDropsValues() => Assert.All(PresetShareService.Parse(Shared(false)).Mods, mod => Assert.Empty(mod.Values));

    [Theory]
    [InlineData("\"alpha/skin\"", "\"../skin\"")]
    [InlineData("\"alpha/skin\"", "\"Alpha/Skin/Extra\"")]
    [InlineData("\"$swap\"", "\"swap\"")]
    [InlineData("\"2\"", "\"1e999\"")]
    [InlineData("\"mod.ini\"", "\"..\\\\other.ini\"")]
    [InlineData("\"mod.ini\"", "\"mod.txt\"")]
    [InlineData("\"version\": 1", "\"version\": 2")]
    [InlineData("\"name\": \"Evening\"", "\"name\": \"Evening\", \"extra\": 1")]
    public void Parse_RejectsUnsafeOrUnknownContent(string find, string replace)
    {
        string json = Shared().Replace(find.Replace("alpha/skin", "Alpha/Skin"), replace);
        Assert.Throws<InvalidDataException>(() => PresetShareService.Parse(json));
    }

    [Fact]
    public void Parse_RejectsDuplicateProperties()
        => Assert.Throws<InvalidDataException>(() => PresetShareService.Parse(Shared().Replace("\"name\": \"Evening\"", "\"name\": \"Evening\", \"name\": \"Other\"")));

    [Fact]
    public void Parse_RejectsBundlesAndJunk()
    {
        Assert.Throws<InvalidDataException>(() => PresetShareService.Parse(Shared().Replace(PresetShareService.Format, CombinationBundleService.Format)));
        Assert.Throws<InvalidDataException>(() => PresetShareService.Parse("not json"));
    }

    [Fact]
    public void Preview_ReportsMatchesMissingAndGame()
    {
        PresetShareFile file = PresetShareService.Parse(Shared());

        PresetSharePreview preview = PresetShareService.Preview(file, ["alpha\\skin", "Other\\Mod"], "Arknights: Endfield");

        Assert.Equal("Alpha/Skin", Assert.Single(preview.Matched).RelativePath);
        Assert.Equal(["Beta/Hair"], preview.Missing);
        Assert.False(preview.SameGame);
        Assert.Equal(50, preview.MatchPercent(file.Mods.Count));
        Assert.True(PresetShareService.Preview(file, [], "genshin impact").SameGame);
    }
}
