using System.Diagnostics;

namespace IntegratedModManager.Core;

public enum RepositoryPerformanceOperation { Scan, Projection, Refresh, Populate, Filter, CoverLayout }
public sealed record RepositoryPerformanceSample(
    DateTimeOffset TimestampUtc, RepositoryPerformanceOperation Operation, double ElapsedMs,
    int ItemCount, int CacheHits, int ThreadId, double Width, double Height, double Scale);

/// <summary>Optional local diagnostics. Samples contain counts/timings only, never
/// names, paths, queries or values. A failing sink cannot interrupt repository work.</summary>
public sealed class RepositoryPerformanceRecorder(bool enabled, Action<RepositoryPerformanceSample> sink)
{
    public Scope? Measure(RepositoryPerformanceOperation operation, int count = 0,
        int hits = 0, double width = 0, double height = 0, double scale = 0)
        => enabled ? new Scope(sink, operation, count, hits, width, height, scale) : null;

    public sealed class Scope : IDisposable
    {
        private readonly Stopwatch _watch = Stopwatch.StartNew();
        private readonly DateTimeOffset _start = DateTimeOffset.UtcNow;
        private readonly int _thread = Environment.CurrentManagedThreadId;
        private readonly Action<RepositoryPerformanceSample> _sink;
        private readonly RepositoryPerformanceOperation _operation;
        private int _count, _hits;
        private readonly double _width, _height, _scale;
        private bool _disposed;
        internal Scope(Action<RepositoryPerformanceSample> sink, RepositoryPerformanceOperation operation,
            int count, int hits, double width, double height, double scale)
        { _sink = sink; _operation = operation; _count = count; _hits = hits; _width = width; _height = height; _scale = scale; }
        public void SetCounts(int count, int hits = 0)
        {
            if (_disposed) return;
            _count = count; _hits = hits;
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _watch.Stop();
            try { _sink(new(_start, _operation, _watch.Elapsed.TotalMilliseconds, _count, _hits, _thread, _width, _height, _scale)); }
            catch { /* Diagnostics must not interfere with refresh, including failures/cancellation. */ }
        }
    }
}
