[CmdletBinding()]
param(
    # A folder with WinUI3\ModFolderCopier.WinUI.exe; by default the runtime scripts\test-all.ps1 staged.
    [string]$Application = 'artifacts\verification\ReleaseSource',
    # Where the pictures go. Under artifacts\, which git ignores.
    [string]$OutputDirectory = '',
    [string[]]$Languages = @('zh-CN', 'en-US', 'ru-RU'),
    # Outer window sizes in device-independent pixels (the same sizes the UI tests use).
    [string[]]$Sizes = @('1400x900', '1000x800'),
    [string[]]$Themes = @('light', 'dark'),
    # updates-downloads and updates-recovery are the Updates page's other two views (stage J3-b).
    [string[]]$Pages = @('dashboard', 'repository', 'online', 'shortcuts', 'updates', 'updates-downloads', 'updates-recovery', 'settings', 'whatsnew', 'firstrun'),
    # How long to wait after the window appears before taking the picture.
    [int]$SettleSeconds = 7,
    # Point the repository's game folder at a folder that does not exist, to show the "setup needed" states.
    [switch]$MissingGameFolder,
    # Add the synthetic tracked Mods, download history and install backups of tests\TestData\SampleState.
    [switch]$SampleData
)

# Baseline screenshots for stage J (docs/development/UI-Baseline-Screenshots.md).
# Each picture comes from a private copy of the built app in %TEMP%, configured with the synthetic
# tests\TestData, so the user's own Mods, settings and system settings are never touched. The theme
# and language are the app's own settings; the system theme and wallpaper are only recorded.
# Pictures are taken from the screen (BitBlt), so leave the desktop unlocked and the mouse still.
$ErrorActionPreference = 'Stop'
# "powershell -File" passes "-Pages a,b" as one string; accept both forms.
$Languages = @($Languages | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
$Sizes = @($Sizes | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
$Themes = @($Themes | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
$Pages = @($Pages | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$app = [IO.Path]::GetFullPath((Join-Path $root $Application))
$exeSource = Join-Path $app 'WinUI3\ModFolderCopier.WinUI.exe'
if (!(Test-Path -LiteralPath $exeSource)) { throw "No built application in $app. Run scripts\test-all.ps1 first." }
if (!$OutputDirectory) { $OutputDirectory = Join-Path 'artifacts\ui-baseline' (Get-Date -Format 'yyyyMMdd-HHmmss') }
$output = [IO.Path]::GetFullPath((Join-Path $root $OutputDirectory))
New-Item -ItemType Directory -Path $output -Force | Out-Null

Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class ImmCapture
{
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] static extern uint GetDpiForSystem();
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out Rect rect, int size);

    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }

    public static void UsePhysicalPixels() { SetProcessDpiAwarenessContext(new IntPtr(-4)); }
    public static uint SystemDpi() { return GetDpiForSystem(); }

    public static void Place(IntPtr window, int x, int y, int width, int height)
    {
        ShowWindow(window, 9); // SW_RESTORE
        // Topmost for the picture, so nothing else covers it even without foreground rights.
        SetWindowPos(window, new IntPtr(-1), x, y, width, height, 0x0040);
        SetForegroundWindow(window);
    }

    public static void Release(IntPtr window) { SetWindowPos(window, new IntPtr(-2), 0, 0, 0, 0, 0x0013); }

    public static Rect Bounds(IntPtr window)
    {
        Rect rect;
        // DWMWA_EXTENDED_FRAME_BOUNDS: the visible frame, without the invisible resize border.
        if (DwmGetWindowAttribute(window, 9, out rect, Marshal.SizeOf(typeof(Rect))) != 0) GetWindowRect(window, out rect);
        return rect;
    }

    public static string Save(IntPtr window, string path)
    {
        Rect r = Bounds(window);
        int width = r.Right - r.Left, height = r.Bottom - r.Top;
        using (var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
        {
            using (Graphics graphics = Graphics.FromImage(bitmap))
                graphics.CopyFromScreen(r.Left, r.Top, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
            bitmap.Save(path, ImageFormat.Png);
        }
        return width + "x" + height;
    }
}
'@
[ImmCapture]::UsePhysicalPixels()
$scale = [ImmCapture]::SystemDpi() / 96.0

$whatsNewSet = [regex]::Match((Get-Content -Raw (Join-Path $root 'src\WinUI3\MainWindow.WhatsNew.cs')), 'WhatsNewFeatureSet = "([^"]+)"').Groups[1].Value
function Encode([string]$value) { [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($value)) }
function ConfigLanguage([string]$language) { switch ($language) { 'zh-CN' { 'zh' } 'en-US' { 'en' } default { $language } } }

# One private copy of the app and the test data for the whole run. The folder name is fixed because
# the dashboard shows the repository paths: a random name would differ in every picture set.
$work = Join-Path ([IO.Path]::GetTempPath()) 'imm-baseline'
if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
$appFolder = Join-Path $work 'WinUI3'
$testData = Join-Path $work 'TestData'
Copy-Item -LiteralPath (Join-Path $app 'WinUI3') -Destination $appFolder -Recurse
Copy-Item -LiteralPath (Join-Path $root 'tests\TestData') -Destination $testData -Recurse
$exe = Join-Path $appFolder 'ModFolderCopier.WinUI.exe'

function Reset-State {
    foreach ($name in 'beta-shell.json', 'config.ini', 'startup.log', 'app-errors.log') {
        $path = Join-Path $appFolder $name
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
    }
}

function Write-Configuration([string]$page, [string]$language, [string]$theme, [int]$width, [int]$height) {
    $mods = Join-Path $testData 'ModRepository'
    $target = Join-Path $testData $(if ($MissingGameFolder) { 'Loader\NoSuchFolder' } else { 'Loader\Mods' })
    $shell = [ordered]@{
        SelectedRepositoryId = 'ui-repository'
        CurrentPrimarySection = $(if ($page -eq 'whatsnew') { 'dashboard' } elseif ($page -like 'updates-*') { 'updates' } else { $page })
        UpdatesView = $(if ($page -like 'updates-*') { $page.Substring('updates-'.Length) } else { 'mods' })
        CheckAppUpdatesOnStartup = $false
        WhatsNewSeenVersion = $(if ($page -eq 'whatsnew') { '' } else { $whatsNewSet })
        RepositoryGamesExplicit = $true
        ThemePreference = $theme
        WindowX = 40; WindowY = 40; WindowWidth = $width; WindowHeight = $height; WindowMaximized = $false
        Repositories = @([ordered]@{
            Id = 'ui-repository'; Name = 'UI repository'; SourcePath = $mods; TargetPath = $target
            PersistentUserIniPath = (Join-Path $testData 'Loader\d3dx_user.ini'); OnlineSourceSite = 'GameBanana'
        })
    }
    [IO.File]::WriteAllText((Join-Path $appFolder 'beta-shell.json'), ($shell | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllLines((Join-Path $appFolder 'config.ini'), [string[]]@(
        "source_dir=$(Encode $mods)", "target_dir=$(Encode $target)", 'launcher_path=',
        "theme=$(Encode $theme)", "language=$(Encode (ConfigLanguage $language))"))
    if ($SampleData) { Add-SampleData }
}

# tests\TestData\SampleState: synthetic tracked Mods, download history and install backups (see its README).
function Add-SampleData {
    $sample = Join-Path $testData 'SampleState'
    $tab = "`t"
    $lines = foreach ($mod in (Get-Content -Raw (Join-Path $sample 'tracked-mods.json') | ConvertFrom-Json)) {
        $fields = @((Join-Path $testData ($mod.Path -replace '/', '\')), 'GameBanana', [string]$mod.ItemId, $mod.Title, $mod.ProfileUrl, '', $mod.LastKnownUpdatedAt, '', '')
        'mod_origin=' + (($fields | ForEach-Object { Encode $_ }) -join $tab)
    }
    [IO.File]::AppendAllLines((Join-Path $appFolder 'config.ini'), [string[]]$lines)
    $backups = Join-Path $appFolder 'backups'
    if (Test-Path -LiteralPath $backups) { Remove-Item -LiteralPath $backups -Recurse -Force }
    $escaped = $testData.Replace('\', '\\')
    foreach ($file in Get-ChildItem -LiteralPath $sample -Recurse -Filter '*.json' | Where-Object { $_.Name -ne 'tracked-mods.json' }) {
        $relative = $file.FullName.Substring($sample.Length + 1)
        $destination = Join-Path $backups $relative
        New-Item -ItemType Directory -Force (Split-Path $destination) | Out-Null
        # The sample's paths use forward slashes; the copy gets the TestData folder and Windows separators (escaped for JSON).
        $text = [regex]::Replace((Get-Content -Raw -Encoding UTF8 -LiteralPath $file.FullName), '"\{TestData\}([^"]*)"',
            { param($m) '"' + $escaped + $m.Groups[1].Value.Replace('/', '\\') + '"' })
        [IO.File]::WriteAllText($destination, $text, [Text.UTF8Encoding]::new($false))
    }
}

function Capture([string]$name, [int]$width, [int]$height) {
    $process = Start-Process -FilePath $exe -WorkingDirectory $appFolder -PassThru
    try {
        $deadline = (Get-Date).AddSeconds(60)
        while ($process.MainWindowHandle -eq 0 -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500; $process.Refresh() }
        if ($process.MainWindowHandle -eq 0) { throw "No window for $name" }
        $handle = $process.MainWindowHandle
        Start-Sleep -Seconds 2
        [ImmCapture]::Place($handle, 40, 40, $width, $height)
        Start-Sleep -Seconds $SettleSeconds
        [ImmCapture]::Place($handle, 40, 40, $width, $height)
        Start-Sleep -Milliseconds 800
        $size = [ImmCapture]::Save($handle, (Join-Path $output "$name.png"))
        [ImmCapture]::Release($handle)
        $errors = @()
        foreach ($log in 'startup.log', 'app-errors.log') {
            $path = Join-Path $appFolder $log
            if (Test-Path -LiteralPath $path) { $errors += @(Get-Content -LiteralPath $path | Where-Object { $_ -match 'Unhandled|Exception' }) }
        }
        [pscustomobject]@{ Name = $name; Pixels = $size; Errors = $errors.Count }
    }
    finally {
        if (!$process.HasExited) { $process.CloseMainWindow() | Out-Null; if (!$process.WaitForExit(5000)) { $process.Kill() } }
        $process.WaitForExit()
    }
}

$results = @()
try {
    foreach ($sizeText in $Sizes) {
        $dip = $sizeText -split 'x'
        $width = [int][Math]::Round([int]$dip[0] * $scale); $height = [int][Math]::Round([int]$dip[1] * $scale)
        foreach ($page in $Pages) {
            if ($page -eq 'firstrun') {
                # A first install has no settings: language and theme follow Windows, so one picture per size.
                Reset-State
                $results += Capture "firstrun-system-system-$sizeText" $width $height
                continue
            }
            foreach ($language in $Languages) {
                foreach ($theme in $Themes) {
                    Reset-State
                    Write-Configuration $page $language $theme $width $height
                    $name = "$page-$language-$theme-$sizeText"
                    Write-Host "Capturing $name"
                    $results += Capture $name $width $height
                }
            }
        }
    }
}
finally {
    for ($attempt = 0; $attempt -lt 10 -and (Test-Path -LiteralPath $work); $attempt++) {
        try { Remove-Item -LiteralPath $work -Recurse -Force } catch { Start-Sleep -Milliseconds 500 }
    }
}

# What the pictures depend on: recorded, never changed.
$desktop = Get-ItemProperty 'HKCU:\Control Panel\Desktop' -ErrorAction SilentlyContinue
$personalize = Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize' -ErrorAction SilentlyContinue
$manifest = [ordered]@{
    TakenAt = (Get-Date).ToString('o')
    Commit = (& git -C $root rev-parse --short HEAD 2>$null)
    Application = $app
    Windows = [Environment]::OSVersion.VersionString
    SystemDpi = [ImmCapture]::SystemDpi()
    Wallpaper = $desktop.WallPaper
    WallpaperStyle = $desktop.WallpaperStyle
    SystemAppsUseLightTheme = $personalize.AppsUseLightTheme
    Pictures = $results
}
[IO.File]::WriteAllText((Join-Path $output 'manifest.json'), ($manifest | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
$failed = @($results | Where-Object { $_.Errors -gt 0 })
Write-Host "$($results.Count) pictures in $output; $($failed.Count) with logged errors."
