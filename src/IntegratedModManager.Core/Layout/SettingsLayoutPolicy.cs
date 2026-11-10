namespace IntegratedModManager.Core.Layout;

/// <summary>
/// The Settings page in two columns: the side column's later groups may move to the end of the main column so
/// both columns end at about the same height (a maximized window otherwise leaves the main column's lower part empty).
/// </summary>
public static class SettingsLayoutPolicy
{
    /// <summary>
    /// Which of <paramref name="movableGroups"/> (side-column groups, in their order, each with its height) go to the
    /// end of the main column. Picks the set that leaves the smallest difference between the column bottoms; on a tie
    /// it moves fewer groups, so nothing moves unless that actually evens the columns out.
    /// </summary>
    /// <param name="mainHeight">The main column without any moved group.</param>
    /// <param name="sideHeight">The side column's groups that never move.</param>
    /// <param name="spacing">The space a group adds between itself and the one before it.</param>
    public static bool[] BalanceColumns(double mainHeight, double sideHeight, IReadOnlyList<double> movableGroups, double spacing = 0)
    {
        int count = movableGroups.Count;
        var best = new bool[count];
        if (count == 0 || count > 16) return best;
        double total = mainHeight + sideHeight + movableGroups.Sum() + count * spacing;
        double bestDifference = Math.Abs(mainHeight - (total - mainHeight));
        int bestMoved = 0;
        for (int mask = 1; mask < 1 << count; mask++)
        {
            double main = mainHeight;
            int moved = 0;
            for (int i = 0; i < count; i++)
            {
                if ((mask & (1 << i)) == 0) continue;
                main += movableGroups[i] + spacing;
                moved++;
            }
            double difference = Math.Abs(main - (total - main));
            if (difference < bestDifference - 0.5 || (Math.Abs(difference - bestDifference) <= 0.5 && moved < bestMoved))
            {
                bestDifference = difference;
                bestMoved = moved;
                for (int i = 0; i < count; i++) best[i] = (mask & (1 << i)) != 0;
            }
        }
        return best;
    }
}
