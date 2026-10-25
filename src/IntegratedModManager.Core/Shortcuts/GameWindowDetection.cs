namespace IntegratedModManager.Core.Shortcuts;

public sealed record GameWindowCandidate(string ProcessName, string Title);

// Picks the game window for shortcut input among visible top-level windows, by process name.
// Only recognized game processes are chosen; anything else is left for the user to select.
public static class GameWindowDetection
{
    // Process names without ".exe", grouped per game. The first group is the Endfield client.
    public static readonly IReadOnlyList<IReadOnlyList<string>> KnownGames =
    [
        ["Endfield"],
        ["GenshinImpact", "YuanShen"],
        ["StarRail"],
        ["ZenlessZoneZero"]
    ];

    // Index of the window to select, or -1. Windows of the preferred game (for example the game of
    // the current repository) win over other recognized games; ties keep the list order.
    public static int Find(IReadOnlyList<GameWindowCandidate> windows, IReadOnlyCollection<string>? preferredProcessNames = null)
    {
        int Rank(GameWindowCandidate window)
        {
            string name = Normalize(window.ProcessName);
            if (preferredProcessNames?.Any(preferred => Normalize(preferred) == name) == true) return 0;
            return KnownGames.Any(game => game.Any(known => Normalize(known) == name)) ? 1 : int.MaxValue;
        }

        int best = -1;
        int bestRank = int.MaxValue;
        for (int index = 0; index < windows.Count; index++)
        {
            int rank = Rank(windows[index]);
            if (rank < bestRank)
            {
                best = index;
                bestRank = rank;
            }
        }

        return best;
    }

    private static string Normalize(string processName)
    {
        string name = Path.GetFileName(processName.Trim());
        return (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name).ToLowerInvariant();
    }
}
