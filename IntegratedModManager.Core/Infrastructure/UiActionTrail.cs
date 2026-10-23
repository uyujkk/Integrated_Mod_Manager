namespace IntegratedModManager.Core.Infrastructure;

public sealed record UiActionEntry(DateTimeOffset AtUtc, string Stage, int ItemId, double Width, double Height);
public sealed class UiActionTrailDocument
{
    public int Version { get; set; } = 1;
    public string AppVersion { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public bool ClosedNormally { get; set; }
    public List<UiActionEntry> Actions { get; set; } = [];
}

/// <summary>Bounded local action context without URLs, file paths, credentials or Mod descriptions.</summary>
public sealed class UiActionTrail
{
    private readonly string _path;
    private readonly object _gate = new();
    private readonly UiActionTrailDocument _document;
    public UiActionTrail(string path, string appVersion)
    {
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        if (File.Exists(path)) File.Copy(path, path + ".previous", overwrite: true);
        _document = new() { AppVersion = appVersion, ProcessId = Environment.ProcessId };
    }

    public void Record(string stage, int itemId = 0, double width = 0, double height = 0, bool closed = false)
    {
        lock (_gate)
        {
            _document.Actions.Add(new(DateTimeOffset.UtcNow, stage, itemId,
                double.IsFinite(width) ? width : 0, double.IsFinite(height) ? height : 0));
            if (_document.Actions.Count > 32) _document.Actions.RemoveAt(0);
            _document.ClosedNormally = closed;
            RecoveryJsonFile.Write(_path, _document, keepBackup: false);
        }
    }
}
