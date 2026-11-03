using CommunityToolkit.Mvvm.ComponentModel;
using IntegratedModManager.Core.Shortcuts;

namespace ModFolderCopier.WinUI;

/// <summary>
/// The Shortcuts page and its overlay: the saved settings, whether input may be sent, and which bindings the
/// selected Mod has. The window still builds the controls, scans INI files and sends keys; it keeps this
/// state here instead of in its own fields.
/// </summary>
public sealed partial class ShortcutPageViewModel : ObservableObject
{
    public const uint DefaultHotkeyModifiers = 3; // Ctrl + Alt
    public const uint DefaultHotkeyKey = 0x4B; // K
    public const int MinimumOverlayOpacity = 10;

    private ShortcutInputMode _inputMode = ShortcutInputMode.ScanCode;
    private int _overlayOpacity = 100;
    private bool _hideUnsupported;
    private bool _hotkeyEnabled = true;
    private uint _hotkeyModifiers = DefaultHotkeyModifiers;
    private uint _hotkeyKey = DefaultHotkeyKey;
    private bool _hotkeyRegistered;
    private bool _inputEnabled;
    private bool _sendInProgress;
    private bool _overlayVisible;
    private IReadOnlyList<DetectedModShortcut> _detected = [];

    // Saved settings.
    public ShortcutInputMode InputMode { get => _inputMode; set => SetProperty(ref _inputMode, Enum.IsDefined(value) ? value : ShortcutInputMode.ScanCode); }

    /// <summary>Background opacity of the overlay in percent; the overlay never goes fully transparent.</summary>
    public int OverlayOpacity { get => _overlayOpacity; set => SetProperty(ref _overlayOpacity, Math.Clamp(value, MinimumOverlayOpacity, 100)); }

    public bool HideUnsupported { get => _hideUnsupported; set => SetProperty(ref _hideUnsupported, value); }

    public bool HotkeyEnabled { get => _hotkeyEnabled; set => SetProperty(ref _hotkeyEnabled, value); }

    /// <summary>MOD_ flags of the show/hide hotkey: Ctrl+Alt (3), Alt+Shift (5) or Ctrl+Shift (6).</summary>
    public uint HotkeyModifiers { get => _hotkeyModifiers; set => SetProperty(ref _hotkeyModifiers, value is 3 or 5 or 6 ? value : DefaultHotkeyModifiers); }

    /// <summary>Virtual key of the hotkey: a letter or F1–F12.</summary>
    public uint HotkeyKey { get => _hotkeyKey; set => SetProperty(ref _hotkeyKey, value is >= 0x41 and <= 0x5A or >= 0x70 and <= 0x7B ? value : DefaultHotkeyKey); }

    // Runtime state.
    public bool HotkeyRegistered { get => _hotkeyRegistered; set => SetProperty(ref _hotkeyRegistered, value); }

    /// <summary>The user armed sending; nothing reaches a game without it.</summary>
    public bool InputEnabled { get => _inputEnabled; set => SetProperty(ref _inputEnabled, value); }

    /// <summary>A key, a slot switch or a capture owns the input or d3dx_user.ini right now.</summary>
    public bool SendInProgress { get => _sendInProgress; set => SetProperty(ref _sendInProgress, value); }

    public bool OverlayVisible { get => _overlayVisible; set => SetProperty(ref _overlayVisible, value); }

    /// <summary>Bindings found in the selected Mod's INI files.</summary>
    public IReadOnlyList<DetectedModShortcut> Detected { get => _detected; set => SetProperty(ref _detected, value); }

    /// <summary>The Mod folder the bindings were read from.</summary>
    public string LoadedPath { get; set; } = string.Empty;

    /// <summary>Raised for each scan, so a slower earlier scan cannot replace a newer result.</summary>
    public int ScanRevision { get; set; }

    /// <summary>The bindings to show: all, or only those that can be sent when the user hides the rest.</summary>
    public DetectedModShortcut[] VisibleShortcuts() => _hideUnsupported
        ? _detected.Where(shortcut => ShortcutKeyPlan.TryCreate(shortcut.RawKey, out _)).ToArray()
        : _detected.ToArray();

    /// <summary>The hotkey as the user presses it, such as "Ctrl+Alt" and "K".</summary>
    public (string Modifiers, string Key) HotkeyLabel() => (
        _hotkeyModifiers switch { 6 => "Ctrl+Shift", 5 => "Alt+Shift", _ => "Ctrl+Alt" },
        _hotkeyKey >= 0x70 ? "F" + (_hotkeyKey - 0x6F) : ((char)_hotkeyKey).ToString());

    /// <summary>Settings as stored in beta-shell.json; anything missing or out of range falls back to its default.</summary>
    public void LoadSettings(string? inputMode, int? overlayOpacity, bool? hideUnsupported, bool hotkeyEnabled, uint hotkeyModifiers, uint hotkeyKey)
    {
        InputMode = Enum.TryParse(inputMode, out ShortcutInputMode mode) ? mode : ShortcutInputMode.ScanCode;
        OverlayOpacity = overlayOpacity ?? 100;
        HideUnsupported = hideUnsupported ?? false;
        HotkeyEnabled = hotkeyEnabled;
        HotkeyModifiers = hotkeyModifiers;
        HotkeyKey = hotkeyKey;
    }
}
