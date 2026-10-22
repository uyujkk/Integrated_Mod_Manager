using System.Globalization;
using System.Text.RegularExpressions;

namespace IntegratedModManager.Core;

/// <summary>A validated keyboard chord. Unknown tokens must never become partial input.</summary>
public sealed record ShortcutKeyPlan(IReadOnlyList<ushort> Keys, IReadOnlyList<ushort> ForbiddenKeys)
{
    public static bool TryCreate(string expression, out ShortcutKeyPlan? plan)
    {
        plan = null;
        if (string.IsNullOrWhiteSpace(expression) || expression.Length > 256) return false;
        var keys = new List<ushort>();
        var forbidden = new List<ushort>();
        string[] tokens = expression.Trim() == "+" ? ["OEM_PLUS"]
            : expression.Split([' ', '\t', '+'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string raw in tokens)
        {
            string token = raw.ToUpperInvariant();
            if (token == "NO_MODIFIERS")
            {
                forbidden.AddRange([0x10, 0x11, 0x12, 0x5B, 0x5C]);
                continue;
            }
            bool negative = token.StartsWith("NO_", StringComparison.Ordinal);
            if (negative) token = token[3..];
            if (token == "WIN" && negative) { forbidden.AddRange([0x5B, 0x5C]); continue; }
            if (!TryKey(token, out ushort key)) return false;
            if (negative)
            {
                if (!IsModifier(key)) return false;
                forbidden.Add(key);
            }
            else if (!keys.Contains(key)) keys.Add(key);
        }
        if (keys.Count == 0 || keys.Count > 5 || keys.Count(k => !IsModifier(k)) > 1) return false;
        // Refuse contradictory constraints, including generic vs sided modifiers.
        if (keys.Any(k => forbidden.Any(f => SameModifier(k, f) || k == f))) return false;
        // A Mod button must not invoke an operating-system action.
        if (keys.Any(k => k is 0x5B or 0x5C)
            || (keys.Any(k => SameModifier(k, 0x12)) && keys.Any(k => k is 0x09 or 0x1B or 0x73))
            || (keys.Any(k => SameModifier(k, 0x11)) && keys.Any(k => k is 0x1B or 0x2E))) return false;
        plan = new(keys.OrderBy(k => IsModifier(k) ? 0 : 1).ToArray(), forbidden.Distinct().ToArray());
        return true;
    }

    public static bool IsModifier(ushort key) => key is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or >= 0xA0 and <= 0xA5;
    private static ushort Family(ushort key) => key switch { 0xA0 or 0xA1 => 0x10, 0xA2 or 0xA3 => 0x11, 0xA4 or 0xA5 => 0x12, _ => key };
    private static bool SameModifier(ushort a, ushort b) => IsModifier(a) && IsModifier(b) && Family(a) == Family(b);

    private static bool TryKey(string token, out ushort key)
    {
        if (token.StartsWith("VK_", StringComparison.Ordinal)) token = token[3..];
        if (token.Length == 1 && char.IsAsciiLetterOrDigit(token[0])) { key = token[0]; return true; }
        if (Regex.IsMatch(token, "^F([1-9]|1[0-9]|2[0-4])$"))
        { key = (ushort)(0x6F + int.Parse(token[1..], CultureInfo.InvariantCulture)); return true; }
        Match num = Regex.Match(token, "^(?:NUMPAD|NUM)([0-9])$");
        if (num.Success) { key = (ushort)(0x60 + int.Parse(num.Groups[1].Value, CultureInfo.InvariantCulture)); return true; }
        key = token switch
        {
            "SHIFT" => 0x10, "CTRL" or "CONTROL" => 0x11, "ALT" or "MENU" => 0x12,
            "LSHIFT" => 0xA0, "RSHIFT" => 0xA1, "LCTRL" or "LCONTROL" => 0xA2,
            "RCTRL" or "RCONTROL" => 0xA3, "LALT" or "LMENU" => 0xA4, "RALT" or "RMENU" => 0xA5,
            "WIN" or "WINDOWS" or "LWIN" => 0x5B, "RWIN" => 0x5C,
            "APPS" or "APPLICATION" => 0x5D, "RETURN" or "ENTER" => 0x0D,
            "ESCAPE" or "ESC" => 0x1B, "SPACE" => 0x20, "TAB" => 0x09,
            "BACK" or "BACKSPACE" => 0x08, "PRIOR" or "PAGEUP" => 0x21,
            "NEXT" or "PAGEDOWN" => 0x22, "END" => 0x23, "HOME" => 0x24,
            "LEFT" => 0x25, "UP" => 0x26, "RIGHT" => 0x27, "DOWN" => 0x28,
            "INSERT" or "INS" => 0x2D, "DELETE" or "DEL" => 0x2E,
            "MULTIPLY" or "NUMMULTIPLY" => 0x6A, "ADD" or "NUMADD" => 0x6B,
            "SUBTRACT" or "NUMSUBTRACT" => 0x6D, "DECIMAL" or "NUMDECIMAL" => 0x6E,
            "DIVIDE" or "NUMDIVIDE" => 0x6F,
            "OEM_PLUS" => 0xBB, "OEM_MINUS" or "-" => 0xBD,
            "OEM_COMMA" or "," => 0xBC, "OEM_PERIOD" or "." => 0xBE,
            "OEM_1" => 0xBA, "OEM_2" => 0xBF, "OEM_3" => 0xC0,
            "OEM_4" => 0xDB, "OEM_5" => 0xDC, "OEM_6" => 0xDD, "OEM_7" => 0xDE,
            _ => 0
        };
        return key != 0;
    }
}
