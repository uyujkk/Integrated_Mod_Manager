namespace IntegratedModManager.Core.Configuration;

/// <summary>The things a new user does once, in the order the dashboard's "First steps" card lists them.</summary>
public enum GettingStartedTask
{
    Paths,
    FirstMod,
    Deploy,
    Search,
    Shortcuts
}

/// <summary>What the app already knows the user has done; each task ticks itself off from this.</summary>
public readonly record struct GettingStartedProgress(bool PathsReady, bool HasMods, bool HasDeployed, bool UsedSearch, bool VisitedShortcuts);

/// <summary>
/// The "First steps" card on the dashboard. It replaced the step-by-step tips of stage J4-b: instead of a popup that
/// walks through the dashboard, a card lists a few first tasks, ticks each one off when it is done anywhere in the
/// app, and takes the user to the place for each. A first install shows it; anyone can show it again from the
/// command palette. Closing it hides it for good; closing the app keeps it.
/// </summary>
public sealed class GettingStartedChecklist
{
    public static readonly IReadOnlyList<GettingStartedTask> Tasks =
        [GettingStartedTask.Paths, GettingStartedTask.FirstMod, GettingStartedTask.Deploy, GettingStartedTask.Search, GettingStartedTask.Shortcuts];

    private GettingStartedChecklist(bool pending) => IsPending = pending;

    /// <summary>
    /// The card at startup: shown on a first install, else as it was saved (false in configs from before the card,
    /// so upgrades do not get it unless they ask for it).
    /// </summary>
    public static GettingStartedChecklist Load(bool firstInstall, bool savedPending) => new(firstInstall || savedPending);

    /// <summary>Whether the card is on the dashboard; saved with the shell settings.</summary>
    public bool IsPending { get; private set; }

    public static bool IsDone(GettingStartedTask task, GettingStartedProgress progress) => task switch
    {
        GettingStartedTask.Paths => progress.PathsReady,
        GettingStartedTask.FirstMod => progress.HasMods,
        GettingStartedTask.Deploy => progress.HasDeployed,
        GettingStartedTask.Search => progress.UsedSearch,
        _ => progress.VisitedShortcuts
    };

    public static int DoneCount(GettingStartedProgress progress) => Tasks.Count(task => IsDone(task, progress));

    public static bool AllDone(GettingStartedProgress progress) => DoneCount(progress) == Tasks.Count;

    /// <summary>The first task still open, which the card points at; null once everything is done.</summary>
    public static GettingStartedTask? Next(GettingStartedProgress progress)
    {
        foreach (GettingStartedTask task in Tasks)
            if (!IsDone(task, progress)) return task;
        return null;
    }

    /// <summary>Closed by the user: not shown again until asked for.</summary>
    public void Close() => IsPending = false;

    /// <summary>Asked for again from the command palette.</summary>
    public void Show() => IsPending = true;
}
