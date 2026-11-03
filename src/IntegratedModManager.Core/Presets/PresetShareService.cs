using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntegratedModManager.Core.Presets;

public sealed record PresetShareMod([property: JsonRequired] string RelativePath, [property: JsonRequired] IReadOnlyList<ModPersistentValue> Values);
public sealed record PresetShareFile([property: JsonRequired] string Format, [property: JsonRequired] int Version,
    [property: JsonRequired] string Name, [property: JsonRequired] string Game,
    [property: JsonRequired] bool IncludesPersistentState, [property: JsonRequired] IReadOnlyList<PresetShareMod> Mods);
public sealed record PresetSharePreview(IReadOnlyList<PresetShareMod> Matched, IReadOnlyList<string> Missing, bool SameGame)
{
    public int MatchPercent(int total) => total == 0 ? 0 : (int)Math.Round(Matched.Count * 100.0 / total);
}

/// <summary>
/// A combination preset without Mod files: which Mods it uses and, optionally, their saved values. The
/// receiver must already have the Mods. Its own format and version, separate from combination bundles,
/// and every value is checked as untrusted input, like a bundle manifest.
/// </summary>
public static class PresetShareService
{
    public const string Format = "imm-preset-share";
    public const string FileExtension = ".imm-share.json";
    private const int MaxBytes = 4 * 1024 * 1024, MaxMods = 2048, MaxValues = 100000;

    public static string Serialize(string name, string game, bool includesState, IReadOnlyList<CombinationPresetState> mods)
    {
        var file = new PresetShareFile(Format, 1, name, game ?? string.Empty, includesState,
            mods.Select(mod => new PresetShareMod(mod.ModRelativePath.Replace('\\', '/'), includesState ? mod.Values.ToArray() : [])).ToArray());
        Validate(file);
        return JsonSerializer.Serialize(file, CombinationBundleService.Json);
    }

    public static PresetShareFile Parse(string json)
    {
        if (Encoding.UTF8.GetByteCount(json) > MaxBytes) throw new InvalidDataException("The shared preset file is too large.");
        try
        {
            using (JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 }))
                CombinationBundleService.RejectDuplicateJson(document.RootElement);
            PresetShareFile file = JsonSerializer.Deserialize<PresetShareFile>(json, CombinationBundleService.Json)
                ?? throw new InvalidDataException("The shared preset file is empty.");
            Validate(file);
            return file;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Not a valid shared preset file: " + ex.Message, ex);
        }
    }

    /// <summary>Which of the shared Mods this repository has, by their path relative to it ("Character/Mod").</summary>
    public static PresetSharePreview Preview(PresetShareFile file, IEnumerable<string> repositoryMods, string? repositoryGame)
    {
        var available = new HashSet<string>(repositoryMods.Select(path => path.Replace('\\', '/')), StringComparer.OrdinalIgnoreCase);
        PresetShareMod[] matched = file.Mods.Where(mod => available.Contains(mod.RelativePath)).ToArray();
        string[] missing = file.Mods.Where(mod => !available.Contains(mod.RelativePath)).Select(mod => mod.RelativePath).ToArray();
        bool sameGame = string.IsNullOrWhiteSpace(file.Game) || string.IsNullOrWhiteSpace(repositoryGame)
            || string.Equals(file.Game.Trim(), repositoryGame.Trim(), StringComparison.OrdinalIgnoreCase);
        return new PresetSharePreview(matched, missing, sameGame);
    }

    private static void Validate(PresetShareFile file)
    {
        if (file.Format != Format || file.Version != 1 || string.IsNullOrWhiteSpace(file.Name) || file.Name.Length > 200
            || file.Name.Any(char.IsControl) || file.Game is null || file.Game.Length > 200 || file.Game.Any(char.IsControl)
            || file.Mods is null || file.Mods.Count == 0 || file.Mods.Count > MaxMods)
            throw new InvalidDataException("Unsupported or incomplete shared preset.");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int count = 0;
        foreach (PresetShareMod mod in file.Mods)
        {
            if (mod?.RelativePath is null || mod.Values is null || !paths.Add(CombinationBundleService.Normalize(mod.RelativePath, 2)))
                throw new InvalidDataException("Invalid or duplicated Mod in the shared preset.");
            if (!file.IncludesPersistentState && mod.Values.Count > 0) throw new InvalidDataException("A set-only shared preset contains values.");
            foreach (ModPersistentValue value in mod.Values)
            {
                if (value is null || ++count > MaxValues || !IsSafeIniPath(value.RelativeIniPath)
                    || !ModPersistentPresetEngine.IsSafeVariable(value.Variable) || !ModPersistentPresetEngine.IsSafeRuntimeKey(value.RuntimeKey)
                    || !ModPersistentPresetEngine.IsSafeNumber(value.Value) || !keys.Add(value.RuntimeKey))
                    throw new InvalidDataException("Invalid or duplicated value in the shared preset.");
            }
        }
    }

    // "sub\mod.ini" as saved in slots: relative, inside the Mod, and an INI file.
    private static bool IsSafeIniPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 512 || !path.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)) return false;
        try { CombinationBundleService.Normalize(path.Replace('\\', '/')); return true; }
        catch (InvalidDataException) { return false; }
    }
}
