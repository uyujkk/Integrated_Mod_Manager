using System.Text.RegularExpressions;

namespace IntegratedModManager.Core;

public enum ModIniConflictKind
{
    /// <summary>Both Mods override the same 3DMigoto resource or shader hash.</summary>
    ResourceHash,

    /// <summary>Both Mods bind the same key; one press triggers both.</summary>
    Shortcut
}

/// <summary>One override section that 3DMigoto matches by hash.</summary>
public sealed record ModIniHashOverride(
    string SectionType,
    string Hash,
    string? MatchFirstIndex,
    string Section,
    string SourceFile);

/// <param name="ConditionalKeySections">
/// Key sections (as "file|section") that carry a condition such as <c>$active == 1</c>:
/// they only react while their own Mod is drawn on screen.
/// </param>
public sealed record ModIniProfile(
    IReadOnlyList<ModIniHashOverride> Overrides,
    IReadOnlyList<DetectedModShortcut> Shortcuts,
    IReadOnlySet<string> ConditionalKeySections)
{
    public static ModIniProfile Empty { get; } = new([], [], new HashSet<string>());

    public bool IsConditional(DetectedModShortcut shortcut) =>
        ConditionalKeySections.Contains(shortcut.SourceFile + "|" + shortcut.Section);
}

/// <summary>
/// Conflicts with one installed Mod, grouped by kind. <see cref="Example"/> is the first
/// shared identity (a hash or a key) and the sections involved, for display.
/// </summary>
public sealed record ModIniConflictSummary(
    string InstalledModName,
    ModIniConflictKind Kind,
    int Count,
    string Example,
    string CandidateSection,
    string InstalledSection);

/// <summary>
/// Read-only INI comparison used before deployment. Two skins for the same character
/// rarely share a file path but almost always override the same hashes, which the
/// path-based check cannot see. Results are warnings: the loader still decides what
/// happens, and conditional (if/else) logic is not evaluated.
/// </summary>
public static class ModIniConflictDetector
{
    private const int DefaultMaximumFiles = 200;
    private const int DefaultMaximumInstalledMods = 2000;
    private const long MaximumIniBytes = 2L * 1024 * 1024;
    private const string DisabledPrefix = "DISABLED";

    private static readonly Regex SectionPattern = new(
        @"^\s*\[(?<name>[^\]]+)\]\s*(?:;.*)?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex SettingPattern = new(
        @"^\s*(?<name>[A-Za-z_][\w.]*)\s*=\s*(?<value>.*)$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex HashValuePattern = new(
        @"^(?:0x)?(?<hex>[0-9a-fA-F]{6,32})$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static ModIniProfile ParseText(string text, string sourceFile = "mod.ini")
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return ModIniProfile.Empty;
        }

        string[] lines = text.Split(["\r\n", "\n"], StringSplitOptions.None);
        var overrides = new List<ModIniHashOverride>();
        var conditionalKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < lines.Length;)
        {
            Match sectionMatch = SectionPattern.Match(lines[index]);
            if (!sectionMatch.Success)
            {
                index++;
                continue;
            }

            string section = sectionMatch.Groups["name"].Value.Trim();
            int next = index + 1;
            string? hash = null;
            string? matchFirstIndex = null;
            bool resolvedByAuthor = false;
            bool hasCondition = false;
            while (next < lines.Length && !SectionPattern.IsMatch(lines[next]))
            {
                Match setting = SettingPattern.Match(lines[next]);
                if (setting.Success)
                {
                    string name = setting.Groups["name"].Value;
                    string value = StripInlineComment(setting.Groups["value"].Value);
                    if (name.Equals("hash", StringComparison.OrdinalIgnoreCase))
                    {
                        hash ??= NormalizeHash(value);
                    }
                    else if (name.Equals("match_first_index", StringComparison.OrdinalIgnoreCase))
                    {
                        matchFirstIndex ??= value.Trim();
                    }
                    else if (name.Equals("condition", StringComparison.OrdinalIgnoreCase) && value.Length > 0)
                    {
                        hasCondition = true;
                    }
                    else if (name.Equals("match_priority", StringComparison.OrdinalIgnoreCase)
                        || (name.Equals("allow_duplicate_hash", StringComparison.OrdinalIgnoreCase)
                            && !value.Equals("false", StringComparison.OrdinalIgnoreCase)))
                    {
                        // The author already resolved the overlap with the loader's own rules.
                        resolvedByAuthor = true;
                    }
                }

                next++;
            }

            if (hasCondition && section.StartsWith("Key", StringComparison.OrdinalIgnoreCase))
            {
                conditionalKeys.Add(sourceFile + "|" + section);
            }

            string? sectionType = GetHashSectionType(section);
            if (sectionType is not null && hash is not null && !resolvedByAuthor)
            {
                overrides.Add(new ModIniHashOverride(
                    sectionType,
                    hash,
                    string.IsNullOrWhiteSpace(matchFirstIndex) ? null : matchFirstIndex,
                    section,
                    sourceFile));
            }

            index = next;
        }

        return new ModIniProfile(overrides, ModShortcutScanner.ParseText(text, sourceFile), conditionalKeys);
    }

    /// <summary>
    /// Reads every INI the loader would read in <paramref name="modDirectory"/>: files or
    /// folders whose name starts with DISABLED are skipped, as 3DMigoto skips them.
    /// </summary>
    public static ModIniProfile ScanDirectory(string modDirectory, int maximumFiles = DefaultMaximumFiles)
    {
        if (string.IsNullOrWhiteSpace(modDirectory) || !Directory.Exists(modDirectory) || maximumFiles <= 0)
        {
            return ModIniProfile.Empty;
        }

        var overrides = new List<ModIniHashOverride>();
        var shortcuts = new List<DetectedModShortcut>();
        var conditionalKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
                string relative = Path.GetRelativePath(modDirectory, iniPath);
                if (IsDisabledPath(relative))
                {
                    continue;
                }

                if (filesRead++ >= maximumFiles)
                {
                    break;
                }

                try
                {
                    if (new FileInfo(iniPath).Length > MaximumIniBytes)
                    {
                        continue;
                    }

                    using var stream = new FileStream(iniPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
                    ModIniProfile profile = ParseText(reader.ReadToEnd(), relative);
                    overrides.AddRange(profile.Overrides);
                    shortcuts.AddRange(profile.Shortcuts);
                    conditionalKeys.UnionWith(profile.ConditionalKeySections);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // An unreadable third-party INI must not block deployment checks.
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A changing or inaccessible Mod tree yields what was read so far.
        }

        return new ModIniProfile(overrides, shortcuts, conditionalKeys);
    }

    /// <summary>
    /// Compares <paramref name="candidateDirectory"/> with each Mod folder directly under
    /// <paramref name="targetRoot"/> that the loader would load, except the ignored ones
    /// (the candidate's own target and folders the deployment will replace).
    /// </summary>
    public static IReadOnlyList<ModIniConflictSummary> FindConflictsInTarget(
        string candidateDirectory,
        string targetRoot,
        IEnumerable<string> ignoredTargets,
        int maximumInstalledMods = DefaultMaximumInstalledMods)
    {
        if (!Directory.Exists(targetRoot))
        {
            return [];
        }

        ModIniProfile candidate = ScanDirectory(candidateDirectory);
        if (candidate.Overrides.Count == 0 && candidate.Shortcuts.Count == 0)
        {
            return [];
        }

        HashSet<string> ignored = ignoredTargets
            .Select(path => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        string candidateFullPath = Path.GetFullPath(candidateDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var installed = new List<(string Name, ModIniProfile Profile)>();
        foreach (string directory in Directory.EnumerateDirectories(targetRoot)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Take(maximumInstalledMods))
        {
            string name = Path.GetFileName(directory);
            string fullPath = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (IsDisabledName(name) || ignored.Contains(fullPath)
                || string.Equals(fullPath, candidateFullPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            installed.Add((name, ScanDirectory(directory)));
        }

        return FindConflicts(candidate, installed);
    }

    public static IReadOnlyList<ModIniConflictSummary> FindConflicts(
        ModIniProfile candidate,
        IEnumerable<(string Name, ModIniProfile Profile)> installedMods)
    {
        var summaries = new List<ModIniConflictSummary>();
        ILookup<string, ModIniHashOverride> candidateByHash = candidate.Overrides
            .ToLookup(item => item.SectionType + "|" + item.Hash, StringComparer.OrdinalIgnoreCase);
        ILookup<string, DetectedModShortcut> candidateByKey = candidate.Shortcuts
            .Where(item => !string.IsNullOrWhiteSpace(item.Shortcut))
            .ToLookup(item => item.Shortcut, StringComparer.OrdinalIgnoreCase);

        foreach ((string name, ModIniProfile profile) in installedMods)
        {
            var hashMatches = new List<(string Identity, string CandidateSection, string InstalledSection)>();
            var seenHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ModIniHashOverride installedOverride in profile.Overrides)
            {
                string key = installedOverride.SectionType + "|" + installedOverride.Hash;
                foreach (ModIniHashOverride candidateOverride in candidateByHash[key])
                {
                    if (!DrawCallsOverlap(candidateOverride.MatchFirstIndex, installedOverride.MatchFirstIndex))
                    {
                        continue;
                    }

                    string identity = installedOverride.MatchFirstIndex is null && candidateOverride.MatchFirstIndex is null
                        ? installedOverride.Hash
                        : $"{installedOverride.Hash}@{candidateOverride.MatchFirstIndex ?? installedOverride.MatchFirstIndex}";
                    if (seenHashes.Add(identity))
                    {
                        hashMatches.Add((identity, candidateOverride.Section, installedOverride.Section));
                    }

                    break;
                }
            }

            if (hashMatches.Count > 0)
            {
                summaries.Add(new ModIniConflictSummary(name, ModIniConflictKind.ResourceHash, hashMatches.Count,
                    hashMatches[0].Identity, hashMatches[0].CandidateSection, hashMatches[0].InstalledSection));
            }

            var keyMatches = new List<(string Key, string CandidateSection, string InstalledSection)>();
            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool drawnTogether = hashMatches.Count > 0;
            foreach (DetectedModShortcut installedShortcut in profile.Shortcuts)
            {
                // Two keys gated on their own Mod being visible collide only when both Mods
                // draw at once, which in practice means they override the same character.
                DetectedModShortcut? candidateShortcut = candidateByKey[installedShortcut.Shortcut]
                    .FirstOrDefault(item => drawnTogether || !candidate.IsConditional(item) || !profile.IsConditional(installedShortcut));
                if (candidateShortcut is not null && seenKeys.Add(installedShortcut.Shortcut))
                {
                    keyMatches.Add((installedShortcut.Shortcut, candidateShortcut.Section, installedShortcut.Section));
                }
            }

            if (keyMatches.Count > 0)
            {
                summaries.Add(new ModIniConflictSummary(name, ModIniConflictKind.Shortcut, keyMatches.Count,
                    string.Join(", ", keyMatches.Select(match => match.Key).Take(4)),
                    keyMatches[0].CandidateSection, keyMatches[0].InstalledSection));
            }
        }

        return summaries
            .OrderBy(summary => summary.Kind)
            .ThenByDescending(summary => summary.Count)
            .ThenBy(summary => summary.InstalledModName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static bool IsDisabledName(string name) =>
        name.StartsWith(DisabledPrefix, StringComparison.OrdinalIgnoreCase);

    private static bool IsDisabledPath(string relativePath) =>
        relativePath.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
            .Any(IsDisabledName);

    // An override without match_first_index applies to every draw call that uses the
    // hash, so it overlaps any index; two explicit indices overlap only when equal.
    private static bool DrawCallsOverlap(string? left, string? right) =>
        left is null || right is null || string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static string? GetHashSectionType(string section)
    {
        if (section.StartsWith("TextureOverride", StringComparison.OrdinalIgnoreCase))
        {
            return "TextureOverride";
        }

        if (section.StartsWith("ShaderOverride", StringComparison.OrdinalIgnoreCase))
        {
            return "ShaderOverride";
        }

        return null;
    }

    private static string? NormalizeHash(string value)
    {
        Match match = HashValuePattern.Match(value.Trim());
        return match.Success ? match.Groups["hex"].Value.ToLowerInvariant() : null;
    }

    private static string StripInlineComment(string value)
    {
        int semicolon = value.IndexOf(';');
        return (semicolon >= 0 ? value[..semicolon] : value).Trim();
    }
}
