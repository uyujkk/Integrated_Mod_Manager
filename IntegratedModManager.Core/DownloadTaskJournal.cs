using System.Text.Json;

namespace IntegratedModManager.Core;

public sealed record DownloadTaskRecord<TRequest>(string Id, string Title, string SourceIdentity,
    string DestinationPath, DownloadTaskState State, double Progress, string StatusZh, string StatusEn,
    TRequest? Request, string ArchivePath = "", bool Interrupted = false);
public sealed class DownloadTaskJournalDocument<TRequest>
{
    public int Version { get; set; } = 1;
    public List<DownloadTaskRecord<TRequest>> Tasks { get; set; } = [];
}
public sealed record DownloadTaskJournalLoad<TRequest>(DownloadTaskRecord<TRequest>[] Tasks, bool UsedBackup);

/// <summary>Restores history and interrupted attempts, never starts network or filesystem operations.</summary>
public sealed class DownloadTaskJournal<TRequest>(string path)
{
    public const int MaximumTasks = 512;
    private bool _usedBackup;
    public DownloadTaskJournalLoad<TRequest> Load()
    {
        if (!File.Exists(path) && !File.Exists(path + ".backup")) return new([], false);
        try { return new(Read(path), false); }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
        {
            if (!File.Exists(path + ".backup")) throw;
            var recovered = Read(path + ".backup");
            _usedBackup = true;
            return new(recovered, true);
        }
    }

    private static DownloadTaskRecord<TRequest>[] Read(string file)
    {
        var document = RecoveryJsonFile.Read<DownloadTaskJournalDocument<TRequest>>(file);
        Validate(document);
        return document.Tasks.Select(task => DownloadProgressPolicy.IsActive(task.State)
            ? task with { State = DownloadTaskState.Failed, Interrupted = true,
                StatusZh = "上次运行中断。可检查原目录后重试；已有文件保留，重试会重新下载。",
                StatusEn = "Interrupted. Inspect the original folder before retrying; existing files are preserved and retry downloads again." }
            : task).ToArray();
    }

    public void Save(IEnumerable<DownloadTaskRecord<TRequest>> tasks)
    {
        var document = new DownloadTaskJournalDocument<TRequest> { Tasks = tasks.ToList() };
        Validate(document);
        if (_usedBackup && File.Exists(path)) File.Copy(path, path + ".corrupt-" + Guid.NewGuid().ToString("N"));
        RecoveryJsonFile.Write(path, document, keepBackup: !_usedBackup);
        _usedBackup = false;
    }

    private static void Validate(DownloadTaskJournalDocument<TRequest> document)
    {
        if (document.Version != 1 || document.Tasks is null || document.Tasks.Count > MaximumTasks
            || document.Tasks.Any(task => task is null || string.IsNullOrWhiteSpace(task.Id)
                || !Enum.IsDefined(task.State) || !double.IsFinite(task.Progress) || task.Progress is < 0 or > 100
                || (DownloadProgressPolicy.IsActive(task.State) && task.Request is null))
            || document.Tasks.Select(task => task.Id).Distinct(StringComparer.Ordinal).Count() != document.Tasks.Count)
            throw new InvalidDataException("Invalid download recovery record.");
    }
}
