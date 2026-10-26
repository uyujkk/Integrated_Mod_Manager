[CmdletBinding(DefaultParameterSetName = 'Run')]
param(
    # The d3dx_user.ini this simulated loader owns.
    [Parameter(ParameterSetName = 'Run', Mandatory)]
    [string]$UserIni,
    # Copies the synthetic TestData fixture to this folder and prints the paths to configure.
    [Parameter(ParameterSetName = 'Fixture', Mandatory)]
    [string]$CreateFixture
)

# A stand-in for a running 3DMigoto/XXMI loader, for testing state slots and F10 hot apply
# without a game. It behaves like the loader towards d3dx_user.ini:
#   - the persist values live in memory (edit them in the grid, like toggling in game);
#   - F10 first saves memory to d3dx_user.ini, then reads the file back (config reload);
#   - closing the window saves memory to d3dx_user.ini (loader exit).
# A read-only d3dx_user.ini makes the save fail, which is what hot apply relies on.
# Test data only: never point it at a real loader's d3dx_user.ini while that loader runs.

$ErrorActionPreference = 'Stop'

if ($PSCmdlet.ParameterSetName -eq 'Fixture') {
    $source = Join-Path (Split-Path -Parent $PSScriptRoot) 'tests\TestData'
    if (Test-Path -LiteralPath $CreateFixture) { throw "Choose a folder that does not exist yet: $CreateFixture" }
    New-Item -ItemType Directory -Path $CreateFixture | Out-Null
    Copy-Item -Path (Join-Path $source 'ModRepository'), (Join-Path $source 'Loader') -Destination $CreateFixture -Recurse
    $root = (Resolve-Path -LiteralPath $CreateFixture).Path
    Write-Host "Fixture ready. In the manager, create a repository with:"
    Write-Host "  Mod storage folder : $root\ModRepository"
    Write-Host "  Target Mods folder : $root\Loader\Mods   (copy mode; leave directory links off)"
    Write-Host "  d3dx_user.ini      : $root\Loader\d3dx_user.ini"
    Write-Host "Then run:"
    Write-Host "  .\scripts\fake-3dmigoto-loader.ps1 -UserIni `"$root\Loader\d3dx_user.ini`""
    return
}

Add-Type -AssemblyName System.Windows.Forms, System.Drawing
$UserIni = (Resolve-Path -LiteralPath $UserIni).Path
$assignment = '^(?<lead>\s*)(?<key>\$\\[^=;\r\n]+?)\s*=\s*(?<value>[^;\s]+)(?<tail>.*)$'
$state = [ordered]@{}

function Read-UserIni {
    $result = [ordered]@{}
    $inConstants = $false
    foreach ($line in [IO.File]::ReadAllLines($UserIni)) {
        if ($line -match '^\s*\[(?<name>[^\]]+)\]') { $inConstants = $Matches.name -eq 'Constants'; continue }
        if ($inConstants -and $line -match $assignment) { $result[$Matches.key.Trim()] = $Matches.value }
    }
    return $result
}

# Writes the in-memory values back, like the loader does: existing keys keep their place.
function Save-UserIni {
    $lines = [Collections.Generic.List[string]]::new([IO.File]::ReadAllLines($UserIni))
    $written = @{}
    $inConstants = $false
    $constantsEnd = -1
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^\s*\[(?<name>[^\]]+)\]') { $inConstants = $Matches.name -eq 'Constants'; if ($inConstants) { $constantsEnd = $i + 1 }; continue }
        if ($inConstants) {
            $constantsEnd = $i + 1
            if ($lines[$i] -match $assignment -and $state.Contains($Matches.key.Trim())) {
                $key = $Matches.key.Trim()
                $lines[$i] = "$($Matches.lead)$key = $($state[$key])$($Matches.tail)"
                $written[$key] = $true
            }
        }
    }
    if ($constantsEnd -lt 0) { $lines.Add('[Constants]'); $constantsEnd = $lines.Count }
    foreach ($key in $state.Keys) {
        if (-not $written.ContainsKey($key)) { $lines.Insert($constantsEnd, "$key = $($state[$key])"); $constantsEnd++ }
    }
    [IO.File]::WriteAllLines($UserIni, $lines, [Text.UTF8Encoding]::new($false))
}

$form = New-Object Windows.Forms.Form
$form.Text = 'Fake 3DMigoto loader (IMM test)'
$form.Size = New-Object Drawing.Size(720, 520)
$form.KeyPreview = $true
$grid = New-Object Windows.Forms.DataGridView
$grid.Dock = 'Fill'
$grid.AllowUserToAddRows = $false
$grid.AutoSizeColumnsMode = 'Fill'
[void]$grid.Columns.Add('Key', 'Runtime key (in loader memory)')
[void]$grid.Columns.Add('Value', 'Value')
$grid.Columns[0].ReadOnly = $true
$grid.Columns[0].FillWeight = 75
$log = New-Object Windows.Forms.TextBox
$log.Multiline = $true; $log.ReadOnly = $true; $log.ScrollBars = 'Vertical'; $log.Dock = 'Bottom'; $log.Height = 150
$hint = New-Object Windows.Forms.Label
$hint.Dock = 'Top'; $hint.Height = 40
$hint.Text = "Edit a value = change it in game. F10 = save memory to the file, then reload the file. Closing = save memory.`r`n$UserIni"
$form.Controls.AddRange(@($grid, $log, $hint))

function Write-Log([string]$text) { $log.AppendText("$(Get-Date -Format HH:mm:ss)  $text`r`n") }
function Show-State {
    $grid.Rows.Clear()
    foreach ($key in $state.Keys) { [void]$grid.Rows.Add($key, $state[$key]) }
}
function Load-State {
    $script:state = Read-UserIni
    Show-State
}
function Reload-Config {
    try {
        if ((Get-Item -LiteralPath $UserIni).IsReadOnly) { throw [UnauthorizedAccessException]::new('read-only') }
        Save-UserIni
        Write-Log 'F10: saved memory to d3dx_user.ini'
    }
    catch [UnauthorizedAccessException] { Write-Log 'F10: save skipped, d3dx_user.ini is read-only (hot apply holds it)' }
    Load-State
    Write-Log ('F10: reloaded file -> ' + (($state.GetEnumerator() | ForEach-Object { "$(Split-Path -Leaf $_.Key)=$($_.Value)" }) -join ', '))
}

$grid.add_CellEndEdit({ param($s, $e)
    $key = [string]$grid.Rows[$e.RowIndex].Cells[0].Value
    $state[$key] = [string]$grid.Rows[$e.RowIndex].Cells[1].Value
    Write-Log "in game: $key = $($state[$key]) (memory only)"
})
$form.add_KeyDown({ param($s, $e)
    if ($e.KeyCode -eq 'F10') { $e.Handled = $true; $e.SuppressKeyPress = $true; Reload-Config }
})
$form.add_FormClosing({
    try { Save-UserIni; Write-Host 'Loader exit: saved memory to d3dx_user.ini' }
    catch { Write-Host "Loader exit: could not save ($($_.Exception.Message))" }
})

Load-State
Write-Log "loaded $($state.Count) values from d3dx_user.ini"
[void]$form.ShowDialog()
