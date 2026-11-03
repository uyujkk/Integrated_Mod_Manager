using System.Globalization;

namespace ModFolderCopier.WinUI;

/// <summary>
/// UI text by key with its values, turned into text when it is shown, so a stored status follows a
/// language switch. A value that is itself a <see cref="UiMessage"/> is shown in the same language.
/// </summary>
internal sealed class UiMessage(string key, params object?[] values)
{
    public string Key { get; } = key;

    public IReadOnlyList<object?> Values { get; } = values;

    public string Resolve(string language)
    {
        string text = UiText.Get(language, Key);
        if (Values.Count == 0) return text;
        object?[] shown = Values.Select(value => value is UiMessage message ? message.Resolve(language) : value).ToArray();
        return string.Format(CultureInfo.CurrentCulture, text, shown);
    }
}
