using Microsoft.UI;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ModFolderCopier.WinUI;

// State and helpers only the workspace page uses, moved from the window's files (stage J7).
public sealed partial class WorkspacePage
{
    internal static readonly SolidColorBrush CopiedBrush = new(ColorHelper.FromArgb(255, 36, 124, 76));
    internal static readonly SolidColorBrush MissingBrush = new(ColorHelper.FromArgb(255, 177, 76, 55));
    internal static readonly SolidColorBrush NeutralBrush = new(ColorHelper.FromArgb(255, 60, 60, 60));
    internal const int InitialShortcutRows = 1;
    internal string StateCopiedText => T("Localization.Copied");
    internal string StateLinkedText => T("Localization.Linked");
    internal string StateMissingText => T("Localization.NotDeployed");
    internal string StateNotConfiguredText => T("Localization.NotConfigured");
    internal string StateNotSelectedText => T("Common.NotSelected");
    internal TextBox? _activeShortcutKeyBox;
    internal bool _isLoadingBindings;
    internal bool _isLoadingModLink;
    internal int _previewImageRequestVersion;
    internal readonly List<Border> _shortcutKeyBorders = [];
    internal int _visibleShortcutRows = 1;
}
