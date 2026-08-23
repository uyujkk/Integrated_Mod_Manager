[CmdletBinding()]
param(
    [string]$SourceDirectory = "dist",
    [string]$OutputDirectory = "artifacts\release",
    [string]$PackageName = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

function Resolve-RepositoryPath {
    param([Parameter(Mandatory)] [string]$Path)

    $resolved = [IO.Path]::GetFullPath((Join-Path $root $Path))
    $rootPrefix = [IO.Path]::GetFullPath($root).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (!$resolved.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path must resolve inside the repository: $resolved"
    }
    return $resolved
}

function Test-IsUserStatePath {
    param([Parameter(Mandatory)] [string]$RelativePath)

    $normalized = $RelativePath.Replace('/', '\')
    $fileName = [IO.Path]::GetFileName($normalized)
    if ($fileName.EndsWith('.pdb', [StringComparison]::OrdinalIgnoreCase) -or
        $fileName.EndsWith('.download', [StringComparison]::OrdinalIgnoreCase)) {
        return $true
    }

    $preservedFiles = @(
        'config.ini',
        'beta-shell.json',
        'startup.log',
        'update-success.log',
        'update-rollback.log',
        'WinUI3\config.ini',
        'WinUI3\beta-shell.json',
        'WinUI3\startup.log'
    )
    if ($preservedFiles -contains $normalized) {
        return $true
    }

    $preservedPrefixes = @(
        'backups\',
        'cache\',
        'diagnostics\',
        'WinUI3\backups\',
        'WinUI3\cache\',
        'WinUI3\diagnostics\'
    )
    return $preservedPrefixes.Where({ $normalized.StartsWith($_, [StringComparison]::OrdinalIgnoreCase) }).Count -gt 0
}

$source = Resolve-RepositoryPath $SourceDirectory
$output = Resolve-RepositoryPath $OutputDirectory
if (!(Test-Path -LiteralPath $source -PathType Container)) {
    throw "Release source directory was not found: $source"
}
$sourceBoundary = $source.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
$outputBoundary = $output.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
if ($source.Equals($output, [StringComparison]::OrdinalIgnoreCase) -or
    $outputBoundary.StartsWith($sourceBoundary, [StringComparison]::OrdinalIgnoreCase) -or
    $sourceBoundary.StartsWith($outputBoundary, [StringComparison]::OrdinalIgnoreCase)) {
    throw "SourceDirectory and OutputDirectory must not contain one another."
}

$runtimePath = Join-Path $source 'WinUI3\ModFolderCopier.WinUI.exe'
$requiredSourceFiles = @(
    (Join-Path $source 'ModFolderCopier.exe'),
    (Join-Path $source 'LocalUpdateAgent.exe'),
    $runtimePath
)
foreach ($requiredFile in $requiredSourceFiles) {
    if (!(Test-Path -LiteralPath $requiredFile -PathType Leaf) -or (Get-Item -LiteralPath $requiredFile).Length -eq 0) {
        throw "Required release file is missing or empty: $requiredFile"
    }
}

if (Test-Path -LiteralPath $output) {
    Remove-Item -LiteralPath $output -Recurse -Force
}
$staging = Join-Path $output 'staging'
New-Item -ItemType Directory -Path $staging -Force | Out-Null

$sourcePrefix = $source.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
foreach ($file in Get-ChildItem -LiteralPath $source -Recurse -File) {
    $relativePath = $file.FullName.Substring($sourcePrefix.Length)
    if (Test-IsUserStatePath $relativePath) {
        continue
    }

    $destination = Join-Path $staging $relativePath
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($destination)) -Force | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
}

$manifestPath = Join-Path $staging '.managed-files.txt'
$stagingPrefix = $staging.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
$managedFiles = @(
    Get-ChildItem -LiteralPath $staging -Recurse -File |
        Where-Object { $_.FullName -ne $manifestPath } |
        ForEach-Object { $_.FullName.Substring($stagingPrefix.Length) }
)
$managedFiles = @($managedFiles + '.managed-files.txt' | Sort-Object -Unique)
[IO.File]::WriteAllLines($manifestPath, [string[]]$managedFiles, (New-Object Text.UTF8Encoding($false)))

$version = [Diagnostics.FileVersionInfo]::GetVersionInfo($runtimePath).FileVersion
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "The runtime executable does not contain a file version."
}
$semanticVersion = ([Version]$version).ToString(3)
if ([string]::IsNullOrWhiteSpace($PackageName)) {
    $PackageName = "Integrated_Mod_Manager-v$semanticVersion.zip"
}
if (![IO.Path]::GetFileName($PackageName).Equals($PackageName, [StringComparison]::Ordinal)) {
    throw "PackageName must be a file name without a directory."
}
if (![IO.Path]::GetExtension($PackageName).Equals('.zip', [StringComparison]::OrdinalIgnoreCase)) {
    throw "PackageName must use the .zip extension."
}

$packagePath = Join-Path $output $PackageName
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory(
    $staging,
    $packagePath,
    [IO.Compression.CompressionLevel]::Optimal,
    $false)

$archive = [IO.Compression.ZipFile]::OpenRead($packagePath)
try {
    $entries = @($archive.Entries | Where-Object { ![string]::IsNullOrEmpty($_.Name) })
    $entryNames = @($entries.FullName | ForEach-Object { $_.Replace('/', '\') })
    foreach ($requiredEntry in @(
        'ModFolderCopier.exe',
        'LocalUpdateAgent.exe',
        'WinUI3\ModFolderCopier.WinUI.exe',
        '.managed-files.txt')) {
        if ($entryNames -notcontains $requiredEntry) {
            throw "Release package is missing: $requiredEntry"
        }
    }

    foreach ($entryName in $entryNames) {
        if (Test-IsUserStatePath $entryName) {
            throw "Release package contains user state or a development artifact: $entryName"
        }
    }

    foreach ($managedFile in Get-Content -LiteralPath $manifestPath) {
        if ($entryNames -notcontains $managedFile.Replace('/', '\')) {
            throw "Managed-file manifest references a missing package entry: $managedFile"
        }
    }
}
finally {
    $archive.Dispose()
}

$packageHash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash
$checksumPath = $packagePath + '.sha256'
[IO.File]::WriteAllText(
    $checksumPath,
    $packageHash + '  ' + [IO.Path]::GetFileName($packagePath) + [Environment]::NewLine,
    (New-Object Text.UTF8Encoding($false)))
Remove-Item -LiteralPath $staging -Recurse -Force

Write-Host "Verified release package: $packagePath"
Write-Host "SHA-256: $packageHash"
Write-Output $packagePath
