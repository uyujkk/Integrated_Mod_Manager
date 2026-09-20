[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot "DeveloperTools\IntegratedModManager.DeveloperTools.csproj"
$msbuildPath = "C:\BuildTools\MSBuild\Current\Bin\MSBuild.exe"

if (-not (Test-Path -LiteralPath $msbuildPath)) {
    throw "MSBuild was not found at $msbuildPath"
}

$projectAssetsPath = Join-Path $repositoryRoot "DeveloperTools\obj\project.assets.json"
$buildArguments = @(
    $projectPath,
    "/t:Build",
    "/p:Configuration=$Configuration",
    "/p:Platform=x64",
    "/p:NoWarn=CA1416",
    "/nologo",
    "/v:minimal"
)
if (-not (Test-Path -LiteralPath $projectAssetsPath)) {
    $buildArguments = @($projectPath, "/restore") + $buildArguments[1..($buildArguments.Count - 1)]
}

& $msbuildPath @buildArguments
if ($LASTEXITCODE -ne 0) {
    throw "Developer tools build failed with exit code $LASTEXITCODE"
}

$outputPath = Join-Path $repositoryRoot "DeveloperTools\bin\x64\$Configuration\net8.0-windows10.0.19041.0\win-x64\IntegratedModManager.DeveloperTools.exe"
if (-not (Test-Path -LiteralPath $outputPath)) {
    throw "Build succeeded but the executable was not found at $outputPath"
}

Write-Output $outputPath
