namespace ModFolderCopier.WinUI;

// Picks one side of a Chinese/English text pair built at runtime (stored statuses, messages from
// other components). Text from the language files goes through UiText instead.
internal static class BetaLocalization
{
    public static string Resolve(bool useEnglish, string zh, string en)
        => useEnglish || string.IsNullOrWhiteSpace(zh) ? en : zh;

    public static string NormalizeChineseText(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
}
