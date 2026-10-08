[CmdletBinding()]
param(
    [ValidateRange(3, 100)] [int]$ModCount = 32,
    [string]$OutputDirectory = 'artifacts\combination-preview-fixture\TestData'
)

$ErrorActionPreference = 'Stop'
$taskRepoRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$taskBoundary = $taskRepoRoot.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
$taskFixtureRoot = [IO.Path]::GetFullPath((Join-Path $taskRepoRoot $OutputDirectory))
if (!$taskFixtureRoot.StartsWith($taskBoundary, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Synthetic fixtures must stay inside this repository.'
}
for ($taskParent = $taskFixtureRoot; $taskParent.StartsWith($taskBoundary, [StringComparison]::OrdinalIgnoreCase);
    $taskParent = [IO.Path]::GetDirectoryName($taskParent)) {
    if ((Test-Path -LiteralPath $taskParent) -and
        ((Get-Item -LiteralPath $taskParent -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Fixture output cannot pass through a directory link: $taskParent"
    }
}
if (Test-Path -LiteralPath $taskFixtureRoot) {
    throw "Fixture already exists. Choose a new directory; nothing was overwritten: $taskFixtureRoot"
}
$taskSource = Join-Path $taskFixtureRoot 'ModRepository'
$taskTarget = Join-Path $taskFixtureRoot 'Loader\Mods'
$taskUser = Join-Path $taskFixtureRoot 'Loader\d3dx_user.ini'
New-Item -ItemType Directory -Path $taskSource, $taskTarget -Force | Out-Null
$taskState = New-Object Collections.Generic.List[string]
$taskState.Add('; SYNTHETIC OFFLINE FIXTURE. NEVER RUN A GAME/LOADER HERE.')
$taskState.Add('[Constants]')
for ($taskIndex = 0; $taskIndex -lt $ModCount; $taskIndex++) {
    $taskName = 'Synthetic-Mod-{0:D3}' -f $taskIndex
    $taskCategory = 'Synthetic-Character-{0:D2}' -f ($taskIndex % 4)
    $taskMod = Join-Path (Join-Path $taskSource $taskCategory) $taskName
    $taskCopy = Join-Path $taskTarget $taskName
    New-Item -ItemType Directory -Path $taskMod, $taskCopy | Out-Null
    $taskIni = "; Synthetic preview data, not a playable Mod.`r`n[Constants]`r`nglobal persist `$outfit = 0`r`nglobal persist `$hair = 0`r`n"
    [IO.File]::WriteAllText((Join-Path $taskMod 'mod.ini'), $taskIni)
    [IO.File]::WriteAllText((Join-Path $taskCopy 'mod.ini'), $taskIni)
    $taskState.Add(('$\Mods\{0}\mod.ini\outfit = {1}' -f $taskName, ($taskIndex % 3)))
    $taskState.Add(('$\Mods\{0}\mod.ini\hair = 1' -f $taskName))
}
$taskExternal = Join-Path $taskTarget 'External-keep'
New-Item -ItemType Directory -Path $taskExternal | Out-Null
[IO.File]::WriteAllText((Join-Path $taskExternal 'README.txt'), 'Unknown target folder. Preset restores must leave it untouched.')
$taskState.Add('$\Mods\External-keep\mod.ini\unrelated = 99 ; keep untouched')
[IO.File]::WriteAllLines($taskUser, $taskState, (New-Object Text.UTF8Encoding($false)))
[IO.File]::WriteAllText((Join-Path $taskFixtureRoot 'FIXTURE.txt'),
    "SYNTHETIC DATA ONLY. $ModCount Mods, 2 parameters each. No game assets or launcher.`r`nUse only an isolated test application. Do not choose a real game loader.`r`nSource=$taskSource`r`nTarget=$taskTarget`r`nState=$taskUser`r`n")
[pscustomobject]@{Synthetic=$true; ModCount=$ModCount; Source=$taskSource; Target=$taskTarget; State=$taskUser}
