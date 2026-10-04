[CmdletBinding()]
param([switch]$SkipBuild)

$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $SkipBuild) {
    $taskMSBuild = Get-Command MSBuild.exe -ErrorAction SilentlyContinue
    $taskMSBuildPath = if ($taskMSBuild) { $taskMSBuild.Source } else { 'C:\BuildTools\MSBuild\Current\Bin\MSBuild.exe' }
    & $taskMSBuildPath (Join-Path $taskRoot 'WinUI3\ModFolderCopier.WinUI.csproj') /t:Build /p:Configuration=Release /p:Platform=x64 /p:Restore=false /p:NoWarn=CA1416 /v:q /nologo
    if ($LASTEXITCODE -ne 0) { throw 'WinUI beta build failed.' }
}
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$taskRuntimeSource = Join-Path $taskRoot 'WinUI3\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64'
$taskRuntimeExe = Join-Path $taskRuntimeSource 'ModFolderCopier.WinUI.exe'
if (-not (Test-Path -LiteralPath $taskRuntimeExe)) { throw 'Runtime output is missing.' }
if ([Diagnostics.FileVersionInfo]::GetVersionInfo($taskRuntimeExe).FileVersion -ne '4.0.0.0') { throw 'Build is not 4.0-beta.' }
$taskOutput = Join-Path $taskRoot ('artifacts\beta\' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 6))
$taskPackageRoot = Join-Path $taskOutput 'IntegratedModManager-4.0-beta'
$taskRuntimeTarget = Join-Path $taskPackageRoot 'WinUI3'
New-Item -ItemType Directory -Path $taskRuntimeTarget | Out-Null

Get-ChildItem -LiteralPath $taskRuntimeSource -Recurse -File | ForEach-Object {
    $taskRelative = $_.FullName.Substring($taskRuntimeSource.Length + 1)
    if ($taskRelative -match '(^|[\\/])(cache|backups|logs)([\\/]|$)' -or
        $_.Name -in @('config.ini', 'beta-shell.json', 'startup.log', 'LocalUpdateAgent.exe') -or
        $_.Extension -in @('.pdb', '.log', '.bak')) { return }
    $taskDestination = Join-Path $taskRuntimeTarget $taskRelative
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($taskDestination)) -Force | Out-Null
    Copy-Item -LiteralPath $_.FullName -Destination $taskDestination
}
Copy-Item -LiteralPath (Join-Path $taskRoot 'TestData') -Destination $taskPackageRoot -Recurse
Copy-Item -LiteralPath (Join-Path $taskRoot 'LICENSE') -Destination $taskPackageRoot
Copy-Item -LiteralPath (Join-Path $taskRoot 'docs\research\state-slots-test-guide.zh-CN.md') -Destination (Join-Path $taskPackageRoot 'TEST-GUIDE.zh-CN.md')
Copy-Item -LiteralPath (Join-Path $taskRoot 'docs\guides\Beta-Presets.en.md') -Destination (Join-Path $taskPackageRoot 'TEST-GUIDE.en.md')
Copy-Item -LiteralPath (Join-Path $taskRoot 'docs\releases\v4.0-beta.md') -Destination (Join-Path $taskPackageRoot 'RELEASE-NOTES.md')
$taskLauncher = Join-Path $taskPackageRoot 'IntegratedModManager-Beta.exe'
& $taskCompiler /nologo /target:winexe (('/win32icon:' + (Join-Path $taskRoot 'WinUI3\Assets\AppIcon.ico'))) (('/out:' + $taskLauncher)) /reference:System.dll /reference:System.Windows.Forms.dll (Join-Path $taskRoot 'WinUILauncher.cs')
if ($LASTEXITCODE -ne 0) { throw 'Beta launcher compilation failed.' }
$taskRequired = @('IntegratedModManager-Beta.exe', 'WinUI3\ModFolderCopier.WinUI.exe',
    'WinUI3\ModFolderCopier.WinUI.dll', 'WinUI3\ModFolderCopier.WinUI.runtimeconfig.json',
    'WinUI3\IntegratedModManager.Core.dll', 'WinUI3\IntegratedModManager.Data.dll',
    'WinUI3\Microsoft.UI.Xaml.dll', 'WinUI3\ModFolderCopier.WinUI.pri',
    'WinUI3\MainWindow.xbf', 'WinUI3\App.xbf', 'TEST-GUIDE.zh-CN.md', 'TEST-GUIDE.en.md',
    'TestData\StateA\d3dx_user.ini', 'TestData\StateB\d3dx_user.ini', 'TestData\Loader\d3dx_user.ini')
foreach ($taskRelative in $taskRequired) {
    if (-not (Test-Path -LiteralPath (Join-Path $taskPackageRoot $taskRelative))) { throw "Missing package file: $taskRelative" }
}
$taskForbidden = @(Get-ChildItem -LiteralPath $taskPackageRoot -Recurse -File | Where-Object {
    $_.Name -in @('config.ini', 'beta-shell.json', 'startup.log', 'LocalUpdateAgent.exe', 'ModFolderCopier.exe') -or
    $_.Extension -in @('.pdb', '.log', '.bak') -or $_.FullName -match '[\\/](cache|backups|logs)[\\/]'
})
if ($taskForbidden.Count -gt 0) { throw 'Package contains user data, debug files, or updater payloads.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$taskZip = Join-Path $taskOutput 'Integrated_Mod_Manager-v4.0-beta.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($taskPackageRoot, $taskZip, [IO.Compression.CompressionLevel]::Optimal, $true)
$taskHash = (Get-FileHash -LiteralPath $taskZip -Algorithm SHA256).Hash.ToLowerInvariant()
$taskChecksum = $taskZip + '.sha256'
[IO.File]::WriteAllText($taskChecksum, $taskHash + '  ' + [IO.Path]::GetFileName($taskZip) + "`n", [Text.UTF8Encoding]::new($false))
[PSCustomObject]@{ Application=$taskLauncher; Package=$taskZip; Checksum=$taskChecksum; SHA256=$taskHash; SizeMB=[Math]::Round((Get-Item -LiteralPath $taskZip).Length / 1MB, 2) } | ConvertTo-Json
