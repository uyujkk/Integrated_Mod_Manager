[CmdletBinding()]
param([switch]$IncludeLiveDownloadProbe)

$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskRuntime = Join-Path $taskRoot 'src\WinUI3\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64'
if (!(Test-Path -LiteralPath (Join-Path $taskRuntime 'ModFolderCopier.WinUI.exe'))) {
    throw 'Build the WinUI Release x64 application before creating this fixture.'
}
$taskFixture = Join-Path $taskRoot ('artifacts\browse-feedback-preview\' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 6))
$taskApp = Join-Path $taskFixture 'App'
$taskRepo = Join-Path $taskFixture 'MockRepository'
New-Item -ItemType Directory -Path $taskApp, $taskRepo | Out-Null
New-Item -ItemType Directory -Path (Join-Path $taskRepo 'Fixture Character') | Out-Null
$taskOldLibrary = Join-Path $taskFixture 'OldModLibrary'
foreach ($taskName in @('Old Mod A', 'Old Mod B')) {
    $taskOldMod = Join-Path $taskOldLibrary $taskName
    New-Item -ItemType Directory -Path $taskOldMod -Force | Out-Null
    '[Constants]' + "`n" + 'global persist $variant = 1' | Set-Content -LiteralPath (Join-Path $taskOldMod 'mod.ini') -Encoding utf8
    'Synthetic fixture only.' | Set-Content -LiteralPath (Join-Path $taskOldMod 'readme.txt') -Encoding utf8
}
Get-ChildItem -LiteralPath $taskRuntime -Recurse -File | ForEach-Object {
    $taskRelative = $_.FullName.Substring($taskRuntime.Length + 1)
    if ($taskRelative -match '(^|[\\/])(cache|backups|logs)([\\/]|$)' -or
        $_.Name -in @('config.ini', 'beta-shell.json', 'startup.log') -or $_.Extension -in @('.pdb', '.log', '.bak')) { return }
    $taskDestination = Join-Path $taskApp $taskRelative
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($taskDestination)) -Force | Out-Null
    Copy-Item -LiteralPath $_.FullName -Destination $taskDestination
}
$taskShell = @{
    CurrentPrimarySection = 'online'; SelectedRepositoryId = 'browse-fixture'
    CheckAppUpdatesOnStartup = $false; UpdateCheckInterval = 'manual'; ModUpdateCheckInterval = 'manual'
    ReduceMotion = $true; MinimizeToTray = $false; OnlineCardLayout = 'list'
    WindowWidth = 1420; WindowHeight = 920
    Repositories = @(@{
        Id = 'browse-fixture'; Name = 'Browse feedback - mock data'; SourcePath = $taskRepo
        TargetPath = ''; OnlineSourceSite = 'GameBanana'; OnlineGameName = 'Browse Fixture'
        OnlineCategoryId = '999999'; WikiUrl = ''; Notes = 'Synthetic data for UI checks. No real Mods.'
    })
}
$taskShell | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $taskApp 'beta-shell.json') -Encoding utf8
$taskCards = @(1..40 | ForEach-Object {
    @{
        ItemId = 0; Title = ('Browse fixture {0:D2}' -f $_); CharacterName = 'Fixture Character'
        RootCategoryName = 'Skins'; Author = $(if ($_ % 3 -eq 0) { '画师甲' } elseif ($_ % 2 -eq 0) { 'Alice Works' } else { 'Bob Studio' })
        AuthorId = $(if ($_ % 3 -eq 0) { 700000003 } elseif ($_ % 2 -eq 0) { 700000002 } else { 700000001 })
        Likes = $_; Views = 100 + $_; Downloads = 1000 - $_; HotnessScore = 10 - ($_ / 100.0)
        ProfileUrl = ('https://example.invalid/mods/{0}' -f $_)
        DownloadUrl = ('https://example.invalid/files/{0}.zip' -f $_)
        UpdatedAt = [DateTimeOffset]::UtcNow.ToString('O'); DownloadFileName = 'fixture.zip'
    }
})
if ($IncludeLiveDownloadProbe) {
    $taskCards[0] = @{
        ItemId=720357; Title='Live verification - select modini.zip (2.8 KiB)'; CharacterName='Fixture Character'
        RootCategoryName='Skins'; Author='Live verification'; AuthorId=0; Likes=1; Views=1; Downloads=1; HotnessScore=100
        ProfileUrl='https://gamebanana.com/mods/720357'; DownloadUrl='https://gamebanana.com/dl/1851735'
        DownloadFileName='modini.zip'; DownloadFileId='1851735'; FileSizeBytes=2865; UpdatedAt=[DateTimeOffset]::UtcNow.ToString('O')
        DownloadFiles=@(@{FileId='1851735';FileName='modini.zip';DownloadUrl='https://gamebanana.com/dl/1851735';FileSizeBytes=2865;IsSupportedArchive=$true})
    }
}
$taskPage = @{ CachedAtUtc = [DateTimeOffset]::UtcNow.ToString('O'); Mods = $taskCards; TotalCount = 40; Page = 1; PageSize = 40 }
$taskCacheKey = 'category-v3|999999'
$taskHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($taskCacheKey))).ToLowerInvariant().Substring(0, 16)
$taskCache = Join-Path $taskApp 'cache\online-pages'
New-Item -ItemType Directory -Path $taskCache -Force | Out-Null
$taskPage | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $taskCache ($taskHash + '-page-1.json')) -Encoding utf8
foreach ($taskAuthor in @(@{ Id=700000001; Name='Bob Studio' }, @{ Id=700000002; Name='Alice Works' }, @{ Id=700000003; Name='画师甲' })) {
    foreach ($taskPageNumber in 1..2) {
        $taskAuthorCards = @($taskCards | ForEach-Object {
            $taskCopy = $_.Clone()
            $taskCopy.AuthorId = $taskAuthor.Id; $taskCopy.Author = $taskAuthor.Name
            $taskCopy.Title = $taskAuthor.Name + ' page ' + $taskPageNumber + ' - ' + $_.Title
            $taskCopy.ProfileUrl = $_.ProfileUrl + '/author-' + $taskAuthor.Id + '/page-' + $taskPageNumber
            $taskCopy
        })
        $taskAuthorKey = 'category-v3|999999|author=' + $taskAuthor.Id
        $taskAuthorHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($taskAuthorKey))).ToLowerInvariant().Substring(0,16)
        @{ CachedAtUtc=[DateTimeOffset]::UtcNow.ToString('O'); Mods=$taskAuthorCards; TotalCount=80; Page=$taskPageNumber; PageSize=40 } |
            ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $taskCache ($taskAuthorHash + '-page-' + $taskPageNumber + '.json')) -Encoding utf8
    }
}
Write-Output ([PSCustomObject]@{ App = (Join-Path $taskApp 'ModFolderCopier.WinUI.exe'); MockRepository = $taskRepo; OldModLibrary = $taskOldLibrary; Fixture = $taskFixture } | ConvertTo-Json)
