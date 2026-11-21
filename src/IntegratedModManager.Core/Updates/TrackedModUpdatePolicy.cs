namespace IntegratedModManager.Core.Updates;

/// <summary>
/// When a Mod installed from the online source has a newer version (the Updates page's check, moved out of the window
/// in stage J finish, step 5a): the site's update time is more than a minute after the one recorded at install. A Mod
/// whose recorded or current time is unknown is never reported as updated.
/// </summary>
public static class TrackedModUpdatePolicy
{
    /// <summary>Site and record times this close are the same version (rounding on either side).</summary>
    public static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(1);

    public static bool HasUpdate(DateTimeOffset? recorded, DateTimeOffset? latest)
        => latest.HasValue && recorded.HasValue && latest.Value > recorded.Value + Tolerance;
}
