using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace IntegratedModManager.Core;

// Deliberately independent of third-party implementations. This handles only scalar
// global persist state; Mod INIs are read-only declaration/namespace references.
public sealed record ModPersistentValue(string RelativeIniPath, string Variable, string RuntimeKey, string Value);

public sealed record ModPersistentFileEdit(string Path, byte[] OriginalBytes, byte[] UpdatedBytes);

public sealed record ModPersistentSourceState(
    string ModDirectory, string DeployedRelativePath, IReadOnlyList<ModPersistentValue> Values);

public enum ModPersistentChangeKind { Changed, Added, Unchanged }
public sealed record ModPersistentValueChange(
    ModPersistentValue Saved, string? PreviousValue, ModPersistentChangeKind Kind);

public sealed record ModPersistentRuntimeRestorePlan(
    ModPersistentFileEdit Edit, IReadOnlyList<ModPersistentValue> Values,
    int ChangedCount, int AddedCount, int UnchangedCount,
    string ModDirectory, string DeployedRelativePath)
{
    public IReadOnlyList<ModPersistentSourceState> Sources { get; init; } = [];
    public IReadOnlyList<ModPersistentValueChange> Changes { get; init; } = [];
}

public static class ModPersistentPresetEngine
{
    private const int MaxIniFiles = 256;
    private const long MaxIniBytes = 2 * 1024 * 1024;
    private const long MaxUserIniBytes = 16 * 1024 * 1024;
    private static readonly Regex Section = new(@"^\s*\[(?<name>[^\]]+)\]\s*(?:;.*)?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Namespace = new(@"^\s*namespace\s*=\s*(?<name>[^;\r\n]+)\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Declaration = new(@"^(?<lead>\s*global\s+persist\s+)(?<name>\$[A-Za-z_][A-Za-z_0-9]*)(?<tail>\s*(?:=\s*(?<default>[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?)\s*)?)$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex RuntimeAssignment = new(@"^(?<lead>\s*(?<key>\$\\[^=\r\n;]+?)\s*=\s*)(?<value>[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?)(?<tail>\s*(?:;[^\r\n]*)?)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex RuntimeKeyAssignment = new(@"^\s*(?<key>\$\\[^=\r\n;]+?)\s*=", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Numeric = new(@"^[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static IReadOnlyList<ModPersistentValue> Capture(
        string modDirectory, string deployedRelativePath, string userIniPath)
    {
        IReadOnlyDictionary<string, string> runtime = ReadRuntimeValues(userIniPath);
        var captured = new List<ModPersistentValue>();
        foreach (string ini in EnumerateSafeIniFiles(modDirectory))
        {
            string relative = Path.GetRelativePath(modDirectory, ini).Replace('/', '\\');
            string defaultNamespace = "Mods\\" + NormalizeRelative(deployedRelativePath) + "\\" + relative;
            string text = Decode(File.ReadAllBytes(ini), out _);
            foreach (DeclarationInfo declaration in ParseDeclarations(text, defaultNamespace))
            {
                if (runtime.TryGetValue(declaration.RuntimeKey, out string? value))
                {
                    captured.Add(new ModPersistentValue(relative, declaration.Variable, declaration.RuntimeKey, value));
                }
            }
        }

        return captured
            .GroupBy(value => value.RelativeIniPath + "|" + value.Variable, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1)
            .Select(group => group.Single())
            .OrderBy(value => value.RelativeIniPath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(value => value.Variable, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    // A combination must not silently save defaults or omit declared runtime values.
    // All Mods are read from the same immutable loader snapshot.
    public static IReadOnlyList<ModPersistentSourceState> CaptureCombination(
        IReadOnlyList<(string ModDirectory, string DeployedRelativePath)> mods, string userIniPath)
    {
        if (!string.Equals(Path.GetFileName(userIniPath), "d3dx_user.ini", StringComparison.OrdinalIgnoreCase)
            || !File.Exists(userIniPath) || (File.GetAttributes(userIniPath) & FileAttributes.ReparsePoint) != 0
            || new FileInfo(userIniPath).Length > MaxUserIniBytes)
            throw new InvalidDataException("Choose a real d3dx_user.ini within the 16 MB safety limit.");
        byte[] before = File.ReadAllBytes(userIniPath);
        IReadOnlyDictionary<string, string> runtime = ReadRuntimeValues(userIniPath);
        var result = new List<ModPersistentSourceState>();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in mods)
        {
            var values = new List<ModPersistentValue>();
            foreach (string ini in EnumerateSafeIniFiles(mod.ModDirectory))
            {
                string relative = Path.GetRelativePath(mod.ModDirectory, ini).Replace('/', '\\');
                string defaultNamespace = "Mods\\" + NormalizeRelative(mod.DeployedRelativePath) + "\\" + relative;
                foreach (DeclarationInfo declaration in ParseDeclarations(Decode(File.ReadAllBytes(ini), out _), defaultNamespace))
                {
                    if (!keys.Add(declaration.RuntimeKey))
                        throw new InvalidDataException("Two declarations share a runtime key; this combination cannot be captured safely.");
                    if (!runtime.TryGetValue(declaration.RuntimeKey, out string? value))
                        throw new InvalidDataException($"Missing saved loader value: {Path.GetFileName(mod.ModDirectory)} · {declaration.Variable}. Save in game first; defaults are not used.");
                    values.Add(new ModPersistentValue(relative, declaration.Variable, declaration.RuntimeKey, value));
                }
            }
            result.Add(new ModPersistentSourceState(mod.ModDirectory, mod.DeployedRelativePath, values));
        }
        if (!File.ReadAllBytes(userIniPath).SequenceEqual(before))
            throw new IOException("d3dx_user.ini changed during capture. Save again after the loader has finished writing.");
        // The planner also rejects duplicate/malformed selected assignments in the file.
        BuildRuntimeRestorePlan(userIniPath, result.SelectMany(source => source.Values).ToArray());
        return result;
    }

    private static void ValidateSlotDeclarations(
        string modDirectory, string deployedRelativePath, IReadOnlyCollection<ModPersistentValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
        {
            throw new InvalidDataException("The preset has no supported persistent values.");
        }

        string root = ValidateRoot(modDirectory);
        string deployedPath = NormalizeRelative(deployedRelativePath);
        var pending = new HashSet<string>(values.Select(value => value.RelativeIniPath + "|" + value.Variable), StringComparer.OrdinalIgnoreCase);
        if (pending.Count != values.Count)
            throw new InvalidDataException("The slot contains duplicate variable declarations.");
        foreach (IGrouping<string, ModPersistentValue> group in values.GroupBy(value => value.RelativeIniPath, StringComparer.OrdinalIgnoreCase))
        {
            string relative = NormalizeRelative(group.Key);
            if (!relative.EndsWith(".ini", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The preset refers to a non-INI file.");
            }
            if (relative.Split('\\').Any(part => part.StartsWith("DISABLED", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("The preset refers to a disabled INI.");

            string path = Path.GetFullPath(Path.Combine(root, relative));
            if (!IsInside(root, path) || !File.Exists(path) || HasReparsePointBetween(root, path))
            {
                throw new InvalidDataException("A preset INI is missing or outside the selected Mod.");
            }

            byte[] original = File.ReadAllBytes(path);
            if (original.LongLength > MaxIniBytes)
            {
                throw new InvalidDataException("A preset INI exceeds the safety limit.");
            }

            string text = Decode(original, out _);
            string defaultNamespace = "Mods\\" + deployedPath + "\\" + relative;
            Dictionary<string, ModPersistentValue> desired = group.ToDictionary(value => value.Variable, StringComparer.OrdinalIgnoreCase);
            foreach (ModPersistentValue value in group)
            {
                if (!IsSafeVariable(value.Variable) || !IsSafeRuntimeKey(value.RuntimeKey) || !IsSafeNumber(value.Value))
                {
                    throw new InvalidDataException("The preset contains an unsupported variable or value.");
                }
            }

            List<DeclarationInfo> declarations = ParseDeclarations(text, defaultNamespace).ToList();
            var matched = declarations.Where(item => desired.ContainsKey(item.Variable)).ToList();
            if (matched.GroupBy(item => item.Variable, StringComparer.OrdinalIgnoreCase).Any(item => item.Count() != 1))
            {
                throw new InvalidDataException("An INI contains ambiguous persistent declarations.");
            }

            foreach (DeclarationInfo item in matched)
            {
                ModPersistentValue wanted = desired[item.Variable];
                if (!string.Equals(wanted.RuntimeKey, item.RuntimeKey, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("The preset no longer matches the Mod namespace.");
                }

                pending.Remove(wanted.RelativeIniPath + "|" + wanted.Variable);
            }

        }

        if (pending.Count != 0)
        {
            throw new InvalidDataException("The preset refers to declarations that no longer exist.");
        }

    }

    // Restore only validated keys for this Mod, never replace shared state with a slot.
    // All other content (including whitespace/comments and other sections) is preserved.
    public static ModPersistentRuntimeRestorePlan PlanRuntimeRestore(
        string modDirectory, string deployedRelativePath, string userIniPath,
        IReadOnlyCollection<ModPersistentValue> values)
    {
        ValidateSlotDeclarations(modDirectory, deployedRelativePath, values);
        return BuildRuntimeRestorePlan(userIniPath, values) with
        {
            ModDirectory = modDirectory,
            DeployedRelativePath = deployedRelativePath,
            Sources = [new ModPersistentSourceState(modDirectory, deployedRelativePath, values.ToArray())]
        };
    }

    public static ModPersistentRuntimeRestorePlan PlanCombinationRuntimeRestore(
        IReadOnlyList<ModPersistentSourceState> sources, string userIniPath)
    {
        ArgumentNullException.ThrowIfNull(sources);
        foreach (ModPersistentSourceState source in sources)
        {
            ValidateSourceState(source);
        }
        ModPersistentValue[] values = sources.SelectMany(source => source.Values).ToArray();
        if (values.Length == 0) throw new InvalidDataException("The combination has no persistent parameters to restore.");
        return BuildRuntimeRestorePlan(userIniPath, values) with { Sources = sources.ToArray() };
    }

    // Read-only declaration/namespace validation for each preview item. Keeping
    // it here prevents the UI/preview service from inventing a second parser.
    public static void ValidateSourceState(ModPersistentSourceState source)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(source.Values);
        if (source.Values.Any(value => value is null || string.IsNullOrWhiteSpace(value.RelativeIniPath)
            || string.IsNullOrWhiteSpace(value.Variable) || string.IsNullOrWhiteSpace(value.RuntimeKey)
            || string.IsNullOrWhiteSpace(value.Value)))
            throw new InvalidDataException("The preset contains an incomplete persistent value.");
        ValidateRoot(source.ModDirectory);
        NormalizeRelative(source.DeployedRelativePath);
        if (source.Values.Count > 0)
            ValidateSlotDeclarations(source.ModDirectory, source.DeployedRelativePath, source.Values.ToArray());
    }

    private static ModPersistentRuntimeRestorePlan BuildRuntimeRestorePlan(
        string userIniPath, IReadOnlyCollection<ModPersistentValue> values)
    {
        string path = Path.GetFullPath(userIniPath);
        if (!string.Equals(Path.GetFileName(path), "d3dx_user.ini", StringComparison.OrdinalIgnoreCase)
            || !File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Choose a real d3dx_user.ini file, not a file link.");
        if (new FileInfo(path).Length > MaxUserIniBytes)
            throw new InvalidDataException("The loader state exceeds the 16 MB safety limit.");
        byte[] original = File.ReadAllBytes(path);
        if (original.LongLength > MaxUserIniBytes)
            throw new InvalidDataException("The loader state exceeds the 16 MB safety limit.");
        string text = Decode(original, out Encoding encoding);
        var desired = new Dictionary<string, ModPersistentValue>(StringComparer.OrdinalIgnoreCase);
        foreach (ModPersistentValue value in values)
            if (!desired.TryAdd(NormalizeRuntimeKey(value.RuntimeKey), value))
                throw new InvalidDataException("The slot contains ambiguous duplicate runtime keys.");

        var replacements = new List<(int Start, int Length, string Text)>();
        var changes = new List<ModPersistentValueChange>();
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int constantsCount = 0, constantsEnd = text.Length, offset = 0, changed = 0, unchanged = 0;
        bool inConstants = false;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            Match section = Section.Match(line);
            if (section.Success)
            {
                if (inConstants) constantsEnd = offset;
                inConstants = section.Groups["name"].Value.Equals("Constants", StringComparison.OrdinalIgnoreCase);
                if (inConstants) constantsCount++;
            }
            else if (inConstants)
            {
                Match keyAssignment = RuntimeKeyAssignment.Match(line);
                string key = keyAssignment.Success ? NormalizeRuntimeKey(keyAssignment.Groups["key"].Value) : string.Empty;
                if (desired.TryGetValue(key, out ModPersistentValue? wanted))
                {
                    if (!found.Add(key)) throw new InvalidDataException("A selected key occurs more than once in d3dx_user.ini.");
                    Match assignment = RuntimeAssignment.Match(line);
                    if (!assignment.Success || !IsSafeNumber(assignment.Groups["value"].Value))
                        throw new InvalidDataException("A selected runtime key has an unsupported value.");
                    Group valueGroup = assignment.Groups["value"];
                    if (valueGroup.Value == wanted.Value)
                    {
                        unchanged++;
                        changes.Add(new(wanted, valueGroup.Value, ModPersistentChangeKind.Unchanged));
                    }
                    else
                    {
                        changed++;
                        changes.Add(new(wanted, valueGroup.Value, ModPersistentChangeKind.Changed));
                        replacements.Add((offset + valueGroup.Index, valueGroup.Length, wanted.Value));
                    }
                }
            }
            offset += raw.Length + 1;
        }
        if (constantsCount != 1)
            throw new InvalidDataException("d3dx_user.ini must have exactly one [Constants] section.");

        ModPersistentValue[] missing = desired.Where(pair => !found.Contains(pair.Key)).Select(pair => pair.Value).ToArray();
        changes.AddRange(missing.Select(value => new ModPersistentValueChange(value, null, ModPersistentChangeKind.Added)));
        if (missing.Length > 0)
        {
            string newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            string prefix = constantsEnd > 0 && text[constantsEnd - 1] != '\n' ? newline : string.Empty;
            string suffix = constantsEnd < text.Length || text.EndsWith('\n') ? newline : string.Empty;
            string added = prefix + string.Join(newline, missing.Select(value => $"{value.RuntimeKey} = {value.Value}")) + suffix;
            replacements.Add((constantsEnd, 0, added));
        }
        var updated = new StringBuilder(text);
        foreach (var replacement in replacements.OrderByDescending(item => item.Start))
            updated.Remove(replacement.Start, replacement.Length).Insert(replacement.Start, replacement.Text);
        return new ModPersistentRuntimeRestorePlan(
            new ModPersistentFileEdit(path, original, Encode(updated.ToString(), encoding)),
            values.ToArray(), changed, missing.Length, unchanged, string.Empty, string.Empty)
        { Changes = changes.ToArray() };
    }

    // The caller must close the game/loader first. A concurrently running loader can
    // overwrite disk state later; file verification cannot prove game-side restoration.
    public static string? ApplyRuntimeRestore(ModPersistentRuntimeRestorePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Sources.Count == 0)
            ValidateSlotDeclarations(plan.ModDirectory, plan.DeployedRelativePath, plan.Values);
        else
        {
            foreach (ModPersistentSourceState source in plan.Sources)
            {
                ValidateRoot(source.ModDirectory);
                if (source.Values.Count > 0)
                    ValidateSlotDeclarations(source.ModDirectory, source.DeployedRelativePath, source.Values.ToArray());
            }
        }
        if (!string.Equals(Path.GetFileName(plan.Edit.Path), "d3dx_user.ini", StringComparison.OrdinalIgnoreCase)
            || (File.GetAttributes(plan.Edit.Path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("The planned loader state is no longer a real d3dx_user.ini file.");
        if (!File.ReadAllBytes(plan.Edit.Path).SequenceEqual(plan.Edit.OriginalBytes))
            throw new IOException("d3dx_user.ini changed after the preview. No values were restored; prepare a new preview.");
        string? backup = plan.ChangedCount + plan.AddedCount == 0 ? null
            : ApplyPlannedEdits([plan.Edit]).Single();
        try
        {
            if (!File.ReadAllBytes(plan.Edit.Path).SequenceEqual(plan.Edit.UpdatedBytes))
                throw new IOException("The loader state changed during verification. Inspect the backup and keep the loader closed.");
            IReadOnlyDictionary<string, string> actual = ReadRuntimeValues(plan.Edit.Path);
            if (plan.Values.Any(value => !actual.TryGetValue(NormalizeRuntimeKey(value.RuntimeKey), out string? restored)
                || restored != value.Value))
                throw new IOException("The loader state did not pass read-back verification. Check the backup and keep the loader closed.");
        }
        catch
        {
            if (backup is not null) RollbackRuntimeRestore(plan, backup);
            throw;
        }
        return backup;
    }

    // Roll back only this exact write. Never overwrite a later loader/user edit.
    public static void RollbackRuntimeRestore(ModPersistentRuntimeRestorePlan plan, string backupPath)
    {
        if (!File.ReadAllBytes(backupPath).SequenceEqual(plan.Edit.OriginalBytes))
            throw new IOException("The state backup no longer matches the original loader file.");
        if (!File.ReadAllBytes(plan.Edit.Path).SequenceEqual(plan.Edit.UpdatedBytes))
            throw new IOException("The loader state changed after restoration. Automatic rollback was refused; inspect the backup.");
        ApplyPlannedEdits([new ModPersistentFileEdit(plan.Edit.Path, plan.Edit.UpdatedBytes, plan.Edit.OriginalBytes)]);
    }

    // Backups intentionally remain beside the edited files so a failed game-side
    // reload never destroys the user's previous defaults.
    private static IReadOnlyList<string> ApplyPlannedEdits(IReadOnlyList<ModPersistentFileEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(edits);
        foreach (ModPersistentFileEdit edit in edits)
        {
            if (!File.Exists(edit.Path) || !File.ReadAllBytes(edit.Path).SequenceEqual(edit.OriginalBytes))
                throw new IOException("The loader state changed after the preview was prepared. No files were written.");
        }

        var backups = new List<(string Path, string Backup)>();
        try
        {
            foreach (ModPersistentFileEdit edit in edits)
            {
                string suffix = ".imm-persist-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)
                    + "-" + Guid.NewGuid().ToString("N");
                string temporary = edit.Path + suffix + ".tmp";
                string backup = edit.Path + suffix + ".bak";
                try
                {
                    File.WriteAllBytes(temporary, edit.UpdatedBytes);
                    File.Replace(temporary, edit.Path, backup);
                    backups.Add((edit.Path, backup));
                }
                finally
                {
                    if (File.Exists(temporary)) File.Delete(temporary);
                }
            }

            return backups.Select(item => item.Backup).ToArray();
        }
        catch
        {
            foreach ((string path, string backup) in backups.AsEnumerable().Reverse())
            {
                File.Copy(backup, path, overwrite: true);
            }

            throw;
        }
    }

    public static IReadOnlyDictionary<string, string> ReadRuntimeValues(string userIniPath)
    {
        if (string.IsNullOrWhiteSpace(userIniPath) || !File.Exists(userIniPath)
            || new FileInfo(userIniPath).Length > MaxUserIniBytes)
        {
            throw new InvalidDataException("Choose an existing d3dx_user.ini within the 16 MB safety limit.");
        }

        string text = Decode(File.ReadAllBytes(userIniPath), out _);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        bool inConstants = false;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            Match section = Section.Match(line);
            if (section.Success)
            {
                inConstants = section.Groups["name"].Value.Equals("Constants", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inConstants || line.StartsWith(';')) continue;
            Match assignment = RuntimeAssignment.Match(line);
            if (assignment.Success && IsSafeRuntimeKey(assignment.Groups["key"].Value)
                && IsSafeNumber(assignment.Groups["value"].Value))
            {
                result[NormalizeRuntimeKey(assignment.Groups["key"].Value)] = assignment.Groups["value"].Value;
            }
        }

        return result;
    }

    private static IEnumerable<DeclarationInfo> ParseDeclarations(string text, string defaultNamespace)
    {
        string effectiveNamespace = NormalizeNamespace(defaultNamespace);
        bool inConstants = false;
        bool firstSectionSeen = false;
        string[] lines = text.Split('\n');
        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index].TrimEnd('\r');
            Match section = Section.Match(line);
            if (section.Success)
            {
                firstSectionSeen = true;
                inConstants = section.Groups["name"].Value.Equals("Constants", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!firstSectionSeen)
            {
                Match namespaceMatch = Namespace.Match(line);
                if (namespaceMatch.Success) effectiveNamespace = NormalizeNamespace(namespaceMatch.Groups["name"].Value);
            }
            else if (inConstants)
            {
                Match declaration = Declaration.Match(line);
                if (declaration.Success)
                {
                    string variable = declaration.Groups["name"].Value;
                    yield return new DeclarationInfo(index, variable,
                        NormalizeRuntimeKey("$\\" + effectiveNamespace + "\\" + variable[1..]));
                }
            }
        }
    }

    private static IReadOnlyList<string> EnumerateSafeIniFiles(string modDirectory)
    {
        string root = ValidateRoot(modDirectory);
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false };
        string[] files = Directory.EnumerateFiles(root, "*.ini", options)
            .Where(path => !Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar)
                .Any(part => part.StartsWith("DISABLED", StringComparison.OrdinalIgnoreCase)))
            .Take(MaxIniFiles + 1).ToArray();
        if (files.Length > MaxIniFiles) throw new InvalidDataException("The Mod has too many INI files for this prototype.");
        foreach (string path in files)
        {
            if (new FileInfo(path).Length > MaxIniBytes || HasReparsePointBetween(root, path))
                throw new InvalidDataException("An INI exceeds the safety limit or traverses a link.");
        }

        return files;
    }

    private static string ValidateRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) throw new DirectoryNotFoundException("Select an existing Mod folder.");
        string root = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Select a real Mod folder, not a directory link.");
        return root;
    }

    private static bool HasReparsePointBetween(string root, string path)
    {
        string current = Path.GetDirectoryName(path)!;
        while (IsInside(root, current) && !current.Equals(root, StringComparison.OrdinalIgnoreCase))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return true;
            current = Path.GetDirectoryName(current)!;
        }

        return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    }

    private static bool IsInside(string root, string path) => path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeRelative(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path)) throw new InvalidDataException("Invalid relative Mod path.");
        string[] parts = path.Replace('/', '\\').Split('\\');
        if (parts.Any(part => part.Length == 0 || part is "." or ".." || part.Contains(':'))) throw new InvalidDataException("Invalid relative Mod path.");
        return string.Join('\\', parts);
    }

    private static string NormalizeNamespace(string value)
    {
        string normalized = value.Trim().TrimStart('\\').Replace('/', '\\').TrimEnd('\\');
        if (normalized.Length == 0 || normalized.Contains("..", StringComparison.Ordinal) || normalized.Contains(':') || normalized.Contains(';'))
            throw new InvalidDataException("Unsupported Mod namespace.");
        return normalized;
    }

    private static string NormalizeRuntimeKey(string key) => key.Trim().Replace('/', '\\').ToLowerInvariant();
    private static bool IsSafeVariable(string value) => Regex.IsMatch(value, @"^\$[A-Za-z_][A-Za-z_0-9]*$", RegexOptions.CultureInvariant);
    private static bool IsSafeRuntimeKey(string value) => value.StartsWith("$\\", StringComparison.Ordinal)
        && value.IndexOfAny(['\r', '\n', '=', ';']) < 0;
    private static bool IsSafeNumber(string value) => Numeric.IsMatch(value) && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double n) && double.IsFinite(n);

    private static string Decode(byte[] bytes, out Encoding encoding)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) { encoding = new UTF8Encoding(true, true); return encoding.GetString(bytes, 3, bytes.Length - 3); }
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) { encoding = new UnicodeEncoding(false, true, true); return encoding.GetString(bytes, 2, bytes.Length - 2); }
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) { encoding = new UnicodeEncoding(true, true, true); return encoding.GetString(bytes, 2, bytes.Length - 2); }
        encoding = StrictUtf8;
        return encoding.GetString(bytes);
    }

    private static byte[] Encode(string text, Encoding encoding)
    {
        byte[] preamble = encoding.GetPreamble();
        byte[] body = encoding.GetBytes(text);
        return [.. preamble, .. body];
    }

    private sealed record DeclarationInfo(int Line, string Variable, string RuntimeKey);
}
