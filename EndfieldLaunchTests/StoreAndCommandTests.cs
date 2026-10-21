// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 EndfieldBridge contributors and Integrated Mod Manager contributors.
using System.Text.Json.Nodes;
using IntegratedModManager.EndfieldLaunch;

namespace IntegratedModManager.EndfieldLaunch.Tests;

public sealed class StoreAndCommandTests : IDisposable
{
    private readonly LaunchFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void ProfileStatusAndLogRoundTrip()
    {
        LaunchStore store = _fixture.Store;
        Assert.Throws<EndfieldLaunchException>(() => store.LoadProfile());
        Assert.Null(store.ReadStatus());

        store.SaveProfile(new LaunchProfile { Game = _fixture.Game, Language = "en-US", WithMods = true, SessionId = "abc" });
        LaunchProfile loaded = store.LoadProfile();
        store.WriteStatus("abc", LaunchStates.GameStarted, new LaunchText("已启动", "Started"), 42);
        store.Log(new LaunchText("中文", "English"), english: true);
        store.Log(new LaunchText("中文", "English"), english: false);

        Assert.Equal(_fixture.Game, loaded.Game);
        Assert.True(loaded.English);
        Assert.True(loaded.WithMods);
        Assert.Equal("-force-d3d11", loaded.GameArguments);
        Assert.NotSame(loaded, loaded.Clone());
        LaunchStatus status = store.ReadStatus()!;
        Assert.Equal(("abc", LaunchStates.GameStarted, 42), (status.SessionId, status.State, status.ProcessId));
        Assert.Equal(new LaunchText("已启动", "Started"), status.Message);
        string[] lines = File.ReadAllLines(store.LogPath);
        Assert.EndsWith("English", lines[0]);
        Assert.EndsWith("中文", lines[1]);
        Assert.True(LaunchStates.IsTerminal(LaunchStates.Failed));
        Assert.False(LaunchStates.IsTerminal(LaunchStates.LoaderStarted));
    }

    [Fact]
    public void CorruptFilesAreTreatedAsMissingAndLargeLogsRotate()
    {
        LaunchStore store = _fixture.Store;
        Directory.CreateDirectory(store.DataDirectory);
        File.WriteAllText(store.StatusPath, "{ not json");
        File.WriteAllText(Path.Combine(store.DataDirectory, "parent-cache.json"), "[]");
        File.WriteAllText(store.LogPath, new string('x', 1024 * 1024 + 1));

        Assert.Null(store.ReadStatus());
        Assert.Null(store.ReadCachedParent(_fixture.Game));
        store.Log(LaunchText.Both("after rotation"), english: false);

        Assert.True(File.Exists(store.LogPath + ".previous"));
        Assert.EndsWith("after rotation", File.ReadAllText(store.LogPath).TrimEnd());
        Assert.Throws<ArgumentException>(() => new LaunchStore(" "));
    }

    [Fact]
    public void CachedLauncherMustStillExist()
    {
        string parent = Path.Combine(_fixture.Root, "Launcher", "1.0.0", "Games.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(parent)!);
        File.WriteAllText(parent, "fixture");
        _fixture.Store.WriteCachedParent(_fixture.Game, parent);

        Assert.Equal(parent, _fixture.Store.ReadCachedParent(_fixture.Game));
        File.Delete(parent);
        Assert.Null(_fixture.Store.ReadCachedParent(_fixture.Game));
    }

    [Fact]
    public void LaunchTextCombinesAndChoosesLanguage()
    {
        LaunchText text = new LaunchText("甲", "A") + new LaunchText("乙", "B");

        Assert.Equal("甲乙", text.For(english: false));
        Assert.Equal("AB", text.For(english: true));
        Assert.Equal(new LaunchText("x", "x"), LaunchText.Both("x"));
        var exception = new EndfieldLaunchException("中", "en", new InvalidOperationException());
        Assert.Equal("en", exception.Message);
        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }

    [Fact]
    public void CommandRecognisesOnlyLaunchVerbsAndReportsFailuresThroughStatus()
    {
        Assert.True(LaunchCommand.IsLaunchCommand(["--endfield-launch"]));
        Assert.True(LaunchCommand.IsLaunchCommand(["--endfield-game-only", "--from-xxmi"]));
        Assert.False(LaunchCommand.IsLaunchCommand([]));
        Assert.False(LaunchCommand.IsLaunchCommand(["--self-test"]));

        // No saved profile: the command fails cleanly and records why.
        Assert.Equal(1, LaunchCommand.Run(["--endfield-launch"], _fixture.Store.DataDirectory, _fixture.ManagerExecutable));
        Assert.Equal(LaunchStates.Failed, _fixture.Store.ReadStatus()!.State);

        // With a profile but an unknown verb (or without elevation) it still never launches anything.
        _fixture.Store.SaveProfile(new LaunchProfile { Game = _fixture.Game, SessionId = "s1" });
        Assert.Equal(1, LaunchCommand.Run(["--endfield-launch", "--unexpected"], _fixture.Store.DataDirectory, _fixture.ManagerExecutable));
        LaunchStatus status = _fixture.Store.ReadStatus()!;
        Assert.Equal(("s1", LaunchStates.Failed), (status.SessionId, status.State));
        Assert.True(File.Exists(_fixture.Store.LogPath));
    }

    [Fact]
    public void RunningMarkerIsVisibleToOtherCallers()
    {
        Assert.False(LaunchCommand.IsLaunchRunning());
        using (new Mutex(false, LaunchCommand.RunningMutexName))
        {
            Assert.True(LaunchCommand.IsLaunchRunning());
        }

        Assert.False(LaunchCommand.IsLaunchRunning());
    }

    [Fact]
    public void ServiceChecksGameAndLauncherAndReadsLegacyBridgeSettings()
    {
        var service = new EndfieldLaunchService(_fixture.Store.DataDirectory, _fixture.ManagerExecutable, _ => false);
        string unsigned = Path.Combine(_fixture.Root, "Launcher", "1.0.0", "Games.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(unsigned)!);
        File.WriteAllText(unsigned, "not signed");

        Assert.Throws<EndfieldLaunchException>(() => service.Check(new LaunchProfile { Game = Path.Combine(_fixture.Root, "missing", "Endfield.exe") }));
        var noLauncher = Assert.Throws<EndfieldLaunchException>(() => service.Check(new LaunchProfile { Game = _fixture.Game, Parent = unsigned }));
        Assert.Contains("签名验证未通过", noLauncher.Text.Zh);

        string legacy = Path.Combine(_fixture.Root, "EndfieldBridge-0.3.2-win-x64");
        Assert.Null(EndfieldLaunchService.ReadLegacyProfile(legacy));
        Directory.CreateDirectory(Path.Combine(legacy, "data"));
        File.WriteAllText(Path.Combine(legacy, "data", "profile.json"), new JsonObject
        {
            ["Game"] = _fixture.Game, ["Parent"] = "", ["Xxmi"] = "C:\\XXMI\\XXMI Launcher.exe",
            ["GameArguments"] = "-force-d3d11 -popupwindow", ["Language"] = "zh-CN", ["WithMods"] = true
        }.ToJsonString());
        LaunchProfile imported = EndfieldLaunchService.ReadLegacyProfile(legacy)!;
        Assert.Equal((_fixture.Game, "C:\\XXMI\\XXMI Launcher.exe", "-force-d3d11 -popupwindow", true),
            (imported.Game, imported.Xxmi, imported.GameArguments, imported.WithMods));

        File.WriteAllText(Path.Combine(legacy, "data", "profile.json"), "{ broken");
        File.WriteAllText(Path.Combine(legacy, "profile.json"), "{\"Game\":\"old-layout\"}");
        LaunchProfile older = EndfieldLaunchService.ReadLegacyProfile(legacy)!;
        Assert.Equal(("old-layout", "-force-d3d11", false), (older.Game, older.GameArguments, older.WithMods));
        Assert.False(service.AdoptLegacyRestoreState(legacy));
    }
}
