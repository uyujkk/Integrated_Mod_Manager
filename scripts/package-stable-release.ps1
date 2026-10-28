[CmdletBinding()]
param(
    [string]$Version = '4.6.0',
    [string]$BaseTag = 'v4.5.2',
    [string]$VerificationDirectory = 'artifacts\verification\v4.6.0',
    [switch]$ForGitHubRelease,
    [string]$CiUrl = '',
    # Only for machines without git: JSON exported by git elsewhere from this same
    # working tree (Commit, Branch, Dirty, BaseCommit, Files). The commit is checked
    # against .git directly and every listed file is hashed here.
    [string]$SourceInfoPath = ''
)

# Packages a GitHub stable release from the output of scripts\test-all.ps1. The ZIP is the
# tested runtime plus 7-Zip, user documentation, license files and PACKAGE-INFO.json.

$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^(?<core>\d+\.\d+\.\d+)$') { throw 'Version must look like 4.3.1.' }
$taskCoreVersion = $Matches.core
$taskFileVersion = $taskCoreVersion + '.0'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskBoundary = $taskRoot.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
function Resolve-TaskPath([string]$relative) {
    $full = [IO.Path]::GetFullPath((Join-Path $taskRoot $relative))
    if (!$full.StartsWith($taskBoundary, [StringComparison]::OrdinalIgnoreCase)) { throw 'Path escaped this checkout.' }
    return $full
}
function Invoke-Git { git -c "safe.directory=$($taskRoot.Replace('\','/'))" -c core.quotepath=false -C $taskRoot @args }
function Get-CheckedOutCommit {
    $gitDir = Join-Path $taskRoot '.git'
    $head = (Get-Content -LiteralPath (Join-Path $gitDir 'HEAD') -Raw).Trim()
    if ($head -notmatch '^ref: (?<ref>refs/.+)$') { return $head }
    $ref = $Matches.ref
    $loose = Join-Path $gitDir ($ref.Replace('/', '\'))
    if (Test-Path -LiteralPath $loose) { return (Get-Content -LiteralPath $loose -Raw).Trim() }
    foreach ($line in Get-Content -LiteralPath (Join-Path $gitDir 'packed-refs')) {
        if ($line -match ('^(?<sha>[0-9a-f]{40}) ' + [regex]::Escape($ref) + '$')) { return $Matches.sha }
    }
    throw ('Could not resolve ' + $ref)
}

$taskVerification = Resolve-TaskPath $VerificationDirectory
$taskSourceInfoOrigin = 'git'
if (Get-Command git -ErrorAction SilentlyContinue) {
    $taskSourceCommit = Invoke-Git rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Could not identify source commit.' }
    $taskSourceBranch = Invoke-Git branch --show-current
    $taskBaseCommit = Invoke-Git rev-parse --short "$BaseTag^{commit}"
    if ($LASTEXITCODE -ne 0) { throw ('Base tag not found: ' + $BaseTag) }
    $taskSourceDirty = @(Invoke-Git status --porcelain).Count -gt 0
    $taskSourceFiles = @(Invoke-Git ls-files --cached --others --exclude-standard | Sort-Object -Unique)
    if ($LASTEXITCODE -ne 0 -or $taskSourceFiles.Count -eq 0) { throw 'Could not inventory stable release source.' }
} else {
    if ([string]::IsNullOrWhiteSpace($SourceInfoPath)) { throw 'git is not available; pass -SourceInfoPath exported by git from this working tree.' }
    $exported = Get-Content -LiteralPath (Resolve-TaskPath $SourceInfoPath) -Raw -Encoding utf8 | ConvertFrom-Json
    $taskSourceCommit = [string]$exported.Commit
    if ($taskSourceCommit -notmatch '^[0-9a-f]{40}$' -or $taskSourceCommit -ne (Get-CheckedOutCommit)) { throw 'Exported source info does not match the checked-out commit.' }
    $taskSourceBranch = [string]$exported.Branch
    $taskBaseCommit = [string]$exported.BaseCommit
    $taskSourceDirty = [bool]$exported.Dirty
    $taskSourceFiles = @($exported.Files | ForEach-Object { [string]$_ } | Sort-Object -Unique)
    if ($taskSourceFiles.Count -eq 0) { throw 'Exported source info lists no files.' }
    $taskSourceInfoOrigin = 'git-export'
}
if ($ForGitHubRelease -and ($taskSourceDirty -or $CiUrl -notmatch '^https://github\.com/uyujkk/Integrated_Mod_Manager/actions/runs/\d+$')) {
    throw 'A GitHub stable release package requires committed clean source and its verified CI URL.'
}

$taskComponents = @(
    @{Name='core';Assembly='IntegratedModManager.Core';Line=0.90;Branch=0.85},
    @{Name='datastore';Assembly='IntegratedModManager.Data';Line=0.70;Branch=0.60},
    @{Name='updater';Assembly='IntegratedModManager.UpdateAgent.Core';Line=0.60;Branch=0.55},
    @{Name='endfield-launch';Assembly='IntegratedModManager.EndfieldLaunch';Line=0.75;Branch=0.65}
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
foreach ($relative in @('IntegratedModManager.exe','ModFolderCopier.exe','LocalUpdateAgent.exe','WinUI3\ModFolderCopier.WinUI.exe')) {
    $info = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $taskReleaseSource $relative))
    if ($info.FileVersion -ne $taskFileVersion -or $info.ProductVersion -ne $Version) { throw ('Wrong stable version: ' + $relative) }
}

$taskPackageName = "Integrated_Mod_Manager-v$Version.zip"
$taskOutput = Resolve-TaskPath ('artifacts\stable\' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,6))
$taskApp = Join-Path $taskOutput "IntegratedModManager-$Version"
New-Item -ItemType Directory -Path $taskApp | Out-Null
$taskVerifiedZip = Join-Path $taskVerification "ReleasePackage\Integrated_Mod_Manager-v$taskCoreVersion.zip"
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
# The package as a whole is AGPL-3.0-only; the root keeps the MIT notice for MIT files
# and explains the split. Full texts are also under WinUI3\licenses.
foreach ($file in @('LICENSE','docs\LICENSING.md')) {
    Copy-Item -LiteralPath (Join-Path $taskRoot $file) -Destination $taskApp
}
Copy-Item -LiteralPath (Join-Path $taskRoot 'src\IntegratedModManager.EndfieldLaunch\LICENSE') -Destination (Join-Path $taskApp 'LICENSE-AGPL-3.0.txt')

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
$taskStartGuide = [IO.File]::ReadAllText((Join-Path $taskRoot 'docs\guides\开始使用.md'), [Text.Encoding]::UTF8)
$taskStartGuide = [regex]::Replace($taskStartGuide, '\]\((?![a-zA-Z][a-zA-Z0-9+.-]*:|#|/)', '](docs/guides/')
foreach ($taskStartName in @('开始使用.md', 'README.md')) {
    [IO.File]::WriteAllText((Join-Path $taskApp $taskStartName), $taskStartGuide, [Text.UTF8Encoding]::new($false))
}
$taskMinor = ($taskCoreVersion -split '\.')[0..1] -join '.'
[IO.File]::WriteAllText((Join-Path $taskApp 'docs\README.md'),
    "# $taskMinor 正式版文档`n`n当前版本以 [$Version 说明](releases/v$Version.md) 为准；基础手册和历史发布说明保留用于查阅。`n`n[快速上手](guides/zh-CN/快速上手.md) · [用户手册](guides/zh-CN/用户手册.md) · [快捷键与浮窗](guides/zh-CN/快捷键与浮窗.md) · [状态槽位](guides/zh-CN/状态槽位.md) · [组合预设](guides/zh-CN/组合预设.md) · [浏览与下载](guides/zh-CN/浏览与下载.md) · [内置终末地启动](guides/zh-CN/终末地启动.md)`n`nEnglish: [Quick Start](guides/en/Quick-Start.md) · [User Guide](guides/en/User-Guide.md)`n",
    [Text.UTF8Encoding]::new($false))

$taskSourceHashes = foreach ($relative in $taskSourceFiles) {
    $file = Resolve-TaskPath $relative
    if (!(Test-Path -LiteralPath $file -PathType Leaf)) { throw ('Missing source: ' + $relative) }
    (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash + '  ' + $relative
}
$taskSourceManifest = Join-Path $taskOutput 'source-files.sha256'
[IO.File]::WriteAllLines($taskSourceManifest,[string[]]$taskSourceHashes,[Text.UTF8Encoding]::new($false))
$taskInfo = [ordered]@{
    Version=$Version;FileVersion=$taskFileVersion;Platform='win-x64';Channel=$(if ($ForGitHubRelease) {'github-stable'} else {'local-stable-validation'})
    License='AGPL-3.0-only (package as a whole); MIT for files outside the Endfield launch component; see LICENSING.md'
    BaseTag=$BaseTag;BaseCommit=$taskBaseCommit;SourceCommit=$taskSourceCommit;SourceBranch=$taskSourceBranch;SourceHasUncommittedChanges=$taskSourceDirty;SourceInfoOrigin=$taskSourceInfoOrigin
    SourceFingerprint=(Get-FileHash -LiteralPath $taskSourceManifest -Algorithm SHA256).Hash
    RuntimeSha256=(Get-FileHash -LiteralPath (Join-Path $taskApp 'WinUI3\ModFolderCopier.WinUI.dll') -Algorithm SHA256).Hash
    BuiltAt=[DateTimeOffset]::Now.ToString('O');Tests=@($taskResults);AppSelfUpdateEnabled=$true
    RealGameAndLoaderValidated=$false;CIUrl=$(if ($ForGitHubRelease) {$CiUrl} else {'not-run'});PublishedAtBuildTime=$false
}
$taskInfo | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $taskApp 'PACKAGE-INFO.json') -Encoding utf8
$taskTotal = ($taskResults | ForEach-Object { $_.Passed } | Measure-Object -Sum).Sum
$taskValidationScope = if ($ForGitHubRelease) {
    "- 对应源码提交已通过 GitHub Build and Test：$CiUrl 。本包用于 GitHub 正式发布，上传后另行下载公开附件并核对 SHA-256。"
} else {
    '- 本地正式版包未运行远端 CI，也未发布到 GitHub。'
}
$taskStatus = @"
# $Version 验证状态

- 本地完整测试：$taskTotal 项通过，0 失败，0 跳过；Core、数据存储、更新器和终末地启动组件的覆盖率均达到项目门槛。
- Release/x64 主程序、启动器和更新器已构建，文件版本统一为 $taskFileVersion，产品版本为 $Version。
- 打包时检查必需运行文件、许可证文件、用户状态排除、文件清单及 ZIP SHA-256。
- 尚未在真实游戏和 XXMI / EFMI 上走完整启动链；本版本其余验证边界见发布说明和验证记录（docs/releases、docs/verification）。该包是正式发布，真实环境验证范围以这些记录为准。
- 本正式版启用软件更新检查，遵循已保存的自动检查与手动检查偏好。
- 许可证：包含终末地启动组件（AGPL-3.0-only），整个发布包按 AGPL-3.0-only 分发；见 LICENSING.md。
$taskValidationScope

精确版本、组件测试与覆盖率、源码指纹和运行程序集哈希见 PACKAGE-INFO.json。压缩包旁的 .sha256 用于核对整包。
"@
[IO.File]::WriteAllText((Join-Path $taskApp '验证状态.md'),$taskStatus,[Text.UTF8Encoding]::new($false))

$taskPackageOutput = Join-Path $taskOutput 'Package'
& (Join-Path $taskRoot 'scripts\package-release.ps1') -SourceDirectory $taskApp.Substring($taskBoundary.Length) `
    -OutputDirectory $taskPackageOutput.Substring($taskBoundary.Length) -PackageName $taskPackageName | Out-Host
if (!(Test-Path -LiteralPath (Join-Path $taskPackageOutput "$taskPackageName.sha256"))) { throw 'Package checksum missing.' }
[ordered]@{Application=(Join-Path $taskApp 'IntegratedModManager.exe');Package=(Join-Path $taskPackageOutput $taskPackageName);SourceManifest=$taskSourceManifest;Output=$taskOutput;TestsPassed=$taskTotal} |
    ConvertTo-Json | Tee-Object -FilePath (Join-Path $taskOutput 'delivery.json')
