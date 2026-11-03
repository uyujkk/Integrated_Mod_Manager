using System.Text.Json;

namespace IntegratedModManager.Core.Repository;

public enum XxmiImporterState { Unknown, Ready, ImporterMissing, NoGameFolder }

/// <summary>How a game's repository starts through XXMI, which then starts the game with that game's importer.</summary>
public static class XxmiLaunchPolicy
{
    public const string LauncherFileName = "XXMI Launcher.exe";

    /// <summary>The importers a repository can choose. EFMI is left out: Endfield has its own integrated launch.</summary>
    public static readonly IReadOnlyList<string> Importers = ["GIMI", "SRMI", "ZZMI", "WWMI", "HIMI"];

    /// <summary>The repository's own choice when it made one, otherwise the one its game preset implies.</summary>
    public static string? ImporterFor(string? presetId, string? chosen) =>
        chosen is not null && Importers.Contains(chosen, StringComparer.OrdinalIgnoreCase)
            ? Importers.First(importer => string.Equals(importer, chosen, StringComparison.OrdinalIgnoreCase))
            : ImporterFor(presetId);

    public static string? ImporterFor(string? presetId) => presetId switch
    {
        "genshin" => "GIMI",
        "hsr" => "SRMI",
        "zzz" => "ZZMI",
        _ => null
    };

    public static bool IsXxmiLauncher(string? path) => !string.IsNullOrWhiteSpace(path)
        && string.Equals(Path.GetFileName(path.Trim()), LauncherFileName, StringComparison.OrdinalIgnoreCase);

    public static string Arguments(string importer) => $"--nogui --xxmi {importer}";

    /// <summary>Reads XXMI's own configuration. Unknown means it could not tell, and XXMI itself should be left to report.</summary>
    public static XxmiImporterState CheckImporter(string configurationJson, string importer)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(configurationJson);
            // Valid JSON that is not an object (an array, null, a number) is a configuration XXMI must report on.
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("Importers", out JsonElement importers)
                || importers.ValueKind != JsonValueKind.Object) return XxmiImporterState.Unknown;
            if (!importers.TryGetProperty(importer, out JsonElement entry) || entry.ValueKind != JsonValueKind.Object
                || !entry.TryGetProperty("Importer", out JsonElement settings) || settings.ValueKind != JsonValueKind.Object)
                return XxmiImporterState.ImporterMissing;
            return settings.TryGetProperty("game_folder", out JsonElement folder) && folder.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(folder.GetString())
                ? XxmiImporterState.Ready : XxmiImporterState.NoGameFolder;
        }
        catch (JsonException)
        {
            return XxmiImporterState.Unknown;
        }
    }
}
