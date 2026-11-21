using System.Text;
using System.Text.Json.Nodes;

namespace IntegratedModManager.Core.DeveloperTools;

/// <summary>
/// Counts from the 4.5+ settings for a diagnostic report: copy records, state slots, combination presets, overlay
/// settings and places, and the cache sizes. Only counts, origins, flags and sizes go in; no names, values or paths.
/// </summary>
public static class DiagnosticSummary
{
    public static string Build(JsonNode? shell, IReadOnlyList<CacheEntry> caches)
    {
        var text = new StringBuilder();
        JsonArray repositories = shell?["Repositories"] as JsonArray ?? [];
        JsonArray records = shell?["CopyDeployments"] as JsonArray ?? [];
        JsonArray slots = shell?["ModPersistentSlots"] as JsonArray ?? [];
        JsonArray presets = shell?["ConfigurationProfiles"] as JsonArray ?? [];
        JsonArray placements = shell?["OverlayPlacements"] as JsonArray ?? [];

        text.AppendLine("[Settings data]");
        text.AppendLine($"Repositories: {repositories.Count}");
        text.AppendLine($"Copy records: {records.Count}" + Breakdown(records, "Origin"));
        text.AppendLine($"State slots: {slots.Count} (Default slots: {slots.Count(slot => (bool?)slot?["IsModDefault"] == true)}, with a picture: {slots.Count(slot => !string.IsNullOrWhiteSpace((string?)slot?["PreviewFile"]))})");
        text.AppendLine($"Combination presets: {presets.Count}");
        text.AppendLine();
        text.AppendLine("[Overlay]");
        foreach (string key in new[]
                 {
                     "ShortcutOverlayOpacity", "ShortcutOverlayElementOpacity", "OverlayCompactView", "OverlayCompactFontSize", "OverlayAutoContrast",
                     "OverlaySlotSwitchEnabled", "OverlayCompactSlots", "ShortcutPanelHotkeyEnabled"
                 })
            text.AppendLine($"{key}: {Scalar(shell?[key])}");
        text.AppendLine($"OverlayPlacements: {placements.Count} display(s)");
        text.AppendLine();
        text.AppendLine("[Cache]");
        foreach (CacheEntry cache in caches)
            text.AppendLine($"{cache.Key}: {(cache.Exists ? (cache.IsLink ? "link" : $"{cache.Bytes} bytes, {cache.Files} files") : "none")}");
        return text.ToString();
    }

    private static string Breakdown(JsonArray items, string property)
    {
        var groups = items.GroupBy(item => (string?)item?[property] ?? "?").OrderBy(group => group.Key, StringComparer.Ordinal).ToArray();
        return groups.Length == 0 ? string.Empty : " (" + string.Join(", ", groups.Select(group => $"{group.Key}: {group.Count()}")) + ")";
    }

    // Numbers and switches only; anything else is reported as set or not, never its text.
    private static string Scalar(JsonNode? node) => node switch
    {
        null => "default",
        JsonValue value when value.TryGetValue(out bool flag) => flag ? "on" : "off",
        JsonValue value when value.TryGetValue(out double number) => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => "set"
    };
}
