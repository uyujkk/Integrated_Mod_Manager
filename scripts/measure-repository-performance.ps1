[CmdletBinding()]
param(
    [string]$FixtureDirectory = 'artifacts\repository-scan-fixtures',
    [string]$OutputDirectory = 'artifacts\repository-performance-20261005'
)
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$taskBoundary = $taskRoot.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
$taskFixtures = [IO.Path]::GetFullPath((Join-Path $taskRoot $FixtureDirectory))
$taskOutput = [IO.Path]::GetFullPath((Join-Path $taskRoot $OutputDirectory))
foreach ($taskPath in @($taskFixtures, $taskOutput)) {
    if (!$taskPath.StartsWith($taskBoundary, [StringComparison]::OrdinalIgnoreCase)) { throw 'Benchmark paths must stay in this repository.' }
    for ($taskParent = $taskPath; $taskParent.StartsWith($taskBoundary, [StringComparison]::OrdinalIgnoreCase);
        $taskParent = [IO.Path]::GetDirectoryName($taskParent)) {
        if ((Test-Path -LiteralPath $taskParent) -and
            ((Get-Item -LiteralPath $taskParent -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw 'Benchmark root cannot pass through a directory link.'
        }
    }
}
if (Test-Path -LiteralPath $taskOutput) { throw 'Results already exist; select a new output directory.' }
& dotnet run --project (Join-Path $taskRoot 'RepositoryPerformance\RepositoryPerformance.csproj') -c Release -- $taskFixtures $taskOutput
if ($LASTEXITCODE -ne 0) { throw "Repository benchmark failed: $LASTEXITCODE" }
