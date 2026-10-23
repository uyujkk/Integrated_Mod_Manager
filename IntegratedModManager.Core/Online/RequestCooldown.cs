namespace IntegratedModManager.Core;

public sealed class RequestCooldown
{
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly TimeSpan _initialDelay;
    private readonly TimeSpan _maximumBackoff;
    private readonly TimeSpan _maximumServerDelay;

    public RequestCooldown(
        Func<DateTimeOffset>? utcNow = null,
        TimeSpan? initialDelay = null,
        TimeSpan? maximumBackoff = null,
        TimeSpan? maximumServerDelay = null)
    {
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _initialDelay = RequirePositive(initialDelay ?? TimeSpan.FromSeconds(15), nameof(initialDelay));
        _maximumBackoff = RequirePositive(maximumBackoff ?? TimeSpan.FromMinutes(5), nameof(maximumBackoff));
        _maximumServerDelay = RequirePositive(maximumServerDelay ?? TimeSpan.FromMinutes(10), nameof(maximumServerDelay));
    }

    public int StrikeCount { get; private set; }

    public DateTimeOffset? UntilUtc { get; private set; }

    public bool TryGetRemaining(out TimeSpan remaining)
    {
        remaining = (UntilUtc ?? DateTimeOffset.MinValue) - _utcNow();
        if (remaining > TimeSpan.Zero)
        {
            return true;
        }

        UntilUtc = null;
        remaining = TimeSpan.Zero;
        return false;
    }

    public TimeSpan Register(TimeSpan? serverRetryAfter)
    {
        StrikeCount = Math.Min(StrikeCount + 1, 6);
        double multiplier = Math.Pow(2, StrikeCount - 1);
        TimeSpan backoff = TimeSpan.FromMilliseconds(
            Math.Min(_maximumBackoff.TotalMilliseconds, _initialDelay.TotalMilliseconds * multiplier));
        TimeSpan delay = serverRetryAfter is { } serverDelay && serverDelay > TimeSpan.Zero
            ? TimeSpan.FromMilliseconds(Math.Min(
                _maximumServerDelay.TotalMilliseconds,
                Math.Max(backoff.TotalMilliseconds, serverDelay.TotalMilliseconds)))
            : backoff;

        UntilUtc = _utcNow().Add(delay);
        return delay;
    }

    public void Reset()
    {
        StrikeCount = 0;
        UntilUtc = null;
    }

    private static TimeSpan RequirePositive(TimeSpan value, string parameterName)
    {
        if (value <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(parameterName, "The duration must be positive.");
        }

        return value;
    }
}
