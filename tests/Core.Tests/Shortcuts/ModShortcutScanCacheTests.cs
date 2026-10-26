using IntegratedModManager.Core.Shortcuts;

namespace IntegratedModManager.Core.Tests;

public sealed class ModShortcutScanCacheTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "imm-shortcut-cache-" + Guid.NewGuid().ToString("N"));
    public ModShortcutScanCacheTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);

    [Fact]
    public void ReusesUnchangedScanAndInvalidatesOnEditAddDeleteRenameAndForce()
    {
        var cache = new ModShortcutScanCache();
        string ini = Path.Combine(root, "mod.ini");
        File.WriteAllText(ini, "[KeyA]\nkey = A\n$x = 1\n");
        var first = cache.Scan(root);
        Assert.Single(first);
        Assert.Same(first, cache.Scan(root));
        Assert.NotSame(first, cache.Scan(root, force: true));
        File.WriteAllText(ini, "[KeyA]\nkey = F10\n$x = 2\n");
        Assert.Equal("F10", Assert.Single(cache.Scan(root)).RawKey);
        string added = Path.Combine(root, "extra.ini");
        File.WriteAllText(added, "[KeyB]\nkey = B\n$x = 3\n");
        Assert.Equal(2, cache.Scan(root).Count);
        File.Move(added, Path.Combine(root, "DISABLED_extra.ini"));
        Assert.Single(cache.Scan(root));
        File.Delete(ini);
        Assert.Empty(cache.Scan(root));
        Assert.Empty(cache.Scan(Path.Combine(root, "missing")));
    }
}
