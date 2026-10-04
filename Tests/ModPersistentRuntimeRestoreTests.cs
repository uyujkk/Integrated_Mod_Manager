using System.Text;
using IntegratedModManager.Core;

namespace IntegratedModManager.Core.Tests;

public sealed class ModPersistentRuntimeRestoreTests
{
    [Fact]
    public void CaptureOnlyDeclaredNumericPersistentValues_AndNeverWritesSource()
    {
        using var f = new Fixture();
        File.WriteAllText(f.Ini, "[Constants]\nglobal persist $x=0\nglobal $temporary=9\n");
        File.WriteAllText(Path.Combine(f.Mod, "DISABLED-old.ini"), "[Constants]\nglobal persist $old=0\n");
        string user = f.User("[Constants]\n$\\Mods\\Mod\\a.ini\\x = 3\n$\\Mods\\Mod\\a.ini\\temporary = 5\n$\\Mods\\Mod\\DISABLED-old.ini\\old = 2\n$\\Mods\\Other\\a.ini\\x = 99\n");
        byte[] before = File.ReadAllBytes(user);
        ModPersistentValue value = Assert.Single(ModPersistentPresetEngine.Capture(f.Mod, "Mod", user));
        Assert.Equal("$x", value.Variable);
        Assert.Equal("3", value.Value);
        Assert.Equal(before, File.ReadAllBytes(user));
        f.User("[Constants]\n$\\Mods\\Mod\\a.ini\\x = 1e999\n");
        Assert.Empty(ModPersistentPresetEngine.Capture(f.Mod, "Mod", user));
    }

    [Fact]
    public void MultipleSlotsChangeOnlyLoaderState_WithExactBackupsAndNoOpSupport()
    {
        using var f = new Fixture();
        byte[] modBefore = File.ReadAllBytes(f.Ini);
        string a = "; preamble\r\n[Constants]\r\n  $\\MODS\\Mod\\a.ini\\x   =  1  ; note\r\n$\\mods\\other\\a.ini\\x=99\r\n[CommandList]\r\n$\\mods\\mod\\a.ini\\x=100\r\n";
        string user = f.User(a);
        var stateA = ModPersistentPresetEngine.Capture(f.Mod, "Mod", user);
        string b = a.Replace("=  1  ; note", "=  2  ; note");
        f.User(b);
        var stateB = ModPersistentPresetEngine.Capture(f.Mod, "Mod", user);
        var plan = ModPersistentPresetEngine.PlanRuntimeRestore(f.Mod, "Mod", user, stateA);
        Assert.Equal(1, plan.ChangedCount);
        string backup = Assert.IsType<string>(ModPersistentPresetEngine.ApplyRuntimeRestore(plan));
        Assert.Equal(b, File.ReadAllText(backup));
        Assert.Equal(a, File.ReadAllText(user));
        ModPersistentPresetEngine.ApplyRuntimeRestore(ModPersistentPresetEngine.PlanRuntimeRestore(f.Mod, "Mod", user, stateB));
        Assert.Equal(b, File.ReadAllText(user));
        Assert.Equal(modBefore, File.ReadAllBytes(f.Ini));
        Assert.Null(ModPersistentPresetEngine.ApplyRuntimeRestore(ModPersistentPresetEngine.PlanRuntimeRestore(f.Mod, "Mod", user, stateB)));
        Assert.Equal(2, Directory.GetFiles(f.Root, "*.bak").Length);
    }

    [Fact]
    public void StalePreviewIsRejectedBeforeBackupOrWrite()
    {
        using var f = new Fixture();
        string user = f.User("[Constants]\n$\\mods\\mod\\a.ini\\x=0\n");
        var plan = ModPersistentPresetEngine.PlanRuntimeRestore(f.Mod, "Mod", user, [Value("2")]);
        f.User("[Constants]\n$\\mods\\mod\\a.ini\\x=9\n");
        Assert.Throws<IOException>(() => ModPersistentPresetEngine.ApplyRuntimeRestore(plan));
        Assert.Contains("\\x=9", File.ReadAllText(user));
        Assert.Empty(Directory.GetFiles(f.Root, "*.bak"));
    }

    [Theory]
    [InlineData("utf8bom")]
    [InlineData("utf16le")]
    [InlineData("utf16be")]
    public void ExplicitNamespaceEncodingAndBom_ArePreserved(string kind)
    {
        using var f = new Fixture();
        File.WriteAllBytes(f.Ini, [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("namespace = Shared\\Outfit\n[Constants]\nglobal persist $x=0\n")]);
        byte[] modBefore = File.ReadAllBytes(f.Ini);
        Encoding enc = kind switch { "utf16le" => new UnicodeEncoding(false, true), "utf16be" => new UnicodeEncoding(true, true), _ => new UTF8Encoding(true) };
        string user = f.User(string.Empty);
        byte[] before = [.. enc.GetPreamble(), .. enc.GetBytes("[Constants]\r\n$\\Shared\\Outfit\\x = 0\r\n")];
        File.WriteAllBytes(user, before);
        var value = Value("2") with { RuntimeKey = "$\\shared\\outfit\\x" };
        string backup = Assert.IsType<string>(ModPersistentPresetEngine.ApplyRuntimeRestore(ModPersistentPresetEngine.PlanRuntimeRestore(f.Mod, "Moved", user, [value])));
        Assert.Equal(before, File.ReadAllBytes(backup));
        Assert.Equal([.. enc.GetPreamble(), .. enc.GetBytes("[Constants]\r\n$\\Shared\\Outfit\\x = 2\r\n")], File.ReadAllBytes(user));
        Assert.Equal(modBefore, File.ReadAllBytes(f.Ini));
    }

    [Theory]
    [InlineData("a.ini", "$gone", "$\\mods\\mod\\a.ini\\x", "1")]
    [InlineData("a.ini", "$x", "$\\mods\\other\\a.ini\\x", "1")]
    [InlineData("../a.ini", "$x", "$\\mods\\mod\\a.ini\\x", "1")]
    [InlineData("DISABLED-old.ini", "$x", "$\\mods\\mod\\a.ini\\x", "1")]
    [InlineData("a.ini", "$x", "$\\mods\\mod\\a.ini\\x", "run = Evil")]
    [InlineData("a.ini", "$x", "$\\mods\\mod\\a.ini\\x", "1e999")]
    public void UnsafeOrMismatchedSlot_IsRejected(string relative, string variable, string key, string value)
    {
        using var f = new Fixture();
        string user = f.User("[Constants]\n");
        Assert.Throws<InvalidDataException>(() => ModPersistentPresetEngine.PlanRuntimeRestore(f.Mod, "Mod", user,
            [new ModPersistentValue(relative, variable, key, value)]));
        Assert.Equal("[Constants]\n", File.ReadAllText(user));
    }

    [Theory]
    [InlineData("[Constants]\n$\\mods\\mod\\a.ini\\x=0\n$\\MODS\\MOD\\A.INI\\X=1\n")]
    [InlineData("[Constants]\n$\\mods\\mod\\a.ini\\x=run = dangerous\n")]
    [InlineData("[Constants]\n$\\mods\\mod\\a.ini\\x=1e999\n")]
    [InlineData("[Settings]\nx=1\n")]
    [InlineData("[Constants]\n[Settings]\nx=1\n[Constants]\n")]
    public void AmbiguousOrUnsupportedLoaderFile_IsRejected(string text)
    {
        using var f = new Fixture();
        string user = f.User(text);
        Assert.Throws<InvalidDataException>(() => ModPersistentPresetEngine.PlanRuntimeRestore(f.Mod, "Mod", user, [Value()]));
        Assert.Equal(text, File.ReadAllText(user));
    }

    [Theory]
    [InlineData("[Constants]\n; keep\n[Other]\nx = 7", "[Constants]\n; keep\n$\\mods\\mod\\a.ini\\x = 2\n[Other]\nx = 7")]
    [InlineData("[Constants]\n; keep", "[Constants]\n; keep\n$\\mods\\mod\\a.ini\\x = 2")]
    [InlineData("[Constants]\r\n; keep\r\n", "[Constants]\r\n; keep\r\n$\\mods\\mod\\a.ini\\x = 2\r\n")]
    public void MissingValidatedKey_IsAddedOnlyInsideConstants(string text, string expected)
    {
        using var f = new Fixture();
        string user = f.User(text);
        var plan = ModPersistentPresetEngine.PlanRuntimeRestore(f.Mod, "Mod", user, [Value("2")]);
        Assert.Equal(0, plan.ChangedCount);
        Assert.Equal(1, plan.AddedCount);
        ModPersistentPresetEngine.ApplyRuntimeRestore(plan);
        Assert.Equal(expected, File.ReadAllText(user));
    }

    [Fact]
    public void DuplicateRuntimeKeysFromExplicitNamespaces_AreRejected()
    {
        using var f = new Fixture();
        foreach (string name in new[] { "a.ini", "b.ini" })
            File.WriteAllText(Path.Combine(f.Mod, name), "namespace = Shared\n[Constants]\nglobal persist $x=0\n");
        string user = f.User("[Constants]\n$\\Shared\\x=0\n");
        Assert.Throws<InvalidDataException>(() => ModPersistentPresetEngine.PlanRuntimeRestore(f.Mod, "Mod", user,
            [new("a.ini", "$x", "$\\shared\\x", "1"), new("b.ini", "$x", "$\\shared\\x", "2")]));
    }

    [Fact]
    public void FangyiFourteenVariableSlot_ChangesOnlyToggleZeroAndKeepsPrecision()
    {
        using var f = new Fixture();
        string deployed = "Zhuang Fangyi Ink Cheongsam full ver 5.2";
        string nested = Path.Combine(f.Mod, deployed);
        Directory.CreateDirectory(nested);
        string relative = deployed + "\\Zhuang Fangyi.ini";
        string[] variables = ["mx", "my", "swapvar_toggle_0", "swapvar_toggle_1", "swapvar_toggle_2", "swapvar_toggle_3", "swapvar_toggle_4", "swapvar_toggle_5", "swapvar_toggle_6", "swapvar_toggle_7", "swapvar_toggle_neiku", "swapvar_toggle_neiyi", "swapvar_toggle_peishi", "swapvar_toggle_shoe"];
        string[] values = ["0.519999981", "0.100000001", "-1", "-1", "0", "1", "-1", "0", "0", "-1", "1", "0", "1", "0"];
        string ini = Path.Combine(nested, "Zhuang Fangyi.ini");
        File.WriteAllText(ini, "[Constants]\n" + string.Join('\n', variables.Select(v => $"global persist ${v} = 0")));
        byte[] modBefore = File.ReadAllBytes(ini);
        ModPersistentValue[] slot = variables.Select((v, i) => new ModPersistentValue(relative, "$" + v, "$\\Mods\\" + deployed + "\\" + relative + "\\" + v, values[i])).ToArray();
        string text = "[Constants]\n" + string.Join('\n', slot.Select(v => v.RuntimeKey + " = " + (v.Variable == "$swapvar_toggle_0" ? "0" : v.Value))) + "\n$\\Mods\\Other\\other.ini\\x = 123\n";
        string user = f.User(text);
        var plan = ModPersistentPresetEngine.PlanRuntimeRestore(f.Mod, deployed, user, slot);
        Assert.Equal(1, plan.ChangedCount);
        Assert.Equal(0, plan.AddedCount);
        Assert.Equal(13, plan.UnchangedCount);
        ModPersistentPresetEngine.ApplyRuntimeRestore(plan);
        Assert.Equal(text.Replace("\\swapvar_toggle_0 = 0", "\\swapvar_toggle_0 = -1"), File.ReadAllText(user));
        Assert.Equal(modBefore, File.ReadAllBytes(ini));
    }

    [Fact]
    public void WrongFilename_IsRejected()
    {
        using var f = new Fixture();
        string user = f.User("[Constants]\n"), other = Path.Combine(f.Root, "other.ini");
        File.Copy(user, other);
        Assert.Throws<InvalidDataException>(() => ModPersistentPresetEngine.PlanRuntimeRestore(f.Mod, "Mod", other, [Value()]));
    }

    private static ModPersistentValue Value(string value = "1") => new("a.ini", "$x", "$\\mods\\mod\\a.ini\\x", value);
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "imm-preset-tests-" + Guid.NewGuid().ToString("N"));
        public string Mod => Path.Combine(Root, "Mod");
        public string Ini => Path.Combine(Mod, "a.ini");
        public Fixture() { Directory.CreateDirectory(Mod); File.WriteAllText(Ini, "[Constants]\nglobal persist $x = 0\n"); }
        public string User(string text) { string path = Path.Combine(Root, "d3dx_user.ini"); File.WriteAllText(path, text); return path; }
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
