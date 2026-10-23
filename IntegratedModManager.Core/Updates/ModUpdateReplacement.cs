using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IntegratedModManager.Core.Updates;

public sealed record ModUpdateSnapshot(string Repository, string ModPath, string Fingerprint);
/// <param name="PreservedImages">Top-level preview images carried over from the old folder (file names in the new folder).</param>
public sealed record ModUpdateReplacementResult(string ModPath, string BackupPath, IReadOnlyList<string> PreservedImages);

/// <summary>Stage a complete replacement, retain the original, and refuse stale or linked inputs.</summary>
public static class ModUpdateReplacement
{
    public static ModUpdateSnapshot Capture(string repository, string modPath)
    {
        repository = FullPath(repository);
        modPath = FullPath(modPath);
        if (!Inside(modPath, repository)) throw new IOException("The Mod must be inside its original repository.");
        RejectLinkedAncestors(modPath);
        return new(repository, modPath, Fingerprint(modPath));
    }

    public static ModUpdateReplacementResult Replace(
        ModUpdateSnapshot snapshot, string payload, string backupRoot,
        string trackingRecord, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        payload = FullPath(payload);
        backupRoot = FullPath(backupRoot);
        if (Inside(payload, snapshot.ModPath) || Inside(snapshot.ModPath, payload) || string.Equals(payload, snapshot.ModPath, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The payload must be separate from the existing Mod.");
        if (Inside(backupRoot, snapshot.Repository) || Inside(snapshot.Repository, backupRoot) || string.Equals(backupRoot, snapshot.Repository, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The backup folder must be outside the repository.");
        RejectLinkedAncestors(backupRoot);
        RejectLinkedAncestors(payload);
        _ = Capture(snapshot.Repository, snapshot.ModPath);
        string parent = Path.GetDirectoryName(snapshot.ModPath)!;
        string stage = Path.Combine(parent, ".imm-update-stage-" + Guid.NewGuid().ToString("N"));
        string backup = Path.Combine(backupRoot, Guid.NewGuid().ToString("N"));
        if (Directory.Exists(stage) || File.Exists(stage)) throw new IOException("The staging path already exists.");
        bool ownsStage = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entries = Inventory(payload);
            if (!entries.Any(entry => !entry.Directory)) throw new InvalidDataException("The replacement contains no files.");
            Directory.CreateDirectory(stage);
            ownsStage = true;
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string target = Path.Combine(stage, entry.Relative);
                if (entry.Directory) Directory.CreateDirectory(target);
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(Path.Combine(payload, entry.Relative), target);
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (Fingerprint(stage) != Fingerprint(payload)) throw new IOException("The replacement changed during staging.");
            if (Capture(snapshot.Repository, snapshot.ModPath).Fingerprint != snapshot.Fingerprint)
                throw new IOException("The original Mod changed after the update was confirmed. Check your edits and retry.");
            // Previews (downloaded or user-chosen) live beside the Mod files; most update archives carry none.
            IReadOnlyList<string> preserved = CarryOverPreviewImages(snapshot.ModPath, stage, cancellationToken);
            Directory.CreateDirectory(backup);
            File.WriteAllText(Path.Combine(backup, "update.json"), JsonSerializer.Serialize(new
            {
                snapshot.Repository, snapshot.ModPath, snapshot.Fingerprint,
                CreatedUtc = DateTimeOffset.UtcNow, TrackingRecord = trackingRecord
            }));
            string originalBackup = Path.Combine(backup, "original");
            cancellationToken.ThrowIfCancellationRequested();
            // Both moves leave the original path intact on failure; the backup is never deleted.
            Directory.Move(snapshot.ModPath, originalBackup);
            try { Directory.Move(stage, snapshot.ModPath); }
            catch
            {
                Directory.Move(originalBackup, snapshot.ModPath);
                throw;
            }
            return new(snapshot.ModPath, originalBackup, preserved);
        }
        finally
        {
            if (ownsStage && Directory.Exists(stage))
            {
                // Delete only this operation's private staging tree. Unexpected links are retained.
                try { _ = Inventory(stage); Directory.Delete(stage, true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static readonly HashSet<string> PreviewImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" };

    /// <summary>
    /// Copies the old folder's top-level images into the staged update. An image the update lacks is
    /// copied as is; a same-named image with different content keeps the update's file and the old one
    /// is saved as "name.before-update.ext". Subfolders (textures) are never touched.
    /// </summary>
    private static IReadOnlyList<string> CarryOverPreviewImages(string original, string stage, CancellationToken cancellationToken)
    {
        var preserved = new List<string>();
        foreach (string source in Directory.EnumerateFiles(original).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!PreviewImageExtensions.Contains(Path.GetExtension(source))) continue;
            string name = Path.GetFileName(source);
            string target = Path.Combine(stage, name);
            if (!File.Exists(target) && !Directory.Exists(target))
            {
                File.Copy(source, target);
                preserved.Add(name);
                continue;
            }
            if (File.Exists(target) && SameContent(source, target)) continue;
            string kept = UniqueName(stage, Path.GetFileNameWithoutExtension(name) + ".before-update", Path.GetExtension(name));
            File.Copy(source, Path.Combine(stage, kept));
            preserved.Add(kept);
        }
        return preserved;
    }

    private static string UniqueName(string directory, string stem, string extension)
    {
        string candidate = stem + extension;
        for (int index = 2; File.Exists(Path.Combine(directory, candidate)) || Directory.Exists(Path.Combine(directory, candidate)); index++)
            candidate = $"{stem} ({index}){extension}";
        return candidate;
    }

    private static bool SameContent(string first, string second)
    {
        if (new FileInfo(first).Length != new FileInfo(second).Length) return false;
        using FileStream a = File.OpenRead(first), b = File.OpenRead(second);
        return SHA256.HashData(a).AsSpan().SequenceEqual(SHA256.HashData(b));
    }

    private static string FullPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    private static bool Inside(string path, string root) => path.StartsWith(
        root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static void RejectLinkedAncestors(string path)
    {
        for (DirectoryInfo? current = new(path); current is not null; current = current.Parent)
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked Mod, payload or backup directories cannot be replaced automatically.");
    }

    private sealed record Entry(string Relative, bool Directory, string Hash);

    private static List<Entry> Inventory(string root)
    {
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        var result = new List<Entry>();
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out string? directory))
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new IOException("Directory links are not supported in update inputs.");
            foreach (string path in Directory.EnumerateFileSystemEntries(directory))
            {
                FileAttributes attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("File and directory links are not supported in update inputs.");
                string relative = Path.GetRelativePath(root, path);
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    result.Add(new(relative, true, string.Empty));
                    pending.Push(path);
                }
                else
                {
                    using var stream = File.OpenRead(path);
                    result.Add(new(relative, false, Convert.ToHexString(SHA256.HashData(stream))));
                }
            }
        }
        return result.OrderBy(entry => entry.Relative, StringComparer.Ordinal).ToList();
    }

    private static string Fingerprint(string root) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(Inventory(root)))));
}
