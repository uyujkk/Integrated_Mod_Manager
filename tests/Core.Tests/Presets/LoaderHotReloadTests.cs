using IntegratedModManager.Core.Presets;

namespace IntegratedModManager.Core.Tests;

public sealed class LoaderHotReloadTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "imm-hot-reload-" + Guid.NewGuid().ToString("N"));
    private string UserIni => Path.Combine(_root, "d3dx_user.ini");

    public LoaderHotReloadTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(UserIni, "[Constants]\n$\\mods\\a\\x = 2\n");
    }

    public void Dispose()
    {
        if (File.Exists(UserIni)) File.SetAttributes(UserIni, FileAttributes.Normal);
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Reload_KeepsTheFileReadOnlyWhileTheKeyIsSentAndRestoresAttributes()
    {
        bool readOnlyDuringSend = false;
        bool loaderSaveFailed = false;

        LoaderHotReloadResult result = await LoaderHotReload.ReloadAsync(UserIni, _ =>
        {
            readOnlyDuringSend = File.GetAttributes(UserIni).HasFlag(FileAttributes.ReadOnly);
            // What the loader does on reload: try to save its in-memory values first.
            try { File.WriteAllText(UserIni, "[Constants]\n$\\mods\\a\\x = 1\n"); }
            catch (UnauthorizedAccessException) { loaderSaveFailed = true; }
            return Task.CompletedTask;
        }, TimeSpan.FromMilliseconds(10));

        Assert.True(readOnlyDuringSend);
        Assert.True(loaderSaveFailed);
        Assert.Equal(new LoaderHotReloadResult(true, true, null), result);
        Assert.False(File.GetAttributes(UserIni).HasFlag(FileAttributes.ReadOnly));
        Assert.Contains("x = 2", File.ReadAllText(UserIni));
    }

    [Fact]
    public async Task Reload_ReportsASendFailureAndKeepsAnOriginallyReadOnlyFile()
    {
        File.SetAttributes(UserIni, FileAttributes.ReadOnly);

        LoaderHotReloadResult result = await LoaderHotReload.ReloadAsync(UserIni,
            _ => throw new InvalidOperationException("no target"), TimeSpan.FromSeconds(30));

        Assert.Equal(new LoaderHotReloadResult(false, true, "no target"), result);
        Assert.True(File.GetAttributes(UserIni).HasFlag(FileAttributes.ReadOnly));
    }

    [Fact]
    public async Task Reload_ReportsAFileChangedAfterTheHold()
    {
        LoaderHotReloadResult result = await LoaderHotReload.ReloadAsync(UserIni, _ =>
        {
            // A writer that ignores the attribute (or clears it) still shows up as not kept.
            File.SetAttributes(UserIni, FileAttributes.Normal);
            File.WriteAllText(UserIni, "changed");
            return Task.CompletedTask;
        }, TimeSpan.Zero);

        Assert.False(result.FileKept);
        Assert.True(result.ReloadSent);
    }

    [Fact]
    public async Task Reload_RejectsOtherFilesAndRestoresAttributesWhenCancelled()
    {
        string other = Path.Combine(_root, "d3dx.ini");
        File.WriteAllText(other, "x");
        await Assert.ThrowsAsync<InvalidDataException>(() => LoaderHotReload.ReloadAsync(other, _ => Task.CompletedTask, TimeSpan.Zero));

        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => LoaderHotReload.ReloadAsync(UserIni, _ =>
        {
            cancellation.Cancel();
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(30), cancellation.Token));
        Assert.False(File.GetAttributes(UserIni).HasFlag(FileAttributes.ReadOnly));
    }
}
