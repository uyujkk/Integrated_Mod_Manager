# 源码结构 / Source Layout

本文说明各项目和目录的职责，方便定位代码。打开 [`IntegratedModManager.sln`](../../IntegratedModManager.sln) 可一次加载全部项目；构建与测试流程见[测试与构建说明](./TESTING.md)。

This page maps each project and folder to its responsibility. [`IntegratedModManager.sln`](../../IntegratedModManager.sln) loads every project; see the [Testing Guide](./TESTING.md) for the build and test flow.

## 项目 / Projects

| 项目 / Project | 职责 / Responsibility | 依赖 / Depends on |
| --- | --- | --- |
| [`IntegratedModManager.Core`](../../IntegratedModManager.Core/) | 不依赖界面的逻辑：解压与校验、部署、预设、在线元数据、更新等。Logic without UI: archives, deployment, presets, online metadata, updates. | — |
| [`IntegratedModManager.Data`](../../IntegratedModManager.Data/) | SQLite 缓存与仓库索引（命名空间 `IntegratedModManager.Data`）。SQLite caches and the repository index. | Core |
| [`IntegratedModManager.EndfieldLaunch`](../../IntegratedModManager.EndfieldLaunch/) | 内置终末地启动，**AGPL-3.0-only**。Integrated Endfield launch, AGPL-3.0-only. | — |
| [`IntegratedModManager.UpdateAgent.Core`](../../IntegratedModManager.UpdateAgent.Core/) | 把根目录 `LocalUpdateAgent.cs` 编译成可测试的程序集。Compiles the root `LocalUpdateAgent.cs` for tests. | — |
| [`WinUI3`](../../WinUI3/) | WinUI 3 主程序 `ModFolderCopier.WinUI`。The WinUI 3 application. | Core, Data, EndfieldLaunch |
| `Tests`, `DataStoreTests`, `UpdaterTests`, `EndfieldLaunchTests` | 四个 xUnit 测试套件，覆盖率门槛见 `scripts/test-all.ps1`。Four xUnit suites with coverage gates. | 各自对应项目 |
| `RepositoryPerformance`, `DeveloperTools` | 性能基准与独立发布的开发者工具。Benchmark harness and the separately released developer tools. | Core (+ Data) |

根目录的 `WinUILauncher.cs` 与 `LocalUpdateAgent.cs` 由 `build_winui.bat` 用 .NET Framework 编译器单独编译为启动器和更新器。
`WinUILauncher.cs` and `LocalUpdateAgent.cs` at the root are compiled separately by `build_winui.bat` into the launcher and updater.

## Core 目录 / Core folders

命名空间与目录一致：`Archives` 目录下的文件属于 `IntegratedModManager.Core.Archives`，其余目录同理。新文件按所在目录选命名空间。
Namespaces follow folders: files in `Archives` belong to `IntegratedModManager.Core.Archives`, and so on. A new file takes the namespace of its folder.

| 目录 / Folder | 内容 / Contents |
| --- | --- |
| `Archives` | `ArchiveExtractor`（ZIP、7-Zip、Bandizip、Windows tar）、清单校验、`ArchiveErrors` 错误分类、导入布局。Extraction, listing validation, error kinds, import layout. |
| `Configuration` | `config.ini` 的解析与生成（`LegacyConfigFile`）、配置文件的原子写入、备份与损坏恢复（`ConfigurationFileStore`）。config.ini parsing/formatting, atomic writes, backups and recovery. |
| `Deployment` | 安装事务与回滚（`ModInstallTransactions`）、文件冲突检测、目录联接、组合部署、INI 冲突检测、旧库整理导入、角色文件夹规划。Install transactions and rollback, file conflicts, junctions, combination deployment, INI conflicts, library import, character folders. |
| `Presets` | 组合预设、恢复预览、可迁移组合包。Combination presets, restore preview, portable bundles. |
| `Online` | GameBanana 元数据与下载数、JSON 读取（`GameBananaJson`）、分类列表解析、角色目录匹配与合并（`OnlineCharacterCatalogMatcher`）、下载传输与会话、浏览与预览策略。GameBanana metadata, JSON readers, listing parsing, character catalog matching, download transport and sessions, browse policies. |
| `Endfield` | 终末地桥接、干员目录解析，以及内置干员表（`endfield-characters.json`，嵌入程序集，新增干员只改这个文件）。Endfield bridge integration, operator catalog parsing and the built-in operator list in the embedded `endfield-characters.json`. |
| `Shortcuts` | Mod 快捷键扫描、描述与按键输入。Mod shortcut scanning, descriptions and key input. |
| `Updates` | 版本信息、更新包校验、Mod 更新替换与恢复。Release policy, update packages, Mod update replacement. |
| `Repository` | 仓库扫描、性能记录、工作区策略、批量集合、角色文件夹头像（`CharacterFolderAvatars`）。Repository scanning, performance, workspace policies, character folder avatars. |
| `Layout` | 与控件无关的窗口和弹窗布局策略。Control-free window and dialog layout policies. |
| `Infrastructure` | 路径安全、不跟随链接的目录遍历/复制/删除（`SafeDirectoryTree`）、错误分类标记（`ErrorTags`）、恢复用 JSON、日志轮换。Path safety, link-safe tree inspect/copy/delete, error tags, recovery JSON, log rotation. |

## WinUI 主窗口 / MainWindow

`MainWindow` 是一个 partial 类。`MainWindow.xaml.cs` 只保留常量、字段、枚举和构造函数，各功能分别放在 `MainWindow.<功能>.cs`：
`MainWindow` is a partial class. `MainWindow.xaml.cs` keeps constants, fields, enums and the constructor; each feature lives in `MainWindow.<Feature>.cs`:

| 分组 / Group | 文件 / Files |
| --- | --- |
| 外壳与通用 / Shell | `Shell`, `Localization`（`T`/`L`，配合 `UiText.cs` 与 `Strings/*.json`）, `Settings`, `Configuration`, `Dialogs`, `Tray`, `SingleInstance`, `UiActionGuard`, `UiDiagnostics`, `BuiltInData` |
| 仓库与本地库 / Repositories | `Dashboard`, `Repositories`, `LocalLibrary`, `RepositoryWorkspace`, `RefinedWorkspace`, `RepositoryPerformance`, `LocalTags`, `ExistingModImport`, `CharacterAvatars` |
| 安装与部署 / Install | `Archives`, `InstallTransactions`, `IniConflicts`, `ConfigurationProfiles`, `CombinationPresets`, `CombinationBundles`, `PersistentSlots` |
| 在线浏览 / Online | `OnlineFeed`, `OnlineCharacters`, `OnlineCards`, `OnlineDetails`, `OnlineImageCache`, `OnlineImages`, `OnlineBrowse`, `OnlineState`, `OnlineLibrary`, `BrowseSessions`, `AuthorWorkspace` |
| 下载与更新 / Downloads | `OnlineDownloadFlow`, `OnlineDownloads`, `OnlineInstall`, `DownloadTasks`, `DownloadRecovery`, `BatchDownloads`, `Updates`, `ModUpdates` |
| 快捷键与终末地 / Shortcuts, Endfield | `ShortcutBindings`, `ShortcutPanel`, `CharacterFollow`, `EndfieldBridge`, `ProductivityFeatures` |

窗口使用的记录与视图模型放在 [`WinUI3/Models`](../../WinUI3/Models/)，每个类型一个文件。
Records and view models used by the window live in `WinUI3/Models`, one type per file.

## 约定 / Conventions

- 新逻辑若不需要控件，放进 Core 并补单元测试；窗口只负责调用、显示和本地化。
  Logic that needs no controls goes into Core with unit tests; the window calls it, displays results and localizes them.
- Core 用 `ErrorTags` 给异常附加错误类型（如 `ArchiveErrorKind`、`InstallTransactionErrorKind`），不改变异常类型；界面在 `MainWindow.Archives.cs`、`MainWindow.InstallTransactions.cs` 各用一张表映射为中英文提示，不匹配异常消息文本。
  Core tags failures with a kind through `ErrorTags` (for example `ArchiveErrorKind`, `InstallTransactionErrorKind`) without changing the exception type; `MainWindow.Archives.cs` and `MainWindow.InstallTransactions.cs` each map kinds to Chinese/English text in one table instead of matching message text.
- 路径包含判断统一使用 `PathSafety.ResolveInsideDirectory`、`IsSameOrInside` 或 `IsStrictlyInside`。
  Path containment checks use `PathSafety.ResolveInsideDirectory`, `IsSameOrInside` or `IsStrictlyInside`.
- 界面文案放在 `WinUI3/Strings/zh-CN.json` 和 `en-US.json`，代码里用 `T("功能.名称")` 取文本，带值的文本用 `T("键", 值…)` 并在文件里写 `{0}`、`{1}`。新增文案时两个文件都要加同一个键；`UiTextResourceTests` 会检查键是否齐全、值的个数是否对得上。`L(中, 英)` 只用于运行时才拼出的文本对（例如保存下来的状态消息）。
  UI text lives in `WinUI3/Strings/zh-CN.json` and `en-US.json`. Code reads it with `T("Feature.Name")`, or `T("Key", values…)` for texts with `{0}`, `{1}` placeholders. Add each new key to both files; `UiTextResourceTests` checks that keys exist and value counts match. `L(zh, en)` is only for text pairs built at runtime, such as stored status messages.
