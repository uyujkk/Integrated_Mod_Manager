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

#### J4 牵连清单（仪表盘）

仪表盘 `DashboardScrollViewer` 里有 59 个命名控件。最大的牵连是三个路径框：它们不只属于仪表盘，整个窗口都把 `SourceTextBox.Text`、`TargetTextBox.Text`、`LauncherTextBox.Text` 当作当前仓库的路径来读写（8 个 partial 文件、约 60 处）。

| 位置 | 做什么 | 改成 |
| --- | --- | --- |
| `Configuration.cs`、`Repositories.cs`、`LocalLibrary.cs`、`RepositoryWorkspace.cs`、`Localization.cs`、`xaml.cs` | 读写三个路径框的文字：加载和保存配置、切换仓库时填入、部署和扫描时读取、浏览后写入 | 新的 `RepositoryPathsViewModel`（`AppServices.Paths`）是路径的唯一来源；仪表盘的路径框双向绑定它，窗口只读写它 |
| `CombinationPresets.cs`、`LocalLibrary.cs` | 应用组合预设、部署时禁用路径框和“复制/链接”开关 | `Paths.ArePathsEditable`、`Paths.IsLinkDeploymentEditable` |
| `Repositories.cs`、`RepositoryWorkspace.cs` | 切换仓库时设置“复制/链接”开关；开关的处理；目标路径变化时刷新工作区状态 | `Paths.UseDirectoryLinks` 和它的变化事件 |
| `EndfieldBridge.cs` | 终末地启动设置按钮的显示、文字、可用；运行启动器时读写启动器路径 | 按钮状态写进 `DashboardViewModel.Bridge*`；判断、默认 XXMI 位置、启动配置、旧版桥接设置迁移、等待启动结果和“进行中”状态拆成 `EndfieldBridgeService`（仪表盘和 Mod 工作区都用它，所以不放进某一页）；设置对话框和状态栏文字仍在窗口，J8 再把对话框做成类 |
| `Shell.cs` | “编辑当前仓库”按钮的可用；把路径框登记为“记住最后聚焦的文本框”；页面宿主 | `DashboardViewModel.CanEditRepository`；窗口从 `DashboardPage.PathTextBoxes` 登记；仪表盘直接是 `DashboardPage` |
| `RefinedWorkspace.cs`、`PageLayout.cs` | 仪表盘两栏排布、路径行列宽、当前仓库条；路径卡片标题里的仓库名 | 页面按自己的尺寸排布；仓库名用 `DashboardViewModel.CurrentRepositoryName` |
| `Dashboard.cs` | 仓库下拉框、“新建仓库”菜单、路径状态图标、按钮点击 | 页面自己从 `DashboardViewModel.Repositories` 建下拉框，菜单项来自 `NewRepositoryChoices`，状态图标在页面 XAML 里；点击变成 `DashboardAction` 请求；窗口剩下的部分在 `MainWindow.DashboardHost.cs` |
| `RepositoryWorkspace.cs` | Mod 工作区空状态的“编辑仓库”“选择文件夹”按钮把仪表盘按钮当作 sender | 改成窗口自己 |
| `Dialogs.cs`、`FirstRun.cs` | 仓库编辑、在线分类编辑和首次安装三个对话框 | 移到 `Dialogs/RepositoryEditorDialog.cs`、`OnlineRepositoryConfigDialog.cs`、`FirstRunDialog.cs`（`DialogLayout` 放共用的“标签+编辑框”）；选好之后怎么改仓库仍在窗口 |

仪表盘代码访问的别的页面：角色卡片打开 Mod 工作区（`OpenCharacterFromDashboardAsync`，通过卡片自己的命令）；“检查更新”切到更新页并开始检查；“浏览在线”切到在线页；这些都由窗口处理请求。#102 的修复（`HasCharacters` 隐藏空的角色列表）在页面 XAML 里保留。

“预设是否对应某个仓库”的判断移到 `RepositoryPresetMatching`，和路径的状态一起补了单元测试。仪表盘的统计、扫描、仓库卡片的数据仍由窗口计算，因为它们用到在线页的分类信息和工作区已加载的角色。

#### J5 牵连清单（快捷键页和浮窗）

快捷键页在 `MainWindow.xaml` 里只有一个空容器（`ShortcutsScrollViewer` > `ShortcutPaneContent`），整页由 `MainWindow.ShortcutPanel.cs`（1249 行）在代码里搭建，控件存在窗口字段里（约 40 个）。浮窗 `ShortcutOverlayWindow` 本身不调用窗口，但它的内容（`BuildShortcutOverlayContent`）也由窗口搭建，按钮直接调窗口方法，而且和页面共用同一组控件和状态。页面的设置栏有六块是别的 partial 文件往里加的。

| 位置 | 做什么 | 打算改成 |
| --- | --- | --- |
| `ShortcutPanel.cs` | 搭建整页（标题、工具栏、绑定卡片、设置栏）、排布、扫描 INI、发送按键、浮窗内容、全局热键、权限提示、以管理员身份重启 | 页面控件进 `Pages/ShortcutsPage`；扫描、发送、热键、重启留在窗口或服务，经 `ShortcutPageViewModel` 和请求事件交互 |
| 页面和浮窗共用的控件 | 浮窗的 Mod 下拉框把选择转给页面的 `_shortcutModPicker`；浮窗的“输入”开关改页面的 `_shortcutEnabledToggle`；浮窗读页面的目标窗口和状态行；两个透明度滑块互相同步 | 共用状态进 ViewModel：Mod 列表和当前 Mod、目标窗口、`InputEnabled`、`StatusText`、`OverlayOpacity`；页面和浮窗都只绑定 ViewModel |
| `HotReload.cs`、`PresetPreviews.cs`、`ShortcutCapture.cs`、`ShortcutPresets.cs`、`OverlayAppearance.cs`（共 5 个文件，约 20 处） | 写页面的状态行 `_shortcutStatus.Text`，读页面选中的 Mod 和目标窗口 | 改写 `ShortcutPageViewModel.StatusText`、读 ViewModel |
| 同上加 `PersistentSlots.cs`（约 20 处） | `RefreshShortcutOverlay(...)` 刷新浮窗 | 浮窗接口的 `Refresh(bindingsChanged)` |
| `OverlayAppearance.cs`、`OverlayAutoContrast.cs`、`HotReload.cs`、`PresetPreviews.cs`、`SlotCapture.cs`、`CharacterFollow.cs` | 各自往设置栏里加一块控件（外观预设、元素透明度、文字颜色、自动对比度、状态槽位热切换、预设预览、槽位截图区域、角色跟随），控件的事件直接改窗口字段并保存配置 | 设置进 ViewModel（每项一个属性），页面用 `SettingsCard`/`SettingsExpander` 显示；窗口监听属性变化后照旧应用到浮窗并保存 |
| `AppearanceEvents.cs` | 换语言时整页和浮窗重建；换主题时清掉绑定卡片缓存 | 页面自己在 `LanguageChanged`、`ThemeChanged` 里重写文字；浮窗经接口重建 |
| `Shell.cs`、`PageLayout.cs` | 第一次打开时 `BuildShortcutPane` 和刷新；`AttachPageLayout(ShortcutsScrollViewer, UpdateShortcutsLayout)`；关窗口时 `CloseShortcutPanel` | 快捷键页直接是 `ShortcutsPage`，页面按自己的尺寸排布；关闭仍在窗口 |
| `Configuration.cs` | 读写快捷键和浮窗的 15 项设置 | 读写 ViewModel |
| 全局热键（`ShortcutPanelHotkey`） | 窗口注册，按下时 `ToggleShortcutOverlay`；页面的两个下拉框改热键 | 留在窗口；页面改 ViewModel 的热键属性，窗口尝试注册，失败时把属性改回 |
| 托盘 | 不碰快捷键页和浮窗 | 不变，留在外壳 |

不属于这一页的：`ShortcutBindings.cs`（710 行）是 Mod 工作区“快捷键”标签页的编辑器，J7 随工作区一起移。

浮窗接口（打算）：`IShortcutOverlay`，窗口和页面只通过它打开、隐藏、切换、刷新浮窗和查询是否可见、是否点击穿透；浮窗内容改为单独的类，它需要的操作（发送绑定、切换状态槽位、保存预设、从磁盘刷新、选 Mod、开关输入）由 `IShortcutOverlayActions` 提供，窗口实现这个接口。

范围：上面这些合计约 3100 行、10 个文件，比阶段表里估的 M 大。

#### J6 牵连清单（在线页）

2026-11-09 清点（`claude/ui-j6-online`，叠在 J8 之上）。在线页的 15 个 partial 文件共 5,414 行：`OnlineFeed`（1,036）、`OnlineDetails`（1,040）、`OnlineCharacters`（1,044）、`OnlineDownloadFlow`（758）、`OnlineCards`（476）、`OnlineImageCache`（204）、`OnlineBrowse`（179）、`OnlineLibrary`（146）、`BatchDownloads`（131）、`OnlineDownloads`（99）、`OnlineInstall`（83）、`DownloadRecovery`（76）、`OnlineState`（73）、`AuthorWorkspace`（35）、`OnlineImages`（34）；另有和它们紧绑在一起的 `BrowseSessions.cs`（168 行，浏览位置和恢复）。XAML 在 `MainWindow.xaml:664-1141`（约 480 行、61 个以 `Online` 开头的命名控件）。

| 方向 | 数量 | 主要内容 | 打算改成 |
| --- | --- | --- | --- |
| 别的文件用在线页的成员 | 约 50 个 | 下载流水线（`DownloadOnlineModArchiveAsync`、`_onlineDownloadScheduler`、`RetryOnlineDownloadAsync`、`PersistDownloadTasks`）被 Mod 更新、下载任务、更新页用；`_trackedModOrigins` 相关（`DeduplicateTrackedModOrigins`、`RemoveTrackedModRecordAsync`）被配置和更新页用；角色目录（`EnsureOnlineCharacterCatalogAsync` 等）被角色头像、仓库设置用；`GetEffectiveOnlineSource/CategoryId/GameName` 被仪表盘、设置、托盘用；图片加载（`LoadLocalBitmapAsync`、`LoadOnlineBitmapAsync`、`SetCachedOnlineImageAsync`）被工作区、头像、更新页用；`IsPathInsideDirectory`、`FormatFileSize` 等通用小函数 | 下载流水线进 `DownloadService`；图片缓存进 `ImageService`；角色目录和“有效在线来源”进新的 `OnlineCatalogService`；通用小函数进 Core；页面自己的状态留在 `OnlinePageViewModel` |
| 在线页用窗口的成员 | 约 100 个 | `_services`、`GetSelectedRepository`、`SaveShellConfig`、对话框、通知、`_httpClient`、`_gameBananaMetadataService`、`_appDataStore`、`_trackedModOrigins`/`_modLinks`/`_modBindings`、本地库刷新（`RefreshListsAsync`、`SelectSecondLevelByPath`）、浏览会话、快捷键自动绑定、下载任务卡片 | 经服务和请求事件：仓库服务、对话框服务、状态服务、新的在线服务；安装后刷新工作区、选中新 Mod 用请求事件交给窗口 |

规模：L 偏大（约 5,600 行代码 + 480 行 XAML，两个方向约 150 处牵连），加上 J6-b 的外观和流程，按步骤分开提交：服务 → 纯移动（分几次）→ 外观 → 测试和截图。

#### J6 后续：拆开 `IOnlineHost`

这次（用户 2026-11-09 选 A）在线页整体移到 `Pages/OnlinePage.xaml` 和 `Pages/Online/`，页面不引用 `MainWindow`，用到的窗口功能都经 `IOnlineHost`。移动后接口有 **47 个成员**，按用途分 8 组：外壳（页面是否显示、根元素和窗口大小、主题画刷、密度和动画、选文件夹、打开链接、看图）、错误和日志、保存设置、仓库和 Mod 工作区、快捷键绑定、追踪的 Mod 和下载任务、HTTP 客户端和本地索引、共用常量和小函数。角色目录和“有效在线来源”这次仍在页面里，窗口经 `_onlineView` 调用。

以后拆成服务，每拆一块就从接口里删掉对应的成员：
- 角色目录服务：`EndfieldCharacters`、`IsFavoriteCharacter`、`ToggleFavoriteCharacter`、`BuiltInRepositoryPresets`、`IsRepositoryForPreset`、`EndfieldOfficialWikiUrl`，以及页面里的角色目录代码（窗口的头像和仓库设置也在用）；
- 在线来源服务：`DefaultOnlineCategoryId`、`DefaultOnlineSourceSite`、`Http`、`Metadata`、`DataStore`，以及页面里的 `GetEffectiveOnline*`（仪表盘、设置、托盘在用）；
- 安装流程服务：`ExtractArchiveToDirectory`、`IsSupportedArchiveFile`、`FindPreviewImage`、`DeleteDirectoryTreeSafely`、`ModBindings`、`ModLinks`、`CreateAutomaticShortcutBinding`、`ShortcutScanSafetyLimit`、`TrackedModOrigins`、`TrackedModUpdateResults`、下载任务的四个成员；
- 安装后刷新工作区（`RefreshListsAsync`、`SelectSecondLevelByPath`、`SetBusyState`）改成请求事件；
- 窗口还在用的页面小函数（`IsPathInsideDirectory`、`FormatFileSize` 等，现在是 `OnlinePage` 的静态成员）移进 Core。

#### J7 牵连清单（Mod 工作区）

2026-11-10 清点（`claude/ui-j7-workspace`，main `4f8da19`）。

- 代码：20 个 partial 文件共 6,455 行。原计划的 17 个，加上 J5 留下的 `ShortcutBindings`（快捷键标签页的编辑器，690 行），以及之后新增的 `CopyOwnership`（#107）和 `CharacterRename`（#108）。
- XAML：`MainWindow.xaml:207-647`（441 行），加上隐藏容器 `CombinationProfileControlsHost`（`:652-686`，J3 留下的组合预设卡片），共 109 个命名控件。

| 方向 | 数量 | 主要内容 |
| --- | --- | --- |
| 别的文件用工作区的成员 | 约 90 个 | **约一半来自快捷键页和浮窗**：`ShortcutPanel`、`ShortcutPresets`、`ShortcutsHost`、`HotReload`、`SlotCapture`、`ShortcutCapture`、`CharacterFollow`。它们用状态槽位、默认槽位、预设预览和组合预设的代码，如 `RefreshPersistentSlotControls`、`TryGetDefaultPersistentIniPath`、`CapturePresetPreviewAsync`、`_applyingCombinationProfile`、`_restoringPersistentRuntime`。其余来自：配置读写（`Configuration`、`ConfigurationProfiles`）；外壳（`RefreshListsAsync`、`SelectSecondLevelByPath`、命令面板）；仪表盘（路径与启动器按钮的处理）；在线页的 `IOnlineHost`（安装后刷新、解压、自动快捷键绑定）；更新页（打开组合工作区） |
| 工作区用窗口的成员 | 约 71 个 | 共享状态：`_characters`、`_secondLevelItems`、`_currentSecondLevelPath`、`_modBindings`、`_modLinks`、`_modPersistentSlots`、`_trackedModOrigins`、`_configurationProfiles`、`_shortcuts`、`_overlay`。还有对话框、保存设置、仓库切换、部署模式开关、安装事务回滚、热重载、浮窗刷新、在线页 |

规模：XL（L 偏大以上），比 J6 大约 20%。真正的难点不在行数：状态槽位、预设预览、组合预设这几块由工作区和快捷键浮窗共用，而这几块操作的结果（状态槽位保存、组合预设恢复）在不能改动的清单上。

拆分（待用户决定）：
- 方案一（建议）：J7 只移界面，即工作区页面、角色和 Mod 列表、详情、快捷键编辑器、导入、标签、批量操作、复制归属和重命名。状态槽位、默认槽位、预设预览、组合预设的逻辑这次留在窗口里，页面经 `IWorkspaceHost` 使用；之后单独拆成 `SlotService` 和 `CombinationService`。外观按 Fluent 方案放到 J7-b，单独一个 PR，在 J7 合并之后。
- 方案二：照 J6 的做法，全部文件随页面移走，浮窗和快捷键页经 `_workspaceView` 调用；J7 和 J7-b 在同一个分支里分提交完成。

#### J7 后续：拆开 `IWorkspaceHost`

这次（用户 2026-11-10 选方案一）只移界面：工作区的 XAML（连同只它用的样式和 `CombinationProfileControlsHost`）和 9 个界面文件移到 `Pages/WorkspacePage.xaml` 和 `Pages/Workspace/`。移过去的文件：`RepositoryWorkspace`、`RefinedWorkspace`、`LocalLibrary`、`LocalTags`、`BatchModActions`、`CharacterSelection`、`ExistingModImport`、`CharacterAvatars`，以及 `ShortcutBindings` 的编辑器部分。

留在窗口的逻辑：状态槽位、变体、默认槽位、预设预览、组合预设和组合包、预设分享、压缩包、复制归属（#107）、角色重命名迁移（#108）、INI 冲突检测，以及 `ShortcutBindingRules`（快捷键规则，在线安装也用）。

页面不引用 `MainWindow`，用到的窗口功能都经 `IWorkspaceHost`。接口现在有 **77 个成员**（76 个名字，`DetectModFileConflicts` 有两个重载），按用途分 18 组，每组一行注释。有 12 个控件的事件处理在留下的逻辑文件里（状态槽位、组合预设），由窗口创建页面后订阅（`AttachWorkspacePageEvents`）。

以后拆成服务，每拆一块就从接口里删掉对应的成员：
- `SlotService`：状态槽位、默认槽位、变体，以及共享的 `ModPersistentSlots`、`DefaultSlotSeeds`。浮窗和快捷键页也用它们。
- `PresetPreviewService`：预设预览的截图、编辑和存储。
- `CombinationService`：组合预设、组合包、预设分享和 `ConfigurationProfiles`。
- 安装事务、压缩包、复制归属、INI 冲突合成一个 `InstallService`，和 J6 后续的“安装流程服务”合并。
- 共享状态（`Characters`、`SecondLevelItems`、`ModBindings`、`ModLinks`）进 `WorkspaceState`，仪表盘、浮窗和命令面板都从这里读。

### J8. 对话框与资源

- 每种对话框一个类（`Dialogs/` 下），由 `DialogService` 统一设置 `XamlRoot` 并保证同一时间只有一个对话框。
- `App.xaml` 拆成 `Themes/Colors.xaml`、`Styles/Controls.xaml`、`Styles/Pages.xaml`；页面专用样式放进页面自己的资源。

#### J8 范围（2026-11-09 清点，main `e9def8b`）

代码里共 39 处建 `ContentDialog`（`new ContentDialog` 35 处，`ContentDialog dialog = new()` 4 处），分布在 29 个文件；另外 36 处直接调用 `dialog.ShowAsync()`，各自设置 `XamlRoot`，没有排队。

| 类别 | 数量 | 位置 |
| --- | --- | --- |
| 已是独立类 | 3 | `Dialogs/FirstRunDialog`、`RepositoryEditorDialog`、`OnlineRepositoryConfigDialog` |
| 共用的三种（`MainWindow.Dialogs.cs`） | 3 | `ShowMessageAsync`（115 处调用）、`ShowConfirmAsync`（22 处）、`PromptForTextAsync`（10 处） |
| 简短确认、提示 | 9 | 重新开始恢复的下载、在线重复安装、安装冲突、读取已有文件（快捷键）、状态槽位、Mod 更新提醒、新功能、仓库详情、下载选项 |
| 改动前的预览 | 8 | 组合预设恢复、组合包安装、失效联接清理、加载器状态恢复、user.ini 快照恢复、撤销整理导入、恢复中断的导入、导入分享的预设 |
| 表单、多步流程 | 16 | 本地标签、角色头像绑定、保存单 Mod 预设、选择下载文件、在线安装位置、Wiki 分类对应、整理导入（选目标、预览、进行中 3 个）、批量下载（准备、确认 2 个）、批量 Mod 操作、下载并替换 Mod、角色区域选择、作者与收藏、终末地启动设置（363 行，J4 留下） |

在 `MainWindow` 的 partial 文件里直接搭建的有 33 个，所在的方法共约 2,200 行；最大的是终末地启动设置（363 行）、批量下载（159 行）、选择下载文件（121 行）。这比计划里的“约 25 处、S–M”大，加上积压到 J8 的外观和流程改动（危险样式、动词按钮、“查看详情”、命令面板分组和投影、1000 宽标题栏），整体约为 L，所以拆成三步：

| 步骤 | 内容 | 工作量 |
| --- | --- | --- |
| J8a 对话框服务和资源 | `DialogService`（统一 `XamlRoot`、`DefaultContentDialogStyle`、排队，提供提示、确认、危险确认、错误、输入五种接口）；共用的三种改走服务，其余 36 处 `ShowAsync` 改成 `Dialogs.ShowAsync(dialog)`（只改这一行）；`App.xaml` 拆分；单独提交的外观：22 个确认和 10 个输入框的主按钮改成动词，删除、移除、覆盖类改用危险样式、默认焦点在“取消”，工作区“从目标文件夹移除”同样处理；加 Esc 测试 | S–M |
| J8b 对话框类 | 纯移动：33 个对话框按上表三类分三次提交移到 `Dialogs/`，含终末地启动设置；之后单独提交的外观：内容超过两段的用 `ScrollViewer`，最大高 400 | M |
| J8c 错误详情和命令面板 | 错误 `InfoBar` 和错误对话框加“查看详情”（展开、复制异常和日志路径）；命令面板按页面、角色、Mod、命令分组并加投影；1000 宽时标题栏搜索框缩成图标按钮 | S–M |

J8b、J8c 都用到 J8a 的服务，两者之间没有依赖。

2026-11-09 用户决定不拆分，在 `claude/ui-j8-dialogs` 上一次完成，提交按步骤分开：

1. `DialogService`：所有对话框都由它显示，统一设置 `XamlRoot`、主题和 `DefaultContentDialogStyle`；排队逻辑在 Core 的 `DialogQueue`，同一时间只显示一个对话框，排队时就被关闭的对话框（进度框）不再弹出。提供提示、确认、危险确认、错误、输入五个接口。
2. `App.xaml` 只合并资源：颜色在 `Themes/Tokens.xaml`，控件样式在 `Styles/Controls.xaml`，几个页面共用的卡片、页头、内嵌面板在 `Styles/Pages.xaml`；只有窗口自己的 XAML 用的样式放进 `RootGrid.Resources`；五个没人用的样式删除。
3. 纯移动，三次提交（简短确认 9 个、改动前预览 8 个、表单和多步流程 16 个），全部在 `Dialogs/` 下，包括终末地启动设置。每个类拿数据、返回选择；对话框里要直接调用窗口功能的（作者与收藏、终末地设置）用一个小的 `Host` 记录传入。`MainWindow` 的 partial 文件共 21,406 → 20,073 行。
4. 设计：主按钮用动词；删除、移除、覆盖类 14 个对话框用固定红色的 `DangerAccentButtonStyle`，默认按钮和焦点都在“取消”，工作区的“从目标文件夹移除”同样用红色；预览按“将新增 / 将移除 / 保留 / 跳过”分组并显示数量；表单用卡片分组（`DialogLayout.Section`），出错提示放在字段下方，多步流程显示“第 n 步，共 m 步”和进度条；文字对话框最高 400 后滚动。
5. 错误提示和错误对话框加“查看详情”（异常类型和代码、消息、调用栈、`app-errors.log` 路径，可复制）；命令面板按页面、角色、Mod、命令分组，加 `ThemeShadow`（窗口内的阴影需要接收层，用面板下的透明层承接）；窗口窄于 1120（含 1000）时标题栏搜索框变成搜索按钮。

## 完成标准

每个阶段：

- 页面类不引用 `MainWindow`，只通过 `AppServices` 和导航服务与外部交互。
- 新的 ViewModel 和从界面移到 Core 的逻辑有单元测试；四组覆盖率门槛不降低。
- 中英俄三种语言、两种窗口尺寸的截图与基线一致（有意的改动在 PR 里说明）。
- `test-all`、代码风格检查和界面测试全部通过；Release 构建无警告。
- 语言文件的键只增不改名；用不到的键在同一个 PR 里删掉。

全部完成后（用户 2026-11-10 按“收尾盘点”调整）：

- `MainWindow` 里不再有业务逻辑：部署、状态槽位、预设、组合、安装、下载都在服务里；窗口不超过约 4,000 行。3,000 行（收尾第 9 步）作为可选目标。`MainWindow.xaml` 只剩导航、标题栏和状态栏。
- 启动时只创建当前页面；记录启动时间的前后对比（`startup.log`）。

## 收尾盘点（2026-11-10，叠在 #122 上）

盘点完 J1–J8 之后离“全部完成后”两条标准还差多少，以及怎样分步做完。**这一节只是盘点和方案，还没有改代码；先等用户决定。**

### 1. 按需创建页面

现在启动时六个页面全部创建：
- `OnlinePage` 和 `WorkspacePage` 在 `MainWindow` 构造函数里、紧跟 `InitializeComponent()` 创建；
- 设置、仪表盘、更新、快捷键四页在 `InitializePageHost()` 里创建。

**启动基线**：`startup.log`，从进程启动算起，7 次的中位数。构建是 #122 的顶端 `88f2f70`，界面测试的合成数据，起始页分别是仪表盘和工作区。

| 阶段 | 仪表盘起始 | 工作区起始 |
| --- | --- | --- |
| 进入 `MainWindow` 构造函数 | 330 ms | 333 ms |
| 窗口 XAML 和六个页面创建完 | 610 ms | 611 ms |
| 读完配置 | 813 ms | 811 ms |
| 构造函数结束 | 916 ms | 916 ms |
| 窗口第一次显示（`RootGrid loaded`） | **1,065 ms** | **1,055 ms** |

另做了一次临时测量（没有提交），给每页的创建单独计时：
- 设置页 69 ms；
- 更新页 32 ms；
- 仪表盘 31 ms；
- 快捷键页 29 ms；
- 窗口 XAML 加在线页和工作区页，合计 118 ms。

所以只创建当前页面，从仪表盘启动大约能省 **150–200 ms**，约占首次显示时间的 15–20%。

要改的地方：

| 页面 | 窗口直接用到它的地方 | 改成按需创建要做的事 | 难度 |
| --- | --- | --- | --- |
| 设置、仪表盘、更新、快捷键 | 只经服务和 ViewModel（`SettingsViewModel`、`DashboardViewModel`、`UpdatesViewModel`、`ShortcutPageViewModel`）。仪表盘有一处直接读 `DashboardPage.PathTextBoxes`；快捷键页有 4 处引用 `_shortcutsPage` | `_pages` 改成首次显示时创建。页面在构造时从 ViewModel 读取当前状态（这四页已经这样做）。`PathTextBoxes` 改成页面加载后再登记 | 低 |
| 在线页 | `_onlineView.*` 有 206 处，在 30 个文件里，用到 61 个成员：启动时读下载任务、初始化控件、浏览会话；配置读写 `_onlinePage` 和 `_onlinePreferences`；`IOnlineHost` 反向调用 | 先把在线状态（`_onlinePage`、偏好、下载任务）移到服务或 ViewModel，窗口不再直接碰页面控件；之后才能按需创建 | 中，依赖第 3、5 步 |
| 工作区页 | `_workspaceView.*` 有 268 处，在 27 个文件里，用到 107 个成员：列表数据源、状态槽位控件、组合预设控件、配置读写 `_modList` 和 `_localLibraries`、命令面板、仪表盘 | 先把共享状态（角色、Mod 列表、绑定）移到 `WorkspaceState`，槽位、预设、组合的控件改由页面自己更新；最后才能按需创建 | 高，依赖第 6、7 步 |

### 2. 主窗口剩下的代码

主窗口现在有 10,421 行（48 个 partial 文件）。`MainWindow.xaml` 有 145 行，只剩标题栏、导航和页面容器，“只剩外壳”这一条在 XAML 上已经达到。代码按功能分块：

| 块 | 文件（行数） | 合计 | 谁在用 | 拆成服务的难度和风险 |
| --- | --- | --- | --- | --- |
| 状态槽位 | `PersistentSlots`（363）、`PersistentVariants`（312）、`DefaultSlots`（139）、`SlotCapture`（82）、`HotReload`（118） | 1,014 | `IWorkspaceHost`：`RefreshPersistentSlotControls`、`ModPersistentSlots`、`DefaultSlotSeeds`、`SeedDefaultSlotsAsync`，以及 5 个由窗口订阅的槽位按钮。`IShortcutOverlayActions`：`OverlaySlots`、`CurrentSlot`、`SwitchSlotAsync`、`RefreshFromDiskAsync`、`ModRelativePath` | 难，风险高：状态槽位保存和 F10 时序都在这里。只能整体移动，配“保存→恢复结果不变”的测试 |
| 预设预览 | `PresetPreviews`（404） | 404 | `IWorkspaceHost`：`CreatePresetPreviewEditor`、`CreatePresetPreviewImage`。浮窗：`ShowSlotGalleryAsync`、`FrameCurrentSlotPictureAsync`、`CreatePresetPreviewImage` | 中：截图和文件读写，不碰部署 |
| 组合预设和组合包 | `ConfigurationProfiles`（198）、`CombinationPresets`（223）、`CombinationBundles`（216）、`PresetShare`（95）、`ShortcutPresets`（100） | 832 | `IWorkspaceHost`：`GetCurrentRepositoryProfiles`、`GetSelectedConfigurationProfile`、`RefreshConfigurationProfiles`、`ConfigurationProfiles`、`SelectedConfigurationProfileId`、`ExportModsAsBundleAsync`、`InitializeCombinationBundleActions`，以及 7 个由窗口订阅的预设按钮。浮窗：`SaveCombinationPresetAsync`、`SaveSingleModPresetAsync` | 难，风险高：组合预设恢复在这里。只移动，配恢复预览和恢复结果的测试 |
| 安装流程 | `InstallTransactions`（232）、`Archives`（163）、`IniConflicts`（74）、`CopyOwnership`（71）、`CharacterRename`（120） | 660 | `IWorkspaceHost`：事务 9 个、压缩包 3 个、INI 1 个、复制归属 3 个、重命名 1 个。`IOnlineHost`：`ExtractArchiveToDirectory`、`IsSupportedArchiveFile`、`DeleteDirectoryTreeSafely`。还有 `ModUpdates` | 中：大多是文件操作，已有 Core 规则和 #105、#107、#108、#109 的界面测试作保护 |
| 快捷键发送、面板和角色跟随 | `ShortcutPanel`（526）、`ShortcutsHost`（70）、`ShortcutCapture`（83）、`ShortcutBindingRules`（155）、`CharacterFollow`（200） | 1,034 | `IShortcutOverlayActions` 的其余成员。`IOnlineHost`：`CreateAutomaticShortcutBinding`、`ModBindings`、`ModLinks`。`IWorkspaceHost`：绑定规则 3 个 | 中：发送按键和输入法；F10 时序要单独测 |
| 应用更新、Mod 更新、下载任务 | `Updates`（575）、`ModUpdates`（120）、`DownloadTasks`（226）、`UpdatesHost`（167） | 1,088 | `IOnlineHost`：下载任务 4 个、`RefreshUpdatesPane`、`SetModUpdateStatus`、`TrackedModOrigins`、`TrackedModUpdateResults` | 中：下载和安装的结果不能变；`DownloadService` 已经有调度器和日志 |
| 仓库与配置 | `Repositories`（540）、`Configuration`（503）、`BuiltInData`（36）、`RepositoryPerformance`（28） | 1,107 | 两个 host 都在用：`GetSelectedRepository`、`SwitchRepository`、`SaveShellConfig`、`SaveConfig`、`BuiltInRepositoryPresets` 等 | 中：`RepositoryService`、`ShellConfigService` 已经有；风险在配置读写顺序 |
| 启动器 | `EndfieldBridge`（248） | 248 | `IWorkspaceHost`：`RunRepositoryLauncherAsync`；仪表盘 | 低到中：`EndfieldBridgeService`、`LaunchService` 已经有 |
| 页面接线 | `DashboardHost`（312）、`SettingsHost`（286）、`OnlineHost`（81）、`WorkspaceHost`（126） | 805 | 窗口和各页之间的请求与转发 | 随上面各步自然变小 |
| 外壳本身 | `Shell`（647）、`ProductivityFeatures`（691：日志、无障碍、诊断、备份）、`Tray`（426）、`Dialogs`（391）、`CommandPalette`（323）、`xaml.cs`（218）、`Localization`（202）、`WhatsNew`（68）、`UiActionGuard`（68）、`AppearanceEvents`（59）、`FirstRun`（54）、`SingleInstance`（47）、`TitleBarSearch`（30）、`UiDiagnostics`（5） | 3,229 | — | 大部分本来就该留在窗口；命令面板和托盘可以各自做成独立的类 |

### 3. 三个过渡接口

| 接口 | 成员 | 拆向 |
| --- | --- | --- |
| `IShortcutOverlayActions` | 22 | `SlotService`（槽位 5 个）、`PresetPreviewService`（3 个）、`CombinationService`（存预设 2 个）、`ShortcutSender`（发送按键、目标窗口、已部署 Mod，6 个）。剩下的 6 个（`IsClosing`、`IsSavingFromOverlay`、`HotkeyAvailable`、`RunAsync`、`ReportFailure`、`LogIssue`）由外壳提供 |
| `IOnlineHost` | 47 | `CharacterCatalog`（约 6 个：角色、收藏、预设与 wiki）、`OnlineSource`（约 5 个：分类、来源站点、HTTP、元数据、本地索引）、`DownloadService` 扩展（追踪与任务，约 8 个）、`InstallService`（约 6 个）。外壳类成员约 22 个（对话框、选文件夹、报错、保存、仓库刷新），改成页面直接用 `AppServices` |
| `IWorkspaceHost` | 77 | `InstallService`（约 17 个）、`SlotService`（约 4 个）、`CombinationService`（约 7 个）、`PresetPreviewService`（2 个）、`CharacterCatalog`（约 5 个）、`WorkspaceState`（共享列表与绑定，约 12 个）。外壳类成员约 30 个 |

两个 host 的外壳类成员加起来约 50 个，大多已经有对应的服务（`DialogService`、`StatusService`、`ShellConfigService`、`RepositoryService`），只是还经窗口转一道，最容易先拿掉。

### 4. 拆分方案（每步一个 PR，先低风险、收益大的）

| 步 | 内容 | 风险 | 窗口剩余（约） | 证明结果没变的测试 |
| --- | --- | --- | --- | --- |
| 1 | 设置、仪表盘、更新、快捷键四页按需创建 | 低 | 10,450 | 启动前后对比（`startup.log`）；来回切页测试；四页首次打开的界面测试 |
| 2 | 两个 host 的外壳类成员（对话框、选择器、报错、保存、仓库刷新）改成页面直接用 `AppServices`，删掉这些转发 | 低 | 10,100 | 现有界面测试；接口成员数下降 |
| 3 | `CharacterCatalog`、`OnlineSource` 服务（角色目录和“有效在线来源”从在线页和窗口移入）；在线页改成按需创建 | 中低 | 9,900 | 在线样例测试（`IMM_ONLINE_FIXTURES`）；请求清单不变的单元测试 |
| 4 | `InstallService`：安装事务、压缩包、INI 冲突、复制归属、重命名迁移，纯移动 | 中 | 9,250 | 复制归属、删除安全、重命名的界面测试；事务回滚的单元测试 |
| 5 | Mod 更新、追踪、下载任务并入 `DownloadService`、`UpdatesViewModel` | 中 | 8,600 | 更新页界面测试；下载恢复的单元测试 |
| 6 | `SlotService` 和 `PresetPreviewService`：状态槽位、默认槽位、变体、截图、热重载、预览，纯移动 | 高 | 7,200 | 槽位保存→恢复结果的测试；浮窗切换槽位和 F10 时序的测试；先补测试再移动 |
| 7 | `CombinationService`：组合预设、组合包、预设分享、浮窗存预设，纯移动；工作区页改成按需创建 | 高 | 6,400 | 恢复预览和恢复结果的测试；组合包导入导出的单元测试 |
| 8 | 仓库与配置进 `RepositoryService` 和 `ShellConfigService`，启动器进 `LaunchService`，命令面板和托盘各成独立的类，页面接线随之缩小 | 中 | 约 4,000 | 全部界面测试；配置读写往返的单元测试 |
| 9（可选） | 诊断、备份和无障碍拆出 `ProductivityFeatures`，设置页接线改进 ViewModel | 低 | 约 3,000 | 诊断导出和备份恢复的测试 |

**关于 3,000 行的目标**：做完第 8 步约剩 4,000 行。其中外壳本身（导航、标题栏、本地化、托盘和命令面板的入口、单实例、首次运行、报错）约 2,500 行，页面接线和少量协调约 1,000–1,500 行。到 3,000 行要做到第 9 步。

建议把完成标准改成：**窗口里不再有业务逻辑（部署、槽位、预设、安装、下载都在服务里），窗口约 4,000 行以内**。3,000 行作为第 9 步的延伸目标。理由是第 9 步拿掉的诊断、备份、无障碍都是外壳级功能，移出去减少的只是行数，不会让页面更独立，也不会降低风险。

第 6、7 步风险最高，状态槽位保存、F10 时序和组合预设恢复都在这两步里。这两步都先补“结果不变”的测试，再纯移动代码，不顺手改任何行为。

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
