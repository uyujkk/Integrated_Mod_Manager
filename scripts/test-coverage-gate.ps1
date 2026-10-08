[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskTokens = $null
$taskErrors = $null
$taskAst = [Management.Automation.Language.Parser]::ParseFile(
    (Join-Path $PSScriptRoot 'test-all.ps1'), [ref]$taskTokens, [ref]$taskErrors)
if ($taskErrors.Count -gt 0) { throw 'Cannot parse test-all.ps1.' }
$taskGate = $taskAst.Find({ param($node)
    $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Assert-CoverageThreshold'
}, $true)
if (!$taskGate) { throw 'Coverage gate function not found.' }
# Load only the trusted local function, never execute test-all's top-level
# restore/build/delete commands from this unit-style check.
. ([scriptblock]::Create($taskGate.Extent.Text))
$taskReports = Join-Path $taskRoot ('artifacts\coverage-gate-checks\' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskReports -Force | Out-Null
$taskCases = @(
    @{Name='component-passes-low-aggregate'; Xml='<coverage line-rate="0.1" branch-rate="0.1"><packages><package name="Other" line-rate="0" branch-rate="0"/><package name="Expected" line-rate="0.8" branch-rate="0.7"/></packages></coverage>'; Pass=$true},
    @{Name='component-fails-high-aggregate'; Xml='<coverage line-rate="0.99" branch-rate="0.99"><packages><package name="Expected" line-rate="0.5" branch-rate="0.5"/></packages></coverage>'; Pass=$false},
    @{Name='missing-component'; Xml='<coverage><packages><package name="Other" line-rate="1" branch-rate="1"/></packages></coverage>'; Pass=$false},
    @{Name='duplicate-component'; Xml='<coverage><packages><package name="Expected" line-rate="1" branch-rate="1"/><package name="Expected" line-rate="1" branch-rate="1"/></packages></coverage>'; Pass=$false}
)
foreach ($taskCase in $taskCases) {
    $taskDirectory = Join-Path $taskReports $taskCase.Name
    New-Item -ItemType Directory -Path $taskDirectory | Out-Null
    [IO.File]::WriteAllText((Join-Path $taskDirectory 'coverage.cobertura.xml'), $taskCase.Xml)
    $taskPassed = $false
    try {
        Assert-CoverageThreshold -ResultsDirectory $taskDirectory -AssemblyName Expected -MinimumLineRate 0.7 -MinimumBranchRate 0.6
        $taskPassed = $true
    } catch {
        if ($taskCase.Pass) { throw }
    }
    if ($taskPassed -ne $taskCase.Pass) { throw "Unexpected coverage decision: $($taskCase.Name)" }
    Write-Output ("Coverage gate check passed: " + $taskCase.Name)
}
