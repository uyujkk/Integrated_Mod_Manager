namespace IntegratedModManager.Core.Shortcuts;

public enum ShortcutPrivilegeVerdict { Allowed, TargetHigher, TargetUnreadable }

/// <summary>
/// Decides whether UIPI will silently drop SendInput to the target. SendInput reports success
/// even when UIPI discards the events, so the decision must be made before sending.
/// </summary>
public static class ShortcutPrivilegePolicy
{
    public const int HighIntegrity = 0x3000;
    private const int AccessDenied = 5;

    public static ShortcutPrivilegeVerdict Evaluate(int? senderLevel, bool senderUiAccess, int? targetLevel, int targetError)
    {
        if (senderUiAccess) return ShortcutPrivilegeVerdict.Allowed;
        if (targetLevel is int target)
            return senderLevel is int sender && target > sender ? ShortcutPrivilegeVerdict.TargetHigher : ShortcutPrivilegeVerdict.Allowed;
        // A medium-integrity process is denied the token of an elevated process. Games that
        // declare requireAdministrator (Endfield does) land here, so treat it as blocked.
        bool senderElevated = senderLevel is int level && level >= HighIntegrity;
        return targetError == AccessDenied && !senderElevated ? ShortcutPrivilegeVerdict.TargetUnreadable : ShortcutPrivilegeVerdict.Allowed;
    }

    public static bool IsElevated(int? level) => level is int value && value >= HighIntegrity;
}
