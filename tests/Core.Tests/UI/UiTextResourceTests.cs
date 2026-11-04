using IntegratedModManager.Core.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace IntegratedModManager.Core.Tests;

public sealed class UiTextResourceTests
{
    private static readonly string WindowDirectory = Path.Combine(FindRepositoryRoot(), "src", "WinUI3");
    private static readonly IReadOnlyDictionary<string, string> Chinese = ReadLanguage("zh-CN");
    private static readonly IReadOnlyDictionary<string, string> English = ReadLanguage("en-US");

    [Fact]
    public void LanguageFiles_HaveTheSameKeys()
    {
        Assert.Empty(Chinese.Keys.Except(English.Keys));
        Assert.Empty(English.Keys.Except(Chinese.Keys));
        Assert.All(English, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Value), entry.Key));
        Assert.All(Chinese, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Value), entry.Key));
    }

    // Languages other than Chinese and English may leave keys out (they fall back to English) but
    // must not add keys or placeholders that English does not have.
    [Fact]
    public void OtherLanguageFiles_OnlyTranslateEnglishKeys()
    {
        var problems = new List<string>();
        foreach (string file in Directory.EnumerateFiles(Path.Combine(WindowDirectory, "Strings"), "*.json"))
        {
            string language = Path.GetFileNameWithoutExtension(file);
            if (language is "zh-CN" or "en-US") continue;
            IReadOnlyDictionary<string, string> texts = ReadLanguage(language);
            if (!texts.ContainsKey("Language.Name")) problems.Add($"{language}: no Language.Name");
            foreach ((string key, string text) in texts)
            {
                if (!English.TryGetValue(key, out string? english)) { problems.Add($"{language}: unknown key {key}"); continue; }
                if (string.IsNullOrWhiteSpace(text)) problems.Add($"{language}: empty {key}");
                else if (!LocalizedTextTable.PlaceholderIndexes(text).IsSubsetOf(LocalizedTextTable.PlaceholderIndexes(english)))
                    problems.Add($"{language}: {key} uses placeholders English does not");
            }
        }

        Assert.Empty(problems);
    }

    [Fact]
    public void EveryKeyUsedInTheWindowExistsAndGetsAllItsValues()
    {
        // Read by UiText itself rather than through T(...).
        var used = new HashSet<string>(StringComparer.Ordinal) { "Language.Name" };
        var problems = new List<string>();
        foreach (string file in Directory.EnumerateFiles(WindowDirectory, "*.cs", SearchOption.AllDirectories)
                     .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                         && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
        {
            SyntaxNode root = CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot();
            foreach (InvocationExpressionSyntax call in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (KeyArgument(call) is not int keyIndex) continue;
                foreach (string key in KeyLiterals(call.ArgumentList.Arguments[keyIndex].Expression))
                {
                    used.Add(key);
                    if (!Chinese.ContainsKey(key) || !English.ContainsKey(key))
                    {
                        problems.Add($"{Path.GetFileName(file)}: missing key {key}");
                        continue;
                    }

                    // UiText.Get, UiText.Chinese and UiText.English return the text without filling it in.
                    if (call.Expression is MemberAccessExpressionSyntax { Name.Identifier.Text: "Get" or "Chinese" or "English" }) continue;
                    int values = call.ArgumentList.Arguments.Count - keyIndex - 1;
                    int[] indexes = LocalizedTextTable.PlaceholderIndexes(Chinese[key])
                        .Union(LocalizedTextTable.PlaceholderIndexes(English[key])).ToArray();
                    if (indexes.Any(index => index >= values) || Enumerable.Range(0, values).Any(index => !indexes.Contains(index)))
                    {
                        problems.Add($"{Path.GetFileName(file)}: {key} has placeholders [{string.Join(",", indexes)}] but gets {values} values");
                    }
                }
            }
        }

        Assert.Empty(problems);
        Assert.Empty(English.Keys.Except(used));
    }

    [Fact]
    public void Parse_RejectsFilesThatAreNotFlatTextTables()
    {
        Assert.Throws<InvalidDataException>(() => LocalizedTextTable.Parse("[]"));
        Assert.Throws<InvalidDataException>(() => LocalizedTextTable.Parse("""{ "A": 1 }"""));
        Assert.Throws<InvalidDataException>(() => LocalizedTextTable.Parse("""{ "A": "x", "A": "y" }"""));
        Assert.Equal("x", LocalizedTextTable.Parse("""{ "A": "x" }""")["A"]);
    }

    [Theory]
    [InlineData("plain", new int[0])]
    [InlineData("{0} of {1}", new[] { 0, 1 })]
    [InlineData("{1,5:F1} {0:yyyy}", new[] { 0, 1 })]
    [InlineData("{{0}} literal", new int[0])]
    [InlineData("{{{0}}}", new[] { 0 })]
    public void PlaceholderIndexes_IgnoresDoubledBraces(string text, int[] expected)
    {
        Assert.Equal(expected, LocalizedTextTable.PlaceholderIndexes(text));
    }

    // Where the key is in a call that reads text: T(key, ...) and M(key, ...) in the window,
    // T(language, key, ...) in helpers outside it, and UiText.Get/Format(language, key, ...).
    private static int? KeyArgument(InvocationExpressionSyntax call)
    {
        var arguments = call.ArgumentList.Arguments;
        bool IsKey(int index) => index < arguments.Count && KeyLiterals(arguments[index].Expression).Any();
        return call.Expression switch
        {
            IdentifierNameSyntax { Identifier.Text: "T" or "M" } when IsKey(0) => 0,
            IdentifierNameSyntax { Identifier.Text: "T" } when IsKey(1) => 1,
            MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.Text: "UiText" }, Name.Identifier.Text: "Get" or "Format" } when IsKey(1) => 1,
            MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.Text: "UiText" }, Name.Identifier.Text: "Chinese" or "English" } when IsKey(0) => 0,
            _ => null
        };
    }

    // A literal key, or each key of a condition that picks one, such as M(ok ? "A.Done" : "A.Failed").
    private static IEnumerable<string> KeyLiterals(ExpressionSyntax expression) => expression switch
    {
        LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression) && literal.Token.ValueText.Contains('.')
            => [literal.Token.ValueText],
        ConditionalExpressionSyntax conditional when KeyLiterals(conditional.WhenTrue).Any() && KeyLiterals(conditional.WhenFalse).Any()
            => KeyLiterals(conditional.WhenTrue).Concat(KeyLiterals(conditional.WhenFalse)),
        ParenthesizedExpressionSyntax parenthesized => KeyLiterals(parenthesized.Expression),
        _ => []
    };

    private static IReadOnlyDictionary<string, string> ReadLanguage(string language)
        => LocalizedTextTable.Parse(File.ReadAllText(Path.Combine(WindowDirectory, "Strings", language + ".json")));

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IntegratedModManager.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("IntegratedModManager.sln was not found above the test output.");
    }
}
