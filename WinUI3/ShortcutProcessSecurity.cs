using System.Runtime.InteropServices;

namespace ModFolderCopier.WinUI;

internal sealed record ShortcutSecurity(int? Integrity, bool UiAccess, int Error)
{
    public string Summary => Integrity is int level ? $"0x{level:X}/UIAccess={UiAccess}" : $"unknown/error={Error}";
}

internal static class ShortcutProcessSecurity
{
    private static readonly Lazy<ShortcutSecurity> current = new(() => Read((uint)Environment.ProcessId));

    public static ShortcutSecurity Current => current.Value;

    public static ShortcutSecurity Read(uint pid)
    {
        nint process = OpenProcess(0x1000, false, pid), token = 0;
        if (process == 0) return new(null, false, Marshal.GetLastWin32Error());
        try
        {
            if (!OpenProcessToken(process, 8, out token)) return new(null, false, Marshal.GetLastWin32Error());
            GetTokenInformation(token, 25, 0, 0, out int bytes);
            if (bytes <= 0 || bytes > 65536) return new(null, false, Marshal.GetLastWin32Error());
            nint buffer = Marshal.AllocHGlobal(bytes);
            try
            {
                if (!GetTokenInformation(token, 25, buffer, bytes, out _)) return new(null, false, Marshal.GetLastWin32Error());
                nint sid = Marshal.ReadIntPtr(buffer);
                byte count = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
                if (count == 0) return new(null, false, 1337);
                int level = Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)(count - 1)));
                bool uiAccess = GetTokenInformation(token, 26, buffer, bytes, out _) && Marshal.ReadInt32(buffer) != 0;
                return new(level, uiAccess, 0);
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        finally { if (token != 0) CloseHandle(token); CloseHandle(process); }
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(nint process, uint access, out nint token);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(nint token, int kind, nint info, int length, out int needed);
    [DllImport("advapi32.dll")] private static extern nint GetSidSubAuthorityCount(nint sid);
    [DllImport("advapi32.dll")] private static extern nint GetSidSubAuthority(nint sid, uint index);
}
