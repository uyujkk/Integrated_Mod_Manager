# Shared by package-release.ps1 and test-all.ps1: paths, relative to a release root,
# that belong to the user's runtime state and must never be packaged or staged.
function Test-IsUserStatePath {
    param([Parameter(Mandatory)] [string]$RelativePath)

    $normalized = $RelativePath.Replace('/', '\')
    $fileName = [IO.Path]::GetFileName($normalized)
    if ($fileName.EndsWith('.pdb', [StringComparison]::OrdinalIgnoreCase) -or
        $fileName.EndsWith('.log', [StringComparison]::OrdinalIgnoreCase) -or
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
