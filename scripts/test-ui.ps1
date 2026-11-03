[CmdletBinding()]
param(
    # A folder with WinUI3\ModFolderCopier.WinUI.exe; by default the runtime test-all.ps1 staged.
    [string]$Application = 'artifacts\verification\ReleaseSource',
    [string]$Filter = ''
)

# Runs the UI automation tests against a built application. They start real windows, press a global
# hotkey and read the screen, so run them on an unlocked desktop and leave the mouse and keyboard alone.
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$app = [IO.Path]::GetFullPath((Join-Path $root $Application))
if (!(Test-Path -LiteralPath (Join-Path $app 'WinUI3\ModFolderCopier.WinUI.exe'))) {
    throw "No built application in $app. Run scripts\test-all.ps1 first."
}
$env:IMM_UI_APP = $app
$arguments = @('test', (Join-Path $root 'tests\UI.Tests\IntegratedModManager.UI.Tests.csproj'), '--configuration', 'Release', '--nologo',
    '--logger', 'console;verbosity=normal')
if ($Filter) { $arguments += @('--filter', $Filter) }
& dotnet @arguments
exit $LASTEXITCODE
