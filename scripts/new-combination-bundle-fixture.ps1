[CmdletBinding()]
param(
    [ValidateRange(3, 100)] [int]$ModCount = 8,
    [ValidateRange(0, 512)] [int]$PayloadMegabytesPerMod = 0,
    [string]$OutputDirectory = 'artifacts\combination-bundle-fixture\TestData',
    [string]$CoreAssembly = 'IntegratedModManager.Core\bin\Release\net8.0\IntegratedModManager.Core.dll'
)

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'Use PowerShell 7 with the .NET 8+ runtime.' }
if ([long]$ModCount * $PayloadMegabytesPerMod -gt 1024) { throw 'Synthetic payload must not exceed 1 GiB in total.' }
$taskRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$taskBoundary = $taskRoot.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
$taskAssembly = [IO.Path]::GetFullPath((Join-Path $taskRoot $CoreAssembly))
if (!$taskAssembly.StartsWith($taskBoundary, [StringComparison]::OrdinalIgnoreCase)) { throw 'CoreAssembly must stay inside this repository.' }
[Reflection.Assembly]::LoadFrom($taskAssembly) | Out-Null
# The existing generator rejects existing/linked/out-of-repository output paths.
$taskFixture = & (Join-Path $PSScriptRoot 'new-combination-preview-fixture.ps1') -ModCount $ModCount -OutputDirectory $OutputDirectory
$taskFixtureRoot = [IO.Path]::GetDirectoryName($taskFixture.Source)
$taskPaths = [IntegratedModManager.Core.ModCombinationDeploymentPolicy]::Capture($taskFixture.Source, $taskFixture.Target)
# Optional bounded, incompressible synthetic data makes native cancellation
# observable without copying game assets or changing an existing fixture.
if ($PayloadMegabytesPerMod -gt 0) {
    $taskBuffer = [byte[]]::new(1MB)
    foreach ($taskRelative in $taskPaths) {
        $taskPayload = Join-Path (Join-Path $taskFixture.Source $taskRelative) 'synthetic-payload.bin'
        $taskPayloadStream = [IO.File]::Open($taskPayload, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
        try {
            for ($taskBlock = 0; $taskBlock -lt $PayloadMegabytesPerMod; $taskBlock++) {
                [Security.Cryptography.RandomNumberGenerator]::Fill($taskBuffer)
                $taskPayloadStream.Write($taskBuffer, 0, $taskBuffer.Length)
            }
        } finally { $taskPayloadStream.Dispose() }
    }
}
$taskIdentities = [ValueTuple[string,string][]]@($taskPaths | ForEach-Object {
    [ValueTuple[string,string]]::new((Join-Path $taskFixture.Source $_), [IO.Path]::GetFileName($_))
})
$taskStates = [IntegratedModManager.Core.ModPersistentPresetEngine]::CaptureCombination($taskIdentities, $taskFixture.State)
$taskMods = [IntegratedModManager.Core.CombinationPresetState[]]@($taskStates | ForEach-Object {
    [IntegratedModManager.Core.CombinationPresetState]::new([IO.Path]::GetRelativePath($taskFixture.Source, $_.ModDirectory), $_.Values)
})
$taskZip = Join-Path $taskFixtureRoot 'Demo-Combination.imm-preset.zip'
[IntegratedModManager.Core.CombinationBundleService]::Export($taskFixture.Source, $taskZip, 'Synthetic bundle demo', 'Synthetic Game', $true,
    $taskMods, [Threading.CancellationToken]::None, $null, $null)
$taskDestination = Join-Path $taskFixtureRoot 'ImportRepository'
$taskImportTarget = Join-Path $taskFixtureRoot 'ImportLoader\Mods'
New-Item -ItemType Directory -Path $taskDestination, $taskImportTarget | Out-Null
$taskImportIni = Join-Path $taskFixtureRoot 'ImportLoader\d3dx_user.ini'
[IO.File]::WriteAllText($taskImportIni, "[Constants]`n`$\Mods\External-keep\mod.ini\unrelated = 99 ; keep untouched`n")
$taskStaging = [IntegratedModManager.Core.CombinationBundleService]::CreateStagingParent($taskDestination)
$taskImport = [IntegratedModManager.Core.CombinationBundleService]::PrepareImport($taskZip, $taskDestination, $taskStaging,
    [Threading.CancellationToken]::None, $null, $null)
try {
    if (!$taskImport.CanInstall -or $taskImport.Preview.Count -ne $ModCount) { throw 'Synthetic bundle failed its own readback validation.' }
} finally { $taskImport.Dispose() }
if ($taskImport.CleanupWarnings.Count -gt 0) { throw ($taskImport.CleanupWarnings -join '; ') }
[pscustomobject]@{Synthetic=$true; Bundle=$taskZip; ModCount=$ModCount; Values=$taskStates.Count*2;
    ImportRepository=$taskDestination; ImportTarget=$taskImportTarget; ImportState=$taskImportIni; Validation='Prepare-only readback passed; no installation'}
