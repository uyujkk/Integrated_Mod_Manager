[CmdletBinding()]
param(
    [string]$ArtifactsDirectory = "artifacts\verification",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
$root = Split-Path -Parent $PSScriptRoot
$artifacts = [IO.Path]::GetFullPath((Join-Path $root $ArtifactsDirectory))
$rootPrefix = [IO.Path]::GetFullPath($root).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
if (!$artifacts.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "ArtifactsDirectory must resolve inside the repository: $artifacts"
}
$testResults = Join-Path $artifacts "TestResults"
$smokeOutput = Join-Path $artifacts "Smoke"

function Invoke-Checked {
    param(
        [Parameter(Mandatory)] [string]$FilePath,
        [Parameter(Mandatory)] [string[]]$Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code $LASTEXITCODE`: $FilePath $($Arguments -join ' ')"
    }
}

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
        $found = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
        if ($found -and (Test-Path -LiteralPath $found)) {
            return $found
        }
    }

    throw "MSBuild was not found. Install Visual Studio 2022 or Build Tools with Windows application build tools."
}

function Find-FrameworkCompiler {
    $candidates = @(
        "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
        "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
    )
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    throw ".NET Framework C# compiler was not found."
}

function Assert-FileExists {
    param([Parameter(Mandatory)] [string]$Path)

    if (!(Test-Path -LiteralPath $Path -PathType Leaf) -or (Get-Item -LiteralPath $Path).Length -eq 0) {
        throw "Expected a non-empty build output: $Path"
    }
}

if (Test-Path -LiteralPath $artifacts) {
    Remove-Item -LiteralPath $artifacts -Recurse -Force
}
New-Item -ItemType Directory -Path $testResults, $smokeOutput -Force | Out-Null

$dotnetCommand = Get-Command dotnet.exe -ErrorAction Stop
$testProjects = @(
    @{ Name = "core"; Path = Join-Path $root "Tests\IntegratedModManager.Core.Tests.csproj" },
    @{ Name = "updater"; Path = Join-Path $root "UpdaterTests\IntegratedModManager.UpdateAgent.Tests.csproj" }
)

foreach ($testProject in $testProjects) {
    $projectResults = Join-Path $testResults $testProject.Name
    New-Item -ItemType Directory -Path $projectResults -Force | Out-Null
    Invoke-Checked $dotnetCommand.Source @("restore", $testProject.Path, "--nologo")
    Invoke-Checked $dotnetCommand.Source @(
        "test", $testProject.Path,
        "--configuration", "Release",
        "--no-restore",
        "--nologo",
        "--logger", "trx;LogFileName=$($testProject.Name)-tests.trx",
        "--results-directory", $projectResults,
        "--collect:XPlat Code Coverage"
    )
}

if (!$SkipBuild) {
    $msbuild = Find-MSBuild
    $project = Join-Path $root "WinUI3\ModFolderCopier.WinUI.csproj"
    Invoke-Checked $msbuild @(
        $project,
        "/restore",
        "/t:Build",
        "/p:Configuration=Release",
        "/p:Platform=x64",
        "/p:NoWarn=CA1416",
        "/p:RestoreIgnoreFailedSources=true",
        "/m:1",
        "/nologo"
    )

    $compiler = Find-FrameworkCompiler
    $launcherOutput = Join-Path $smokeOutput "ModFolderCopier.exe"
    $updaterOutput = Join-Path $smokeOutput "LocalUpdateAgent.exe"
    $icon = Join-Path $root "WinUI3\Assets\AppIcon.ico"
    $iconArgument = if (Test-Path -LiteralPath $icon) { @("/win32icon:$icon") } else { @() }

    Invoke-Checked $compiler (@(
        "/nologo", "/target:winexe", "/out:$launcherOutput",
        "/reference:System.dll", "/reference:System.Windows.Forms.dll"
    ) + $iconArgument + @(Join-Path $root "WinUILauncher.cs"))

    Invoke-Checked $compiler (@(
        "/nologo", "/target:winexe", "/out:$updaterOutput",
        "/reference:System.dll", "/reference:System.Core.dll",
        "/reference:System.Windows.Forms.dll", "/reference:System.IO.Compression.dll",
        "/reference:System.IO.Compression.FileSystem.dll"
    ) + $iconArgument + @(Join-Path $root "LocalUpdateAgent.cs"))

    $runtimeOutput = Join-Path $root "WinUI3\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\ModFolderCopier.WinUI.exe"
    Assert-FileExists $runtimeOutput
    Assert-FileExists $launcherOutput
    Assert-FileExists $updaterOutput

    $declaredVersions = @(
        (Select-String -LiteralPath (Join-Path $root "WinUI3\AssemblyInfo.cs") -Pattern 'AssemblyFileVersion\("([^"]+)"\)').Matches.Groups[1].Value,
        (Select-String -LiteralPath (Join-Path $root "WinUILauncher.cs") -Pattern 'AssemblyFileVersion\("([^"]+)"\)').Matches.Groups[1].Value,
        (Select-String -LiteralPath (Join-Path $root "LocalUpdateAgent.cs") -Pattern 'AssemblyFileVersion\("([^"]+)"\)').Matches.Groups[1].Value
    )
    if (($declaredVersions | Select-Object -Unique).Count -ne 1) {
        throw "Application, launcher, and updater versions do not match: $($declaredVersions -join ', ')"
    }
}

Write-Host "All tests and build checks completed successfully."
Write-Host "Artifacts: $artifacts"
