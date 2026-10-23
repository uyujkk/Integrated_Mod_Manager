using System.Security.Cryptography;
using System.Text.Json;

namespace IntegratedModManager.Core;

public sealed record ImportFileSnapshot(string RelativePath, long Length, string Sha256);
public sealed record ImportDirectorySnapshot(string[] Directories, ImportFileSnapshot[] Files);
public sealed record ExistingModImportItem(string SourceName, string DestinationName, ImportDirectorySnapshot Snapshot, string? Conflict);
public sealed record ExistingModImportPlan(string SourceRoot, string DestinationRoot, ExistingModImportItem[] Items);
public sealed record ExistingModImportOutput(string DestinationName, ImportDirectorySnapshot Snapshot, string? OwnershipToken = null);
public sealed record ExistingModImportPending(string DestinationName, string StagingName, string OwnershipToken, ImportDirectorySnapshot Snapshot);
public sealed class ExistingModImportManifest
{
    public int Version { get; set; } = 1;
    public string DestinationRoot { get; set; } = string.Empty;
    public List<ExistingModImportOutput> Outputs { get; set; } = [];
    public List<ExistingModImportPending> Pending { get; set; } = [];
    public bool Complete { get; set; }
}
public sealed record ExistingModImportUndoResult(string[] Removed, string[] Preserved);
public sealed record ExistingModImportRecoveryResult(string[] Recovered, string[] Preserved, string[] StagingFolders);

/// <summary>Copies complete Mod folders after a preview. Never moves sources or overwrites existing outputs.</summary>
public static class ExistingModImportService
{
    private const int MaximumEntries = 50000;
    public const string OwnershipFileName = ".imm-import-owner";

    public static ExistingModImportPlan Preview(string sourceRoot, string destinationRoot, CancellationToken cancellationToken = default)
    {
        string source = ExistingDirectory(sourceRoot);
        string destination = ExistingDirectory(destinationRoot);
        if (Contains(source, destination) || Contains(destination, source))
            throw new InvalidDataException("Source and destination must be separate directory trees.");
        var items = new List<ExistingModImportItem>();
        foreach (string directory in Directory.EnumerateDirectories(source).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            RejectLinks(directory);
            ImportDirectorySnapshot snapshot = Snapshot(directory, cancellationToken);
            if (snapshot.Files.Any(file => file.RelativePath.Equals(OwnershipFileName, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("The source contains a reserved import ownership file.");
            if (!snapshot.Files.Any(file => Path.GetExtension(file.RelativePath).Equals(".ini", StringComparison.OrdinalIgnoreCase))) continue;
            string name = Path.GetFileName(directory);
            string target = Child(destination, name);
            items.Add(new(name, name, snapshot, File.Exists(target) || Directory.Exists(target) ? "Destination already exists" : null));
            if (items.Count > 1000 || items.Sum(item => item.Snapshot.Files.Length + item.Snapshot.Directories.Length) > MaximumEntries)
                throw new InvalidDataException("The import exceeds the preview size limit. Import a smaller folder.");
        }
        return new(source, destination, items.ToArray());
    }

    public static async Task ExecuteAsync(ExistingModImportPlan plan, string manifestPath,
        Action<int, int>? progress = null, CancellationToken cancellationToken = default, Action? afterPublication = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        string source = ExistingDirectory(plan.SourceRoot);
        string destination = ExistingDirectory(plan.DestinationRoot);
        if (Contains(source, destination) || Contains(destination, source)) throw new InvalidDataException("Overlapping import roots.");
        if (plan.Items.Length == 0 || plan.Items.Length > 1000 || plan.Items.Any(item => item.Conflict is not null))
            throw new InvalidDataException("Resolve conflicts before importing.");
        if (plan.Items.Select(item => item.DestinationName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != plan.Items.Length)
            throw new InvalidDataException("Duplicate import destinations.");
        // Validate the entire preview before producing any output.
        foreach (ExistingModImportItem item in plan.Items)
        {
            string input = Child(source, item.SourceName);
            string output = Child(destination, item.DestinationName);
            if (File.Exists(output) || Directory.Exists(output)) throw new IOException("The destination now exists: " + item.DestinationName);
            if (!Equivalent(Snapshot(input, cancellationToken), item.Snapshot)) throw new IOException("The source changed after preview: " + item.SourceName);
        }
        string manifestFile = Path.GetFullPath(manifestPath);
        if (Contains(source, manifestFile) || Contains(destination, manifestFile))
            throw new InvalidDataException("The import record must be outside source and destination.");
        RejectLinks(Path.GetDirectoryName(manifestFile)!);
        using FileStream recordLock = LockRecord(manifestFile);
        if (File.Exists(manifestFile)) throw new IOException("An import record already exists.");
        var manifest = new ExistingModImportManifest { Version = 2, DestinationRoot = destination };
        var fullRecord = new ExistingModImportManifest { DestinationRoot = destination,
            Outputs = plan.Items.Select(item => new ExistingModImportOutput(item.DestinationName, item.Snapshot)).ToList() };
        if (JsonSerializer.SerializeToUtf8Bytes(fullRecord).Length > RecoveryJsonFile.MaximumBytes / 2)
            throw new InvalidDataException("The import record would exceed the size limit. Import fewer folders.");
        await SaveManifestAsync(manifestFile, manifest, cancellationToken);
        int completed = 0;
        foreach (ExistingModImportItem item in plan.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string input = Child(source, item.SourceName);
            string output = Child(destination, item.DestinationName);
            string staging = Child(destination, ".imm-import-" + Guid.NewGuid().ToString("N"));
            var pending = new ExistingModImportPending(item.DestinationName, Path.GetFileName(staging), Guid.NewGuid().ToString("N"), item.Snapshot);
            manifest.Pending.Add(pending);
            // Write intent before creating files. The ownership token moves with the directory.
            await SaveManifestAsync(manifestFile, manifest, cancellationToken);
            if (Directory.Exists(staging) || File.Exists(staging)) throw new IOException("Import staging directory already exists.");
            Directory.CreateDirectory(staging);
            try
            {
                RecoveryJsonFile.Write(Path.Combine(staging, OwnershipFileName), pending.OwnershipToken, keepBackup: false);
                foreach (string relative in item.Snapshot.Directories)
                    Directory.CreateDirectory(PathSafety.ResolveInsideDirectory(staging, relative));
                foreach (ImportFileSnapshot file in item.Snapshot.Files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string inputFile = PathSafety.ResolveInsideDirectory(input, file.RelativePath);
                    RejectLinks(inputFile);
                    string outputFile = PathSafety.ResolveInsideDirectory(staging, file.RelativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);
                    await using (var read = new FileStream(inputFile, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
                    await using (var write = new FileStream(outputFile, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                        await read.CopyToAsync(write, cancellationToken);
                    if (new FileInfo(outputFile).Length != file.Length || Hash(outputFile) != file.Sha256)
                        throw new IOException("Source content changed while copying: " + file.RelativePath);
                }
                if (!Equivalent(Snapshot(input, cancellationToken), item.Snapshot)) throw new IOException("Source changed during import.");
                cancellationToken.ThrowIfCancellationRequested();
                ExistingDirectory(destination);
                Directory.Move(staging, output);
                afterPublication?.Invoke(); // Fault-injection/diagnostic boundary: output exists, intent is still durable.
                // Only a successful non-overwriting publication gives this attempt ownership.
                // A late cancellation must not skip recording a published directory.
                manifest.Outputs.Add(new(item.DestinationName, item.Snapshot, pending.OwnershipToken));
                manifest.Pending.Remove(pending);
                await SaveManifestAsync(manifestFile, manifest, CancellationToken.None);
                File.Delete(Path.Combine(output, OwnershipFileName));
                progress?.Invoke(++completed, plan.Items.Length);
            }
            finally
            {
                if (Directory.Exists(staging)) DeleteOwnedTree(staging);
            }
        }
        manifest.Complete = true;
        await SaveManifestAsync(manifestFile, manifest, CancellationToken.None);
    }

    /// <summary>Reconciles only token-owned, unchanged publications. Ambiguous and changed content stays in place.</summary>
    public static ExistingModImportRecoveryResult Recover(string manifestPath, string expectedDestinationRoot)
    {
        string expected = ExistingDirectory(expectedDestinationRoot);
        RejectLinks(manifestPath);
        using FileStream recordLock = LockRecord(manifestPath);
        ExistingModImportManifest manifest = ReadManifest(manifestPath, expected);
        return RecoverRecord(manifestPath, expected, manifest);
    }

    private static ExistingModImportRecoveryResult RecoverRecord(string path, string expected, ExistingModImportManifest manifest)
    {
        var recovered = new List<string>();
        var preserved = new List<string>();
        var staging = new List<string>();
        foreach (ExistingModImportPending pending in manifest.Pending.ToArray())
        {
            string output = Child(expected, pending.DestinationName);
            string temporary = Child(expected, pending.StagingName);
            if (Directory.Exists(temporary)) staging.Add(pending.StagingName);
            if (!Directory.Exists(output))
            {
                if (File.Exists(output)) preserved.Add(pending.DestinationName);
                else if (!Directory.Exists(temporary) && !File.Exists(temporary)) manifest.Pending.Remove(pending);
                continue;
            }
            try
            {
                string marker = Path.Combine(output, OwnershipFileName);
                RejectLinks(marker);
                if (!File.Exists(marker) || RecoveryJsonFile.Read<string>(marker) != pending.OwnershipToken
                    || !Equivalent(Snapshot(output, CancellationToken.None, ignoreOwnershipFile: true), pending.Snapshot))
                { preserved.Add(pending.DestinationName); continue; }
                manifest.Outputs.Add(new(pending.DestinationName, pending.Snapshot, pending.OwnershipToken));
                manifest.Pending.Remove(pending);
                SaveManifestAsync(path, manifest, CancellationToken.None).GetAwaiter().GetResult();
                File.Delete(marker);
                recovered.Add(pending.DestinationName);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
            { preserved.Add(pending.DestinationName); }
        }
        // A crash can also occur after the record commit but before removing its marker.
        foreach (ExistingModImportOutput output in manifest.Outputs)
        {
            string directory = Child(expected, output.DestinationName);
            string marker = Path.Combine(directory, OwnershipFileName);
            if (!File.Exists(marker)) continue;
            try
            {
                RejectLinks(marker);
                if (output.OwnershipToken is not null && RecoveryJsonFile.Read<string>(marker) == output.OwnershipToken
                    && Equivalent(Snapshot(directory, CancellationToken.None, ignoreOwnershipFile: true), output.Snapshot)) File.Delete(marker);
                else preserved.Add(output.DestinationName);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
            { preserved.Add(output.DestinationName); }
        }
        if (manifest.Version == 2)
        {
            manifest.Complete = manifest.Pending.Count == 0;
            SaveManifestAsync(path, manifest, CancellationToken.None).GetAwaiter().GetResult();
        }
        return new(recovered.ToArray(), preserved.Distinct().ToArray(), staging.ToArray());
    }

    private static ExistingModImportManifest ReadManifest(string path, string expected)
    {
        ExistingModImportManifest manifest = RecoveryJsonFile.Read<ExistingModImportManifest>(path);
        if (manifest.Version is not (1 or 2) || !string.Equals(Path.GetFullPath(manifest.DestinationRoot), expected, StringComparison.OrdinalIgnoreCase)
            || manifest.Outputs is null || manifest.Pending is null || manifest.Outputs.Count + manifest.Pending.Count > 1000
            || manifest.Outputs.Any(output => output is null || output.Snapshot is null)
            || manifest.Pending.Any(pending => pending is null || pending.Snapshot is null || !Guid.TryParseExact(pending.OwnershipToken, "N", out _)
                || !pending.StagingName.StartsWith(".imm-import-", StringComparison.Ordinal))
            || manifest.Outputs.Select(output => output.DestinationName).Concat(manifest.Pending.Select(pending => pending.DestinationName))
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != manifest.Outputs.Count + manifest.Pending.Count)
            throw new InvalidDataException("Import record does not match the selected destination.");
        foreach (string name in manifest.Outputs.Select(output => output.DestinationName)
            .Concat(manifest.Pending.SelectMany(pending => new[] { pending.DestinationName, pending.StagingName }))) Child(expected, name);
        return manifest;
    }

    private static FileStream LockRecord(string path)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        RejectLinks(path);
        Directory.CreateDirectory(directory);
        RejectLinks(path + ".lock");
        return new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    public static ExistingModImportUndoResult Undo(string manifestPath, string expectedDestinationRoot)
    {
        string expected = ExistingDirectory(expectedDestinationRoot);
        RejectLinks(manifestPath);
        using FileStream recordLock = LockRecord(manifestPath);
        ExistingModImportManifest manifest = ReadManifest(manifestPath, expected);
        ExistingModImportRecoveryResult recovery = RecoverRecord(manifestPath, expected, manifest);
        var removed = new List<string>();
        var preserved = new List<string>(recovery.Preserved.Concat(recovery.StagingFolders));
        foreach (ExistingModImportOutput output in manifest.Outputs.ToArray())
        {
            string path = Child(expected, output.DestinationName);
            if (!Directory.Exists(path))
            {
                if (File.Exists(path)) { preserved.Add(output.DestinationName); continue; }
                manifest.Outputs.Remove(output); continue;
            }
            try
            {
                if (!Equivalent(Snapshot(path, CancellationToken.None), output.Snapshot)) { preserved.Add(output.DestinationName); continue; }
                // Recheck immediately before deletion. This is not an inter-process filesystem lock.
                DeleteOwnedTree(path, output.Snapshot);
                removed.Add(output.DestinationName);
                manifest.Outputs.Remove(output);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            { preserved.Add(output.DestinationName); }
        }
        SaveManifestAsync(manifestPath, manifest, CancellationToken.None).GetAwaiter().GetResult();
        return new(removed.ToArray(), preserved.ToArray());
    }

    private static ImportDirectorySnapshot Snapshot(string root, CancellationToken token, bool ignoreOwnershipFile = false)
    {
        ExistingDirectory(root);
        var directories = new List<string>();
        var files = new List<ImportFileSnapshot>();
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((root, 0));
        while (pending.TryPop(out var item))
        {
            token.ThrowIfCancellationRequested();
            if (item.Depth > 64) throw new InvalidDataException("Import directory is too deeply nested.");
            RejectLinks(item.Path);
            foreach (string path in Directory.EnumerateFileSystemEntries(item.Path))
            {
                token.ThrowIfCancellationRequested();
                RejectLinks(path);
                string relative = Path.GetRelativePath(root, path);
                if (ignoreOwnershipFile && relative == OwnershipFileName) continue;
                if (Directory.Exists(path)) { directories.Add(relative); pending.Push((path, item.Depth + 1)); }
                else files.Add(new(relative, new FileInfo(path).Length, Hash(path)));
                if (files.Count + directories.Count > MaximumEntries) throw new InvalidDataException("Import directory exceeds the preview limit.");
            }
        }
        return new(directories.Order(StringComparer.OrdinalIgnoreCase).ToArray(), files.OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static bool Equivalent(ImportDirectorySnapshot a, ImportDirectorySnapshot b)
        => b is not null && b.Directories is not null && b.Files is not null
            && a.Directories.SequenceEqual(b.Directories, StringComparer.OrdinalIgnoreCase) && a.Files.SequenceEqual(b.Files);

    private static string Hash(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string ExistingDirectory(string path)
    {
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        RejectLinks(full);
        if (!Directory.Exists(full)) throw new DirectoryNotFoundException(full);
        return full;
    }

    private static string Child(string root, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.IndexOfAny(['/', '\\', ':']) >= 0)
            throw new InvalidDataException("Invalid direct-child name.");
        return PathSafety.ResolveInsideDirectory(root, name);
    }

    private static bool Contains(string root, string path) => PathSafety.IsSameOrInside(path, root);

    private static void RejectLinks(string path)
    {
        string? current = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Filesystem links are not supported by this import: " + current);
            current = Path.GetDirectoryName(current);
        }
    }

    private static void DeleteOwnedTree(string path, ImportDirectorySnapshot? expected = null)
    {
        // Traverse without following links, including links added after the preview.
        ImportDirectorySnapshot actual = Snapshot(path, CancellationToken.None);
        if (expected is not null && !Equivalent(actual, expected)) throw new IOException("Imported content changed before undo.");
        Directory.Delete(path, recursive: true);
    }

    private static Task SaveManifestAsync(string path, ExistingModImportManifest manifest, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        RejectLinks(path);
        RejectLinks(path + ".backup");
        RecoveryJsonFile.Write(path, manifest);
        return Task.CompletedTask;
    }
}
