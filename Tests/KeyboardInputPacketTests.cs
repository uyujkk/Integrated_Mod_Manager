using IntegratedModManager.Core.Shortcuts;

namespace IntegratedModManager.Core.Tests;

public sealed class KeyboardInputPacketTests
{
    [Theory]
    [InlineData(0x41, 0x1E)]
    [InlineData(0x65, 0x4C)]
    [InlineData(0x75, 0x40)]
    public void ScanCodeModeUsesPhysicalScanWithZeroVirtualKey(int vk, int scan)
    {
        var down = KeyboardInputPacket.Create((ushort)vk, (uint)scan, false, ShortcutInputMode.ScanCode);
        var up = KeyboardInputPacket.Create((ushort)vk, (uint)scan, true, ShortcutInputMode.ScanCode);
        Assert.Equal((ushort)0, down.VirtualKey);
        Assert.Equal((ushort)scan, down.Scan);
        Assert.Equal(8u, down.Flags);
        Assert.Equal(down.Flags | 2, up.Flags);
        Assert.Equal(down.Scan, up.Scan);
    }

    [Theory]
    [InlineData(0x27, 0xE04D)]
    [InlineData(0xA3, 0xE01D)]
    [InlineData(0xA5, 0xE038)]
    [InlineData(0x5D, 0xE05D)]
    [InlineData(0x6F, 0xE035)]
    [InlineData(0x90, 0xE045)]
    public void ExtendedKeysKeepE0FlagAndLowScanByte(int vk, int mapped)
    {
        var packet = KeyboardInputPacket.Create((ushort)vk, (uint)mapped, false, ShortcutInputMode.ScanCode);
        Assert.Equal(9u, packet.Flags);
        Assert.Equal((ushort)(mapped & 0xFF), packet.Scan);
    }

    [Fact]
    public void NumpadRightAndNavigationRightAreDistinctPhysicalKeys()
    {
        var num = KeyboardInputPacket.Create(0x66, 0x4D, false, ShortcutInputMode.ScanCode);
        var nav = KeyboardInputPacket.Create(0x27, 0xE04D, false, ShortcutInputMode.ScanCode);
        Assert.Equal(num.Scan, nav.Scan);
        Assert.Equal(8u, num.Flags);
        Assert.Equal(9u, nav.Flags);
    }

    [Fact]
    public void VirtualKeyModeRetainsOriginalVirtualKeyAndDoesNotSetScanCodeFlag()
    {
        var packet = KeyboardInputPacket.Create(0x65, 0x4C, false, ShortcutInputMode.VirtualKey);
        Assert.Equal((ushort)0x65, packet.VirtualKey);
        Assert.Equal(0u, packet.Flags);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0xE11D)]
    public void MissingOrUnsupportedMultiPartScanIsRejected(int scan) =>
        Assert.Throws<ArgumentException>(() => KeyboardInputPacket.Create(0x13, (uint)scan, false, ShortcutInputMode.ScanCode));
}
