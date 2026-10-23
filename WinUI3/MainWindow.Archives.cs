using System.Diagnostics;
using System.IO.Compression;
using System.Globalization;
using IntegratedModManager.Core;
using Windows.System;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private static bool IsSupportedArchiveFile(string path)
    {
        return GetSupportedArchiveExtension(path) is not null;
    }

    private static string? GetSupportedArchiveExtension(string path)
    {
        string fileName = Path.GetFileName(path);
        return SupportedArchiveExtensions
            .OrderByDescending(extension => extension.Length)
            .FirstOrDefault(extension => fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
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
        string extension = GetSupportedArchiveExtension(archivePath) ?? Path.GetExtension(archivePath).ToLowerInvariant();
        DispatcherQueue.TryEnqueue(() => UpdateProgress(10, L("正在准备解压...", "Preparing extraction...")));

        if (extension == ".zip")
        {
            using ZipArchive archive = ZipFile.OpenRead(archivePath);
            List<ZipArchiveEntry> entries = archive.Entries
                .Where(entry => !string.IsNullOrEmpty(entry.FullName) && !entry.FullName.EndsWith("/", StringComparison.Ordinal))
                .ToList();

            if (entries.Count == 0)
            {
                throw new InvalidOperationException(L("这个压缩包里没有可解压的文件。", "This archive does not contain extractable files."));
            }

            for (int index = 0; index < entries.Count; index++)
            {
                ZipArchiveEntry entry = entries[index];
                if (IsZipLinkEntry(entry))
                {
                    throw new InvalidDataException(L("压缩包包含不受支持的符号链接。", "The archive contains an unsupported symbolic link."));
                }

                string destinationPath = GetSafePathInsideDirectory(
                    destinationDirectory,
                    entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                string? destinationFolder = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(destinationFolder))
                {
                    Directory.CreateDirectory(destinationFolder);
                }

                entry.ExtractToFile(destinationPath, true);
                int percent = Math.Min(95, 10 + ((index + 1) * 85 / entries.Count));
                DispatcherQueue.TryEnqueue(() => UpdateProgress(percent, L("正在解压文件...", "Extracting files...")));
            }

            DispatcherQueue.TryEnqueue(() => UpdateProgress(100, L("解压完成", "Extraction complete")));
            return;
        }

        if (ExternalArchiveExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            // Resolve once so inspection and extraction use the same installed tool.
            string? sevenZipPath = FindSevenZipExecutable();
            if (sevenZipPath is not null)
            {
                ValidateSevenZipArchiveEntries(sevenZipPath, archivePath, destinationDirectory);
                RunSevenZipExtraction(sevenZipPath, archivePath, destinationDirectory);
            }
            else
            {
                string? bandizipPath = FindBandizipExecutable();
                if (bandizipPath is null)
                {
                    throw new InvalidOperationException(L(
                        "解压此格式需要 7-Zip 或 Bandizip。请安装其中一个，或把 7z.exe 和 7z.dll 放到程序目录的 Tools 文件夹中。Bandizip 需要附带的 bz.exe。",
                        "This format requires 7-Zip or Bandizip. Install either tool, or place 7z.exe and 7z.dll in the app's Tools folder. Bandizip requires its bundled bz.exe."));
                }

                try
                {
                    BandizipArchiveExtractor.Extract(bandizipPath, archivePath, destinationDirectory, RunArchiveProcessWithTimeout);
                }
                catch (TimeoutException ex) when (ex.Data[BandizipArchiveExtractor.StagingCleanupFailureKey] is Exception cleanupFailure)
                {
                    throw new TimeoutException(ex.Message + L(
                        " 临时压缩包清理也失败了：", " Temporary archive cleanup also failed: ") + cleanupFailure.Message, ex);
                }
                catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    string cleanupDetails = ex.Data[BandizipArchiveExtractor.StagingCleanupFailureKey] is Exception cleanupFailure
                        ? L(" 临时压缩包清理也失败了：", " Temporary archive cleanup also failed: ") + cleanupFailure.Message
                        : string.Empty;
                    throw new InvalidOperationException(L(
                        "Bandizip 解压失败。压缩包必须先通过 Windows tar 的路径和链接检查；不支持检查的格式请尝试安装 7-Zip。详情：",
                        "Bandizip extraction failed. The archive must pass Windows tar path and link checks first; try installing 7-Zip if inspection is unsupported. Details: ") + ex.Message + cleanupDetails, ex);
                }
            }
            InspectDirectoryTreeSafely(destinationDirectory);
            DispatcherQueue.TryEnqueue(() => UpdateProgress(100, L("解压完成", "Extraction complete")));
            return;
        }

        if (TarArchiveExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            ValidateTarArchiveEntries(archivePath, destinationDirectory);
            RunTarExtraction(archivePath, destinationDirectory);
            InspectDirectoryTreeSafely(destinationDirectory);
            DispatcherQueue.TryEnqueue(() => UpdateProgress(100, L("解压完成", "Extraction complete")));
            return;
        }

        ValidateTarArchiveEntries(archivePath, destinationDirectory);
        RunTarExtraction(archivePath, destinationDirectory);
        InspectDirectoryTreeSafely(destinationDirectory);
        DispatcherQueue.TryEnqueue(() => UpdateProgress(100, L("解压完成", "Extraction complete")));
    }

    private static bool IsZipLinkEntry(ZipArchiveEntry entry)
    {
        const int UnixFileTypeMask = 0xF000;
        const int UnixSymbolicLink = 0xA000;
        int unixMode = (entry.ExternalAttributes >> 16) & UnixFileTypeMask;
        return unixMode == UnixSymbolicLink;
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

    private void RunSevenZipExtraction(string sevenZipPath, string archivePath, string destinationDirectory)
    {
        ProcessStartInfo startInfo = CreateSevenZipStartInfo(sevenZipPath, "x", "-y", "-aoa", "-o" + destinationDirectory, archivePath);
        (int exitCode, _, string error) = RunArchiveProcessWithTimeout(startInfo);
        if (exitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                ? L("7-Zip 无法解压当前文件。请检查压缩包是否完整。", "7-Zip could not extract this file. Check whether the archive is valid.")
                : error.Trim());
        }
    }

    private void RunTarExtraction(string archivePath, string destinationDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "tar.exe",
            Arguments = $"-xf \"{archivePath}\" -C \"{destinationDirectory}\"",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };

        (int exitCode, _, string error) = RunArchiveProcessWithTimeout(startInfo);
        if (exitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                ? L("当前系统无法解压该格式，建议尝试 ZIP 或 7Z。", "This system could not extract the selected format. Try ZIP or 7Z instead.")
                : error.Trim());
        }
    }

    private void ValidateSevenZipArchiveEntries(string sevenZipPath, string archivePath, string destinationDirectory)
    {
        ProcessStartInfo startInfo = CreateSevenZipStartInfo(sevenZipPath, "l", "-slt", archivePath);
        (int exitCode, string output, string error) = RunArchiveProcessWithTimeout(startInfo);
        if (exitCode != 0)
        {
            // With encrypted headers 7-Zip cannot even list the archive and only reports
            // that the closed password prompt was interrupted.
            throw new InvalidDataException(L(
                "7-Zip 无法读取压缩包，它可能已损坏或设置了密码（不支持带密码的压缩包）。详情：",
                "7-Zip could not read the archive. It may be damaged or password-protected (password-protected archives are not supported). Details: ")
                + (string.IsNullOrWhiteSpace(error) ? exitCode.ToString(CultureInfo.InvariantCulture) : error.Trim()));
        }

        try
        {
            SevenZipListingValidator.Validate(output, destinationDirectory);
        }
        catch (InvalidDataException ex) when (ArchiveErrors.Is(ex, ArchiveErrorKind.Encrypted))
        {
            throw new InvalidDataException(
                L("压缩包设置了密码，暂不支持导入。请先解压后再导入文件夹。", "The archive is password-protected, which is not supported. Extract it yourself first, then import the folder."),
                ex);
        }
        catch (InvalidDataException ex) when (ArchiveErrors.Is(ex, ArchiveErrorKind.LinkEntry))
        {
            throw new InvalidDataException(
                L("压缩包包含真实的符号链接、硬链接或复制链接，已停止解压。", "The archive contains an actual symbolic, hard, or copy link. Extraction was stopped."),
                ex);
        }
    }

    private void ValidateTarArchiveEntries(string archivePath, string destinationDirectory)
    {
        var listInfo = new ProcessStartInfo
        {
            FileName = "tar.exe",
            Arguments = $"-tf \"{archivePath}\"",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        (int exitCode, string output, string error) = RunArchiveProcessWithTimeout(listInfo);
        if (exitCode != 0)
        {
            throw new InvalidDataException(string.IsNullOrWhiteSpace(error) ? "Unable to inspect the archive." : error.Trim());
        }
        foreach (string entryPath in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            GetSafePathInsideDirectory(destinationDirectory, entryPath.Trim());
        }

        listInfo.Arguments = $"-tvf \"{archivePath}\"";
        (exitCode, output, error) = RunArchiveProcessWithTimeout(listInfo);
        if (exitCode != 0)
        {
            throw new InvalidDataException(string.IsNullOrWhiteSpace(error) ? "Unable to inspect archive links." : error.Trim());
        }
        foreach (string rawLine in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.TrimStart();
            if (line.Length > 0 && (line[0] == 'l' || line[0] == 'h'))
            {
                throw new InvalidDataException(L("压缩包包含不受支持的链接。", "The archive contains an unsupported link."));
            }
        }
    }

    private (int ExitCode, string Output, string Error) RunArchiveProcessWithTimeout(ProcessStartInfo startInfo)
    {
        try
        {
            return ArchiveProcessRunner.Run(startInfo, TimeSpan.FromMinutes(ArchiveToolTimeoutMinutes));
        }
        catch (TimeoutException ex)
        {
            string tool = Path.GetFileName(startInfo.FileName);
            string cleanupDetails = ex.InnerException is null ? string.Empty : L(
                " 未能确认所有子进程已退出。详情：", " Termination of all child processes could not be confirmed. Details: ") + ex.InnerException.Message;
            throw new TimeoutException(L(
                $"{tool} 运行超过 {ArchiveToolTimeoutMinutes} 分钟，任务已停止。请检查压缩包后重试。",
                $"{tool} exceeded the {ArchiveToolTimeoutMinutes}-minute time limit and the task was stopped. Check the archive before retrying.") + cleanupDetails, ex);
        }
    }

    private static ProcessStartInfo CreateSevenZipStartInfo(string sevenZipPath, params string[] arguments)
    {
        // stdin is redirected so ArchiveProcessRunner closes it: an encrypted archive
        // fails instead of waiting for a hidden password prompt until the timeout.
        var startInfo = new ProcessStartInfo
        {
            FileName = sevenZipPath,
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
