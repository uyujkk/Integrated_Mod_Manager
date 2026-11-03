using IntegratedModManager.Core.Repository;

namespace IntegratedModManager.Core.Tests;

public sealed class XxmiLaunchPolicyTests
{
    [Theory]
    [InlineData("genshin", "GIMI")]
    [InlineData("hsr", "SRMI")]
    [InlineData("zzz", "ZZMI")]
    [InlineData("endfield", null)]
    [InlineData(null, null)]
    public void ImporterFor_CoversThePresetsThatLaunchPlainly(string? preset, string? expected)
        => Assert.Equal(expected, XxmiLaunchPolicy.ImporterFor(preset));

    [Theory]
    [InlineData(@"C:\Games\XXMI Launcher\Resources\Bin\XXMI Launcher.exe", true)]
    [InlineData("xxmi launcher.exe", true)]
    [InlineData(@"C:\Games\launcher.exe", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsXxmiLauncher_LeavesOtherProgramsAlone(string? path, bool expected)
        => Assert.Equal(expected, XxmiLaunchPolicy.IsXxmiLauncher(path));

    [Fact]
    public void Arguments_NameTheImporter() => Assert.Equal("--nogui --xxmi GIMI", XxmiLaunchPolicy.Arguments("GIMI"));

    [Theory]
    [InlineData("""{ "Importers": { "GIMI": { "Importer": { "game_folder": "D:\\Game" } } } }""", XxmiImporterState.Ready)]
    [InlineData("""{ "Importers": { "GIMI": { "Importer": { "game_folder": "  " } } } }""", XxmiImporterState.NoGameFolder)]
    [InlineData("""{ "Importers": { "GIMI": { "Importer": { } } } }""", XxmiImporterState.NoGameFolder)]
    [InlineData("""{ "Importers": { "SRMI": { "Importer": { "game_folder": "D:\\Game" } } } }""", XxmiImporterState.ImporterMissing)]
    [InlineData("""{ "Other": 1 }""", XxmiImporterState.Unknown)]
    [InlineData("not json", XxmiImporterState.Unknown)]
    public void CheckImporter_ReadsTheConfiguration(string json, XxmiImporterState expected)
        => Assert.Equal(expected, XxmiLaunchPolicy.CheckImporter(json, "GIMI"));
}
