// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 EndfieldBridge contributors and Integrated Mod Manager contributors.
// Launch-core approach and quoting structure adapted after reviewing endfield-nolauncher
// (fcda770), whose upstream is Better-Endfield (514f6d6). Both declare AGPL-3.0.
// Modified 2026-10-21: integrated into Integrated Mod Manager; the official signature is
// verified once while its file handle is held; see NOTICE.md.
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace IntegratedModManager.EndfieldLaunch;

internal static class NativeMethods
{
    internal const uint CreateSuspended = 0x00000004;
    internal const uint CreateNoWindow = 0x08000000;
    internal const uint ExtendedStartupInfoPresent = 0x00080000;
    internal const nuint ProcThreadAttributeParentProcess = 0x00020000;
    internal const uint WaitTimeout = 258;
    internal const uint Infinite = uint.MaxValue;

    [StructLayout(LayoutKind.Sequential)]
    internal struct StartupInfo
    {
        public uint cb; public IntPtr reserved, desktop, title;
        public uint x, y, cx, cy, xCount, yCount, fill, flags;
        public ushort show, reserved2; public IntPtr reservedPtr, input, output, error;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct StartupInfoEx { public StartupInfo si; public IntPtr attributes; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ProcessInformation { public IntPtr process, thread; public uint pid, tid; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct TrustFileInfo { public uint size; [MarshalAs(UnmanagedType.LPWStr)] public string path; public IntPtr file, subject; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TrustData
    {
        public uint size; public IntPtr policy, sip; public uint ui, revocation, choice; public IntPtr info;
        public uint action; public IntPtr state, url; public uint flags, context;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ProcessBasicInformation { public IntPtr reserved1, peb, reserved2a, reserved2b, pid, parent; }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CreateProcessW(string application, StringBuilder command, IntPtr processAttributes,
        IntPtr threadAttributes, bool inheritHandles, uint flags, IntPtr environment, string currentDirectory,
        ref StartupInfoEx startup, out ProcessInformation information);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, uint flags, ref nuint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, nuint attribute, IntPtr value,
        nuint size, IntPtr previous, IntPtr returned);

    [DllImport("kernel32.dll")]
    internal static extern void DeleteProcThreadAttributeList(IntPtr list);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool TerminateProcess(IntPtr process, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);

    [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    internal static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);

    [DllImport("ntdll.dll")]
    internal static extern int NtQueryInformationProcess(IntPtr process, int kind, out ProcessBasicInformation information,
        int size, out int returned);

    internal static EndfieldLaunchException Error(string zh, string en)
    {
        string detail = new Win32Exception(Marshal.GetLastWin32Error()).Message;
        return new EndfieldLaunchException($"{zh}：{detail}", $"{en}: {detail}");
    }

    /// <summary>The parent process id recorded by Windows for a process handle.</summary>
    internal static long GetParentProcessId(IntPtr process)
    {
        int status = NtQueryInformationProcess(process, 0, out ProcessBasicInformation information,
            Marshal.SizeOf<ProcessBasicInformation>(), out _);
        return status == 0 ? information.parent.ToInt64() : -1;
    }
}

public static class CommandLine
{
    /// <summary>Quotes one argument for CommandLineToArgvW / the MSVC runtime parser.</summary>
    public static string Quote(string value)
    {
        var builder = new StringBuilder("\"");
        int slashes = 0;
        foreach (char c in value)
        {
            if (c == '\\')
            {
                slashes++;
                continue;
            }

            if (c == '"') builder.Append('\\', slashes * 2 + 1).Append('"');
            else builder.Append('\\', slashes).Append(c);
            slashes = 0;
        }

        return builder.Append('\\', slashes * 2).Append('"').ToString();
    }
}

public static class OfficialLauncher
{
    internal const string ExpectedOrganization = "Shanghai Hypergryph Network Technology Co., Ltd.";
    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");

    /// <summary>
    /// Verifies the Authenticode signature and signer organization of an official Games.exe.
    /// Returns the signer's simple name.
    /// </summary>
    public static string Verify(string path)
    {
        EnsureCandidate(path);

        var file = new NativeMethods.TrustFileInfo { size = (uint)Marshal.SizeOf<NativeMethods.TrustFileInfo>(), path = path };
        IntPtr memory = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.TrustFileInfo>());
        Marshal.StructureToPtr(file, memory, false);
        Guid policy = GenericVerifyV2;
        // UI none, no revocation lookup, file choice, verify action, cache-only URL retrieval.
        var data = new NativeMethods.TrustData
        {
            size = (uint)Marshal.SizeOf<NativeMethods.TrustData>(), ui = 2, choice = 1, info = memory, action = 1, flags = 0x1000
        };
        try
        {
            int result = NativeMethods.WinVerifyTrust(new IntPtr(-1), ref policy, ref data);
            if (result != 0)
            {
                string code = $"0x{unchecked((uint)result):X8}";
                throw new OfficialSignatureException(
                    $"官方文件签名验证未通过：{code}。请用官方启动器修复或更新文件。",
                    $"Official file signature verification failed: {code}. Repair or update this file with the official launcher.");
            }

#pragma warning disable SYSLIB0057
            using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
            if (!HasOrganization(certificate.SubjectName.Decode(X500DistinguishedNameFlags.UseNewLines), ExpectedOrganization))
            {
                throw new OfficialSignatureException("签名有效，但签名组织不是预期的鹰角官方组织。",
                    "The signature is valid, but the signer is not the expected Hypergryph organization.");
            }

            return certificate.GetNameInfo(X509NameType.SimpleName, false);
        }
        finally
        {
            data.action = 2; // close the verification state
            NativeMethods.WinVerifyTrust(new IntPtr(-1), ref policy, ref data);
            Marshal.DestroyStructure<NativeMethods.TrustFileInfo>(memory);
            Marshal.FreeHGlobal(memory);
        }
    }

    internal static void EnsureCandidate(string path)
    {
        if (!File.Exists(path) || !Path.GetFileName(path).Equals("Games.exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new OfficialSignatureException("父进程必须是实际存在的官方 Games.exe。", "The parent must be an existing official Games.exe.");
        }
    }

    internal static bool HasOrganization(string distinguishedName, string organization) =>
        distinguishedName.Split('\n')
            .Select(part => part.Trim())
            .Any(part => part.StartsWith("O=", StringComparison.Ordinal)
                && part[2..].Trim('"').Equals(organization, StringComparison.OrdinalIgnoreCase));
}

internal sealed class OwnedHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public OwnedHandle(IntPtr value) : base(true) => SetHandle(value);

    protected override bool ReleaseHandle() => NativeMethods.CloseHandle(handle);
}

/// <summary>
/// A suspended official parent process plus the target created under it. Disposing
/// terminates only the suspended parent this session created, never the target.
/// </summary>
public sealed class LaunchSession : IDisposable
{
    private OwnedHandle? _parent;
    private OwnedHandle? _target;
    private readonly Action<LaunchText> _log;

    private LaunchSession(Action<LaunchText> log) => _log = log;

    public uint ParentId { get; private set; }

    public uint TargetId { get; private set; }

    /// <summary>Verifies <paramref name="parentExe"/> while holding it open, then creates the target under it.</summary>
    public static LaunchSession Create(string parentExe, string target, string arguments, Action<LaunchText> log) =>
        Create(parentExe, target, arguments, log, verifyParent: true);

    // extraTargetFlags exists for tests: a console target (cmd.exe) cannot attach to the console of
    // a parent that is still suspended, so tests give it its own hidden console. Game launches pass none.
    internal static LaunchSession Create(string parentExe, string target, string arguments, Action<LaunchText> log, bool verifyParent,
        uint extraTargetFlags = 0)
    {
        var session = new LaunchSession(log);
        try
        {
            if (verifyParent) OfficialLauncher.EnsureCandidate(parentExe);
            // Holding a read handle without write sharing keeps the verified file from being
            // replaced between verification and process creation.
            using FileStream held = File.Open(parentExe, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (verifyParent)
            {
                string signer = OfficialLauncher.Verify(parentExe);
                log(new LaunchText("官方签名通过：" + signer, "Official signature verified: " + signer));
            }

            var startup = new NativeMethods.StartupInfoEx { si = new NativeMethods.StartupInfo { cb = (uint)Marshal.SizeOf<NativeMethods.StartupInfo>() } };
            if (!NativeMethods.CreateProcessW(parentExe, new StringBuilder(CommandLine.Quote(parentExe)), IntPtr.Zero, IntPtr.Zero, false,
                    NativeMethods.CreateSuspended | NativeMethods.CreateNoWindow, IntPtr.Zero, Path.GetDirectoryName(parentExe)!, ref startup,
                    out NativeMethods.ProcessInformation parentInfo))
            {
                throw NativeMethods.Error("创建挂起官方父进程失败", "Failed to create the suspended official parent");
            }

            session._parent = new OwnedHandle(parentInfo.process);
            using (new OwnedHandle(parentInfo.thread))
            {
            }

            session.ParentId = parentInfo.pid;
            log(new LaunchText($"已创建挂起父进程，PID {parentInfo.pid}", $"Suspended parent created, PID {parentInfo.pid}"));

            nuint bytes = 0;
            NativeMethods.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref bytes);
            if (bytes == 0) throw NativeMethods.Error("查询进程属性空间失败", "Failed to query process attribute storage");
            IntPtr list = Marshal.AllocHGlobal(checked((int)bytes));
            IntPtr value = Marshal.AllocHGlobal(IntPtr.Size);
            bool initialized = false;
            try
            {
                if (!NativeMethods.InitializeProcThreadAttributeList(list, 1, 0, ref bytes))
                    throw NativeMethods.Error("初始化进程属性失败", "Failed to initialize process attributes");
                initialized = true;
                Marshal.WriteIntPtr(value, session._parent.DangerousGetHandle());
                if (!NativeMethods.UpdateProcThreadAttribute(list, 0, NativeMethods.ProcThreadAttributeParentProcess, value,
                        (nuint)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
                {
                    throw NativeMethods.Error("设置父进程属性失败", "Failed to set the parent process attribute");
                }

                var extended = new NativeMethods.StartupInfoEx
                {
                    si = new NativeMethods.StartupInfo { cb = (uint)Marshal.SizeOf<NativeMethods.StartupInfoEx>() },
                    attributes = list
                };
                var command = new StringBuilder(CommandLine.Quote(target) + (string.IsNullOrWhiteSpace(arguments) ? string.Empty : " " + arguments));
                if (command.Length >= 32767) throw new EndfieldLaunchException("游戏启动参数过长。", "Game arguments are too long.");
                if (!NativeMethods.CreateProcessW(target, command, IntPtr.Zero, IntPtr.Zero, false, NativeMethods.ExtendedStartupInfoPresent | extraTargetFlags,
                        IntPtr.Zero, Path.GetDirectoryName(target)!, ref extended, out NativeMethods.ProcessInformation child))
                {
                    throw NativeMethods.Error("创建目标进程失败", "Failed to create the target process");
                }

                session._target = new OwnedHandle(child.process);
                using (new OwnedHandle(child.thread))
                {
                }

                session.TargetId = child.pid;
                log(new LaunchText($"目标进程已创建，PID {child.pid}；这不代表游戏认证或 Mod 加载成功。",
                    $"Target process created, PID {child.pid}; this does not confirm game authentication or Mod loading."));
            }
            finally
            {
                if (initialized) NativeMethods.DeleteProcThreadAttributeList(list);
                Marshal.FreeHGlobal(value);
                Marshal.FreeHGlobal(list);
            }

            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    internal long TargetParentId => _target is null ? -1 : NativeMethods.GetParentProcessId(_target.DangerousGetHandle());

    public int Wait()
    {
        if (_target is null) throw new InvalidOperationException("No target process.");
        uint status = NativeMethods.WaitForSingleObject(_target.DangerousGetHandle(), NativeMethods.Infinite);
        if (status != 0) throw NativeMethods.Error("等待目标退出失败", "Failed to wait for the target process");
        if (!NativeMethods.GetExitCodeProcess(_target.DangerousGetHandle(), out uint code))
            throw NativeMethods.Error("读取退出码失败", "Failed to read the exit code");
        return unchecked((int)code);
    }

    public void Dispose()
    {
        // Only terminate the suspended helper this session owns. Never terminate the game or other launchers.
        if (_parent is { IsClosed: false })
        {
            if (NativeMethods.WaitForSingleObject(_parent.DangerousGetHandle(), 0) == NativeMethods.WaitTimeout)
            {
                if (!NativeMethods.TerminateProcess(_parent.DangerousGetHandle(), 0))
                    _log(new LaunchText("警告：挂起父进程清理失败，PID " + ParentId, "Warning: failed to clean up the suspended parent, PID " + ParentId));
                else
                    NativeMethods.WaitForSingleObject(_parent.DangerousGetHandle(), 5000);
            }

            _parent.Dispose();
            _parent = null;
        }

        _target?.Dispose();
        _target = null;
    }
}
