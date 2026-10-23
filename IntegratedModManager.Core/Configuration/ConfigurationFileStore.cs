using System.Text;
using System.Text.Json;

namespace IntegratedModManager.Core;

/// <param name="Text">The configuration text that passed validation.</param>
/// <param name="RestoredFromBackup">True when the file was invalid and the latest valid backup replaced it.</param>
public readonly record struct ConfigurationReadResult(string Text, bool RestoredFromBackup);

/// <summary>
/// Reads and writes the application's configuration files durably. Writes go through a
/// temporary file and an atomic replace, and keep timestamped backups (at most one per
/// backup interval, pruned to a fixed count). A read that fails or does not validate
/// restores the newest backup that does.
/// </summary>
public sealed class ConfigurationFileStore
{
    private readonly string _backupDirectory;
    private readonly int _keepBackups;
    private readonly TimeSpan _backupInterval;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Dictionary<string, DateTimeOffset> _lastBackupUtc = new(StringComparer.OrdinalIgnoreCase);

    public ConfigurationFileStore(
        string backupDirectory,
        int keepBackups = 10,
        TimeSpan? backupInterval = null,
        Func<DateTimeOffset>? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupDirectory);
        _backupDirectory = backupDirectory;
        _keepBackups = Math.Max(1, keepBackups);
        _backupInterval = backupInterval ?? TimeSpan.FromMinutes(10);
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Returns the file's text when <paramref name="validator"/> accepts it. Otherwise the
    /// newest backup that validates is written back and returned; if none does, the
    /// original read or validation failure is rethrown.
    /// </summary>
    public ConfigurationReadResult Read(string filePath, Func<string, bool> validator)
    {
        ArgumentNullException.ThrowIfNull(validator);
        try
        {
            string text = File.ReadAllText(filePath, Encoding.UTF8);
            if (!validator(text))
            {
                throw new InvalidDataException($"Invalid configuration file: {Path.GetFileName(filePath)}");
            }

            return new ConfigurationReadResult(text, false);
        }
        catch when (TryRestoreLatestBackup(filePath, validator, out string restoredText))
        {
            return new ConfigurationReadResult(restoredText, true);
        }
    }

    public void Write(string filePath, string content)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? AppContext.BaseDirectory;
        Directory.CreateDirectory(directory);

        DateTimeOffset now = _clock();
        if (File.Exists(filePath)
            && (!_lastBackupUtc.TryGetValue(filePath, out DateTimeOffset lastBackup) || now - lastBackup >= _backupInterval))
        {
            Directory.CreateDirectory(_backupDirectory);
            string backupName = $"{Path.GetFileName(filePath)}.{now:yyyyMMdd-HHmmssfff}.bak";
            File.Copy(filePath, Path.Combine(_backupDirectory, backupName), overwrite: false);
            _lastBackupUtc[filePath] = now;
            PruneBackups(filePath);
        }

        string temporaryPath = filePath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporaryPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporaryPath, filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    /// <summary>Validator for JSON configuration: the text must parse to a JSON object.</summary>
    public static bool IsJsonObject(string text)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(text);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private bool TryRestoreLatestBackup(string filePath, Func<string, bool> validator, out string restoredText)
    {
        restoredText = string.Empty;
        try
        {
            if (!Directory.Exists(_backupDirectory))
            {
                return false;
            }

            foreach (string backupPath in BackupsNewestFirst(filePath))
            {
                string candidate = File.ReadAllText(backupPath, Encoding.UTF8);
                if (!validator(candidate))
                {
                    continue;
                }

                string restoreTemporaryPath = filePath + ".restore-" + Guid.NewGuid().ToString("N");
                File.WriteAllText(restoreTemporaryPath, candidate, new UTF8Encoding(false));
                File.Move(restoreTemporaryPath, filePath, overwrite: true);
                restoredText = candidate;
                return true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Recovery is best effort: the caller then sees the original failure.
        }

        return false;
    }

    private void PruneBackups(string filePath)
    {
        try
        {
            foreach (string staleBackup in BackupsNewestFirst(filePath).Skip(_keepBackups))
            {
                File.Delete(staleBackup);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A locked old backup is retried on the next write.
        }
    }

    private IEnumerable<string> BackupsNewestFirst(string filePath) =>
        Directory.EnumerateFiles(_backupDirectory, Path.GetFileName(filePath) + ".*.bak", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc);
}
