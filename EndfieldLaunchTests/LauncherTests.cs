// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 EndfieldBridge contributors and Integrated Mod Manager contributors.
// Ported and extended from the EndfieldBridge 0.3.2 self-test; see ../IntegratedModManager.EndfieldLaunch/NOTICE.md.
using System.Diagnostics;
using IntegratedModManager.EndfieldLaunch;

namespace IntegratedModManager.EndfieldLaunch.Tests;

public sealed class LauncherTests : IDisposable
{
    private static readonly string Cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
    private readonly LaunchFixture _fixture = new();
    private readonly List<LaunchText> _log = [];

    public void Dispose() => _fixture.Dispose();

    [Theory]
    [InlineData("", "\"\"")]
    [InlineData("x\\", "\"x\\\\\"")]
    [InlineData("a\"b", "\"a\\\"b\"")]
    [InlineData("C:\\Program Files\\Game\\Endfield.exe", "\"C:\\Program Files\\Game\\Endfield.exe\"")]
    [InlineData("a\\\\\"b", "\"a\\\\\\\\\\\"b\"")]
    public void QuoteFollowsWindowsArgumentRules(string value, string expected) =>
        Assert.Equal(expected, CommandLine.Quote(value));

    [Fact]
    public void UnsignedOrMisnamedLaunchersAreRejected()
    {
        string unsigned = Path.Combine(_fixture.Root, "Launcher", "1.0.0", "Games.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(unsigned)!);
        File.WriteAllText(unsigned, "not a signed executable");

        Assert.Throws<OfficialSignatureException>(() => OfficialLauncher.Verify(Cmd));
        Assert.Throws<OfficialSignatureException>(() => OfficialLauncher.Verify(Path.Combine(_fixture.Root, "missing", "Games.exe")));
        Assert.Contains("签名验证未通过", Assert.Throws<OfficialSignatureException>(() => OfficialLauncher.Verify(unsigned)).Text.Zh);
        Assert.Throws<OfficialSignatureException>(() => LaunchSession.Create(unsigned, _fixture.Game, string.Empty, _ => { }));
    }

    [Fact]
    public void SignerOrganizationMustMatchExactly()
    {
        const string organization = OfficialLauncher.ExpectedOrganization;

        Assert.True(OfficialLauncher.HasOrganization($"CN=Games\nO=\"{organization}\"\nC=CN", organization));
        Assert.True(OfficialLauncher.HasOrganization($"CN=Games\nO={organization.ToUpperInvariant()}", organization));
        Assert.False(OfficialLauncher.HasOrganization($"CN={organization}\nO=Somebody Else", organization));
        Assert.False(OfficialLauncher.HasOrganization($"OU={organization}", organization));
    }

    [Fact]
    public void SessionCreatesTargetUnderSuspendedParentAndCleansOnlyTheParent()
    {
        uint parentId;
        using (LaunchSession session = LaunchSession.Create(Cmd, Cmd, "/c exit 7", _log.Add, verifyParent: false))
        {
            parentId = session.ParentId;
            Assert.NotEqual(0u, session.TargetId);
            Assert.Equal(7, session.Wait());
            // Windows records the suspended helper as the target's parent.
            Assert.Equal(parentId, (uint)session.TargetParentId);
        }

        Assert.Throws<ArgumentException>(() => Process.GetProcessById((int)parentId).Dispose());
        Assert.Contains(_log, line => line.En.StartsWith("Suspended parent created", StringComparison.Ordinal));
        Assert.Contains(_log, line => line.En.StartsWith("Target process created", StringComparison.Ordinal));
    }

    [Fact]
    public void ParentCandidatesUseExplicitChoiceAloneOtherwiseCacheFirst()
    {
        string newer = CreateLauncher("2.1.0");
        string older = CreateLauncher("1.9.9");
        string cached = CreateLauncher("cached");
        string explicitChoice = Path.Combine(_fixture.Root, "explicit", "Games.exe");
        var profile = new LaunchProfile { Game = _fixture.Game };

        IReadOnlyList<string> discovered = EndfieldGame.ParentCandidates(profile, cached, _ => [older, newer, cached, Path.Combine(_fixture.Root, "gone", "Games.exe")]);
        profile.Parent = explicitChoice;

        Assert.Equal(new[] { cached, older, newer }, discovered);
        Assert.Equal(new[] { explicitChoice }, EndfieldGame.ParentCandidates(profile, cached, _ => [newer]));
        Assert.Equal(new[] { newer, older }, EndfieldGame.VersionedGamesExecutables(Path.Combine(_fixture.Root, "Launcher")).Where(path => path != cached));
        Assert.Empty(EndfieldGame.VersionedGamesExecutables(Path.Combine(_fixture.Root, "no-launcher")));
    }

    [Fact]
    public void LaunchFallsBackPastInvalidLaunchersAndCachesTheOneUsed()
    {
        string tampered = CreateLauncher("2.0.0");
        string good = CreateLauncher("1.0.0");
        var profile = new LaunchProfile { Game = _fixture.Game, GameArguments = "-force-d3d11" };
        var launcher = new EndfieldLauncher(_fixture.Store, _log.Add, _ => false);
        var attempts = new List<string>();
        int startedPid = 0;

        int code = launcher.LaunchGame(profile, pid => startedPid = pid, (parent, game, arguments) =>
        {
            attempts.Add(parent);
            Assert.Equal(_fixture.Game, game);
            Assert.Equal("-force-d3d11", arguments);
            if (parent == tampered) throw new OfficialSignatureException("签名验证未通过", "verification failed");
            return LaunchSession.Create(Cmd, Cmd, "/c exit 3", _log.Add, verifyParent: false);
        }, _ => [tampered, good]);

        Assert.Equal(3, code);
        Assert.Equal(new[] { tampered, good }, attempts);
        Assert.NotEqual(0, startedPid);
        Assert.Equal(good, _fixture.Store.ReadCachedParent(_fixture.Game));
        Assert.Null(_fixture.Store.ReadCachedParent(_fixture.CreateGame("OtherGame")));

        attempts.Clear();
        launcher.LaunchGame(profile, null, (parent, _, _) =>
        {
            attempts.Add(parent);
            return LaunchSession.Create(Cmd, Cmd, "/c exit 0", _log.Add, verifyParent: false);
        }, _ => [tampered, good]);
        Assert.Equal(good, attempts[0]);
    }

    [Fact]
    public void LaunchReportsEveryRejectedLauncherAndRefusesWhenGameIsRunning()
    {
        string tampered = CreateLauncher("2.0.0");
        var profile = new LaunchProfile { Game = _fixture.Game };
        var launcher = new EndfieldLauncher(_fixture.Store, _log.Add, _ => false);

        var failure = Assert.Throws<EndfieldLaunchException>(() => launcher.LaunchGame(profile, null,
            (_, _, _) => throw new OfficialSignatureException("签名组织不符", "wrong signer"), _ => [tampered]));
        Assert.Contains("未找到签名有效的官方 Games.exe", failure.Text.Zh);
        Assert.Contains("2.0.0：签名组织不符", failure.Text.Zh);
        Assert.Contains("请手动选择", Assert.Throws<EndfieldLaunchException>(() => launcher.LaunchGame(profile, null,
            (_, _, _) => throw new InvalidOperationException(), _ => [])).Text.Zh);

        var running = new EndfieldLauncher(_fixture.Store, _log.Add, name => name == "Endfield");
        Assert.Contains("已在运行", Assert.Throws<EndfieldLaunchException>(() => running.LaunchGame(profile)).Text.Zh);
    }

    [Fact]
    public void ProfileValidationRejectsWrongExecutablesAndControlCharacters()
    {
        Assert.Throws<EndfieldLaunchException>(() => EndfieldGame.ValidateProfile(new LaunchProfile { Game = Cmd }));
        Assert.Throws<EndfieldLaunchException>(() => EndfieldGame.ValidateProfile(new LaunchProfile { Game = _fixture.Game, GameArguments = "-a\n-b" }));
        EndfieldGame.ValidateProfile(new LaunchProfile { Game = _fixture.Game });

        Assert.True(EndfieldGame.IsValidGame(_fixture.Game));
        Directory.Delete(Path.Combine(Path.GetDirectoryName(_fixture.Game)!, "Endfield_Data"));
        Assert.False(EndfieldGame.IsValidGame(_fixture.Game));
        Assert.False(EndfieldGame.IsValidGame(null));
    }

    [Fact]
    public void LaunchThroughXxmiChecksLoaderStateBeforeStartingIt()
    {
        var profile = new LaunchProfile { Game = _fixture.Game, Xxmi = Path.Combine(_fixture.Root, "missing", "XXMI Launcher.exe"), WithMods = true };
        var launcher = new EndfieldLauncher(_fixture.Store, _log.Add, _ => false);
        var integration = new XxmiIntegration(_fixture.ManagerExecutable, _fixture.Store, _ => false);

        Assert.Contains("XXMI 程序不存在", Assert.Throws<EndfieldLaunchException>(() => launcher.LaunchThroughXxmi(profile, integration)).Text.Zh);

        LaunchProfile configured = _fixture.CreateXxmi("{\"Importers\":{\"EFMI\":{\"Importer\":{\"xxmi_dll_inject_mode\":\"DIRECT\"}}}}");
        Assert.Contains("配置不完整", Assert.Throws<EndfieldLaunchException>(() => launcher.LaunchThroughXxmi(configured, integration)).Text.Zh);

        integration.Apply(configured);
        var loaderRunning = new EndfieldLauncher(_fixture.Store, _log.Add, name => name == "XXMI Launcher");
        Assert.Contains("退出已运行的 XXMI", Assert.Throws<EndfieldLaunchException>(() => loaderRunning.LaunchThroughXxmi(configured, integration)).Text.Zh);
        var gameRunning = new EndfieldLauncher(_fixture.Store, _log.Add, name => name == "Endfield");
        Assert.Contains("游戏已经运行", Assert.Throws<EndfieldLaunchException>(() => gameRunning.LaunchThroughXxmi(configured, integration)).Text.Zh);
    }

    private string CreateLauncher(string version)
    {
        string path = Path.Combine(_fixture.Root, "Launcher", version, "Games.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "test fixture - never executed");
        return path;
    }
}
