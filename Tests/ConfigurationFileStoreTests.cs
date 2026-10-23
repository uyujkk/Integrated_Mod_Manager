using IntegratedModManager.Core;

namespace IntegratedModManager.Core.Tests;

public sealed class ConfigurationFileStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "imm-config-store-" + Guid.NewGuid().ToString("N"));
    private DateTimeOffset _now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
    private string Config => Path.Combine(_root, "config.ini");
    private string Backups => Path.Combine(_root, "backups", "config");

    public ConfigurationFileStoreTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Write_ReplacesTheFileWithoutBomOrTemporaryLeftovers()
    {
        Store().Write(Config, "source_dir=x\n");

        Assert.Equal("source_dir=x\n", File.ReadAllText(Config));
        Assert.NotEqual(0xEF, File.ReadAllBytes(Config)[0]);
        Assert.Equal([Config], Directory.GetFiles(_root));
        Assert.False(Directory.Exists(Backups));
    }

    [Fact]
    public void Write_BacksUpTheOldFileAtMostOncePerInterval()
    {
        ConfigurationFileStore store = Store();
        store.Write(Config, "v1");
        store.Write(Config, "v2");
        _now += TimeSpan.FromMinutes(5);
        store.Write(Config, "v3");
        _now += TimeSpan.FromMinutes(6);
        store.Write(Config, "v4");

        string[] backups = Directory.GetFiles(Backups).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(["v1", "v3"], backups.Select(File.ReadAllText));
        Assert.All(backups, backup => Assert.StartsWith("config.ini.", Path.GetFileName(backup)));
        Assert.Equal("v4", File.ReadAllText(Config));
    }

    [Fact]
    public void Write_KeepsOnlyTheNewestBackups()
    {
        ConfigurationFileStore store = Store(keepBackups: 2);
        for (int version = 1; version <= 5; version++)
        {
            store.Write(Config, "v" + version);
            File.SetLastWriteTimeUtc(Config, new DateTime(2026, 1, version, 0, 0, 0, DateTimeKind.Utc));
            _now += TimeSpan.FromMinutes(11);
        }

        Assert.Equal(["v3", "v4"], Directory.GetFiles(Backups).Select(File.ReadAllText).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Read_ReturnsValidTextWithoutTouchingBackups()
    {
        File.WriteAllText(Config, "source_dir=ok");

        ConfigurationReadResult result = Store().Read(Config, LegacyConfigFile.IsValid);

        Assert.Equal(new ConfigurationReadResult("source_dir=ok", false), result);
    }

    [Fact]
    public void Read_RestoresTheNewestValidBackupWhenTheFileIsDamaged()
    {
        Directory.CreateDirectory(Backups);
        Backup("config.ini.20261001-000000000.bak", "source_dir=older", new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));
        Backup("config.ini.20261002-000000000.bak", "source_dir=newer", new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc));
        Backup("config.ini.20261003-000000000.bak", "damaged too", new DateTime(2026, 10, 3, 0, 0, 0, DateTimeKind.Utc));
        Backup("other.json.20261004-000000000.bak", "source_dir=wrong file", new DateTime(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc));
        File.WriteAllText(Config, "garbage");

        ConfigurationReadResult result = Store().Read(Config, LegacyConfigFile.IsValid);

        Assert.Equal(new ConfigurationReadResult("source_dir=newer", true), result);
        Assert.Equal("source_dir=newer", File.ReadAllText(Config));
        Assert.Equal([Config], Directory.GetFiles(_root));
    }

    [Fact]
    public void Read_RestoresABackupWhenTheFileIsMissing()
    {
        Directory.CreateDirectory(Backups);
        Backup("config.ini.20261001-000000000.bak", "source_dir=saved", DateTime.UtcNow);

        Assert.True(Store().Read(Config, LegacyConfigFile.IsValid).RestoredFromBackup);
        Assert.Equal("source_dir=saved", File.ReadAllText(Config));
    }

    [Fact]
    public void Read_DamagedFileWithoutUsableBackupThrowsAndKeepsTheFile()
    {
        File.WriteAllText(Config, "garbage");

        Assert.Throws<InvalidDataException>(() => Store().Read(Config, LegacyConfigFile.IsValid));
        Assert.Equal("garbage", File.ReadAllText(Config));
    }

    [Theory]
    [InlineData("{}", true)]
    [InlineData("{\"Repositories\":[]}", true)]
    [InlineData("[]", false)]
    [InlineData("null", false)]
    [InlineData("{", false)]
    [InlineData("", false)]
    public void IsJsonObject_AcceptsOnlyAJsonObject(string text, bool expected)
    {
        Assert.Equal(expected, ConfigurationFileStore.IsJsonObject(text));
    }

    private ConfigurationFileStore Store(int keepBackups = 10) => new(Backups, keepBackups, TimeSpan.FromMinutes(10), () => _now);

    private void Backup(string name, string content, DateTime lastWriteUtc)
    {
        string path = Path.Combine(Backups, name);
        File.WriteAllText(path, content);
        File.SetLastWriteTimeUtc(path, lastWriteUtc);
    }
}
