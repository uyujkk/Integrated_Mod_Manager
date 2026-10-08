# Repository performance / 仓库性能诊断

Local stage 3 work, not a public release. Stable 4.0.0, real Mods and game folders are untouched. Keep each benchmark result and test application in a fresh directory.
第三阶段本地开发，不是正式发布。保留 4.0 正式包、真实 Mod 和游戏目录；每次测量使用新的结果目录和独立测试应用。

## Repeatable service benchmark / 可重复服务基准

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\measure-repository-performance.ps1 -OutputDirectory artifacts\repository-performance-new-run
```

Requires the generated 100/500/1,000-Mod scanner fixtures. Uses a fresh isolated SQLite index for each of five cold/warm pairs. Reads fixture sources only; writes developer-owned indexes and JSON results. Existing results and out-of-repository roots are rejected. `RepositoryPerformance` is a developer console project, never shipped with the app.
需要已生成的三档扫描模拟目录。每档五组冷/热扫描使用独立 SQLite 索引；只读模拟源，结果和索引写到开发目录。已有结果和仓库外根目录会拒绝。控制台基准项目不随应用打包。

The output separates scan+SQLite, a lightweight projection/deployment-existence probe, and collection notifications. It does **not** include WinUI row construction, image decoding, layout, GPU rendering or frame rate. OS filesystem caches remain warm between iterations; “cold” means an empty application index, not a cold disk.
输出区分扫描与 SQLite、轻量投影/部署存在性检查、集合通知，**不包含** WinUI 行构建、图片解码、布局、GPU 或帧率。“冷”指应用索引为空，不指硬盘/系统缓存已清空。

## Local native-window timings / 本地原生窗口记录

Opt in by creating `WinUI3/repository-performance.enabled` in an isolated test app **before** launching it. No marker means no performance scope allocation or logging. Remove the marker before a later launch to disable logging. Do not edit a running app's binaries.
在独立测试应用启动前放置 `WinUI3/repository-performance.enabled` 开启诊断。没有标记则不生成计时对象或日志；下次启动前移走标记关闭。不要替换运行中应用的 DLL。

Records go to `WinUI3/repository-performance.log`, capped at approximately 1 MB with no rotation/deletion. Each JSON line contains operation, elapsed milliseconds, item/cache-hit counts, start thread ID and, for UI scopes, content width/height and actual XAML rasterization scale. No Mod names, absolute paths, search text, INI values or user identifiers are included. Nothing is uploaded. Existing general error/configuration logs are separate and may contain other diagnostic details.
日志保存在本地，约 1 MB 后停止，不轮转删除。记录操作、耗时、数量/缓存命中、开始线程、窗口内容大小和实际 XAML 缩放；不记录 Mod 名称、绝对路径、搜索词、参数或身份信息，不上传。原有错误/配置日志独立，不能据此声称其他日志也没有路径。

| Operation | Meaning / 含义 |
| --- | --- |
| Scan | Background scan including index reads/writes / 后台扫描和索引读取写入 |
| Projection | Build folder models and read deployment status / 构造目录模型、读取部署状态 |
| Refresh | End-to-end refresh scope, includes awaited work / 刷新总范围，包含异步等待 |
| Populate | Apply selected category and its selection / 分类列表与选择应用 |
| Filter | Search/filter and apply visible snapshot / 搜索过滤和可见列表应用 |
| CoverLayout | Cover-layout model updates, not GPU frame completion / 封面布局模型更新，不是 GPU 帧完成 |

These scopes time code execution, not actual rendered-frame latency. They may include failed/cancelled work; inspect the error/status context before treating a sample as a completed operation. Optional logging adds overhead. Do not add nested durations together or extrapolate synthetic samples to real preview-heavy repositories.
记录代码执行范围，不是实际帧显示延迟；失败/取消也可能记录，应结合状态和错误检查。开启日志本身有开销。范围互相嵌套，不要相加，也不要推算真实大量预览图仓库的性能。

## Changes justified by notification counts / 按通知数量进行的优化

- `BatchObservableCollection.ReplaceAll` materializes before changing data, emits at most one Reset, keeps item objects/order/duplicates, and emits nothing for an equal snapshot. Ordinary Add/Remove still behave like ObservableCollection.
- 批量替换先准备数据，再最多发一次 Reset；保留对象、顺序及重复项，相同快照不通知，普通增删语义不变。
- Category population, category filtering and visible Mod filtering use this operation. Selection remains path-based; native binding/selection behavior still requires window checks.
- 分类填充、分类过滤及 Mod 过滤采用批量替换，选择仍按路径，原生绑定/选择需窗口验证。
- Queued repository refresh skips work when an explicit refresh already applied the same catalog revision; enqueue failure clears the pending flag.
- 显式刷新已应用当前目录代次时跳过重复排队刷新，排队失败会释放等待标记。

One Reset can still recreate realized controls or reset scroll state. It is not an unconditional speed improvement over fine-grained diffs. For now it applies to snapshot replacements, not all individual changes; future diffs require evidence and selection/scroll testing.
一次 Reset 仍可能重建已实现控件或重置滚动，不一定在所有情形都快于细粒度差异。当前只用于整批快照，不替代所有单项更新；进一步差异算法需要测量和选择/滚动验证。

## Harder single-category UI fixture / 单分类压力目录

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\new-repository-scan-fixture.ps1 -ModCount 1000 -CategoryCount 1 -OutputDirectory artifacts\single-category-new-run
```

Check selection, search/clear, Files/Covers round trips, refresh selection retention, internal scrolling, maximized/restored window layout and actual scaling. Use no real launcher. A synthetic fixture has no preview images, so it does not test image-heavy workloads or game effects.
检查选择、搜索/清空、文件/封面切换、刷新保留选择、内部滚动、最大化/还原与缩放；不要接真实启动器。模拟数据无预览图片，不验证图片密集负载或游戏效果。
