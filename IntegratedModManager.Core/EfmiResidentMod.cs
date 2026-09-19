using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace IntegratedModManager.Core;

public enum EfmiResidentCompatibility
{
    Supported,
    ReviewRequired,
    Unsupported
}

public sealed record EfmiResidentIssue(string Code, string Message, bool IsBlocking);

public sealed record EfmiResidentAnalysis(
    string ModPath,
    EfmiResidentCompatibility Compatibility,
    IReadOnlyList<string> IniFiles,
    int PersistentVariableCount,
    int KeySectionCount,
    IReadOnlyList<EfmiResidentIssue> Issues)
{
    public bool CanGenerate => Compatibility == EfmiResidentCompatibility.Supported;
}

public sealed record EfmiResidentModDefinition(
    string Id,
    string DisplayName,
    string SourcePath,
    bool EnabledByDefault = false);

public sealed record EfmiResidentProfile(
    int Id,
    string Name,
    IReadOnlySet<string> EnabledModIds);

public sealed record EfmiResidentDeploymentResult(
    string OutputPath,
    string ControllerPath,
    IReadOnlyList<string> GeneratedModPaths);

/// <summary>
/// Builds an EFMI-native resident mod set. It never reads or writes d3dx_user.ini and
/// never edits the source mods. Only conservative, EFMI-Tools-style INI files are
/// accepted for automatic transformation.
/// </summary>
public static partial class EfmiResidentMod
{
    public const string ControllerNamespace = "IntegratedModManager\\ResidentController";
    public const string ControllerFileName = "000_IntegratedModManager_ResidentController.ini";

    private static readonly string[] RuntimeGateVariables = ["$mod_enabled", "$object_detected"];

    public static EfmiResidentAnalysis Analyze(string modPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modPath);
        string fullModPath = Path.GetFullPath(modPath);
        if (!Directory.Exists(fullModPath))
        {
            throw new DirectoryNotFoundException(fullModPath);
        }

        List<string> iniFiles = Directory.EnumerateFiles(fullModPath, "*.ini", SearchOption.AllDirectories)
            .Where(path => !Path.GetFileName(path).StartsWith("DISABLED", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        List<EfmiResidentIssue> issues = [];
        if (iniFiles.Count == 0)
        {
            issues.Add(new("NO_ACTIVE_INI", "No active INI file was found in the mod.", true));
            return BuildAnalysis(EfmiResidentCompatibility.Unsupported, 0, 0);
        }

        if (iniFiles.Any(path => string.Equals(Path.GetFileName(path), "d3dx.ini", StringComparison.OrdinalIgnoreCase)))
        {
            issues.Add(new("GLOBAL_D3DX_IN_MOD", "The mod contains d3dx.ini, which is not safe inside the EFMI Mods folder.", true));
        }

        int persistentVariables = 0;
        int keySections = 0;
        bool foundEfmiRegistration = false;
        bool foundModEnabled = false;
        bool foundObjectDetected = false;
        bool foundUnrecognizedEffectPath = false;

        foreach (string iniFile in iniFiles)
        {
            string text = File.ReadAllText(iniFile);
            persistentVariables += PersistDeclarationRegex().Matches(text).Count;
            keySections += KeySectionRegex().Matches(text).Count;
            foundEfmiRegistration |= text.Contains("CommandList\\EFMIv1\\RegisterMod", StringComparison.OrdinalIgnoreCase)
                || text.Contains("CommandListRegisterMod", StringComparison.OrdinalIgnoreCase);
            foundModEnabled |= ModEnabledDeclarationRegex().IsMatch(text);
            foundObjectDetected |= ObjectDetectedDeclarationRegex().IsMatch(text);

            foreach (IniSection section in ParseSections(text))
            {
                if (!IsOverrideSection(section.Name) || !ContainsRuntimeEffect(section.Lines))
                {
                    continue;
                }

                bool hasRecognizedGate = section.Lines.Any(line =>
                    IsIfLineUsing(line, "$mod_enabled") || IsIfLineUsing(line, "$object_detected"));
                if (!hasRecognizedGate)
                {
                    foundUnrecognizedEffectPath = true;
                    issues.Add(new(
                        "UNGUARDED_OVERRIDE",
                        $"{Path.GetRelativePath(fullModPath, iniFile)} [{section.Name}] has runtime effects without a standard EFMI gate.",
                        false));
                }
            }
        }

        if (!foundEfmiRegistration || !foundModEnabled || !foundObjectDetected)
        {
            issues.Add(new(
                "NONSTANDARD_EFMI_LAYOUT",
                "The mod does not expose the complete EFMI Tools registration/mod_enabled/object_detected pattern.",
                false));
        }

        EfmiResidentCompatibility compatibility;
        if (issues.Any(issue => issue.IsBlocking))
        {
            compatibility = EfmiResidentCompatibility.Unsupported;
        }
        else if (!foundEfmiRegistration || !foundModEnabled || !foundObjectDetected || foundUnrecognizedEffectPath)
        {
            compatibility = EfmiResidentCompatibility.ReviewRequired;
        }
        else
        {
            compatibility = EfmiResidentCompatibility.Supported;
        }

        return BuildAnalysis(compatibility, persistentVariables, keySections);

        EfmiResidentAnalysis BuildAnalysis(EfmiResidentCompatibility compatibility, int persistCount, int keys)
            => new(
                fullModPath,
                compatibility,
                iniFiles.Select(path => Path.GetRelativePath(fullModPath, path)).ToArray(),
                persistCount,
                keys,
                issues);
    }

    public static string CreateStableModId(string displayName, string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        string slug = Regex.Replace(displayName.Trim().ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_');
        if (string.IsNullOrEmpty(slug))
        {
            slug = "mod";
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(sourcePath).ToLowerInvariant()));
        return $"{slug}_{Convert.ToHexString(hash, 0, 4).ToLowerInvariant()}";
    }

    public static string TransformIni(string iniText, string modId)
    {
        ArgumentNullException.ThrowIfNull(iniText);
        string variable = GetEnableVariable(modId);
        string newline = iniText.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        string[] lines = iniText.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        string? section = null;
        List<string> output = new(lines.Length + 8);
        bool keyConditionWritten = false;

        foreach (string originalLine in lines)
        {
            string line = originalLine;
            Match sectionMatch = SectionHeaderRegex().Match(line);
            if (sectionMatch.Success)
            {
                if (section is not null && IsKeySection(section) && !keyConditionWritten)
                {
                    output.Add($"condition = {variable}");
                }

                section = sectionMatch.Groups[1].Value.Trim();
                keyConditionWritten = false;
                output.Add(line);
                continue;
            }

            if (section is not null && IsKeySection(section) && ConditionRegex().Match(line) is { Success: true } conditionMatch)
            {
                string indent = conditionMatch.Groups[1].Value;
                string expression = conditionMatch.Groups[2].Value.Trim();
                output.Add($"{indent}condition = ({expression}) && {variable}");
                keyConditionWritten = true;
                continue;
            }

            if (IfRegex().Match(line) is { Success: true } ifMatch
                && RuntimeGateVariables.Any(gate => ContainsVariable(ifMatch.Groups[2].Value, gate)))
            {
                string indent = ifMatch.Groups[1].Value;
                string expression = ifMatch.Groups[2].Value.Trim();
                output.Add($"{indent}if {variable} && ({expression})");
                continue;
            }

            output.Add(line);
        }

        if (section is not null && IsKeySection(section) && !keyConditionWritten)
        {
            output.Add($"condition = {variable}");
        }

        return string.Join(newline, output);
    }

    public static string GenerateController(
        IReadOnlyList<EfmiResidentModDefinition> mods,
        IReadOnlyList<EfmiResidentProfile> profiles,
        string nextProfileKey = "ctrl no_alt no_shift VK_RIGHT",
        string previousProfileKey = "ctrl no_alt no_shift VK_LEFT")
    {
        ArgumentNullException.ThrowIfNull(mods);
        ArgumentNullException.ThrowIfNull(profiles);
        if (mods.Count == 0)
        {
            throw new ArgumentException("At least one mod is required.", nameof(mods));
        }

        HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
        foreach (EfmiResidentModDefinition mod in mods)
        {
            ValidateIdentifier(mod.Id);
            if (!ids.Add(mod.Id))
            {
                throw new ArgumentException("Duplicate mod id: " + mod.Id, nameof(mods));
            }
        }

        if (profiles.Count == 0 || profiles.Any(profile => profile.Id <= 0) || profiles.Select(profile => profile.Id).Distinct().Count() != profiles.Count)
        {
            throw new ArgumentException("Profiles must have unique positive ids.", nameof(profiles));
        }

        foreach (EfmiResidentProfile profile in profiles)
        {
            string? unknown = profile.EnabledModIds.FirstOrDefault(id => !ids.Contains(id));
            if (unknown is not null)
            {
                throw new ArgumentException($"Profile {profile.Id} references unknown mod id: {unknown}", nameof(profiles));
            }
        }

        StringBuilder builder = new();
        builder.AppendLine("; Generated by Integrated Mod Manager. Source mods are not modified.");
        builder.AppendLine($"namespace = {ControllerNamespace}");
        builder.AppendLine();
        builder.AppendLine("[Constants]");
        builder.AppendLine($"global persist $active_profile = {profiles[0].Id}");
        foreach (EfmiResidentModDefinition mod in mods)
        {
            builder.AppendLine($"global {GetLocalEnableVariable(mod.Id)} = {(mod.EnabledByDefault ? 1 : 0)}");
        }

        builder.AppendLine();
        builder.AppendLine("[KeyResidentProfile]");
        builder.AppendLine($"key = {nextProfileKey}");
        builder.AppendLine($"back = {previousProfileKey}");
        builder.AppendLine("type = cycle");
        builder.AppendLine("smart = true");
        builder.AppendLine("$active_profile = " + string.Join(", ", profiles.Select(profile => profile.Id)));
        builder.AppendLine();
        builder.AppendLine("[Present]");
        foreach (EfmiResidentProfile profile in profiles)
        {
            builder.AppendLine($"if $active_profile == {profile.Id}");
            foreach (EfmiResidentModDefinition mod in mods)
            {
                builder.AppendLine($"    {GetLocalEnableVariable(mod.Id)} = {(profile.EnabledModIds.Contains(mod.Id) ? 1 : 0)}");
            }
            builder.AppendLine("endif");
        }

        return builder.ToString();
    }

    public static EfmiResidentDeploymentResult GenerateDeployment(
        string outputPath,
        IReadOnlyList<EfmiResidentModDefinition> mods,
        IReadOnlyList<EfmiResidentProfile> profiles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(mods);
        string fullOutputPath = Path.GetFullPath(outputPath);
        if (Directory.Exists(fullOutputPath) && Directory.EnumerateFileSystemEntries(fullOutputPath).Any())
        {
            throw new IOException("The deployment output directory must be empty: " + fullOutputPath);
        }

        List<(EfmiResidentModDefinition Definition, EfmiResidentAnalysis Analysis)> validated = [];
        foreach (EfmiResidentModDefinition mod in mods)
        {
            ValidateIdentifier(mod.Id);
            EfmiResidentAnalysis analysis = Analyze(mod.SourcePath);
            if (!analysis.CanGenerate)
            {
                throw new InvalidDataException($"Mod '{mod.DisplayName}' is not safe for automatic resident conversion ({analysis.Compatibility}).");
            }
            validated.Add((mod, analysis));
        }

        Directory.CreateDirectory(fullOutputPath);
        List<string> generatedPaths = [];
        try
        {
            string controllerPath = PathSafety.ResolveInsideDirectory(fullOutputPath, ControllerFileName);
            File.WriteAllText(controllerPath, GenerateController(mods, profiles), new UTF8Encoding(false));

            foreach ((EfmiResidentModDefinition definition, EfmiResidentAnalysis analysis) in validated)
            {
                string relativeFolder = "Resident_" + definition.Id;
                string targetModPath = PathSafety.ResolveInsideDirectory(fullOutputPath, relativeFolder);
                CopyDirectory(definition.SourcePath, targetModPath);
                foreach (string relativeIni in analysis.IniFiles)
                {
                    string iniPath = PathSafety.ResolveInsideDirectory(targetModPath, relativeIni);
                    string transformed = TransformIni(File.ReadAllText(iniPath), definition.Id);
                    File.WriteAllText(iniPath, transformed, new UTF8Encoding(false));
                }
                generatedPaths.Add(targetModPath);
            }

            return new(fullOutputPath, controllerPath, generatedPaths);
        }
        catch
        {
            if (Directory.Exists(fullOutputPath))
            {
                Directory.Delete(fullOutputPath, true);
            }
            throw;
        }
    }

    public static string GetEnableVariable(string modId)
    {
        ValidateIdentifier(modId);
        return $"$\\{ControllerNamespace}\\enable_{modId}";
    }

    private static string GetLocalEnableVariable(string modId) => "$enable_" + modId;

    private static void ValidateIdentifier(string modId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modId);
        if (!IdentifierRegex().IsMatch(modId))
        {
            throw new ArgumentException("Mod id may contain only ASCII letters, digits, and underscores: " + modId, nameof(modId));
        }
    }

    private static void CopyDirectory(string sourcePath, string targetPath)
    {
        string source = Path.GetFullPath(sourcePath);
        Directory.CreateDirectory(targetPath);
        foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(PathSafety.ResolveInsideDirectory(targetPath, Path.GetRelativePath(source, directory)));
        }
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string destination = PathSafety.ResolveInsideDirectory(targetPath, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, false);
        }
    }

    private static bool ContainsVariable(string expression, string variable)
        => Regex.IsMatch(expression, $@"(?<![A-Za-z0-9_]){Regex.Escape(variable)}(?![A-Za-z0-9_])", RegexOptions.IgnoreCase);

    private static bool IsIfLineUsing(string line, string variable)
        => IfRegex().Match(line) is { Success: true } match && ContainsVariable(match.Groups[2].Value, variable);

    private static bool IsOverrideSection(string name)
        => name.StartsWith("TextureOverride", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("ShaderOverride", StringComparison.OrdinalIgnoreCase);

    private static bool IsKeySection(string name) => name.StartsWith("Key", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsRuntimeEffect(IReadOnlyList<string> lines)
        => lines.Any(line => RuntimeEffectRegex().IsMatch(line));

    private static IReadOnlyList<IniSection> ParseSections(string text)
    {
        List<IniSection> sections = [];
        IniSection? current = null;
        foreach (string line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            Match match = SectionHeaderRegex().Match(line);
            if (match.Success)
            {
                current = new(match.Groups[1].Value.Trim(), []);
                sections.Add(current);
            }
            else if (current is not null)
            {
                current.Lines.Add(line);
            }
        }
        return sections;
    }

    private sealed record IniSection(string Name, List<string> Lines);

    [GeneratedRegex(@"(?im)^\s*global\s+persist\s+\$[A-Za-z0-9_]+\s*=")]
    private static partial Regex PersistDeclarationRegex();

    [GeneratedRegex(@"(?im)^\s*\[Key[^\]]*\]\s*$")]
    private static partial Regex KeySectionRegex();

    [GeneratedRegex(@"(?im)^\s*global(?:\s+persist)?\s+\$mod_enabled\s*=")]
    private static partial Regex ModEnabledDeclarationRegex();

    [GeneratedRegex(@"(?im)^\s*global(?:\s+persist)?\s+\$object_detected\s*=")]
    private static partial Regex ObjectDetectedDeclarationRegex();

    [GeneratedRegex(@"^\s*\[([^\]]+)\]\s*(?:;.*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex SectionHeaderRegex();

    [GeneratedRegex(@"^(\s*)condition\s*=\s*(.*?)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex ConditionRegex();

    [GeneratedRegex(@"^(\s*)if\s+(.+?)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex IfRegex();

    [GeneratedRegex(@"(?im)^\s*(?:handling|draw|drawindexed|drawindexedinstanced|run|this|[vpcdgh]s-(?:t|u|cb)\d+|vb\d+|ib)\s*=")]
    private static partial Regex RuntimeEffectRegex();

    [GeneratedRegex(@"^[A-Za-z0-9_]+$")]
    private static partial Regex IdentifierRegex();
}
