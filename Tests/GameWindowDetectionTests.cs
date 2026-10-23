using IntegratedModManager.Core.Shortcuts;

namespace IntegratedModManager.Core.Tests;

public sealed class GameWindowDetectionTests
{
    [Fact]
    public void Find_PicksTheEndfieldClientAmongOtherWindows()
    {
        GameWindowCandidate[] windows =
        [
            new("explorer", "Downloads"),
            new("ModFolderCopier.WinUI", "Integrated Mod Manager"),
            new("Endfield", "Arknights: Endfield"),
            new("chrome", "Endfield wiki")
        ];

        Assert.Equal(2, GameWindowDetection.Find(windows));
    }

    [Fact]
    public void Find_PrefersTheRepositoryGameOverOtherRecognizedGames()
    {
        GameWindowCandidate[] windows = [new("StarRail", "Star Rail"), new("Endfield", "Endfield")];

        Assert.Equal(0, GameWindowDetection.Find(windows));
        Assert.Equal(1, GameWindowDetection.Find(windows, ["Endfield.exe"]));
        Assert.Equal(0, GameWindowDetection.Find(windows, ["NotRunning"]));
    }

    [Fact]
    public void Find_IgnoresCaseAndExeSuffixAndOnlyChoosesKnownGames()
    {
        Assert.Equal(0, GameWindowDetection.Find([new("ENDFIELD.EXE", "x")]));
        Assert.Equal(1, GameWindowDetection.Find([new("Notepad", "Endfield notes"), new("yuanshen", "原神")]));
        Assert.Equal(-1, GameWindowDetection.Find([new("Notepad", "Endfield notes"), new("EndfieldLauncher", "Launcher")]));
        Assert.Equal(-1, GameWindowDetection.Find([]));
    }
}
