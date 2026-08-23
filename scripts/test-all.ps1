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

function Get-RepositoryRelativePath {
    param([Parameter(Mandatory)] [string]$Path)

    $fullPath = [IO.Path]::GetFullPath($Path)
    if (!$fullPath.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path must resolve inside the repository: $fullPath"
    }
    return $fullPath.Substring($rootPrefix.Length)
}

function Assert-CoverageThreshold {
    param(
        [Parameter(Mandatory)] [string]$ResultsDirectory,
        [Parameter(Mandatory)] [double]$MinimumLineRate,
        [Parameter(Mandatory)] [double]$MinimumBranchRate
    )

    $coverageFile = Get-ChildItem -LiteralPath $ResultsDirectory -Recurse -Filter 'coverage.cobertura.xml' |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
    if (!$coverageFile) {
        throw "Coverage report was not produced in $ResultsDirectory"
    }

    [xml]$coverage = Get-Content -LiteralPath $coverageFile.FullName
    $lineRate = [double]::Parse($coverage.coverage.'line-rate', [Globalization.CultureInfo]::InvariantCulture)
    $branchRate = [double]::Parse($coverage.coverage.'branch-rate', [Globalization.CultureInfo]::InvariantCulture)
    if ($lineRate -lt $MinimumLineRate -or $branchRate -lt $MinimumBranchRate) {
        throw ("Coverage threshold failed for {0}: line={1:P1} (min {2:P0}), branch={3:P1} (min {4:P0})" -f
            $ResultsDirectory, $lineRate, $MinimumLineRate, $branchRate, $MinimumBranchRate)
    }
    Write-Host ("Coverage passed: line={0:P1}, branch={1:P1}" -f $lineRate, $branchRate)
}

if (Test-Path -LiteralPath $artifacts) {
    Remove-Item -LiteralPath $artifacts -Recurse -Force
}
New-Item -ItemType Directory -Path $testResults, $smokeOutput -Force | Out-Null

$dotnetCommand = Get-Command dotnet.exe -ErrorAction Stop
$testProjects = @(
    @{ Name = "core"; Path = Join-Path $root "Tests\IntegratedModManager.Core.Tests.csproj"; Line = 0.90; Branch = 0.85 },
    @{ Name = "datastore"; Path = Join-Path $root "DataStoreTests\IntegratedModManager.DataStore.Tests.csproj"; Line = 0.70; Branch = 0.60 },
    @{ Name = "updater"; Path = Join-Path $root "UpdaterTests\IntegratedModManager.UpdateAgent.Tests.csproj"; Line = 0.60; Branch = 0.55 }
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
    Assert-CoverageThreshold $projectResults $testProject.Line $testProject.Branch
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

    $releaseSource = Join-Path $artifacts "ReleaseSource"
    New-Item -ItemType Directory -Path (Join-Path $releaseSource "WinUI3") -Force | Out-Null
    Copy-Item -LiteralPath $launcherOutput -Destination (Join-Path $releaseSource "ModFolderCopier.exe") -Force
    Copy-Item -LiteralPath $updaterOutput -Destination (Join-Path $releaseSource "LocalUpdateAgent.exe") -Force
    Copy-Item -Path (Join-Path (Split-Path -Parent $runtimeOutput) '*') `
        -Destination (Join-Path $releaseSource "WinUI3") -Recurse -Force
    & (Join-Path $root "scripts\package-release.ps1") `
        -SourceDirectory (Get-RepositoryRelativePath $releaseSource) `
        -OutputDirectory (Get-RepositoryRelativePath (Join-Path $artifacts "ReleasePackage")) |
        Out-Host
}

Write-Host "All tests and build checks completed successfully."
Write-Host "Artifacts: $artifacts"
