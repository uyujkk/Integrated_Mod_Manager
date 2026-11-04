# 界面结构拆分计划 / UI structure plan

2026-11-04，基于 v4.7.3 之后的 main（`5dfe728`）。这是计划，不是已实现功能。工作量粗估：S ≈ 1–2 天，M ≈ 3–5 天，L ≈ 2 周以上。路线图里记为阶段 J，见 [Roadmap](./Roadmap.md)。

## 为什么要做

现在整个界面是一个 `MainWindow`：

| 项目 | 现状 |
| --- | --- |
| 代码 | 67 个 `MainWindow.*.cs` partial 文件，共约 23,800 行，全部共享同一组字段和控件 |
| XAML | 一个 `MainWindow.xaml`，2142 行、406 个具名控件；六个页面都是它的顶层容器 |
| 切页 | `ApplyShellState` 按 `PrimarySection` 把其中一个容器设为可见，其余折叠（`MainWindow.Shell.cs`） |
| 对话框 | 约 25 个文件直接 `new ContentDialog` |
| 已有基础 | 10 个 ViewModel（CommunityToolkit.Mvvm）、5 个自定义控件、语言文件、FlaUI 界面测试 |

后果：

- 改一页要动同一个类和同一个 XAML，评审和冲突都集中在这两处。
- 任何代码都能直接访问任何页面的控件，页面之间的依赖看不出来。
- 启动时六个页面全部创建，即使用户只看仪表盘。
- 单个页面无法单独测试或预览。

各页面目前在 `MainWindow.xaml` 里的位置：

| 页面 | XAML 行 | 主要代码文件 |
| --- | --- | --- |
| 仪表盘 | 175–542 | Dashboard、Repositories、FirstRun |
| Mod 工作区 | 543–984 | RepositoryWorkspace、RefinedWorkspace、LocalLibrary、LocalTags、BatchModActions、CharacterAvatars、CharacterSelection、CombinationPresets、CombinationBundles、PresetPreviews、PresetShare、PersistentSlots、PersistentVariants、DefaultSlots、ExistingModImport、Archives、IniConflicts |
| 在线 | 985–1463 | OnlineFeed、OnlineCards、OnlineCharacters、OnlineDetails、OnlineDownloadFlow、OnlineDownloads、OnlineBrowse、OnlineLibrary、OnlineImageCache、OnlineImages、OnlineInstall、OnlineState、BrowseSessions、BatchDownloads、AuthorWorkspace |
| 快捷键 | 1464–1466（内容在代码里搭建） | ShortcutPanel、ShortcutBindings、ShortcutCapture、ShortcutPresets、CharacterFollow、HotReload、SlotCapture、OverlayAppearance、OverlayAutoContrast |
| 更新 | 1467–1712 | Updates、ModUpdates、DownloadTasks、DownloadRecovery、ConfigurationProfiles、InstallTransactions |
| 设置 | 1715–2130 | Settings、ProductivityFeatures（诊断、备份、自更新）、Configuration 的界面部分 |
| 外壳（留在窗口） | 其余 | Shell、CommandPalette、Tray、SingleInstance、Localization、WhatsNew、UiActionGuard、UiDiagnostics、PageLayout |

`EndfieldBridge` 同时被仪表盘和 Mod 工作区用到，拆成服务而不是放进某一页。

## 目标结构

```
MainWindow            标题栏、导航栏、状态栏、托盘、单实例、全局快捷键、浮窗、命令面板
 └─ ContentControl    放当前页面
     ├─ DashboardPage + DashboardViewModel
     ├─ WorkspacePage + ModListViewModel / CharacterListViewModel / ModDetailsViewModel …
     ├─ OnlinePage    + OnlinePageViewModel
     ├─ ShortcutsPage + ShortcutPageViewModel
     ├─ UpdatesPage   + UpdatesViewModel（新）
     └─ SettingsPage  + SettingsViewModel（新）
AppServices           手写的组合根，启动时创建一次
 ├─ RepositoryService   仓库列表、当前仓库、切换与刷新事件
 ├─ ShellConfigService  beta-shell.json 与 config.ini 的读写
 ├─ LanguageService     T()、M()、当前语言、LanguageChanged 事件
 ├─ DialogService       ContentDialog 的 XamlRoot、排队、确认框
 ├─ DeploymentService   部署、移除、安装事务、备份
 ├─ DownloadService     下载任务、恢复、历史
 ├─ LaunchService       运行启动器、终末地启动、XXMI
 └─ StatusService       状态栏消息、后台任务计数
```

几个选择和理由：

- **页面实例常驻，不用 `Frame` 导航。** 现在切页不会丢失滚动位置、选择和正在加载的数据；用 `ContentControl` 加按需创建、创建后保留的页面实例，行为和现在一样，又不用处理 `Frame` 的导航参数和缓存模式。
- **手写组合根，不引入依赖注入容器。** 服务数量少，手写更直观，也不给裁剪和 AOT 增加风险。页面通过构造函数拿到 `AppServices`。
- **页面不引用 `MainWindow`。** 页面之间不直接调用，跨页的事（例如在线下载完成后刷新工作区、命令面板跳到某个 Mod）通过服务的事件或 `NavigationService.Navigate(section, argument)` 完成。
- **继续用 WinUI 3。** 只加 Windows Community Toolkit（`CommunityToolkit.WinUI.Controls`）的 `SettingsCard` / `SettingsExpander`，按需再用 `Segmented`、`TokenizingTextBox`、`GridSplitter`。不换 Avalonia、Uno 或 WPF。

## 阶段

一个阶段一个 PR，从最新 main 建分支。每个阶段完成后都可以单独发布，界面看起来不变（J2 的设置卡片除外）。

| 阶段 | 内容 | 工作量 | 依赖 |
| --- | --- | --- | --- |
| J0 准备 | 基线截图、记录界面测试依赖的自动化名称、确认工具包与 Windows App SDK 1.7 兼容 | S | 无 |
| J1 外壳与服务 | 建 `AppServices` 和上面的服务，`MainWindow` 改为用 `ContentControl` 放页面；先用“包装页面”承载现有容器，行为不变 | M–L | J0 |
| J2 设置页 | 移出设置页；`SettingRow` 换成 `SettingsCard` / `SettingsExpander`；新增 `SettingsViewModel` | S–M | J1 |
| J3 更新页 | 移出更新页、下载历史、配置方案、安装回滚；新增 `UpdatesViewModel` | M | J1 |
| J4 仪表盘 | 移出仪表盘、仓库对话框、首次安装弹窗 | S–M | J1 |
| J5 快捷键页 | 移出快捷键页；浮窗通过接口和服务通信，不再直接调用 `MainWindow` | M | J1 |
| J6 在线页 | 移出在线页的 15 个文件；图片缓存和浏览会话变成服务 | L | J1，最好在 J4 之后 |
| J7 Mod 工作区 | 移出工作区的 17 个文件；部署、组合预设、状态槽位的界面逻辑进 ViewModel | L | J1，最好最后做 |
| J8 对话框与资源 | 约 25 处 `new ContentDialog` 改成独立对话框类；`App.xaml` 拆成主题、控件样式、页面样式几个资源字典 | S–M | 可与 J2–J7 穿插 |

J2–J5 之间没有顺序依赖，可以按方便调整。J6、J7 最大、和其他页面牵连最多，放在后面，等服务层已经被前几页验证过。

### J0. 准备

1. 用脚本在测试副本里截基线图：中文、英文、俄语，1400×900 和 1000×800，六个页面加“新功能”和首次安装弹窗。之后每个阶段用同一组截图对比。
2. 列出 `tests/UI.Tests` 用到的自动化名称和控件；拆分时保持不变。
3. 在一个空分支里引用 `CommunityToolkit.WinUI.Controls.SettingsControls`，确认能和现在的 Windows App SDK 1.7、`net8.0-windows10.0.19041.0` 一起构建并打包，再决定版本号。

J0 结果（2026-11-04，分支 `claude/ui-j0-prep`）：

- 截图：脚本 `tools/ui-baseline/Capture-UiBaseline.ps1`，用法、限制和当前基线（含壁纸）见[界面基线截图](./UI-Baseline-Screenshots.md)。
- 自动化名称：见[界面测试依赖的自动化名称](./UI-Automation-Names.md)，39 个 AutomationId，另有按文字和类名的查找，并按阶段列出需要同步改的测试。
- 工具包：`CommunityToolkit.WinUI.Controls.SettingsControls`、`Segmented`、`Sizers` 均为 8.2.251219（最新稳定版，要求 Windows App SDK ≥ 1.6）。引用后 `test-all` 的 Release 构建 0 警告，ZIP 打包通过，三个程序集和它们的 XAML 资源都在包里；此时还没有任何界面使用这些控件，首次实际使用在 J2。

### J1. 外壳与服务

1. 按“目标结构”建 `AppServices` 和各服务。先把现有字段和方法原样搬进服务，不改逻辑；`T()`、`M()` 改为转发到 `LanguageService`。
2. `MainWindow.xaml` 里六个页面容器各自包进一个 `UserControl`（包装页面），`ApplyShellState` 改为切换 `ContentControl.Content`。这一步只换宿主，页面内部代码暂时仍是 `MainWindow` 的 partial 方法。
3. 语言切换、主题切换、紧凑模式改为广播事件，由各页面自己响应。
4. 截图对比无变化；界面测试全部通过。

### J2–J7. 逐页拆分

每一页按同样的步骤：

1. **查牵连**：搜索这一页的控件名在其他 partial 文件里的引用，以及这一页代码访问的其他页面控件。每一处改成服务调用、事件或导航参数。把清单写进 PR。
2. **移动**：XAML 移到 `Pages/<名称>Page.xaml`，代码移到页面类或 ViewModel；能不依赖控件的逻辑进 ViewModel 或 Core，并补单元测试。
3. **删除**：从 `MainWindow` 删掉对应的 partial 文件和字段；`MainWindow` 的行数应明显下降。
4. **检查**：基线截图对比；界面测试通过，并为这一页加一个打开页面、做一次主要操作的冒烟测试。

#### J2 牵连清单（设置页）

设置页 `SettingsScrollViewer` 里有 82 个命名控件。除了 `MainWindow.Settings.cs` 本身，下面这些地方直接读写它们，或者设置页的代码读写别的页面：

| 位置 | 做什么 | 改成 |
| --- | --- | --- |
| `MainWindow.RefinedWorkspace.cs` | 分区按钮、两栏总览、卡片排布（`SettingsLayoutPolicy`）；状态行无文字时折叠 | 移进 `SettingsPage`；`ReflowCards`/`Place` 仍留给仪表盘 |
| `MainWindow.PageLayout.cs` | `AttachPageLayout(SettingsScrollViewer, …)`、`SettingsHeaderHeight` | 页面自己监听尺寸 |
| `MainWindow.Localization.cs` | 设置页标题、外观卡、项目卡文字，三个按钮的提示和朗读名称；在线页那段也写 `EditOnlineRepositoryButton.Content` | 页面在 `LanguageService.LanguageChanged` 里自己写文字 |
| `MainWindow.ProductivityFeatures.cs` | 启动时检查更新开关、三个更新按钮的排布、下载进度、诊断与反馈卡的文字和状态行、复制 QQ 群号 | 状态进 `SettingsViewModel`，文字和排布进页面，操作经 `ActionRequested` 回到窗口 |
| `MainWindow.Updates.cs` | 更新状态行、检查间隔下拉框、最新版本说明、检查中禁用按钮、检测到本地更新包后改按钮文字 | 写 `SettingsViewModel`（`UpdateStatus`、`UpdateIntervalIndex`、`Release*`、`IsCheckingUpdates`、`SetInstallState`） |
| `MainWindow.Tray.cs` | “最小化到托盘”一行的文字和开关 | 开关值进 `SettingsViewModel.MinimizeToTray`，文字进页面 |
| `MainWindow.LocalLibrary.cs` | 部署时禁用语言、主题下拉框 | `SettingsViewModel.IsBusy` |
| `MainWindow.ShortcutBindings.cs` | 重建快捷键页时顺带刷新设置页按钮文字 | 删掉：按钮文字由页面根据 ViewModel 决定 |
| `MainWindow.xaml.cs` | `AppearanceSettings` 属性（设置页的 `x:Bind`）；把 `EditOnlineRepositoryButton` 的点击换成在线版本 | `AppServices.Settings.Appearance`；页面发 `EditOnlineRepository` 请求 |
| `MainWindow.Shell.cs` | 页面宿主、进场动画对象 | 设置页直接是 `SettingsPage`，其他五页仍是包装页面 |
| `MainWindow.Repositories.cs`、`AppearanceEvents.cs`、`Shell.cs` | 改仓库、换语言、切页面后 `RefreshSettingsPane()` | 保留调用，改为把仓库在线信息、更新状态推给 ViewModel |

设置页代码访问的别的页面或外壳：`RefreshSettingsPane` 顺带刷新更新页的备份管理文字（`RefreshBackupManagerText`）和终末地桥接控件（`RefreshEndfieldBridgeControls`），这两处留在窗口；仓库在线信息需要 `GetEffectiveOnline*`（在线页的辅助方法），由窗口算好推给 ViewModel；“编辑仓库信息”“编辑角色映射”会刷新在线页，仍在窗口里处理；下载更新时切到设置页（`ApplyShellState`）也在窗口。`MainWindow.Settings.cs` 里不属于设置页的方法（在线页翻页按钮文字、冲突检测和链接部署开关、密度和语言切换）移到各自页面的 partial 文件。

应用自更新的逻辑（检查、下载、校验、启动更新程序）仍在 `MainWindow.Updates.cs` 和 `ProductivityFeatures.cs`，因为它依赖窗口的配置保存、对话框和关闭窗口；它只通过 `SettingsViewModel` 改设置页。以后可以连同配置一起移进单独的服务。

#### J3 牵连清单（更新页）

更新页 `UpdatesScrollViewer` 里有 62 个命名控件。除了 `MainWindow.Updates.cs`，下面这些地方直接读写它们，或者更新页的代码读写别的页面和外壳：

| 位置 | 做什么 | 改成 |
| --- | --- | --- |
| `MainWindow.Shell.cs` | 更新页的导航徽标（J1N 的 `InfoBadge`）直接数 `_trackedModUpdateResults`；窗口尺寸变化时调 `UpdateUpdatesLayout` | 徽标跟 `UpdatesViewModel.AvailableUpdateCount` 的变化事件；页面按自己的尺寸排布 |
| `MainWindow.DownloadTasks.cs` | 下载历史列表、筛选条、“清除已完成”；任务更新时同时刷新更新页的行和导航下载浮层的卡片，并启用两个“清除”按钮 | `DownloadService` 新增 `Changed`、`TaskUpdated` 事件：页面自己重建历史、更新自己的行，窗口只管导航浮层；行的状态文字和按钮由共用的 `DownloadTaskViews` 决定；行上的按钮经请求回到窗口（`RunDownloadTaskActionAsync`，浮层也用它） |
| `MainWindow.ProductivityFeatures.cs` | 三栏排布、下载历史卡片按整行收高、备份管理卡片文字、上限、备份列表（含进场动画）、“恢复” | 排布和文字进页面；备份列表由窗口读出后写进 `UpdatesViewModel.SetBackups`；“恢复”发请求；动画是否减少由 `AppearanceService.ReduceAnimations` 告诉页面 |
| `MainWindow.ConfigurationProfiles.cs` | “撤销上次 Mod 改动”按钮的可用、显示、说明文字和这一行的列宽 | `UpdatesViewModel.SetRollback` |
| `MainWindow.RepositoryWorkspace.cs` | 启动时把更新页里“组合预设”卡片的控件搬进 Mod 工作区的预设视图，删掉这张卡片，再往更新页的恢复操作行里塞一个“打开组合工作区”按钮；冲突检测开关的处理 | 预设卡片不再属于更新页：它的 XAML 留在 `MainWindow.xaml` 的隐藏容器 `CombinationProfileControlsHost` 里，J7 随工作区一起移；“打开组合工作区”按钮写进更新页 XAML，点击发请求；冲突检测开关进 ViewModel |
| `MainWindow.Localization.cs` | “立即检查”的提示和朗读名称 | 页面自己写 |
| `MainWindow.PageLayout.cs` | `AttachPageLayout(UpdatesScrollViewer, …)` | 页面自己监听尺寸 |
| `MainWindow.OnlineImageCache.cs`（在线页） | 已跟踪 Mod 行的缩略图走在线页的图片缓存 | 新的 `ImageService`：窗口在启动时把缓存的加载方法交给它，J6 再把缓存本身移过去 |
| `MainWindow.OnlineDownloadFlow.cs`、`ModUpdates.cs`（在线页） | “下载并替换”“移除跟踪”、正在替换的 Mod、重试要等在线页准备完 | 行上的按钮发请求，窗口照旧执行；正在替换的路径写进 `UpdatesViewModel.SetReplacing`；`DownloadService.IsRetryBlocked` |
| 空状态的“浏览在线 Mod”、恢复行的“打开组合工作区” | 直接调 `OnOnlineNavClicked`、`OnOpenCombinationPresetsClicked` | 页面发 `BrowseOnline`、`OpenCombinationPresets` 请求，由窗口导航 |

检查更新、下载替换、回滚和恢复备份的逻辑仍在窗口里：它们依赖配置保存、对话框、部署和在线页的状态。已跟踪 Mod 的数据（`_trackedModOrigins`、`_trackedModUpdateResults`）也还在窗口，在线页下载时会写它们；窗口把它们转成 `TrackedModRow` 交给页面。`PopulateStableIntervalOptions` 不再有人用，已删除。

### J8. 对话框与资源

- 每种对话框一个类（`Dialogs/` 下），由 `DialogService` 统一设置 `XamlRoot` 并保证同一时间只有一个对话框。
- `App.xaml` 拆成 `Themes/Colors.xaml`、`Styles/Controls.xaml`、`Styles/Pages.xaml`；页面专用样式放进页面自己的资源。

## 完成标准

每个阶段：

- 页面类不引用 `MainWindow`，只通过 `AppServices` 和导航服务与外部交互。
- 新的 ViewModel 和从界面移到 Core 的逻辑有单元测试；四组覆盖率门槛不降低。
- 中英俄三种语言、两种窗口尺寸的截图与基线一致（有意的改动在 PR 里说明）。
- `test-all`、代码风格检查和界面测试全部通过；Release 构建无警告。
- 语言文件的键只增不改名；用不到的键在同一个 PR 里删掉。

全部完成后：

- `MainWindow` 只剩外壳，预计不到 3,000 行；`MainWindow.xaml` 只剩导航、标题栏和状态栏。
- 启动时只创建当前页面；记录启动时间的前后对比（`startup.log`）。

## 风险与对策

| 风险 | 对策 |
| --- | --- |
| 隐藏的跨页依赖（例如更新页设置导航徽标、命令面板直接选中 Mod、状态栏消息） | 每页拆分前先做“查牵连”清单；改成事件或导航参数后再移动 |
| 行为在移动中被顺手改掉 | 移动和改进分开提交；J1–J7 的 PR 默认“界面不变”，截图对比把关 |
| 界面测试依赖控件名 | J0 记录，拆分时保持自动化名称 |
| 浮窗和托盘直接调用窗口方法 | J5 先为浮窗定义接口，托盘留在外壳 |
| 一次改动太大，难以评审 | 一页一个 PR；J6、J7 必要时再按功能区拆成两个 PR |
| 和功能开发冲突 | 拆分期间新功能尽量做在已经拆出的页面；正在拆的页面暂停其他改动 |

## 不在范围内

- 换界面引擎（Avalonia、Uno、WPF）。Linux 尝试仍按路线图 E 搁置；服务层拆好后，将来再评估会容易一些。
- 重新设计页面外观。除了 J2 的设置卡片，J1–J8 的“移动”提交外观保持现状。逐页的外观改进见[界面外观优化方案](./UI-Fluent-Design-Plan.md)：它新增 J1T（设计令牌）、J1N（外壳）两个阶段和 J3-b、J4-b、J6-b、J7-b 几个后续 PR，外观改动一律与结构移动分开提交（现有的「工作台新布局」画布只覆盖 Mod 工作区和全局浮层，作为其中 J7-b 的参考）。
- 截图二阶段（WGC）：维护者 2026-11-04 决定不做。

## English summary

The whole UI is one `MainWindow`: 67 partial files (about 23,800 lines) sharing one set of fields, and one 2,142-line XAML with 406 named controls, where the six pages are top-level containers toggled by `ApplyShellState`; about 25 files create `ContentDialog`s directly. The plan keeps WinUI 3 and splits it into pages without changing how the app looks. A hand-written `AppServices` composition root holds repository, shell config, language, dialog, deployment, download, launch and status services; `MainWindow` keeps the title bar, navigation, status bar, tray, single instance, global hotkeys, overlay and command palette, and hosts pages in a `ContentControl` that creates each page on first use and keeps it alive, matching today's behaviour. Pages never reference `MainWindow`; cross-page work goes through service events or a navigation service. Stages, one PR each: J0 baseline screenshots, automation names and a toolkit compatibility check (S); J1 services and a shell that hosts the existing containers as wrapper pages (M–L); J2 Settings with Community Toolkit `SettingsCard`/`SettingsExpander` (S–M); J3 Updates (M); J4 Dashboard (S–M); J5 Shortcuts, with the overlay talking through an interface (M); J6 Online (L); J7 Mod workspace (L); J8 dialog classes and split resource dictionaries (S–M). Each page move first lists its cross-page references, then moves XAML and code, deletes the partial files, and passes screenshot comparison in Chinese, English and Russian at two window sizes plus the UI tests and a new smoke test. Out of scope: switching UI engines, redesigning pages (other than the Settings cards), and screenshot phase 2, which the maintainer dropped on 2026-11-04.
