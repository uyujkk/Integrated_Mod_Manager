using IntegratedModManager.Core.Infrastructure;

namespace IntegratedModManager.Core.Tests;

public sealed class UiActionTrailTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "imm-ui-trail-tests-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_root, "context.json");
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [Fact]
    public void NextStartupPreservesInterruptedRunAndNormalCloseIsExplicit()
    {
        var first = new UiActionTrail(FilePath, "preview");
        first.Record("Details requested", 123, 1600, 900);
        var second = new UiActionTrail(FilePath, "preview");
        second.Record("Application starting");
        var previous = RecoveryJsonFile.Read<UiActionTrailDocument>(FilePath + ".previous");
        Assert.False(previous.ClosedNormally); Assert.Equal(123, Assert.Single(previous.Actions).ItemId);
        second.Record("Window closed", closed: true);
        Assert.True(RecoveryJsonFile.Read<UiActionTrailDocument>(FilePath).ClosedNormally);
    }

    [Fact]
    public void TrailIsBoundedAndNonFiniteLayoutValuesDoNotBreakRecording()
    {
        var trail = new UiActionTrail(FilePath, "preview");
        for (int i = 0; i < 50; i++) trail.Record("Details requested", i, double.NaN, double.PositiveInfinity);
        var document = RecoveryJsonFile.Read<UiActionTrailDocument>(FilePath);
        Assert.Equal(32, document.Actions.Count); Assert.Equal(18, document.Actions[0].ItemId);
        Assert.All(document.Actions, action => Assert.Equal(0, action.Width));
    }
}
