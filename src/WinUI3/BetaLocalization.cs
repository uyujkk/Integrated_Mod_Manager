using System.Text;

namespace ModFolderCopier.WinUI;

internal static class BetaLocalization
{
    private const string MojibakeHints = "鍙鍦鏇璁鐩鍒寮閫鐢璺鏈宸褰鏉娌鎿纭浠撳簱瀵煎叆閰嶇疆鏂囦欢鍥剧墖棰勮妯″潡鎻愮ず";
    private const string ReadablePunctuation = "：:，。、“”‘’！？-_/()[]{}.+";

    public static string Resolve(bool useEnglish, string zh, string en)
    {
        if (useEnglish)
        {
            return en;
        }

        string normalizedZh = NormalizeChineseText(zh);
        return string.IsNullOrWhiteSpace(normalizedZh) ? en : PreserveBoundaryWhitespace(zh, normalizedZh);
    }

    private static string PreserveBoundaryWhitespace(string original, string translated)
    {
        int start = 0, end = original.Length;
        while (start < end && char.IsWhiteSpace(original[start])) start++;
        while (end > start && char.IsWhiteSpace(original[end - 1])) end--;
        // Messages are composed from localized fragments. Trimming their separators
        // joined preview summaries, backup paths and instructions into one line.
        return original[..start] + translated + original[end..];
    }

    public static string NormalizeChineseText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string trimmed = value.Trim();
        string best = trimmed;
        string candidate = trimmed;

        for (int i = 0; i < 2; i++)
        {
            string repaired = TryRepairMojibake(candidate);
            if (string.Equals(repaired, candidate, StringComparison.Ordinal))
            {
                break;
            }

            if (GetChineseReadabilityScore(repaired) > GetChineseReadabilityScore(best))
            {
                best = repaired;
            }

            candidate = repaired;
        }

        return best;
    }

    private static string TryRepairMojibake(string value)
    {
        try
        {
            byte[] bytes = Encoding.GetEncoding("GB18030").GetBytes(value);
            string repaired = Encoding.UTF8.GetString(bytes);
            return repaired.Contains('\uFFFD') ? value : repaired;
        }
        catch
        {
            return value;
        }
    }

    private static int GetChineseReadabilityScore(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        int score = 0;

        foreach (char ch in value)
        {
            if (ch is >= '\u4e00' and <= '\u9fff')
            {
                score += 3;
            }
            else if (char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch) || ReadablePunctuation.Contains(ch))
            {
                score += 1;
            }

            if (MojibakeHints.IndexOf(ch) >= 0)
            {
                score -= 4;
            }
        }

        return score;
    }
}
