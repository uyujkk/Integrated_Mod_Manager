namespace IntegratedModManager.Core;

public sealed class ShortcutBinding
{
    public ShortcutBinding(string shortcut, string action)
    {
        Shortcut = shortcut;
        Action = action;
    }

    public string Shortcut { get; }

    public string Action { get; }

    public string AutoSection { get; set; } = string.Empty;

    public string AutoBehavior { get; set; } = string.Empty;

    public string AutoTarget { get; set; } = string.Empty;

    public string AutoSourceFile { get; set; } = string.Empty;

    public bool HasAutoDescription => !string.IsNullOrWhiteSpace(AutoSection)
        || !string.IsNullOrWhiteSpace(AutoTarget);
}
