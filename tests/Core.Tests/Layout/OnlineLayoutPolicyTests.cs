using IntegratedModManager.Core.Layout;

namespace IntegratedModManager.Core.Tests;

public sealed class OnlineLayoutPolicyTests
{
    [Theory]
    [InlineData(845, 621, false, 152)]
    [InlineData(600, 466, false, 152)]
    [InlineData(845, 739, false, 152)]
    [InlineData(845, 740, true, 152)]
    [InlineData(1039, 900, true, 152)]
    [InlineData(1040, 900, false, 188)]
    [InlineData(1500, 620, false, 188)]
    [InlineData(1600, 900, false, 220)]
    public void ShortWindowsKeepVerticalSpaceForResults(double width, double height, bool horizontal, double columnWidth)
        => Assert.Equal(new OnlineCharacterRailLayout(horizontal, columnWidth), OnlineLayoutPolicy.CharacterRail(width, height));

    [Theory]
    [InlineData(400, 1, 400, 219)]
    [InlineData(600, 2, 300, 163)]
    [InlineData(1400, 4, 350, 191)]
    [InlineData(2600, 8, 325, 177)]
    public void GridTilesFillTheRowWithA16By9Preview(double width, int columns, double itemWidth, double imageHeight)
    {
        OnlineGridTileLayout layout = OnlineLayoutPolicy.GridTiles(width);
        Assert.Equal(columns, layout.Columns);
        Assert.Equal(itemWidth, layout.ItemWidth);
        Assert.Equal(imageHeight, layout.ImageHeight);
        Assert.Equal(imageHeight + OnlineLayoutPolicy.TileBodyHeight + 2 * OnlineLayoutPolicy.TileMargin, layout.ItemHeight);
        Assert.True(layout.Columns * layout.ItemWidth <= width);
    }

    [Fact]
    public void VeryWideGridsCapColumnsAndImageHeight()
    {
        OnlineGridTileLayout layout = OnlineLayoutPolicy.GridTiles(4000);
        Assert.Equal(OnlineLayoutPolicy.MaxTileColumns, layout.Columns);
        Assert.Equal(240, layout.ImageHeight);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(0)]
    [InlineData(-50)]
    public void InvalidWidthsFallBackToOnePreferredTile(double width)
    {
        OnlineGridTileLayout layout = OnlineLayoutPolicy.GridTiles(width);
        Assert.Equal(1, layout.Columns);
        Assert.Equal(OnlineLayoutPolicy.PreferredTileWidth, layout.ItemWidth);
    }
}
