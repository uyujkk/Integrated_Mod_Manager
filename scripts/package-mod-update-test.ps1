[CmdletBinding()]
param([string]$VerificationDirectory='artifacts\verification\v4.3.1-test.1-final')
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$boundary=$root.TrimEnd('\')+'\'
$verification=[IO.Path]::GetFullPath((Join-Path $root $VerificationDirectory))
if(!$verification.StartsWith($boundary,[StringComparison]::OrdinalIgnoreCase)){throw 'Verification path escaped checkout'}
$version='4.3.1-test.1'
$source=Join-Path $verification 'ReleaseSource'
$out=Join-Path $root ('artifacts\mod-update-test\'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
$app=Join-Path $out '运行目录'
$results=foreach($component in @('core','datastore','updater','endfield-launch')){
 [xml]$trx=Get-Content (Join-Path $verification "TestResults\$component\$component-tests.trx")
 $counts=$trx.TestRun.ResultSummary.Counters
 if($trx.TestRun.ResultSummary.outcome -ne 'Completed' -or [int]$counts.passed -ne [int]$counts.total -or [int]$counts.total -le 0){throw "Tests not fully passed: $component"}
 [ordered]@{Component=$component;Passed=[int]$counts.passed;Failed=[int]$counts.failed;Skipped=[int]$counts.notExecuted}
}
foreach($file in @('IntegratedModManager.exe','ModFolderCopier.exe','LocalUpdateAgent.exe','WinUI3\ModFolderCopier.WinUI.exe')){
 $info=[Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $source $file))
 if($info.FileVersion -ne '4.3.1.0' -or $info.ProductVersion -ne $version){throw "Version mismatch: $file"}
}
New-Item -ItemType Directory -Path $out | Out-Null
Copy-Item -LiteralPath $source -Destination $app -Recurse
foreach($folder in @('guides','releases','assets','verification','development')){
 $from=Join-Path $root ('docs\'+$folder)
 foreach($file in Get-ChildItem $from -Recurse -File){
  if($file.Extension -notin @('.md','.png','.jpg','.jpeg','.svg','.gif','.webp')){continue}
  $relative=$file.FullName.Substring($boundary.Length)
  $target=Join-Path $app $relative
  New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($target)) -Force | Out-Null
  Copy-Item $file.FullName $target
 }
}
foreach($file in @('LICENSE','LICENSING.md','开始使用.md')){Copy-Item (Join-Path $root $file) $app}
Copy-Item (Join-Path $root 'src\IntegratedModManager.EndfieldLaunch\LICENSE') (Join-Path $app 'LICENSE-AGPL-3.0.txt')
New-Item -ItemType Directory -Path (Join-Path $app 'WinUI3\Tools') -Force | Out-Null
foreach($file in @('7z.exe','7z.dll','License.txt')){
 $name=if($file -eq 'License.txt'){'7zip-License.txt'}else{$file}
 Copy-Item (Join-Path 'C:\Program Files\7-Zip' $file) (Join-Path $app ('WinUI3\Tools\'+$name))
}
New-Item -ItemType Directory -Path (Join-Path $app 'docs\verification') -Force | Out-Null
Copy-Item (Join-Path $root 'docs\verification\v4.3.1-update-fix.md') (Join-Path $app 'docs\verification')
$notes=@'
# Mod 更新修复测试版 4.3.1-test.1

这是本地测试包，未发布到 GitHub，不替换旧版。软件自身更新已关闭；Mod 更新功能保留。

先退出旧版，完整解压后运行根目录 IntegratedModManager.exe。建议先使用独立测试库。需要迁移设置时，先退出新旧程序并备份，再复制 WinUI3/config.ini 与 WinUI3/beta-shell.json；复制后核对仓库路径。

## 本次测试

1. 在“更新”页反复切换“仅手动 / 每天 / 每 3 天 / 每周”，确认程序保持运行，退出重启后设置保留。
2. 检查有更新的 Mod，确认应用内弹窗提供“查看更新”和“稍后”。按频率启动检查发现更新时同样提醒。
3. 点更新卡“下载并替换”，选择具体文件／变体，检查原路径后确认。此操作完整替换原 Mod 文件夹，不合并新旧文件；请选完整版本包，补丁包应取消并按作者说明手动安装。
4. 检查下载进度、新版文件、原跟踪记录的更新时间及“打开旧版备份”。旧文件含自行修改的 INI，保存在仓库旁 .imm-mod-update-backups/<批次>/original；同批次 update.json 保留原路径和跟踪记录。备份不会自动清理。
5. 下载或解压时可取消；若原 Mod 在确认后被修改，替换会停止。旧版备份应保留原始内容。中断的替换必须重新检查并确认，不会自动重放。

目录联接保持原 Mod 路径，后续使用新版文件；复制到加载器 Mods 目录的副本需要重新部署。本轮未启动真实游戏，也没有验证游戏内效果。回退 Mod 时先退出相关程序，将当前新版移到其他目录，再把对应 original 备份放回 update.json 记录的原路径，避免直接合并覆盖。需要恢复旧跟踪信息时，保留 update.json 并反馈。

反馈请注明：全新或迁移配置、切换选项、Mod 条目和选定文件、完整操作步骤、错误提示与发生时间。日志位于运行目录 WinUI3/app-errors.log 和 startup.log。
'@
[IO.File]::WriteAllText((Join-Path $app '测试说明.md'),$notes,[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $app 'README.md'),$notes,[Text.UTF8Encoding]::new($false))
$files=@(git -c core.quotepath=false -C $root ls-files --cached --others --exclude-standard | Sort-Object -Unique)
$hashes=foreach($file in $files){(Get-FileHash (Join-Path $root $file)).Hash+'  '+$file}
[IO.File]::WriteAllLines((Join-Path $out 'source-files.sha256'),[string[]]$hashes,[Text.UTF8Encoding]::new($false))
$info=[ordered]@{Version=$version;FileVersion='4.3.1.0';Channel='local-test';BaseCommit='51fb293543853426e7fbc53dbe1d650d143e355f';SourceCommit=(git -C $root rev-parse HEAD);SourceBranch=(git -C $root branch --show-current);SourceHasUncommittedChanges=(@(git -C $root status --porcelain).Count -gt 0);SourceFingerprint=(Get-FileHash (Join-Path $out 'source-files.sha256')).Hash;RuntimeSha256=(Get-FileHash (Join-Path $app 'WinUI3\ModFolderCopier.WinUI.dll')).Hash;Tests=@($results);AppSelfUpdateEnabled=$false;ModUpdatesEnabled=$true;RealGameValidated=$false;RemoteCI='not-run';Published=$false;License='AGPL-3.0-only as a whole; see LICENSING.md'}
$info | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $app 'PACKAGE-INFO.json') -Encoding utf8
& (Join-Path $PSScriptRoot 'package-release.ps1') -SourceDirectory $app.Substring($boundary.Length) -OutputDirectory (Join-Path $out 'Package').Substring($boundary.Length) -PackageName 'Integrated_Mod_Manager-v4.3.1-test.1-ModUpdates-win-x64.zip' | Out-Host
$delivery=[ordered]@{Application=(Join-Path $app 'IntegratedModManager.exe');Package=(Join-Path $out 'Package\Integrated_Mod_Manager-v4.3.1-test.1-ModUpdates-win-x64.zip');Output=$out;TestsPassed=($results | ForEach-Object { $_.Passed } | Measure-Object -Sum).Sum}
$delivery | ConvertTo-Json | Tee-Object -FilePath (Join-Path $out 'delivery.json')
