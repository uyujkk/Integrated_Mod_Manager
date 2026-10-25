[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BasePackage,
    [Parameter(Mandatory)][string]$Package,
    [Parameter(Mandatory)][string]$Application,
    [string]$Version = '4.5.2-preview.1',
    [string]$OutputDirectory = 'artifacts\preview-upgrade'
)
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$taskBoundary = $taskRoot.TrimEnd('\') + '\'
$taskAccept = [IO.Path]::GetFullPath((Join-Path $taskRoot $OutputDirectory))
if (!$taskAccept.StartsWith($taskBoundary, [StringComparison]::OrdinalIgnoreCase) -or (Test-Path $taskAccept)) {
    throw 'Choose a new acceptance directory inside this checkout.'
}
$taskInstall = Join-Path $taskAccept 'Install'
New-Item -ItemType Directory -Path $taskInstall -Force | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::ExtractToDirectory([IO.Path]::GetFullPath($BasePackage), $taskInstall)
$taskOldVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $taskInstall 'WinUI3\ModFolderCopier.WinUI.exe')).ProductVersion
$taskOldHash = (Get-FileHash (Join-Path $taskInstall 'WinUI3\ModFolderCopier.WinUI.dll')).Hash
$taskAgent = Join-Path $taskAccept 'OldUpdateAgent.exe'
Copy-Item (Join-Path $taskInstall 'LocalUpdateAgent.exe') $taskAgent
$taskSource = Join-Path $taskAccept 'Repository'
$taskTarget = Join-Path $taskAccept 'Mods'
New-Item -ItemType Directory -Path (Join-Path $taskSource 'Character\Mod'), $taskTarget -Force | Out-Null
@('[Constants]', 'global persist $x = 0') | Set-Content (Join-Path $taskSource 'Character\Mod\mod.ini')
function Encode([string]$value) { [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($value)) }
@("source_dir=$(Encode $taskSource)", "target_dir=$(Encode $taskTarget)", 'launcher_path=', "theme=$(Encode 'dark')", "language=$(Encode 'en')") |
    Set-Content (Join-Path $taskInstall 'WinUI3\config.ini') -Encoding utf8
$taskShell = [ordered]@{
    SelectedRepositoryId='preview-upgrade'; CurrentPrimarySection='dashboard'; CheckAppUpdatesOnStartup=$false
    WhatsNewSeenVersion=$Version; ShortcutPanelHotkeyEnabled=$false; MinimizeToTray=$false
    Repositories=@(@{Id='preview-upgrade';Name='Synthetic upgrade';SourcePath=$taskSource;TargetPath=$taskTarget;UseDirectoryLinks=$false
        AllowMultipleModsPerCharacter=$true;CharacterSelectionOverrides=@{Character=$false};CharacterAvatarBindings=@{Character='test-character'}})
    OnlinePreferences=@{ConcurrentDownloads=3;RememberDownloadFolders=$true;DownloadFolders=@{synthetic=$taskSource}}
    LocalLibraries=@{'preview-upgrade'=@{ModTags=@{'Character\Mod'=@('favorite','preview')};Filters=@()}}
    ConfigurationProfiles=@(@{Id='profile';RepositoryId='preview-upgrade';Name='Saved combination';ModRelativePaths=@('Character\Mod');IncludesPersistentState=$false;PersistentStates=@()})
    ModPersistentSlots=@(@{Id='slot';RepositoryId='preview-upgrade';Name='Saved slot';ModRelativePath='Character\Mod';Values=@(@{RelativeIniPath='mod.ini';Variable='x';RuntimeKey='$\Mods\Mod\mod.ini\x';Value='7'})})
}
$taskShell | ConvertTo-Json -Depth 20 | Set-Content (Join-Path $taskInstall 'WinUI3\beta-shell.json') -Encoding utf8
$taskSentinels = @('WinUI3\backups\acceptance\sentinel.txt','WinUI3\snapshots\user-ini\preview-upgrade\sentinel.ini')
foreach ($relative in $taskSentinels) {
    $file = Join-Path $taskInstall $relative
    New-Item -ItemType Directory -Path (Split-Path $file) -Force | Out-Null
    'Synthetic preserved user data' | Set-Content $file
}
$taskHashes = @{}; foreach ($relative in $taskSentinels) { $taskHashes[$relative] = (Get-FileHash (Join-Path $taskInstall $relative)).Hash }
$taskBefore = Get-Content (Join-Path $taskInstall 'WinUI3\beta-shell.json') -Raw | ConvertFrom-Json
$taskSnapshot = Join-Path $taskAccept 'BeforeUpdate'
Copy-Item -LiteralPath $taskInstall -Destination $taskSnapshot -Recurse
$taskTemp = Join-Path $taskAccept 'Temp'; New-Item -ItemType Directory -Path $taskTemp | Out-Null
$taskPreviousTemp=$env:TEMP; $taskPreviousTmp=$env:TMP
try {
    $env:TEMP=$taskTemp; $env:TMP=$taskTemp
    $parent = Start-Process (Get-Command dotnet).Source -ArgumentList '--version' -WindowStyle Hidden -PassThru -Wait -RedirectStandardOutput (Join-Path $taskAccept 'parent.txt')
    $numeric = $Version -replace '-.*$', ''
    $agent = Start-Process $taskAgent -ArgumentList @('--package',('"'+[IO.Path]::GetFullPath($Package)+'"'),
        '--install-root',('"'+$taskInstall+'"'),'--launcher',('"'+(Join-Path $taskInstall 'IntegratedModManager.exe')+'"'),
        '--process-id',$parent.Id,'--expected-version',$numeric,'--language','en') -WindowStyle Hidden -PassThru
    if (!$agent.WaitForExit(55000)) { throw 'Updater did not finish in 55 seconds.' }
    if ($agent.ExitCode -ne 0) { throw ('Updater exit: ' + $agent.ExitCode) }
} finally { $env:TEMP=$taskPreviousTemp; $env:TMP=$taskPreviousTmp }
Start-Sleep -Seconds 6
$taskAfter = Get-Content (Join-Path $taskInstall 'WinUI3\beta-shell.json') -Raw | ConvertFrom-Json
# Schema-aware subset comparison: preserve every old business field while allowing new defaults.
function Test-Preserved($old, $new) {
    if ($null -eq $old) { return $null -eq $new }
    if ($old -is [pscustomobject]) {
        foreach ($property in $old.PSObject.Properties) {
            if (!$new.PSObject.Properties[$property.Name] -or !(Test-Preserved $property.Value $new.($property.Name))) { return $false }
        }
        return $true
    }
    if ($old -is [array]) {
        if (@($old).Count -ne @($new).Count) { return $false }
        for ($i=0;$i -lt $old.Count;$i++) { if (!(Test-Preserved $old[$i] $new[$i])) { return $false } }
        return $true
    }
    return $old -ceq $new
}
$taskResult = [ordered]@{From=$taskOldVersion;To=[Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $taskInstall 'WinUI3\ModFolderCopier.WinUI.exe')).ProductVersion;AgentExit=$agent.ExitCode}
foreach ($key in @('Repositories','OnlinePreferences','LocalLibraries','ConfigurationProfiles','ModPersistentSlots')) {
    $taskResult[$key+'Preserved'] = Test-Preserved $taskBefore.$key $taskAfter.$key
}
$taskResult['UserBackupsAndSnapshotsPreserved'] = @($taskSentinels | Where-Object { (Get-FileHash (Join-Path $taskInstall $_)).Hash -ne $taskHashes[$_] }).Count -eq 0
$taskResult['RuntimeMatches'] = (Get-FileHash (Join-Path $taskInstall 'WinUI3\ModFolderCopier.WinUI.dll')).Hash -eq (Get-FileHash (Join-Path (Split-Path $Application) 'WinUI3\ModFolderCopier.WinUI.dll')).Hash
$manifest=Get-Content (Join-Path $taskInstall '.managed-files.txt')
$taskResult['ManifestUsesBackslashes'] = @($manifest | Where-Object { $_.Contains('/') }).Count -eq 0
$taskExe=Join-Path $taskInstall 'WinUI3\ModFolderCopier.WinUI.exe'
$running=@(Get-Process | Where-Object { try { $_.Path -eq $taskExe } catch { $false } })
$taskResult['RuntimeStarted']=$running.Count -eq 1
$taskResult['WindowTitle']=($running | Select-Object -First 1).MainWindowTitle
foreach ($process in $running) {
    [void]$process.CloseMainWindow()
    if (!$process.WaitForExit(3000)) {
        # Only stop this fixture's process, after checking its exact executable path.
        if ($process.Path -ne $taskExe) { throw 'Refused to stop a process outside the fixture.' }
        Stop-Process -Id $process.Id; [void]$process.WaitForExit(3000)
    }
}
if ($taskResult.To -ne $Version -or @($taskResult.Keys | Where-Object { $taskResult[$_] -is [bool] -and !$taskResult[$_] }).Count -gt 0) {
    $taskResult | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $taskAccept 'failure.json'); throw 'Upgrade acceptance failed; see failure.json.'
}
$taskUpgraded=Join-Path $taskAccept 'Upgraded'
foreach ($path in @($taskInstall,$taskSnapshot,$taskUpgraded)) {
    if (![IO.Path]::GetFullPath($path).StartsWith($taskAccept+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Rollback path escaped fixture.' }
}
[IO.Directory]::Move($taskInstall,$taskUpgraded)
[IO.Directory]::Move($taskSnapshot,$taskInstall)
$taskResult['RollbackVersion']=[Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $taskInstall 'WinUI3\ModFolderCopier.WinUI.exe')).ProductVersion
$taskResult['RollbackRuntimeMatches']=(Get-FileHash (Join-Path $taskInstall 'WinUI3\ModFolderCopier.WinUI.dll')).Hash -eq $taskOldHash
$taskRestored=Get-Content (Join-Path $taskInstall 'WinUI3\beta-shell.json') -Raw | ConvertFrom-Json
$taskResult['RollbackBusinessFieldsPreserved']=Test-Preserved $taskBefore $taskRestored
if ($taskResult.RollbackVersion -ne $taskOldVersion -or !$taskResult.RollbackRuntimeMatches -or !$taskResult.RollbackBusinessFieldsPreserved) { throw 'Rollback verification failed.' }
$taskResult | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $taskAccept 'upgrade-acceptance.json')
$taskResult | ConvertTo-Json -Depth 8
