using IntegratedModManager.Core.Shortcuts;

namespace IntegratedModManager.Core.Tests;

public sealed class ShortcutPrivilegePolicyTests
{
    private const int Medium = 0x2000, High = 0x3000, System = 0x4000;

    [Fact]
    public void MediumSenderToElevatedTargetIsBlocked() =>
        Assert.Equal(ShortcutPrivilegeVerdict.TargetHigher, ShortcutPrivilegePolicy.Evaluate(Medium, false, High, 0));

    [Fact]
    public void DeniedTokenQueryFromMediumSenderIsTreatedAsElevatedTarget() =>
        Assert.Equal(ShortcutPrivilegeVerdict.TargetUnreadable, ShortcutPrivilegePolicy.Evaluate(Medium, false, null, 5));

    [Fact]
    public void UnknownSenderWithDeniedTargetIsStillBlocked() =>
        Assert.Equal(ShortcutPrivilegeVerdict.TargetUnreadable, ShortcutPrivilegePolicy.Evaluate(null, false, null, 5));

    [Fact]
    public void ElevatedSenderMayReachUnreadableTarget() =>
        Assert.Equal(ShortcutPrivilegeVerdict.Allowed, ShortcutPrivilegePolicy.Evaluate(High, false, null, 5));

    [Fact]
    public void OtherQueryFailuresDoNotBlock() =>
        Assert.Equal(ShortcutPrivilegeVerdict.Allowed, ShortcutPrivilegePolicy.Evaluate(Medium, false, null, 87));

    [Theory]
    [InlineData(Medium, Medium)]
    [InlineData(High, Medium)]
    [InlineData(High, High)]
    public void SameOrLowerTargetIsAllowed(int sender, int target) =>
        Assert.Equal(ShortcutPrivilegeVerdict.Allowed, ShortcutPrivilegePolicy.Evaluate(sender, false, target, 0));

    [Fact]
    public void ElevatedSenderToSystemTargetIsBlocked() =>
        Assert.Equal(ShortcutPrivilegeVerdict.TargetHigher, ShortcutPrivilegePolicy.Evaluate(High, false, System, 0));

    [Theory]
    [InlineData(null, 5)]
    [InlineData(High, 0)]
    public void UiAccessSenderBypassesIntegrityChecks(int? target, int error) =>
        Assert.Equal(ShortcutPrivilegeVerdict.Allowed, ShortcutPrivilegePolicy.Evaluate(Medium, true, target, error));

    [Theory]
    [InlineData(null, false)]
    [InlineData(Medium, false)]
    [InlineData(High, true)]
    [InlineData(System, true)]
    public void ElevationUsesHighIntegrityThreshold(int? level, bool expected) =>
        Assert.Equal(expected, ShortcutPrivilegePolicy.IsElevated(level));
}
