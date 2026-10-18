[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$AppPath,
    [Parameter(Mandatory)][string]$ProcDumpPath,
    [ValidateRange(30, 1800)][int]$CaptureSeconds = 600
)

$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskArtifacts = [IO.Path]::GetFullPath((Join-Path $taskRoot 'artifacts')) + [IO.Path]::DirectorySeparatorChar
$taskApp = (Resolve-Path -LiteralPath $AppPath).Path
if (!$taskApp.StartsWith($taskArtifacts, [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($taskApp) -ne 'ModFolderCopier.WinUI.exe') {
    throw 'Use an isolated ModFolderCopier.WinUI.exe inside this checkout artifacts directory.'
}
$taskProcDump = (Resolve-Path -LiteralPath $ProcDumpPath).Path
$taskSignature = Get-AuthenticodeSignature -LiteralPath $taskProcDump
if ($taskSignature.Status -ne 'Valid' -or $taskSignature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') {
    throw 'ProcDump must have a valid Microsoft signature.'
}
$taskCapture = Join-Path $taskArtifacts ('verification\crash-investigation\capture-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $taskCapture | Out-Null
$taskPreviousSetting = $env:IMM_UI_DIAGNOSTICS
try {
    $env:IMM_UI_DIAGNOSTICS = '1'
    # Match the native layout pass countdown, not our startup tracing-level message.
    # -at bounds one dump collection; it does NOT bound the monitoring lifetime.
    $taskArguments = @('-accepteula', '-ma', '-e', '-g', '-l', '-f', 'C000027B,LayoutCycleCountdown', '-n', '1', '-at', '60',
        '-x', ('"' + $taskCapture + '"'), ('"' + $taskApp + '"'))
    $taskMonitor = Start-Process -FilePath $taskProcDump -ArgumentList $taskArguments -WindowStyle Hidden -PassThru `
        -WorkingDirectory ([IO.Path]::GetDirectoryName($taskApp)) `
        -RedirectStandardOutput (Join-Path $taskCapture 'procdump.log') `
        -RedirectStandardError (Join-Path $taskCapture 'procdump.err')
    $taskRecord = [ordered]@{
        App = $taskApp; AppSha256 = (Get-FileHash -LiteralPath $taskApp -Algorithm SHA256).Hash
        ManagedSha256 = (Get-FileHash -LiteralPath ([IO.Path]::ChangeExtension($taskApp, '.dll')) -Algorithm SHA256).Hash
        MonitorPid = $taskMonitor.Id; CaptureSeconds = $CaptureSeconds; Capture = $taskCapture
        MonitorStartTicks = $taskMonitor.StartTime.ToUniversalTime().Ticks
        ProcDumpPath = $taskProcDump; ProcDumpSha256 = (Get-FileHash -LiteralPath $taskProcDump -Algorithm SHA256).Hash
        UiDiagnosticsEnabled = $true; StartedAt = [DateTimeOffset]::Now.ToString('O')
    }
    $taskMetadata = Join-Path $taskCapture 'capture.json'
    $taskRecord | ConvertTo-Json | Set-Content -LiteralPath $taskMetadata -Encoding utf8
    $taskShell = (Get-Command pwsh -ErrorAction Stop).Source
    $taskWatcher = Start-Process -FilePath $taskShell -WindowStyle Hidden -PassThru -ArgumentList @(
        '-NoProfile', '-File', ('"' + (Join-Path $PSScriptRoot 'watch-ui-crash-capture.ps1') + '"'),
        '-MetadataPath', ('"' + $taskMetadata + '"')) `
        -RedirectStandardOutput (Join-Path $taskCapture 'watchdog.log') `
        -RedirectStandardError (Join-Path $taskCapture 'watchdog.err')
    $taskWatcher.Id | Set-Content -LiteralPath (Join-Path $taskCapture 'watchdog.pid')
    $taskRecord['WatchdogPid'] = $taskWatcher.Id
    $taskRecord | ConvertTo-Json
}
finally { $env:IMM_UI_DIAGNOSTICS = $taskPreviousSetting }
