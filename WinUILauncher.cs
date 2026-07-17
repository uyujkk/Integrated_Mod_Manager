using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

[assembly: AssemblyTitle("集成化mod管理器")]
[assembly: AssemblyProduct("集成化mod管理器")]
[assembly: AssemblyCopyright("Copyright (c) 2026 uyujkk")]
[assembly: AssemblyVersion("3.1.0.0")]
[assembly: AssemblyFileVersion("3.1.0.0")]
[assembly: AssemblyInformationalVersion("3.1.0")]

internal static class WinUILauncher
{
    [STAThread]
    private static void Main()
    {
        string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        string runtimeDirectory = Path.Combine(baseDirectory, "WinUI3");
        string targetExe = Path.Combine(runtimeDirectory, "ModFolderCopier.WinUI.exe");

        if (!File.Exists(targetExe))
        {
            MessageBox.Show(
                "未找到 WinUI 3 运行文件：\n" + targetExe,
                "启动失败",
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
                throw new InvalidOperationException("WinUI 3 程序没有成功启动。");
            }
        }
        catch (Win32Exception ex)
        {
            if (ex.NativeErrorCode == 1223)
            {
                MessageBox.Show(
                    "启动 WinUI 3 版本时被 Windows 取消。\n\n" +
                    "请尝试：\n" +
                    "1. 先把压缩包完整解压到普通文件夹\n" +
                    "2. 右键压缩包或 EXE，检查属性里是否有“解除锁定”\n" +
                    "3. 确认安全软件或系统弹窗没有拦截程序\n\n" +
                    "详细信息：" + ex.Message,
                    "启动失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            MessageBox.Show(
                "启动 WinUI 3 版本失败：\n" + ex.Message,
                "启动失败",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "启动 WinUI 3 版本失败：\n" + ex.Message,
                "启动失败",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
