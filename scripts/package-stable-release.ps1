[CmdletBinding()]
param(
    [string]$VerificationDirectory = 'artifacts\verification\v4.2.0',
    [switch]$ForGitHubRelease,
    [string]$CiUrl = ''
)

$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskBoundary = $taskRoot.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
function Resolve-TaskPath([string]$relative) {
    $full = [IO.Path]::GetFullPath((Join-Path $taskRoot $relative))
    if (!$full.StartsWith($taskBoundary, [StringComparison]::OrdinalIgnoreCase)) { throw 'Path escaped this checkout.' }
    return $full
}
$taskVerification = Resolve-TaskPath $VerificationDirectory
$taskSourceCommit = git -c "safe.directory=$($taskRoot.Replace('\','/'))" -C $taskRoot rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Could not identify source commit.' }
$taskSourceBranch = git -c "safe.directory=$($taskRoot.Replace('\','/'))" -C $taskRoot branch --show-current
$taskSourceDirty = @(git -c "safe.directory=$($taskRoot.Replace('\','/'))" -C $taskRoot status --porcelain).Count -gt 0
if ($ForGitHubRelease -and ($taskSourceDirty -or $CiUrl -notmatch '^https://github\.com/uyujkk/Integrated_Mod_Manager/actions/runs/\d+$')) {
    throw 'A GitHub stable package requires committed clean source and its verified CI URL.'
}
$taskComponents = @(
    @{Name='core';Assembly='IntegratedModManager.Core';Line=0.90;Branch=0.85},
    @{Name='datastore';Assembly='IntegratedModManager.Data';Line=0.70;Branch=0.60},
    @{Name='updater';Assembly='IntegratedModManager.UpdateAgent.Core';Line=0.60;Branch=0.55}
)
$taskResults = foreach ($component in $taskComponents) {
    $resultRoot = Join-Path $taskVerification ('TestResults\' + $component.Name)
    [xml]$trx = Get-Content -LiteralPath (Join-Path $resultRoot ($component.Name + '-tests.trx'))
    $counts = $trx.TestRun.ResultSummary.Counters
    if ($trx.TestRun.ResultSummary.outcome -ne 'Completed' -or [int]$counts.total -le 0 -or
        [int]$counts.failed -ne 0 -or [int]$counts.passed -ne [int]$counts.total) { throw ('Tests not fully passed: ' + $component.Name) }
    $reports = @(Get-ChildItem -LiteralPath $resultRoot -Recurse -Filter coverage.cobertura.xml)
    # VSTest can retain both the collector original and an identical TRX attachment copy.
    $reportHashes = @($reports | ForEach-Object { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } | Select-Object -Unique)
    if ($reports.Count -eq 0 -or $reportHashes.Count -ne 1) { throw 'Expected one unambiguous coverage report per component.' }
    [xml]$coverage = Get-Content -LiteralPath $reports[0].FullName
    $assemblies = @($coverage.coverage.packages.package | Where-Object name -eq $component.Assembly)
    if ($assemblies.Count -ne 1) { throw 'Expected one matching coverage assembly.' }
    $line = [double]::Parse($assemblies[0].'line-rate', [Globalization.CultureInfo]::InvariantCulture)
    $branch = [double]::Parse($assemblies[0].'branch-rate', [Globalization.CultureInfo]::InvariantCulture)
    if ($line -lt $component.Line -or $branch -lt $component.Branch) { throw ('Coverage gate failed: ' + $component.Name) }
    [ordered]@{Component=$component.Name;Passed=[int]$counts.passed;Failed=0;Skipped=0;LineCoverage=$line;BranchCoverage=$branch}
}
$taskReleaseSource = Join-Path $taskVerification 'ReleaseSource'
$taskRuntime = Join-Path $taskReleaseSource 'WinUI3\ModFolderCopier.WinUI.exe'
foreach ($relative in @('IntegratedModManager.exe','ModFolderCopier.exe','LocalUpdateAgent.exe','WinUI3\ModFolderCopier.WinUI.exe')) {
    $version = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $taskReleaseSource $relative))
    if ($version.FileVersion -ne '4.2.0.0' -or $version.ProductVersion -ne '4.2.0') { throw ('Wrong stable version: ' + $relative) }
}
$taskOutput = Resolve-TaskPath ('artifacts\stable\' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,6))
$taskApp = Join-Path $taskOutput 'IntegratedModManager-4.2.0'
New-Item -ItemType Directory -Path $taskApp | Out-Null
$taskVerifiedZip = Join-Path $taskVerification 'ReleasePackage\Integrated_Mod_Manager-v4.2.0.zip'
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::ExtractToDirectory($taskVerifiedZip, $taskApp)
if ((Get-FileHash -LiteralPath (Join-Path $taskApp 'WinUI3\ModFolderCopier.WinUI.dll')).Hash -ne
    (Get-FileHash -LiteralPath (Join-Path $taskReleaseSource 'WinUI3\ModFolderCopier.WinUI.dll')).Hash) { throw 'Verified ZIP differs from the tested runtime.' }
$taskTools = Join-Path $taskApp 'WinUI3\Tools'
New-Item -ItemType Directory -Path $taskTools -Force | Out-Null
foreach ($tool in @('7z.exe','7z.dll','License.txt')) {
    $from = Join-Path 'C:\Program Files\7-Zip' $tool
    $to = Join-Path $taskTools $(if ($tool -eq 'License.txt') { '7zip-License.txt' } else { $tool })
    Copy-Item -LiteralPath $from -Destination $to
}
Copy-Item -LiteralPath (Join-Path $taskRoot 'LICENSE') -Destination $taskApp
Copy-Item -LiteralPath (Join-Path $taskRoot '开始使用.md') -Destination $taskApp

# Ship user documentation and public release history, without following source-code links.
foreach ($folder in @('docs\guides','docs\releases','docs\assets','docs\verification')) {
    $sourceRoot = Join-Path $taskRoot $folder
    foreach ($file in Get-ChildItem -LiteralPath $sourceRoot -Recurse -File) {
        if ($file.Extension -notin @('.md','.png','.jpg','.jpeg','.svg','.gif','.webp')) { continue }
        $relative = $file.FullName.Substring($taskBoundary.Length)
        $target = Join-Path $taskApp $relative
        New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($target)) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target
    }
}
$taskBundleContract = Join-Path $taskApp 'docs\development\Combination-Bundles.md'
New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($taskBundleContract)) -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $taskRoot 'docs\development\Combination-Bundles.md') -Destination $taskBundleContract
Copy-Item -LiteralPath (Join-Path $taskRoot '开始使用.md') -Destination (Join-Path $taskApp 'README.md')
[IO.File]::WriteAllText((Join-Path $taskApp 'docs\README.md'), "# 4.2 正式版文档`n`n当前版本以 [4.2 正式版说明](releases/v4.2.0.md) 为准；基础手册和历史发布说明保留用于查阅。`n`n[用户手册](guides/用户手册.zh-CN.md) · [浏览下载与旧库整理](guides/Browse-Download-Task-Pack.zh-CN.md) · [组合预设](guides/Combination-Presets.zh-CN.md) · [桥接器](guides/Endfield-Bridge.md)`n", [Text.UTF8Encoding]::new($false))
$taskSourceFiles = @(git -c "safe.directory=$($taskRoot.Replace('\','/'))" -c core.quotepath=false -C $taskRoot ls-files --cached --others --exclude-standard | Sort-Object -Unique)
if ($LASTEXITCODE -ne 0 -or $taskSourceFiles.Count -eq 0) { throw 'Could not inventory integration source.' }
$taskSourceHashes = foreach ($relative in $taskSourceFiles) {
    $file = Resolve-TaskPath $relative
    if (!(Test-Path -LiteralPath $file -PathType Leaf)) { throw ('Missing source: ' + $relative) }
    (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash + '  ' + $relative
}
$taskSourceManifest = Join-Path $taskOutput 'source-files.sha256'
[IO.File]::WriteAllLines($taskSourceManifest,[string[]]$taskSourceHashes,[Text.UTF8Encoding]::new($false))
$taskInfo = [ordered]@{
    Version='4.2.0';FileVersion='4.2.0.0';Platform='win-x64';Channel=$(if ($ForGitHubRelease) {'github-stable'} else {'local-stable-validation'})
    BaseCommit='7d0ed62';SourceCommit=$taskSourceCommit;SourceBranch=$taskSourceBranch;SourceHasUncommittedChanges=$taskSourceDirty
    SourceFingerprint=(Get-FileHash -LiteralPath $taskSourceManifest -Algorithm SHA256).Hash
    RuntimeSha256=(Get-FileHash -LiteralPath (Join-Path $taskApp 'WinUI3\ModFolderCopier.WinUI.dll') -Algorithm SHA256).Hash
    BuiltAt=[DateTimeOffset]::Now.ToString('O');Tests=@($taskResults);AppSelfUpdateEnabled=$true
    NativeCrashRootCauseConfirmed=$false;CIUrl=$(if ($ForGitHubRelease) {$CiUrl} else {'not-run'});InPlaceUpgradeAcceptance='recorded-externally';PublishedAtBuildTime=$false
}
$taskInfo | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $taskApp 'PACKAGE-INFO.json') -Encoding utf8
$taskTotal = ($taskResults | ForEach-Object { $_.Passed } | Measure-Object -Sum).Sum
$taskValidationScope = if ($ForGitHubRelease) {
    "- 对应源码提交已通过 GitHub Build and Test：$CiUrl 。本包用于 GitHub 正式发布，上传后另行下载公开附件并校验。真实游戏/加载器验收尚未完成。"
} else {
    '- 本地包尚未运行远端 CI 或真实游戏/加载器验收，也未发布到 GitHub。'
}
$taskStatus = @"
# 4.2 整合包验证状态

- 本地完整测试：$taskTotal 项通过，0 失败，0 跳过；Core、数据存储和更新器覆盖率均通过项目既定门槛。
- Release/x64 主程序、启动器和更新器已构建，文件版本统一为 4.2.0.0，产品版本为 4.2.0。
- 打包时检查必需运行文件、用户状态排除、文件清单及 ZIP SHA-256。
- 既有功能已保留；4.2 增加稍后下载批量清单、按仓库保存浏览进度、收藏和最近作者管理、本地批量标签与保存筛选。队列恢复、导入恢复和诊断功能继续保留。
- 当前 4.2 最终 ZIP 的独立解压和桌面启动结果另附本地验收记录；不要将上述旧样本检查等同于完整 4.2 验收。
- 详情页原生闪退根因未确认。正式发布仍保留这一已知问题；详见 docs/releases/v4.2.0.md。
- 原地覆盖升级、配置保留与回退结果见本次外部验收记录。
$taskValidationScope

精确版本、组件测试与覆盖率、源码指纹和运行程序集哈希见 PACKAGE-INFO.json。压缩包旁的 .sha256 用于核对整包。
"@
[IO.File]::WriteAllText((Join-Path $taskApp '验证状态.md'),$taskStatus,[Text.UTF8Encoding]::new($false))
$taskPackageOutput = Join-Path $taskOutput 'Package'
& (Join-Path $taskRoot 'scripts\package-release.ps1') -SourceDirectory $taskApp.Substring($taskBoundary.Length) `
    -OutputDirectory $taskPackageOutput.Substring($taskBoundary.Length) -PackageName 'Integrated_Mod_Manager-v4.2.0.zip' | Out-Host
if (!(Test-Path -LiteralPath (Join-Path $taskPackageOutput 'Integrated_Mod_Manager-v4.2.0.zip.sha256'))) { throw 'Package checksum missing.' }
[ordered]@{Application=(Join-Path $taskApp 'IntegratedModManager.exe');Package=(Join-Path $taskPackageOutput 'Integrated_Mod_Manager-v4.2.0.zip');SourceManifest=$taskSourceManifest;Output=$taskOutput;TestsPassed=$taskTotal} | ConvertTo-Json | Tee-Object -FilePath (Join-Path $taskOutput 'delivery.json')
