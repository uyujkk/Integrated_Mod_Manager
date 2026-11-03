using System.Text.RegularExpressions;

namespace IntegratedModManager.Core.Presets;

/// <summary>
/// The loader's settings_auto_save_interval in d3dx.ini [System]: seconds between writes of persist
/// values to d3dx_user.ini. Only that number changes; comments, spacing and line endings stay.
/// </summary>
public static partial class LoaderAutoSaveSetting
{
    public static readonly IReadOnlyList<int> Choices = [5, 10, 30, 60];

    public static int? Read(string d3dxIni) => Find(d3dxIni) is { } match ? int.Parse(match.Groups["value"].Value) : null;

    /// <summary>The file with the new interval, or null when [System] has no such line to change.</summary>
    public static string? Write(string d3dxIni, int seconds)
    {
        if (seconds is < -1 or > 86400) throw new ArgumentOutOfRangeException(nameof(seconds));
        return Find(d3dxIni) is { } match
            ? d3dxIni[..match.Groups["value"].Index] + seconds + d3dxIni[(match.Groups["value"].Index + match.Groups["value"].Length)..]
            : null;
    }

    private static Match? Find(string text)
    {
        bool inSystem = false;
        int offset = 0;
        foreach (string line in text.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith('[')) inSystem = trimmed.StartsWith("[System]", StringComparison.OrdinalIgnoreCase);
            else if (inSystem && Setting().Match(line) is { Success: true } match)
            {
                // Re-match on the whole text so the group index is a position in it.
                return Setting().Match(text, offset, line.Length);
            }
            offset += line.Length + 1;
        }
        return null;
    }

    [GeneratedRegex(@"^[ \t]*settings_auto_save_interval[ \t]*=[ \t]*(?<value>-?\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex Setting();
}
