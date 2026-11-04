[CmdletBinding()]
param(
    # Folders with WinUI3\ModFolderCopier.WinUI.exe to compare, such as a saved earlier build and the current one.
    [Parameter(Mandatory)] [string[]]$Application,
    [int]$Runs = 5
)

# Startup times from startup.log, for stage J (docs/development/UI-Baseline-Screenshots.md).
# Each build runs from a private copy in %TEMP% with the synthetic tests\TestData and opens on the
# dashboard, like the baseline screenshots. Builds take turns, so a busy moment affects both alike.
# Reported per build, as medians in milliseconds:
#   Constructor:   "MainWindow constructor entered" to "MainWindow constructor completed".
#   ToRootLoaded:  process start to "RootGrid loaded" (the window's content is on screen).
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$Application = @($Application | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
$whatsNewSet = [regex]::Match((Get-Content -Raw (Join-Path $root 'src\WinUI3\MainWindow.WhatsNew.cs')), 'WhatsNewFeatureSet = "([^"]+)"').Groups[1].Value
function Encode([string]$value) { [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($value)) }

$copies = foreach ($source in $Application) {
    $full = [IO.Path]::GetFullPath((Join-Path $root $source))
    if (!(Test-Path -LiteralPath (Join-Path $full 'WinUI3\ModFolderCopier.WinUI.exe'))) { throw "No built application in $full" }
    $work = Join-Path ([IO.Path]::GetTempPath()) ('imm-startup-' + [Guid]::NewGuid().ToString('N').Substring(0, 12))
    Copy-Item -LiteralPath (Join-Path $full 'WinUI3') -Destination (Join-Path $work 'WinUI3') -Recurse
    Copy-Item -LiteralPath (Join-Path $root 'tests\TestData') -Destination (Join-Path $work 'TestData') -Recurse
    [pscustomobject]@{ Source = $source; Work = $work; Constructor = [Collections.Generic.List[double]]::new(); ToRootLoaded = [Collections.Generic.List[double]]::new() }
}

function Stage([string[]]$lines, [string]$name) {
    $line = $lines | Where-Object { $_ -match (' ' + [regex]::Escape($name) + '$') } | Select-Object -First 1
    if ($line) { [DateTimeOffset]::Parse($line.Substring(0, $line.IndexOf(' '))) }
}

try {
    for ($run = 0; $run -lt $Runs; $run++) {
        foreach ($copy in $copies) {
            $app = Join-Path $copy.Work 'WinUI3'
            $data = Join-Path $copy.Work 'TestData'
            foreach ($name in 'beta-shell.json', 'config.ini', 'startup.log', 'app-errors.log') {
                if (Test-Path -LiteralPath (Join-Path $app $name)) { Remove-Item -LiteralPath (Join-Path $app $name) -Force }
            }
            $mods = Join-Path $data 'ModRepository'; $target = Join-Path $data 'Loader\Mods'
            $shell = [ordered]@{
                SelectedRepositoryId = 'ui-repository'; CurrentPrimarySection = 'dashboard'; CheckAppUpdatesOnStartup = $false
                WhatsNewSeenVersion = $whatsNewSet; RepositoryGamesExplicit = $true; ThemePreference = 'light'
                Repositories = @([ordered]@{ Id = 'ui-repository'; Name = 'UI repository'; SourcePath = $mods; TargetPath = $target; OnlineSourceSite = 'GameBanana' })
            }
            [IO.File]::WriteAllText((Join-Path $app 'beta-shell.json'), ($shell | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
            [IO.File]::WriteAllLines((Join-Path $app 'config.ini'), [string[]]@("source_dir=$(Encode $mods)", "target_dir=$(Encode $target)", 'launcher_path=', "language=$(Encode 'zh')"))

            $process = Start-Process -FilePath (Join-Path $app 'ModFolderCopier.WinUI.exe') -WorkingDirectory $app -PassThru
            $log = Join-Path $app 'startup.log'
            $deadline = (Get-Date).AddSeconds(60)
            while ((Get-Date) -lt $deadline -and !((Test-Path -LiteralPath $log) -and (Select-String -LiteralPath $log -SimpleMatch 'Accessibility initialized' -Quiet))) {
                Start-Sleep -Milliseconds 250
            }
            $lines = @(Get-Content -LiteralPath $log -ErrorAction SilentlyContinue)
            $entered = Stage $lines 'MainWindow constructor entered'
            $completed = Stage $lines 'MainWindow constructor completed'
            $loaded = Stage $lines 'RootGrid loaded'
            if ($entered -and $completed) { $copy.Constructor.Add(($completed - $entered).TotalMilliseconds) }
            if ($loaded) { $copy.ToRootLoaded.Add(($loaded - [DateTimeOffset]$process.StartTime).TotalMilliseconds) }
            Start-Sleep -Seconds 2
            if (!$process.HasExited) { $process.CloseMainWindow() | Out-Null; if (!$process.WaitForExit(5000)) { $process.Kill() } }
            $process.WaitForExit()
        }
    }
}
finally {
    foreach ($copy in $copies) { Remove-Item -LiteralPath $copy.Work -Recurse -Force -ErrorAction SilentlyContinue }
}

function Median($values) {
    $sorted = @($values | Sort-Object)
    if ($sorted.Count -eq 0) { return $null }
    [Math]::Round($sorted[[int][Math]::Floor(($sorted.Count - 1) / 2)], 0)
}
$copies | ForEach-Object {
    [pscustomobject]@{
        Application = $_.Source; Runs = $_.Constructor.Count
        ConstructorMs = Median $_.Constructor; ToRootLoadedMs = Median $_.ToRootLoaded
        ConstructorAll = ($_.Constructor | ForEach-Object { [Math]::Round($_, 0) }) -join ' '
        ToRootLoadedAll = ($_.ToRootLoaded | ForEach-Object { [Math]::Round($_, 0) }) -join ' '
    }
} | Format-List
