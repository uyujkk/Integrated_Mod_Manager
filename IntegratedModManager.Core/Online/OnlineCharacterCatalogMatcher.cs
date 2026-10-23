using System.Text.RegularExpressions;

namespace IntegratedModManager.Core.Online;

// Matching and merging of character catalogs: the wiki list (names, order) and the
// GameBanana character categories (category ids, avatars), plus name lookups shared by
// the online page and repository character folders.
public static class OnlineCharacterCatalogMatcher
{
    public static string NormalizeLookup(string value)
    {
        return Regex.Replace(value ?? string.Empty, @"[^\p{L}\p{N}]", string.Empty)
            .ToLowerInvariant();
    }

    public static bool ContainsAlias(string? text, string alias)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(alias))
        {
            return false;
        }

        if (alias.Any(character => character > 127))
        {
            return text.Contains(alias, StringComparison.OrdinalIgnoreCase);
        }

        string pattern = $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(alias).Replace("\\ ", @"\s*")}(?![\p{{L}}\p{{N}}])";
        return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    public static List<string> ParseWikiAliases(string title, string aliasName)
    {
        return new[] { title, aliasName }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(value => Regex.Split(value, @"[,，、;/；|（）()]+"))
            .Select(value => value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string MappingKey(OnlineCharacterOption character)
    {
        return !string.IsNullOrWhiteSpace(character.WikiUrl)
            ? character.WikiUrl.Trim().ToLowerInvariant()
            : NormalizeLookup(character.DisplayNameZh);
    }

    public static double NameSimilarity(string left, string right)
    {
        string normalizedLeft = NormalizeLookup(left);
        string normalizedRight = NormalizeLookup(right);
        if (string.IsNullOrWhiteSpace(normalizedLeft) || string.IsNullOrWhiteSpace(normalizedRight))
        {
            return 0;
        }

        if (string.Equals(normalizedLeft, normalizedRight, StringComparison.Ordinal))
        {
            return 1;
        }

        if (normalizedLeft.Contains(normalizedRight, StringComparison.Ordinal)
            || normalizedRight.Contains(normalizedLeft, StringComparison.Ordinal))
        {
            double containmentRatio = (double)Math.Min(normalizedLeft.Length, normalizedRight.Length)
                / Math.Max(normalizedLeft.Length, normalizedRight.Length);
            return 0.78 + containmentRatio * 0.2;
        }

        int[] previous = Enumerable.Range(0, normalizedRight.Length + 1).ToArray();
        int[] current = new int[normalizedRight.Length + 1];
        for (int leftIndex = 1; leftIndex <= normalizedLeft.Length; leftIndex++)
        {
            current[0] = leftIndex;
            for (int rightIndex = 1; rightIndex <= normalizedRight.Length; rightIndex++)
            {
                int substitutionCost = normalizedLeft[leftIndex - 1] == normalizedRight[rightIndex - 1] ? 0 : 1;
                current[rightIndex] = Math.Min(
                    Math.Min(current[rightIndex - 1] + 1, previous[rightIndex] + 1),
                    previous[rightIndex - 1] + substitutionCost);
            }

            (previous, current) = (current, previous);
        }

        return 1d - (double)previous[normalizedRight.Length] / Math.Max(normalizedLeft.Length, normalizedRight.Length);
    }

    public static double MatchScore(
        OnlineCharacterOption wikiCharacter,
        OnlineCharacterOption gameBananaCharacter)
    {
        IEnumerable<string> wikiNames = new[]
            {
                wikiCharacter.DisplayNameZh,
                wikiCharacter.DisplayNameEn,
                wikiCharacter.Key
            }
            .Concat(wikiCharacter.Aliases);
        IEnumerable<string> gameBananaNames = new[]
            {
                gameBananaCharacter.DisplayNameZh,
                gameBananaCharacter.DisplayNameEn,
                gameBananaCharacter.Key
            }
            .Concat(gameBananaCharacter.Aliases);

        double bestScore = 0;
        foreach (string wikiName in wikiNames.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (string gameBananaName in gameBananaNames.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                bestScore = Math.Max(bestScore, NameSimilarity(wikiName, gameBananaName));
            }
        }

        return bestScore;
    }

    public static List<OnlineCharacterOption> Merge(
        List<OnlineCharacterOption> wikiCharacters,
        List<OnlineCharacterOption> gameBananaCharacters,
        IReadOnlyDictionary<string, string> manualMappings)
    {
        if (wikiCharacters.Count == 0)
        {
            return gameBananaCharacters;
        }

        var usedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (OnlineCharacterOption wikiCharacter in wikiCharacters)
        {
            string mappingKey = MappingKey(wikiCharacter);
            if (manualMappings.TryGetValue(mappingKey, out string? manualCategoryId)
                && !string.IsNullOrWhiteSpace(manualCategoryId))
            {
                OnlineCharacterOption? manualMatch = gameBananaCharacters.FirstOrDefault(candidate =>
                    string.Equals(candidate.CategoryId, manualCategoryId, StringComparison.OrdinalIgnoreCase));
                ApplyCategoryMatch(wikiCharacter, manualMatch, manualCategoryId);
                if (manualMatch is not null)
                {
                    usedKeys.Add(manualMatch.Key);
                }
                continue;
            }

            OnlineCharacterOption? bestMatch = null;
            double bestScore = 0;
            double secondBestScore = 0;
            foreach (OnlineCharacterOption candidate in gameBananaCharacters)
            {
                if (usedKeys.Contains(candidate.Key))
                {
                    continue;
                }

                double score = MatchScore(wikiCharacter, candidate);
                if (score > bestScore)
                {
                    secondBestScore = bestScore;
                    bestScore = score;
                    bestMatch = candidate;
                }
                else if (score > secondBestScore)
                {
                    secondBestScore = score;
                }
            }

            bool isConfidentMatch = bestScore >= 0.74
                && (bestScore >= 0.94 || bestScore - secondBestScore >= 0.08);
            if (bestMatch is null || !isConfidentMatch)
            {
                continue;
            }

            usedKeys.Add(bestMatch.Key);
            ApplyCategoryMatch(wikiCharacter, bestMatch, bestMatch.CategoryId);
        }

        int fallbackOrder = wikiCharacters.Count;
        foreach (OnlineCharacterOption character in gameBananaCharacters)
        {
            if (usedKeys.Add(character.Key))
            {
                character.WikiOrder = fallbackOrder++;
                wikiCharacters.Add(character);
            }
        }

        return wikiCharacters;
    }

    private static void ApplyCategoryMatch(
        OnlineCharacterOption wikiCharacter,
        OnlineCharacterOption? gameBananaCharacter,
        string categoryId)
    {
        wikiCharacter.CategoryId = categoryId.Trim();
        if (gameBananaCharacter is null)
        {
            return;
        }

        foreach (string alias in new[]
                 {
                     gameBananaCharacter.Key,
                     gameBananaCharacter.DisplayNameZh,
                     gameBananaCharacter.DisplayNameEn
                 }.Concat(gameBananaCharacter.Aliases))
        {
            if (!string.IsNullOrWhiteSpace(alias)
                && !wikiCharacter.Aliases.Contains(alias, StringComparer.OrdinalIgnoreCase))
            {
                wikiCharacter.Aliases.Add(alias);
            }
        }

        wikiCharacter.LatestModUpdatedAt = gameBananaCharacter.LatestModUpdatedAt;
    }

    public static void ApplyManualMappings(
        IReadOnlyDictionary<string, string> mappings,
        IEnumerable<OnlineCharacterOption> characters)
    {
        foreach (OnlineCharacterOption character in characters.Where(character => character.IsWikiCharacter))
        {
            if (mappings.TryGetValue(MappingKey(character), out string? categoryId)
                && !string.IsNullOrWhiteSpace(categoryId))
            {
                character.CategoryId = categoryId.Trim();
            }
        }
    }

    public static void SortByWikiOrderThenNewest(List<OnlineCharacterOption> characters)
    {
        characters.Sort((left, right) =>
        {
            int wikiOrderComparison = left.WikiOrder.CompareTo(right.WikiOrder);
            if (wikiOrderComparison != 0)
            {
                return wikiOrderComparison;
            }

            int dateComparison = right.LatestModUpdatedAt.CompareTo(left.LatestModUpdatedAt);
            if (dateComparison != 0)
            {
                return dateComparison;
            }

            return StringComparer.CurrentCultureIgnoreCase.Compare(left.DisplayNameEn, right.DisplayNameEn);
        });
    }

    // The catalog plus the fallback characters it lacks (for example the built-in list when the online
    // catalog is missing an entry). A fallback character matches a catalog one when any of their
    // key, names or aliases are equal after normalization.
    public static List<OnlineCharacterOption> WithFallback(
        IReadOnlyList<OnlineCharacterOption> catalog, IReadOnlyList<OnlineCharacterOption> fallback)
    {
        static IEnumerable<string> Names(OnlineCharacterOption character) => new[]
            {
                character.Key, character.DisplayNameZh, character.DisplayNameEn
            }
            .Concat(character.Aliases)
            .Select(NormalizeLookup)
            .Where(name => name.Length > 0);

        var known = new HashSet<string>(catalog.SelectMany(Names), StringComparer.Ordinal);
        var result = new List<OnlineCharacterOption>(catalog);
        foreach (OnlineCharacterOption character in fallback)
        {
            if (!Names(character).Any(known.Contains))
            {
                result.Add(character);
            }
        }

        return result;
    }
}
