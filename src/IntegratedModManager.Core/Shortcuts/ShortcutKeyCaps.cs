namespace IntegratedModManager.Core.Shortcuts;

/// <summary>Splits a binding's key text ("Ctrl+Shift+H") into the keys drawn as separate key caps.</summary>
public static class ShortcutKeyCaps
{
    /// <summary>The keys in order; a "+" that is a key itself ("Ctrl++") stays one key.</summary>
    public static IReadOnlyList<string> Split(string? keys)
    {
        var caps = new List<string>();
        if (string.IsNullOrWhiteSpace(keys)) return caps;
        var current = new System.Text.StringBuilder();
        foreach (char c in keys.Trim())
        {
            if (c == '+' && current.Length > 0)
            {
                caps.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        if (current.Length > 0) caps.Add(current.ToString().Trim());
        caps.RemoveAll(string.IsNullOrEmpty);
        return caps;
    }
}
