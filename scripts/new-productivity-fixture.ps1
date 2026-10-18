[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskFixture = (& (Join-Path $PSScriptRoot 'new-online-browse-fixture.ps1') -IncludeLiveDownloadProbe | ConvertFrom-Json)
$taskApp = Split-Path -Parent $taskFixture.App
$taskShellPath = Join-Path $taskApp 'beta-shell.json'
$taskShell = Get-Content -LiteralPath $taskShellPath -Raw | ConvertFrom-Json -AsHashtable
$taskSecondRepo = Join-Path $taskFixture.Fixture 'SecondRepository'
foreach ($taskRepo in @($taskFixture.MockRepository, $taskSecondRepo)) {
    foreach ($taskMod in @('Test Mod A','Test Mod B')) {
        $taskFolder = Join-Path $taskRepo ('Fixture Character\' + $taskMod)
        New-Item -ItemType Directory -Path $taskFolder -Force | Out-Null
        '[Constants]' + "`n" + 'global persist $variant = 1' | Set-Content -LiteralPath (Join-Path $taskFolder 'mod.ini') -Encoding utf8
    }
}
$taskShell.Repositories += @{
    Id='productivity-second';Name='Second isolated repository';SourcePath=$taskSecondRepo;TargetPath='';
    OnlineSourceSite='GameBanana';OnlineGameName='Browse Fixture';OnlineCategoryId='999999';WikiUrl=''
}
$taskPagePath = Get-ChildItem -LiteralPath (Join-Path $taskApp 'cache\online-pages') -File | Where-Object {
    $taskPage = Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json
    $taskPage.Mods[0].ItemId -eq 720357 -and $taskPage.Mods[1].Title -eq 'Browse fixture 02'
} | Select-Object -First 1
$taskPage = Get-Content -LiteralPath $taskPagePath.FullName -Raw | ConvertFrom-Json -AsHashtable
$taskSavedCards = @($taskPage.Mods[0], $taskPage.Mods[1])
$taskSavedCards[1].DownloadFiles = @(@{ FileId='fixture-file';FileName='fixture.zip';DownloadUrl='https://example.invalid/fixture.zip';IsSupportedArchive=$true })
$taskShell.OnlinePreferences = @{
    ConcurrentDownloads=2;RememberDownloadFolders=$false;DownloadFolders=@{};BrowseSessions=@{};
    Authors=@(@{Id=700000002;Name='Alice Works';CategoryId='999999'});
    RecentAuthors=@(@{Id=700000001;Name='Bob Studio';CategoryId='999999';VisitedAt=[DateTimeOffset]::UtcNow.ToString('O')});
    SavedMods=@($taskSavedCards | ForEach-Object { @{RepositoryId='browse-fixture';RepositoryPath=$taskFixture.MockRepository;Mod=$_} })
}
$taskShell | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $taskShellPath -Encoding utf8
$taskFixture | Add-Member -NotePropertyName SecondRepository -NotePropertyValue $taskSecondRepo
$taskFixture | ConvertTo-Json
