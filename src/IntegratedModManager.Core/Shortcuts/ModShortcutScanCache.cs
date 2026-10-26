namespace IntegratedModManager.Core.Shortcuts;

/// <summary>Bounded per-window cache; enumerates metadata each time, never trusts a directory timestamp.</summary>
public sealed class ModShortcutScanCache
{
    private readonly Dictionary<string, Entry> entries = new(StringComparer.OrdinalIgnoreCase);
    private sealed record Stamp(string Path, long Size, long Modified);
    private sealed record Entry(Stamp[] Files, IReadOnlyList<DetectedModShortcut> Shortcuts);
    private readonly object sync = new();

    public IReadOnlyList<DetectedModShortcut> Scan(string path, bool force = false)
    {
        lock (sync)
        {
            string root = Path.GetFullPath(path);
            Stamp[] Inspect() => Directory.EnumerateFiles(root, "*.ini", new EnumerationOptions
                { RecurseSubdirectories = true, IgnoreInaccessible = false, AttributesToSkip = FileAttributes.ReparsePoint,
                  MatchCasing = MatchCasing.CaseInsensitive })
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                .Select(file => new FileInfo(file)).Select(file => new Stamp(file.FullName, file.Length, file.LastWriteTimeUtc.Ticks)).ToArray();
            try
            {
                Stamp[] before = Inspect();
                if (!force && entries.TryGetValue(root, out Entry? cached) && before.SequenceEqual(cached.Files)) return cached.Shortcuts;
                var result = ModShortcutScanner.ScanDirectory(root, activeOnly: true);
                // Do not cache a scan that raced a writer, an add, delete or rename.
                if (before.SequenceEqual(Inspect()))
                {
                    if (entries.Count >= 32) entries.Clear();
                    entries[root] = new Entry(before, result);
                }
                else entries.Remove(root);
                return result;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                entries.Remove(root);
                return ModShortcutScanner.ScanDirectory(root, activeOnly: true);
            }
        }
    }
}
