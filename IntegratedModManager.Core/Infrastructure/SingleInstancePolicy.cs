using System.Security.Cryptography;
using System.Text;

namespace IntegratedModManager.Core.Infrastructure;

/// <summary>
/// Configuration, caches, backups and download journals live beside the executable.
/// Two processes from the same install directory would write those files concurrently,
/// so the application allows one instance per install directory. Separate install
/// directories (for example a stable copy and a preview copy) keep separate data and
/// may still run side by side.
/// </summary>
public static class SingleInstancePolicy
{
    private const string KeyPrefix = "IntegratedModManager.Main.";

    public static string CreateInstanceKey(string installDirectory)
    {
        if (string.IsNullOrWhiteSpace(installDirectory))
        {
            throw new ArgumentException("An install directory is required.", nameof(installDirectory));
        }

        string normalized = NormalizeDirectory(installDirectory);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return KeyPrefix + Convert.ToHexString(hash, 0, 12);
    }

    public static string NormalizeDirectory(string installDirectory)
    {
        string fullPath = Path.GetFullPath(installDirectory.Trim());
        string root = Path.GetPathRoot(fullPath) ?? string.Empty;
        // Keep a bare root such as "C:\" intact; trim separators from deeper paths so
        // "G:\App" and "G:\App\" map to the same instance.
        string trimmed = fullPath.Length > root.Length
            ? fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            : fullPath;
        // Windows paths are case-insensitive; the key must not depend on how the
        // launcher spelled the directory.
        return trimmed.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .ToUpperInvariant();
    }
}
