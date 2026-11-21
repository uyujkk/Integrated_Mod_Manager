using IntegratedModManager.Core.Updates;

namespace IntegratedModManager.Core.Tests;

// When a tracked Mod counts as updated (stage J finish, step 5a: the rule moved out of the window unchanged).
public sealed class TrackedModUpdatePolicyTests
{
    private static readonly DateTimeOffset Recorded = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ANewerSiteTimeIsAnUpdate()
    {
        Assert.True(TrackedModUpdatePolicy.HasUpdate(Recorded, Recorded.AddHours(3)));
        Assert.True(TrackedModUpdatePolicy.HasUpdate(Recorded, Recorded.AddMinutes(1).AddSeconds(1)));
    }

    [Fact]
    public void WithinAMinuteIsTheSameVersion()
    {
        Assert.False(TrackedModUpdatePolicy.HasUpdate(Recorded, Recorded));
        Assert.False(TrackedModUpdatePolicy.HasUpdate(Recorded, Recorded.AddMinutes(1)));
        Assert.False(TrackedModUpdatePolicy.HasUpdate(Recorded, Recorded.AddSeconds(30)));
    }

    [Fact]
    public void AnOlderSiteTimeIsNotAnUpdate()
    {
        Assert.False(TrackedModUpdatePolicy.HasUpdate(Recorded, Recorded.AddDays(-2)));
    }

    [Fact]
    public void UnknownTimesAreNeverAnUpdate()
    {
        Assert.False(TrackedModUpdatePolicy.HasUpdate(null, Recorded));
        Assert.False(TrackedModUpdatePolicy.HasUpdate(Recorded, null));
        Assert.False(TrackedModUpdatePolicy.HasUpdate(null, null));
    }

    [Fact]
    public void TimeZonesDoNotMatter()
    {
        DateTimeOffset sameMoment = Recorded.ToOffset(TimeSpan.FromHours(8));
        Assert.False(TrackedModUpdatePolicy.HasUpdate(Recorded, sameMoment));
        Assert.True(TrackedModUpdatePolicy.HasUpdate(Recorded, sameMoment.AddMinutes(5)));
    }
}
