namespace IntegratedModManager.Core;

public static class ModUpdateRecoveryPolicy
{
    public static DownloadTaskRecord<TRequest> ForSnapshot<TRequest>(DownloadTaskRecord<TRequest> task)
    {
        if (!task.SourceIdentity.StartsWith("mod-update|", StringComparison.Ordinal)) return task;
        // A new confirmation and fresh source fingerprint are required after every restart.
        return DownloadProgressPolicy.IsActive(task.State)
            ? task with
            {
                State = DownloadTaskState.Failed, Interrupted = true, Request = default,
                StatusZh = "Mod 更新中断。请检查原目录和旧版备份，再重新检查更新并确认替换。",
                StatusEn = "Mod update interrupted. Inspect the original folder and backup, then check and confirm replacement again."
            }
            : task with { Request = default };
    }
}
