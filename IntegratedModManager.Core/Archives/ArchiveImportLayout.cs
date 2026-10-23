using IntegratedModManager.Core.Infrastructure;

namespace IntegratedModManager.Core.Archives;

public enum ArchiveLayoutKind
{
    /// <summary>Exactly one folder at the root: it already is the Mod folder.</summary>
    SingleFolder,

    /// <summary>Only files at the root (INI, buffers, textures): they need a Mod folder.</summary>
    FlatFiles,

    /// <summary>Files and folders side by side at the root: kept together in one Mod folder.</summary>
    Mixed,

    /// <summary>Several folders and no files: kept together so one archive stays one Mod.</summary>
    MultipleFolders
}

/// <param name="ContentRoot">The extracted folder whose contents become the Mod folder.</param>
/// <param name="ModFolderName">The name of the Mod folder created in the character folder.</param>
public sealed record ArchiveImportPlan(ArchiveLayoutKind Kind, string ContentRoot, string ModFolderName);

public sealed class ArchiveImportConflictException(string destination)
    : IOException("A Mod folder with this name already exists: " + destination)
{
    public string Destination { get; } = destination;
}

/// <summary>
/// Decides how an extracted archive becomes exactly one Mod folder, so archives without
/// a top-level folder never scatter files into the repository or a character folder,
/// and archives that already have one are not nested a second time.
/// </summary>
public static class ArchiveImportLayout
{
    private const string StagingPrefix = ".imm-import-";

    /// <summary>Archive tool and OS clutter that never decides the layout.</summary>
    public static bool IsArchiveClutter(string name) =>
        name.Equals("__MACOSX", StringComparison.OrdinalIgnoreCase)
        || name.Equals(".DS_Store", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase)
        || name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase);

    public static ArchiveImportPlan Plan(string extractedRoot, string archivePath, IEnumerable<string> archiveExtensions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extractedRoot);
        string root = Path.GetFullPath(extractedRoot);
        string[] directories = Directory.GetDirectories(root).Where(path => !IsArchiveClutter(Path.GetFileName(path))).ToArray();
        string[] files = Directory.GetFiles(root).Where(path => !IsArchiveClutter(Path.GetFileName(path))).ToArray();
        if (directories.Length == 0 && files.Length == 0)
        {
            throw ArchiveErrors.Create(ArchiveErrorKind.Empty, "The archive is empty.");
        }

        if (files.Length == 0 && directories.Length == 1)
        {
            return new ArchiveImportPlan(ArchiveLayoutKind.SingleFolder, directories[0], Path.GetFileName(directories[0]));
        }

        ArchiveLayoutKind kind = files.Length == 0
            ? ArchiveLayoutKind.MultipleFolders
            : directories.Length == 0 ? ArchiveLayoutKind.FlatFiles : ArchiveLayoutKind.Mixed;
        return new ArchiveImportPlan(kind, root, FolderNameFromArchive(archivePath, archiveExtensions));
    }

    /// <summary>A valid Windows folder name derived from the archive file name.</summary>
    public static string FolderNameFromArchive(string archivePath, IEnumerable<string> archiveExtensions)
    {
        string name = Path.GetFileName(archivePath ?? string.Empty);
        foreach (string extension in archiveExtensions.OrderByDescending(item => item.Length))
        {
            if (name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                name = name[..^extension.Length];
                break;
            }
        }

        if (name.EndsWith(".tar", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        // Windows silently drops trailing dots and spaces, which would change the name.
        name = name.Trim().TrimEnd('.', ' ');
        if (name.Length == 0 || name.StartsWith(StagingPrefix, StringComparison.OrdinalIgnoreCase) || IsReservedDeviceName(name))
        {
            return "ImportedMod";
        }

        return name;
    }

    /// <summary>
    /// Copies the planned content into a new Mod folder inside <paramref name="characterDirectory"/>.
    /// The folder is assembled under a staging name on the same volume and renamed in one
    /// step, so a failure or cancellation never leaves a half-written Mod or touches an
    /// existing folder. Returns the new Mod folder.
    /// </summary>
    public static string Install(ArchiveImportPlan plan, string characterDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(characterDirectory);
        string parent = Path.GetFullPath(characterDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!Directory.Exists(parent))
        {
            throw new DirectoryNotFoundException("Character folder not found: " + parent);
        }

        if ((File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("The character folder must not be a directory link: " + parent);
        }

        string destination = PathSafety.ResolveInsideDirectory(parent, plan.ModFolderName);
        if (!string.Equals(Path.GetDirectoryName(destination), parent, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The Mod folder name must be a single folder: " + plan.ModFolderName);
        }

        if (Directory.Exists(destination) || File.Exists(destination))
        {
            throw new ArchiveImportConflictException(destination);
        }

        string staging = Path.Combine(parent, StagingPrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            File.SetAttributes(staging, File.GetAttributes(staging) | FileAttributes.Hidden);
            CopyTree(plan.ContentRoot, staging, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (Directory.Exists(destination) || File.Exists(destination))
            {
                throw new ArchiveImportConflictException(destination);
            }

            Directory.Move(staging, destination);
            File.SetAttributes(destination, File.GetAttributes(destination) & ~FileAttributes.Hidden);
            return destination;
        }
        catch
        {
            DeleteOwnedTree(staging);
            throw;
        }
    }

    /// <summary>
    /// For a freshly extracted, manager-owned folder: when it holds exactly one folder
    /// and nothing else, moves that folder's contents up one level so the Mod is not
    /// nested twice. Relative references stay valid because the whole subtree moves.
    /// Returns false and changes nothing when the layout is anything else.
    /// </summary>
    public static bool HoistSingleTopFolder(string extractedFolder)
    {
        string root = Path.GetFullPath(extractedFolder);
        string[] entries = Directory.GetFileSystemEntries(root);
        string[] meaningful = entries.Where(path => !IsArchiveClutter(Path.GetFileName(path))).ToArray();
        if (meaningful.Length != 1 || !Directory.Exists(meaningful[0])
            || (File.GetAttributes(meaningful[0]) & FileAttributes.ReparsePoint) != 0)
        {
            return false;
        }

        string inner = meaningful[0];
        var clutter = entries.Where(path => !string.Equals(path, inner, StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        string[] children = Directory.GetFileSystemEntries(inner);
        if (children.Length == 0 || children.Any(child => clutter.Contains(Path.GetFileName(child))))
        {
            return false;
        }

        // Rename first so a child named like the inner folder itself cannot collide.
        string moving = Path.Combine(root, StagingPrefix + Guid.NewGuid().ToString("N"));
        Directory.Move(inner, moving);
        foreach (string child in Directory.GetFileSystemEntries(moving))
        {
            string target = Path.Combine(root, Path.GetFileName(child));
            if (Directory.Exists(child))
            {
                Directory.Move(child, target);
            }
            else
            {
                File.Move(child, target);
            }
        }

        Directory.Delete(moving, recursive: false);
        return true;
    }

    private static void CopyTree(string source, string destination, CancellationToken cancellationToken)
    {
        var pending = new Stack<(string Source, string Destination)>();
        pending.Push((Path.GetFullPath(source), destination));
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            (string from, string to) = pending.Pop();
            Directory.CreateDirectory(to);
            foreach (FileSystemInfo entry in new DirectoryInfo(from).EnumerateFileSystemInfos())
            {
                if (entry.Name.Equals("__MACOSX", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException("Archives containing links are not imported: " + entry.FullName);
                }

                string target = PathSafety.ResolveInsideDirectory(to, entry.Name);
                if ((entry.Attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push((entry.FullName, target));
                }
                else
                {
                    File.Copy(entry.FullName, target, overwrite: false);
                }
            }
        }
    }

    /// <summary>Deletes a folder this class created, without following any link inside it.</summary>
    private static void DeleteOwnedTree(string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                return;
            }

            foreach (FileSystemInfo entry in new DirectoryInfo(path).EnumerateFileSystemInfos())
            {
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    Directory.Delete(entry.FullName, recursive: false);
                }
                else if ((entry.Attributes & FileAttributes.Directory) != 0)
                {
                    DeleteOwnedTree(entry.FullName);
                }
                else
                {
                    entry.Attributes = FileAttributes.Normal;
                    entry.Delete();
                }
            }

            Directory.Delete(path, recursive: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Leaves only the hidden staging folder; the original error is what matters.
        }
    }

    private static bool IsReservedDeviceName(string name)
    {
        string stem = name.Split('.')[0].TrimEnd();
        return stem.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            || (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
                && char.IsAsciiDigit(stem[3]) && stem[3] != '0');
    }
}
