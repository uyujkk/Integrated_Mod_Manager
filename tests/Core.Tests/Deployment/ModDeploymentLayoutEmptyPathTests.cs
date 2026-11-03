using IntegratedModManager.Core.Deployment;

namespace IntegratedModManager.Core.Tests;

public sealed class ModDeploymentLayoutEmptyPathTests
{
    [Theory]
    [InlineData("", @"C:\Mods\Alpha\Skin")]
    [InlineData("  ", @"C:\Mods\Alpha\Skin")]
    [InlineData(@"C:\Mods", "")]
    public void CharacterFolder_HasNoAnswerBeforeTheFoldersAreChosen(string root, string mod)
        => Assert.Null(ModDeploymentLayout.CharacterFolder(root, mod));
}
