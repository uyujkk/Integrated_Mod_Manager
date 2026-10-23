namespace IntegratedModManager.Core;

/// <summary>Bounded .NET image streams, independent of WinRT path/URI parsing.</summary>
public static class ImageFileReadPolicy
{
    public const long DefaultMaximumBytes = 25 * 1024 * 1024;

    public static FileStream OpenRead(string path, long maximumBytes = DefaultMaximumBytes)
    {
        if (maximumBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        var file = new FileStream(Path.GetFullPath(path), FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        try
        {
            if (file.Length <= 0 || file.Length > maximumBytes)
                throw new InvalidDataException("The image is empty or exceeds the size limit.");
            return file;
        }
        catch { file.Dispose(); throw; }
    }
}
