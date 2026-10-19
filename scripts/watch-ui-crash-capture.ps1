[CmdletBinding()]
param([Parameter(Mandatory)][string]$MetadataPath)
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskBoundary = (Join-Path $taskRoot 'artifacts').TrimEnd('\') + '\'
$taskMetadataPath = [IO.Path]::GetFullPath($MetadataPath)
if (!$taskMetadataPath.StartsWith($taskBoundary, [StringComparison]::OrdinalIgnoreCase)) { throw 'Capture metadata must be inside this checkout artifacts.' }
$taskMetadata = Get-Content -LiteralPath $taskMetadataPath -Raw | ConvertFrom-Json
$taskResultPath = Join-Path (Split-Path -Parent $taskMetadataPath) 'monitor-result.json'
if ([int]$taskMetadata.CaptureSeconds -lt 30 -or [int]$taskMetadata.CaptureSeconds -gt 1800) { throw 'Invalid monitoring duration.' }
$taskApp = [IO.Path]::GetFullPath($taskMetadata.App)
if (!$taskApp.StartsWith($taskBoundary, [StringComparison]::OrdinalIgnoreCase)) { throw 'Target app must be inside this checkout artifacts.' }
try { $taskMonitor = Get-Process -Id ([int]$taskMetadata.MonitorPid) -ErrorAction Stop }
catch { @{ MonitorExited=$true; TimedOut=$false; Reason='Monitor already exited' } | ConvertTo-Json | Set-Content -LiteralPath $taskResultPath; return }
if ($taskMonitor.StartTime.ToUniversalTime().Ticks -ne [long]$taskMetadata.MonitorStartTicks -or
    ![string]::Equals($taskMonitor.Path, $taskMetadata.ProcDumpPath, [StringComparison]::OrdinalIgnoreCase)) { throw 'Monitor process identity changed.' }
$taskHandle = $taskMonitor.Handle
$taskDeadline = [DateTimeOffset]::Parse($taskMetadata.StartedAt).AddSeconds([int]$taskMetadata.CaptureSeconds)
$taskWait = [int][Math]::Clamp(($taskDeadline - [DateTimeOffset]::Now).TotalMilliseconds, 0, 1800000)
if ($taskMonitor.WaitForExit($taskWait)) {
    @{ MonitorExited=$true; TimedOut=$false; ExitCode=$taskMonitor.ExitCode } | ConvertTo-Json | Set-Content -LiteralPath $taskResultPath
    return
}
if ((Get-FileHash -LiteralPath $taskMetadata.ProcDumpPath).Hash -ne $taskMetadata.ProcDumpSha256) { throw 'ProcDump changed after monitoring began.' }
$taskTargets = @(Get-CimInstance Win32_Process -Filter "ParentProcessId=$($taskMonitor.Id)" | Where-Object {
    [string]::Equals($_.ExecutablePath, $taskApp, [StringComparison]::OrdinalIgnoreCase)
})
if ($taskTargets.Count -ne 1) { throw 'Cannot uniquely identify this monitor target; no process was canceled.' }
$taskTargetPid = [int]$taskTargets[0].ProcessId
# ProcDump's supported cancellation detaches its monitoring; it does not kill the application.
$taskCancel = Start-Process -FilePath $taskMetadata.ProcDumpPath -ArgumentList @('-accepteula','-cancel',$taskTargetPid) `
    -WindowStyle Hidden -PassThru -Wait -RedirectStandardOutput (Join-Path (Split-Path -Parent $taskMetadataPath) 'cancel.log')
$taskExited = $taskMonitor.WaitForExit(10000)
$taskStillRunning = $null -ne (Get-Process -Id $taskTargetPid -ErrorAction SilentlyContinue)
@{ MonitorExited=$taskExited; TimedOut=$true; CancelExitCode=$taskCancel.ExitCode; TargetPid=$taskTargetPid;
    TargetStillRunning=$taskStillRunning; FinishedAt=[DateTimeOffset]::Now.ToString('O') } |
    ConvertTo-Json | Set-Content -LiteralPath $taskResultPath -Encoding utf8
if (!$taskExited) { throw 'ProcDump did not exit after supported cancellation; no process was killed.' }
