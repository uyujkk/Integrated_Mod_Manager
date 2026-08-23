namespace IntegratedModManager.Core;

public enum DownloadTaskState
{
    Queued,
    Preparing,
    Downloading,
    Extracting,
    Canceling,
    Completed,
    Failed,
    Canceled
}

/// <summary>
/// Keeps queued UI callbacks from moving a download task backwards to an older
/// phase or a lower percentage.
/// </summary>
public static class DownloadProgressPolicy
{
    public static bool IsTerminal(DownloadTaskState state)
        => state is DownloadTaskState.Completed or DownloadTaskState.Failed or DownloadTaskState.Canceled;

    public static bool IsActive(DownloadTaskState state) => !IsTerminal(state);

    public static bool CanApply(DownloadTaskState current, DownloadTaskState next)
    {
        if (current == next)
        {
            return true;
        }

        return !IsTerminal(current) && GetPhaseRank(next) >= GetPhaseRank(current);
    }

    public static double MergeProgress(double current, double requested)
        => Math.Max(Math.Clamp(current, 0, 100), Math.Clamp(requested, 0, 100));

    private static int GetPhaseRank(DownloadTaskState state) => state switch
    {
        DownloadTaskState.Queued => 0,
        DownloadTaskState.Preparing => 1,
        DownloadTaskState.Downloading => 2,
        DownloadTaskState.Extracting => 3,
        DownloadTaskState.Canceling => 4,
        DownloadTaskState.Completed or DownloadTaskState.Failed or DownloadTaskState.Canceled => 5,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
    };
}
