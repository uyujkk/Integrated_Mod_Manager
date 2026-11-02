using System.Diagnostics;
using System.Text;
using IntegratedModManager.Core.Archives;

namespace IntegratedModManager.Core.Tests;

public sealed class ArchiveProcessRunnerTests
{
    // Windows CI has taken more than 20 s to start PowerShell and its child, so a deadline that
    // includes the start fails at random. The timeout tests start the deadline only once the
    // fixture is in the state under test, which also keeps them short.
    private static readonly TimeSpan ProcessTreeTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan FixtureStartLimit = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ProcessStartAllowance = TimeSpan.FromMinutes(1);

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

        var result = ArchiveProcessRunner.Run(startInfo, ProcessStartAllowance);

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
        string script = "$ErrorActionPreference = 'Stop'; " + $"[IO.File]::WriteAllText('{processes.ParentIdPath}', [string]$PID); " +
            $"$child = Start-Process -FilePath '{HangingChildPath}' -ArgumentList {HangingChildArguments} -WindowStyle Hidden -PassThru; " +
            $"[IO.File]::WriteAllText('{processes.ChildIdPath}', [string]$child.Id); " +
            "Start-Sleep -Seconds 600";
        ProcessStartInfo startInfo = CreatePowerShellStartInfo(script);
        var sinceDeadlineStart = new Stopwatch();

        var exception = Assert.Throws<TimeoutException>(() =>
        {
            var result = ArchiveProcessRunner.Run(startInfo, ProcessTreeTimeout, processes.WaitForChildAsync(sinceDeadlineStart));
            Assert.Fail($"The timeout fixture exited early: {result.ExitCode}. {result.Error} {result.Output}");
        });

        Assert.Contains(startInfo.FileName, exception.Message);
        Assert.Null(exception.InnerException);
        AssertDeadlineHonored(sinceDeadlineStart);
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
        // Redirecting stdin makes CreateProcess inherit the parent's stdout/stderr handles.
        string script = "$ErrorActionPreference = 'Stop'; " + $"[IO.File]::WriteAllText('{processes.ParentIdPath}', [string]$PID); " +
            "$info = New-Object Diagnostics.ProcessStartInfo; " +
            $"$info.FileName = '{HangingChildPath}'; " +
            "$info.Arguments = '-n 600 127.0.0.1'; " +
            "$info.UseShellExecute = $false; $info.CreateNoWindow = $true; $info.RedirectStandardInput = $true; " +
            "$child = [Diagnostics.Process]::Start($info); $child.StandardInput.Close(); " +
            $"[IO.File]::WriteAllText('{processes.ChildIdPath}', [string]$child.Id); exit 0";
        var sinceDeadlineStart = new Stopwatch();

        var exception = Assert.Throws<TimeoutException>(() =>
        {
            // The parent must be gone when the deadline passes: that is the case under test.
            var result = ArchiveProcessRunner.Run(CreatePowerShellStartInfo(script), ProcessTreeTimeout,
                processes.WaitForChildThenParentExitAsync(sinceDeadlineStart));
            Assert.Fail($"The timeout fixture exited early: {result.ExitCode}. {result.Error} {result.Output}");
        });

        AssertDeadlineHonored(sinceDeadlineStart);
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

    private static void AssertDeadlineHonored(Stopwatch sinceDeadlineStart)
    {
        Assert.True(sinceDeadlineStart.IsRunning, "The deadline never started.");
        Assert.InRange(sinceDeadlineStart.Elapsed, ProcessTreeTimeout - TimeSpan.FromMilliseconds(100), ProcessStartAllowance);
    }

    private static string PowerShellPath => Path.Combine(
        Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");

    // A console program that starts quickly, needs no input and keeps running for ten minutes.
    private static string HangingChildPath => Path.Combine(Environment.SystemDirectory, "PING.EXE");

    private const string HangingChildArguments = "'-n','600','127.0.0.1'";

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

        // If the marker never appears the deadline starts anyway and the test's own checks fail.
        public async Task WaitForChildAsync(Stopwatch sinceDeadlineStart)
        {
            await WaitForChildMarkerAsync();
            sinceDeadlineStart.Start();
        }

        public async Task WaitForChildThenParentExitAsync(Stopwatch sinceDeadlineStart)
        {
            await WaitForChildMarkerAsync();
            using Process? parent = FindProcess(ParentIdPath);
            if (parent is not null)
            {
                using var limit = new CancellationTokenSource(FixtureStartLimit);
                try
                {
                    await parent.WaitForExitAsync(limit.Token);
                }
                catch (OperationCanceledException)
                {
                    // The test's message check reports a parent that did not exit.
                }
            }
            sinceDeadlineStart.Start();
        }

        private async Task WaitForChildMarkerAsync()
        {
            var waited = Stopwatch.StartNew();
            while (!HasPid(ChildIdPath) && waited.Elapsed < FixtureStartLimit)
            {
                await Task.Delay(50);
            }
        }

        // WriteAllText creates the file before filling it, so an empty marker is not ready yet.
        private static bool HasPid(string path)
        {
            try
            {
                return File.Exists(path) && int.TryParse(File.ReadAllText(path), out _);
            }
            catch (IOException)
            {
                return false;
            }
        }

        public void AssertBothExited()
        {
            foreach (string path in new[] { ParentIdPath, ChildIdPath })
            {
                Assert.True(File.Exists(path), "Both test processes must start before the deadline.");
                using Process? process = FindProcess(path);
                Assert.True(process is null || process.WaitForExit(10000), $"Test process in {path} survived termination.");
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
