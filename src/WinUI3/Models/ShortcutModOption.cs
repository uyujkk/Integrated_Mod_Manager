namespace ModFolderCopier.WinUI;

/// <summary>A Mod folder of the current repository on the Shortcuts page and the overlay; the label is its relative path.</summary>
public sealed record ShortcutModOption(string Path, string Label)
{
    public override string ToString() => Label;
}
