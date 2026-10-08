# Regression checklist / 回归清单

Keep the stable package, real repositories and game installation untouched. Use a separately extracted local test application and G-drive fixture folders first.
保留正式包及实际游戏目录，先使用独立解压的测试应用和 G 盘测试目录。清单中“待验证”不得记作成功。

## Automated / 自动验证

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-all.ps1 -ArtifactsDirectory artifacts\optimization-verified-20261005
```

Choose a new artifacts directory for subsequent runs to retain earlier evidence. The script replaces its chosen output directory; never point it at an application, repository fixture or historic release directory.
后续验证使用新的输出目录以保留证据。脚本会替换所选输出目录，不要指向应用、测试仓库或历史发行目录。

Tests must cover cold/warm scans, sorting, empty/disabled folders, rename/removal, corrupt caches, inaccessible children, root failure, cancellation, SQLite compatibility and repository isolation.
覆盖冷/热扫描、排序、空及禁用目录、改名删除、缓存损坏、访问失败、取消、SQLite 兼容和仓库隔离。

## Synthetic native-window checks / 模拟目录窗口检查

2026-10-05 opt3 follow-up: a **single-category, 1,000-Mod** text-only fixture passed first load, search/clear, Files/Covers round trip, local cover scrolling, refresh selection retention, maximized/restored layout at actual 150% scaling and normal close. This is narrower than the multi-size/multi-repository matrix below; those rows remain pending. See [opt3 evidence](../verification/repository-performance-opt3-20261005.md).
第三阶段已检查单分类 1,000 项模拟仓库的上述操作；下表完整多档/多仓库矩阵仍待验证，不能将本次结果扩展到所有布局及真实数据。

Generate fixtures without overwriting existing directories:
生成模拟目录（已有目录会拒绝，不覆盖）：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\new-repository-scan-fixture.ps1 -ModCount 100
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\new-repository-scan-fixture.ps1 -ModCount 500
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\new-repository-scan-fixture.ps1 -ModCount 1000
```

Configure a new repository in the isolated test app using each generated `Source` and `Target`. Do not select a real launcher: these are text-only synthetic fixtures, not playable Mods.
在独立测试应用中新建测试仓库，使用输出的 Source 和 Target。不要选择真实启动器：模拟数据不是真正游戏 Mod。

| Check / 操作 | Expected / 预期 | Status / 状态 |
| --- | --- | --- |
| First/second refresh / 首次及再次刷新 | 10 categories; exact Mod count; no duplicate entries / 分类 10 个，数量准确，无重复 | Pending / 待验证 |
| Files/Covers/Presets / 三视图切换 | Same selected path, no accidental deployment / 选择路径一致，不误部署 | Pending / 待验证 |
| Fast repository changes / 快速切仓库 | Old scan cannot replace the current list / 旧结果不能覆盖当前仓库 | Pending / 待验证 |
| Missing source then search / 无效源路径后搜索 | Clear error, no old categories reappear / 明确失败，不复活旧分类 | Pending / 待验证 |
| Rename/add/remove / 改名增删目录 | Next refresh reflects actual directories / 刷新反映当前目录 | Pending / 待验证 |
| Search, local scroll, resize / 搜索、模块滚动、缩放 | Correct selection, usable layout / 选择与布局正常 | Pending / 待验证 |
| Minimize/tray/Alt+Tab / 托盘与窗口切换 | Selection unchanged; no unexpected Mod operations / 不改变选择或误触操作 | Pending / 待验证 |
| Close during scan / 扫描中关闭 | No unhandled error or stale window update / 不出现未处理错误 | Pending / 待验证 |

## Combination preview checks / 组合预览检查

2026-10-05 follow-up: opt2 English synthetic preview binding was observed with 33 inventory items / 64 unchanged values; Restore was disabled before acknowledgement and Escape returned to normal navigation. No restore was executed. Other changes/missing/stale/compact/language branches below remain pending; see the opt3 report.
已补查 opt2 英文模拟预览绑定和取消，未执行恢复；修改参数、缺失、过期计划及双语言窄窗口等完整矩阵仍待验证。

The local opt2 package includes **synthetic** `TestData` (32 Mods / 64 values) and resolves its offline repository at first run. Do not copy real user configuration into it or select a real launcher. You can also generate separate fixtures:
opt2 本地包带有 **模拟** `TestData`（32 个 Mod / 64 个参数），首次启动会解析离线测试仓库。不要复制真实配置，不要选择真实启动器。也可单独生成：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\new-combination-preview-fixture.ps1 -ModCount 32
```

| Check / 操作 | Expected / 预期 | Status / 状态 |
| --- | --- | --- |
| Save preset including state, then preview / 保存带状态的组合并预览 | 32 Mods + external keep folder; 64 unchanged values; no write merely opening preview / 32 个 Mod 和外部保留目录、64 个已一致值；预览本身不写文件 | Pending / 待验证 |
| Change one fixture value in `TestData/Loader/d3dx_user.ini` with editor, then preview / 用编辑器改一个模拟参数再预览 | One changed old → saved row; source INI defaults remain untouched / 显示一行旧到保存值，源 INI 默认值不动 | Pending / 待验证 |
| Cancel preview / 取消预览 | No deployment changes or new backups / 不改部署、不生成备份 | Pending / 待验证 |
| Confirm closed checkbox / 勾选退出确认 | Restore enabled only when all checks pass / 无阻断问题时才能恢复 | Pending / 待验证 |
| Restore modified synthetic state / 恢复修改后的模拟参数 | Value restored, exact `.bak` retained, external value stays 99 / 选定值恢复、备份保留、外部值仍为 99 | Pending / 待验证 |
| Move two source Mod folders out of fixture repository, preview saved preset / 将两个模拟源 Mod 移出仓库后预览 | Both missing items listed; cannot restore; restore folders afterward / 同时显示两个缺失项、禁用恢复，检查后移回目录 | Pending / 待验证 |
| Edit fixture state while preview stays open / 预览打开时改模拟参数文件 | Confirm refuses stale preview, no transaction started / 确认拒绝过期计划、不开始事务 | Pending / 待验证 |
| Chinese/English, wide/compact window, actual scaling / 双语言、宽窄窗口、实际缩放 | Complete rows, internal scrolling, readable dialog buttons and checkbox / 清单完整、内部滚动、按钮和退出确认可见 | Pending / 待验证 |

Core tests verify 100-Mod/100-value plans, encoding preservation, strict matching, read-only preparation, stale approval rejection and exact state rollback. They do not verify WinUI row binding, virtualization behavior or visible layout. The preview is not a filesystem/process lock, content-integrity scan or automatic game-closed detector. An already copied folder is still identified by the existing folder-name policy, not a file-content hash.
核心测试覆盖 100 Mod / 100 参数计划、编码保留、严格匹配、只读预览、过期计划拒绝和精确参数回滚，不证明实际 WinUI 绑定、虚拟化或可见排版。预览不是文件系统/进程锁、内容完整性检查或自动退出检测。已部署副本仍沿用原来的目录名识别，不是文件内容哈希识别。

## Real-environment checks / 真实环境检查

## Combination bundle native checks (opt9–opt11, partial) / 组合包窗口检查（部分完成）

Use only the independently packaged app and its mock `TestData`. The demo ZIP has 8 Mods/16 values and no game assets. Set `ImportRepository`, `ImportLoader/Mods` and `ImportLoader/d3dx_user.ini` explicitly before import.
只使用独立测试程序与模拟数据；先在仪表板选择空的导入仓库和模拟加载器参数文件。

| Action / 操作 | Expected / 预期 | Status / 状态 |
| --- | --- | --- |
| Install the demo bundle, accept preview, continue to approved restore / 导入示例包、确认安装并进入恢复确认 | 8 new Mod folders, a new preset, 16 selected numeric values; unrelated 99 preserved / 8 个目录、新预设、16 参数，无关值 99 保留 | Passed opt9 synthetic native; file/hash/config readback |
| Cancel the picker or installation preview / 取消文件选择或预览 | No installed folders/preset; buttons unlock / 无安装和预设新增，按钮解锁 | File/folder picker and blocked preview cancellation passed opt10; unblocked two-reuse preview cancellation passed opt11 |
| Install with restore option unchecked / 取消勾选恢复后安装 | Files/preset saved; loader target/state unchanged / 保存文件和预设，不改部署与参数 | Passed opt10 synthetic native: 8 source files, 8 members/16 values, empty target, unchanged state SHA-256 |
| Import the same ZIP again / 再次导入相同包 | All files reused, new uniquely named profile / 全部复用，预设名追加序号 | Passed opt10 synthetic native: 8 reuse rows, two distinct profile IDs, second name `(2)` |
| Modify one imported Mod file then preview again / 修改一项文件再导入 | Conflict blocks whole import; other Mods not partially installed / 冲突阻止整包，无部分跳过 | Passed opt10 synthetic native: 1 conflict/7 reuse, install disabled; cancel preserves files and two profiles |
| Export selected preset after updating saved state / 更新保存状态后导出 | Unique ZIP outside repository; reimport matches saved state / 唯一文件名，可回读保存状态 | Native export + reimport of deliberately different saved snapshot passed opt11, user-assisted folder confirmation; separate UI Update Preset sequence pending |
| Cancel while copying/hashing, close window, retry / 复制哈希时取消、关窗后重试 | Pre-commit new unchanged folders rolled back; reused folders retained / 提交前清理本次未被外部修改的新文件，复用项保留 | 810 MiB ZIP validation cancellation passed opt11 at 15%; destination-move/close checks pending |
| Deny configuration save in an isolated app copy / 独立副本中模拟配置写入失败 | Profile not retained, owned new Mod folders rolled back; warnings if cleanup fails / 不保留预设，回滚本次新文件，清理失败有提示 | Passed opt11 native: eight new files rolled back after error acknowledgement, no new profile, two existing profiles retained; read-only flag restored |
| Change repository after approved preview / 确认预览后改仓库文件 | Stale decision refused before commit / 提交前拒绝旧计划 | Passed opt11 native: reused INI edit refused, profile/other file/loader unchanged; original fixture bytes restored |
| Cancel/fail restore after import commit / 导入成功后取消或失败恢复 | Imported files/preset remain available for retry / 导入文件和预设仍保留 | Cancel passed opt10 synthetic native: two profiles retained, target empty/state unchanged; failure branch pending |
| Chinese/English, wide/narrow, 100/150/200% scale / 双语言、宽窄窗和缩放 | Actions and dialogs visible, lists scroll inside only / 按钮可见，仅清单内部滚动 | Partial opt11: Chinese/light wide and English/dark short dialogs observed; full real-DPI/Settings/Presets matrix pending |

The opt8 baseline added 82 offline cases for bundle/file/state contracts; opt10 adds two more. These cases do not prove native-window checks. ZIP integrity is not authentication, compatibility, malware scanning or a promise of account safety. Export includes all selected Mod files; inspect private content and redistribution permissions before sharing.

Opt10 adds two offline cross-repository re-export cases (with and without state). Native export completion/reimport remains pending because the owned system folder picker could not be confirmed through the bounded computer-use API; export cancellation was verified. See [opt10 evidence](../verification/combination-bundle-opt10-20261007.md). No real Mod/game directory or released package was changed.
opt10 新增带/不带参数的跨仓库再次导出回归；系统文件夹选择器确认受自动化边界限制，不能用服务测试代替窗口导出成功。已记录取消解锁结果，真实目录及正式包未改动。

Opt11 supersedes the pending export-completion boundary above: the user confirmed the folder picker and native export/reimport then succeeded. See [combined evidence](../verification/combined-regression-opt11-20261007.md) for exact byte/hash/config readbacks, save-failure rollback, validation cancellation, stale-preview rejection and qualified layout observations. Earlier opt9/10 reports retain their historical results.
opt11 已由用户协助确认系统文件夹选择器后完成窗口导出回读；不将此写成完全自动化，也不将验证中取消扩展为目录移动/关窗取消。

## Isolated live-online checkpoint (opt11) / 隔离实际在线检查

| Action / 操作 | Result / 结果 | Boundary / 边界 |
| --- | --- | --- |
| Newest/manual archive picker / 最新与手选压缩包 | Six files, newest current selected by default; archived selection completed | One dated GameBanana entry, not every provider/format |
| Character destination / 角色目录 | Snowshine matched `昼雪`; archive and extraction stayed in that category | Isolated repository, no launcher or deployment target |
| Images and installed state / 图片和安装状态 | Avatars, online images, 2560 × 1440 local preview and Installed label observed; startup readback retained records | Not game rendering/compatibility |
| Immediate tracking refresh / 追踪立即刷新 | Multiple independent installations appeared without restart after commit | No forced metadata-persistence failure in live download |
| Download body cancellation / 下载中取消 | Native task canceled before completion; no partial/fourth extraction, three existing records preserved | Earlier completed attempts are not cancellation passes |
| Retry after cancel / 取消后重试 | Entry unlocked; retry completed in unique fourth folder/third latest archive, four saved records read back | Genuine network-failure/concurrent-task matrix still pending |

The local clean package excludes downloaded Mod files, QA configuration, caches and fixtures. Offline tests/build/package checks total 747 cases; the live and pending scopes are recorded separately.

## Remaining real-environment checks / 其他真实环境检查

Opt4 transport has 50 offline tests (fake HTTP plus real synthetic ZIP routing/readback). Live online UI checks below are **not** marked passed by those tests. See [opt4 evidence](../verification/online-download-opt4-20261005.md).
opt4 传输的 50 项离线测试不能将下方实际网络/窗口项目标为通过。

Opt6 adds 54 metadata Core and 15 cache DataStore tests (578 total local regression cases), including a service + real SQLite readback fixture. This is not a restarted native app, live API or full window orchestration test. Add isolated manual checks for newest/manual file selection after repeated details loads, old-cache preservation, cancellation during details/body reads, repository switches, and actual restart/installed-state readback. See [opt6 evidence](../verification/online-metadata-opt6-20261005.md).
opt6 的服务与 SQLite 回读测试不等于应用重启或窗口端到端验证；上述实际网络/窗口检查仍待完成。

Opt7 adds 29 headless cases that link the production state-adapter source (607 total). Native checks still pending: rapidly reopen the same Mod, close during metadata/translation, change repository/language, repeat latest/manual/card downloads, cancel a picker, retry after failure and verify another task's completion cannot unlock the current attempt. See [opt7 evidence](../verification/online-state-opt7-20261007.md).
opt7 直接测试实际状态适配器，不代表 XAML/系统弹窗实测；上述快速切换、防重复、取消重试与并行任务的窗口检查仍待完成。

These checks are not automated by the fixture script. Back up first; test deployment in copies or dedicated loader folders. Never infer real-game compatibility from synthetic data.
这些项目不由模拟脚本自动执行。先备份，在副本或专用目录测试部署；模拟数据通过不代表游戏兼容。

- Local import, character routing, copy/link deployment, removal and undo. / 导入、归类、复制/联接、移除和撤销。
- Live online download, multiple-file selection, extracted character destination, HD preview, installed state and cancellation. / 实际在线下载、文件选择、角色目录、预览、安装识别和取消。
- Save combination A, switch to B, restore A; verify enabled folders and selected d3dx values, and unchanged unrelated values. / A/B 组合切换并核对参数及无关参数保留。
- Missing/renamed/updated Mods fail visibly instead of guessing restoration targets. / 缺失、改名、更新后不得猜测恢复目标。
- Application upgrade retains repositories, favorites, presets and user files; failed upgrade rolls back. / 升级保留数据，失败回滚。

Record date, source revision, package hash, window size/scaling, environment, expected/actual result and failure evidence. Keep private paths/account details out of public reports.
记录时间、源码版本、包哈希、窗口大小和缩放、预期/实际结果及失败证据。公开报告去除私人路径和账号信息。
