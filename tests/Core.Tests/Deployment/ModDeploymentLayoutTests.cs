using IntegratedModManager.Core.Deployment;

namespace IntegratedModManager.Core.Tests;

public sealed class ModDeploymentLayoutTests
{
    private static readonly string Repository = Path.Combine(Path.GetTempPath(), "imm-layout-repo");

    [Fact]
    public void DeployedPath_UsesOnlyTheModFolderName()
    {
        string mod = Path.Combine(Repository, "Hu Tao", "Red Dress") + Path.DirectorySeparatorChar;

        Assert.Equal("Red Dress", ModDeploymentLayout.DeployedName(mod));
        Assert.Equal(Path.Combine("D:", "Mods", "Red Dress"), ModDeploymentLayout.DeployedPath(Path.Combine("D:", "Mods"), mod));
        Assert.True(ModDeploymentLayout.SameDeployedName(mod, Path.Combine(Repository, "Ayaka", "red dress")));
        Assert.False(ModDeploymentLayout.SameDeployedName(mod, Path.Combine(Repository, "Hu Tao", "Blue")));
    }

    [Fact]
    public void CharacterFolder_IsTheFirstLevelFolderOfASecondLevelMod()
    {
        Assert.Equal(Path.Combine(Repository, "Hu Tao"), ModDeploymentLayout.CharacterFolder(Repository, Path.Combine(Repository, "Hu Tao", "Red Dress")));
        Assert.Null(ModDeploymentLayout.CharacterFolder(Repository, Path.Combine(Repository, "Hu Tao")));
        Assert.Null(ModDeploymentLayout.CharacterFolder(Repository, Path.Combine(Repository, "Hu Tao", "Pack", "Variant")));
        Assert.Null(ModDeploymentLayout.CharacterFolder(Repository, Path.Combine(Path.GetTempPath(), "elsewhere", "A", "B")));
    }

    [Fact]
    public void AllowsMultiple_UsesTheOverrideForTheFolderOrTheDefault()
    {
        var overrides = new Dictionary<string, bool> { ["Hu Tao"] = true, ["Ayaka"] = false };

        Assert.True(CharacterSelectionPolicy.AllowsMultiple("hu tao", false, overrides));
        Assert.False(CharacterSelectionPolicy.AllowsMultiple("Ayaka", true, overrides));
        Assert.False(CharacterSelectionPolicy.AllowsMultiple("Raiden", false, overrides));
        Assert.True(CharacterSelectionPolicy.AllowsMultiple("Raiden", true, null));
    }

    [Fact]
    public void SingleSelectionViolations_ListsSingleSelectionCharactersWithSeveralMods()
    {
        string[] mods =
        [
            Path.Combine(Repository, "Hu Tao", "A"), Path.Combine(Repository, "Hu Tao", "B"),
            Path.Combine(Repository, "Ayaka", "A"), Path.Combine(Repository, "Ayaka", "B"),
            Path.Combine(Repository, "Raiden", "A"),
            Path.Combine(Repository, "Loose")
        ];
        var overrides = new Dictionary<string, bool> { ["Ayaka"] = true };

        Assert.Equal(["Hu Tao"], CharacterSelectionPolicy.SingleSelectionViolations(Repository, mods, false, overrides));
        Assert.Empty(CharacterSelectionPolicy.SingleSelectionViolations(Repository, mods, true, null));
    }
}
