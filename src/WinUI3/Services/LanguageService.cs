using IntegratedModManager.Core.Configuration;

namespace ModFolderCopier.WinUI;

/// <summary>The UI language and the text lookup behind MainWindow's T() and M().</summary>
internal sealed class LanguageService
{
    /// <summary>A language code with a file in Strings, such as zh-CN or en-US.</summary>
    public string Current { get; private set; } = UiLanguagePolicy.Chinese;

    /// <summary>Raised after the user switches language; each page relabels itself in its handler.</summary>
    public event EventHandler? LanguageChanged;

    // Chinese content (character names, wiki names, 万/亿) in Chinese; English content in every other language.
    public bool IsChinese => string.Equals(Current, UiLanguagePolicy.Chinese, StringComparison.Ordinal);

    // Text from the language files (Strings/*.json) by key.
    public string T(string key) => UiText.Get(Current, key);

    // Text with values: the language files use {0}, {1}, ... placeholders.
    public string T(string key, params object?[] values)
        => string.Format(System.Globalization.CultureInfo.CurrentCulture, T(key), values);

    // A stored message, shown in the current language.
    public string T(UiMessage message) => message.Resolve(Current);

    // Text by key and values kept for later, such as a status line that a language switch shows again.
    public static UiMessage M(string key, params object?[] values) => new(key, values);

    /// <summary>Sets the language found at startup, before anything is shown; nobody is told.</summary>
    public void Load(string language) => Current = language;

    /// <summary>Switches the language and tells every page.</summary>
    public void Change(string language)
    {
        Current = language;
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }
}
