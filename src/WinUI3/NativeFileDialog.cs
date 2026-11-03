using System.Runtime.InteropServices;

namespace ModFolderCopier.WinUI;

/// <summary>
/// The system open dialog through its COM interface. The WinRT pickers fail in an elevated process,
/// and the manager runs elevated whenever it has to send input to an elevated game.
/// </summary>
internal static class NativeFileDialog
{
    private const int Cancelled = unchecked((int)0x800704C7);
    private const uint NoChangeDirectory = 0x8, PickFolders = 0x20, ForceFileSystem = 0x40, PathMustExist = 0x800, FileMustExist = 0x1000;
    private const uint FileSystemPath = 0x80058000;

    public static string? PickFile(nint owner, IReadOnlyList<string> extensions)
    {
        string pattern = string.Join(";", extensions.Select(extension => "*" + extension));
        return Show(owner, ForceFileSystem | NoChangeDirectory | PathMustExist | FileMustExist, pattern);
    }

    public static string? PickFolder(nint owner) => Show(owner, ForceFileSystem | NoChangeDirectory | PathMustExist | PickFolders, null);

    private static string? Show(nint owner, uint options, string? pattern)
    {
        var dialog = (IFileOpenDialog)new FileOpenDialogClass();
        try
        {
            dialog.SetOptions(options);
            if (pattern is not null) dialog.SetFileTypes(1, [new FilterSpec { Name = pattern, Pattern = pattern }]);
            int result = dialog.Show(owner);
            if (result == Cancelled) return null;
            Marshal.ThrowExceptionForHR(result);
            dialog.GetResult(out IShellItem item);
            try
            {
                item.GetDisplayName(FileSystemPath, out nint path);
                try { return Marshal.PtrToStringUni(path); }
                finally { Marshal.FreeCoTaskMem(path); }
            }
            finally { Marshal.ReleaseComObject(item); }
        }
        finally { Marshal.ReleaseComObject(dialog); }
    }

    [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
    private class FileOpenDialogClass;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FilterSpec
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string Name;
        [MarshalAs(UnmanagedType.LPWStr)] public string Pattern;
    }

    [ComImport, Guid("d57c7288-d4ad-4768-be02-9d969532d960"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOpenDialog
    {
        [PreserveSig] int Show(nint owner);
        void SetFileTypes(uint count, [MarshalAs(UnmanagedType.LPArray)] FilterSpec[] types);
        void SetFileTypeIndex(uint index);
        void GetFileTypeIndex(out uint index);
        void Advise(nint events, out uint cookie);
        void Unadvise(uint cookie);
        void SetOptions(uint options);
        void GetOptions(out uint options);
        void SetDefaultFolder(IShellItem item);
        void SetFolder(IShellItem item);
        void GetFolder(out IShellItem item);
        void GetCurrentSelection(out IShellItem item);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetFileName(out nint name);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
        void GetResult(out IShellItem item);
    }

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(nint context, in Guid handler, in Guid interfaceId, out nint result);
        void GetParent(out IShellItem parent);
        void GetDisplayName(uint kind, out nint name);
    }
}
