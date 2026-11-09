namespace IntegratedModManager.Core.Configuration;

/// <summary>The places the getting-started tips point at on the dashboard, in order.</summary>
public enum GettingStartedStep
{
    NewRepository,
    Paths,
    OpenMods
}

/// <summary>
/// The getting-started tips after a first install (stage J4-b): a short tour of the dashboard that
/// shows until it is closed or finished. Only a first install starts it, so people upgrading never see
/// it; closing the app before closing the tips keeps them for the next start.
/// </summary>
public sealed class GettingStartedTips
{
    public static readonly IReadOnlyList<GettingStartedStep> Steps = [GettingStartedStep.NewRepository, GettingStartedStep.Paths, GettingStartedStep.OpenMods];

    private int _index;

    private GettingStartedTips(bool pending) => IsPending = pending;

    /// <summary>
    /// The tips at startup: pending on a first install, else what was saved (false in configs from
    /// before the tips, so upgrades skip them).
    /// </summary>
    public static GettingStartedTips Load(bool firstInstall, bool savedPending) => new(firstInstall || savedPending);

    /// <summary>Whether the tips still have to be shown; saved with the shell settings.</summary>
    public bool IsPending { get; private set; }

    public GettingStartedStep Current => Steps[_index];

    /// <summary>The current step, counted from 1, for "1 of 3".</summary>
    public int Number => _index + 1;

    public bool IsLast => _index == Steps.Count - 1;

    /// <summary>Shown only on the dashboard, and never over a dialog.</summary>
    public bool ShouldShow(bool dashboardShown, bool dialogOpen) => IsPending && dashboardShown && !dialogOpen;

    /// <summary>Goes to the next step; after the last one the tips are done, as if closed.</summary>
    public void Next()
    {
        if (!IsPending) return;
        if (IsLast) Close();
        else _index++;
    }

    /// <summary>Closed by the user: not shown again.</summary>
    public void Close()
    {
        IsPending = false;
        _index = 0;
    }
}
