using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IntegratedModManager.Core;

public sealed record ModUpdateSnapshot(string Repository, string ModPath, string Fingerprint);
public sealed record ModUpdateReplacementResult(string ModPath, string BackupPath);

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
            return new(snapshot.ModPath, originalBackup);
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
