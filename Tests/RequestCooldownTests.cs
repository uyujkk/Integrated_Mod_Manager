using IntegratedModManager.Core;

namespace IntegratedModManager.Core.Tests;

public sealed class RequestCooldownTests
{
    [Fact]
    public void Register_UsesExponentialBackoffAndCapsIt()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-22T12:00:00Z");
        var cooldown = new RequestCooldown(
            () => now,
            initialDelay: TimeSpan.FromSeconds(2),
            maximumBackoff: TimeSpan.FromSeconds(5));

        Assert.Equal(TimeSpan.FromSeconds(2), cooldown.Register(null));
        Assert.Equal(TimeSpan.FromSeconds(4), cooldown.Register(null));
        Assert.Equal(TimeSpan.FromSeconds(5), cooldown.Register(null));
        Assert.Equal(3, cooldown.StrikeCount);

        for (int index = 0; index < 10; index++)
        {
            cooldown.Register(null);
        }

        Assert.Equal(6, cooldown.StrikeCount);
        Assert.Equal(TimeSpan.FromSeconds(5), cooldown.Register(null));
    }

    [Fact]
    public void Register_HonorsLongerServerDelayButCapsIt()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-22T12:00:00Z");
        var cooldown = new RequestCooldown(
            () => now,
            initialDelay: TimeSpan.FromSeconds(15),
            maximumServerDelay: TimeSpan.FromMinutes(10));

        TimeSpan delay = cooldown.Register(TimeSpan.FromMinutes(30));

        Assert.Equal(TimeSpan.FromMinutes(10), delay);
        Assert.Equal(now.AddMinutes(10), cooldown.UntilUtc);
    }

    [Fact]
    public void TryGetRemaining_ExpiresAgainstInjectedClock()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-22T12:00:00Z");
        var cooldown = new RequestCooldown(() => now);
        cooldown.Register(null);

        Assert.True(cooldown.TryGetRemaining(out TimeSpan active));
        Assert.Equal(TimeSpan.FromSeconds(15), active);

        now = now.AddSeconds(16);

        Assert.False(cooldown.TryGetRemaining(out TimeSpan expired));
        Assert.Equal(TimeSpan.Zero, expired);
        Assert.Null(cooldown.UntilUtc);
    }

    [Fact]
    public void Reset_ClearsStrikesAndActiveDelay()
    {
        var cooldown = new RequestCooldown();
        cooldown.Register(null);

        cooldown.Reset();

        Assert.Equal(0, cooldown.StrikeCount);
        Assert.Null(cooldown.UntilUtc);
        Assert.False(cooldown.TryGetRemaining(out _));
    }

    [Fact]
    public void Constructor_RejectsNonPositiveDurations()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RequestCooldown(initialDelay: TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RequestCooldown(maximumBackoff: TimeSpan.FromSeconds(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RequestCooldown(maximumServerDelay: TimeSpan.Zero));
    }
}
