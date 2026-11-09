using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

/// <summary>
/// The Shortcuts overlay as the rest of the app sees it (stage J5): the page, the global hotkey and the window
/// open, hide and refresh it through this, never through the window that happens to own it.
/// </summary>
internal interface IShortcutOverlay
{
    /// <summary>Shown on screen (it may be hidden while still created).</summary>
    bool IsVisible { get; }

    /// <summary>Lets mouse clicks through to the game; only the global hotkey brings the mouse back.</summary>
    bool IsClickThrough { get; }

    /// <summary>Shows the overlay, creating it when needed.</summary>
    void Open();

    void Hide();

    /// <summary>What the global hotkey does: leave click-through first, else show or hide.</summary>
    void Toggle();

    /// <summary>Updates the overlay from the page's state; <paramref name="bindingsChanged"/> also rebuilds its bindings and slots.</summary>
    void Refresh(bool bindingsChanged = false);
}

/// <summary>
/// Stage J5a bridge: the settings blocks of the Shortcuts page that still belong to the window (appearance,
/// overlay behaviour and character follow). Stage J5b replaces them with ShortcutPageViewModel properties.
/// </summary>
internal interface IShortcutPageSettings
{
    /// <summary>Adds the overlay appearance rows, placing the page's background-opacity slider among them.</summary>
    void AddOverlayAppearance(StackPanel group, Slider backgroundOpacity);

    /// <summary>Adds the overlay behaviour rows: slot hot switching, preset previews and slot capture.</summary>
    void AddOverlayBehavior(StackPanel group);

    /// <summary>Adds the character follow controls; returns the on/off switch for the row header and the summary text.</summary>
    (ToggleSwitch Toggle, TextBlock Summary) AddCharacterFollow(StackPanel body);
}
