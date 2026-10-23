namespace IntegratedModManager.Core;

public static class ModDeploymentLinks
{
    /// <summary>True only when <paramref name="deployed"/> is a directory link whose target is exactly <paramref name="source"/>.</summary>
    public static bool IsLinkTo(string deployed, string source)
    {
        try
        {
            var info = new DirectoryInfo(deployed);
            if (!info.Exists || (info.Attributes & FileAttributes.ReparsePoint) == 0 || info.LinkTarget is not { Length: > 0 } target) return false;
            string resolved = Path.GetFullPath(Path.IsPathRooted(target) ? target : Path.Combine(info.Parent?.FullName ?? string.Empty, target));
            return string.Equals(Trim(resolved), Trim(Path.GetFullPath(source)), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }

        static string Trim(string path) => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
