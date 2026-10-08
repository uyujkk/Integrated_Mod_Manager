# Incremental optimization / 分步优化

Historical opt1–opt11 development sequence. For the consolidated 4.1 code and verification boundaries, read [Source Optimization Record](./Source-Optimization-v4.1.md). 下文保留各阶段当时的版本和本地开发限制；当前源码优化总览见链接，不将历史“未推送”状态当成当前状态。

## Scope / 范围

Development branch: `optimization/repository-scan`, based on stable 4.0.0.
本轮基于 4.0.0 的独立开发分支，不改变已发布包、不推送、不改变用户配置格式。开发验证不触碰真实 Mod 或加载器文件；用户明确确认恢复时，仍沿用定向回写 `d3dx_user.ini`，不回写原始 Mod INI。

This is a local development checkpoint, not a new public release. The application file version remains 4.0.0; distinguish this build by its separately named package and folder.
这是本地开发检查点，不是新的正式版。文件版本保持 4.0.0，通过独立目录和测试包名称区分，不覆盖正式安装目录。

## Sequence / 顺序

1. Establish a repeatable regression baseline and extract repository scanning/indexing. / 建立回归基线，提取仓库扫描与索引。
2. Add preset restore-plan preview and missing/ambiguous item warnings, preserving targeted d3dx restoration. / 为预设增加恢复前预览、缺失和歧义提示，保持定向回写。
3. Profile large repositories in visible native windows; optimize actual bottlenecks. / 在实际窗口中测大仓库，按测量结果优化。
4. Extract download/online services and page state in small slices. / 逐步分离下载、在线服务和页面状态。
5. Add portable preset export/import after identity matching and rollback are verified. / 匹配和回滚验证后，再做可迁移预设导入导出。

Each slice requires tests, build, package checks and its own manual checklist before release. Avoid a simultaneous UI, schema and deployment rewrite.
每步独立验证后再考虑发布，不同时重写界面、数据结构和部署流程。

## First slice / 第一阶段改动

- `RepositoryScanService` in Core owns two-level directory enumeration, sorting, cache reuse, byte counts and warnings; it has no WinUI, SQLite, deployment or INI dependencies.
- Core 中的扫描服务负责两级目录、排序、缓存复用、文件大小和警告，与窗口、SQLite、部署和参数恢复分离。
- A Data adapter preserves the existing SQLite schema and `IndexedModFolder` compatibility. / 数据适配层沿用原表结构。
- Partial/cancelled scans do not replace a complete index. Root failure is reported as failure, not a successful empty repository. / 不完整或已观察到取消的扫描不覆盖完整索引；根目录失败不伪装成空仓库。
- Refresh cancellation is cooperative; a filesystem operation/SQLite write already in progress cannot be interrupted instantly. Generation and path guards still prevent stale UI results. / 取消是协作式的，已开始的文件系统操作或数据库写入不能立即打断，代次和路径检查继续拦截旧界面结果。
- Corrupt cache metadata is rescanned, including total bytes; unreadable children generate diagnostics while readable siblings remain visible. / 缓存损坏后重扫并重新统计大小，单个目录不可读不阻断其他目录。
- A new refresh clears the old searchable category collection, so failed path changes cannot resurrect old repository entries via search. / 刷新时同时清除旧搜索数据，避免路径切换失败后搜索复活旧条目。
- Coverage gates now select the documented assembly rather than a dependency-wide aggregate; original thresholds are unchanged and the gate has four independent checks. / 覆盖率门槛按指定程序集检查，避免传递依赖干扰；没有降低门槛，另加四项检查。

## Second slice / 第二阶段改动

- Read-only `CombinationRestorePreviewService` aggregates missing, disabled, unsafe, duplicate, ambiguous and mismatched-state problems before a deployment transaction. / 核心层只读预览汇总缺失、禁用、不安全、重复、同名歧义和快照不匹配，先检查再备份执行。
- Existing strict deployment and persistent-state engines remain authoritative. No automatic renaming, partial recovery or fallback to Mod defaults. / 沿用严格部署与参数引擎，不自动改名、不跳过问题局部恢复、不拿默认值代替快照。
- The bounded preview dialog has complete Mod, parameter and check tabs. Lists scroll locally and use ListView virtualization; no 20-item truncation. / 有界弹窗分三标签，完整清单在内部滚动并虚拟化，不截断 20 项。
- Parameter rows show old → saved values, including added and unchanged keys, from the same byte snapshot used to prepare the patch. / 参数展示当前值到保存值，区分新增和已一致，使用同一原始字节快照。
- Check copied deployments' declarations before starting. Revalidate the approved deployment, external-directory inventory, declarations and loader bytes after every confirmation dialog and before backups. / 已部署副本也先验证声明；所有确认弹窗之后、备份之前再次核对计划、外部目录清单、声明及加载器字节。
- Missing/unsafe items disable restore. Users still acknowledge closing the game and loader; this is not an automatic process lock or proof that the game is closed. / 有阻断问题时禁用恢复；保留手动确认退出游戏和加载器，不宣称实现进程锁或自动检测退出。

## Third slice / 第三阶段改动

- Optional bounded local timing scopes separate scan, projection, refresh, population, filter and cover-layout work. No diagnostics marker means disabled. / 可选本地计时区分六类操作，没有诊断标记则关闭。
- A separate developer benchmark compares isolated SQLite cold/warm scans and collection event counts at 100/500/1,000 Mods; no claim about rendered frame rates. / 独立开发基准测 SQLite 冷/热扫描和集合事件数量，不声称验证帧率。
- Batch snapshot application replaces per-item Clear/Add storms; unchanged snapshots notify nothing. Queue revisions avoid a duplicate filter after an explicit refresh. / 整批快照减少逐项通知，相同结果不通知，代次检查避免显式刷新后的重复过滤。
- See [Repository Performance](./Repository-Performance.md) for exact scope and limitations. / 具体范围和边界见性能诊断说明。

## Fourth slice, transport checkpoint / 第四阶段传输检查点

- Core archive transport now owns streaming, response checks, progress throttling and owned-partial cleanup; the window owns selection, destination confirmation, extraction and tracking. / Core 提取流式下载、响应检查、节流和临时文件清理，窗口保留选择、目录确认、解压与记录。
- HTTP names become safe leaf names; download and extension correction no longer overwrite existing files. Tasks retain the actual corrected path. / HTTP 名称统一为文件名，下载及改扩展名不覆盖原文件，任务保存实际路径。
- 50 new offline HTTP/stream tests, including a real synthetic ZIP + Chinese character-route smoke test. Live website/WinUI flow remains separate. / 新增 50 项离线测试，真实模拟 ZIP 验证中文角色归类；不代替网站/窗口实测。
- See [download service](./Online-Download-Service.md). This is the first transport slice, not a completed extraction of all online/page state. / 仅完成传输切片，不声称在线和页面状态已全部拆分。

## Fourth slice, execution checkpoint / 第四阶段任务执行检查点

Fourth-stage execution checkpoint (opt5): captured repository context, explicit acquired-resource cleanup, prepared preview/shortcut metadata and synchronous record commit. Valid installations in different repositories/folders retain independent tracking records; stale duplicate cleanup is scoped. 19 additional tests, 509 total. See [execution contracts](./Online-Download-Execution.md). Live WinUI download/cancel checks are not yet claimed.
第四阶段任务执行检查点 opt5：固定仓库上下文、明确本次资源清理、暂存图片和快捷键信息再提交；不同仓库/目录的有效安装记录独立保留。新增 19 项测试，共 509 项，未声称完成网站和窗口取消实测。

## Metadata checkpoint (opt6) / 元数据检查点

Core Mod details/file metadata service and parser, bounded/cancellable HTTP, expiring details memory cache, and Data raw-cache adapter are now connected to the existing WinUI adapters. Old caches are read-only fallbacks; file lists stay live. 69 additional tests, 578 total. See [metadata contracts](./Online-Metadata-Service.md). The whole online feed/category/avatar/translation/page-state layer is not yet extracted.
已接入详情/文件元数据服务与解析器，限制请求、检查详情缓存有效期；旧缓存只读回退，文件列表实时读取。新增 69 项，共 578 项；不声称完成全部在线功能拆分。

## Page-state checkpoint (opt7) / 页面状态检查点

Revision-owned detail requests and captured repository/language/card guards now prevent stale publish/error/final callbacks. Default/manual/card downloads share a preflight gate and per-source/repository attempt lock, released after task completion; different tasks can continue concurrently. The actual XAML-independent adapter source is linked into 29 offline tests, 607 total. See [page-state contracts](./Online-Page-State.md). No claim of full MainWindow/XAML, native picker or live API validation.
详情按代次管理，关闭、切仓库和语言后丢弃旧回调；下载共用准备入口并按来源与仓库防重，创建任务后允许其他任务继续。新增 29 项，共 607 项；不声称全部窗口和网站实测完成。

## Portable bundle checkpoint (opt8) / 可迁移组合包检查点

The expanded fifth slice adds versioned combination ZIPs containing selected saved parameters and corresponding Mod files. Export streams/hashes to an owned partial; import validates in same-volume staging, shows new/reuse/conflict decisions, installs without overwriting existing Mods and persists a new profile before committing ownership. Optional restore follows the existing approved deployment + targeted d3dx transaction. Cancellation, rollback and strict manifest/path/state checks have offline coverage. See [bundle contracts](./Combination-Bundles.md) and [opt8 evidence](../verification/combination-bundle-opt8-20261007.md). Native pickers/layout and real-game workflow remain pending; no public release/update-channel change.
第五阶段按本次需求扩展为“组合参数 + Mod 文件”的 ZIP。已接入导出、预览、安装保存及可选恢复，复用原有部署与参数事务；没有覆盖已发布包。剩余工作为窗口/缩放与性能矩阵、隔离实际流程、获批准后的发布候选验证。

## Deliberately unchanged policies / 保持不变的策略

Opt9 begins native bundle validation. A constructor crash missed by headless tests was reproduced and fixed by keeping the Create control in its original owner. Bundle commands moved to a shared toolbar, short-window recovery sizing and localized fragment separators now have regression tests. Native demo import/save/approved restore and restart were verified in isolated mock directories. The broader DPI, performance and failure matrix is still pending; no real game/loader or release channel was changed. See [opt9 checkpoint](../verification/combination-bundle-opt9-20261007.md).
opt9 已开始组合包窗口实测，修复构造崩溃、底部按钮挤出及中文换行丢失，并在模拟目录完成安装、保存、确认恢复和重启检查。完整缩放/性能/失败矩阵仍待补齐，不等于真实游戏验证。

Opt10 extends the isolated native checks: install-only leaves deployment/state untouched, a repeated bundle reuses all eight files and saves a uniquely named/identified profile, cancel after import commit retains the profile, and one content conflict disables whole-bundle installation. Two offline cross-repository re-export cases cover saved-state/default separation and repeated reuse. Native export completion is still pending at the owned folder-picker automation boundary, as are large-copy/config-failure and full DPI checks. See [opt10 evidence](../verification/combination-bundle-opt10-20261007.md).
opt10 补查只安装不恢复、重复导入复用与唯一预设、提交后取消恢复，以及单项冲突阻止整包；另加带/不带参数的跨仓库再次导出测试。窗口导出确认、复制中取消、配置写入失败和完整缩放仍待验证，不改变发布边界。

Opt11 consolidates the four requested QA areas into one local checkpoint: user-assisted native bundle export/reimport, configuration-save rollback, stale-preview refusal and large validation cancellation; bounded themed dialogs, local preview/cover stream repair and short-window online layout; repeated service benchmarks and a 10-category/1,000-Mod selection round trip; live newest/manual downloads, Chinese character routing, HD preview, installed/tracked state and download-body cancellation. The current build passes 747 tests, coverage gates, WinUI x64 and clean-package checks. See [combined regression](../verification/combined-regression-opt11-20261007.md). Complete DPI, folder-move/close/committed-restore failures, concurrent/network-failure and approved real-game/upgrade checks remain distinct release gates. This checkpoint does not publish a version.
opt11 将四项合并实测并修复发现的问题，完成用户协助的组合包导出回读、保存失败回滚、过期计划拒绝、大包验证取消，以及实际在线下载/角色归类/高清预览/安装追踪/下载中取消。短窗布局与弹窗预算按观察调整，747 项测试及干净包门槛通过；完整缩放、移动/关窗取消、提交后恢复失败、网络并行及真实游戏/升级仍单列，不假称全部通过。


- Files/Covers/Presets layout, existing selection restoration and sorting culture. / 三视图、按路径恢复选择和排序语言规则。
- Two-level category/Mod convention, including `DISABLED` folder visibility in the browser. This is not the stricter deployment planner. / 浏览仍包含禁用目录，不与部署规划器的过滤规则混为一谈。
- Only immediate files are indexed. A directory timestamp is not a content hash, recursive stamp or file-integrity proof. In-place edits can leave cached size data stale until invalidated. / 只索引直接文件，目录时间戳不保证内容与嵌套文件新鲜度。
- Existing copy/link deployment, targeted parameter writes, backups and rollback remain; preset schema is unchanged. / 保留复制/联接、定向参数回写、备份和回滚，预设格式不变。
- No online synchronization, injection, automated game keys or automatic game repair. / 不新增云同步、注入、自动游戏按键或自动修复。

## Checks / 验证

The user authorized a 4.1 stable package/publication on 2026-10-07. The release consolidates these checkpoints and the restored-window Settings repair; [4.1 verification](../verification/v4.1.0.md) lists current evidence and remaining limits. Historical paragraphs above describe the earlier local checkpoints, not the current release channel.
2026-10-07 用户已授权打包发布 4.1。本版合并上述检查点和非最大化设置页修复；当前验证范围见 4.1 记录。上方历史段落保留当时的本地开发事实，不表示当前渠道。

See [Regression Checklist](./Regression-Checklist.md) for synthetic and real-environment checks. Local timings are diagnostic samples, not universal performance promises or evidence of native-window smoothness.
操作检查见[回归清单](./Regression-Checklist.md)。模拟目录耗时只作本地参考，不代表实际界面流畅度或所有硬盘性能。

Stage 3 has a 1,000-Mod single-category native-window checkpoint at actual 150% scaling and five-pair service benchmarks; see [opt3 evidence](../verification/repository-performance-opt3-20261005.md). Stage 4 has transport, execution/metadata-commit, metadata and headless page-state checkpoints. Stage 5 has the portable combination-bundle checkpoint. Next: broader native window/scaling/performance matrix and isolated workflow validation; compilation and adapter tests do not prove frame rates or live website/game effects. Public release remains a separate approval boundary.
第三阶段完成单分类 1,000 项、150% 窗口及服务层基准；第四阶段完成传输、执行/提交、元数据与无窗口状态检查点，第五阶段已加入组合包。下一步为完整窗口/性能矩阵及隔离流程实测，发布仍需明确批准。
