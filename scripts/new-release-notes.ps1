[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$OutputPath
)

# Writes the GitHub release body from docs\releases\v<version>.md: the heading is dropped (the
# release has its own title) and relative links point at the tagged files on GitHub.
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$notesPath = Join-Path $root "docs\releases\v$Version.md"
if (!(Test-Path -LiteralPath $notesPath)) { throw "Release notes are missing: docs\releases\v$Version.md" }

$blob = "https://github.com/uyujkk/Integrated_Mod_Manager/blob/v$Version/"
$lines = [IO.File]::ReadAllLines($notesPath, [Text.Encoding]::UTF8)
$start = 0
if ($lines.Count -gt 0 -and $lines[0].StartsWith('# ')) { $start = 1 }
while ($start -lt $lines.Count -and [string]::IsNullOrWhiteSpace($lines[$start])) { $start++ }
$body = ($lines[$start..($lines.Count - 1)] -join "`n") + "`n"

# Relative links resolve from docs\releases; anchors, absolute URLs and site-rooted links stay as they are.
$body = [regex]::Replace($body, '\]\((?![a-zA-Z][a-zA-Z0-9+.-]*:|#|/)(?<path>[^)#\s]+)(?<anchor>#[^)\s]*)?\)', {
    param($match)
    $parts = [System.Collections.Generic.List[string]]::new()
    foreach ($part in ('docs/releases/' + $match.Groups['path'].Value).Split('/')) {
        if ($part -eq '..') { if ($parts.Count -gt 0) { $parts.RemoveAt($parts.Count - 1) } }
        elseif ($part -ne '.' -and $part -ne '') { $parts.Add($part) }
    }
    '](' + $blob + ($parts -join '/') + $match.Groups['anchor'].Value + ')'
})

$output = [IO.Path]::GetFullPath((Join-Path $root $OutputPath))
New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($output)) -Force | Out-Null
[IO.File]::WriteAllText($output, $body, [Text.UTF8Encoding]::new($false))
Write-Output $output
