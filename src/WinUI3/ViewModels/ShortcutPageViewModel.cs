using CommunityToolkit.Mvvm.ComponentModel;
using IntegratedModManager.Core.Shortcuts;

namespace ModFolderCopier.WinUI;

/// <summary>What the Shortcuts page asks the window to do; <see cref="ShortcutPageRequest.Argument"/> carries the binding or hotkey.</summary>
public enum ShortcutPageAction
{
    RefreshWindows,
    ReloadIni,
    OpenIni,

    /// <summary>Argument: the <see cref="DetectedModShortcut"/> to send.</summary>
    SendShortcut,
    SendNumLock,
    RestartElevated,

    /// <summary>Argument: the hotkey as (modifiers, key); the window registers it or reports <see cref="ShortcutPageViewModel.HotkeyRejected"/>.</summary>
    SetHotkey,
    DisableHotkey
}

public sealed record ShortcutPageRequest(ShortcutPageAction Action, object? Argument = null);

/// <summary>
/// The Shortcuts page and its overlay: the saved settings, the Mod and window to send to, whether input may be
/// sent, the bindings the selected Mod has and the last status. The page (Pages/ShortcutsPage) and the overlay
/// show this; the window scans INI files, sends keys and answers the page's requests (stage J5).
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
    private IReadOnlyList<ShortcutModOption> _modOptions = [];
    private ShortcutModOption? _selectedMod;
    private IReadOnlyList<ShortcutTarget> _targets = [];
    private ShortcutTarget? _selectedTarget;
    private string _statusText = string.Empty;
    private double _tapDuration = 200, _holdDuration = 300;
    private string _privilegeText = string.Empty;
    private bool _isElevated;
    private bool _hotkeyRejected;

    /// <summary>Raised when the page asks for something only the window can do.</summary>
    public event EventHandler<ShortcutPageRequest>? ActionRequested;

    public void Request(ShortcutPageAction action, object? argument = null) => ActionRequested?.Invoke(this, new ShortcutPageRequest(action, argument));

    /// <summary>The current repository's Mod folders.</summary>
    public IReadOnlyList<ShortcutModOption> ModOptions { get => _modOptions; set => SetProperty(ref _modOptions, value ?? []); }

    /// <summary>The Mod whose bindings are shown and sent; the page and the overlay both choose it.</summary>
    public ShortcutModOption? SelectedMod { get => _selectedMod; set => SetProperty(ref _selectedMod, value); }

    /// <summary>Windows that can receive keys.</summary>
    public IReadOnlyList<ShortcutTarget> Targets { get => _targets; set => SetProperty(ref _targets, value ?? []); }

    /// <summary>The game window. Choosing another one always disarms input, so it is re-armed deliberately.</summary>
    public ShortcutTarget? SelectedTarget
    {
        get => _selectedTarget;
        set
        {
            if (SetProperty(ref _selectedTarget, value)) InputEnabled = false;
        }
    }

    /// <summary>The last read, send or capture result, shown on the page and the overlay.</summary>
    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value ?? string.Empty); }

    /// <summary>How long a key is pressed, in ms; a cleared box is NaN and sending falls back to 200.</summary>
    public double TapDuration { get => _tapDuration; set => SetProperty(ref _tapDuration, value); }

    /// <summary>How long a "hold" binding is held, in ms; NaN falls back to 300.</summary>
    public double HoldDuration { get => _holdDuration; set => SetProperty(ref _holdDuration, value); }

    public int TapMilliseconds => (int)(double.IsFinite(_tapDuration) ? _tapDuration : 200);

    public int HoldMilliseconds => (int)(double.IsFinite(_holdDuration) ? _holdDuration : 300);

    /// <summary>Whether keys can reach the selected window, in words.</summary>
    public string PrivilegeText { get => _privilegeText; set => SetProperty(ref _privilegeText, value ?? string.Empty); }

    /// <summary>The manager already runs elevated, so restarting as administrator is not offered.</summary>
    public bool IsElevated { get => _isElevated; set => SetProperty(ref _isElevated, value); }

    /// <summary>The last hotkey choice could not be registered; the previous one stays in effect.</summary>
    public bool HotkeyRejected { get => _hotkeyRejected; set => SetProperty(ref _hotkeyRejected, value); }

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

    /// <summary>The user armed sending; nothing reaches a game without it, and it needs a target window.</summary>
    public bool InputEnabled { get => _inputEnabled; set => SetProperty(ref _inputEnabled, value && _selectedTarget is not null); }

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
