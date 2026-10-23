// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 EndfieldBridge contributors and Integrated Mod Manager contributors.
// Ported from the EndfieldBridge 0.3.2 self-test; see ../IntegratedModManager.EndfieldLaunch/NOTICE.md.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace IntegratedModManager.EndfieldLaunch.Tests;

public sealed class XxmiIntegrationTests : IDisposable
{
    private const string OriginalConfig =
        "{\"Launcher\":{\"active_importer\":\"ZZMI\"},\"Importers\":{\"EFMI\":{\"Importer\":{\"game_launch\":\"MANUAL\",\"custom_launch\":\"previous\",\"custom_launch_signature\":\"keep\",\"xxmi_dll_inject_mode\":\"DIRECT\",\"launch_options\":\"preserve me\"}},\"ZZMI\":{\"preserve\":true}}}";

    private readonly LaunchFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private XxmiIntegration Integration(Func<string, bool>? running = null) =>
        new(_fixture.ManagerExecutable, _fixture.Store, running ?? (_ => false));

    [Fact]
    public void CommandPointsAtManagerExecutableAndRecognisesOldBridgeCommands()
    {
        XxmiIntegration integration = Integration();

        Assert.Equal(CommandLine.Quote(_fixture.ManagerExecutable) + " --endfield-game-only --from-xxmi", integration.Command);
        Assert.True(XxmiIntegration.IsLegacyBridgeCommand("\"C:\\Tools\\EndfieldBridge\\EndfieldBridge.exe\" --game-only --from-xxmi"));
        Assert.True(XxmiIntegration.IsLegacyBridgeCommand("\"D:\\x\\endfieldbridge.exe\" --game-only"));
        Assert.False(XxmiIntegration.IsLegacyBridgeCommand("\"D:\\x\\Other.exe\" --game-only"));
        Assert.False(XxmiIntegration.IsLegacyBridgeCommand("\"D:\\x\\EndfieldBridge.exe\" --game-only && calc"));
        Assert.False(XxmiIntegration.IsLegacyBridgeCommand(null));
    }

    [Fact]
    public void ApplyWritesOnlyLaunchFieldsAndRestoreReturnsOriginal()
    {
        LaunchProfile profile = _fixture.CreateXxmi(OriginalConfig);
        XxmiIntegration integration = Integration();

        string backup = integration.Apply(profile);

        Assert.True(File.Exists(backup));
        Assert.True(integration.IsConfigured(profile));
        JsonObject efmi = XxmiIntegration.Efmi(XxmiIntegration.Read(_fixture.XxmiConfig));
        Assert.Equal("CUSTOM", efmi["game_launch"]!.ToString());
        Assert.Equal("NATIVE", efmi["start_method"]!.ToString());
        Assert.True(efmi["game_process_exe_enabled"]!.GetValue<bool>());
        Assert.Equal("Endfield.exe", efmi["game_process_exe"]!.ToString());
        Assert.Equal(Path.GetDirectoryName(profile.Game), efmi["game_folder"]!.ToString());
        Assert.Equal(integration.Command, efmi["custom_launch"]!.ToString());
        Assert.Equal(string.Empty, efmi["custom_launch_signature"]!.ToString());
        Assert.Equal("preserve me", efmi["launch_options"]!.ToString());
        JsonObject document = XxmiIntegration.Read(_fixture.XxmiConfig);
        Assert.Equal("ZZMI", document["Launcher"]!["active_importer"]!.ToString());
        Assert.True(document["Importers"]!["ZZMI"]!["preserve"]!.GetValue<bool>());

        var repeated = Assert.Throws<EndfieldLaunchException>(() => integration.Apply(profile));
        Assert.Contains("不覆盖首次备份", repeated.Text.Zh);

        integration.Restore();

        Assert.True(JsonNode.DeepEquals(XxmiIntegration.Read(_fixture.XxmiConfig), JsonNode.Parse(OriginalConfig)));
        Assert.False(File.Exists(_fixture.Store.RestoreStatePath));
        Assert.Throws<EndfieldLaunchException>(() => integration.Restore());
    }

    [Fact]
    public void ReapplyingKeepsFirstRestorePointAndExistingAuthorization()
    {
        LaunchProfile profile = _fixture.CreateXxmi(OriginalConfig);
        XxmiIntegration integration = Integration();
        integration.Apply(profile);
        string firstState = File.ReadAllText(_fixture.Store.RestoreStatePath);
        string signature = _fixture.Authorize(integration.Command);
        Assert.True(integration.IsCommandAuthorized(profile));

        // Break only the folder, as a user editing XXMI might; the same command stays signed.
        _fixture.EditEfmi(efmi => efmi["game_folder"] = "missing-shortcut.lnk");
        Assert.False(integration.IsConfigured(profile));
        integration.Apply(profile);

        Assert.Equal(firstState, File.ReadAllText(_fixture.Store.RestoreStatePath));
        Assert.Equal(signature, XxmiIntegration.Efmi(XxmiIntegration.Read(_fixture.XxmiConfig))["custom_launch_signature"]!.ToString());
        Assert.True(integration.IsCommandAuthorized(profile));
        Assert.Contains("签名验证通过", integration.AuthorizationStatus(profile).Zh);

        _fixture.EditEfmi(efmi => efmi["custom_launch"] = integration.Command + " altered");
        Assert.False(integration.IsCommandAuthorized(profile));
        Assert.Contains("尚未通过", integration.AuthorizationStatus(profile).Zh);
    }

    [Fact]
    public void MigratingFromOldBridgeKeepsItsPreBridgeRestorePoint()
    {
        const string preBridge = "{\"Importers\":{\"EFMI\":{\"Importer\":{\"game_launch\":\"MANUAL\",\"xxmi_dll_inject_mode\":\"HOOK\"}}}}";
        LaunchProfile profile = _fixture.CreateXxmi(preBridge);
        string legacyRoot = Path.Combine(_fixture.Root, "legacy-bridge");
        Directory.CreateDirectory(Path.Combine(legacyRoot, "data"));
        string oldCommand = CommandLine.Quote(Path.Combine(legacyRoot, "EndfieldBridge.exe")) + " --game-only --from-xxmi";
        var legacyFields = new JsonObject
        {
            ["game_launch"] = new JsonObject { ["exists"] = true, ["value"] = "MANUAL" },
            ["custom_launch"] = new JsonObject { ["exists"] = false, ["value"] = null },
            ["start_method"] = new JsonObject { ["exists"] = false, ["value"] = null }
        };
        File.WriteAllText(Path.Combine(legacyRoot, "data", "integration-backup.json"),
            new JsonObject { ["schema"] = 2, ["config"] = _fixture.XxmiConfig, ["fields"] = legacyFields }.ToJsonString());
        _fixture.EditEfmi(efmi =>
        {
            efmi["game_launch"] = "CUSTOM";
            efmi["start_method"] = "NATIVE";
            efmi["custom_launch"] = oldCommand;
        });
        var service = new EndfieldLaunchService(_fixture.Store.DataDirectory, _fixture.ManagerExecutable, _ => false);

        Assert.Contains("旧版 EndfieldBridge", service.Integration.Inspect(profile).Zh);
        Assert.True(service.AdoptLegacyRestoreState(legacyRoot));
        Assert.False(service.AdoptLegacyRestoreState(legacyRoot));
        service.Integration.Apply(profile);
        Assert.True(service.Integration.IsConfigured(profile));
        service.Integration.Restore();

        JsonObject efmi = XxmiIntegration.Efmi(XxmiIntegration.Read(_fixture.XxmiConfig));
        Assert.Equal("MANUAL", efmi["game_launch"]!.ToString());
        Assert.False(efmi.ContainsKey("custom_launch"));
        Assert.False(efmi.ContainsKey("start_method"));
        Assert.Equal("HOOK", efmi["xxmi_dll_inject_mode"]!.ToString());
    }

    [Theory]
    [InlineData("game_launch")]
    [InlineData("start_method")]
    [InlineData("game_folder")]
    [InlineData("game_process_exe")]
    [InlineData("game_process_exe_enabled")]
    public void RestoreRefusesLaterLaunchFieldEditsWithoutWritingAnything(string field)
    {
        LaunchProfile profile = _fixture.CreateXxmi(OriginalConfig);
        XxmiIntegration integration = Integration();
        integration.Apply(profile);
        if (field == "game_process_exe_enabled") _fixture.EditEfmi(efmi => efmi[field] = false);
        else _fixture.EditEfmi(efmi => efmi[field] = "user changed this");
        string editedConfig = File.ReadAllText(_fixture.XxmiConfig);
        string restore = File.ReadAllText(_fixture.Store.RestoreStatePath);

        var error = Assert.Throws<EndfieldLaunchException>(() => integration.Restore());

        Assert.Contains(field, error.Text.En);
        Assert.Equal(editedConfig, File.ReadAllText(_fixture.XxmiConfig));
        Assert.Equal(restore, File.ReadAllText(_fixture.Store.RestoreStatePath));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(_fixture.XxmiConfig)!, "*.before-restore-*.bak"));
    }

    [Fact]
    public void RestoreAllowsXxmiAuthorizationAndPreservesUnrelatedUserChanges()
    {
        LaunchProfile profile = _fixture.CreateXxmi(OriginalConfig);
        XxmiIntegration integration = Integration();
        integration.Apply(profile);
        _fixture.Authorize(integration.Command);
        _fixture.EditEfmi(efmi => efmi["launch_options"] = "user's later arguments");

        integration.Restore();

        JsonObject efmi = XxmiIntegration.Efmi(XxmiIntegration.Read(_fixture.XxmiConfig));
        Assert.Equal("previous", efmi["custom_launch"]!.ToString());
        Assert.Equal("keep", efmi["custom_launch_signature"]!.ToString());
        Assert.Equal("user's later arguments", efmi["launch_options"]!.ToString());
    }

    [Fact]
    public void ExistingSetupCanUpgradeOldRestoreRecordWithoutLosingOriginalFields()
    {
        LaunchProfile profile = _fixture.CreateXxmi(OriginalConfig);
        XxmiIntegration integration = Integration();
        integration.Apply(profile);
        JsonObject state = XxmiIntegration.Read(_fixture.Store.RestoreStatePath);
        JsonNode originalFields = state["fields"]!.DeepClone();
        state.Remove("applied");
        state["schema"] = 2;
        File.WriteAllText(_fixture.Store.RestoreStatePath, state.ToJsonString());
        string config = File.ReadAllText(_fixture.XxmiConfig);

        Assert.Contains("旧恢复点", Assert.Throws<EndfieldLaunchException>(() => integration.Restore()).Text.Zh);
        Assert.Equal(config, File.ReadAllText(_fixture.XxmiConfig));
        var service = new EndfieldLaunchService(_fixture.Store.DataDirectory, _fixture.ManagerExecutable, _ => false);
        Assert.Null(service.ConfigureXxmi(profile));
        Assert.True(JsonNode.DeepEquals(originalFields, XxmiIntegration.Read(_fixture.Store.RestoreStatePath)["fields"]));
        integration.Restore();
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(OriginalConfig), XxmiIntegration.Read(_fixture.XxmiConfig)));
    }

    [Fact]
    public void ImportReadsArgumentsAndProtectsValidGamePath()
    {
        LaunchProfile profile = _fixture.CreateXxmi(OriginalConfig);
        XxmiIntegration integration = Integration();
        integration.Apply(profile);
        _fixture.EditEfmi(efmi =>
        {
            efmi["d3d11_mode_cmd_args"] = "-force-d3d11";
            efmi["use_launch_options"] = true;
            efmi["launch_options"] = "-screen-width 1920";
        });

        Assert.Contains("已读取", integration.Import(profile, strict: true).Zh);
        Assert.Equal("-force-d3d11 -screen-width 1920", profile.GameArguments);

        _fixture.EditEfmi(efmi => efmi["use_launch_options"] = false);
        integration.Import(profile, strict: true);
        Assert.Equal("-force-d3d11", profile.GameArguments);

        string validGame = profile.Game;
        _fixture.EditEfmi(efmi => efmi["game_folder"] = Path.Combine(_fixture.Root, "missing"));
        Assert.Contains("保留", integration.Import(profile).Zh);
        Assert.Equal(validGame, profile.Game);
        Assert.Throws<EndfieldLaunchException>(() => integration.Import(profile, strict: true));
        Assert.Contains("游戏目录无效", integration.Inspect(profile).Zh);
    }

    [Fact]
    public void ValidateModeRequiresSupportedInjectionCompleteSetupAndNoPreLaunchCall()
    {
        LaunchProfile profile = _fixture.CreateXxmi(OriginalConfig);
        XxmiIntegration integration = Integration();
        Assert.Contains("配置不完整", Assert.Throws<EndfieldLaunchException>(() => integration.ValidateMode(profile)).Text.Zh);

        integration.Apply(profile);
        integration.ValidateMode(profile);

        _fixture.EditEfmi(efmi =>
        {
            efmi["run_pre_launch_enabled"] = true;
            efmi["run_pre_launch"] = integration.Command;
        });
        Assert.Contains("前置命令", Assert.Throws<EndfieldLaunchException>(() => integration.ValidateMode(profile)).Text.Zh);

        _fixture.EditEfmi(efmi => efmi["xxmi_dll_inject_mode"] = "NONE");
        Assert.Contains("DIRECT 或 HOOK", Assert.Throws<EndfieldLaunchException>(() => integration.ValidateMode(profile)).Text.Zh);
    }

    [Fact]
    public void ApplyRefusesUnsupportedConfigurations()
    {
        LaunchProfile profile = _fixture.CreateXxmi(OriginalConfig.Replace("\"DIRECT\"", "\"NONE\""));
        Assert.Throws<EndfieldLaunchException>(() => Integration().Apply(profile));

        _fixture.WriteXxmiConfig("{\"Importers\":{}}");
        Assert.Contains("缺少 EFMI", Assert.Throws<EndfieldLaunchException>(() => Integration().Apply(profile)).Text.Zh);

        _fixture.WriteXxmiConfig("[1,2]");
        Assert.Throws<EndfieldLaunchException>(() => XxmiIntegration.Read(_fixture.XxmiConfig));

        _fixture.WriteXxmiConfig(OriginalConfig.Replace("\"launch_options\"", "\"run_pre_launch_enabled\":true,\"run_pre_launch\":\"EndfieldBridge.exe\",\"launch_options\""));
        Assert.Contains("前置命令", Assert.Throws<EndfieldLaunchException>(() => Integration().Apply(profile)).Text.Zh);
    }

    [Fact]
    public void RunningLoaderOrGameBlocksEditsAndForeignCommandBlocksRestore()
    {
        LaunchProfile profile = _fixture.CreateXxmi(OriginalConfig);
        Assert.Contains("退出 XXMI", Assert.Throws<EndfieldLaunchException>(() => Integration(name => name == "XXMI Launcher").Apply(profile)).Text.Zh);
        Assert.Contains("退出终末地", Assert.Throws<EndfieldLaunchException>(() => Integration(name => name == "Endfield").Apply(profile)).Text.Zh);

        XxmiIntegration integration = Integration();
        integration.Apply(profile);
        _fixture.EditEfmi(efmi => efmi["custom_launch"] = "\"C:\\Mine\\custom.exe\"");
        Assert.Contains("命令已改变", Assert.Throws<EndfieldLaunchException>(() => integration.Restore()).Text.Zh);
    }

    [Fact]
    public void ApplyingAnotherXxmiInstallationKeepsTheExistingRestorePoint()
    {
        LaunchProfile profile = _fixture.CreateXxmi(OriginalConfig);
        XxmiIntegration integration = Integration();
        integration.Apply(profile);
        string originalState = File.ReadAllText(_fixture.Store.RestoreStatePath);
        string second = Path.Combine(_fixture.Root, "OtherXXMI");
        Directory.CreateDirectory(second);
        string config = Path.Combine(second, "XXMI Launcher Config.json");
        File.WriteAllText(config, OriginalConfig);
        var other = profile.Clone();
        other.Xxmi = Path.Combine(second, "XXMI Launcher.exe");

        Assert.Contains("另一套 XXMI", Assert.Throws<EndfieldLaunchException>(() => integration.Apply(other)).Text.Zh);
        Assert.Equal(originalState, File.ReadAllText(_fixture.Store.RestoreStatePath));
        Assert.Equal(OriginalConfig, File.ReadAllText(config));
        integration.Restore();
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(OriginalConfig), XxmiIntegration.Read(_fixture.XxmiConfig)));
    }
    [Fact]
    public void RestorePointOfARemovedXxmiIsRetiredInsteadOfBlockingSetup()
    {
        string second = Path.Combine(_fixture.Root, "OldXXMI");
        Directory.CreateDirectory(second);
        string oldConfig = Path.Combine(second, "XXMI Launcher Config.json");
        File.WriteAllText(oldConfig, OriginalConfig);
        LaunchProfile old = _fixture.CreateXxmi(OriginalConfig).Clone();
        old.Xxmi = Path.Combine(second, "XXMI Launcher.exe");
        XxmiIntegration integration = Integration();
        integration.Apply(old);
        string staleState = File.ReadAllText(_fixture.Store.RestoreStatePath);
        Directory.Delete(second, recursive: true);

        Assert.Contains("已不存在", Assert.Throws<EndfieldLaunchException>(() => integration.Restore()).Text.Zh);
        Assert.Equal(staleState, File.ReadAllText(_fixture.Store.RestoreStatePath));

        LaunchProfile current = new() { Game = _fixture.Game, Xxmi = _fixture.XxmiExecutable };
        integration.Apply(current);
        string retired = Assert.Single(Directory.GetFiles(_fixture.Store.DataDirectory, Path.GetFileName(_fixture.Store.RestoreStatePath) + ".stale-*"));
        Assert.Equal(staleState, File.ReadAllText(retired));
        Assert.True(XxmiIntegration.SamePath(_fixture.XxmiConfig, XxmiIntegration.Read(_fixture.Store.RestoreStatePath)["config"]!.ToString()));
        integration.Restore();
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(OriginalConfig), XxmiIntegration.Read(_fixture.XxmiConfig)));
    }

    [Fact]
    public void RestoreToleratesXxmiRewritingTheGameFolderAsAnEquivalentPath()
    {
        LaunchProfile profile = _fixture.CreateXxmi(OriginalConfig);
        XxmiIntegration integration = Integration();
        integration.Apply(profile);
        string folder = Path.GetDirectoryName(_fixture.Game)!;
        _fixture.EditEfmi(efmi => efmi["game_folder"] = folder.ToUpperInvariant() + Path.DirectorySeparatorChar);

        integration.Restore();

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(OriginalConfig), XxmiIntegration.Read(_fixture.XxmiConfig)));
    }

    [Theory]
    [InlineData("game_folder", "C:\\Games\\Endfield", "c:/games/endfield/", true)]
    [InlineData("game_folder", "C:\\Games\\Endfield", "C:\\Games\\Other", false)]
    [InlineData("game_process_exe", "Endfield.exe", "endfield.exe", false)]
    [InlineData("game_folder", "C:\\Games", null, false)]
    public void OnlyTheGameFolderComparesAsAPath(string key, string expected, string? actual, bool same) =>
        Assert.Equal(same, XxmiIntegration.SameFieldValue(key, JsonValue.Create(expected), actual is null ? null : JsonValue.Create(actual)));

    [Fact]
    public void ReadaptingKeepsTheFirstBackupAndOnlyTheNewestFew()
    {
        LaunchProfile profile = _fixture.CreateXxmi(OriginalConfig);
        XxmiIntegration integration = Integration();
        string first = integration.Apply(profile);
        for (int i = 0; i < XxmiIntegration.KeptSetupBackups + 3; i++)
        {
            Thread.Sleep(2);
            integration.Apply(profile, allowConfigured: true);
        }

        string[] backups = Directory.GetFiles(Path.GetDirectoryName(_fixture.XxmiConfig)!, "*.imm-endfield-*.bak");
        Assert.Equal(XxmiIntegration.KeptSetupBackups + 1, backups.Length);
        Assert.Contains(first, backups);
        Assert.Equal(OriginalConfig, File.ReadAllText(first));
    }

    [Fact]
    public void ForeignCommandSetupArchivesTheReplacedRestorePoint()
    {
        LaunchProfile profile = _fixture.CreateXxmi(OriginalConfig);
        XxmiIntegration integration = Integration();
        integration.Apply(profile);
        string firstState = File.ReadAllText(_fixture.Store.RestoreStatePath);
        _fixture.EditEfmi(efmi => efmi["custom_launch"] = "user's own launcher");

        integration.Apply(profile);

        string archived = Assert.Single(Directory.GetFiles(_fixture.Store.DataDirectory, Path.GetFileName(_fixture.Store.RestoreStatePath) + ".superseded-*"));
        Assert.Equal(firstState, File.ReadAllText(archived));
        integration.Restore();
        Assert.Equal("user's own launcher", XxmiIntegration.Efmi(XxmiIntegration.Read(_fixture.XxmiConfig))["custom_launch"]!.ToString());
    }

    [Fact]
    public void ConfigPathSearchesUpwardAndRequiresAnExecutable()
    {
        _fixture.CreateXxmi(OriginalConfig);
        string nested = Path.Combine(Path.GetDirectoryName(_fixture.XxmiExecutable)!, "Resources", "Bin");
        Directory.CreateDirectory(nested);

        Assert.Equal(_fixture.XxmiConfig, XxmiIntegration.ConfigPath(Path.Combine(nested, "XXMI Launcher.exe")));
        Assert.Throws<EndfieldLaunchException>(() => XxmiIntegration.ConfigPath(" "));
        Assert.Throws<EndfieldLaunchException>(() => XxmiIntegration.ConfigPath(Path.Combine(_fixture.Root, "nowhere", "XXMI Launcher.exe")));
    }
}

internal sealed class LaunchFixture : IDisposable
{
    public LaunchFixture()
    {
        Root = Path.Combine(Path.GetTempPath(), "imm-endfield-launch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        Store = new LaunchStore(Path.Combine(Root, "data"));
        ManagerExecutable = Path.Combine(Root, "Manager", "WinUI3", "ModFolderCopier.WinUI.exe");
        Game = CreateGame("Game");
    }

    public string Root { get; }

    public LaunchStore Store { get; }

    public string ManagerExecutable { get; }

    public string Game { get; }

    public string XxmiExecutable => Path.Combine(Root, "XXMI", "XXMI Launcher.exe");

    public string XxmiConfig => Path.Combine(Root, "XXMI", "XXMI Launcher Config.json");

    public string CreateGame(string folder)
    {
        string directory = Path.Combine(Root, folder);
        Directory.CreateDirectory(Path.Combine(directory, "Endfield_Data"));
        string executable = Path.Combine(directory, "Endfield.exe");
        File.WriteAllText(executable, "test fixture - never executed");
        return executable;
    }

    public LaunchProfile CreateXxmi(string config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(XxmiExecutable)!);
        File.WriteAllText(XxmiExecutable, "test fixture - never executed");
        WriteXxmiConfig(config);
        return new LaunchProfile { Game = Game, Xxmi = XxmiExecutable };
    }

    public void WriteXxmiConfig(string config) => File.WriteAllText(XxmiConfig, config);

    public void EditEfmi(Action<JsonObject> edit)
    {
        JsonObject document = XxmiIntegration.Read(XxmiConfig);
        edit(XxmiIntegration.Efmi(document));
        File.WriteAllText(XxmiConfig, document.ToJsonString());
    }

    /// <summary>Signs the command the way XXMI would, with a throw-away key whose public half is installed.</summary>
    public string Authorize(string command)
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        string security = Path.Combine(Path.GetDirectoryName(XxmiConfig)!, "Resources", "Security");
        Directory.CreateDirectory(security);
        File.WriteAllText(Path.Combine(security, "public_key.der"), Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
        string signature = Convert.ToBase64String(key.SignData(Encoding.UTF8.GetBytes(command), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));
        EditEfmi(efmi => efmi["custom_launch_signature"] = signature);
        return signature;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
