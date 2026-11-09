using IntegratedModManager.Core.Layout;

namespace ModFolderCopier.WinUI;

/// <summary>One choice of the loader's auto-save interval: the seconds and how the list shows them.</summary>
public sealed record LoaderAutoSaveChoice(int Seconds, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// Stage J5b: the overlay's look and behaviour, character follow and slot capture as page settings. The page shows
/// and edits them; the window applies them (to the overlay, the loader files, the capture) and saves them.
/// </summary>
public sealed partial class ShortcutPageViewModel
{
    private int _overlayElementOpacity = OverlayAppearancePolicy.DefaultElementOpacity;
    private string _overlayTextColor = "#FFFFFF";
    private bool _overlayCompactView;
    private bool _overlayAutoContrast;
    private bool _overlaySlotSwitchEnabled;
    private bool _overlayCompactSlots;
    private bool _capturePresetPreviews;
    private bool _keepMenuStateVariables;
    private bool _characterFollowEnabled;
    private string _characterFollowStatus = string.Empty;
    private string _characterFollowSummary = string.Empty;
    private IReadOnlyList<LoaderAutoSaveChoice> _loaderAutoSaveChoices = [];
    private int? _loaderAutoSaveSeconds;
    private string _loaderAutoSaveNote = string.Empty;

    /// <summary>Backing opacity of the overlay's buttons and labels, in percent.</summary>
    public int OverlayElementOpacity { get => _overlayElementOpacity; set => SetProperty(ref _overlayElementOpacity, OverlayAppearancePolicy.ClampElementOpacity(value)); }

    /// <summary>The user's overlay text colour as #RRGGBB (automatic contrast may show another for a while).</summary>
    public string OverlayTextColor { get => _overlayTextColor; set => SetProperty(ref _overlayTextColor, value ?? "#FFFFFF"); }

    /// <summary>The overlay shows only the Mod picker, the view row and the bindings.</summary>
    public bool OverlayCompactView { get => _overlayCompactView; set => SetProperty(ref _overlayCompactView, value); }

    /// <summary>The overlay text turns dark over bright scenes.</summary>
    public bool OverlayAutoContrast { get => _overlayAutoContrast; set => SetProperty(ref _overlayAutoContrast, value); }

    /// <summary>The overlay lists the selected Mod's state slots and switches them in game (F10).</summary>
    public bool OverlaySlotSwitchEnabled { get => _overlaySlotSwitchEnabled; set => SetProperty(ref _overlaySlotSwitchEnabled, value); }

    /// <summary>The overlay's slots are text only, without their pictures.</summary>
    public bool OverlayCompactSlots { get => _overlayCompactSlots; set => SetProperty(ref _overlayCompactSlots, value); }

    /// <summary>Presets saved from the overlay get a game screenshot; the user agreed to the privacy notice.</summary>
    public bool CapturePresetPreviews { get => _capturePresetPreviews; set => SetProperty(ref _capturePresetPreviews, value); }

    /// <summary>A captured slot keeps menu-state variables instead of leaving them out.</summary>
    public bool KeepMenuStateVariables { get => _keepMenuStateVariables; set => SetProperty(ref _keepMenuStateVariables, value); }

    /// <summary>The Mod follows the character on screen; the window turns it back off when it is not set up.</summary>
    public bool CharacterFollowEnabled { get => _characterFollowEnabled; set => SetProperty(ref _characterFollowEnabled, value); }

    /// <summary>What character follow is doing or why it cannot.</summary>
    public string CharacterFollowStatus { get => _characterFollowStatus; set => SetProperty(ref _characterFollowStatus, value ?? string.Empty); }

    /// <summary>One line for the folded character follow row.</summary>
    public string CharacterFollowSummary { get => _characterFollowSummary; set => SetProperty(ref _characterFollowSummary, value ?? string.Empty); }

    /// <summary>The loader's auto-save intervals to choose from; empty when its d3dx.ini cannot be read.</summary>
    public IReadOnlyList<LoaderAutoSaveChoice> LoaderAutoSaveChoices { get => _loaderAutoSaveChoices; set => SetProperty(ref _loaderAutoSaveChoices, value ?? []); }

    /// <summary>The interval now in the loader's d3dx.ini, or null when unknown.</summary>
    public int? LoaderAutoSaveSeconds { get => _loaderAutoSaveSeconds; set => SetProperty(ref _loaderAutoSaveSeconds, value); }

    /// <summary>Which d3dx.ini that is, or why the interval cannot be changed.</summary>
    public string LoaderAutoSaveNote { get => _loaderAutoSaveNote; set => SetProperty(ref _loaderAutoSaveNote, value ?? string.Empty); }

    /// <summary>A preset of background opacity, element opacity and compact view.</summary>
    public void ApplyOverlayPreset(OverlayAppearancePreset preset)
    {
        OverlayOpacity = preset.BackgroundOpacity;
        OverlayElementOpacity = preset.ElementOpacity;
        OverlayCompactView = preset.CompactView;
    }
}
