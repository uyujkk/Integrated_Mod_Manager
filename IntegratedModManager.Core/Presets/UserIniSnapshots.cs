using System.Globalization;

namespace IntegratedModManager.Core.Presets;

public sealed record UserIniSnapshot(string Path, string Name, DateTimeOffset SavedAt, long Bytes);

// Whole-file copies of d3dx_user.ini kept as manual restore points. Unlike state slots (which
// write only one Mod's keys), restoring a snapshot replaces every value in the file, so it is a
// fallback rather than a way to switch presets. Files are named "<yyyyMMdd-HHmmss>-<name>.ini".
public static class UserIniSnapshots
{
    private const long MaxBytes = 16 * 1024 * 1024;

    public static UserIniSnapshot Save(string userIniPath, string snapshotDirectory, string name, DateTimeOffset now)
    {
        ValidateUserIni(userIniPath);
        string safeName = SafeName(name);
        Directory.CreateDirectory(snapshotDirectory);
        string stamp = now.ToLocalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string path = Path.Combine(snapshotDirectory, $"{stamp}-{safeName}.ini");
        for (int suffix = 2; File.Exists(path); suffix++)
        {
            path = Path.Combine(snapshotDirectory, $"{stamp}-{safeName}-{suffix}.ini");
        }

        File.Copy(userIniPath, path);
        return Describe(path)!;
    }

    public static IReadOnlyList<UserIniSnapshot> List(string snapshotDirectory)
    {
        if (!Directory.Exists(snapshotDirectory))
        {
            return [];
        }

        return Directory.EnumerateFiles(snapshotDirectory, "*.ini")
            .Select(Describe)
            .OfType<UserIniSnapshot>()
            .OrderByDescending(snapshot => snapshot.SavedAt)
            .ThenBy(snapshot => snapshot.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    // Replaces d3dx_user.ini with the snapshot. The current file is kept beside it as
    // d3dx_user.ini.imm-snapshot-<time>.bak. The caller must close the loader first, since a
    // running loader rewrites the file on exit.
    public static string Restore(UserIniSnapshot snapshot, string userIniPath, DateTimeOffset now)
    {
        ValidateUserIni(userIniPath);
        if (!File.Exists(snapshot.Path) || new FileInfo(snapshot.Path).Length > MaxBytes)
        {
            throw new InvalidDataException("The snapshot is missing or exceeds the 16 MB safety limit.");
        }

        string stamp = now.ToUniversalTime().ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        string backup = userIniPath + ".imm-snapshot-" + stamp + ".bak";
        for (int suffix = 2; File.Exists(backup); suffix++)
        {
            backup = userIniPath + ".imm-snapshot-" + stamp + "-" + suffix + ".bak";
        }

        string temporary = userIniPath + ".imm-snapshot-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.Copy(snapshot.Path, temporary);
            File.Replace(temporary, userIniPath, backup);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }

        if (!File.ReadAllBytes(userIniPath).SequenceEqual(File.ReadAllBytes(snapshot.Path)))
        {
            File.Copy(backup, userIniPath, overwrite: true);
            throw new IOException("d3dx_user.ini did not match the snapshot after restoring; the previous file was put back.");
        }

        return backup;
    }

    public static void Delete(UserIniSnapshot snapshot)
    {
        if (File.Exists(snapshot.Path)) File.Delete(snapshot.Path);
    }

    private static UserIniSnapshot? Describe(string path)
    {
        string fileName = Path.GetFileNameWithoutExtension(path);
        if (fileName.Length < 17 || fileName[15] != '-'
            || !DateTime.TryParseExact(fileName[..15], "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime saved))
        {
            return null;
        }

        return new UserIniSnapshot(path, fileName[16..], new DateTimeOffset(saved), new FileInfo(path).Length);
    }

    private static string SafeName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string cleaned = new string((name ?? string.Empty).Trim().Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim('.', ' ');
        if (cleaned.Length == 0)
        {
            throw new ArgumentException("Enter a name for the snapshot.", nameof(name));
        }
        return cleaned.Length > 60 ? cleaned[..60] : cleaned;
    }

    private static void ValidateUserIni(string userIniPath)
    {
        if (!string.Equals(Path.GetFileName(userIniPath), "d3dx_user.ini", StringComparison.OrdinalIgnoreCase)
            || !File.Exists(userIniPath)
            || (File.GetAttributes(userIniPath) & FileAttributes.ReparsePoint) != 0
            || new FileInfo(userIniPath).Length > MaxBytes)
        {
            throw new InvalidDataException("Choose a real d3dx_user.ini within the 16 MB safety limit.");
        }
    }
}
