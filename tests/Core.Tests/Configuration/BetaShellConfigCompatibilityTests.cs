using System.Text.Json;
using System.Text.Json.Nodes;
using ModFolderCopier.WinUI;

namespace IntegratedModManager.Core.Tests;

// The samples hold every field the named release wrote. A renamed field or a changed type fails here
// instead of silently dropping a user's setting.
public sealed class BetaShellConfigCompatibilityTests
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private static string Sample(string version) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Configuration", "Samples", $"beta-shell-{version}.json"));

    [Theory]
    [InlineData("v4.5.2")]
    [InlineData("v4.6.1")]
    public void Sample_EveryFieldIsStillRecognised(string version)
    {
        BetaShellConfig config = JsonSerializer.Deserialize<BetaShellConfig>(Sample(version))!;

        Assert.Null(config.ExtensionData);
        Assert.Null(config.OnlinePreferences!.ExtensionData);
        Assert.All(config.Repositories, repository => Assert.Null(repository.ExtensionData));
        Assert.All(config.ConfigurationProfiles, profile => Assert.Null(profile.ExtensionData));
        Assert.All(config.ModPersistentSlots, slot => Assert.Null(slot.ExtensionData));
    }

    [Theory]
    [InlineData("v4.5.2")]
    [InlineData("v4.6.1")]
    public void Sample_WritesBackEveryStoredValue(string version)
    {
        string original = Sample(version);

        string saved = JsonSerializer.Serialize(JsonSerializer.Deserialize<BetaShellConfig>(original), Indented);

        AssertContains(JsonNode.Parse(original)!, JsonNode.Parse(saved)!, "$");
    }

    [Fact]
    public void Sample_v452_ReadsItsSettingsAndLeavesLaterOnesUnset()
    {
        BetaShellConfig config = JsonSerializer.Deserialize<BetaShellConfig>(Sample("v4.5.2"))!;

        Assert.Equal(40, config.ShortcutOverlayOpacity);
        Assert.Equal("repo-1", Assert.Single(config.Repositories).Id);
        Assert.Equal("Evening", Assert.Single(config.ConfigurationProfiles).Name);
        Assert.Equal("1", Assert.Single(Assert.Single(config.ModPersistentSlots).Values).Value);
        Assert.False(config.ModPersistentSlots[0].IsModDefault);
        Assert.Null(config.ThemePreference);
        Assert.Null(config.DefaultSlotSeeds);
        Assert.Null(config.ShortcutOverlayElementOpacity);
    }

    [Fact]
    public void Sample_v461_ReadsItsLaterSettings()
    {
        BetaShellConfig config = JsonSerializer.Deserialize<BetaShellConfig>(Sample("v4.6.1"))!;

        Assert.Equal("dark", config.ThemePreference);
        Assert.True(config.NavigationCollapsed);
        Assert.Equal("Alpha\\Skin", Assert.Single(config.DefaultSlotSeeds!["repo-1"]));
        Assert.True(Assert.Single(config.ModPersistentSlots).IsModDefault);
        Assert.Null(config.ShortcutOverlayElementOpacity);
    }

    [Fact]
    public void UnknownFields_SurviveLoadAndSaveAtEveryLevel()
    {
        JsonObject root = JsonNode.Parse(Sample("v4.6.1"))!.AsObject();
        JsonObject future = new() { ["Mode"] = "x", ["Sizes"] = new JsonArray(1, 2) };
        root["FutureSetting"] = future.DeepClone();
        root["OnlinePreferences"]!["FutureSetting"] = future.DeepClone();
        root["Repositories"]![0]!["FutureSetting"] = future.DeepClone();
        root["ConfigurationProfiles"]![0]!["FutureSetting"] = "profile";
        root["ModPersistentSlots"]![0]!["FutureSetting"] = "slot";

        string saved = JsonSerializer.Serialize(JsonSerializer.Deserialize<BetaShellConfig>(root.ToJsonString()), Indented);

        AssertContains(root, JsonNode.Parse(saved)!, "$");
    }

    [Fact]
    public void UnknownFields_CarriedOverToANewConfigObjectAreWritten()
    {
        BetaShellConfig loaded = JsonSerializer.Deserialize<BetaShellConfig>("""{ "FutureSetting": 5 }""")!;

        // SaveShellConfig builds a new object each time and passes the loaded fields along.
        string saved = JsonSerializer.Serialize(new BetaShellConfig { ExtensionData = loaded.ExtensionData });

        Assert.Equal(5, JsonNode.Parse(saved)!["FutureSetting"]!.GetValue<int>());
        Assert.Null(JsonNode.Parse(saved)!["ExtensionData"]);
    }

    [Fact]
    public void Migration_WritesOutTheFormerGameForEmptyFieldsOnly()
    {
        var config = new BetaShellConfig
        {
            Repositories =
            [
                new WorkspaceRepository { Id = "empty" },
                new WorkspaceRepository { Id = "named", OnlineGameName = "Genshin Impact", OnlineCategoryId = "18140" },
                new WorkspaceRepository { Id = "partial", OnlineGameName = "Custom" }
            ]
        };

        Assert.True(RepositoryGameMigration.Apply(config, "Former", "1"));

        Assert.Equal(("Former", "1"), (config.Repositories[0].OnlineGameName, config.Repositories[0].OnlineCategoryId));
        Assert.Equal(("Genshin Impact", "18140"), (config.Repositories[1].OnlineGameName, config.Repositories[1].OnlineCategoryId));
        Assert.Equal(("Custom", "1"), (config.Repositories[2].OnlineGameName, config.Repositories[2].OnlineCategoryId));
    }

    [Fact]
    public void Migration_RunsOnceSoALaterEmptyRepositoryStaysWithoutAGame()
    {
        BetaShellConfig config = JsonSerializer.Deserialize<BetaShellConfig>(Sample("v4.6.1"))!;
        Assert.False(config.RepositoryGamesExplicit);
        Assert.True(RepositoryGameMigration.Apply(config, "Former", "1"));
        config.Repositories.Add(new WorkspaceRepository { Id = "new" });

        BetaShellConfig saved = JsonSerializer.Deserialize<BetaShellConfig>(JsonSerializer.Serialize(config))!;

        Assert.False(RepositoryGameMigration.Apply(saved, "Former", "1"));
        Assert.Equal(string.Empty, saved.Repositories[^1].OnlineGameName);
    }

    private static void AssertContains(JsonNode expected, JsonNode? actual, string path)
    {
        Assert.True(actual is not null, $"{path} was dropped.");
        switch (expected)
        {
            case JsonObject map:
                foreach ((string key, JsonNode? value) in map)
                {
                    if (value is not null) AssertContains(value, actual![key], $"{path}.{key}");
                }
                break;
            case JsonArray list:
                JsonArray saved = actual!.AsArray();
                Assert.True(list.Count == saved.Count, $"{path} has {saved.Count} items instead of {list.Count}.");
                for (int i = 0; i < list.Count; i++) AssertContains(list[i]!, saved[i], $"{path}[{i}]");
                break;
            default:
                Assert.True(JsonNode.DeepEquals(expected, actual), $"{path} changed from {expected.ToJsonString()} to {actual!.ToJsonString()}.");
                break;
        }
    }
}
