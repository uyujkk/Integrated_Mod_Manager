# Online page state / 在线页面状态

Local opt7 continues stage 4 with detail-request ownership and download-command admission. File version remains 4.0.0. No public release, push, automatic-update change, layout redesign or preset-schema change.
本地 opt7 整理详情请求归属与下载入口，版本仍为 4.0.0，不推送、不发布、不修改自动更新、布局和预设格式。

## Production adapters / 实际使用的适配器

`WinUI3/OnlinePageStateAdapter.cs` has no XAML dependency. `MainWindow.OnlineState.cs` supplies window callbacks; the test project links the exact adapter source rather than implementing a second model. Calls belong to the UI thread; these adapters are not general-purpose thread-safe services.
测试直接编译窗口使用的适配器源文件，通过受控异步回调验证，不复制第二套逻辑；这不等于加载真实 XAML 或测试全部 MainWindow 事件。

| Boundary | Contract |
| --- | --- |
| `OnlinePageStateAdapter` | New detail request invalidates/cancels the prior request. Opening, metadata and translation awaits check revision plus captured context before publish/error/final callbacks. |
| Detail context | Repository ID/path/source/category, language and selected card reference must still match. Reopening the same ID still starts a new revision. |
| Detail close/reset | Invalidate before animation waits or repository reset; invalidate hero-image revision too. Stale failures do not open new error dialogs. |
| `OnlineDownloadActionAdapter` | Default/manual/card actions share one preflight/picker gate. Source identity + captured repository ID/path stays locked until the attempt finishes. |
| Task creation | Release the global preflight gate while retaining the attempt's source lock; different Mods/repository scopes may proceed. |
| Completion/cancel/failure | Dispose the attempt lease and refresh button state. Double disposal is safe; another attempt's picker is not unlocked by an old completion. |

## Window integration / 窗口接入

- Detail loading no longer toggles the global repository busy state. A stale detail finally cannot unlock a download. Concurrent online installations keep a separate busy count; this is not a rewrite of all application-wide busy ownership.
- 详情只控制自己的操作按钮；并行在线安装单独计数，不声称统一重构了全应用忙碌状态。
- Disable both latest/manual detail controls during detail loading or conflicting preparation/active source. Card clicks disable their own button; command admission remains authoritative even for an existing rendered button.
- 详情加载与冲突下载期间禁用入口；旧卡片仍由执行入口兜底防重，不依赖按钮外观保证安全。
- Manual selection captures routing before the first await and checks the selected card/context both before and after its picker. A changed detail does not receive the previous file-list result or start its old selection. Default download keeps its explicitly captured destination scope.
- 手选在等待前固定上下文，文件弹窗前后核对；默认下载继续使用已经捕获的仓库，不因切换而改投。
- A language toggle reloads/re-projects the visible detail through the current raw cache. Superseded translation waits can stop, but an already running translation-cache request may finish in the background.
- 切语言会重新生成当前详情；取消等待不代表终止已经开始的翻译缓存请求。
- Repository/source edits use the shared reset path. List cancellation advances request revision even if replacement loading returns early (missing source, cache hit or blocked load). This prevents old finally callbacks from becoming current again.
- 修改仓库来源统一重置；列表取消立即推进代次，新请求提前返回也不让旧结果复活。
- Window close cancels detail/preflight work. Already-created download tasks retain their existing cancellation/cleanup lifecycle; no promise of killing native pickers, external extraction or all workers immediately on close.
- 关闭窗口取消详情和准备阶段；已建立任务沿用原有取消清理，不宣称即时终止系统弹窗、外部解压工具和所有工作线程。
- Detail/preflight error reporting catches a failed dialog invocation and logs it, rather than letting that secondary reporting error escape an async event handler. Existing app-wide dialog arbitration is not rewritten.
- 详情与准备错误的弹窗失败会记录日志，未统一改写全应用弹窗调度。

## Verification and remaining scope / 验证与边界

29 offline adapter cases cover stale opening/loading/projection/errors, repeated same-ID selection, cancellation, changed repository/language, late error-dialog completion, shared preflight, retry, scoped installations and lease lifetime. Full regression count: 607 (556 Core / 24 DataStore / 27 UpdateAgent). See [opt7 evidence](../verification/online-state-opt7-20261007.md).

原生按钮绑定、可见动画、真实网络与目录/文件弹窗、多任务状态和安装标识刷新仍需在隔离窗口手测。Complete feed/category/avatar/translation extraction and same-ID metadata request coalescing remain outside this checkpoint. Headless adapter tests and successful compilation are not native-window or live-website validation.

Next planned checkpoint: portable combination-preset export/import, with explicit schema, read-only import preview, repository/Mod matching and conflict checks before saving. Existing directed `d3dx_user.ini` restoration stays authoritative; no fallback to rewriting original Mod INI defaults.
下一步：可迁移组合预设导入/导出，先定义格式、只读预览和匹配冲突规则，再保存；保留定向回写 d3dx_user.ini，不恢复到原始 Mod INI。
