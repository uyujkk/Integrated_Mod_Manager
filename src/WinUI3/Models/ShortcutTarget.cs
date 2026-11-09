namespace ModFolderCopier.WinUI;

/// <summary>A top-level window that can receive keys; the start time tells a restarted game from the old one.</summary>
public sealed record ShortcutTarget(nint Handle, uint ProcessId, long StartTicks, string Label, string ProcessName = "")
{
    public override string ToString() => Label;
}
