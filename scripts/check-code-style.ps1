[CmdletBinding()]
param(
    [string]$OutputDirectory = "artifacts\code-style"
)

# Builds every project with ImmCodeStyle=true (see Directory.Build.props) so the
# .editorconfig rules, such as IDE0005 (unnecessary using), are reported, and fails
# when any of them fire. Outputs go to their own folder: XML doc files generated for
# the analysis must not end up in the normal build output that gets packaged.

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$output = [IO.Path]::GetFullPath((Join-Path $root $OutputDirectory))

$msbuild = (Get-Command msbuild.exe -ErrorAction SilentlyContinue).Source
if (!$msbuild -and (Test-Path "C:\BuildTools\MSBuild\Current\Bin\MSBuild.exe")) {
    $msbuild = "C:\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
}
if (!$msbuild) {
    throw "MSBuild was not found. It is needed for the WinUI projects."
}

$dotnetProjects = @(
    "IntegratedModManager.Core\IntegratedModManager.Core.csproj",
    "IntegratedModManager.Data\IntegratedModManager.Data.csproj",
    "IntegratedModManager.EndfieldLaunch\IntegratedModManager.EndfieldLaunch.csproj",
    "IntegratedModManager.UpdateAgent.Core\IntegratedModManager.UpdateAgent.Core.csproj",
    "Tests\IntegratedModManager.Core.Tests.csproj",
    "DataStoreTests\IntegratedModManager.DataStore.Tests.csproj",
    "UpdaterTests\IntegratedModManager.UpdateAgent.Tests.csproj",
    "EndfieldLaunchTests\IntegratedModManager.EndfieldLaunch.Tests.csproj",
    "RepositoryPerformance\RepositoryPerformance.csproj"
)
$winuiProjects = @(
    "WinUI3\ModFolderCopier.WinUI.csproj",
    "DeveloperTools\IntegratedModManager.DeveloperTools.csproj"
)

$findings = New-Object System.Collections.Generic.List[string]
function Add-Findings {
    param([string[]]$Lines)
    foreach ($line in $Lines) {
        if ($line -match 'warning (IDE\d+):') {
            $findings.Add(($line -replace '\s*\[[^\]]+\]\s*$', '').Trim())
        }
    }
}

foreach ($project in $dotnetProjects) {
    $name = [IO.Path]::GetFileNameWithoutExtension($project)
    Write-Host "Checking $name"
    $lines = & dotnet build (Join-Path $root $project) -c Release --nologo --no-incremental `
        -p:ImmCodeStyle=true "-p:OutDir=$output\$name\" 2>&1 | ForEach-Object { "$_" }
    if ($LASTEXITCODE -ne 0) {
        $lines | Out-Host
        throw "Build failed: $project"
    }
    Add-Findings $lines
}

foreach ($project in $winuiProjects) {
    $name = [IO.Path]::GetFileNameWithoutExtension($project)
    Write-Host "Checking $name"
    $lines = & $msbuild (Join-Path $root $project) -restore -t:Rebuild -p:Configuration=Release -p:Platform=x64 `
        "-p:NoWarn=CA1416" -p:ImmCodeStyle=true "-p:OutDir=$output\$name\" -v:m -nologo 2>&1 | ForEach-Object { "$_" }
    if ($LASTEXITCODE -ne 0) {
        $lines | Out-Host
        throw "Build failed: $project"
    }
    Add-Findings $lines
}

$unique = @($findings | Sort-Object -Unique)
if ($unique.Count -gt 0) {
    $unique | ForEach-Object { Write-Host $_ }
    throw "$($unique.Count) code style finding(s). Fix them or adjust .editorconfig."
}
Write-Host "No code style findings."
