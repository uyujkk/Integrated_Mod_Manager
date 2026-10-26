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

    [Fact]
    public void EveryKeyUsedInTheWindowExistsAndGetsAllItsValues()
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        var problems = new List<string>();
        foreach (string file in Directory.EnumerateFiles(WindowDirectory, "*.cs", SearchOption.AllDirectories)
                     .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                         && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
        {
            SyntaxNode root = CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot();
            foreach (InvocationExpressionSyntax call in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (call.Expression is not IdentifierNameSyntax { Identifier.Text: "T" }
                    || call.ArgumentList.Arguments.FirstOrDefault()?.Expression is not LiteralExpressionSyntax literal
                    || !literal.IsKind(SyntaxKind.StringLiteralExpression))
                {
                    continue;
                }

                string key = literal.Token.ValueText;
                used.Add(key);
                int values = call.ArgumentList.Arguments.Count - 1;
                if (!Chinese.ContainsKey(key) || !English.ContainsKey(key))
                {
                    problems.Add($"{Path.GetFileName(file)}: missing key {key}");
                    continue;
                }

                int[] indexes = LocalizedTextTable.PlaceholderIndexes(Chinese[key])
                    .Union(LocalizedTextTable.PlaceholderIndexes(English[key])).ToArray();
                if (indexes.Any(index => index >= values) || Enumerable.Range(0, values).Any(index => !indexes.Contains(index)))
                {
                    problems.Add($"{Path.GetFileName(file)}: {key} has placeholders [{string.Join(",", indexes)}] but gets {values} values");
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
