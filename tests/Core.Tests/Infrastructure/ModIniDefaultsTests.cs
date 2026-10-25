using System.Text;
using IntegratedModManager.Core.Presets;

namespace IntegratedModManager.Core.Tests;

public sealed class ModIniDefaultsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "imm-ini-defaults-" + Guid.NewGuid().ToString("N"));
    private string Mod => Path.Combine(_root, "Mod");
    private string Ini => Path.Combine(Mod, "a.ini");

    public ModIniDefaultsTests() => Directory.CreateDirectory(Path.Combine(Mod, "sub"));

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Declarations_ListsPathAndNamespaceKeysWithoutAUserIni()
    {
        File.WriteAllText(Ini, "[Constants]\nglobal persist $x = 0\nglobal $temp = 1\n");
        File.WriteAllText(Path.Combine(Mod, "sub", "b.ini"), "namespace = Shared\\Outfit\n[Constants]\nglobal persist $y\n");

        IReadOnlyList<ModPersistentDeclaration> declarations = ModPersistentPresetEngine.Declarations(Mod, "Mod");

        Assert.Equal(
            [new ModPersistentDeclaration("a.ini", "$x", "$\\mods\\mod\\a.ini\\x"), new ModPersistentDeclaration("sub\\b.ini", "$y", "$\\shared\\outfit\\y")],
            declarations.OrderBy(declaration => declaration.RelativeIniPath));
    }

    [Fact]
    public void PlanAndApply_ChangeOnlyTheDefaultNumbersAndKeepBackups()
    {
        const string original = "; header\r\n[Constants]\r\n  global persist $x = 0\r\nglobal persist $y\r\nglobal persist $z = 7\r\n[Present]\r\nrun = x\r\n";
        File.WriteAllText(Ini, original);
        ModPersistentValue[] values = [Value("$x", "3"), Value("$y", "-1.5"), Value("$z", "7")];

        ModIniDefaultsPlan plan = ModPersistentPresetEngine.PlanModIniDefaults(Mod, "Mod", values);
        Assert.Equal((2, 1, 1), (plan.ChangedCount, plan.UnchangedCount, plan.Edits.Count));
        Assert.Equal(original, File.ReadAllText(Ini));

        string backup = Assert.Single(ModPersistentPresetEngine.ApplyModIniDefaults(plan));

        Assert.Equal("; header\r\n[Constants]\r\n  global persist $x = 3\r\nglobal persist $y = -1.5\r\nglobal persist $z = 7\r\n[Present]\r\nrun = x\r\n", File.ReadAllText(Ini));
        Assert.Equal(original, File.ReadAllText(backup));
        Assert.StartsWith(Ini + ".imm-persist-", backup);
        Assert.EndsWith(".bak", backup);
        Assert.Equal(0, ModPersistentPresetEngine.PlanModIniDefaults(Mod, "Mod", values).ChangedCount);
        Assert.Empty(ModPersistentPresetEngine.ApplyModIniDefaults(ModPersistentPresetEngine.PlanModIniDefaults(Mod, "Mod", values)));
    }

    [Fact]
    public void Apply_KeepsTheByteOrderMark()
    {
        File.WriteAllBytes(Ini, [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("[Constants]\nglobal persist $x = 0\n")]);

        ModPersistentPresetEngine.ApplyModIniDefaults(ModPersistentPresetEngine.PlanModIniDefaults(Mod, "Mod", [Value("$x", "2")]));

        Assert.Equal([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("[Constants]\nglobal persist $x = 2\n")], File.ReadAllBytes(Ini));
    }

    [Fact]
    public void Apply_RefusesAStalePlanAndRollbackRestoresTheOriginal()
    {
        File.WriteAllText(Ini, "[Constants]\nglobal persist $x = 0\n");
        ModIniDefaultsPlan plan = ModPersistentPresetEngine.PlanModIniDefaults(Mod, "Mod", [Value("$x", "5")]);
        File.WriteAllText(Ini, "[Constants]\nglobal persist $x = 1\n");
        Assert.Throws<IOException>(() => ModPersistentPresetEngine.ApplyModIniDefaults(plan));
        Assert.Empty(Directory.GetFiles(Mod, "*.bak"));

        File.WriteAllText(Ini, "[Constants]\nglobal persist $x = 0\n");
        ModIniDefaultsPlan fresh = ModPersistentPresetEngine.PlanModIniDefaults(Mod, "Mod", [Value("$x", "5")]);
        IReadOnlyList<string> backups = ModPersistentPresetEngine.ApplyModIniDefaults(fresh);
        ModPersistentPresetEngine.RollbackModIniDefaults(fresh, backups);
        Assert.Equal("[Constants]\nglobal persist $x = 0\n", File.ReadAllText(Ini));

        File.WriteAllText(Ini, "[Constants]\nglobal persist $x = 9\n");
        Assert.Throws<IOException>(() => ModPersistentPresetEngine.RollbackModIniDefaults(fresh, backups));
        Assert.Throws<ArgumentException>(() => ModPersistentPresetEngine.RollbackModIniDefaults(fresh, []));
    }

    [Fact]
    public void Plan_RejectsValuesThatNoLongerMatchADeclaration()
    {
        File.WriteAllText(Ini, "[Constants]\nglobal persist $x = 0\n");

        Assert.Throws<InvalidDataException>(() => ModPersistentPresetEngine.PlanModIniDefaults(Mod, "Mod", [Value("$gone", "1")]));
        Assert.Throws<InvalidDataException>(() => ModPersistentPresetEngine.PlanModIniDefaults(Mod, "Mod", [Value("$x", "1; drop")]));
        Assert.Empty(ModPersistentPresetEngine.ApplyModIniDefaults(
            new ModIniDefaultsPlan(Mod, "Mod", [], [], 0, 0)));
    }

    [Fact]
    public void JasmImport_MatchesPathAndNamespaceKeysAndCountsTheRest()
    {
        File.WriteAllText(Ini, "[Constants]\nglobal persist $x = 0\nglobal persist $y = 0\n");
        File.WriteAllText(Path.Combine(Mod, "sub", "b.ini"), "namespace = Shared\\Outfit\n[Constants]\nglobal persist $z\n");
        string json = """
            {
              "ActiveVariantName": "Battle",
              "Variants": [
                { "Name": " Battle ", "Preferences": {
                    "$\\mods\\character\\hutao\\mod\\a.ini\\x": "2",
                    "$\\MODS\\Character\\HuTao\\Mod\\A.INI\\Y": 1.5,
                    "$\\shared\\outfit\\z": "4",
                    "$\\mods\\character\\hutao\\othermod\\a.ini\\x": "9",
                    "$\\mods\\character\\hutao\\mod\\a.ini\\gone": "1",
                    "$\\mods\\character\\hutao\\mod\\a.ini\\x2": "text" } },
                { "Name": "" },
                { "Name": "Empty" }
              ]
            }
            """;

        ImportedVariantSet set = VariantFileImport.Parse(json, "Mod", ModPersistentPresetEngine.Declarations(Mod, "Mod"));

        Assert.Equal("Battle", set.ActiveName);
        Assert.Equal(["Battle", "Empty"], set.Variants.Select(variant => variant.Name));
        ImportedVariant battle = set.Variants[0];
        Assert.Equal(3, battle.UnmatchedCount);
        Assert.Equal(
            [("a.ini", "$x", "2"), ("a.ini", "$y", "1.5"), ("sub\\b.ini", "$z", "4")],
            battle.Values.Select(value => (value.RelativeIniPath, value.Variable, value.Value)));
        Assert.Equal("$\\mods\\mod\\a.ini\\x", battle.Values[0].RuntimeKey);
        Assert.Empty(set.Variants[1].Values);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("""{ "Variants": {} }""")]
    [InlineData("""{ "ActiveVariantName": "x" }""")]
    public void JasmImport_RejectsOtherFiles(string json)
    {
        Assert.Throws<InvalidDataException>(() => VariantFileImport.Parse(json, "Mod", []));
    }

    private static ModPersistentValue Value(string variable, string value)
        => new("a.ini", variable, "$\\mods\\mod\\a.ini\\" + variable[1..].ToLowerInvariant(), value);
}
