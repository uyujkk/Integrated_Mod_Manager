using System.Diagnostics;
using System.Text;
using IntegratedModManager.Core;

namespace IntegratedModManager.Core.Tests;

public sealed class ArchiveProcessRunnerTests
{
    // Windows CI can take several seconds to launch two nested PowerShell processes.
    // Leave enough time for both PID markers to be written before exercising timeout cleanup.
    private static readonly TimeSpan ProcessTreeTimeout = TimeSpan.FromSeconds(20);

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void Run_ClosesInputAndPreservesLargeOutputAndExitCode(int exitCode)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // Each output exceeds pipe capacity, so sequential reads would deadlock.
        ProcessStartInfo startInfo = CreatePowerShellStartInfo(
            "$inputText = [Console]::In.ReadToEnd(); " +
            "[Console]::Out.Write(('o' * 131072) + $inputText); " +
            $"[Console]::Error.Write('e' * 131072); exit {exitCode}");

        var result = ArchiveProcessRunner.Run(startInfo, TimeSpan.FromSeconds(20));

        Assert.Equal(exitCode, result.ExitCode);
        Assert.Equal(new string('o', 131072), result.Output);
        Assert.Equal(new string('e', 131072), result.Error);
    }

    [Fact]
    public void Run_TimeoutTerminatesHangingProcessAndItsChild()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var processes = new TemporaryProcesses();
        string childScript = $"[IO.File]::WriteAllText('{processes.ChildIdPath}', [string]$PID); Start-Sleep -Seconds 600";
        string script = $"[IO.File]::WriteAllText('{processes.ParentIdPath}', [string]$PID); " +
            $"$child = Start-Process -FilePath '{PowerShellPath}' -ArgumentList '-NoProfile','-NonInteractive','-EncodedCommand','{Encode(childScript)}' -WindowStyle Hidden -PassThru; " +
            "Start-Sleep -Seconds 600";
        ProcessStartInfo startInfo = CreatePowerShellStartInfo(script);
        var stopwatch = Stopwatch.StartNew();

        var exception = Assert.Throws<TimeoutException>(() =>
            ArchiveProcessRunner.Run(startInfo, ProcessTreeTimeout));

        Assert.Contains(startInfo.FileName, exception.Message);
        Assert.Null(exception.InnerException);
        Assert.InRange(stopwatch.Elapsed, TimeSpan.FromSeconds(18), TimeSpan.FromSeconds(35));
        processes.AssertBothExited();
    }

    [Fact]
    public void Run_TimeoutAlsoBoundsPipesHeldOpenAfterParentExit()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var processes = new TemporaryProcesses();
        string childScript = $"[IO.File]::WriteAllText('{processes.ChildIdPath}', [string]$PID); Start-Sleep -Seconds 600";
        // Redirecting stdin makes CreateProcess inherit the parent's stdout/stderr handles.
        string script = $"[IO.File]::WriteAllText('{processes.ParentIdPath}', [string]$PID); " +
            "$info = New-Object Diagnostics.ProcessStartInfo; " +
            $"$info.FileName = '{PowerShellPath}'; " +
            $"$info.Arguments = '-NoProfile -NonInteractive -EncodedCommand {Encode(childScript)}'; " +
            "$info.UseShellExecute = $false; $info.CreateNoWindow = $true; $info.RedirectStandardInput = $true; " +
            "$child = [Diagnostics.Process]::Start($info); $child.StandardInput.Close(); exit 0";
        var stopwatch = Stopwatch.StartNew();

        var exception = Assert.Throws<TimeoutException>(() =>
            ArchiveProcessRunner.Run(CreatePowerShellStartInfo(script), ProcessTreeTimeout));

        Assert.InRange(stopwatch.Elapsed, TimeSpan.FromSeconds(18), TimeSpan.FromSeconds(35));
        Assert.NotNull(exception.InnerException);
        Assert.Contains("descendant termination could not be confirmed", exception.Message);
        Assert.True(File.Exists(processes.ChildIdPath), "The child must start before the timeout test can be meaningful.");
        // The parent already exited, so the fixture explicitly cleans up its surviving child.
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(4294967295)]
    public void Run_RejectsInvalidTimeoutBeforeStartingProcess(double milliseconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ArchiveProcessRunner.Run(
            new ProcessStartInfo("must-not-start"), TimeSpan.FromMilliseconds(milliseconds)));
    }

    private static string PowerShellPath => Path.Combine(
        Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");

    private static string Encode(string script) => Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

    private static ProcessStartInfo CreatePowerShellStartInfo(string script)
    {
        var startInfo = new ProcessStartInfo(PowerShellPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Encode(script) })
        {
            startInfo.ArgumentList.Add(argument);
        }
        return startInfo;
    }

    private sealed class TemporaryProcesses : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "imm-process-tests", Guid.NewGuid().ToString("N"));

        public TemporaryProcesses() => Directory.CreateDirectory(_directory);

        public string ParentIdPath => Path.Combine(_directory, "parent.pid");
        public string ChildIdPath => Path.Combine(_directory, "child.pid");

        public void AssertBothExited()
        {
            foreach (string path in new[] { ParentIdPath, ChildIdPath })
            {
                Assert.True(File.Exists(path), "Both test processes must start before the timeout.");
                using Process? process = FindProcess(path);
                Assert.True(process is null || process.WaitForExit(2000), $"Test process in {path} survived termination.");
            }
        }

        public void Dispose()
        {
            // Cleanup runs even if an assertion fails, preventing long-lived test processes.
            foreach (string path in new[] { ParentIdPath, ChildIdPath })
            {
                using Process? process = FindProcess(path);
                if (process is not null && !process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    Assert.True(process.WaitForExit(5000), "Test process cleanup did not finish.");
                }
            }
            Directory.Delete(_directory, recursive: true);
        }

        private static Process? FindProcess(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }
            try
            {
                return Process.GetProcessById(int.Parse(File.ReadAllText(path)));
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
    }
}
