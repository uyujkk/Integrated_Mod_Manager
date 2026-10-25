using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using IntegratedModManager.Core.Infrastructure;

namespace IntegratedModManager.Core.Archives;

public enum ArchiveExtractionStage
{
    Preparing,
    Extracting,
    Completed
}

public readonly record struct ArchiveExtractionProgress(int Percent, ArchiveExtractionStage Stage);

/// <summary>
/// Extracts a supported archive into an empty staging directory after checking every
/// entry path and rejecting links. ZIP is read in-process; 7Z/RAR/ZIPX/CAB use 7-Zip
/// (or the Bandizip fallback) and the tar family uses Windows tar, each bounded by
/// one time limit with stdin closed so no tool can wait for a hidden prompt.
/// Failures are tagged with an <see cref="ArchiveErrorKind"/> for localization.
/// </summary>
public sealed class ArchiveExtractor
{
    /// <summary>Recognized archive suffixes; compound suffixes are matched before plain ones.</summary>
    public static readonly IReadOnlyList<string> SupportedExtensions =
    [
        ".tar.gz", ".tar.bz2", ".tar.xz", ".tar.zst",
        ".zip", ".zipx", ".7z", ".rar", ".tar", ".gz", ".tgz", ".bz2", ".xz", ".zst", ".cab"
    ];

    private static readonly string[] ExternalToolExtensions = [".7z", ".rar", ".zipx", ".cab"];

    private readonly Func<string?> _findSevenZip;
    private readonly Func<string?> _findBandizip;
    private readonly TimeSpan _toolTimeout;
    private readonly string _tarPath;

    /// <param name="findSevenZip">Resolves 7z.exe when a 7-Zip format is extracted, or null.</param>
    /// <param name="findBandizip">Resolves Bandizip's bz.exe when 7-Zip is missing, or null.</param>
    /// <param name="toolTimeout">Deadline for each external tool invocation.</param>
    /// <param name="tarPath">Windows tar; defaults to System32 so a tar.exe planted next to the app is never used.</param>
    public ArchiveExtractor(Func<string?> findSevenZip, Func<string?> findBandizip, TimeSpan toolTimeout, string? tarPath = null)
    {
        _findSevenZip = findSevenZip ?? throw new ArgumentNullException(nameof(findSevenZip));
        _findBandizip = findBandizip ?? throw new ArgumentNullException(nameof(findBandizip));
        _toolTimeout = toolTimeout;
        _tarPath = tarPath ?? Path.Combine(Environment.SystemDirectory, "tar.exe");
    }

    public static string? GetSupportedExtension(string path)
    {
        string fileName = Path.GetFileName(path);
        return SupportedExtensions
            .OrderByDescending(extension => extension.Length)
            .FirstOrDefault(extension => fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
    }

    public void Extract(string archivePath, string destinationDirectory, Action<ArchiveExtractionProgress>? onProgress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        string extension = GetSupportedExtension(archivePath) ?? Path.GetExtension(archivePath).ToLowerInvariant();
        onProgress?.Invoke(new(10, ArchiveExtractionStage.Preparing));

        if (extension == ".zip")
        {
            ExtractZip(archivePath, destinationDirectory, onProgress);
        }
        else if (ExternalToolExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            ExtractWithExternalTool(archivePath, destinationDirectory);
            SafeDirectoryTree.Inspect(destinationDirectory);
        }
        else
        {
            // The tar family and any unrecognized suffix go through Windows tar.
            ValidateTarEntries(archivePath, destinationDirectory);
            RunTarExtraction(archivePath, destinationDirectory);
            SafeDirectoryTree.Inspect(destinationDirectory);
        }

        onProgress?.Invoke(new(100, ArchiveExtractionStage.Completed));
    }

    private static void ExtractZip(string archivePath, string destinationDirectory, Action<ArchiveExtractionProgress>? onProgress)
    {
        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        List<ZipArchiveEntry> entries = archive.Entries
            .Where(entry => !string.IsNullOrEmpty(entry.FullName) && !entry.FullName.EndsWith("/", StringComparison.Ordinal))
            .ToList();
        if (entries.Count == 0)
        {
            throw ArchiveErrors.Tag(
                new InvalidOperationException("This archive does not contain extractable files."),
                ArchiveErrorKind.NoExtractableFiles);
        }

        for (int index = 0; index < entries.Count; index++)
        {
            ZipArchiveEntry entry = entries[index];
            if (IsZipLinkEntry(entry))
            {
                throw ArchiveErrors.Create(ArchiveErrorKind.ZipSymbolicLink, "The archive contains an unsupported symbolic link.");
            }

            string destinationPath = PathSafety.ResolveInsideDirectory(
                destinationDirectory,
                entry.FullName.Replace('/', Path.DirectorySeparatorChar));
            string? destinationFolder = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(destinationFolder))
            {
                Directory.CreateDirectory(destinationFolder);
            }

            entry.ExtractToFile(destinationPath, true);
            onProgress?.Invoke(new(Math.Min(95, 10 + ((index + 1) * 85 / entries.Count)), ArchiveExtractionStage.Extracting));
        }
    }

    internal static bool IsZipLinkEntry(ZipArchiveEntry entry)
    {
        const int UnixFileTypeMask = 0xF000;
        const int UnixSymbolicLink = 0xA000;
        int unixMode = (entry.ExternalAttributes >> 16) & UnixFileTypeMask;
        return unixMode == UnixSymbolicLink;
    }

    private void ExtractWithExternalTool(string archivePath, string destinationDirectory)
    {
        // Resolve once so inspection and extraction use the same installed tool.
        string? sevenZipPath = _findSevenZip();
        if (sevenZipPath is not null)
        {
            ValidateSevenZipEntries(sevenZipPath, archivePath, destinationDirectory);
            RunSevenZipExtraction(sevenZipPath, archivePath, destinationDirectory);
            return;
        }

        string? bandizipPath = _findBandizip();
        if (bandizipPath is null)
        {
            throw ArchiveErrors.Tag(
                new InvalidOperationException("This format requires 7-Zip or Bandizip. Install either tool, or place 7z.exe and 7z.dll in the app's Tools folder. Bandizip requires its bundled bz.exe."),
                ArchiveErrorKind.ToolUnavailable);
        }

        try
        {
            BandizipArchiveExtractor.Extract(bandizipPath, archivePath, destinationDirectory, RunTool);
        }
        catch (TimeoutException)
        {
            // Already tagged as ToolTimeout; Bandizip attaches any staging cleanup failure.
            throw;
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or Win32Exception)
        {
            string cleanupDetails = ex.Data[BandizipArchiveExtractor.StagingCleanupFailureKey] is Exception cleanupFailure
                ? " Temporary archive cleanup also failed: " + cleanupFailure.Message
                : string.Empty;
            throw ArchiveErrors.Tag(
                new InvalidOperationException(
                    "Bandizip extraction failed. The archive must pass Windows tar path and link checks first; try installing 7-Zip if inspection is unsupported. Details: "
                    + ex.Message + cleanupDetails,
                    ex),
                ArchiveErrorKind.BandizipFailed,
                ex.Message);
        }
    }

    private void ValidateSevenZipEntries(string sevenZipPath, string archivePath, string destinationDirectory)
    {
        (int exitCode, string output, string error) = RunTool(CreateStartInfo(sevenZipPath, "l", "-slt", archivePath));
        if (exitCode != 0)
        {
            // With encrypted headers 7-Zip cannot even list the archive and only reports
            // that the closed password prompt was interrupted.
            string detail = string.IsNullOrWhiteSpace(error) ? exitCode.ToString(CultureInfo.InvariantCulture) : error.Trim();
            throw ArchiveErrors.Create(
                ArchiveErrorKind.SevenZipListingFailed,
                "7-Zip could not read the archive. It may be damaged or password-protected (password-protected archives are not supported). Details: " + detail,
                detail);
        }

        SevenZipListingValidator.Validate(output, destinationDirectory);
    }

    private void RunSevenZipExtraction(string sevenZipPath, string archivePath, string destinationDirectory)
    {
        (int exitCode, _, string error) = RunTool(CreateStartInfo(sevenZipPath, "x", "-y", "-aoa", "-o" + destinationDirectory, archivePath));
        if (exitCode != 0)
        {
            string? detail = string.IsNullOrWhiteSpace(error) ? null : error.Trim();
            throw ArchiveErrors.Tag(
                new InvalidOperationException(detail ?? "7-Zip could not extract this file. Check whether the archive is valid."),
                ArchiveErrorKind.SevenZipExtractionFailed,
                detail);
        }
    }

    private void ValidateTarEntries(string archivePath, string destinationDirectory)
    {
        (int exitCode, string output, string error) = RunTool(CreateStartInfo(_tarPath, "-tf", archivePath));
        if (exitCode != 0)
        {
            throw new InvalidDataException(string.IsNullOrWhiteSpace(error) ? "Unable to inspect the archive." : error.Trim());
        }

        foreach (string entryPath in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            PathSafety.ResolveInsideDirectory(destinationDirectory, entryPath.Trim());
        }

        (exitCode, output, error) = RunTool(CreateStartInfo(_tarPath, "-tvf", archivePath));
        if (exitCode != 0)
        {
            throw new InvalidDataException(string.IsNullOrWhiteSpace(error) ? "Unable to inspect archive links." : error.Trim());
        }

        foreach (string rawLine in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.TrimStart();
            if (line.Length > 0 && (line[0] == 'l' || line[0] == 'h'))
            {
                throw ArchiveErrors.Create(ArchiveErrorKind.TarLinkEntry, "The archive contains an unsupported link.");
            }
        }
    }

    private void RunTarExtraction(string archivePath, string destinationDirectory)
    {
        (int exitCode, _, string error) = RunTool(CreateStartInfo(_tarPath, "-xf", archivePath, "-C", destinationDirectory));
        if (exitCode != 0)
        {
            string? detail = string.IsNullOrWhiteSpace(error) ? null : error.Trim();
            throw ArchiveErrors.Tag(
                new InvalidOperationException(detail ?? "This system could not extract the selected format. Try ZIP or 7Z instead."),
                ArchiveErrorKind.TarExtractionFailed,
                detail);
        }
    }

    private (int ExitCode, string Output, string Error) RunTool(ProcessStartInfo startInfo)
    {
        try
        {
            return ArchiveProcessRunner.Run(startInfo, _toolTimeout);
        }
        catch (TimeoutException ex)
        {
            string tool = Path.GetFileName(startInfo.FileName);
            string cleanupDetails = ex.InnerException is null
                ? string.Empty
                : " Termination of all child processes could not be confirmed. Details: " + ex.InnerException.Message;
            throw ArchiveErrors.Tag(
                new TimeoutException(
                    $"{tool} exceeded the {_toolTimeout.TotalMinutes:g}-minute time limit and the task was stopped. Check the archive before retrying." + cleanupDetails,
                    ex),
                ArchiveErrorKind.ToolTimeout,
                tool);
        }
    }

    private static ProcessStartInfo CreateStartInfo(string fileName, params string[] arguments)
    {
        // stdin is redirected so ArchiveProcessRunner closes it: an encrypted archive
        // fails instead of waiting for a hidden password prompt until the timeout.
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }
}
