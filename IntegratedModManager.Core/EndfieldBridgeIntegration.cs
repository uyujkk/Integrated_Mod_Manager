using System.IO.Compression;
using System.Security.Cryptography;

namespace IntegratedModManager.Core;

/// <summary>Interoperates with the separately distributed bridge; contains no bridge implementation.</summary>
public static class EndfieldBridgeIntegration
{
    public const int OfferRevision = 1;
    public const string Version = "0.3.2";
    public const string FolderName = "EndfieldBridge-0.3.2-win-x64";
    public const string PackageName = FolderName + "-portable.zip";
    public const string ReleaseUrl = "https://github.com/uyujkk/EndfieldBridge/releases/tag/v0.3.2";
    public const string SourceUrl = "https://github.com/uyujkk/EndfieldBridge";
    public const string DownloadUrl = "https://github.com/uyujkk/EndfieldBridge/releases/download/v0.3.2/" + PackageName;
    // Pinned to the reviewed release, not an unchecked 'latest' executable.
    public const string PackageSha256 = "008daa343e61e3f4d83085e946a06c8eb189fb445f52edf529ef003cc2d00668";
    public const long MaxPackageBytes = 80L * 1024 * 1024;
    public const long MaxExpandedBytes = 160L * 1024 * 1024;

    public static bool IsEndfield(string? categoryId, string? gameName) =>
        categoryId?.Trim() == "42770"
        || string.Equals(gameName?.Trim(), "Arknights: Endfield", StringComparison.OrdinalIgnoreCase)
        || string.Equals(gameName?.Trim(), "明日方舟：终末地", StringComparison.OrdinalIgnoreCase)
        || string.Equals(gameName?.Trim(), "终末地", StringComparison.OrdinalIgnoreCase);

    public static bool ShouldOffer(bool eligible, int revision) => eligible && revision < OfferRevision;

    public static string ResolveLoader(string? configured, IEnumerable<string> defaults)
    {
        // Never replace a user's explicit path, even when temporarily unavailable.
        if (!string.IsNullOrWhiteSpace(configured)) return configured.Trim();
        return defaults.FirstOrDefault(File.Exists) ?? string.Empty;
    }

    public static string InstallDirectory(string toolsRoot) => Path.Combine(toolsRoot, FolderName);
    public static string Executable(string toolsRoot) => Path.Combine(InstallDirectory(toolsRoot), "EndfieldBridge.exe");

    public static bool IsInstalled(string toolsRoot)
    {
        string root = InstallDirectory(toolsRoot);
        return File.Exists(Path.Combine(root, "EndfieldBridge.exe"))
            && File.Exists(Path.Combine(root, "licenses", "LICENSE"))
            && File.Exists(Path.Combine(root, "docs", "NOTICE.md"))
            && File.Exists(Path.Combine(root, "docs", "SOURCE.txt"))
            && File.Exists(Path.Combine(root, "docs", "RISK.zh-CN.md"));
    }

    public static async Task<string> DownloadAsync(HttpClient client, string toolsRoot,
        Action<long>? progress = null, CancellationToken cancellationToken = default)
    {
        EnsureOrdinaryAncestors(toolsRoot);
        Directory.CreateDirectory(toolsRoot);
        string temporary = Path.Combine(toolsRoot, Guid.NewGuid().ToString("N") + ".download");
        try
        {
            using var response = await client.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaxPackageBytes) throw new InvalidDataException("Bridge download is too large.");
            await using (Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken))
            {
                await UpdatePackageFile.WriteAsync(stream, temporary, count =>
                {
                    if (count > MaxPackageBytes) throw new InvalidDataException("Bridge download is too large.");
                    progress?.Invoke(count);
                }, cancellationToken);
            }
            return await InstallArchiveAsync(temporary, toolsRoot, PackageSha256, cancellationToken);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public static async Task<string> InstallArchiveAsync(string archivePath, string toolsRoot,
        string expectedSha256, CancellationToken cancellationToken = default)
    {
        if (expectedSha256.Length != 64 || !expectedSha256.All(Uri.IsHexDigit)) throw new InvalidDataException("Invalid bridge checksum.");
        EnsureOrdinaryAncestors(toolsRoot);
        Directory.CreateDirectory(toolsRoot);
        string destination = InstallDirectory(toolsRoot);
        // Replacing a bridge directory can break its absolute XXMI command or destroy recovery metadata.
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("Bridge already exists. Configure the existing installation; it will not be overwritten.");
        string staging = Path.Combine(toolsRoot, ".bridge-install-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var input = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (input.Length > MaxPackageBytes) throw new InvalidDataException("Bridge archive is too large.");
            string actualHash = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken));
            if (!actualHash.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Bridge SHA-256 verification failed.");
            input.Position = 0;
            using var zip = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
            if (zip.Entries.Count is 0 or > 512) throw new InvalidDataException("Unexpected bridge archive size.");
            var files = new List<(ZipArchiveEntry Entry, string Relative)>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                string name = entry.FullName;
                if (!name.StartsWith(FolderName + "/", StringComparison.Ordinal)) throw new InvalidDataException("Unexpected bridge archive root.");
                string relative = name[(FolderName.Length + 1)..];
                if (relative.Length == 0) continue;
                string[] parts = relative.TrimEnd('/').Split('/');
                if (parts.Any(InvalidPart) || relative.Contains('\\')) throw new InvalidDataException("Unsafe bridge archive path.");
                int unixType = (entry.ExternalAttributes >> 16) & 0xF000;
                if (unixType is not (0 or 0x8000 or 0x4000)
                    || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Archive links are not allowed.");
                if (!names.Add(relative.TrimEnd('/'))) throw new InvalidDataException("Duplicate bridge archive path.");
                if (parts[0].Equals("data", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Bridge packages must not ship user data.");
                if (name.EndsWith('/')) continue;
                if (entry.Length > MaxExpandedBytes - total) throw new InvalidDataException("Expanded bridge archive is too large.");
                total += entry.Length;
                files.Add((entry, relative));
            }
            foreach (string required in new[] { "EndfieldBridge.exe", "licenses/LICENSE", "docs/NOTICE.md", "docs/SOURCE.txt", "docs/RISK.zh-CN.md", "docs/RISK.md" })
                if (!files.Any(file => file.Relative == required && file.Entry.Length > 0)) throw new InvalidDataException("Incomplete bridge archive: " + required);

            Directory.CreateDirectory(staging);
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string target = Path.Combine(staging, file.Relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using Stream source = file.Entry.Open();
                await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                // Bound actual bytes, not only attacker-controlled ZIP length fields.
                byte[] buffer = new byte[128 * 1024];
                long written = 0;
                int count;
                while ((count = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    written += count;
                    if (written > file.Entry.Length) throw new InvalidDataException("Unexpected ZIP entry length.");
                    await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                }
                if (written != file.Entry.Length) throw new InvalidDataException("Truncated ZIP entry.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            EnsureOrdinaryAncestors(toolsRoot);
            Directory.Move(staging, destination);
            return Path.Combine(destination, "EndfieldBridge.exe");
        }
        finally
        {
            // Only the random directory created by this operation is removed; existing data is untouched.
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    private static bool InvalidPart(string part)
    {
        if (part.Length == 0 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ')
            || part.Any(character => character < 32 || "<>:\"|?*".Contains(character))) return true;
        string stem = part.Split('.')[0];
        return stem.Equals("CON", StringComparison.OrdinalIgnoreCase) || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && stem[3] is >= '1' and <= '9');
    }

    private static void EnsureOrdinaryAncestors(string path)
    {
        for (DirectoryInfo? directory = new(Path.GetFullPath(path)); directory is not null; directory = directory.Parent)
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Bridge installation must not traverse directory links.");
    }
}
