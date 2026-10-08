using IntegratedModManager.Core;

namespace IntegratedModManager.Core.Tests;

public sealed class DialogViewportPolicyTests
{
    // Simulated logical dimensions are not proof of an actual Windows DPI change.
    [Theory]
    [InlineData(1920, 1080, 960, 580)]
    [InlineData(1280, 720, 960, 460)]
    [InlineData(960, 540, 864, 280)]
    [InlineData(800, 520, 704, 260)]
    [InlineData(360, 420, 280, 160)]
    public void SelectionReservesRoomForTitleAndCommands(double width, double height, double maxWidth, double contentHeight)
    {
        DialogViewport layout = DialogViewportPolicy.Selection(width, height);
        Assert.Equal(maxWidth, layout.MaxWidth);
        Assert.Equal(contentHeight, layout.ContentHeight);
        Assert.True(layout.ContentHeight + 260 <= height);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(-1)]
    [InlineData(0)]
    public void UnknownMeasurementsUseFiniteSmallDialogBudget(double value)
        => Assert.Equal(new DialogViewport(280, 160), DialogViewportPolicy.Selection(value, value));
}
