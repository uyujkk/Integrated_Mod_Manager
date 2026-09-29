using System.Diagnostics;
using System.Text;

namespace IntegratedModManager.Core;

/// <summary>
/// Uses Bandizip's console tool after Windows tar has checked paths and links.
/// Bandizip's 7Z listing omits link metadata, so it cannot replace this preflight.
/// </summary>
public static class BandizipArchiveExtractor
{
    public const string StagingCleanupFailureKey = "ArchiveStagingCleanupFailure";

    public static string? FindExecutable(IEnumerable<string> directories)
    {
        // Ignore empty/relative PATH entries instead of searching the working directory.
        return directories
            .Where(directory => !string.IsNullOrWhiteSpace(directory) && Path.IsPathFullyQualified(directory))
            .Select(directory => Path.Combine(directory, "bz.exe"))
            .FirstOrDefault(File.Exists);
    }

    public static void Extract(
        string executablePath,
        string archivePath,
        string destinationDirectory,
        Func<ProcessStartInfo, (int ExitCode, string Output, string Error)> runProcess)
    {
        string stagingDirectory = Directory.CreateTempSubdirectory("ModFolderCopier_Archive_").FullName;
        string stagedArchivePath = Path.Combine(stagingDirectory, Path.GetFileName(archivePath));
        Exception? extractionFailure = null;
        try
        {
            // Lock the source while copying, then inspect only our private snapshot.
            // The caller's file can change afterwards without changing the checked bytes.
            using (var source = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var target = new FileStream(stagedArchivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                source.CopyTo(target);
            }

            // Keep this read-only sharing handle alive through every preflight and bz.exe.
            // On Windows this allows readers but prevents writes, deletion, and replacement.
            using var archiveLock = new FileStream(stagedArchivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            ExtractStaged(executablePath, stagedArchivePath, destinationDirectory, runProcess);
        }
        catch (Exception ex)
        {
            extractionFailure = ex;
            throw;
        }
        finally
        {
            // Only delete the snapshot we created; the user's source is never removed.
            try
            {
                File.Delete(stagedArchivePath);
                Directory.Delete(stagingDirectory);
            }
            catch (Exception cleanupFailure) when (extractionFailure is not null
                && cleanupFailure is IOException or UnauthorizedAccessException)
            {
                // A tool whose termination failed may still hold the archive. Keep the
                // original failure and attach cleanup details instead of hiding the timeout.
                extractionFailure.Data[StagingCleanupFailureKey] = cleanupFailure;
            }
        }
    }

    private static void ExtractStaged(
        string executablePath,
        string archivePath,
        string destinationDirectory,
        Func<ProcessStartInfo, (int ExitCode, string Output, string Error)> runProcess)
    {
        // tar omits some RAR5 redirection kinds; inspect those headers independently.
        Rar5ArchiveValidator.Validate(archivePath);
        string tarPath = Path.Combine(Environment.SystemDirectory, "tar.exe");
        var listing = runProcess(CreateStartInfo(tarPath, "-tf", archivePath));
        EnsureInspectionSucceeded(listing.ExitCode, listing.Error);
        string[] entries = listing.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        foreach (string entryPath in entries)
        {
            PathSafety.ResolveInsideDirectory(destinationDirectory, entryPath);
        }

        var details = runProcess(CreateStartInfo(tarPath, "-tvf", archivePath));
        EnsureInspectionSucceeded(details.ExitCode, details.Error);
        string[] detailLines = details.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (entries.Length == 0 || entries.Length != detailLines.Length)
        {
            throw new InvalidDataException("Windows tar returned an empty or inconsistent archive listing.");
        }
        for (int index = 0; index < detailLines.Length; index++)
        {
            string line = detailLines[index].TrimStart();
            // Only regular files and directories are safe to hand to another extractor.
            // Reject links, special files, and unrecognized listing output before any writes.
            // RAR5 hard links can have regular-file permissions in bsdtar's output.
            // Include the entry name so a regular file named "notes link to mod.txt"
            // is not mistaken for the link annotation appended after a filename.
            bool isHardLink = line.Contains(" " + entries[index] + " link to ", StringComparison.Ordinal);
            if (line.Length == 0 || (line[0] != '-' && line[0] != 'd') || isHardLink)
            {
                throw new InvalidDataException("The archive contains an unsupported link or special entry.");
            }
        }

        ProcessStartInfo extraction = CreateStartInfo(
            executablePath, "x", "-y", "-aoa", "-consolemode:utf8", "-o:" + destinationDirectory, "-", archivePath);
        var result = runProcess(extraction);
        if (result.ExitCode != 0)
        {
            // bz.exe writes some failures to stdout; keep those details for the task error.
            string message = string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error;
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(message)
                ? "Bandizip could not extract this file. Check whether the archive is valid."
                : message.Trim());
        }
    }

    private static void EnsureInspectionSucceeded(int exitCode, string error)
    {
        if (exitCode != 0)
        {
            throw new InvalidDataException(string.IsNullOrWhiteSpace(error)
                ? "Windows tar could not inspect this archive for Bandizip. Try installing 7-Zip."
                : error.Trim());
        }
    }

    private static ProcessStartInfo CreateStartInfo(string executablePath, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true
        };
        // ArgumentList preserves spaces, Unicode, and trailing slashes without shell quoting.
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        return startInfo;
    }
}
