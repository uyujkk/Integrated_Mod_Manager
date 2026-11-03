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

    // One Mod with this many saved values; names and game in Chinese so the limit counts multi-byte text.
    private static string Large(int values) => PresetShareService.Serialize("夜晚方案", "原神", true,
        [new CombinationPresetState("角色\\皮肤", Enumerable.Range(0, values)
            .Select(index => new ModPersistentValue("mod.ini", $"$v{index}", $"$\\mods\\skin\\mod.ini\\v{index}", "1")).ToArray())]);

    [Fact]
    public void Serialize_RefusesWhatImportWouldRefuseAndKeepsWhatItAccepts()
    {
        // The largest preset that still exports, found by halving the range.
        int low = 1, high = 80_000;
        while (low < high)
        {
            int middle = (low + high + 1) / 2;
            try { Large(middle); low = middle; }
            catch (PresetShareTooLargeException) { high = middle - 1; }
        }

        string largest = Large(low);
        Assert.InRange(System.Text.Encoding.UTF8.GetByteCount(largest), PresetShareService.MaxBytes - 200, PresetShareService.MaxBytes);
        Assert.Equal(low, PresetShareService.Parse(largest).Mods[0].Values.Count);
        Assert.Throws<PresetShareTooLargeException>(() => Large(low + 1));
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
