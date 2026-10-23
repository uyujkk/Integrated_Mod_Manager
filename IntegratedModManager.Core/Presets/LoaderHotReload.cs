namespace IntegratedModManager.Core.Presets;

public sealed record LoaderHotReloadResult(bool ReloadSent, bool FileKept, string? SendError);

// Makes a running 3DMigoto/XXMI loader pick up a d3dx_user.ini written while the game runs.
// On a config reload (F10 by default) the loader first saves its in-memory persist values to
// d3dx_user.ini and then reads the file. Keeping the file read-only across the key press makes
// that save fail, so the reload reads the values just written. Afterwards the original attributes
// come back. FileKept reports whether the file still has the written bytes; if the reload key
// did not arrive, the running loader will overwrite the file when the game exits.
public static class LoaderHotReload
{
    public static readonly TimeSpan DefaultHold = TimeSpan.FromSeconds(3);

    public static async Task<LoaderHotReloadResult> ReloadAsync(
        string userIniPath, Func<CancellationToken, Task> sendReloadKey, TimeSpan hold, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(Path.GetFileName(userIniPath), "d3dx_user.ini", StringComparison.OrdinalIgnoreCase) || !File.Exists(userIniPath))
        {
            throw new InvalidDataException("Choose the loader's d3dx_user.ini.");
        }

        byte[] written = await File.ReadAllBytesAsync(userIniPath, cancellationToken);
        FileAttributes original = File.GetAttributes(userIniPath);
        File.SetAttributes(userIniPath, original | FileAttributes.ReadOnly);
        string? sendError = null;
        try
        {
            try
            {
                await sendReloadKey(cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                sendError = exception.Message;
            }

            if (sendError is null)
            {
                await Task.Delay(hold, cancellationToken);
            }
        }
        finally
        {
            File.SetAttributes(userIniPath, original);
        }

        bool kept = (await File.ReadAllBytesAsync(userIniPath, CancellationToken.None)).SequenceEqual(written);
        return new LoaderHotReloadResult(sendError is null, kept, sendError);
    }
}
