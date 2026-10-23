using IntegratedModManager.Core.Infrastructure;

namespace IntegratedModManager.Core.Tests;

public sealed class LogFileRotationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "imm-log-rotation-" + Guid.NewGuid().ToString("N"));

    public LogFileRotationTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void MissingLogIsLeftAlone()
    {
        string path = Path.Combine(_directory, "app-errors.log");

        Assert.False(LogFileRotation.RotateIfLarger(path, 10));
        Assert.False(File.Exists(LogFileRotation.GetPreviousPath(path)));
    }

    [Fact]
    public void LogAtOrBelowLimitIsKept()
    {
        string path = Path.Combine(_directory, "app-errors.log");
        File.WriteAllText(path, "0123456789");

        Assert.False(LogFileRotation.RotateIfLarger(path, 10));
        Assert.Equal("0123456789", File.ReadAllText(path));
    }

    [Fact]
    public void LargeLogBecomesPreviousAndReplacesOlderGeneration()
    {
        string path = Path.Combine(_directory, "app-errors.log");
        string previous = LogFileRotation.GetPreviousPath(path);
        File.WriteAllText(previous, "old generation");
        File.WriteAllText(path, "current log that is too long");

        Assert.True(LogFileRotation.RotateIfLarger(path, 10));

        Assert.False(File.Exists(path));
        Assert.Equal("current log that is too long", File.ReadAllText(previous));
        Assert.Equal(path + ".previous", previous);
    }

    [Fact]
    public void InvalidArgumentsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => LogFileRotation.RotateIfLarger(" "));
        Assert.Throws<ArgumentOutOfRangeException>(() => LogFileRotation.RotateIfLarger(Path.Combine(_directory, "x.log"), 0));
    }
}
