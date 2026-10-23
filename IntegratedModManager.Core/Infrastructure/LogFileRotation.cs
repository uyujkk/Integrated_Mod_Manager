namespace IntegratedModManager.Core;

/// <summary>
/// Keeps an append-only local log bounded: once it exceeds the limit it becomes
/// "&lt;name&gt;.previous" (replacing an older one) and a new file starts. At most two
/// generations exist, so a long-running install never grows the log without bound.
/// </summary>
public static class LogFileRotation
{
    public const long DefaultMaximumBytes = 1024 * 1024;

    public static string GetPreviousPath(string logPath) => logPath + ".previous";

    /// <returns><see langword="true"/> when the log was rotated.</returns>
    public static bool RotateIfLarger(string logPath, long maximumBytes = DefaultMaximumBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logPath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);

        var file = new FileInfo(logPath);
        if (!file.Exists || file.Length <= maximumBytes)
        {
            return false;
        }

        File.Move(logPath, GetPreviousPath(logPath), overwrite: true);
        return true;
    }
}
