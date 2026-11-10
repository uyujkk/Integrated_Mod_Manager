using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

[assembly: AssemblyTitle("Integrated Mod Manager")]
[assembly: AssemblyProduct("Integrated Mod Manager")]
[assembly: AssemblyCopyright("Copyright (c) 2026 uyujkk")]
[assembly: AssemblyVersion("4.8.0.0")]
[assembly: AssemblyFileVersion("4.8.0.0")]
[assembly: AssemblyInformationalVersion("4.8.0")]

internal static class WinUILauncher
{
    [STAThread]
    private static void Main()
    {
        string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        string runtimeDirectory = Path.Combine(baseDirectory, "WinUI3");
        string targetExe = Path.Combine(runtimeDirectory, "ModFolderCopier.WinUI.exe");
        bool chinese = UsesChinese(runtimeDirectory);
        string failedTitle = chinese ? "启动失败" : "Could Not Start";

        if (!File.Exists(targetExe))
        {
            MessageBox.Show(
                (chinese ? "未找到 WinUI 3 运行文件：\n" : "The WinUI 3 program file was not found:\n") + targetExe,
                failedTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = targetExe,
                WorkingDirectory = runtimeDirectory,
                UseShellExecute = false
            };

            Process process = Process.Start(startInfo);
            if (process == null)
            {
                throw new InvalidOperationException(chinese ? "WinUI 3 程序没有成功启动。" : "The WinUI 3 program did not start.");
            }
        }
        catch (Win32Exception ex)
        {
            if (ex.NativeErrorCode == 1223)
            {
                MessageBox.Show(
                    (chinese
                        ? "启动 WinUI 3 版本时被 Windows 取消。\n\n" +
                          "请尝试：\n" +
                          "1. 先把压缩包完整解压到普通文件夹\n" +
                          "2. 右键压缩包或 EXE，检查属性里是否有“解除锁定”\n" +
                          "3. 确认安全软件或系统弹窗没有拦截程序\n\n" +
                          "详细信息："
                        : "Windows canceled starting the WinUI 3 version.\n\n" +
                          "Try this:\n" +
                          "1. Extract the whole archive to a normal folder first\n" +
                          "2. Right-click the archive or EXE and check Properties for \"Unblock\"\n" +
                          "3. Make sure security software or a system prompt did not block the program\n\n" +
                          "Details: ") + ex.Message,
                    failedTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            MessageBox.Show(
                (chinese ? "启动 WinUI 3 版本失败：\n" : "Could not start the WinUI 3 version:\n") + ex.Message,
                failedTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                (chinese ? "启动 WinUI 3 版本失败：\n" : "Could not start the WinUI 3 version:\n") + ex.Message,
                failedTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    // The app's saved language (config.ini, base64 "language=" field): Chinese only for "zh".
    // Before the first start there is no config, so the system language decides.
    private static bool UsesChinese(string runtimeDirectory)
    {
        try
        {
            string config = Path.Combine(runtimeDirectory, "config.ini");
            if (File.Exists(config))
            {
                foreach (string line in File.ReadAllLines(config))
                {
                    if (!line.StartsWith("language=", StringComparison.Ordinal)) continue;
                    string value = Encoding.UTF8.GetString(Convert.FromBase64String(line.Substring("language=".Length).Trim()));
                    return string.Equals(value, "zh", StringComparison.OrdinalIgnoreCase);
                }
            }
        }
        catch (Exception)
        {
            // An unreadable config falls back to the system language.
        }

        return CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
    }
}
