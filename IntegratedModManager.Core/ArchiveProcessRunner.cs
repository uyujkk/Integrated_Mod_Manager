using System.Diagnostics;

namespace IntegratedModManager.Core;

/// <summary>
/// Runs an archive tool with one deadline for both its exit and redirected output.
/// </summary>
public static class ArchiveProcessRunner
{
    private const int CleanupTimeoutMilliseconds = 2000;

    public static (int ExitCode, string Output, string Error) Run(
        ProcessStartInfo startInfo, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        if (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > uint.MaxValue - 1)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "The archive timeout must be finite and positive.");
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        using var cancellation = new CancellationTokenSource();
        // EOF prevents password or overwrite prompts from waiting for interactive input.
        if (startInfo.RedirectStandardInput)
        {
            process.StandardInput.Close();
        }

        // Drain both pipes concurrently; either pipe can fill before the tool exits.
        Task<string> output = startInfo.RedirectStandardOutput
            ? process.StandardOutput.ReadToEndAsync(cancellation.Token)
            : Task.FromResult(string.Empty);
        Task<string> error = startInfo.RedirectStandardError
            ? process.StandardError.ReadToEndAsync(cancellation.Token)
            : Task.FromResult(string.Empty);
        Task completion = Task.WhenAll(process.WaitForExitAsync(cancellation.Token), output, error);
        try
        {
            // A child can keep a pipe open even after the original tool has exited.
            // Applying the deadline to the whole operation also bounds that case.
            completion.WaitAsync(timeout).GetAwaiter().GetResult();
            return (process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
        }
        catch (TimeoutException)
        {
            Exception? cleanupFailure = null;
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    if (!process.WaitForExit(CleanupTimeoutMilliseconds))
                    {
                        cleanupFailure = new InvalidOperationException("The archive process did not exit after termination.");
                    }
                }
                else
                {
                    // Process.Kill cannot discover descendants once their parent has exited.
                    cleanupFailure = new InvalidOperationException(
                        "The archive process exited, but its output pipes stayed open; descendant termination could not be confirmed.");
                }
            }
            catch (Exception exception)
            {
                // Preserve termination failures instead of claiming the tool was stopped.
                cleanupFailure = exception;
            }
            finally
            {
                cancellation.Cancel();
            }

            string cleanupDetails = cleanupFailure is null ? string.Empty : " " + cleanupFailure.Message;
            throw new TimeoutException(
                $"Archive tool '{startInfo.FileName}' exceeded its timeout of {timeout.TotalSeconds:g} seconds.{cleanupDetails}",
                cleanupFailure);
        }
    }
}
