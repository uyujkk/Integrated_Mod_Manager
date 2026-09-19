using System.Text.RegularExpressions;

namespace IntegratedModManager.Core;

public sealed record DetectedModShortcut(
    string Shortcut,
    string Section,
    string Behavior,
    string Target,
    string SourceFile);

public static class ModShortcutScanner
{
    private const int DefaultMaximumFiles = 200;
    private const int DefaultMaximumResults = 256;
    private const long MaximumIniBytes = 2L * 1024 * 1024;
    private static readonly Regex SectionPattern = new(
        @"^\s*\[(?<name>[^\]]+)\]\s*(?:;.*)?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex SettingPattern = new(
        @"^\s*(?<name>[A-Za-z_][\w.]*)\s*=\s*(?<value>.*)$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex VariableAssignmentPattern = new(
        @"^\s*(?<target>\$[^\s=]+)\s*=\s*(?<value>.*)$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex RunPattern = new(
        @"^\s*run\s*=\s*(?<target>[^;]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static IReadOnlyList<DetectedModShortcut> ScanDirectory(
        string modDirectory,
        int maximumResults = DefaultMaximumResults,
        int maximumFiles = DefaultMaximumFiles)
    {
        if (string.IsNullOrWhiteSpace(modDirectory)
            || !Directory.Exists(modDirectory)
            || maximumResults <= 0
            || maximumFiles <= 0)
        {
            return [];
        }

        var results = new List<DetectedModShortcut>();
        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            MatchCasing = MatchCasing.CaseInsensitive
        };

        int filesRead = 0;
        try
        {
            foreach (string iniPath in Directory.EnumerateFiles(modDirectory, "*.ini", options)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                if (filesRead++ >= maximumFiles || results.Count >= maximumResults)
                {
                    break;
                }

                try
                {
                    if (new FileInfo(iniPath).Length > MaximumIniBytes)
                    {
                        continue;
                    }

                    foreach (DetectedModShortcut shortcut in ParseFile(iniPath))
                    {
                        string identity = shortcut.Shortcut + "|" + shortcut.Section;
                        if (identities.Add(identity))
                        {
                            results.Add(shortcut);
                            if (results.Count >= maximumResults)
                            {
                                break;
                            }
                        }
                    }
                }
                catch (Exception) when (
                    !System.Diagnostics.Debugger.IsAttached)
                {
                    // A malformed or unreadable third-party INI must not break browsing.
                }
            }
        }
        catch (Exception) when (!System.Diagnostics.Debugger.IsAttached)
        {
            // Treat an inaccessible or changing Mod tree as having no detected shortcuts.
        }

        return results;
    }

    public static IReadOnlyList<DetectedModShortcut> ParseText(string text, string sourceFile = "mod.ini")
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        string[] lines = text.Split(["\r\n", "\n"], StringSplitOptions.None);
        var results = new List<DetectedModShortcut>();
        for (int index = 0; index < lines.Length;)
        {
            Match sectionMatch = SectionPattern.Match(lines[index]);
            if (!sectionMatch.Success || !sectionMatch.Groups["name"].Value.StartsWith("Key", StringComparison.OrdinalIgnoreCase))
            {
                index++;
                continue;
            }

            string section = sectionMatch.Groups["name"].Value.Trim();
            int next = index + 1;
            var body = new List<string>();
            while (next < lines.Length && !SectionPattern.IsMatch(lines[next]))
            {
                body.Add(lines[next]);
                next++;
            }

            string behavior = ReadSetting(body, "type");
            if (string.IsNullOrWhiteSpace(behavior))
            {
                behavior = InferBehavior(body, section);
            }
            string target = InferTarget(body, section);
            foreach (string rawKey in ReadSettings(body, "key"))
            {
                string shortcut = NormalizeKeyExpression(rawKey);
                if (!string.IsNullOrWhiteSpace(shortcut))
                {
                    results.Add(new DetectedModShortcut(shortcut, section, behavior, target, sourceFile));
                }
            }

            index = next;
        }

        return results;
    }

    private static IReadOnlyList<DetectedModShortcut> ParseFile(string iniPath)
    {
        using var stream = new FileStream(iniPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
        return ParseText(reader.ReadToEnd(), Path.GetFileName(iniPath));
    }

    private static IEnumerable<string> ReadSettings(IEnumerable<string> lines, string name)
    {
        foreach (string line in lines)
        {
            Match match = SettingPattern.Match(line);
            if (match.Success && string.Equals(match.Groups["name"].Value, name, StringComparison.OrdinalIgnoreCase))
            {
                string value = StripInlineComment(match.Groups["value"].Value);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    yield return value;
                }
            }
        }
    }

    private static string ReadSetting(IEnumerable<string> lines, string name) =>
        ReadSettings(lines, name).FirstOrDefault() ?? string.Empty;

    private static string InferTarget(IEnumerable<string> lines, string section)
    {
        foreach (string line in lines)
        {
            string candidate = StripInlineComment(line).Trim();
            if (candidate.StartsWith("condition", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Match variable = VariableAssignmentPattern.Match(candidate);
            if (variable.Success)
            {
                return variable.Groups["target"].Value.TrimStart('$');
            }

            Match run = RunPattern.Match(candidate);
            if (run.Success)
            {
                return Humanize(run.Groups["target"].Value.Replace("CommandList", string.Empty, StringComparison.OrdinalIgnoreCase));
            }
        }

        return Humanize(section.Length > 3 ? section[3..] : section);
    }

    private static string InferBehavior(IEnumerable<string> lines, string section)
    {
        if (section.Contains("cycle", StringComparison.OrdinalIgnoreCase))
        {
            return "cycle";
        }

        if (section.Contains("toggle", StringComparison.OrdinalIgnoreCase))
        {
            return "toggle";
        }

        if (section.Contains("hold", StringComparison.OrdinalIgnoreCase))
        {
            return "hold";
        }

        foreach (string line in lines)
        {
            string candidate = StripInlineComment(line).Trim();
            Match variable = VariableAssignmentPattern.Match(candidate);
            if (variable.Success)
            {
                string value = variable.Groups["value"].Value.Trim();
                if (value.Contains(','))
                {
                    return "cycle";
                }

                if (value.Contains('!')
                    || value.Contains("not ", StringComparison.OrdinalIgnoreCase))
                {
                    return "toggle";
                }
            }

            if (RunPattern.IsMatch(candidate))
            {
                return "run";
            }
        }

        return string.Empty;
    }

    private static string NormalizeKeyExpression(string value)
    {
        string[] tokens = value.Split([' ', '\t', '+'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var normalized = new List<string>();
        foreach (string rawToken in tokens)
        {
            string token = rawToken.Trim().Trim(',');
            string lower = token.ToLowerInvariant();
            if (lower is "no_modifiers" or "no_ctrl" or "no_control" or "no_shift" or "no_alt" or "no_win")
            {
                continue;
            }

            string mapped = lower switch
            {
                "ctrl" or "control" => "Ctrl",
                "shift" => "Shift",
                "alt" => "Alt",
                "win" or "windows" => "Win",
                _ => NormalizeKeyToken(token)
            };
            if (!string.IsNullOrWhiteSpace(mapped) && !normalized.Contains(mapped, StringComparer.OrdinalIgnoreCase))
            {
                normalized.Add(mapped);
            }
        }

        return string.Join("+", normalized);
    }

    private static string NormalizeKeyToken(string token)
    {
        string value = token.StartsWith("VK_", StringComparison.OrdinalIgnoreCase) ? token[3..] : token;
        return value.ToUpperInvariant() switch
        {
            "LBUTTON" => "Mouse Left",
            "RBUTTON" => "Mouse Right",
            "MBUTTON" => "Mouse Middle",
            "XBUTTON1" => "Mouse X1",
            "XBUTTON2" => "Mouse X2",
            "RETURN" => "Enter",
            "ESCAPE" => "Esc",
            "PRIOR" => "PageUp",
            "NEXT" => "PageDown",
            "SPACE" => "Space",
            "UP" => "Up",
            "DOWN" => "Down",
            "LEFT" => "Left",
            "RIGHT" => "Right",
            "OEM_PERIOD" => ".",
            "OEM_COMMA" => ",",
            "OEM_PLUS" => "+",
            "OEM_MINUS" => "-",
            _ when value.StartsWith("NUMPAD", StringComparison.OrdinalIgnoreCase) => "Num" + value[6..],
            _ when value.StartsWith("XB_", StringComparison.OrdinalIgnoreCase) => value.ToUpperInvariant(),
            _ when value.Length == 1 => value.ToUpperInvariant(),
            _ when Regex.IsMatch(value, "^(?i)F(?:[1-9]|1[0-9]|2[0-4])$") => value.ToUpperInvariant(),
            _ => Humanize(value)
        };
    }

    private static string StripInlineComment(string value)
    {
        int semicolon = value.IndexOf(';');
        return (semicolon >= 0 ? value[..semicolon] : value).Trim();
    }

    private static string Humanize(string value)
    {
        string separated = Regex.Replace(
            value.Replace('.', ' ').Replace('_', ' ').Replace('\\', ' '),
            "(?<=[a-z0-9])(?=[A-Z])",
            " ");
        return Regex.Replace(separated, @"\s+", " ").Trim();
    }
}
