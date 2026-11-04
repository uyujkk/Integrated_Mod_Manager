namespace ModFolderCopier.WinUI;

/// <summary>The applied theme and interface density, announced to the pages when they change.</summary>
internal sealed class AppearanceService
{
    /// <summary>Whether the window shows the dark theme; set before the window recolours itself.</summary>
    public bool IsDark { get; set; }

    /// <summary>Whether Windows uses a high-contrast theme; the window reads it at startup.</summary>
    public bool IsHighContrast { get; set; }

    /// <summary>The theme dictionary the window shows: "Light", "Dark" or "HighContrast".</summary>
    public string ThemeKey => IsHighContrast ? "HighContrast" : IsDark ? "Dark" : "Light";

    public bool IsCompact { get; private set; }

    /// <summary>Raised after the window took the new theme; pages recolour what they drew in code.</summary>
    public event EventHandler? ThemeChanged;

    /// <summary>Raised when a density is applied; pages adjust their padding and spacing.</summary>
    public event EventHandler? DensityChanged;

    public void NotifyThemeChanged() => ThemeChanged?.Invoke(this, EventArgs.Empty);

    public void ApplyDensity(bool compact)
    {
        IsCompact = compact;
        DensityChanged?.Invoke(this, EventArgs.Empty);
    }
}
