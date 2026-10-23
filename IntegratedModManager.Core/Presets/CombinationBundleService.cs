using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntegratedModManager.Core;

public sealed record CombinationBundleFile([property: JsonRequired] string Path,
    [property: JsonRequired] long Length, [property: JsonRequired] string Sha256);
public sealed record CombinationBundleMod([property: JsonRequired] string RelativePath,
    [property: JsonRequired] IReadOnlyList<CombinationBundleFile> Files,
    [property: JsonRequired] IReadOnlyList<ModPersistentValue> Values);
public sealed record CombinationBundleManifest([property: JsonRequired] string Format,
    [property: JsonRequired] int Version, [property: JsonRequired] string Name, [property: JsonRequired] string Game,
    [property: JsonRequired] bool IncludesPersistentState, [property: JsonRequired] IReadOnlyList<CombinationBundleMod> Mods);
public sealed record CombinationBundleLimits(int MaxMods = 2048, int MaxFiles = 100000,
    long MaxTotalBytes = 32L * 1024 * 1024 * 1024, long MaxFileBytes = 8L * 1024 * 1024 * 1024,
    long MaxArchiveBytes = 16L * 1024 * 1024 * 1024, int MaxManifestBytes = 8 * 1024 * 1024);
public enum CombinationBundleDisposition { Install, Reuse, Conflict }
public sealed record CombinationBundlePreviewItem(string RelativePath, CombinationBundleDisposition Disposition);
public sealed record CombinationBundleProgress(int Percent, string RelativePath);

/// <summary>Versioned ZIP bundles. Does not execute files or modify loader state/configuration.</summary>
public static class CombinationBundleService
{
    public const string Format = "imm-combination-bundle";
    public const string ManifestName = "preset.json";
    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 32
    };

    public static void Export(string repositoryRoot, string outputPath, string name, string game,
        bool includesState, IReadOnlyList<CombinationPresetState> mods,
        CancellationToken cancellationToken = default, CombinationBundleLimits? limits = null,
        IProgress<CombinationBundleProgress>? progress = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        limits ??= new();
        ValidateLimits(limits);
        string root = SafeDirectory(repositoryRoot);
        string output = System.IO.Path.GetFullPath(outputPath);
        string parent = SafeDirectory(System.IO.Path.GetDirectoryName(output)!);
        if (Inside(root, output)) throw new InvalidDataException("Save the bundle outside the repository.");
        if (File.Exists(output) || Directory.Exists(output)) throw new IOException("The bundle path already exists. Choose a new name.");
        var shape = new CombinationBundleManifest(Format, 1, name, game, includesState,
            mods.Select(mod => new CombinationBundleMod(Normalize(mod.ModRelativePath.Replace('\\', '/'), 2), [], mod.Values.ToArray())).ToArray());
        ValidateManifest(shape, limits);
        var inventory = new List<string[]>();
        int inventoryCount = 0;
        foreach (var mod in shape.Mods)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string[] files = Inventory(Resolve(root, mod.RelativePath));
            if (files.Length > limits.MaxFiles - inventoryCount) throw new InvalidDataException("Too many bundle files.");
            inventoryCount += files.Length;
            inventory.Add(files);
        }
        int plannedFiles = inventory.Sum(files => files.Length), lastPercent = -1;
        foreach (var mod in shape.Mods)
            ModPersistentPresetEngine.ValidateSourceState(new(Resolve(root, mod.RelativePath), Leaf(mod.RelativePath), mod.Values));
        string partial = System.IO.Path.Combine(parent, ".imm-bundle-" + Guid.NewGuid().ToString("N") + ".partial");
        bool owned = false;
        try
        {
            using (var stream = new FileStream(partial, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                owned = true;
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
                {
                    var packed = new List<CombinationBundleMod>();
                    long total = 0;
                    int count = 0;
                    for (int i = 0; i < shape.Mods.Count; i++)
                    {
                        var files = new List<CombinationBundleFile>();
                        string modRoot = Resolve(root, shape.Mods[i].RelativePath);
                        foreach (string relative in inventory[i])
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            string source = Resolve(modRoot, relative);
                            SafeFile(source);
                            using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
                            if (++count > limits.MaxFiles || input.Length > limits.MaxFileBytes
                                || input.Length > limits.MaxTotalBytes - total) throw new InvalidDataException("Bundle file/size limit exceeded.");
                            total += input.Length;
                            ZipArchiveEntry entry = zip.CreateEntry(Payload(i, relative), CompressionLevel.Fastest);
                            using Stream target = entry.Open();
                            var value = CopyAndHash(input, target, input.Length, cancellationToken);
                            files.Add(new(relative, value.Length, value.Hash));
                            int percent = count * 90 / Math.Max(1, plannedFiles);
                            if (percent != lastPercent) { lastPercent = percent; progress?.Report(new(percent, shape.Mods[i].RelativePath)); }
                        }
                        packed.Add(shape.Mods[i] with { Files = files.ToArray() });
                    }
                    var manifest = shape with { Mods = packed.ToArray() };
                    ValidateManifest(manifest, limits);
                    byte[] json = JsonSerializer.SerializeToUtf8Bytes(manifest, Json);
                    if (json.Length > limits.MaxManifestBytes) throw new InvalidDataException("Bundle manifest exceeds its size limit.");
                    using (Stream entry = zip.CreateEntry(ManifestName).Open()) entry.Write(json);
                    // A directory snapshot/hash is not a lock, but changed source data must fail visibly.
                    progress?.Report(new(95, string.Empty));
                    for (int i = 0; i < manifest.Mods.Count; i++)
                        if (!Matches(Resolve(root, manifest.Mods[i].RelativePath), manifest.Mods[i].Files, cancellationToken))
                            throw new IOException("Mod files changed during export; retry after editing has stopped.");
                }
                if (stream.Length > limits.MaxArchiveBytes) throw new InvalidDataException("Bundle ZIP exceeds its size limit.");
                stream.Flush(true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            SafeDirectory(parent);
            File.Move(partial, output, overwrite: false);
            progress?.Report(new(100, string.Empty));
        }
        finally { if (owned && File.Exists(partial)) File.Delete(partial); }
    }

    public static CombinationBundleImport PrepareImport(string archivePath, string repositoryRoot, string stagingParent,
        CancellationToken cancellationToken = default, CombinationBundleLimits? limits = null,
        IProgress<CombinationBundleProgress>? progress = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        limits ??= new();
        ValidateLimits(limits);
        string root = SafeDirectory(repositoryRoot);
        string parent = SafeDirectory(stagingParent);
        if (Inside(root, parent) || Inside(parent, root)) throw new InvalidDataException("Bundle staging and repository must be separate, non-nested folders.");
        if (!string.Equals(System.IO.Path.GetPathRoot(root), System.IO.Path.GetPathRoot(parent), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Bundle staging must be on the repository's volume.");
        SafeFile(archivePath);
        using var input = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length > limits.MaxArchiveBytes) throw new InvalidDataException("Bundle ZIP exceeds its size limit.");
        using var zip = new ZipArchive(input, ZipArchiveMode.Read);
        if (zip.Entries.Count > limits.MaxFiles + 1) throw new InvalidDataException("Too many bundle entries.");
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = Normalize(entry.FullName);
            int unixType = (entry.ExternalAttributes >> 16) & 0xF000;
            if (entry.FullName != path || !entries.TryAdd(path, entry) || entry.Name.Length == 0
                || (entry.ExternalAttributes & (int)(FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0
                || (unixType != 0 && unixType != 0x8000))
                throw new InvalidDataException("Unsafe, linked, duplicate or directory ZIP entry.");
            if (entry.Length > limits.MaxFileBytes || entry.Length > Math.Max(1024L * 1024, entry.CompressedLength * 1000L))
                throw new InvalidDataException("ZIP entry exceeds size/compression limits.");
        }
        if (!entries.TryGetValue(ManifestName, out ZipArchiveEntry? manifestEntry)
            || manifestEntry.Length > limits.MaxManifestBytes) throw new InvalidDataException("Missing or oversized preset.json.");
        byte[] manifestBytes;
        using (Stream entry = manifestEntry.Open())
        using (var output = new MemoryStream())
        {
            CopyAndHash(entry, output, manifestEntry.Length, cancellationToken);
            manifestBytes = output.ToArray();
        }
        using (JsonDocument doc = JsonDocument.Parse(manifestBytes, new JsonDocumentOptions { MaxDepth = 32 })) RejectDuplicateJson(doc.RootElement);
        CombinationBundleManifest manifest = JsonSerializer.Deserialize<CombinationBundleManifest>(manifestBytes, Json)
            ?? throw new InvalidDataException("Invalid bundle manifest.");
        ValidateManifest(manifest, limits);
        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ManifestName };
        for (int i = 0; i < manifest.Mods.Count; i++)
            foreach (var file in manifest.Mods[i].Files)
            {
                string key = Payload(i, file.Path);
                if (!expected.Add(key) || !entries.TryGetValue(key, out ZipArchiveEntry? entry) || entry.Length != file.Length)
                    throw new InvalidDataException("Missing, duplicate or incorrectly sized payload file.");
            }
        if (!expected.SetEquals(entries.Keys)) throw new InvalidDataException("Bundle contains unlisted payload files.");
        string staging = System.IO.Path.Combine(parent, ".imm-bundle-" + Guid.NewGuid().ToString("N"));
        if (Directory.Exists(staging) || File.Exists(staging)) throw new IOException("Staging folder already exists.");
        Directory.CreateDirectory(staging);
        try
        {
            int verified = 0, lastPercent = -1;
            int fileCount = manifest.Mods.Sum(mod => mod.Files.Count);
            for (int i = 0; i < manifest.Mods.Count; i++)
            {
                var mod = manifest.Mods[i];
                string stagedRoot = System.IO.Path.Combine(staging, i.ToString("D6"));
                Directory.CreateDirectory(stagedRoot);
                foreach (var file in mod.Files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string destination = Resolve(stagedRoot, file.Path);
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destination)!);
                    using Stream source = entries[Payload(i, file.Path)].Open();
                    using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    var actual = CopyAndHash(source, target, file.Length, cancellationToken);
                    if (!actual.Hash.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Bundle payload checksum mismatch: " + file.Path);
                    int percent = ++verified * 90 / Math.Max(1, fileCount);
                    if (percent != lastPercent) { lastPercent = percent; progress?.Report(new(percent, mod.RelativePath)); }
                }
                ModPersistentPresetEngine.ValidateSourceState(new(stagedRoot, Leaf(mod.RelativePath), mod.Values));
            }
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new(100, string.Empty));
            return new(root, staging, manifest, cancellationToken);
        }
        catch
        {
            DeleteSafeTree(staging);
            throw;
        }
    }

    /// <summary>Creates a checked staging container beside, never inside, the selected repository.</summary>
    public static string CreateStagingParent(string repositoryRoot)
    {
        string root = SafeDirectory(repositoryRoot);
        string parent = System.IO.Path.GetDirectoryName(root)
            ?? throw new InvalidDataException("A drive root cannot be used as a bundle repository.");
        SafeDirectory(parent);
        string staging = System.IO.Path.Combine(parent, ".imm-bundle-staging");
        if (Inside(root, staging) || Inside(staging, root)) throw new InvalidDataException("Choose a repository separate from bundle staging.");
        CheckAncestors(staging);
        Directory.CreateDirectory(staging);
        return SafeDirectory(staging);
    }

    internal static string Payload(int index, string relative) => $"mods/{index:D6}/{relative}";
    internal static string Leaf(string path) => path.Split('/')[^1];
    internal static string Normalize(string path, int? exactParts = null)
    {
        if (string.IsNullOrEmpty(path) || path.Length > 1024 || path.Contains('\\') || path.StartsWith('/')
            || path.EndsWith('/')) throw new InvalidDataException("Unsafe bundle relative path.");
        string[] parts = path.Split('/');
        if (parts.Length > 32 || (exactParts.HasValue && parts.Length != exactParts.Value)) throw new InvalidDataException("Invalid bundle path depth.");
        foreach (string part in parts)
        {
            string stem = part.Split('.')[0];
            if (part.Length == 0 || part.Length > 240 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ')
                || part.Any(c => c < 32 || "<>:\"|?*".Contains(c))
                || new[] { "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$" }.Contains(stem, StringComparer.OrdinalIgnoreCase)
                || (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
                    && "123456789¹²³".Contains(stem[3]))) throw new InvalidDataException("Unsafe Windows filename in bundle.");
        }
        return path;
    }

    internal static string Resolve(string root, string relative)
    {
        Normalize(relative);
        string result = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, relative.Replace('/', System.IO.Path.DirectorySeparatorChar)));
        if (!Inside(root, result)) throw new InvalidDataException("Bundle path escapes its root.");
        return result;
    }
    internal static bool Inside(string root, string path) => path.Equals(root, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(System.IO.Path.TrimEndingDirectorySeparator(root) + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    internal static void CheckAncestors(string path)
    {
        for (string? current = System.IO.Path.GetFullPath(path); current is not null; current = System.IO.Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Bundle paths cannot contain directory/file links.");
    }
    internal static string SafeDirectory(string path)
    {
        string root = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path));
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Choose an existing real folder.");
        CheckAncestors(root);
        return root;
    }
    internal static void SafeFile(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Bundle/source file is missing.", path);
        CheckAncestors(path);
    }
    internal static string[] Inventory(string root)
    {
        SafeDirectory(root);
        var files = new List<string>();
        int entries = 0;
        void Walk(string directory, int depth)
        {
            if (depth > 32) throw new InvalidDataException("Mod directory nesting exceeds the bundle limit.");
            CheckAncestors(directory);
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
            {
                if (++entries > 100000) throw new InvalidDataException("Mod inventory exceeds the bundle limit.");
                FileAttributes attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Mod contains a file/directory link.");
                if ((attributes & FileAttributes.Directory) != 0) Walk(entry, depth + 1);
                else files.Add(Normalize(System.IO.Path.GetRelativePath(root, entry).Replace('\\', '/')));
            }
        }
        Walk(root, 0);
        return files.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    internal static bool Matches(string root, IReadOnlyList<CombinationBundleFile> expected, CancellationToken token)
    {
        if (!Directory.Exists(root)) return false;
        string[] actual = Inventory(root);
        if (!actual.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(expected.Select(file => file.Path))) return false;
        foreach (var file in expected)
        {
            token.ThrowIfCancellationRequested();
            string path = Resolve(root, file.Path);
            SafeFile(path);
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (input.Length != file.Length || !CopyAndHash(input, Stream.Null, file.Length, token).Hash.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }
    internal static void DeleteSafeTree(string path)
    {
        if (!Directory.Exists(path)) return;
        Inventory(path); // Reject reparse points before recursive cleanup of this owned path.
        Directory.Delete(path, recursive: true);
    }
    private static (long Length, string Hash) CopyAndHash(Stream input, Stream output, long expected, CancellationToken token)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[64 * 1024];
        long count = 0;
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            token.ThrowIfCancellationRequested();
            if (read > expected - count) throw new InvalidDataException("Payload exceeds its declared length.");
            count += read;
            output.Write(buffer, 0, read);
            hash.AppendData(buffer, 0, read);
        }
        token.ThrowIfCancellationRequested();
        if (count != expected) throw new InvalidDataException("Payload is truncated.");
        return (count, Convert.ToHexString(hash.GetHashAndReset()));
    }
    private static void ValidateLimits(CombinationBundleLimits limits)
    {
        if (limits.MaxMods < 0 || limits.MaxMods > 2048 || limits.MaxFiles < 0 || limits.MaxFiles > 100000
            || limits.MaxTotalBytes < 0 || limits.MaxTotalBytes > 32L * 1024 * 1024 * 1024
            || limits.MaxFileBytes < 0 || limits.MaxFileBytes > 8L * 1024 * 1024 * 1024
            || limits.MaxArchiveBytes <= 0 || limits.MaxArchiveBytes > 16L * 1024 * 1024 * 1024
            || limits.MaxManifestBytes <= 0 || limits.MaxManifestBytes > 8 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(limits));
    }
    private static void ValidateManifest(CombinationBundleManifest manifest, CombinationBundleLimits limits)
    {
        if (manifest.Format != Format || manifest.Version != 1 || string.IsNullOrWhiteSpace(manifest.Name)
            || manifest.Name.Length > 200 || manifest.Name.Any(char.IsControl) || manifest.Game is null || manifest.Game.Length > 200
            || manifest.Mods is null || manifest.Mods.Count > limits.MaxMods) throw new InvalidDataException("Unsupported or incomplete bundle manifest.");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var runtimeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        int count = 0, valueCount = 0;
        foreach (var mod in manifest.Mods)
        {
            if (mod is null || mod.RelativePath is null || mod.Files is null || mod.Values is null) throw new InvalidDataException("Incomplete bundle Mod.");
            string relative = Normalize(mod.RelativePath, 2);
            if (relative.Split('/').Any(part => part.StartsWith("DISABLED", StringComparison.OrdinalIgnoreCase))
                || !paths.Add(relative) || !names.Add(Leaf(relative))) throw new InvalidDataException("Disabled, duplicate or ambiguous bundle Mod.");
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in mod.Files)
            {
                if (file is null || file.Path is null || file.Sha256 is null || file.Sha256.Length != 64 || !file.Sha256.All(Uri.IsHexDigit)
                    || file.Length < 0 || file.Length > limits.MaxFileBytes || file.Length > limits.MaxTotalBytes - total
                    || ++count > limits.MaxFiles || !files.Add(Normalize(file.Path))) throw new InvalidDataException("Invalid bundle file inventory.");
                total += file.Length;
            }
            foreach (string file in files)
                for (string? parent = System.IO.Path.GetDirectoryName(file.Replace('/', System.IO.Path.DirectorySeparatorChar)); !string.IsNullOrEmpty(parent); parent = System.IO.Path.GetDirectoryName(parent))
                    if (files.Contains(parent.Replace('\\', '/'))) throw new InvalidDataException("Bundle file/directory collision.");
            if (!manifest.IncludesPersistentState && mod.Values.Count > 0) throw new InvalidDataException("Set-only bundle contains persistent values.");
            foreach (var value in mod.Values)
                if (value is null || ++valueCount > 100000 || string.IsNullOrWhiteSpace(value.RuntimeKey) || !runtimeKeys.Add(value.RuntimeKey))
                    throw new InvalidDataException("Invalid or duplicated bundle state.");
        }
    }
    private static void RejectDuplicateJson(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate manifest property.");
                RejectDuplicateJson(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RejectDuplicateJson(item);
    }
}

/// <summary>Owns staged files and newly installed Mod folders only. No cross-process lock.</summary>
public sealed class CombinationBundleImport : IDisposable
{
    private readonly CombinationBundleManifest _manifest;
    private readonly string _root, _staging;
    private readonly List<(string Path, int Index)> _installed = [];
    private readonly List<string> _categories = [];
    private bool _started, _disposed;
    public bool IsCommitted { get; private set; }
    public CombinationBundleManifest Manifest => JsonSerializer.Deserialize<CombinationBundleManifest>(JsonSerializer.Serialize(_manifest, CombinationBundleService.Json), CombinationBundleService.Json)!;
    public IReadOnlyList<CombinationBundlePreviewItem> Preview { get; }
    public List<string> CleanupWarnings { get; } = [];
    public bool CanInstall => Preview.All(item => item.Disposition != CombinationBundleDisposition.Conflict);

    internal CombinationBundleImport(string root, string staging, CombinationBundleManifest manifest, CancellationToken token)
    {
        _root = root; _staging = staging; _manifest = manifest;
        Preview = Array.AsReadOnly(Evaluate(token).ToArray());
    }
    private IEnumerable<CombinationBundlePreviewItem> Evaluate(CancellationToken token)
    {
        string[] categories = Directory.GetDirectories(_root).Where(path => !System.IO.Path.GetFileName(path).StartsWith("DISABLED", StringComparison.OrdinalIgnoreCase)).ToArray();
        foreach (var mod in _manifest.Mods)
        {
            token.ThrowIfCancellationRequested();
            string path = CombinationBundleService.Resolve(_root, mod.RelativePath);
            CombinationBundleService.CheckAncestors(path);
            bool ambiguous = categories.Any(category =>
            {
                string other = System.IO.Path.Combine(category, CombinationBundleService.Leaf(mod.RelativePath));
                return !other.Equals(path, StringComparison.OrdinalIgnoreCase) && Directory.Exists(other);
            });
            yield return new(mod.RelativePath, ambiguous || File.Exists(System.IO.Path.GetDirectoryName(path)!) || File.Exists(path) ? CombinationBundleDisposition.Conflict
                : Directory.Exists(path) ? CombinationBundleService.Matches(path, mod.Files, token) ? CombinationBundleDisposition.Reuse : CombinationBundleDisposition.Conflict
                : CombinationBundleDisposition.Install);
        }
    }
    public void InstallFiles(CancellationToken token = default, IProgress<CombinationBundleProgress>? progress = null)
    {
        if (_disposed || _started) throw new InvalidOperationException("Bundle import already started or disposed.");
        if (!CanInstall) throw new InvalidDataException("Existing Mod files conflict; nothing will be overwritten.");
        CombinationBundleService.SafeDirectory(_root);
        var fresh = Evaluate(token).ToArray();
        if (!fresh.SequenceEqual(Preview)) throw new IOException("Repository changed after preview. Inspect the bundle again.");
        for (int i = 0; i < _manifest.Mods.Count; i++)
            if (!CombinationBundleService.Matches(System.IO.Path.Combine(_staging, i.ToString("D6")), _manifest.Mods[i].Files, token))
                throw new IOException("Staged Mod files changed after validation.");
        _started = true;
        for (int i = 0; i < fresh.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            if (fresh[i].Disposition == CombinationBundleDisposition.Reuse) continue;
            string path = CombinationBundleService.Resolve(_root, fresh[i].RelativePath);
            CombinationBundleService.CheckAncestors(path);
            string category = System.IO.Path.GetDirectoryName(path)!;
            if (!Directory.Exists(category))
            {
                if (File.Exists(category)) throw new IOException("Category path is occupied by a file.");
                Directory.CreateDirectory(category);
                _categories.Add(category);
            }
            Directory.Move(System.IO.Path.Combine(_staging, i.ToString("D6")), path); // Never overwrites.
            _installed.Add((path, i));
            progress?.Report(new((i + 1) * 95 / Math.Max(1, fresh.Length), fresh[i].RelativePath));
        }
        token.ThrowIfCancellationRequested();
    }
    // Call synchronously only AFTER profile/config persistence succeeds. No more cancellation point.
    public void Complete()
    {
        if (_disposed || !_started || IsCommitted) throw new InvalidOperationException("Bundle import cannot be committed.");
        IsCommitted = true;
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (!IsCommitted)
        {
            foreach (var item in _installed.AsEnumerable().Reverse())
                try
                {
                    if (!CombinationBundleService.Matches(item.Path, _manifest.Mods[item.Index].Files, default))
                        throw new IOException("Imported folder changed; retained rather than deleting: " + item.Path);
                    CombinationBundleService.DeleteSafeTree(item.Path);
                }
                catch (Exception ex) { CleanupWarnings.Add(ex.Message); }
            foreach (string category in _categories.AsEnumerable().Reverse())
                try
                {
                    CombinationBundleService.CheckAncestors(category);
                    if (Directory.Exists(category) && !Directory.EnumerateFileSystemEntries(category).Any()) Directory.Delete(category);
                }
                catch (Exception ex) { CleanupWarnings.Add(ex.Message); }
        }
        try { CombinationBundleService.DeleteSafeTree(_staging); }
        catch (Exception ex) { CleanupWarnings.Add(ex.Message); }
    }
}
