using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private const int SwHide = 0;
    private const int SwRestore = 9;
    private const uint WmAppTray = 0x8000 + 73;
    private const uint WmLeftButtonDoubleClick = 0x0203;
    private const uint WmRightButtonUp = 0x0205;
    private const uint NimAdd = 0;
    private const uint NimModify = 1;
    private const uint NimDelete = 2;
    private const uint NifMessage = 1;
    private const uint NifIcon = 2;
    private const uint NifTip = 4;
    private const uint NifInfo = 16;
    private const uint NiifInfo = 1;
    private const uint MfString = 0;
    private const uint MfGrayed = 0x0001;
    private const uint MfSeparator = 0x0800;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmReturnCommand = 0x0100;
    private const uint TrayOpenCommand = 1;
    private const uint TrayExitCommand = 2;
    private const uint TrayAppInfoCommand = 9;
    private const uint TrayRepositoryInfoCommand = 10;
    private const uint TrayModInfoCommand = 11;
    private const uint TraySourceInfoCommand = 12;
    private const uint ImageIcon = 1;
    private const uint LoadFromFile = 0x0010;
    private const uint LoadDefaultSize = 0x0040;

    private nint _trayIconHandle;
    private TraySubclassProc? _traySubclassProc;
    private nint _trayWindowHandle;
    private bool _trayIconAdded;
    private bool _minimizeToTray;
    private bool _isApplyingTraySetting;
    private bool _trayHintShown;
    private string _trayOpenText = "Open";
    private string _trayExitText = "Exit";

    private void InitializeTraySupport()
    {
        try
        {
            _trayWindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
            string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            _trayIconHandle = LoadImage(0, iconPath, ImageIcon, 0, 0, LoadFromFile | LoadDefaultSize);
            if (_trayIconHandle == 0)
            {
                throw new InvalidOperationException("Could not load the application tray icon.");
            }
            _traySubclassProc = TrayWindowSubclass;
            if (!SetWindowSubclass(_trayWindowHandle, _traySubclassProc, 1, 0))
            {
                throw new InvalidOperationException("Could not register the system tray window callback.");
            }

            AppWindow.Changed += OnTrayAppWindowChanged;
            UpdateTrayLanguage();
            SetTrayIconVisibility(_minimizeToTray);
        }
        catch (Exception ex)
        {
            LogApplicationIssue("Initialize system tray", ex);
            _minimizeToTray = false;
        }
    }

    private void OnTrayAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!_minimizeToTray
            || sender.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Minimized })
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            SetTrayIconVisibility(true);
            ShowWindow(_trayWindowHandle, SwHide);
            RefreshTrayStatus();
            if (!_trayHintShown)
            {
                _trayHintShown = true;
                ShowTrayBalloon(
                    L("仍在后台运行", "Still running in the background"),
                    BuildTraySummaryMessage());
            }
        });
    }

    private nint TrayWindowSubclass(nint hwnd, uint message, nuint wParam, nint lParam, nuint subclassId, nuint referenceData)
    {
        if (message == WmAppTray)
        {
            uint mouseMessage = unchecked((uint)lParam.ToInt64());
            if (mouseMessage == WmLeftButtonDoubleClick)
            {
                DispatcherQueue.TryEnqueue(RestoreFromTray);
                return 0;
            }
            if (mouseMessage == WmRightButtonUp)
            {
                ShowTrayMenu();
                return 0;
            }
        }

        return DefSubclassProc(hwnd, message, wParam, lParam);
    }

    private void ShowTrayMenu()
    {
        nint menu = CreatePopupMenu();
        if (menu == 0)
        {
            return;
        }

        try
        {
            AppendMenu(menu, MfString | MfGrayed, TrayAppInfoCommand, BuildTrayVersionLine());
            AppendMenu(menu, MfString | MfGrayed, TrayRepositoryInfoCommand, BuildTrayRepositoryLine());
            AppendMenu(menu, MfString | MfGrayed, TrayModInfoCommand, BuildTrayModLine());
            AppendMenu(menu, MfString | MfGrayed, TraySourceInfoCommand, BuildTraySourceLine());
            AppendMenu(menu, MfSeparator, 0, string.Empty);
            AppendMenu(menu, MfString, TrayOpenCommand, _trayOpenText);
            AppendMenu(menu, MfString, TrayExitCommand, _trayExitText);
            GetCursorPos(out NativePoint point);
            SetForegroundWindow(_trayWindowHandle);
            uint command = TrackPopupMenuEx(
                menu,
                TpmRightButton | TpmReturnCommand,
                point.X,
                point.Y,
                _trayWindowHandle,
                0);
            if (command == TrayOpenCommand)
            {
                DispatcherQueue.TryEnqueue(RestoreFromTray);
            }
            else if (command == TrayExitCommand)
            {
                DispatcherQueue.TryEnqueue(Close);
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private void RestoreFromTray()
    {
        ShowWindow(_trayWindowHandle, SwRestore);
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Restore();
        }
        Activate();
    }

    private void OnMinimizeToTrayToggled(object sender, RoutedEventArgs e)
    {
        if (_isApplyingTraySetting)
        {
            return;
        }

        _minimizeToTray = MinimizeToTrayToggleSwitch.IsOn;
        SetTrayIconVisibility(_minimizeToTray);
        SaveShellConfig();
    }

    private void RefreshTraySetting()
    {
        TraySettingTitleTextBlock.Text = L("最小化到系统托盘", "Minimize to system tray");
        TraySettingDescriptionTextBlock.Text = L(
            "开启后，最小化窗口会继续在后台运行；双击托盘图标恢复。",
            "Keep the app running in the background when minimized; double-click the tray icon to restore it.");
        _isApplyingTraySetting = true;
        MinimizeToTrayToggleSwitch.IsOn = _minimizeToTray;
        MinimizeToTrayToggleSwitch.OnContent = L("开", "On");
        MinimizeToTrayToggleSwitch.OffContent = L("关", "Off");
        _isApplyingTraySetting = false;
    }

    private void UpdateTrayLanguage()
    {
        _trayOpenText = L("打开主窗口", "Open main window");
        _trayExitText = L("退出", "Exit");
        RefreshTrayStatus();
    }

    private void RefreshTrayStatus()
    {
        if (!_trayIconAdded)
        {
            return;
        }

        NotifyIconData data = CreateTrayData(NifTip);
        ShellNotifyIcon(NimModify, ref data);
    }

    private string BuildTrayRepositoryLine()
    {
        WorkspaceRepository? repository = GetSelectedRepository();
        string name = repository?.Name ?? L("未选择", "Not selected");
        return L($"当前仓库：{name}", $"Repository: {name}");
    }

    private string BuildTrayVersionLine()
    {
        return L($"集成化 Mod 管理器 {AppVersion}", $"Integrated Mod Manager {AppVersion}");
    }

    private string BuildTrayModLine()
    {
        WorkspaceRepository? repository = GetSelectedRepository();
        if (repository is null)
        {
            return L("Mod：0 · 待配置", "Mods: 0 · Needs setup");
        }

        RepositorySnapshot snapshot = BuildRepositorySnapshot(repository);
        string state = snapshot.IsReady ? L("就绪", "Ready") : L("待配置", "Needs setup");
        return L($"Mod：{snapshot.ModCount} · {state}", $"Mods: {snapshot.ModCount} · {state}");
    }

    private string BuildTraySourceLine()
    {
        WorkspaceRepository? repository = GetSelectedRepository();
        string source = repository is null ? L("未选择", "Not selected") : GetEffectiveOnlineSource(repository);
        return L($"在线来源：{source}", $"Online source: {source}");
    }

    private string BuildTraySummaryMessage()
    {
        return string.Join(
            Environment.NewLine,
            BuildTrayVersionLine(),
            BuildTrayRepositoryLine(),
            BuildTrayModLine(),
            BuildTraySourceLine(),
            L("双击托盘图标恢复窗口。", "Double-click the tray icon to restore the window."));
    }

    private string BuildTrayTooltip()
    {
        string repository = GetSelectedRepository()?.Name ?? L("未选择", "Not selected");
        string modSummary = BuildTrayModLine();
        string tooltip = L(
            $"集成化 Mod 管理器 {AppVersion} · {repository} · {modSummary}",
            $"Integrated Mod Manager {AppVersion} · {repository} · {modSummary}");
        return tooltip.Length < 128 ? tooltip : tooltip[..127];
    }

    private void SetTrayIconVisibility(bool visible)
    {
        if (_trayWindowHandle == 0 || _trayIconHandle == 0 || visible == _trayIconAdded)
        {
            return;
        }

        NotifyIconData data = CreateTrayData(NifMessage | NifIcon | NifTip);
        if (visible)
        {
            _trayIconAdded = ShellNotifyIcon(NimAdd, ref data);
        }
        else
        {
            ShellNotifyIcon(NimDelete, ref data);
            _trayIconAdded = false;
        }
    }

    private void ShowTrayBalloon(string title, string message)
    {
        if (!_trayIconAdded)
        {
            return;
        }

        NotifyIconData data = CreateTrayData(NifInfo);
        data.InfoTitle = title;
        data.Info = message;
        data.InfoFlags = NiifInfo;
        ShellNotifyIcon(NimModify, ref data);
    }

    private NotifyIconData CreateTrayData(uint flags)
    {
        return new NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NotifyIconData>(),
            WindowHandle = _trayWindowHandle,
            Id = 1,
            Flags = flags,
            CallbackMessage = WmAppTray,
            IconHandle = _trayIconHandle,
            Tip = BuildTrayTooltip(),
            Info = string.Empty,
            InfoTitle = string.Empty
        };
    }

    private void DisposeTraySupport()
    {
        AppWindow.Changed -= OnTrayAppWindowChanged;
        SetTrayIconVisibility(false);
        if (_traySubclassProc is not null && _trayWindowHandle != 0)
        {
            RemoveWindowSubclass(_trayWindowHandle, _traySubclassProc, 1);
        }
        _traySubclassProc = null;
        if (_trayIconHandle != 0)
        {
            DestroyIcon(_trayIconHandle);
            _trayIconHandle = 0;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public nint WindowHandle;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public nint IconHandle;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State;
        public uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags;
        public Guid GuidItem;
        public nint BalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    private delegate nint TraySubclassProc(nint hwnd, uint message, nuint wParam, nint lParam, nuint subclassId, nuint referenceData);

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(nint hwnd, TraySubclassProc callback, nuint subclassId, nuint referenceData);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(nint hwnd, TraySubclassProc callback, nuint subclassId);

    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(nint hwnd, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint hwnd, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(nint menu, uint flags, nuint itemId, string text);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint hwnd, nint parameters);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(nint menu);

    [DllImport("user32.dll", EntryPoint = "LoadImageW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadImage(nint instance, string name, uint type, int width, int height, uint loadFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint icon);
}
