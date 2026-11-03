using System.Runtime.CompilerServices;

namespace IntegratedModManager.EndfieldLaunch.Tests;

internal static class TestRunMutex
{
    // A game started through the manager holds the real marker, which must not change these tests' results.
    [ModuleInitializer]
    internal static void UseOwnRunningMarker() => LaunchCommand.RunningMutexName = @"Local\IntegratedModManager.Tests." + Guid.NewGuid().ToString("N");
}
