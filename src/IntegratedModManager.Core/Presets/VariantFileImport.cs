using System.Globalization;
using System.Text.Json;

namespace IntegratedModManager.Core.Presets;

public sealed record ImportedVariant(string Name, IReadOnlyList<ModPersistentValue> Values, int UnmatchedCount);

public sealed record ImportedVariantSet(IReadOnlyList<ImportedVariant> Variants, string? ActiveName);

// Reads a compatible per-Mod variant JSON: named sets of
// d3dx_user.ini keys and values. Only the file format is used. Imported keys may carry a different folder layout
// ($\mods\<category>\<character>\<Mod>\<ini>\<variable>), so each key is matched to this Mod's declarations
// either exactly (namespaced INIs) or by "<Mod folder>\<ini path>\<variable>" at its end.
public static class VariantFileImport
{
    public const string FileName = ".JASM_Variants.json";

    public static ImportedVariantSet Parse(string json, string modFolderName, IReadOnlyList<ModPersistentDeclaration> declarations)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !TryGetProperty(root, "Variants", out JsonElement variants) || variants.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("The file is not a compatible variant list.");
        }

        string folder = modFolderName.Trim().ToLowerInvariant();
        var result = new List<ImportedVariant>();
        foreach (JsonElement variant in variants.EnumerateArray())
        {
            string name = TryGetProperty(variant, "Name", out JsonElement nameElement) && nameElement.ValueKind == JsonValueKind.String
                ? nameElement.GetString()!.Trim()
                : string.Empty;
            if (name.Length == 0)
            {
                continue;
            }

            var values = new Dictionary<string, ModPersistentValue>(StringComparer.OrdinalIgnoreCase);
            int unmatched = 0;
            if (TryGetProperty(variant, "Preferences", out JsonElement preferences) && preferences.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty preference in preferences.EnumerateObject())
                {
                    string? text = preference.Value.ValueKind switch
                    {
                        JsonValueKind.String => preference.Value.GetString(),
                        JsonValueKind.Number => preference.Value.GetRawText(),
                        _ => null
                    };
                    ModPersistentDeclaration? declaration = Match(preference.Name, folder, declarations);
                    if (declaration is null || !TryNumber(text, out string number))
                    {
                        unmatched++;
                        continue;
                    }

                    values[declaration.RuntimeKey] = new ModPersistentValue(declaration.RelativeIniPath, declaration.Variable, declaration.RuntimeKey, number);
                }
            }

            result.Add(new ImportedVariant(name, values.Values
                .OrderBy(value => value.RelativeIniPath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(value => value.Variable, StringComparer.OrdinalIgnoreCase).ToArray(), unmatched));
        }

        string? active = TryGetProperty(root, "ActiveVariantName", out JsonElement activeElement) && activeElement.ValueKind == JsonValueKind.String
            ? activeElement.GetString()
            : null;
        return new ImportedVariantSet(result, active);
    }

    private static ModPersistentDeclaration? Match(string key, string modFolder, IReadOnlyList<ModPersistentDeclaration> declarations)
    {
        string normalized = key.Trim().Replace('/', '\\').ToLowerInvariant();
        ModPersistentDeclaration? exact = declarations.FirstOrDefault(declaration => declaration.RuntimeKey == normalized);
        if (exact is not null)
        {
            return exact;
        }

        ModPersistentDeclaration[] bySuffix = declarations
            .Where(declaration => normalized.EndsWith(
                "\\" + modFolder + "\\" + declaration.RelativeIniPath.ToLowerInvariant() + "\\" + declaration.Variable[1..].ToLowerInvariant(),
                StringComparison.Ordinal))
            .ToArray();
        return bySuffix.Length == 1 ? bySuffix[0] : null;
    }

    private static bool TryNumber(string? text, out string number)
    {
        number = text?.Trim() ?? string.Empty;
        return double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            && double.IsFinite(value)
            && number.All(character => char.IsAsciiDigit(character) || character is '+' or '-' or '.' or 'e' or 'E');
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
