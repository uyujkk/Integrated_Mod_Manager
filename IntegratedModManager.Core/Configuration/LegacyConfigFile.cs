using System.Globalization;
using System.Text;

namespace IntegratedModManager.Core;

/// <summary>
/// The contents of config.ini. A null scalar means the line was absent, so callers keep
/// their current value. Keys of the per-Mod maps are Mod folder paths.
/// </summary>
public sealed class LegacyConfig
{
    public string? SourceDirectory { get; set; }

    public string? TargetDirectory { get; set; }

    public string? LauncherPath { get; set; }

    public bool? DarkTheme { get; set; }

    /// <summary>"en" or "zh" as stored.</summary>
    public string? Language { get; set; }

    public Dictionary<string, List<ShortcutBinding>> Bindings { get; } = new(StringComparer.CurrentCultureIgnoreCase);

    public Dictionary<string, string> ModLinks { get; } = new(StringComparer.CurrentCultureIgnoreCase);

    public Dictionary<string, TrackedModOrigin> ModOrigins { get; } = new(StringComparer.CurrentCultureIgnoreCase);
}

/// <summary>
/// Parses and formats config.ini: one <c>key=value</c> per line, every value UTF-8 Base64,
/// multi-field values separated by tabs. Unknown or malformed lines are skipped so an
/// older or newer file never prevents the rest from loading.
/// </summary>
public static class LegacyConfigFile
{
    private const string SourceKey = "source_dir=";
    private const string TargetKey = "target_dir=";
    private const string LauncherKey = "launcher_path=";
    private const string ThemeKey = "theme=";
    private const string LanguageKey = "language=";
    private const string BindingKey = "binding=";
    private const string BindingAutoKey = "binding_auto=";
    private const string ModLinkKey = "mod_link=";
    private const string ModOriginKey = "mod_origin=";

    /// <summary>Validator for <see cref="ConfigurationFileStore.Read"/>: a real config.ini always has a source line.</summary>
    public static bool IsValid(string text)
    {
        return !string.IsNullOrWhiteSpace(text) && SplitLines(text).Any(line => line.StartsWith(SourceKey, StringComparison.Ordinal));
    }

    public static LegacyConfig Parse(string text)
    {
        var config = new LegacyConfig();
        foreach (string line in SplitLines(text))
        {
            if (TryValue(line, SourceKey, out string value))
            {
                config.SourceDirectory = value;
            }
            else if (TryValue(line, TargetKey, out value))
            {
                config.TargetDirectory = value;
            }
            else if (TryValue(line, LauncherKey, out value))
            {
                config.LauncherPath = value;
            }
            else if (TryValue(line, ThemeKey, out value))
            {
                config.DarkTheme = string.Equals(value, "dark", StringComparison.OrdinalIgnoreCase);
            }
            else if (TryValue(line, LanguageKey, out value))
            {
                config.Language = value;
            }
            else if (TryFields(line, BindingKey, out string[] parts) && parts.Length == 3)
            {
                string modPath = Decode(parts[0]);
                if (!config.Bindings.TryGetValue(modPath, out List<ShortcutBinding>? bindings))
                {
                    bindings = [];
                    config.Bindings[modPath] = bindings;
                }

                bindings.Add(new ShortcutBinding(Decode(parts[1]), Decode(parts[2])));
            }
            else if (TryFields(line, BindingAutoKey, out parts) && parts.Length == 6
                && int.TryParse(Decode(parts[1]), NumberStyles.Integer, CultureInfo.InvariantCulture, out int bindingIndex))
            {
                // Refers to a binding= line read earlier for the same Mod.
                if (config.Bindings.TryGetValue(Decode(parts[0]), out List<ShortcutBinding>? bindings)
                    && bindingIndex >= 0
                    && bindingIndex < bindings.Count)
                {
                    ShortcutBinding binding = bindings[bindingIndex];
                    binding.AutoSection = Decode(parts[2]);
                    binding.AutoBehavior = Decode(parts[3]);
                    binding.AutoTarget = Decode(parts[4]);
                    binding.AutoSourceFile = Decode(parts[5]);
                }
            }
            else if (TryFields(line, ModLinkKey, out parts) && parts.Length == 2)
            {
                config.ModLinks[Decode(parts[0])] = Decode(parts[1]);
            }
            else if (TryFields(line, ModOriginKey, out parts) && parts.Length >= 7)
            {
                string path = Decode(parts[0]);
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                int.TryParse(Decode(parts[2]), NumberStyles.Integer, CultureInfo.InvariantCulture, out int itemId);
                config.ModOrigins[path] = new TrackedModOrigin
                {
                    Path = path,
                    SourceSite = Decode(parts[1]),
                    ItemId = itemId,
                    Title = Decode(parts[3]),
                    ProfileUrl = Decode(parts[4]),
                    PreviewUrl = Decode(parts[5]),
                    LastKnownUpdatedAt = ParseTimestamp(Decode(parts[6])),
                    DownloadFileId = parts.Length > 7 ? Decode(parts[7]) : string.Empty,
                    DownloadFileName = parts.Length > 8 ? Decode(parts[8]) : string.Empty
                };
            }
        }

        return config;
    }

    /// <summary>
    /// Formats <paramref name="config"/> with Mods in a stable order. Bindings with neither
    /// a shortcut nor an action are dropped, and binding_auto indexes count only the
    /// bindings actually written, so they point at the same binding when read back.
    /// </summary>
    public static string Format(LegacyConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var lines = new List<string>
        {
            SourceKey + Encode(config.SourceDirectory ?? string.Empty),
            TargetKey + Encode(config.TargetDirectory ?? string.Empty),
            LauncherKey + Encode(config.LauncherPath ?? string.Empty),
            ThemeKey + Encode(config.DarkTheme == true ? "dark" : "light"),
            LanguageKey + Encode(config.Language ?? "zh")
        };

        foreach ((string path, List<ShortcutBinding> bindings) in config.Bindings.OrderBy(item => item.Key, StringComparer.CurrentCultureIgnoreCase))
        {
            int writtenIndex = 0;
            foreach (ShortcutBinding binding in bindings)
            {
                if (string.IsNullOrWhiteSpace(binding.Shortcut) && string.IsNullOrWhiteSpace(binding.Action))
                {
                    continue;
                }

                lines.Add(BindingKey + Join(path, binding.Shortcut, binding.Action));
                if (binding.HasAutoDescription)
                {
                    lines.Add(BindingAutoKey + Join(
                        path,
                        writtenIndex.ToString(CultureInfo.InvariantCulture),
                        binding.AutoSection,
                        binding.AutoBehavior,
                        binding.AutoTarget,
                        binding.AutoSourceFile));
                }

                writtenIndex++;
            }
        }

        foreach ((string path, string link) in config.ModLinks.OrderBy(item => item.Key, StringComparer.CurrentCultureIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(link))
            {
                lines.Add(ModLinkKey + Join(path, link));
            }
        }

        foreach ((string path, TrackedModOrigin origin) in config.ModOrigins.OrderBy(item => item.Key, StringComparer.CurrentCultureIgnoreCase))
        {
            lines.Add(ModOriginKey + Join(
                path,
                origin.SourceSite,
                origin.ItemId.ToString(CultureInfo.InvariantCulture),
                origin.Title,
                origin.ProfileUrl,
                origin.PreviewUrl ?? string.Empty,
                origin.LastKnownUpdatedAt?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
                origin.DownloadFileId,
                origin.DownloadFileName));
        }

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    public static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    /// <summary>Decodes a Base64 field; a damaged field reads as empty instead of failing the file.</summary>
    public static string Decode(string value)
    {
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(value));
        }
        catch (FormatException)
        {
            return string.Empty;
        }
    }

    private static DateTimeOffset? ParseTimestamp(string value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsed) ? parsed : null;

    private static string Join(params string[] values) => string.Join('\t', values.Select(Encode));

    private static IEnumerable<string> SplitLines(string text) => text.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);

    private static bool TryValue(string line, string key, out string value)
    {
        value = line.StartsWith(key, StringComparison.Ordinal) ? Decode(line[key.Length..]) : string.Empty;
        return line.StartsWith(key, StringComparison.Ordinal);
    }

    private static bool TryFields(string line, string key, out string[] parts)
    {
        parts = line.StartsWith(key, StringComparison.Ordinal) ? line[key.Length..].Split('\t') : [];
        return line.StartsWith(key, StringComparison.Ordinal);
    }
}
