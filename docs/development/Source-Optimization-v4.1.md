# 4.1 source optimization / 源码优化记录

This is the consolidated developer record for the 4.0 → 4.1 changes, not a second release announcement. The code is pinned to [`v4.1.0`](https://github.com/uyujkk/Integrated_Mod_Manager/tree/v4.1.0), commit `ed52da984ca4fec2d34a023401832e0661f76f42`. The documentation follow-up does not replace that tag or rebuild its ZIP.

本文汇总 4.0 到 4.1 的源码优化，供维护和参与开发时查阅。面向用户的简介仍保持简洁，见[更新说明](../releases/v4.1.0.md)。本次只补文档，不移动版本标签、不替换安装包。

## What changed / 改了什么

| Area / 模块 | Change and purpose / 调整与作用 | Source entry / 源码入口 |
| --- | --- | --- |
| Repository scanning / 仓库扫描 | Move enumeration, sorting, index reuse and warnings into Core; keep SQLite behind an adapter. Partial/cancelled results must not replace a complete index. / 扫描与窗口、数据库分离，避免取消或失败后留下不完整索引。 | [RepositoryScanService](../../IntegratedModManager.Core/RepositoryScanService.cs), [RepositoryIndexStoreAdapter](../../IntegratedModManager.Data/RepositoryIndexStoreAdapter.cs) |
| Collection updates / 列表更新 | Apply snapshots with at most one Reset; equal snapshots do not notify. Skip redundant queued refreshes after an explicit refresh. / 减少逐项通知和重复刷新，不替代所有单项增删。 | [BatchObservableCollection](../../IntegratedModManager.Core/BatchObservableCollection.cs), [repository workspace](../../WinUI3/MainWindow.RepositoryWorkspace.cs) |
| Download transport / 下载传输 | Separate streamed HTTP copying, progress throttling, bounded response inspection and no-overwrite publication from UI routing. / 下载服务不再负责界面选项；只清理本次创建的临时文件，保留实际落盘路径。 | [OnlineArchiveDownloadService](../../IntegratedModManager.Core/OnlineArchiveDownloadService.cs) |
| Download lifecycle / 下载生命周期 | Capture the destination repository before awaits; prepare metadata before a synchronous commit. Late cancellation must not remove a committed installation. / 下载中切仓库不改变目标，提交成功后的取消不删除安装结果。 | [OnlineDownloadSession](../../IntegratedModManager.Core/OnlineDownloadSession.cs), [installation adapter](../../WinUI3/MainWindow.OnlineInstall.cs) |
| Website metadata / 网站元数据 | Separate fetching/parsing from localized UI projection; bound JSON input and store raw metadata through a cache adapter. / 原始元数据与翻译显示分离，保留现有缓存兼容入口。 | [service](../../IntegratedModManager.Core/GameBananaMetadataService.cs), [parser](../../IntegratedModManager.Core/GameBananaMetadataParser.cs), [cache adapter](../../IntegratedModManager.Data/OnlineMetadataCacheAdapter.cs) |
| Page state / 页面状态 | Revisions and captured context reject stale detail results; latest/manual/card downloads share admission rules. / 防止旧请求覆盖新选择，防重不只依赖按钮是否禁用。 | [OnlinePageStateAdapter](../../WinUI3/OnlinePageStateAdapter.cs), [window integration](../../WinUI3/MainWindow.OnlineState.cs) |
| Image loading / 图片加载 | Decode bounded local file streams explicitly; retry a failed cached online image once with a refreshed cache. / 修复在线预览、头像和本地预览的路径加载问题，不把设置 URI 当成加载成功。 | [ImageFileReadPolicy](../../IntegratedModManager.Core/ImageFileReadPolicy.cs), [OnlineImageLoadAdapter](../../WinUI3/OnlineImageLoadAdapter.cs), [window image loading](../../WinUI3/MainWindow.OnlineImages.cs) |
| Restore and bundles / 恢复与组合包 | Add read-only restore plans and a versioned bundle service; validate identity, manifests and payloads before installation. / 文件、配置和参数职责分开；冲突不覆盖，保存失败只回滚本次拥有的新增内容。 | [CombinationRestorePreview](../../IntegratedModManager.Core/CombinationRestorePreview.cs), [CombinationBundleService](../../IntegratedModManager.Core/CombinationBundleService.cs), [bundle UI](../../WinUI3/MainWindow.CombinationBundles.cs) |
| Responsive layout / 自适应布局 | Extract window/viewport policies; Settings chooses overview or sections using both width and height. / 非最大化时不再硬塞两列，按钮按模块实际宽度排列，长内容在内部浏览。 | [SettingsLayoutPolicy](../../IntegratedModManager.Core/SettingsLayoutPolicy.cs), [DialogViewportPolicy](../../IntegratedModManager.Core/DialogViewportPolicy.cs), [Settings integration](../../WinUI3/MainWindow.RefinedWorkspace.cs) |

This is incremental extraction, not a complete MainWindow/MVVM rewrite. Core services avoid XAML dependencies; UI-thread adapters still coordinate native controls. Default latest-archive selection, manual selection, existing SQLite/configuration compatibility and the established deployment/backup paths are retained.

本次是逐步拆分，不是重写整个 MainWindow 或宣称完成 MVVM。默认下载最新压缩包、手动选文件、现有仓库和配置继续兼容。恢复状态仍定向回写 `d3dx_user.ini`，不恢复到原 Mod INI，也不加入游戏注入、自动按键或自动修复。

## Performance evidence / 性能依据

- A separate [RepositoryPerformance](../../RepositoryPerformance/Program.cs) harness measures isolated 100/500/1,000-Mod fixtures and collection notification counts. Optional local timings separate scan, projection, refresh, population, filtering and cover-layout work. Diagnostics are off without the opt-in marker; the harness is not shipped in the main package.
- 独立基准工具和可选本地计时用于定位耗时，不上传数据。集合批量替换有通知数量依据，但 Reset 仍可能重建控件；模拟扫描耗时不等于真实图片仓库的帧率，也不能据此宣称固定百分比的加速。

See [performance scope](./Repository-Performance.md) and [measurement evidence](../verification/repository-performance-opt3-20261005.md). “Cold” refers to an empty application index, not a cleared OS/disk cache.

## Test cleanup and verification / 测试整理与验证

- Three suites remain: Core, DataStore and UpdateAgent. Shared package versions moved to [TestDependencies.props](../../Tests/TestDependencies.props); no safety cases or coverage thresholds were removed or lowered.
- The [testing guide](./TESTING.md) now describes one entry point, suite boundaries and release checks instead of repeating stage-by-stage totals. Historical opt1–opt11 evidence is retained.
- The 4.1 full run passed **759 tests**: Core 708, DataStore 24, UpdateAgent 27, with zero failures/skips. Line/branch coverage: Core **96.7% / 91.9%**, Data **88.6% / 89.1%**, UpdateAgent **60.2% / 60.6%**. Four coverage-gate self-checks, WinUI x64 build, executable versions and minimal-ZIP checks passed.
- 测试直接链接生产页面/图片适配器，避免另写一套测试模型；另有真实隔离 SQLite、模拟 HTTP、文本 Mod 和真实 ZIP 用例。编译通过不等于所有原生窗口、真实网站或游戏场景验证完成。
- [GitHub Build and Test](https://github.com/uyujkk/Integrated_Mod_Manager/actions/runs/37722853990) completed successfully for the pinned release commit. Native-window evidence and the final UI-only rebuild scope are documented in [4.1 verification](../verification/v4.1.0.md).

These results belong to the recorded 4.1 build. This documentation-only follow-up does not claim a new full test run. 截至本记录更新时，4.1 安装包仍为 GitHub Release 草稿；源码推送不等于公开发布。

## Remaining limits / 仍需注意

Cancellation is cooperative, not immediate termination of native pickers, filesystem calls or external extractors. Filesystem checks are not a cross-process lock; bundle import has no full crash/power-loss recovery journal. Hashes establish consistency, not trusted provenance, malware safety or redistribution permission. Whole-application busy/dialog arbitration, complete feed/category/translation extraction and exhaustive DPI/live-game testing remain outside this optimization.

后续优先继续覆盖窗口尺寸与缩放、图片密集仓库和并发失败路径。不要把离线测试、哈希一致或本地窗口观察写成“所有游戏兼容”或“账号零风险”。具体保护边界见[组合包说明](./Combination-Bundles.md)、[下载生命周期](./Online-Download-Execution.md)和[回归清单](./Regression-Checklist.md)。

Maintained by **uyujkk**. The existing Bandizip fallback contribution from **[CaramelizedCUDA](https://github.com/CaramelizedCUDA)** remains credited separately; it predates this 4.1 extraction work.

[Documentation index](../README.md) · [User-facing changes](../releases/v4.1.0.md) · [Historical optimization sequence](./Optimization-Roadmap.md)
