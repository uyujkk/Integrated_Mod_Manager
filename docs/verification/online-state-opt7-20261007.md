# Online page-state opt7 verification / 在线页面状态 opt7 验证

Date: 2026-10-07. Local `optimization/repository-scan` checkpoint. No push, public release, automatic-update change, layout redesign or preset-schema change. All development artifacts remain on G:; preserved stable package is unchanged.
日期：2026-10-07。本地检查点，不推送、不发布、不修改自动更新、布局和预设格式；保留正式包。

## Scope / 范围

- `OnlinePageStateAdapter` owns detail-request revisions, cancellation and callback admission after open/load/projection awaits. Repository/language/card context guards are supplied by MainWindow. Same-ID reopen is a distinct request; stale publish/error/final callbacks are rejected.
- Detail close invalidates before animation delay. Repository/source edits share reset/cancellation; list cancellation increments its existing revision even when replacement loading exits early. Language toggle re-projects visible details. Hero-image revision is invalidated with detail reset.
- `OnlineDownloadActionAdapter` admits one preparing picker at a time; locks source identity in captured repository ID/path until the entire attempt ends. Default/manual/card paths share the gate. Task creation releases the global preparation gate so other Mods/scopes may continue.
- Detail loading no longer changes the global repository busy state. Concurrent online installations use a separate busy count. Manual metadata/picker results check current selection/context before use; existing destination routing and post-install repository checks remain.
- Detail/preflight reporting catches secondary dialog errors. No rewrite of every application-wide dialog or busy operation.

Contracts: [Online Page State](../development/Online-Page-State.md). Default newest archive, manual selection, character-folder routing, installed detection, archive safety, prepared metadata commit and targeted d3dx restoration retain their prior policies.

## Final automated checks / 最终自动验证

Final directory: `artifacts/optimization-online-state-verified-opt7-r2-20261007`.

Final log: `artifacts/optimization-online-state-opt7-r2-build.log`.

| Component | Passed | Failed / skipped | Line / branch coverage |
| --- | ---: | ---: | --- |
| Core test suite | 556 | 0 / 0 | Core 96.4% / 91.5% |
| DataStore | 24 | 0 / 0 | 88.6% / 89.1% |
| UpdateAgent | 27 | 0 / 0 | 60.2% / 60.6% |
| Total | 607 | 0 / 0 | All existing component gates passed |

29 new cases link the **exact production** `WinUI3/OnlinePageStateAdapter.cs` into the headless Core test project. They exercise controlled asynchronous callbacks and leases, including stale open/load/translation, close/reset, same-item reopen, context changes, failure/retry, late dialog completion, shared preflight, per-repository identity, overlapping tasks and cancellation. The coverage columns are existing component assembly gates, not a claim of whole-window/XAML coverage or a separate WinUI adapter coverage gate.

Coverage-gate fixtures, WinUI x64 Release build (**0 warnings, 0 errors**), executable version checks, managed manifest and minimal-package contract passed. NuGet audit was disabled for the local offline run; a vulnerability audit was not verified. `git diff --check` with repository CRLF handling passed.

An initial complete run also passed. Final r2 includes a small window wiring refinement: refresh detail actions when preparation releases the picker gate, and protect secondary detail/preflight error reporting. Final package uses r2, not the preliminary artifacts.

## Package / 测试包

Clean runtime ZIP:

`artifacts/optimization-online-state-verified-opt7-r2-20261007/ReleasePackage/Integrated_Mod_Manager-v4.0.0.zip`

SHA-256: `9DAF43195C6EB3B807FF1750AD88ABB28D6AFE6CEA9A62716870275D450E30DD`.

Independent test ZIP:

`artifacts/local-test-online-state-opt7-20261007/Integrated_Mod_Manager-v4.0.0-online-state-opt7.zip`

SHA-256: `F5DD64683C22AFB554D3004BAFA609763883C056C48ADB6EF18970286951A9C9`.

Freshly extracted runnable:

`artifacts/local-test-online-state-opt7-20261007/App/IntegratedModManager.exe`

The package was copied from clean r2 runtime source, not a previously launched App. Four executable file versions are **4.0.0.0**: IntegratedModManager, compatibility ModFolderCopier, LocalUpdateAgent and WinUI3/ModFolderCopier.WinUI. TestData contains **1,000 mock sample.ini files** and a synthetic loader/target fixture. Fresh root has no config.ini, beta-shell.json or performance.enabled. LOCAL_TEST.md explains explicit mock-path selection and pending manual checks. No real Mod, launcher or personal configuration was copied.

Stable ZIP `artifacts/release-v4.0.0-stable-20261005/package/Integrated_Mod_Manager-v4.0.0.zip` was rehashed unchanged:

`6099EC3CB19BAA655F0E2509246E313C1714A702FB33A2BEB952302901935E31`.

## Not verified / 尚未验证

- No application window was launched for this checkpoint. Tests link the real state adapter, not the whole MainWindow or XAML tree. Animation, button binding, native folder/file pickers and manual installed-state refresh remain pending.
- No live GameBanana request/download, game, launcher, deployment into real folders or app self-update was exercised.
- Same-ID HTTP request coalescing and full feed/category/avatar/translation service extraction remain outside this checkpoint.
- Cancellation rejects stale callbacks/starts; it does not instantly terminate existing translation-cache work, native pickers, SQLite operations or extraction tools. Already-created tasks retain their existing lifecycle; all-worker shutdown is not claimed.
- The gate is in-memory and UI-thread-owned, not a cross-process installation lock. Existing dialogs cannot be forcibly dismissed by this revision guard after they have opened. Installed identity/selection/layout policies are retained, not independently proven by the adapter fixtures.

下一开发检查点：可迁移组合预设导出/导入；先格式与只读匹配预览，再保存。完整窗口/性能矩阵、隔离实际流程与发布候选验证仍是后续独立阶段，发布需用户明确批准。
