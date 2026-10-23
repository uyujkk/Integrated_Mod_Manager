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
            throw new InvalidOperationException(L(
                $"角色目录中已有同名文件夹，未合并或覆盖：\n{ex.Destination}\n请先重命名或删除原有文件夹，再导入。",
                $"A folder with the same name already exists and was not merged or overwritten:\n{ex.Destination}\nRename or remove it first, then import again."), ex);
        }
        catch (InvalidDataException ex) when (ArchiveErrors.Is(ex, ArchiveErrorKind.Empty))
        {
            throw new InvalidOperationException(L("压缩包是空的，没有可导入的内容。", "The archive is empty; there is nothing to import."), ex);
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
                    ArchiveExtractionStage.Preparing => L("正在准备解压...", "Preparing extraction..."),
                    ArchiveExtractionStage.Extracting => L("正在解压文件...", "Extracting files..."),
                    _ => L("解压完成", "Extraction complete")
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
                L("这个压缩包里没有可解压的文件。", "This archive does not contain extractable files."), ex),
            ArchiveErrorKind.ZipSymbolicLink => new InvalidDataException(
                L("压缩包包含不受支持的符号链接。", "The archive contains an unsupported symbolic link."), ex),
            ArchiveErrorKind.TarLinkEntry => new InvalidDataException(
                L("压缩包包含不受支持的链接。", "The archive contains an unsupported link."), ex),
            ArchiveErrorKind.LinkEntry => new InvalidDataException(
                L("压缩包包含真实的符号链接、硬链接或复制链接，已停止解压。", "The archive contains an actual symbolic, hard, or copy link. Extraction was stopped."), ex),
            ArchiveErrorKind.Encrypted => new InvalidDataException(
                L("压缩包设置了密码，暂不支持导入。请先解压后再导入文件夹。", "The archive is password-protected, which is not supported. Extract it yourself first, then import the folder."), ex),
            ArchiveErrorKind.ToolUnavailable => new InvalidOperationException(L(
                "解压此格式需要 7-Zip 或 Bandizip。请安装其中一个，或把 7z.exe 和 7z.dll 放到程序目录的 Tools 文件夹中。Bandizip 需要附带的 bz.exe。",
                "This format requires 7-Zip or Bandizip. Install either tool, or place 7z.exe and 7z.dll in the app's Tools folder. Bandizip requires its bundled bz.exe."), ex),
            ArchiveErrorKind.SevenZipListingFailed => new InvalidDataException(L(
                "7-Zip 无法读取压缩包，它可能已损坏或设置了密码（不支持带密码的压缩包）。详情：",
                "7-Zip could not read the archive. It may be damaged or password-protected (password-protected archives are not supported). Details: ") + detail, ex),
            ArchiveErrorKind.SevenZipExtractionFailed => new InvalidOperationException(detail
                ?? L("7-Zip 无法解压当前文件。请检查压缩包是否完整。", "7-Zip could not extract this file. Check whether the archive is valid."), ex),
            ArchiveErrorKind.TarExtractionFailed => new InvalidOperationException(detail
                ?? L("当前系统无法解压该格式，建议尝试 ZIP 或 7Z。", "This system could not extract the selected format. Try ZIP or 7Z instead."), ex),
            ArchiveErrorKind.BandizipFailed => new InvalidOperationException(L(
                "Bandizip 解压失败。压缩包必须先通过 Windows tar 的路径和链接检查；不支持检查的格式请尝试安装 7-Zip。详情：",
                "Bandizip extraction failed. The archive must pass Windows tar path and link checks first; try installing 7-Zip if inspection is unsupported. Details: ")
                + detail + StagingCleanupDetails(ex.InnerException), ex),
            ArchiveErrorKind.ToolTimeout => new TimeoutException(L(
                $"{detail} 运行超过 {ArchiveToolTimeoutMinutes} 分钟，任务已停止。请检查压缩包后重试。",
                $"{detail} exceeded the {ArchiveToolTimeoutMinutes}-minute time limit and the task was stopped. Check the archive before retrying.")
                + (ex.InnerException?.InnerException is Exception childFailure
                    ? L(" 未能确认所有子进程已退出。详情：", " Termination of all child processes could not be confirmed. Details: ") + childFailure.Message
                    : string.Empty)
                + StagingCleanupDetails(ex), ex),
            _ => null
        };
    }

    private string StagingCleanupDetails(Exception? ex)
    {
        return ex?.Data[BandizipArchiveExtractor.StagingCleanupFailureKey] is Exception cleanupFailure
            ? L(" 临时压缩包清理也失败了：", " Temporary archive cleanup also failed: ") + cleanupFailure.Message
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
