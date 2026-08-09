using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("Integrated Mod Manager Local Update Agent")]
[assembly: AssemblyProduct("Integrated Mod Manager")]
[assembly: AssemblyCopyright("Copyright (c) 2026 uyujkk")]
[assembly: AssemblyVersion("3.3.0.0")]
[assembly: AssemblyFileVersion("3.3.0.0")]
[assembly: AssemblyInformationalVersion("3.3.0")]

internal static class LocalUpdateAgent
{
    private static readonly string[] PreservedRelativePaths =
    {
        "config.ini",
        "beta-shell.json",
        Path.Combine("WinUI3", "config.ini"),
        Path.Combine("WinUI3", "beta-shell.json")
    };

    [STAThread]
    private static int Main(string[] args)
    {
        Dictionary<string, string> options = ParseArguments(args);
        string packagePath;
        string installRoot;
        string launcherPath;
        string processIdText;
        string language;
        string expectedVersion;

        if (!options.TryGetValue("package", out packagePath) ||
            !options.TryGetValue("install-root", out installRoot) ||
            !options.TryGetValue("launcher", out launcherPath) ||
            !options.TryGetValue("process-id", out processIdText))
        {
            return 2;
        }

        options.TryGetValue("language", out language);
        options.TryGetValue("expected-version", out expectedVersion);
        bool english = string.Equals(language, "en", StringComparison.OrdinalIgnoreCase);
        string extractionRoot = Path.Combine(Path.GetTempPath(), "IntegratedModManagerUpdate-" + Guid.NewGuid().ToString("N"));
        string backupRoot = Path.Combine(extractionRoot, "rollback");
        var createdFiles = new List<string>();

        try
        {
            packagePath = Path.GetFullPath(packagePath);
            installRoot = Path.GetFullPath(installRoot);
            launcherPath = Path.GetFullPath(launcherPath);

            if (!File.Exists(packagePath))
            {
                throw new FileNotFoundException("The update package was not found.", packagePath);
            }

            WaitForApplicationExit(processIdText);
            Directory.CreateDirectory(extractionRoot);

            string payloadExtractionRoot = Path.Combine(extractionRoot, "payload");
            Directory.CreateDirectory(payloadExtractionRoot);
            ExtractZipSafely(packagePath, payloadExtractionRoot);

            string payloadRoot = FindPayloadRoot(payloadExtractionRoot);
            ValidatePayload(payloadRoot, expectedVersion);
            BackupFilesThatWillBeReplaced(payloadRoot, installRoot, backupRoot);
            CopyPayload(payloadRoot, installRoot, createdFiles);
            RestorePreservedConfiguration(backupRoot, installRoot);

            File.WriteAllText(
                Path.Combine(installRoot, "update-success.log"),
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine + Path.GetFileName(packagePath));

            Process.Start(new ProcessStartInfo
            {
                FileName = launcherPath,
                WorkingDirectory = installRoot,
                UseShellExecute = true
            });

            return 0;
        }
        catch (Exception ex)
        {
            try
            {
                RollBackFiles(backupRoot, installRoot, createdFiles);
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "IntegratedModManagerUpdate-error.log"), ex.ToString());
            }
            catch
            {
            }

            MessageBox.Show(
                english
                    ? "The local update failed. Your existing configuration was kept.\n\n" + ex.Message
                    : "本地更新失败，现有配置已保留。\n\n" + ex.Message,
                english ? "Update Failed" : "更新失败",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 1;
        }
        finally
        {
            try
            {
                if (Directory.Exists(extractionRoot))
                {
                    Directory.Delete(extractionRoot, true);
                }
            }
            catch
            {
            }
        }
    }

    private static Dictionary<string, string> ParseArguments(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index + 1 < args.Length; index += 2)
        {
            if (args[index].StartsWith("--", StringComparison.Ordinal))
            {
                options[args[index].Substring(2)] = args[index + 1];
            }
        }

        return options;
    }

    private static void WaitForApplicationExit(string processIdText)
    {
        int processId;
        if (!int.TryParse(processIdText, out processId))
        {
            return;
        }

        try
        {
            using (Process process = Process.GetProcessById(processId))
            {
                if (!process.WaitForExit(30000))
                {
                    throw new TimeoutException("The application did not exit within 30 seconds.");
                }
            }
        }
        catch (ArgumentException)
        {
        }
    }

    private static void ExtractZipSafely(string packagePath, string destinationRoot)
    {
        string normalizedRoot = Path.GetFullPath(destinationRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        using (FileStream stream = File.OpenRead(packagePath))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
        {
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                string relativePath = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                string destinationPath = Path.GetFullPath(Path.Combine(destinationRoot, relativePath));
                if (!destinationPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("The update package contains an unsafe path: " + entry.FullName);
                }

                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(destinationPath);
                    continue;
                }

                string destinationDirectory = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(destinationDirectory))
                {
                    Directory.CreateDirectory(destinationDirectory);
                }

                using (Stream input = entry.Open())
                using (FileStream output = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    input.CopyTo(output);
                }
            }
        }
    }

    private static string FindPayloadRoot(string extractionRoot)
    {
        if (IsPayloadRoot(extractionRoot))
        {
            return extractionRoot;
        }

        string[] candidates = Directory.GetDirectories(extractionRoot, "*", SearchOption.AllDirectories)
            .Where(IsPayloadRoot)
            .OrderBy(path => path.Count(character => character == Path.DirectorySeparatorChar))
            .ToArray();

        if (candidates.Length == 0)
        {
            throw new InvalidDataException("The ZIP is not an Integrated Mod Manager release package.");
        }

        return candidates[0];
    }

    private static bool IsPayloadRoot(string path)
    {
        return File.Exists(Path.Combine(path, "ModFolderCopier.exe")) &&
               File.Exists(Path.Combine(path, "WinUI3", "ModFolderCopier.WinUI.exe"));
    }

    private static void ValidatePayload(string payloadRoot, string expectedVersionText)
    {
        var launcherInfo = new FileInfo(Path.Combine(payloadRoot, "ModFolderCopier.exe"));
        string runtimePath = Path.Combine(payloadRoot, "WinUI3", "ModFolderCopier.WinUI.exe");
        var runtimeInfo = new FileInfo(runtimePath);
        if (launcherInfo.Length == 0 || runtimeInfo.Length == 0)
        {
            throw new InvalidDataException("The update package contains an empty executable.");
        }

        Version expectedVersion;
        Version payloadVersion;
        string payloadVersionText = FileVersionInfo.GetVersionInfo(runtimePath).FileVersion;
        if (!string.IsNullOrWhiteSpace(expectedVersionText) &&
            (!Version.TryParse(expectedVersionText, out expectedVersion) ||
             !Version.TryParse(payloadVersionText, out payloadVersion) ||
             expectedVersion.Major != payloadVersion.Major ||
             expectedVersion.Minor != payloadVersion.Minor ||
             expectedVersion.Build != payloadVersion.Build))
        {
            throw new InvalidDataException("The version in the ZIP filename does not match the application files inside it.");
        }
    }

    private static void BackupFilesThatWillBeReplaced(string payloadRoot, string installRoot, string backupRoot)
    {
        foreach (string preservedPath in PreservedRelativePaths)
        {
            BackupFileIfPresent(installRoot, backupRoot, preservedPath);
        }

        foreach (string sourceFile in Directory.GetFiles(payloadRoot, "*", SearchOption.AllDirectories))
        {
            string relativePath = GetRelativePath(payloadRoot, sourceFile);
            if (IsPreserved(relativePath))
            {
                continue;
            }

            BackupFileIfPresent(installRoot, backupRoot, relativePath);
        }
    }

    private static void BackupFileIfPresent(string installRoot, string backupRoot, string relativePath)
    {
        string installedFile = Path.Combine(installRoot, relativePath);
        if (!File.Exists(installedFile))
        {
            return;
        }

        string backupFile = Path.Combine(backupRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(backupFile));
        File.Copy(installedFile, backupFile, true);
    }

    private static void CopyPayload(string payloadRoot, string installRoot, List<string> createdFiles)
    {
        foreach (string sourceFile in Directory.GetFiles(payloadRoot, "*", SearchOption.AllDirectories))
        {
            string relativePath = GetRelativePath(payloadRoot, sourceFile);
            if (IsPreserved(relativePath))
            {
                continue;
            }

            string destinationFile = Path.Combine(installRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile));
            if (!File.Exists(destinationFile))
            {
                createdFiles.Add(destinationFile);
            }

            File.Copy(sourceFile, destinationFile, true);
        }
    }

    private static void RestorePreservedConfiguration(string backupRoot, string installRoot)
    {
        foreach (string preservedPath in PreservedRelativePaths)
        {
            string backupFile = Path.Combine(backupRoot, preservedPath);
            if (!File.Exists(backupFile))
            {
                continue;
            }

            string destinationFile = Path.Combine(installRoot, preservedPath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile));
            File.Copy(backupFile, destinationFile, true);
        }
    }

    private static void RollBackFiles(string backupRoot, string installRoot, List<string> createdFiles)
    {
        foreach (string createdFile in createdFiles)
        {
            if (File.Exists(createdFile))
            {
                File.Delete(createdFile);
            }
        }

        if (!Directory.Exists(backupRoot))
        {
            return;
        }

        foreach (string backupFile in Directory.GetFiles(backupRoot, "*", SearchOption.AllDirectories))
        {
            string relativePath = GetRelativePath(backupRoot, backupFile);
            string destinationFile = Path.Combine(installRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile));
            File.Copy(backupFile, destinationFile, true);
        }
    }

    private static bool IsPreserved(string relativePath)
    {
        return PreservedRelativePaths.Any(path => string.Equals(path, relativePath, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetRelativePath(string root, string path)
    {
        string normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string normalizedPath = Path.GetFullPath(path);
        if (!normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Path is outside the expected root.");
        }

        return normalizedPath.Substring(normalizedRoot.Length);
    }
}
