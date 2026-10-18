using System.Text.Json;

namespace IntegratedModManager.Core;

/// <summary>Bounded local recovery records. Publish only flushed complete JSON; retain the previous version.</summary>
public static class RecoveryJsonFile
{
    public const int MaximumBytes = 16 * 1024 * 1024;

    public static T Read<T>(string path)
    {
        if (new FileInfo(path).Length > MaximumBytes) throw new InvalidDataException("Recovery record is too large.");
        return JsonSerializer.Deserialize<T>(File.ReadAllBytes(path)) ?? throw new InvalidDataException("Empty recovery record.");
    }

    public static void Write<T>(string path, T value, bool keepBackup = true)
    {
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(value);
        if (data.Length > MaximumBytes) throw new InvalidDataException("Recovery record is too large.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                81920, FileOptions.WriteThrough))
            {
                stream.Write(data);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(path) && keepBackup) File.Replace(temporary, path, path + ".backup");
            else File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
