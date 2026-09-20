[CmdletBinding()]
param(
    [string]$OutputDirectory = "artifacts\release",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$rootFullPath = [IO.Path]::GetFullPath($root)
$rootBoundary = $rootFullPath.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
$output = [IO.Path]::GetFullPath((Join-Path $root $OutputDirectory))
if (!$output.StartsWith($rootBoundary, [StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputDirectory must resolve inside the repository: $output"
}

if (!$SkipBuild) {
    & (Join-Path $PSScriptRoot "build-developer-tools.ps1") -Configuration Release | Out-Host
}

$source = Join-Path $root "DeveloperTools\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64"
$sourceBoundary = $source.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
$runtimePath = Join-Path $source "IntegratedModManager.DeveloperTools.exe"
if (!(Test-Path -LiteralPath $runtimePath -PathType Leaf) -or (Get-Item -LiteralPath $runtimePath).Length -eq 0) {
    throw "Developer Tools build output was not found: $runtimePath"
}

$fileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($runtimePath).FileVersion
if ([string]::IsNullOrWhiteSpace($fileVersion)) {
    throw "Developer Tools does not contain a file version."
}
$semanticVersion = ([Version]$fileVersion).ToString(3)
$packageName = "Integrated_Mod_Manager_Developer_Tools-v$semanticVersion.zip"
$packagePath = Join-Path $output $packageName
$checksumPath = $packagePath + ".sha256"
$staging = Join-Path $output ".developer-tools-staging"

New-Item -ItemType Directory -Path $output -Force | Out-Null
if (Test-Path -LiteralPath $staging) {
    Remove-Item -LiteralPath $staging -Recurse -Force
}
New-Item -ItemType Directory -Path $staging -Force | Out-Null

try {
    foreach ($file in Get-ChildItem -LiteralPath $source -Recurse -File) {
        if ($file.Extension.Equals('.pdb', [StringComparison]::OrdinalIgnoreCase) -or
            $file.Extension.Equals('.log', [StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        $relativePath = $file.FullName.Substring($sourceBoundary.Length)
        $destination = Join-Path $staging $relativePath
        New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($destination)) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
    }

    Copy-Item -LiteralPath (Join-Path $root "DeveloperTools\README.md") -Destination (Join-Path $staging "README.md") -Force

    Remove-Item -LiteralPath $packagePath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $checksumPath -Force -ErrorAction SilentlyContinue
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory(
        $staging,
        $packagePath,
        [IO.Compression.CompressionLevel]::Optimal,
        $false)

    $archive = [IO.Compression.ZipFile]::OpenRead($packagePath)
    try {
        $entryNames = @($archive.Entries | Where-Object { ![string]::IsNullOrEmpty($_.Name) } | ForEach-Object { $_.FullName.Replace('/', '\') })
        foreach ($requiredEntry in @('IntegratedModManager.DeveloperTools.exe', 'README.md')) {
            if ($entryNames -notcontains $requiredEntry) {
                throw "Developer Tools package is missing: $requiredEntry"
            }
        }
        foreach ($forbiddenEntry in @('IntegratedModManager.exe', 'ModFolderCopier.exe', 'LocalUpdateAgent.exe', '.managed-files.txt')) {
            if ($entryNames -contains $forbiddenEntry) {
                throw "Developer Tools package contains a main-application payload: $forbiddenEntry"
            }
        }
        if ($entryNames.Where({ $_.EndsWith('.pdb', [StringComparison]::OrdinalIgnoreCase) }).Count -gt 0) {
            throw "Developer Tools package contains debug symbols."
        }
    }
    finally {
        $archive.Dispose()
    }

    $packageHash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash
    [IO.File]::WriteAllText(
        $checksumPath,
        $packageHash + '  ' + $packageName + [Environment]::NewLine,
        (New-Object Text.UTF8Encoding($false)))

    Write-Host "Verified standalone Developer Tools package: $packagePath"
    Write-Host "SHA-256: $packageHash"
    Write-Output $packagePath
}
finally {
    if (Test-Path -LiteralPath $staging) {
        Remove-Item -LiteralPath $staging -Recurse -Force
    }
}
