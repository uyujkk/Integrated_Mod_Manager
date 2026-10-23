namespace ModFolderCopier.WinUI;

public sealed class ProgressInfo
{
    public ProgressInfo(int percent, string message)
    {
        Percent = percent;
        Message = message;
    }

    public int Percent { get; }

    public string Message { get; }
}
