using System.Text;
using System.Text.RegularExpressions;

namespace IntegratedModManager.Core.Presets;

public sealed record ModPersistentDeclaration(string RelativeIniPath, string Variable, string RuntimeKey);

// Changes to a Mod's own INI files that make saved values its defaults. Edits contains only
// the files that change; every edit is checked against the file's bytes before writing.
public sealed record ModIniDefaultsPlan(
    string ModDirectory, string DeployedRelativePath, IReadOnlyList<ModPersistentValue> Values,
    IReadOnlyList<ModPersistentFileEdit> Edits, int ChangedCount, int UnchangedCount);

public static partial class ModPersistentPresetEngine
{
    // Every global persist declaration of the Mod with its 3DMigoto runtime key, without reading d3dx_user.ini.
    public static IReadOnlyList<ModPersistentDeclaration> Declarations(string modDirectory, string deployedRelativePath)
    {
        string deployed = NormalizeRelative(deployedRelativePath);
        var declarations = new List<ModPersistentDeclaration>();
        foreach (string ini in EnumerateSafeIniFiles(modDirectory))
        {
            string relative = Path.GetRelativePath(modDirectory, ini).Replace('/', '\\');
            string defaultNamespace = "Mods\\" + deployed + "\\" + relative;
            foreach (DeclarationInfo declaration in ParseDeclarations(Decode(File.ReadAllBytes(ini), out _), defaultNamespace))
            {
                declarations.Add(new ModPersistentDeclaration(relative, declaration.Variable, declaration.RuntimeKey));
            }
        }

        return declarations;
    }

    // The Mod's own defaults as slot values: each "global persist $x = value" as declared, and 0 for a
    // declaration without a value (as 3DMigoto starts it). Does not read d3dx_user.ini, so a Mod the
    // loader has never written still gets one. Variables declared twice in one INI are left out, as in Capture.
    public static IReadOnlyList<ModPersistentValue> DeclaredDefaults(string modDirectory, string deployedRelativePath)
    {
        string deployed = NormalizeRelative(deployedRelativePath);
        var values = new List<ModPersistentValue>();
        foreach (string ini in EnumerateSafeIniFiles(modDirectory))
        {
            string relative = Path.GetRelativePath(modDirectory, ini).Replace('/', '\\');
            string defaultNamespace = "Mods\\" + deployed + "\\" + relative;
            foreach (DeclarationInfo declaration in ParseDeclarations(Decode(File.ReadAllBytes(ini), out _), defaultNamespace))
            {
                values.Add(new ModPersistentValue(relative, declaration.Variable, declaration.RuntimeKey, declaration.Default ?? "0"));
            }
        }

        return values
            .GroupBy(value => value.RelativeIniPath + "|" + value.Variable, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1)
            .Select(group => group.Single())
            .OrderBy(value => value.RelativeIniPath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(value => value.Variable, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    // Plans rewriting "global persist $x = default" so each saved value becomes the default. Only the
    // number changes; indentation, keywords, other lines, line endings and the file encoding are kept.
    // A declaration without a default gains " = value".
    public static ModIniDefaultsPlan PlanModIniDefaults(
        string modDirectory, string deployedRelativePath, IReadOnlyCollection<ModPersistentValue> values)
    {
        ValidateSlotDeclarations(modDirectory, deployedRelativePath, values);
        string root = ValidateRoot(modDirectory);
        string deployed = NormalizeRelative(deployedRelativePath);
        var edits = new List<ModPersistentFileEdit>();
        int changed = 0, unchanged = 0;
        foreach (IGrouping<string, ModPersistentValue> group in values.GroupBy(value => value.RelativeIniPath, StringComparer.OrdinalIgnoreCase))
        {
            string relative = NormalizeRelative(group.Key);
            string path = Path.GetFullPath(Path.Combine(root, relative));
            byte[] original = File.ReadAllBytes(path);
            string text = Decode(original, out Encoding encoding);
            Dictionary<string, string> wanted = group.ToDictionary(value => value.Variable, value => value.Value, StringComparer.OrdinalIgnoreCase);
            string[] lines = text.Split('\n');
            bool fileChanged = false;
            foreach (DeclarationInfo declaration in ParseDeclarations(text, "Mods\\" + deployed + "\\" + relative))
            {
                if (!wanted.TryGetValue(declaration.Variable, out string? value))
                {
                    continue;
                }

                string line = lines[declaration.Line];
                bool carriageReturn = line.EndsWith('\r');
                Match match = Declaration.Match(carriageReturn ? line[..^1] : line);
                if (match.Groups["default"].Success && match.Groups["default"].Value == value)
                {
                    unchanged++;
                    continue;
                }

                lines[declaration.Line] = match.Groups["lead"].Value + match.Groups["name"].Value + " = " + value + (carriageReturn ? "\r" : string.Empty);
                changed++;
                fileChanged = true;
            }

            if (fileChanged)
            {
                edits.Add(new ModPersistentFileEdit(path, original, Encode(string.Join('\n', lines), encoding)));
            }
        }

        return new ModIniDefaultsPlan(root, deployed, values.ToArray(), edits, changed, unchanged);
    }

    // Writes the planned INI files atomically, keeping each original beside it as
    // <name>.ini.imm-persist-<time>-<id>.bak (3DMigoto only loads *.ini). The result is read back;
    // on any mismatch every file is restored from its backup. Returns the backup paths.
    public static IReadOnlyList<string> ApplyModIniDefaults(ModIniDefaultsPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Edits.Count == 0)
        {
            return [];
        }

        string root = ValidateRoot(plan.ModDirectory);
        foreach (ModPersistentFileEdit edit in plan.Edits)
        {
            if (!IsInside(root, edit.Path) || HasReparsePointBetween(root, edit.Path)
                || !edit.Path.EndsWith(".ini", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("A planned INI is outside the Mod or traverses a link.");
            }
        }

        IReadOnlyList<string> backups = ApplyPlannedEdits(plan.Edits);
        try
        {
            if (plan.Edits.Any(edit => !File.ReadAllBytes(edit.Path).SequenceEqual(edit.UpdatedBytes))
                || PlanModIniDefaults(plan.ModDirectory, plan.DeployedRelativePath, plan.Values).ChangedCount != 0)
            {
                throw new IOException("The Mod INI files did not pass read-back verification; the originals were restored.");
            }
        }
        catch
        {
            RollbackModIniDefaults(plan, backups);
            throw;
        }

        return backups;
    }

    // Restores the originals of an applied plan. Refuses a file that changed after the write.
    public static void RollbackModIniDefaults(ModIniDefaultsPlan plan, IReadOnlyList<string> backups)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (backups.Count != plan.Edits.Count)
        {
            throw new ArgumentException("Each planned INI needs its backup.", nameof(backups));
        }

        for (int index = 0; index < plan.Edits.Count; index++)
        {
            ModPersistentFileEdit edit = plan.Edits[index];
            if (!File.ReadAllBytes(backups[index]).SequenceEqual(edit.OriginalBytes))
                throw new IOException("A backup no longer matches the original INI: " + backups[index]);
            if (!File.ReadAllBytes(edit.Path).SequenceEqual(edit.UpdatedBytes))
                throw new IOException("An INI changed after the write; automatic rollback was refused: " + edit.Path);
        }

        foreach (ModPersistentFileEdit edit in plan.Edits)
        {
            File.WriteAllBytes(edit.Path, edit.OriginalBytes);
        }
    }
}
