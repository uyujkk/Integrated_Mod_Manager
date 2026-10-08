using System.Text.Json;
using IntegratedModManager.Core;

namespace IntegratedModManager.Core.Tests;

public sealed class RepositoryPerformanceRecorderTests
{
    [Fact]
    public void DisabledRecorderDoesNotAllocateScopeOrCallSink()
    {
        var recorder = new RepositoryPerformanceRecorder(false, _ => throw new Exception("must not call"));
        Assert.Null(recorder.Measure(RepositoryPerformanceOperation.Refresh));
    }
    [Theory]
    [InlineData(RepositoryPerformanceOperation.Scan)] [InlineData(RepositoryPerformanceOperation.Projection)]
    [InlineData(RepositoryPerformanceOperation.Refresh)] [InlineData(RepositoryPerformanceOperation.Populate)]
    [InlineData(RepositoryPerformanceOperation.Filter)] [InlineData(RepositoryPerformanceOperation.CoverLayout)]
    public void CapturesDurationsCountsAndViewportOnce(RepositoryPerformanceOperation operation)
    {
        List<RepositoryPerformanceSample> records = [];
        var recorder = new RepositoryPerformanceRecorder(true, records.Add);
        var scope = recorder.Measure(operation, 1000, 900, 1200, 800, 1.25)!;
        scope.Dispose(); scope.Dispose();
        var record = Assert.Single(records);
        Assert.Equal(operation, record.Operation); Assert.Equal(1000, record.ItemCount); Assert.Equal(900, record.CacheHits);
        Assert.Equal(1200, record.Width); Assert.Equal(800, record.Height); Assert.Equal(1.25, record.Scale);
        Assert.InRange(record.ElapsedMs, 0, double.MaxValue); Assert.True(record.ThreadId > 0);
        Assert.Equal(TimeSpan.Zero, record.TimestampUtc.Offset);
    }
    [Fact]
    public void ThrowingDiagnosticSinkCannotBreakRepositoryWork()
    {
        var recorder = new RepositoryPerformanceRecorder(true, _ => throw new IOException("read-only log"));
        using (recorder.Measure(RepositoryPerformanceOperation.Filter)) { }
    }
    [Fact]
    public void CompletedScanCountsCanReplaceUnknownInitialCounts()
    {
        RepositoryPerformanceSample? sample = null;
        var scope = new RepositoryPerformanceRecorder(true, value => sample = value).Measure(RepositoryPerformanceOperation.Scan)!;
        scope.SetCounts(1000, 1000); scope.Dispose(); scope.SetCounts(0);
        Assert.Equal(1000, sample!.ItemCount); Assert.Equal(1000, sample.CacheHits);
    }
    [Fact]
    public void SerializationContainsNoPathModNameOrSearchTextFields()
    {
        RepositoryPerformanceSample? sample = null;
        using (new RepositoryPerformanceRecorder(true, value => sample = value).Measure(RepositoryPerformanceOperation.Scan)) { }
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(sample));
        Assert.Equal(["TimestampUtc", "Operation", "ElapsedMs", "ItemCount", "CacheHits", "ThreadId", "Width", "Height", "Scale"], json.RootElement.EnumerateObject().Select(p => p.Name));
    }
}
