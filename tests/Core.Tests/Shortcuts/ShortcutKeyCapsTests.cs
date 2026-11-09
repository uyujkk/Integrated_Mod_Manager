using IntegratedModManager.Core.Shortcuts;

namespace IntegratedModManager.Core.Tests;

public sealed class ShortcutKeyCapsTests
{
    [Theory]
    [InlineData("H", new[] { "H" })]
    [InlineData("Ctrl+Shift+H", new[] { "Ctrl", "Shift", "H" })]
    [InlineData("Ctrl++", new[] { "Ctrl", "+" })]
    [InlineData("+", new[] { "+" })]
    [InlineData(" Alt + F5 ", new[] { "Alt", "F5" })]
    [InlineData("Mouse Left", new[] { "Mouse Left" })]
    public void Split_GivesOneCapPerKey(string keys, string[] expected) => Assert.Equal(expected, ShortcutKeyCaps.Split(keys));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Split_EmptyTextHasNoCaps(string? keys) => Assert.Empty(ShortcutKeyCaps.Split(keys));
}
