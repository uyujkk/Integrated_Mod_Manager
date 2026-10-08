# Download execution opt5 / 下载任务执行验证

Date: 2026-10-05. Local `optimization/repository-scan` checkpoint with accumulated stage 1–4 changes; no Git push, release, stable replacement or automatic-update channel change. File versions remain 4.0.0.0.
第四阶段第二小步；保留前序本地改动，不推送、不发布、不覆盖正式版、不改自动更新渠道。

## Implementation / 实现

- Capture repository ID/path/name, game metadata mode and character aliases before asynchronous download-info refresh/manual selection. Destination UI uses that context instead of the later active repository.
- 第一次异步等待前固定仓库与游戏/角色归类信息，目录确认使用固定上下文。
- Core `OnlineDownloadSession` tracks acquired direct-child archive/extraction paths, validates cleanup scope, and separates asynchronous preparation from a synchronous non-awaiting commit.
- 明确登记本次直接子项资源；区分异步准备与同步提交。
- Preview and shortcut preparation no longer writes partial source/link/shortcut install records. Cancellation before commit skips those records. Tracking-config failure after installation is a warning; post-commit refresh/notification errors cannot delete the installation.
- 预览和快捷键准备阶段不再写一半的安装记录；提交前取消不写记录，配置保存失败显示警告，完成后刷新失败不删除结果。
- Independent valid local copies retain tracking records. Only missing duplicate records within a repository/parent scope are deduplicated. Most-specific nested repository root wins.
- 有效安装副本独立保留，仅清理同一范围内的失效重复记录。
- Dispose removed finished task cancellation sources; guard the cancel action after commit starts. Old progress callbacks remain governed by the existing phase policy.
- 清理已移除任务的取消资源；提交开始后不再接受取消；旧进度回调仍受现有阶段规则限制。

## Automated evidence / 自动证据

Final run: `artifacts/optimization-execution-verified-opt5-r2-20261005`.
Log: `artifacts/optimization-execution-opt5-r2-build.log`.

| Suite | Passed | Failed |
| --- | ---: | ---: |
| Core | 473 | 0 |
| DataStore | 9 | 0 |
| UpdateAgent | 27 | 0 |
| Total | **509** | **0** |

19 new execution/scope cases preserve the previous 490 tests. A preliminary targeted run passed 13 cases; six additional cases were added before the final full rerun. Core line/branch coverage: 96.0% / 91.6%; Data: 87.0% / 82.7%; Updater: 60.2% / 60.6%. All component gates and script-level gate self-checks passed. These are component test metrics, not whole-UI coverage.
新增 19 项、保留原 490 项；最终重跑全部通过。覆盖率是组件指标，不是完整 UI 覆盖率。

Cases cover captured context during delayed preparation, cancellation before/after preparation, late cancellation during commit, preparation/commit exceptions, overlapping preparation, actual corrected archive ownership, rejection of parent/sibling/nested cleanup claims, nested roots, independent existing copies and stale record scope. Three cases create real temporary text-only files, apply the cleanup plan, and assert existing/committed files survive. They do not run WinUI, an archive tool, a real website, launcher or game.
三项真实模拟文件测试执行清理并核对保留结果；其余验证会话/范围约定，不运行窗口、外部解压工具、网站或游戏。

WinUI x64 Release: zero warnings/errors. Version consistency, minimal runtime ZIP contract, managed manifest and SHA-256 sidecar checks passed. NuGet vulnerability audit was disabled for offline verification; this is not a vulnerability-audit pass. No dependency versions changed.
WinUI 构建、版本、精简包及校验契约通过；离线验证关闭 NuGet 漏洞审计，不声称漏洞审计通过。

Clean runtime package SHA-256:
`E40D1C72495D149746C719EE04B3736F11B3CC62E1EBFFA581D41308C701CF8F`.

## Local test deliverable / 本地测试交付

- Package: `artifacts/local-test-online-execution-opt5-20261005/Integrated_Mod_Manager-v4.0.0-execution-opt5.zip`.
- SHA-256: `B633E7C1D0B7539D873DCF7C8C446BAED66504233A5353E4747FC11E31FD3D91`.
- Extracted entry: `artifacts/local-test-online-execution-opt5-20261005/App/IntegratedModManager.exe`.
- Clean package source: `artifacts/local-test-online-execution-source-opt5-20261005`, copied from the final verified clean runtime, not a previously used app directory. Includes a cold text-only TestData fixture from the previous unlaunched opt4 source plus new LOCAL_TEST.md; no user config or performance.enabled marker.
- All four executable versions verified as 4.0.0.0. There are 1,000 synthetic sample.ini files in the bundled mock repository. No real launcher or game assets are included.

The earlier opt5 preliminary verification output is superseded by the r2 run. The local app was extracted but **not launched** in this turn. No new native-window/download/game success is claimed.
以 r2 最终验证为准；应用已解压但本轮未启动，不声称新的原生窗口、下载或游戏实测成功。

Stable ZIP SHA-256 remained:
`6099EC3CB19BAA655F0E2509246E313C1714A702FB33A2BEB952302901935E31`.

## Remaining limits / 保留边界

See [execution contracts and manual checks](../development/Online-Download-Execution.md). External extraction/scanning cancellation is cooperative. Translation cache work can finish after the caller stops waiting. Ownership checks are lexical plus an existence check, not cross-process filesystem locking. Metadata commit is not a multi-file disk transaction. Live website/manual-picker behavior, restart/readback, installed badges, denied-write warnings and visible cancel timing still need isolated WinUI verification.
取消、并发文件系统修改、配置事务及窗口实测边界见说明。下一小步为元数据读取/缓存拆分和实际编排离线夹具，之后再进入可迁移预设。
