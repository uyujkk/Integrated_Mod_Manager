using IntegratedModManager.Core.Archives;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private static bool IsSupportedArchiveFile(string path)
    {
        return GetSupportedArchiveExtension(path) is not null;
    }

    private static string? GetSupportedArchiveExtension(string path)
    {
        return ArchiveExtractor.GetSupportedExtension(path);
    }

    /// <summary>
    /// Extracts an archive and installs it as exactly one new Mod folder in the character
    /// folder: archives without a top-level folder get one, archives with a single
    /// top-level folder are not nested again, and an existing folder is never merged.
    /// </summary>
    private string ImportArchiveContents(string archivePath, string firstLevelDirectory)
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), "ModFolderCopier_Import_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            ExtractArchiveToDirectory(archivePath, tempDirectory);
            ArchiveImportPlan plan = ArchiveImportLayout.Plan(tempDirectory, archivePath, SupportedArchiveExtensions);
            return ArchiveImportLayout.Install(plan, firstLevelDirectory);
        }
        catch (ArchiveImportConflictException ex)
        {
            throw new InvalidOperationException(T("Archives.AFolderWithTheSameName", ex.Destination), ex);
        }
        catch (InvalidDataException ex) when (ArchiveErrors.Is(ex, ArchiveErrorKind.Empty))
        {
            throw new InvalidOperationException(T("Archives.TheArchiveIsEmptyThereIs"), ex);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                DeleteDirectoryTreeSafely(tempDirectory);
            }
        }
    }

    private void ExtractArchiveToDirectory(string archivePath, string destinationDirectory)
    {
        try
        {
            ArchiveExtraction.Extract(archivePath, destinationDirectory, progress =>
            {
                string message = progress.Stage switch
                {
                    ArchiveExtractionStage.Preparing => T("Archives.PreparingExtraction"),
                    ArchiveExtractionStage.Extracting => T("Archives.ExtractingFiles"),
                    _ => T("Archives.ExtractionComplete")
                };
                DispatcherQueue.TryEnqueue(() => UpdateProgress(progress.Percent, message));
            });
        }
        catch (Exception ex) when (LocalizeArchiveError(ex) is Exception localized)
        {
            throw localized;
        }
    }

    /// <summary>Maps a tagged Core archive failure to the user's language; null keeps the original.</summary>
    private Exception? LocalizeArchiveError(Exception ex)
    {
        string? detail = ArchiveErrors.DetailOf(ex);
        return ArchiveErrors.KindOf(ex) switch
        {
            ArchiveErrorKind.NoExtractableFiles => new InvalidOperationException(
                T("Archives.ThisArchiveDoesNotContainExtractable"), ex),
            ArchiveErrorKind.ZipSymbolicLink => new InvalidDataException(
                T("Archives.TheArchiveContainsAnUnsupportedSymbolic"), ex),
            ArchiveErrorKind.TarLinkEntry => new InvalidDataException(
                T("Archives.TheArchiveContainsAnUnsupportedLink"), ex),
            ArchiveErrorKind.LinkEntry => new InvalidDataException(
                T("Archives.TheArchiveContainsAnActualSymbolic"), ex),
            ArchiveErrorKind.Encrypted => new InvalidDataException(
                T("Archives.TheArchiveIsPasswordProtectedWhich"), ex),
            ArchiveErrorKind.ToolUnavailable => new InvalidOperationException(T("Archives.ThisFormatRequires7ZipOr"), ex),
            ArchiveErrorKind.SevenZipListingFailed => new InvalidDataException(T("Archives.N7ZipCouldNotReadThe") + detail, ex),
            ArchiveErrorKind.SevenZipExtractionFailed => new InvalidOperationException(detail
                ?? T("Archives.N7ZipCouldNotExtractThis"), ex),
            ArchiveErrorKind.TarExtractionFailed => new InvalidOperationException(detail
                ?? T("Archives.ThisSystemCouldNotExtractThe"), ex),
            ArchiveErrorKind.BandizipFailed => new InvalidOperationException(T("Archives.BandizipExtractionFailedTheArchiveMust")
                + detail + StagingCleanupDetails(ex.InnerException), ex),
            ArchiveErrorKind.ToolTimeout => new TimeoutException(T("Archives.ExceededTheMinuteTimeLimitAnd", detail, ArchiveToolTimeoutMinutes)
                + (ex.InnerException?.InnerException is Exception childFailure
                    ? T("Archives.TerminationOfAllChildProcessesCould") + childFailure.Message
                    : string.Empty)
                + StagingCleanupDetails(ex), ex),
            _ => null
        };
    }

    private string StagingCleanupDetails(Exception? ex)
    {
        return ex?.Data[BandizipArchiveExtractor.StagingCleanupFailureKey] is Exception cleanupFailure
            ? T("Archives.TemporaryArchiveCleanupAlsoFailed") + cleanupFailure.Message
            : string.Empty;
    }

    private static string? FindSevenZipExecutable()
    {
        string baseDirectory = AppContext.BaseDirectory;
        string[] candidates =
        [
            Path.Combine(baseDirectory, "Tools", "7z.exe"),
            Path.Combine(baseDirectory, "7z.exe"),
            @"C:\Program Files\7-Zip\7z.exe",
            @"C:\Program Files (x86)\7-Zip\7z.exe"
        ];

        return candidates.FirstOrDefault(File.Exists);
    }

    private static string? FindBandizipExecutable()
    {
        var directories = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "Tools"),
            AppContext.BaseDirectory
        };

        // App Paths also covers installations on custom drives. Resolve the console
        // sibling instead of launching Bandizip.exe, whose GUI can outlive its process.
        foreach (Microsoft.Win32.RegistryHive hive in new[] { Microsoft.Win32.RegistryHive.CurrentUser, Microsoft.Win32.RegistryHive.LocalMachine })
        {
            foreach (Microsoft.Win32.RegistryView view in new[] { Microsoft.Win32.RegistryView.Registry64, Microsoft.Win32.RegistryView.Registry32 })
            {
                try
                {
                    using var root = Microsoft.Win32.RegistryKey.OpenBaseKey(hive, view);
                    using var key = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\Bandizip.exe");
                    if (key?.GetValue(null) is string registeredPath
                        && Path.GetDirectoryName(registeredPath.Trim('"')) is string directory)
                    {
                        directories.Add(directory);
                    }
                }
                catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
                {
                    // A restricted registry must not prevent discovery via standard paths.
                }
            }
        }

        directories.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Bandizip"));
        directories.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Bandizip"));
        directories.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Bandizip"));
        directories.AddRange((Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator).Select(directory => directory.Trim().Trim('"')));
        return BandizipArchiveExtractor.FindExecutable(directories);
    }
}
