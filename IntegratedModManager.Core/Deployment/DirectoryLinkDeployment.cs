using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace IntegratedModManager.Core;

/// <summary>
/// Creates and removes Windows directory junctions without traversing their targets.
/// A junction makes the loader and the repository observe the same physical files.
/// </summary>
/// <summary>A deployed link whose target is missing. <see cref="Target"/> is null when unreadable.</summary>
public sealed record BrokenDirectoryLink(string LinkPath, string? Target);

public static class DirectoryLinkDeployment
{
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint FileShareDelete = 0x00000004;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const uint FsctlSetReparsePoint = 0x000900A4;
    private const uint IoReparseTagMountPoint = 0xA0000003;

    public static bool IsDirectoryLink(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            return Directory.Exists(path)
                && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static string? TryGetTarget(string path)
    {
        if (!IsDirectoryLink(path))
        {
            return null;
        }

        try
        {
            FileSystemInfo? target = new DirectoryInfo(path).ResolveLinkTarget(returnFinalTarget: true);
            return target is null ? null : Path.GetFullPath(target.FullName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        {
            return null;
        }
    }

    public static void CreateJunction(string junctionPath, string targetPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Directory junction deployment is available on Windows only.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(junctionPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        string fullJunctionPath = NormalizeDirectoryPath(junctionPath);
        string fullTargetPath = NormalizeDirectoryPath(targetPath);
        if (!Directory.Exists(fullTargetPath))
        {
            throw new DirectoryNotFoundException("Junction target not found: " + fullTargetPath);
        }

        if (Directory.Exists(fullJunctionPath) || File.Exists(fullJunctionPath))
        {
            throw new IOException("The junction path already exists: " + fullJunctionPath);
        }

        if (fullTargetPath.StartsWith(@"\\", StringComparison.Ordinal))
        {
            throw new NotSupportedException("Windows directory junctions cannot target a network share.");
        }

        if (IsSameOrInside(fullTargetPath, fullJunctionPath)
            || IsSameOrInside(fullJunctionPath, fullTargetPath))
        {
            throw new InvalidOperationException("The junction and target paths must not contain one another.");
        }

        string? parent = Path.GetDirectoryName(fullJunctionPath);
        if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
        {
            throw new DirectoryNotFoundException("Junction parent directory not found: " + parent);
        }

        Directory.CreateDirectory(fullJunctionPath);
        try
        {
            SetMountPointReparseData(fullJunctionPath, fullTargetPath);
        }
        catch
        {
            if (Directory.Exists(fullJunctionPath)
                && (File.GetAttributes(fullJunctionPath) & FileAttributes.ReparsePoint) == 0)
            {
                Directory.Delete(fullJunctionPath, recursive: false);
            }
            throw;
        }
    }

    public static void RemoveLink(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = NormalizeDirectoryPath(path);
        if (!Directory.Exists(fullPath))
        {
            return;
        }

        if (!IsDirectoryLink(fullPath))
        {
            throw new InvalidOperationException("Refused to remove a normal directory as a link: " + fullPath);
        }

        Directory.Delete(fullPath, recursive: false);
    }

    /// <summary>
    /// The path a link points to as stored in the link itself, even when that path no
    /// longer exists. Returns <see langword="null"/> for anything that is not a link.
    /// </summary>
    public static string? TryGetStoredTarget(string path)
    {
        if (!IsDirectoryLink(path))
        {
            return null;
        }

        try
        {
            string? target = new DirectoryInfo(path).LinkTarget;
            if (string.IsNullOrWhiteSpace(target))
            {
                return null;
            }

            const string NtPrefix = @"\??\";
            if (target.StartsWith(NtPrefix, StringComparison.Ordinal))
            {
                target = target[NtPrefix.Length..];
            }

            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, target))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>A link whose target directory is gone, so its contents cannot be inspected.</summary>
    public static bool IsBrokenLink(string path)
    {
        if (!IsDirectoryLink(path))
        {
            return false;
        }

        string? target = TryGetStoredTarget(path);
        return target is null || !Directory.Exists(target);
    }

    /// <summary>First-level links in <paramref name="loaderRoot"/> whose target no longer exists.</summary>
    public static IReadOnlyList<BrokenDirectoryLink> FindBrokenLinks(string loaderRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(loaderRoot);
        if (!Directory.Exists(loaderRoot))
        {
            return [];
        }

        var broken = new List<BrokenDirectoryLink>();
        foreach (string entry in Directory.EnumerateDirectories(loaderRoot))
        {
            if (IsBrokenLink(entry))
            {
                broken.Add(new BrokenDirectoryLink(Path.GetFullPath(entry), TryGetStoredTarget(entry)));
            }
        }

        broken.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.LinkPath, right.LinkPath));
        return broken;
    }

    /// <summary>
    /// First-level links in <paramref name="loaderRoot"/> that point exactly at
    /// <paramref name="sourcePath"/>, whatever their names. Similar names never match.
    /// </summary>
    public static IReadOnlyList<string> FindLinksTargeting(string sourcePath, string loaderRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(loaderRoot);
        if (!Directory.Exists(loaderRoot))
        {
            return [];
        }

        string source = Path.GetFullPath(sourcePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var links = new List<string>();
        foreach (string entry in Directory.EnumerateDirectories(loaderRoot))
        {
            if (string.Equals(TryGetStoredTarget(entry), source, StringComparison.OrdinalIgnoreCase))
            {
                links.Add(Path.GetFullPath(entry));
            }
        }

        links.Sort(StringComparer.OrdinalIgnoreCase);
        return links;
    }

    /// <summary>
    /// Removes a link only if it is still broken, so a target restored in the meantime
    /// keeps its deployment. Only the link is deleted; nothing is followed.
    /// </summary>
    public static bool RemoveBrokenLink(string path)
    {
        if (!IsBrokenLink(path))
        {
            return false;
        }

        RemoveLink(path);
        return true;
    }

    /// <summary>
    /// Finds currently deployed junctions that belong to other second-level Mods under
    /// the same first-level character folder. Only junctions whose resolved targets
    /// exactly match those repository siblings are returned.
    /// </summary>
    public static IReadOnlyList<string> FindSiblingLinksToReplace(
        string sourceModPath,
        string repositoryRoot,
        string loaderRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceModPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(loaderRoot);

        string fullSourcePath = NormalizeDirectoryPath(sourceModPath);
        string fullRepositoryRoot = NormalizeDirectoryPath(repositoryRoot);
        string fullLoaderRoot = NormalizeDirectoryPath(loaderRoot);
        if (!Directory.Exists(fullSourcePath))
        {
            throw new DirectoryNotFoundException("Mod source directory not found: " + fullSourcePath);
        }
        if (!Directory.Exists(fullRepositoryRoot))
        {
            throw new DirectoryNotFoundException("Repository directory not found: " + fullRepositoryRoot);
        }
        if (!Directory.Exists(fullLoaderRoot))
        {
            throw new DirectoryNotFoundException("Loader Mods directory not found: " + fullLoaderRoot);
        }
        if (!IsSameOrInside(fullSourcePath, fullRepositoryRoot))
        {
            throw new InvalidOperationException("The Mod source is outside the selected repository.");
        }

        string characterDirectory = Path.GetDirectoryName(fullSourcePath)
            ?? throw new InvalidOperationException("The Mod source does not have a character folder.");
        if (string.Equals(characterDirectory, fullRepositoryRoot, StringComparison.OrdinalIgnoreCase)
            || !IsSameOrInside(characterDirectory, fullRepositoryRoot))
        {
            throw new InvalidOperationException("The Mod source must be inside a first-level character folder.");
        }

        var links = new List<string>();
        foreach (string siblingSource in Directory.EnumerateDirectories(characterDirectory))
        {
            string fullSiblingSource = NormalizeDirectoryPath(siblingSource);
            if (string.Equals(fullSiblingSource, fullSourcePath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string siblingName = Path.GetFileName(fullSiblingSource);
            string deployedPath = Path.Combine(fullLoaderRoot, siblingName);
            if (!IsDirectoryLink(deployedPath))
            {
                continue;
            }

            string? resolvedTarget = TryGetTarget(deployedPath);
            if (!string.IsNullOrWhiteSpace(resolvedTarget)
                && string.Equals(
                    NormalizeDirectoryPath(resolvedTarget),
                    fullSiblingSource,
                    StringComparison.OrdinalIgnoreCase))
            {
                links.Add(Path.GetFullPath(deployedPath));
            }
        }

        links.Sort(StringComparer.OrdinalIgnoreCase);
        return links;
    }

    private static void SetMountPointReparseData(string junctionPath, string targetPath)
    {
        string substituteName = @"\??\" + targetPath;
        string printName = targetPath;
        byte[] substituteBytes = System.Text.Encoding.Unicode.GetBytes(substituteName);
        byte[] printBytes = System.Text.Encoding.Unicode.GetBytes(printName);
        int pathBufferLength = substituteBytes.Length + sizeof(char) + printBytes.Length + sizeof(char);
        ushort reparseDataLength = checked((ushort)(8 + pathBufferLength));
        byte[] buffer = new byte[8 + reparseDataLength];

        WriteUInt32(buffer, 0, IoReparseTagMountPoint);
        WriteUInt16(buffer, 4, reparseDataLength);
        WriteUInt16(buffer, 6, 0);
        WriteUInt16(buffer, 8, 0);
        WriteUInt16(buffer, 10, checked((ushort)substituteBytes.Length));
        WriteUInt16(buffer, 12, checked((ushort)(substituteBytes.Length + sizeof(char))));
        WriteUInt16(buffer, 14, checked((ushort)printBytes.Length));
        Buffer.BlockCopy(substituteBytes, 0, buffer, 16, substituteBytes.Length);
        Buffer.BlockCopy(printBytes, 0, buffer, 16 + substituteBytes.Length + sizeof(char), printBytes.Length);

        using SafeFileHandle handle = CreateFileW(
            junctionPath,
            GenericWrite,
            FileShareRead | FileShareWrite | FileShareDelete,
            IntPtr.Zero,
            OpenExisting,
            FileFlagBackupSemantics | FileFlagOpenReparsePoint,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to open the junction directory.");
        }

        if (!DeviceIoControl(
                handle,
                FsctlSetReparsePoint,
                buffer,
                buffer.Length,
                IntPtr.Zero,
                0,
                out _,
                IntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to create the directory junction.");
        }
    }

    private static string NormalizeDirectoryPath(string path)
    {
        string fullPath = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(fullPath) || string.Equals(fullPath, Path.GetPathRoot(fullPath), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("A drive root cannot be used as a Mod junction path.");
        }
        return fullPath;
    }

    private static bool IsSameOrInside(string candidate, string root) => PathSafety.IsSameOrInside(candidate, root);

    private static void WriteUInt16(byte[] buffer, int offset, ushort value)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        Buffer.BlockCopy(bytes, 0, buffer, offset, bytes.Length);
    }

    private static void WriteUInt32(byte[] buffer, int offset, uint value)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        Buffer.BlockCopy(bytes, 0, buffer, offset, bytes.Length);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle device,
        uint ioControlCode,
        byte[] inBuffer,
        int inBufferSize,
        IntPtr outBuffer,
        int outBufferSize,
        out int bytesReturned,
        IntPtr overlapped);
}
