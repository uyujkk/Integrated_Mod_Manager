[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot "DeveloperTools\IntegratedModManager.DeveloperTools.csproj"

function Find-MSBuild {
    $command = Get-Command msbuild.exe -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $candidates = @(
        "C:\BuildTools\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
    )
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path -LiteralPath $vswhere) {
        $found = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" |
            Select-Object -First 1
        if ($found -and (Test-Path -LiteralPath $found)) {
            return $found
        }
    }

    throw "MSBuild was not found. Install Visual Studio 2022 or Build Tools with Windows application build tools."
}

$msbuildPath = Find-MSBuild

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
