using CommunityToolkit.Mvvm.ComponentModel;
using IntegratedModManager.Core.Configuration;

namespace ModFolderCopier.WinUI;

/// <summary>
/// The language, theme, density and motion choices on the Settings page. The window localizes the
/// option labels and applies a choice when <see cref="ObservableObject.PropertyChanged"/> reports it.
/// </summary>
/// <remarks>
/// Combo boxes bind <c>SelectedIndex</c> two-way. Replacing an options list makes a combo box
/// report index -1, so negative indexes are ignored and the index is announced again after the
/// new labels, which puts the selection back.
/// </remarks>
public sealed partial class AppearanceSettingsViewModel : ObservableObject
{
    private readonly IReadOnlyList<string> _languageOptions = ["中文", "English"];
    private IReadOnlyList<string> _themeOptions = [];
    private IReadOnlyList<string> _densityOptions = [];
    private int _languageIndex;
    private ThemePreference _theme = ThemePreference.System;
    private int _densityIndex;
    private bool _reduceMotion;

    // Language names stay in their own language so either one can be found from the other.
    public IReadOnlyList<string> LanguageOptions => _languageOptions;

    /// <summary>System, Light, Dark, in <see cref="ThemePreference"/> order.</summary>
    public IReadOnlyList<string> ThemeOptions => _themeOptions;

    /// <summary>Comfortable, Compact.</summary>
    public IReadOnlyList<string> DensityOptions => _densityOptions;

    /// <summary>0 Chinese, 1 English.</summary>
    public int LanguageIndex
    {
        get => _languageIndex;
        set => SetIndex(ref _languageIndex, value, 2);
    }

    public ThemePreference Theme
    {
        get => _theme;
        set
        {
            if (SetProperty(ref _theme, value)) OnPropertyChanged(nameof(ThemeIndex));
        }
    }

    public int ThemeIndex
    {
        get => (int)_theme;
        set
        {
            if (Enum.IsDefined((ThemePreference)value)) Theme = (ThemePreference)value;
        }
    }

    /// <summary>0 comfortable, 1 compact.</summary>
    public int DensityIndex
    {
        get => _densityIndex;
        set => SetIndex(ref _densityIndex, value, 2);
    }

    public bool ReduceMotion
    {
        get => _reduceMotion;
        set => SetProperty(ref _reduceMotion, value);
    }

    public void SetOptionLabels(IReadOnlyList<string> themeOptions, IReadOnlyList<string> densityOptions)
    {
        _themeOptions = themeOptions;
        _densityOptions = densityOptions;
        OnPropertyChanged(nameof(ThemeOptions));
        OnPropertyChanged(nameof(DensityOptions));
        OnPropertyChanged(nameof(LanguageIndex));
        OnPropertyChanged(nameof(ThemeIndex));
        OnPropertyChanged(nameof(DensityIndex));
    }

    private void SetIndex(ref int field, int value, int count, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (value >= 0 && value < count) SetProperty(ref field, value, propertyName);
    }
}
