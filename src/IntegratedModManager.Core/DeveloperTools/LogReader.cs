using System.Globalization;
using System.Text;

namespace IntegratedModManager.Core.DeveloperTools;

public enum LogLevel { Info, Warning, Error }

/// <summary>One log entry: a timestamped line and the indented lines after it (a stack trace).</summary>
public sealed record LogEntry(DateTimeOffset? Time, LogLevel Level, string File, string Text);

/// <summary>
/// Reads the app's text logs (<c>startup.log</c>, <c>app-errors.log</c>, the updater's logs). Each entry starts with
/// an ISO 8601 time; lines without one belong to the entry before them. Reading never locks the file.
/// </summary>
public static class LogReader
{
    /// <summary>The logs the app writes, relative to its data folder, with the level of a plain line in each.</summary>
    public static IReadOnlyList<(string Name, LogLevel DefaultLevel)> KnownLogs { get; } =
    [
        ("startup.log", LogLevel.Info),
        ("app-errors.log", LogLevel.Error),
        ("update-agent.log", LogLevel.Info),
        ("update.log", LogLevel.Info),
        ("ui-diagnostics.log", LogLevel.Info)
    ];

    /// <summary>Reads at most the last <paramref name="maximumBytes"/> of a file, starting at a whole line.</summary>
    public static string[] ReadTail(string path, int maximumBytes = 1024 * 1024)
    {
        if (!File.Exists(path)) return [];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        long start = Math.Max(0, stream.Length - maximumBytes);
        stream.Seek(start, SeekOrigin.Begin);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        string text = reader.ReadToEnd();
        string[] lines = text.Replace("\r\n", "\n").Split('\n');
        // A tail that starts mid-file drops the partial first line.
        return start > 0 && lines.Length > 0 ? lines[1..] : lines;
    }

    public static List<LogEntry> Parse(string fileName, IEnumerable<string> lines, LogLevel defaultLevel)
    {
        var entries = new List<LogEntry>();
        DateTimeOffset? time = null;
        var text = new StringBuilder();
        bool open = false;

        void Flush()
        {
            if (!open) return;
            string value = text.ToString().TrimEnd();
            entries.Add(new LogEntry(time, Classify(value, defaultLevel), fileName, value));
            text.Clear();
            open = false;
        }

        foreach (string raw in lines)
        {
            string line = raw.TrimEnd('\r');
            if (line.Length == 0) continue;
            if (TryReadTime(line, out DateTimeOffset stamp, out string rest))
            {
                Flush();
                time = stamp;
                text.Append(rest);
                open = true;
            }
            else if (open)
            {
                text.Append('\n').Append(line);
            }
            else
            {
                time = null;
                text.Append(line);
                open = true;
            }
        }
        Flush();
        return entries;
    }

    /// <summary>Entries at or above a level, at or after a time, containing the text; newest last.</summary>
    public static IEnumerable<LogEntry> Filter(IEnumerable<LogEntry> entries, LogLevel minimum, DateTimeOffset? since, string? contains)
        => entries.Where(entry => entry.Level >= minimum
            && (since is null || entry.Time is null || entry.Time >= since)
            && (string.IsNullOrWhiteSpace(contains) || entry.Text.Contains(contains, StringComparison.OrdinalIgnoreCase)));

    /// <summary>All entries of several files in time order (entries without a time keep their place in their file).</summary>
    public static List<LogEntry> Merge(IEnumerable<IReadOnlyList<LogEntry>> files)
        => files.SelectMany(file => file.Select((entry, index) => (entry, index)))
            .OrderBy(item => item.entry.Time ?? DateTimeOffset.MinValue)
            .ThenBy(item => item.index)
            .Select(item => item.entry)
            .ToList();

    private static LogLevel Classify(string text, LogLevel defaultLevel)
    {
        if (defaultLevel == LogLevel.Error) return LogLevel.Error;
        if (Contains(text, "exception") || Contains(text, "error") || Contains(text, "failed")) return LogLevel.Error;
        if (Contains(text, "warn")) return LogLevel.Warning;
        return defaultLevel;
    }

    private static bool Contains(string text, string word) => text.Contains(word, StringComparison.OrdinalIgnoreCase);

    // "2026-11-16T13:44:01.1234567-05:00 rest" (the app writes DateTimeOffset.ToString("O")).
    private static bool TryReadTime(string line, out DateTimeOffset time, out string rest)
    {
        time = default;
        rest = string.Empty;
        int space = line.IndexOf(' ');
        if (space < 19 || !char.IsDigit(line[0])) return false;
        if (!DateTimeOffset.TryParse(line[..space], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out time)) return false;
        rest = line[(space + 1)..];
        return true;
    }
}
