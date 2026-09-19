using IntegratedModManager.Core;

namespace IntegratedModManager.Core.Tests;

public sealed class ThreeDmigotoPersistentStateTests
{
    [Fact]
    public void ParseUserConfig_ReadsOnlyNumericConstantAssignments()
    {
        const string content = """
            ; generated file
            [Constants]
            $\Mods\A\A.ini\dress = 2
            $\Mods\A\A.ini\coat = -1
            $bad = run something

            [Present]
            $\Mods\A\A.ini\ignored = 7
            """;

        IReadOnlyDictionary<string, string> result = ThreeDmigotoPersistentState.ParseUserConfig(content);

        Assert.Equal(2, result.Count);
        Assert.Equal("2", result["$\\mods\\a\\a.ini\\dress"]);
        Assert.Equal("-1", result["$\\mods\\a\\a.ini\\coat"]);
    }

    [Fact]
    public void ParsePersistentVariableNames_UsesDefaultAndExplicitNamespaces()
    {
        const string defaultIni = """
            [Constants]
            global persist $dress = 2
            global $runtime = 1
            global persist $coat=-1
            """;
        const string explicitIni = """
            namespace = Shared\Outfit
            [Constants]
            GLOBAL PERSIST $color = 4
            """;

        IReadOnlySet<string> defaultNames = ThreeDmigotoPersistentState.ParsePersistentVariableNames(
            defaultIni,
            "Mods\\Character A\\Outfit.ini");
        IReadOnlySet<string> explicitNames = ThreeDmigotoPersistentState.ParsePersistentVariableNames(
            explicitIni,
            "Mods\\Ignored.ini");

        Assert.Equal(2, defaultNames.Count);
        Assert.Contains("$\\mods\\character a\\outfit.ini\\dress", defaultNames);
        Assert.Contains("$\\mods\\character a\\outfit.ini\\coat", defaultNames);
        Assert.Contains("$\\shared\\outfit\\color", explicitNames);
    }

    [Fact]
    public void CaptureModState_IntersectsDeclaredPersistentVariablesWithUserConfig()
    {
        string root = Path.Combine(Path.GetTempPath(), "IntegratedModManagerTests", Guid.NewGuid().ToString("N"));
        string mod = Path.Combine(root, "Character A");
        try
        {
            Directory.CreateDirectory(mod);
            File.WriteAllText(Path.Combine(mod, "Outfit.ini"), """
                [Constants]
                global persist $dress = 0
                global persist $coat = 1
                global $runtime = 0
                """);
            const string userConfig = """
                [Constants]
                $\mods\character a\outfit.ini\dress = 3
                $\mods\character a\outfit.ini\coat = -1
                $\mods\character a\outfit.ini\runtime = 9
                $\mods\other\other.ini\value = 7
                """;

            IReadOnlyList<PersistentVariableValue> result = ThreeDmigotoPersistentState.CaptureModState(root, mod, userConfig);

            Assert.Equal(2, result.Count);
            Assert.Equal("$\\mods\\character a\\outfit.ini\\coat", result[0].Name);
            Assert.Equal("-1", result[0].Value);
            Assert.Equal("$\\mods\\character a\\outfit.ini\\dress", result[1].Name);
            Assert.Equal("3", result[1].Value);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void BuildBridge_UsesPostAssignmentsAndRejectsCommands()
    {
        string bridge = ThreeDmigotoPersistentState.BuildBridge(
        [
            new PersistentVariableValue("$\\Mods\\A\\A.ini\\dress", "2"),
            new PersistentVariableValue("$\\Mods\\A\\A.ini\\coat", "-1")
        ]);

        Assert.StartsWith(ThreeDmigotoPersistentState.BridgeMarker, bridge, StringComparison.Ordinal);
        Assert.Contains("[Constants]", bridge, StringComparison.Ordinal);
        Assert.Contains("post $\\mods\\a\\a.ini\\dress = 2", bridge, StringComparison.Ordinal);
        Assert.True(ThreeDmigotoPersistentState.IsGeneratedBridge(bridge));
        Assert.Throws<InvalidDataException>(() => ThreeDmigotoPersistentState.BuildBridge(
            [new PersistentVariableValue("$\\mods\\a\\value", "run CommandListBad")]));
    }

    [Fact]
    public void BuildBridge_RejectsMalformedVariableNames()
    {
        Assert.Throws<InvalidDataException>(() => ThreeDmigotoPersistentState.BuildBridge(
            [new PersistentVariableValue("$\\mods\\a.ini\\x\n[KeyBad]", "1")]));
    }

    [Fact]
    public void MergeUserConfig_UpdatesOnlyTargetValuesAndPreservesOtherSections()
    {
        const string content = "; header\r\n[Constants]\r\n$\\efmiv1\\first_run = 0\r\n$\\mods\\a.ini\\dress = 1\r\n\r\n[Present]\r\nrun = CommandListKeep\r\n";

        string merged = ThreeDmigotoPersistentState.MergeUserConfig(content,
        [
            new PersistentVariableValue("$\\mods\\a.ini\\dress", "3"),
            new PersistentVariableValue("$\\mods\\a.ini\\coat", "-1")
        ]);

        Assert.Contains("$\\efmiv1\\first_run = 0", merged, StringComparison.Ordinal);
        Assert.Contains("$\\mods\\a.ini\\dress = 3", merged, StringComparison.Ordinal);
        Assert.Contains("$\\mods\\a.ini\\coat = -1", merged, StringComparison.Ordinal);
        Assert.Contains("[Present]\r\nrun = CommandListKeep", merged, StringComparison.Ordinal);
        Assert.EndsWith("\r\n", merged, StringComparison.Ordinal);
    }

    [Fact]
    public void MergeUserConfig_CreatesConstantsSectionWhenMissing()
    {
        string merged = ThreeDmigotoPersistentState.MergeUserConfig("; header", [new PersistentVariableValue("$\\mods\\a.ini\\dress", "2")]);

        Assert.Equal("; header\n\n[Constants]\n$\\mods\\a.ini\\dress = 2\n", merged);
    }

    [Fact]
    public void MergeUserConfig_UpdatesEveryDuplicateSoLastAssignmentCannotRestoreOldValue()
    {
        const string content = "[Constants]\n$\\mods\\a.ini\\dress = 1\n$\\mods\\a.ini\\dress = 9";

        string merged = ThreeDmigotoPersistentState.MergeUserConfig(content, [new PersistentVariableValue("$\\mods\\a.ini\\dress", "4")]);
        IReadOnlyDictionary<string, string> parsed = ThreeDmigotoPersistentState.ParseUserConfig(merged);

        Assert.Equal(2, merged.Split("dress = 4", StringSplitOptions.None).Length - 1);
        Assert.Equal("4", parsed["$\\mods\\a.ini\\dress"]);
        Assert.False(merged.EndsWith('\n'));
    }
}
