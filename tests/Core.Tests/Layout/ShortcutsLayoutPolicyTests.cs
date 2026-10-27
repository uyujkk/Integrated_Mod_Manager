using IntegratedModManager.Core.Layout;

namespace IntegratedModManager.Core.Tests;

public sealed class ShortcutsLayoutPolicyTests
{
    [Theory]
    [InlineData(2400, true)]
    [InlineData(1230, true)]
    [InlineData(1100, true)]
    [InlineData(1099, false)]
    [InlineData(700, false)]
    public void SettingsBesideBindings_FromAboutAWindowedPage(double width, bool expected)
        => Assert.Equal(expected, ShortcutsLayoutPolicy.SettingsBesideBindings(width));

    [Theory]
    [InlineData(860, true)]
    [InlineData(859, false)]
    public void ToolbarInOneRow_StacksOnNarrowPages(double width, bool expected)
        => Assert.Equal(expected, ShortcutsLayoutPolicy.ToolbarInOneRow(width));

    [Theory]
    [InlineData(0, 1)]
    [InlineData(279, 1)]
    [InlineData(280, 1)]
    [InlineData(568, 2)]
    [InlineData(830, 2)]
    [InlineData(856, 3)]
    [InlineData(1144, 4)]
    [InlineData(3000, 4)]
    public void BindingColumns_OnePerCardWidthUpToFour(double width, int expected)
        => Assert.Equal(expected, ShortcutsLayoutPolicy.BindingColumns(width));
}
