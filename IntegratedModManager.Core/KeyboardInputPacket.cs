namespace IntegratedModManager.Core;

public enum ShortcutInputMode { ScanCode, VirtualKey }

/// <summary>Win32 keyboard packet policy, separated from input delivery for regression tests.</summary>
public sealed record KeyboardInputPacket(ushort VirtualKey, ushort Scan, uint Flags)
{
    public static KeyboardInputPacket Create(ushort key, uint mappedScan, bool up, ShortcutInputMode mode)
    {
        bool extended = (mappedScan & 0xFF00) == 0xE000
            || key is >= 0x21 and <= 0x28 or 0x2D or 0x2E or 0x5B or 0x5C or 0x5D or 0x6F or 0x90 or 0xA3 or 0xA5;
        uint flags = (up ? 2u : 0u) | (extended ? 1u : 0u);
        if (mode == ShortcutInputMode.ScanCode)
        {
            if ((mappedScan & 0xFF) == 0 || (mappedScan & 0xFF00) == 0xE100)
                throw new ArgumentException("This key has no supported scan code.");
            return new(0, (ushort)(mappedScan & 0xFF), flags | 8);
        }
        return new(key, (ushort)(mappedScan & 0xFF), flags);
    }
}
