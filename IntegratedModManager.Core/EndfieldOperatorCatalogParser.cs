using System.Net;
using System.Text.RegularExpressions;

namespace IntegratedModManager.Core;

public sealed record EndfieldOperatorCatalogEntry(
    string Slug,
    string ChineseName,
    string EnglishName,
    string AvatarUrl,
    int Order);

public static class EndfieldOperatorCatalogParser
{
    private static readonly Regex OperatorBlockRegex = new(
        @"<div\s+class=""[^""]*OperatorItem_operatorItem__[^""]*""[^>]*>(?<body>.*?)(?=<div\s+class=""[^""]*OperatorItem_operatorItem__|</(?:main|section)>|\z)",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex IdentityRegex = new(
        @"OperatorItem_image__[^""]*""[^>]*\bdata-key=""(?<slug>[^""]+)""[^>]*\bstyle=""[^""]*background-image\s*:\s*url\((?<avatar>[^)]+)\)",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex ChineseNameRegex = new(
        @"OperatorItem_nameText__[^""]*""[^>]*>(?<name>.*?)</(?:span|div)>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex EnglishNameRegex = new(
        @"OperatorItem_codename__[^""]*""[^>]*>\s*(?://)?\s*(?<name>.*?)</div>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex OrderRegex = new(
        @"OperatorItem_index__[^""]*""[^>]*>\s*(?<order>\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex HtmlTagRegex = new(
        @"<[^>]+>",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex WhitespaceRegex = new(
        @"\s+",
        RegexOptions.Compiled);

    public static IReadOnlyList<EndfieldOperatorCatalogEntry> Parse(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return [];
        }

        var entries = new List<EndfieldOperatorCatalogEntry>();
        var knownEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match blockMatch in OperatorBlockRegex.Matches(html))
        {
            string body = blockMatch.Groups["body"].Value;
            Match identityMatch = IdentityRegex.Match(body);
            Match chineseNameMatch = ChineseNameRegex.Match(body);
            Match englishNameMatch = EnglishNameRegex.Match(body);
            Match orderMatch = OrderRegex.Match(body);
            if (!identityMatch.Success
                || !chineseNameMatch.Success
                || !englishNameMatch.Success
                || !orderMatch.Success
                || !int.TryParse(orderMatch.Groups["order"].Value, out int order)
                || order <= 0)
            {
                continue;
            }

            string slug = DecodeText(identityMatch.Groups["slug"].Value);
            string chineseName = DecodeText(chineseNameMatch.Groups["name"].Value);
            string englishName = DecodeText(englishNameMatch.Groups["name"].Value);
            string avatarUrl = WebUtility.HtmlDecode(identityMatch.Groups["avatar"].Value).Trim(' ', '\'', '"');
            if (string.IsNullOrWhiteSpace(slug)
                || string.IsNullOrWhiteSpace(chineseName)
                || string.IsNullOrWhiteSpace(englishName)
                || !knownEntries.Add($"{slug}|{order}"))
            {
                continue;
            }

            entries.Add(new EndfieldOperatorCatalogEntry(
                slug,
                chineseName,
                englishName,
                avatarUrl,
                order));
        }

        return entries
            .OrderBy(entry => entry.Order)
            .ToList();
    }

    private static string DecodeText(string value)
    {
        string withoutTags = HtmlTagRegex.Replace(value, " ");
        return WhitespaceRegex.Replace(WebUtility.HtmlDecode(withoutTags), " ").Trim();
    }
}
