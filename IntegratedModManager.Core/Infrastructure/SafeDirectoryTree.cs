namespace IntegratedModManager.Core;

public static class SafeDirectoryTree
{
    /// <summary>
    /// Lists every directory and file below <paramref name="rootPath"/>, sorted, and rejects
    /// the whole tree if the root or any entry is a junction or symbolic link. Inaccessible
    /// entries throw instead of being skipped, so a partial listing is never returned.
    /// </summary>
    public static (List<string> Directories, List<string> Files) Inspect(string rootPath)
    {
        string fullRoot = Path.GetFullPath(rootPath);
        if (!Directory.Exists(fullRoot))
        {
            throw new DirectoryNotFoundException($"Directory not found: {fullRoot}");
        }

        if ((File.GetAttributes(fullRoot) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Directory junctions and symbolic links are not supported: " + fullRoot);
        }

        var directories = new List<string>();
        var files = new List<string>();
        var pending = new Stack<string>();
        pending.Push(fullRoot);
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            ReturnSpecialDirectories = false,
            IgnoreInaccessible = false,
            AttributesToSkip = 0
        };

        while (pending.Count > 0)
        {
            string current = pending.Pop();
            foreach (FileSystemInfo entry in new DirectoryInfo(current).EnumerateFileSystemInfos("*", options))
            {
                FileAttributes attributes = entry.Attributes;
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException("Directory junctions and symbolic links are not supported: " + entry.FullName);
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    directories.Add(entry.FullName);
                    pending.Push(entry.FullName);
                }
                else
                {
                    files.Add(entry.FullName);
                }
            }
        }

        directories.Sort(StringComparer.OrdinalIgnoreCase);
        files.Sort(StringComparer.OrdinalIgnoreCase);
        return (directories, files);
    }
}
