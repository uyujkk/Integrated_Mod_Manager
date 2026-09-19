using System.Text;
using IntegratedModManager.Core;

namespace IntegratedModManager.Core.Tests;

public sealed class EfmiResidentModTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "imm-efmi-resident-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Analyze_RecognizesStandardEfmiToolsLayout()
    {
        string mod = CreateStandardMod("Standard");

        EfmiResidentAnalysis result = EfmiResidentMod.Analyze(mod);

        Assert.Equal(EfmiResidentCompatibility.Supported, result.Compatibility);
        Assert.True(result.CanGenerate);
        Assert.Equal(2, result.PersistentVariableCount);
        Assert.Equal(1, result.KeySectionCount);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Analyze_RequiresReviewForUnguardedOverride()
    {
        string mod = CreateStandardMod("Unsafe");
        File.AppendAllText(Path.Combine(mod, "mod.ini"), """

            [TextureOverride_Uncontrolled]
            hash = deadbeef
            handling = skip
            """);

        EfmiResidentAnalysis result = EfmiResidentMod.Analyze(mod);

        Assert.Equal(EfmiResidentCompatibility.ReviewRequired, result.Compatibility);
        Assert.Contains(result.Issues, issue => issue.Code == "UNGUARDED_OVERRIDE");
    }

    [Fact]
    public void Analyze_RejectsModShippingGlobalD3dxIni()
    {
        string mod = CreateStandardMod("GlobalConfig");
        File.WriteAllText(Path.Combine(mod, "d3dx.ini"), "[Loader]\ntarget = game.exe");

        EfmiResidentAnalysis result = EfmiResidentMod.Analyze(mod);

        Assert.Equal(EfmiResidentCompatibility.Unsupported, result.Compatibility);
        Assert.Contains(result.Issues, issue => issue.Code == "GLOBAL_D3DX_IN_MOD" && issue.IsBlocking);
    }

    [Fact]
    public void Analyze_IgnoresDisabledIniFiles()
    {
        string mod = CreateStandardMod("Disabled");
        File.WriteAllText(Path.Combine(mod, "DISABLEDLegacy.ini"), "[TextureOverrideBad]\nhandling = skip");

        EfmiResidentAnalysis result = EfmiResidentMod.Analyze(mod);

        Assert.Equal(EfmiResidentCompatibility.Supported, result.Compatibility);
        Assert.DoesNotContain(result.IniFiles, path => path.Contains("DISABLED", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TransformIni_GatesStandardRuntimePathsAndKeyConditions()
    {
        string input = """
            [KeyDress]
            key = 9
            condition = $active == 1
            type = cycle
            $dress = 0, 1

            [TextureOverrideComponent]
            hash = 12345678
            if $mod_enabled && DRAW_TYPE == 4
                handling = skip
            endif

            [TextureOverrideTexture]
            hash = abcdef00
            if $object_detected
                this = ResourceTexture
            endif
            """;

        string result = EfmiResidentMod.TransformIni(input, "dress_a");

        Assert.Contains("condition = ($active == 1) && $\\IntegratedModManager\\ResidentController\\enable_dress_a", result);
        Assert.Contains("if $\\IntegratedModManager\\ResidentController\\enable_dress_a && ($mod_enabled && DRAW_TYPE == 4)", result);
        Assert.Contains("if $\\IntegratedModManager\\ResidentController\\enable_dress_a && ($object_detected)", result);
    }

    [Fact]
    public void TransformIni_AddsConditionToKeyWithoutExistingCondition()
    {
        const string input = "[KeyToggle]\nkey = 8\ntype = toggle\n$x = 0, 1\n\n[Present]\npost $active = 0";

        string result = EfmiResidentMod.TransformIni(input, "mod_a");

        Assert.Contains("condition = $\\IntegratedModManager\\ResidentController\\enable_mod_a\n[Present]", result);
    }

    [Fact]
    public void GenerateController_CreatesPersistentProfileAndDerivedEnableFlags()
    {
        EfmiResidentModDefinition[] mods =
        [
            new("mod_a", "Mod A", "C:\\mods\\a"),
            new("mod_b", "Mod B", "C:\\mods\\b")
        ];
        EfmiResidentProfile[] profiles =
        [
            new(1, "Daily", new HashSet<string>(["mod_a"], StringComparer.OrdinalIgnoreCase)),
            new(2, "Photo", new HashSet<string>(["mod_a", "mod_b"], StringComparer.OrdinalIgnoreCase))
        ];

        string result = EfmiResidentMod.GenerateController(mods, profiles);

        Assert.Contains("namespace = IntegratedModManager\\ResidentController", result);
        Assert.Contains("global persist $active_profile = 1", result);
        Assert.Contains("$active_profile = 1, 2", result);
        Assert.Contains("if $active_profile == 1\n    $enable_mod_a = 1\n    $enable_mod_b = 0", Normalize(result));
        Assert.Contains("if $active_profile == 2\n    $enable_mod_a = 1\n    $enable_mod_b = 1", Normalize(result));
    }

    [Fact]
    public void GenerateController_RejectsUnknownProfileMod()
    {
        EfmiResidentModDefinition[] mods = [new("mod_a", "Mod A", "C:\\mods\\a")];
        EfmiResidentProfile[] profiles = [new(1, "Broken", new HashSet<string>(["missing"]))];

        Assert.Throws<ArgumentException>(() => EfmiResidentMod.GenerateController(mods, profiles));
    }

    [Fact]
    public void CreateStableModId_IsDeterministicAndSafe()
    {
        string path = Path.Combine(_root, "Some Mod");

        string first = EfmiResidentMod.CreateStableModId("庄方易 墨韵旗袍", path);
        string second = EfmiResidentMod.CreateStableModId("庄方易 墨韵旗袍", path);

        Assert.Equal(first, second);
        Assert.Matches("^[a-z0-9_]+$", first);
    }

    [Fact]
    public void GenerateDeployment_CopiesAndTransformsWithoutEditingSource()
    {
        string source = CreateStandardMod("Deploy");
        string originalIni = File.ReadAllText(Path.Combine(source, "mod.ini"));
        string output = Path.Combine(_root, "deployment");
        string id = "deploy_mod";
        EfmiResidentModDefinition[] mods = [new(id, "Deploy", source)];
        EfmiResidentProfile[] profiles = [new(1, "Default", new HashSet<string>([id]))];

        EfmiResidentDeploymentResult result = EfmiResidentMod.GenerateDeployment(output, mods, profiles);

        Assert.True(File.Exists(result.ControllerPath));
        Assert.Single(result.GeneratedModPaths);
        string generatedIni = File.ReadAllText(Path.Combine(result.GeneratedModPaths[0], "mod.ini"));
        Assert.Contains(EfmiResidentMod.GetEnableVariable(id), generatedIni);
        Assert.Equal(originalIni, File.ReadAllText(Path.Combine(source, "mod.ini")));
    }

    [Fact]
    public void GenerateDeployment_RefusesNonEmptyOutputDirectory()
    {
        string source = CreateStandardMod("Existing");
        string output = Path.Combine(_root, "existing-output");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "keep.txt"), "user data");
        EfmiResidentModDefinition[] mods = [new("existing", "Existing", source)];
        EfmiResidentProfile[] profiles = [new(1, "Default", new HashSet<string>(["existing"]))];

        Assert.Throws<IOException>(() => EfmiResidentMod.GenerateDeployment(output, mods, profiles));
        Assert.Equal("user data", File.ReadAllText(Path.Combine(output, "keep.txt")));
    }

    private string CreateStandardMod(string name)
    {
        string directory = Path.Combine(_root, name);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "texture.dds"), "texture", Encoding.UTF8);
        File.WriteAllText(Path.Combine(directory, "mod.ini"), """
            [Constants]
            global $required_efmi_version = 1.10
            global $mod_id = -1000
            global $mod_enabled = 0
            global $object_detected = 0
            global persist $dress = 1
            global persist $color = 0

            [Present]
            if $object_detected
                if $mod_enabled
                    post $object_detected = 0
                else if $mod_id == -1000
                    run = CommandListRegisterMod
                endif
            endif

            [CommandListRegisterMod]
            run = CommandList\EFMIv1\RegisterMod
            $mod_id = $\EFMIv1\mod_id
            if $mod_id >= 0
                $mod_enabled = 1
            endif

            [KeyDress]
            key = 9
            type = cycle
            $dress = 0, 1

            [TextureOverride_Component0]
            hash = 526afce0
            $object_detected = 1
            if $mod_enabled && DRAW_TYPE == 4
                handling = skip
                run = CommandList_Draw_Component0
            endif

            [TextureOverride_Texture0]
            hash = fe1d6277
            if $object_detected
                this = Resource_Texture0
            endif

            [CommandList_Draw_Component0]
            drawindexed = 3, 0, 0

            [Resource_Texture0]
            filename = texture.dds
            """);
        return directory;
    }

    private static string Normalize(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }
}
