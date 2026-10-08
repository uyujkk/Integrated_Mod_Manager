[CmdletBinding()]
param(
    [ValidateSet(100, 500, 1000)] [int]$ModCount = 100,
    [ValidateRange(1, 100)] [int]$CategoryCount = 10,
    [string]$OutputDirectory = 'artifacts\repository-scan-fixtures'
)

$ErrorActionPreference = 'Stop'
$taskRepoRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$taskBoundary = $taskRepoRoot.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
$taskOutputRoot = [IO.Path]::GetFullPath((Join-Path $taskRepoRoot $OutputDirectory))
if (!$taskOutputRoot.StartsWith($taskBoundary, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Fixture output must be inside this repository.'
}
# An existing link in the output chain could escape the lexical boundary.
for ($taskParent = $taskOutputRoot; $taskParent.StartsWith($taskBoundary, [StringComparison]::OrdinalIgnoreCase);
    $taskParent = [IO.Path]::GetDirectoryName($taskParent)) {
    if ((Test-Path -LiteralPath $taskParent) -and
        ((Get-Item -LiteralPath $taskParent -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Fixture output cannot pass through a directory link: $taskParent"
    }
}
$taskFixtureRoot = Join-Path $taskOutputRoot ("synthetic-{0}-mods" -f $ModCount)
if (Test-Path -LiteralPath $taskFixtureRoot) {
    throw "Fixture already exists; choose a new output directory. Nothing was overwritten: $taskFixtureRoot"
}
$taskSource = Join-Path $taskFixtureRoot 'Source'
$taskTarget = Join-Path $taskFixtureRoot 'Target'
New-Item -ItemType Directory -Path $taskSource, $taskTarget -Force | Out-Null
for ($taskIndex = 0; $taskIndex -lt $ModCount; $taskIndex++) {
    $taskCategory = 'Synthetic-Character-{0:D2}' -f ($taskIndex % $CategoryCount)
    $taskName = 'Synthetic-Mod-{0:D4}' -f $taskIndex
    $taskModPath = Join-Path (Join-Path $taskSource $taskCategory) $taskName
    New-Item -ItemType Directory -Path $taskModPath | Out-Null
    [IO.File]::WriteAllText((Join-Path $taskModPath 'README.txt'),
        "SYNTHETIC TEST DATA ONLY. This is not a working game Mod. Do not point a loader/game at this fixture.`r`n")
    [IO.File]::WriteAllText((Join-Path $taskModPath 'sample.ini'),
        "; Synthetic scanner/shortcut fixture, not a game Mod.`r`n[Constants]`r`nglobal persist `$sample = 0`r`n[KeySample]`r`nkey = F8`r`ntype = cycle`r`n`$sample = 0,1`r`n")
}
[IO.File]::WriteAllText((Join-Path $taskFixtureRoot 'FIXTURE.txt'),
    "Synthetic scanner fixture: $ModCount mods in $CategoryCount categories.`r`nSource=$taskSource`r`nTarget=$taskTarget`r`nNo game assets or launcher. Existing folders were not overwritten.`r`n")
[pscustomobject]@{Synthetic=$true; ModCount=$ModCount; Categories=$CategoryCount; Source=$taskSource; Target=$taskTarget}
