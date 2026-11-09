# 界面外观优化方案 / UI Fluent design plan

2026-11-04，基于 `49fcca2`（界面结构拆分计划）。这是设计方案，不是已实现功能。它是[界面结构拆分计划](./UI-Structure-Plan.md)（阶段 J）的配套文档：拆分计划规定**结构怎么拆**，这里规定**每页的外观怎么改、改在哪个阶段**。工作量粗估沿用：S ≈ 1–2 天，M ≈ 3–5 天，L ≈ 2 周以上。

参考只用微软公开的设计文档、控件文档和公开截图里能看到的布局模式；不复制微软应用的图标、插画、配色或其他品牌素材。

## 原则

1. **移动和外观分开提交。** 每个 J 阶段的 PR 先有一个“纯移动”提交，截图与基线一致；外观改动放在其后的独立提交里，PR 说明列出“有意的截图差异”。S–M 的外观改动放在同一个 PR 里单独提交；M 以上的另开一个 `Jn-b` PR（见各节），不和移动混在一个 PR 里评审（已决定，见文末）。
2. **先令牌，后页面。** 颜色、字号、圆角、按钮高度这些全局值改一次，会让所有页面的截图都变。所以把它们集中在新阶段 J1T（设计令牌），做完后**重新拍一次基线**；之后每页的外观提交只改这一页。
3. **继续用 WinUI 3。** 只用 Windows App SDK 1.7 自带控件和 Windows Community Toolkit（`CommunityToolkit.WinUI.Controls.*`）。不换引擎，不引入其他控件库。
4. **语言与窗口尺寸。** 每项改动都要在中文、英文、俄语，1400×900 和 1000×800 两种窗口下检查（深浅两种主题各一组）。俄语通常是中文的 2–3 倍长（例如“Mod 快捷键 / Mod shortcuts / Горячие клавиши мода”），按钮和导航项按俄语宽度定最小宽度，标签允许换两行或省略号加提示。
5. **自动化名称不变。** 换控件（例如自制导航按钮换 `NavigationViewItem`）时，用 `AutomationProperties.AutomationId` 保留原来的 `x:Name`，`tests/UI.Tests` 靠它们定位。

两种窗口在现有外壳里的实际情况（`MainWindow.Shell.cs:213-271`）：宽度 < 1120 是 Minimal，< 1440 是 Compact，所以 **1400×900 和 1000×800 都只显示图标导航栏**（84 px），内容区约 1280 px 和 880 px。按微软的响应式断点，1000 px 属于“中等”（641–1007），1400 px 属于“大”（≥ 1008）。

## 微软参考资料

| 主题 | 链接 |
| --- | --- |
| Windows 应用设计总览 | https://learn.microsoft.com/windows/apps/design/ |
| Fluent 2 设计系统 | https://fluent2.microsoft.design/ |
| 字体层级（Caption 12、Body 14、Subtitle 20、Title 28…） | https://learn.microsoft.com/windows/apps/design/signature-experiences/typography |
| 几何与圆角（控件 4 px、浮层 8 px） | https://learn.microsoft.com/windows/apps/design/signature-experiences/geometry |
| 颜色与主题画刷 | https://learn.microsoft.com/windows/apps/design/signature-experiences/color |
| 材质：Mica、Acrylic | https://learn.microsoft.com/windows/apps/design/style/mica ・ https://learn.microsoft.com/windows/apps/design/style/acrylic |
| 间距与 4 px 网格 | https://learn.microsoft.com/windows/apps/design/style/spacing |
| 响应式断点 | https://learn.microsoft.com/windows/apps/design/layout/screen-sizes-and-breakpoints-for-responsive-design |
| 标题栏设计 / TitleBar 控件（App SDK 1.7 新增） | https://learn.microsoft.com/windows/apps/design/basics/titlebar-design ・ https://learn.microsoft.com/windows/apps/develop/title-bar |
| NavigationView | https://learn.microsoft.com/windows/apps/design/controls/navigationview |
| InfoBar / InfoBadge | https://learn.microsoft.com/windows/apps/design/controls/infobar ・ https://learn.microsoft.com/windows/apps/design/controls/info-badge |
| BreadcrumbBar | https://learn.microsoft.com/windows/apps/design/controls/breadcrumbbar |
| SelectorBar | https://learn.microsoft.com/windows/apps/design/controls/selector-bar |
| ItemsView / ListView 与 GridView | https://learn.microsoft.com/windows/apps/design/controls/itemsview ・ https://learn.microsoft.com/windows/apps/design/controls/listview-and-gridview |
| 列表/详情模式 | https://learn.microsoft.com/windows/apps/design/controls/list-details |
| CommandBar | https://learn.microsoft.com/windows/apps/design/controls/command-bar |
| TeachingTip | https://learn.microsoft.com/windows/apps/design/controls/dialogs-and-flyouts/teaching-tip |
| ContentDialog | https://learn.microsoft.com/windows/apps/design/controls/dialogs-and-flyouts/dialogs |
| Expander | https://learn.microsoft.com/windows/apps/design/controls/expander |
| 进度控件（ProgressBar / ProgressRing） | https://learn.microsoft.com/windows/apps/design/controls/progress-controls |
| AutoSuggestBox（搜索） | https://learn.microsoft.com/windows/apps/design/controls/auto-suggest-box |
| 应用设置指南 | https://learn.microsoft.com/windows/apps/design/app-settings/guidelines-for-app-settings |
| 键盘无障碍、访问键、快捷键 | https://learn.microsoft.com/windows/apps/design/accessibility/keyboard-accessibility ・ https://learn.microsoft.com/windows/apps/design/input/access-keys ・ https://learn.microsoft.com/windows/apps/design/input/keyboard-accelerators |
| WinUI 3 Gallery（控件示例与源码） | https://github.com/microsoft/WinUI-Gallery |
| Community Toolkit：SettingsCard / SettingsExpander | https://learn.microsoft.com/dotnet/communitytoolkit/windows/settingscontrols/settingscard ・ https://learn.microsoft.com/dotnet/communitytoolkit/windows/settingscontrols/settingsexpander |
| Community Toolkit：Segmented、GridSplitter | https://learn.microsoft.com/dotnet/communitytoolkit/windows/segmented ・ https://learn.microsoft.com/dotnet/communitytoolkit/windows/sizers/gridsplitter |
| PowerToys（设置界面、键盘管理器、命令面板） | https://github.com/microsoft/PowerToys ・ https://learn.microsoft.com/windows/powertoys/keyboard-manager ・ https://learn.microsoft.com/windows/powertoys/command-palette/overview |
| Microsoft Store（浏览、库与下载） | https://apps.microsoft.com/ |
| Dev Home（2025 年 5 月已停用，仅作历史参考：仪表盘小组件布局） | https://github.com/microsoft/devhome |

微软自家应用对应关系：Windows 11“设置”→ 设置页、更新页里的应用自更新；Microsoft Store → 在线页、更新页的下载列表、仪表盘；文件资源管理器（列表 + 详情窗格）→ Mod 工作区；PowerToys（设置卡片、键盘管理器、命令面板）→ 快捷键页、命令面板。

## 现有外观的共性问题

这些问题横跨所有页面，集中在 J1T 一次解决：

| 问题 | 位置 | Fluent 的做法 |
| --- | --- | --- |
| 窗口设了 Mica，但根 `Grid` 用不透明的 `AppPageBackgroundBrush`（浅色 `#F6F7F9`，深色 `#0D1117`）铺满，Mica 实际看不见 | `MainWindow.xaml:13,16`；`App.xaml:14,54` | 根背景透明，导航区直接显示 Mica；内容区用 `LayerFillColorDefaultBrush` 一层，卡片用 `CardBackgroundFillColorDefaultBrush` |
| 自定义了约 30 个 `App*Brush` 固定色值，强调色写死为 `#2563EB`，并覆盖了系统的 `AccentFillColor*`、`AccentButton*` | `App.xaml:13-79` | 用系统主题画刷（`TextFillColorSecondaryBrush`、`CardStrokeColorDefaultBrush`、`SystemFillColorSuccessBrush`/`CriticalBrush` 等），强调色跟随系统；高对比度主题自动可用 |
| 圆角 10–14 px（卡片 12、导航 14、按钮 10） | `App.xaml:98,106,114,199,209,219` | 页面内元素 `ControlCornerRadius`（4），顶层容器和浮层 `OverlayCornerRadius`（8） |
| 按钮最小高度 40、内边距 16–18 | `App.xaml:194-212` | 默认按钮约 32 高；只有主要操作或触控区域才放大 |
| 字号散落：18（`SectionTitleTextStyle`）、20/22（页标题）、15/16（卡片标题）、34（指标）；次要文字用 `Opacity="0.8"` 变淡 | `App.xaml:176-192`；`MainWindow.xaml:561,666,726,1005` 等 | 用系统字体层级：`TitleTextBlockStyle`（28）页标题、`SubtitleTextBlockStyle`（20）区块标题、`BodyStrongTextBlockStyle`（14 粗）卡片标题、`CaptionTextBlockStyle`（12）；次要文字用 `TextFillColorSecondaryBrush` 而不是透明度（透明度在 Mica 上对比度不稳定） |
| 状态药丸手画（`Border` + 软色背景），每处各写一遍 | `MainWindow.xaml:219-224,437-442,779-784` | 统一成一个 `StatusBadge` 样式，颜色取 `SystemFillColor*Background`/`SystemFillColor*` 画刷；或用 `InfoBadge` 表示数量 |
| 页面说明藏在 ⓘ 图标的提示里，键盘和讲述人用户看不到 | `Controls/PageHeader.xaml`；`MainWindow.xaml:1007-1017` | 说明放在标题下一行（`TextFillColorSecondaryBrush`），窄窗口时省略；或改为 `TeachingTip`/“了解更多”链接 |

## 外壳

### 现状

- 标题栏：自制 48 px `Grid`（图标、标题、版本号），`ExtendsContentIntoTitleBar` + `SetTitleBar`（`MainWindow.xaml:65-77`，`MainWindow.Shell.cs:814-830`）。
- 导航：自制按钮栏，`NavigationIconButtonStyle` 76×64（`MainWindow.xaml:86-152`），代码里按窗口宽度改宽高（`MainWindow.Shell.cs:254-271`）；搜索按钮、下载任务按钮（带自画徽标和进度条、右侧弹出 380 px 的任务浮层，`MainWindow.xaml:110-146`）、折叠按钮和设置按钮都是普通 `Button`。还有一个已不再显示的二级导航 `SecondaryNavBorder`（`:154-172`）。
- 状态栏：没有全局状态栏。状态文字只在工作区底部卡片（`WorkspaceStatusCard`，`:971-980`）；全局消息用底部居中的浮动 `InfoBar`（`:2133-2140`）。
- 命令面板：代码搭建的遮罩 + 640 px 面板（`MainWindow.CommandPalette.cs:67-110`），黑色 43% 遮罩、10 px 圆角，没有材质，结果项只有文字。
- 对话框：25 个文件里直接 `new ContentDialog`，都没设置 `DefaultContentDialogStyle`，所以用的是旧模板，不是 WinUI 3 的 Fluent 样式。

### 参考

- Windows 11“设置”、Microsoft Store、文件资源管理器：搜索框在标题栏中间，左侧 `NavigationView`，内容区是一层带圆角的面板放在 Mica 上。
- `NavigationView`：`PaneDisplayMode="Auto"`，`FooterMenuItems` 放下载和设置，`InfoBadge` 显示数量。
- `TitleBar` 控件（App SDK 1.7）：图标、标题、副标题、中间内容（搜索框）、返回和折叠按钮。
- PowerToys 命令面板：单行搜索框 + 带图标和快捷键提示的结果列表，Acrylic 浮层。

### 改动

1. 标题栏换成 `TitleBar` 控件：`Title`、`Subtitle`（版本号）、`Content` 放一个 `AutoSuggestBox`（占位“搜索 Mod、角色和命令 (Ctrl+K)”），点击或获得焦点就打开命令面板；`IsPaneToggleButtonVisible` 接 `NavigationView` 折叠。1000×800 下搜索框收成图标按钮。
2. 导航换成 `NavigationView`：`PaneDisplayMode="Auto"`，`ExpandedModeThresholdWidth="1440"`、`CompactModeThresholdWidth="641"`（保持现在 1440 才展开的行为），`OpenPaneLength="260"`（俄语“Горячие клавиши мода”需要约 230 px）；`IsSettingsVisible="False"`，设置作为自定义 `FooterMenuItems` 项以便沿用名称和自动化 ID。下载任务做成 `FooterMenuItems` 里的一项，`SelectsOnInvoked="False"`，图标上挂 `InfoBadge`（数值），原来的任务浮层挂在这一项上继续用；更新页的徽标同样换成 `InfoBadge`（`RefreshUpdatesNavBadge`）。删掉不用的 `SecondaryNavBorder`。
3. 内容区：`NavigationView` 内容外包一层 `Border`，`Background="{ThemeResource LayerFillColorDefaultBrush}"`、`BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}"`、左上圆角 8，与 Windows 设置一致。页面统一外边距：宽窗口 `36,24,36,24`，Compact 以下 `24,16,24,16`。
4. 全局消息：`AppInfoBar` 保留浮动位置，但规定 `Severity`：成功和普通信息 5 秒自动关闭，警告和错误不自动关闭并带“查看详情”按钮；需要持续存在的问题（未设置路径、权限不足）改为页面顶部的内联 `InfoBar`，不是浮动提示。
5. 命令面板：面板背景 `AcrylicInAppFillColorDefaultBrush`，圆角 `OverlayCornerRadius`，阴影用 `ThemeShadow`；遮罩改为 `SmokeFillColorDefaultBrush`（系统定义的遮罩色）；结果项加图标（页面/命令/角色/Mod 四类）、右侧显示快捷键或所属角色；分组标题（“页面”“命令”“Mod”）；空结果用 `EmptyState`。键盘：上下选、回车执行、Esc 关闭，打开时焦点在搜索框，关闭后回到原来的焦点而不是根 `Grid`（现在 `MainWindow.CommandPalette.cs:62` 固定回到 `RootGrid`）。
6. 键盘：导航项加 `AccessKey`（按 Alt 显示），页面切换加 `KeyboardAccelerator`（Ctrl+1…6），命令面板 Ctrl+K 已有。

### 阶段与工作量

新阶段 **J1N 外壳外观**，在 J1 之后（J1 只换宿主，不改外观），M。命令面板一项（第 5 条）可以放进 J1N 的第二个提交。

## 仪表盘（J4）

### 现状

`MainWindow.xaml:175-540`。一个 `ScrollViewer` 里依次是：`PageHeader` + “新建仓库”下拉；当前仓库条（44 px 图标、`ComboBox`、状态药丸、两个按钮，`:193-238`）；左栏“路径”卡片——三行路径输入框，固定列宽 132/*/112/172（`:271-314`），俄语下按钮文字放不下；“角色概览” `GridView`，项目固定 196×60（`:359-395`）；右栏“所有仓库”卡片，每个仓库是一个带三按钮的 `ItemsRepeater` 项，当前仓库用叠加的 2 px 描边表示（`:406-503`）；“快捷操作”三个等宽按钮（`:504-536`）。首次安装提示在 `MainWindow.FirstRun.cs`。

问题：当前仓库出现两次（顶部条 + 仓库列表）；路径是“设置”性质的内容却占了首页左上角；快捷操作是普通按钮，和其余按钮看不出主次；角色为空时只有一行灰字。

### 参考

- Windows 11“设置”首页：顶部是设备/账户的大卡片，下面是“推荐设置”和若干信息卡片。
- Microsoft Store 首页、Dev Home 仪表盘（历史参考）：大块卡片网格、每块卡片一个主题，卡片头部带标题和“查看全部”。

### 改动

1. 顶部改成**当前仓库主卡片**：左侧大图标或游戏封面（用户自己的仓库封面，不用游戏商标），名称（`TitleTextBlockStyle`）、状态 `StatusBadge`、计数（Caption）；右侧主按钮“打开 Mod 工作区”（`AccentButtonStyle`）和“运行启动器”；仓库切换用名称旁的 `DropDownButton`（列出仓库），去掉单独的 `ComboBox`。
2. **快捷操作**改成一排 `SettingsCard`（`IsClickEnabled="True"`，带图标、标题、一行说明、右侧箭头）：浏览在线 Mod、检查更新、导入 Mod。1000×800 下竖排。
3. **路径**改成 `SettingsExpander`“仓库路径”，默认折叠，标题右侧显示“3 项已设置/缺 1 项”；展开后每个路径一张 `SettingsCard`（`Header` 为名称，`Description` 为路径，`Content` 为“更改”“打开”两个按钮）。路径未设置时折叠项显示警告色并自动展开，同时在页面顶部给一个内联 `InfoBar`（Warning）“还没有设置 Mod 目录”+“设置”按钮。
4. **所有仓库**：列表项改成 `SettingsCard` 风格的行（名称、副标题、状态、`…` 菜单），当前仓库用 `ListView` 的选中态和强调色竖条，而不是叠加描边；“切换”“管理”合并为点击行切换、`…` 菜单里管理。
5. **角色概览**：改为 `ItemsView` + `UniformGridLayout`（`MinItemWidth="180"`，自适应列数），空状态用 `EmptyState`（图标、“还没有角色文件夹”、“打开 Mod 工作区”按钮）。
6. **首次安装**：现在的弹窗改为页面内的“开始使用”卡片（三步：选择仓库预设 → 设置路径 → 导入 Mod），完成一步打勾；后续提示用 `TeachingTip` 指向对应按钮，而不是连续弹窗。
7. 布局：1400 宽时两栏（主卡片通栏，下面左“角色概览”右“仓库列表”）；1000 宽时单栏。

### 阶段与工作量

J4 的移动提交之后，外观放在同一 PR 的单独提交（S–M）。首次安装改成卡片 + `TeachingTip`（第 6 条）牵涉 `FirstRun` 逻辑，可单独作为 J4-b，S。

### J4-b 的做法（2026-11-09）

- **首次安装弹窗留着，改成卡片式。** 语言要在界面出现前定好，游戏要在仓库写入前定好，所以仍然是一个对话框（`DefaultContentDialogStyle`），只是每个游戏是一张卡片（`RadioButtons`，两列，卡片颜色取主题令牌，悬停和按下不变透明），下面一条“接下来”说明路径和 Mod 管理在哪里。没有做第 6 条里页面内带打勾的三步卡片：三步里只有“选游戏”在首次安装时决定，其余两步由提示指出，打勾需要另外判断“路径已设好”“已导入”，留作以后再定。
- **`TeachingTip` 引导：** 一个提示依次指向“新建仓库”按钮、路径折叠项和“进入 Mod 管理”，带“第 1 步，共 3 步”、“跳过引导”和“下一步/完成”。打开时焦点移到“下一步”，按钮的帮助文字是步骤标题和说明，并发出一次讲述人通知；Esc、“跳过引导”、提示右上角的 ✕ 和最后一步的“完成”都算关闭。
- **判断逻辑（新增，`Core/Configuration/GettingStartedTips.cs`，有单元测试）：** 只有首次安装才开始引导（`beta-shell.json` 新字段 `GettingStartedTipsPending`，旧配置没有这个字段，所以升级用户不会看到）；关闭或完成后写回 `false`，不再出现；没关闭就退出程序，下次启动还会从第 1 步出现。引导在“新功能”等启动对话框关闭后才交给仪表盘，只在仪表盘显示，换到别的页面时收起，回来再显示。
- **不变的部分：** 首次安装的判断（`config.ini` 和 `beta-shell.json` 都不存在）、语言跟随 Windows、选游戏写入的仓库字段、“稍后设置”时仓库不带游戏，这些代码没有改。首次安装本来就没有 XXMI 启动设置这一步，也没有加。
- 截图脚本新增页面 `gettingstarted`（带引导的仪表盘，不在默认页面里）。

## Mod 工作区（J7）

### 现状

`MainWindow.xaml:543-983`。顶部工具卡片（`CommandSurfaceBorderStyle`，22 px 标题 + 仓库 `ComboBox` + 刷新/导入/运行三个 36 px 按钮，第二行视图按钮由代码加入，`:549-588`）；三栏工作台：角色卡片（标题、加号和 `…` 按钮、搜索框、`ListView`，`:652-710`）、Mod 卡片（标题、计数、删除按钮、状态筛选芯片、列表或封面视图，`:712-793`）、详情区（选中摘要卡片、`TabView` 三个标签“预览/快捷键/状态槽位”，底部“部署”大按钮，`:794-969`）；最底下状态卡片（`:971-980`）。另有三块折叠的“隐藏容器”（`:590-640`）只为保留控件。栏宽由 `RefinedWorkspace.cs` 在代码里分配。

问题：`TabView` 是文档标签控件（可关闭、可拖动），这里只当分段切换用；预览区固定高 320（`:834`），1000×800 下挤掉下面的链接和按钮；“部署”这个最重要的操作在右下角，和删除、打开文件夹按钮距离很远；删除按钮在 Mod 列表头部，没有确认之外的保护；三张卡片各自 12 px 内边距加 12 px 间距，视觉上像三个独立窗口；空状态和拖放提示是代码叠加的。

### 参考

- 文件资源管理器：顶部命令栏（新建、操作、排序、视图）、左侧导航树、中间列表（详细信息/图标视图切换）、右侧详情窗格，三者在同一平面上，用细分隔线而不是卡片区分。
- 列表/详情模式文档：宽窗口并排，窄窗口详情覆盖或下钻。
- 「工作台新布局」设计画布（https://claude.ai/artifact/JmPxxjLDtP1SLx2YZUPWZJ）：宽屏有内容、半屏详情抽屉、空状态与首次引导、全局任务与 Ctrl+K 四张稿，本节沿用它的分区。

### 改动

1. 顶部工具卡片换成 `CommandBar`：左边页面标题和仓库切换（同仪表盘的 `DropDownButton`），右边 `AppBarButton`：导入、刷新、运行启动器；视图切换（列表/封面）用 Toolkit `Segmented`（2 项，图标）；不常用命令进 `SecondaryCommands`（`…`）。计数文字移到 Mod 列标题旁。
2. 三栏改为同一平面：去掉三张卡片的边框和各自背景，用 `GridSplitter`（Toolkit）分隔，列宽可拖动，保存在配置里并在下次打开时恢复；角色栏 `MinWidth=200`，详情栏 `MinWidth=320`。
3. 角色栏：顶部 `AutoSuggestBox` 搜索（带清除按钮和 Ctrl+F），列表项高度 40，头像 24；加号和 `…` 放进栏标题行的小 `AppBarButton`。
4. Mod 列表：详细信息视图用 `ListView`，列为缩略图、名称（含标签）、大小、状态，表头可点击排序；封面视图用 `ItemsView` + `UniformGridLayout`；多选时 `CommandBar` 出现批量操作（部署、移除、加标签）——与文件资源管理器的选择行为一致。删除移到右键菜单和 `CommandBar`，用 `DangerButton` 样式加确认对话框。状态筛选芯片用 Toolkit `Segmented`（全部/已部署/未部署）。
5. 详情窗格：顶部是名称（`SubtitleTextBlockStyle`）、状态徽标和**主操作“部署/移除”**（`AccentButtonStyle`），紧接着“打开文件夹”和 `…`；`TabView` 换成 `SelectorBar`（预览/快捷键/状态槽位）；预览图高度按可用高度自适应（`MaxHeight` 320，最小 160），链接一行放在预览下方。状态槽位区里的 8 个控件（`:917-945`）按“当前文件/槽位选择/操作”分成三张 `SettingsCard`。
6. 1000×800：详情窗格按画布“半屏 · 详情抽屉”收成右侧覆盖抽屉（选中 Mod 时滑出，Esc 关闭），角色栏和 Mod 列表各占一半。
7. 空状态：仓库为空、角色为空、筛选无结果三种都用 `EmptyState`，带对应主操作（导入、新建角色、清除筛选）；拖放时整个 Mod 列表区显示虚线描边和“松开以导入”文字。
8. 状态卡片（`:971-980`）去掉，消息统一走外壳的 `InfoBar`；进度用 `CommandBar` 下方一条不确定 `ProgressBar`。
9. 键盘：角色列表 → Mod 列表 → 详情窗格之间 F6 循环；Delete 删除（带确认），Enter 部署/移除，Ctrl+I 导入；所有图标按钮有 `AutomationProperties.Name` 和工具提示。

### 阶段与工作量

J7 本身是 L，外观改动也是 L，**必须单独开 J7-b PR**，在 J7 合并之后。J7-b 可再按“命令栏与三栏平面（1–4）”和“详情窗格与窄窗口（5–9）”拆两个提交。

## 在线（J6）

### 现状

`MainWindow.xaml:985-1462`。整页是一张大卡片：标题（20 px）+ ⓘ 提示 + 状态药丸 + 缓存状态 + 下载进度 + 列表/网格切换（两个自绘 36×32 按钮，`:1053-1072`）+ 刷新；左侧 188 px 角色栏（`:1089-1136`，头像列表由代码生成）；右侧工具行（搜索、排序 `ComboBox` 176 px、筛选/清除/收藏/下载选项按钮，`:1151-1180`）；结果 `ListView` 或 `GridView`（瓦片 300×307，`:1251`）；加载遮罩是 `ProgressRing` 加三条假文字条（`:1262-1277`）；详情是浮在右侧的 600 px 抽屉，左边一条手画渐变阴影（`:1278-1422`），含 360 px 主图、缩略图条、说明和底部三个按钮；页脚是筛选摘要和上一页/下一页（`:1424-1456`）。

问题：顶部一行挤了 5–6 种信息，1000 宽时放不下；抽屉固定 600 px，在 880 px 内容区里几乎盖满结果；阴影手画，深色主题下发灰；加载、空结果、网络错误只靠一行状态文字区分；角色栏只能滚动，不能搜索。

### 参考

- Microsoft Store：顶部搜索框，结果页上方是一排筛选按钮/下拉（类型、类别、价格），结果是统一尺寸的卡片网格；详情是单独的页面，顶部大图和主按钮“获取”，下方截图条和说明；返回用返回按钮。
- `ItemsView` + `UniformGridLayout`、`BreadcrumbBar`（“在线 › 角色 › Mod”）、`ProgressRing`、`InfoBar`。

### 改动

1. 顶部：标题 + `AutoSuggestBox`（搜索，带最近搜索建议）+ 右侧 `CommandBar`（刷新、收藏、下载选项、视图 `Segmented`）。状态药丸、缓存状态移到页脚左侧的一行 Caption；下载进度只在外壳的下载导航项显示（`InfoBadge` + 浮层），页面里不再重复。
2. 筛选：搜索下一行是筛选条——排序 `ComboBox`、“作品筛选” `DropDownButton`（勾选项）、已选筛选以可移除的标签（`Button` + ✕）显示、“清除全部”文字按钮。这一行可换行，俄语下不截断。
3. 角色栏：顶部加一个小搜索框；项目改为 `ListView`（头像 28 + 名称 + 数量），选中态用系统 `ListViewItem` 样式；1000 宽时收成顶部的角色 `ComboBox`（代码里已有 `OnlineCharacterComboBox`，`:1162-1167`）。
4. 结果：网格视图换 `ItemsView` + `UniformGridLayout`（`MinItemWidth=260`、`MinColumnSpacing=12`），瓦片圆角 8、封面 16:9、标题两行省略、作者和更新时间 Caption；列表视图项高 72。键盘：方向键移动，Enter 打开详情，空格加入批量下载。
5. 详情（已决定：保留抽屉，即原方案 A）：宽度改为内容区的 45%（最小 400、最大 600），用 `ThemeShadow` 和 `Translation` 取代手画渐变，背景 `AcrylicInAppFillColorDefaultBrush`；1000 宽时抽屉占满内容区并显示返回按钮。保持现在“不离开结果页”的行为，上一个/下一个按钮保留。不做 Store 式的独立详情页。底部操作区改成：主按钮“下载并安装”（`AccentButtonStyle`）、次按钮“选择文件”、超链接按钮“在网页中打开”。
6. 状态：加载用 `ProgressRing` + 与瓦片同尺寸的占位卡片（去掉三条假文字）；无结果用 `EmptyState`（“没有找到 Mod”+“清除筛选”）；网络或接口错误用结果区顶部的内联 `InfoBar`（Error，带“重试”）；离线缓存模式用 `InfoBar`（Informational）“正在显示缓存内容”。
7. 分页：保留上一页/下一页（接口按页取），中间显示“第 3 / 12 页”；按钮最小宽度按俄语“Назад/Далее”定为 96。

### 阶段与工作量

J6 本身是 L，外观改动 M–L，**单独开 J6-b PR**。

## 快捷键（J5）

### 现状

整页在代码里搭建（`MainWindow.xaml:1464-1466`，`MainWindow.ShortcutPanel.cs:132` 起）。`PageHeader` + “刷新窗口”“重新读取 INI”按钮；工具卡片：Mod `ComboBox`、目标窗口 `ComboBox`、“允许向所选窗口发送按键”开关、权限提示文字 + “以管理员身份重启”按钮（`:153-198`）；绑定卡片：标题、“隐藏无法发送的绑定”复选框、只读提示、状态文字、按键按钮网格（`:204-239`）；右侧设置栏：浮窗热键（两个 `ComboBox`）、角色跟随、输入方式、按下/按住时长、浮窗外观（`:242-360`），每组是一个 `Expander`（`:399`）。

问题：权限不足是一条 Caption 文字，很容易错过；所有设置项是“`ComboBox` 带 `Header`”的表单写法，和设置页风格不一致；按键显示是普通按钮文字，看不出是“键”；页面全部手写 `Grid`/`StackPanel`，没有可复用的样式。

### 参考

- PowerToys 键盘管理器和设置页：每个选项一张 `SettingsCard`，相关选项收在 `SettingsExpander`；按键用带边框的键帽样式显示（“Ctrl”“Shift”“F5”分开画）；需要管理员权限时页面顶部是 `InfoBar`。
- `InfoBar`、`SettingsCard`/`SettingsExpander`、`ToggleSwitch`。

### 改动

1. 页面顶部：权限不足时显示内联 `InfoBar`（Warning，“游戏以管理员身份运行时需要……”，操作按钮“以管理员身份重启”），替换 Caption + 按钮。
2. “发送目标”区改为一组 `SettingsCard`：Mod（`Content` 为 `ComboBox`）、目标窗口（`ComboBox` + 刷新 `AppBarButton`）、“允许发送按键”（`ToggleSwitch`，开启时卡片左侧显示强调色状态点）。1000 宽时 `SettingsCard` 自动把控件换到下一行（`SettingsCard` 本身支持）。
3. 绑定列表：每个绑定一行——左侧名称和说明，右侧键帽（新建一个小控件 `KeyVisual`：`Border`，`ControlCornerRadius`，`ControlFillColorSecondaryBrush`，`CardStrokeColorDefaultBrush`，每个键一个），再右是“发送”按钮；不可发送的绑定变灰并带原因提示。“隐藏无法发送的绑定”改为列表标题行的 `ToggleSwitch`；“打开 INI”放进标题行 `…` 菜单。
4. 右侧设置栏全部改为 `SettingsExpander`：“浮窗”（热键、透明度、外观预设）、“角色跟随”、“输入方式”（输入方式、按下时长、按住时长，时长用 `NumberBox`）。1400 宽时右栏 360 px，1000 宽时移到绑定列表下方。
5. 键盘：每个绑定行可 Tab 到“发送”按钮；热键录制框聚焦时显示“按下组合键，Esc 取消”。
6. 浮窗（`ShortcutOverlayWindow`）不在本方案范围，只要求它使用 J1T 的同一组令牌。

### 阶段与工作量

J5 移动后的单独提交，M。`KeyVisual` 控件可先在 J5 里建，后续工作区的“快捷键”标签也复用。

## 更新（J3）

### 现状

`MainWindow.xaml:1467-1712`。`PageHeader` 下两到三栏（`UpdatesTopGrid`，1.35* / 0.85* / 0，宽时第三栏，`:1471-1480`，由 `UpdatesLayoutPolicy` 决定）：左栏“跟踪的 Mod”卡片（40 px 图标、标题、摘要、“检查更新”主按钮、检查间隔、筛选按钮、搜索框、列表，`:1483-1562`）；右栏依次是“下载记录”“安装安全”（冲突检测开关、回滚按钮）、“配置方案”（`ComboBox`、复选框、只读路径框、四个按钮）、“备份”（`NumberBox`、刷新、`Expander` 列表）四张卡片（`:1574-1707`）。

问题：一个页面放了五件事，其中“配置方案”“备份”“冲突检测”是设置性质；右栏单独滚动、左栏列表也单独滚动，1000×800 下出现嵌套滚动；“配置方案”一行四个按钮，俄语下放不下；空列表用代码加的 `EmptyState`。

### 参考

- Windows 11“设置 › Windows 更新”：顶部状态卡片（图标、“你已是最新状态/有可用更新”、上次检查时间、主按钮“检查更新”），下面是一组 `SettingsCard`（暂停更新、高级选项、更新历史）。
- Microsoft Store“库”：“获取更新”主按钮，下面是应用列表，每行图标、名称、状态、进度和操作；顶部有类型筛选。
- `SelectorBar` 做页内分区。

### 改动

1. 页面顶部状态区（仿 Windows 更新的结构，不用其图标）：图标、“有 3 个 Mod 可更新 / 都是最新”、“上次检查：10 分钟前”Caption、主按钮“检查更新”；检查中显示不确定 `ProgressBar`。
2. 下面用 `SelectorBar` 分三个视图：**Mod 更新**（默认）、**下载记录**、**安装与恢复**。去掉多栏和嵌套滚动，每个视图一个滚动区。
   - Mod 更新：筛选 `Segmented`（全部/有更新/出错）+ 搜索框；列表行仿 Store 库：缩略图、名称、当前 → 新版本、状态、“更新”按钮；多个可更新时列表顶部有“全部更新”。检查间隔改为列表下方的一张 `SettingsCard`（它只影响 Mod 更新，留在本视图，不移到设置页）。
   - 下载记录：同样的行样式，带进度、失败原因和“重试/打开文件夹”，“清除”放在视图右上角。
   - 安装与恢复：`SettingsCard`“冲突检测”（开关）、`SettingsCard`“撤销上次安装”（按钮）、`SettingsExpander`“配置方案”（`ComboBox` 在标题行，展开后是保存/更新/删除与“包含组合状态”）、`SettingsExpander`“备份”（上限 `NumberBox` 在标题行，展开后是备份列表）。
3. 空状态：Mod 更新“还没有跟踪的 Mod”+“浏览在线 Mod”；下载记录“没有下载记录”。
4. 1000×800：单栏本来就是新布局的默认，不需要专门处理；`SelectorBar` 三项俄语总宽约 420 px，放得下。

### 阶段与工作量

M。结构变化大（多栏 → 分视图），**单独开 J3-b PR**，在 J3 移动合并之后。

## 设置（J2）

### 现状

`MainWindow.xaml:1714-2128`。`PageHeader` 下是 5 个自制分区按钮（`SettingsSectionButton0`–`4`，`:1726-1774`）+ 一个卡片区；宽窗口时按钮隐藏，卡片排成两栏总览（`SettingsOverviewGrid`，`:1777-1784`，由 `SettingsLayoutPolicy` 决定）。卡片：外观（5 个自制 `SettingRow`，`:1786-1831`）、应用更新（开关、间隔、三个等宽按钮、进度、发行说明，`:1833-1919`）、在线信息（两列只读表格 + 两个按钮，`:1921-1959`）、诊断与反馈（`:1961-2005`）、项目与开源组件（7 个 54 px 高的链接按钮，`:2007-2122`）。

问题：拆分计划已经规定 `SettingRow` 换 `SettingsCard`；此外分区按钮和两栏总览是两套导航方式；应用更新卡片里控件排列方式和其他卡片不同；开源组件做成了大按钮网格；说明文字两行截断再靠提示补全。

### 参考

- Windows 11“设置”和 PowerToys“常规”页：单列、最大宽度约 1000 px，分组标题（`BodyStrongTextBlockStyle`，上方 30 px 间距），组内 `SettingsCard` 间距 4 px；“关于”是一个 `SettingsExpander`，展开后是版本、链接和许可。WinUI 3 Gallery 的设置页就是这一结构，可直接对照源码。
- 应用设置指南：常用设置在前，“关于”放最后。

### 改动

1. 去掉分区按钮和两栏总览，改为**单列长页面**：`MaxWidth="1000"`，左对齐，分组标题“外观”“应用更新”“在线 Mod”“诊断与反馈”“关于”。5 个分组都不长，单列滚动比分区切换更快；如需跳转，用命令面板（已能打开设置页）加分组锚点。不保留分区导航（已决定）。
2. 外观：语言、主题、密度（`ComboBox`）、减少动画、最小化到托盘（`ToggleSwitch`）各一张 `SettingsCard`，带 `HeaderIcon`（现有字形）。改语言后显示内联 `InfoBar`“部分文字重启后生效”（如适用）。
3. 应用更新：仿“Windows 更新”的状态卡片（当前版本、最新版本、主按钮“检查更新/下载并安装”、进度），下面 `SettingsCard`“启动时检查更新”（开关）、`SettingsCard`“检查间隔”（`ComboBox`）、`SettingsExpander`“发行说明”（展开显示说明文字）、`SettingsCard`“在 GitHub 查看”（`IsClickEnabled`，外链图标）。
4. 在线 Mod：`SettingsExpander`“仓库在线信息”，展开后每项一行只读 `SettingsCard`（可选择文字）；“编辑仓库信息”“编辑角色映射”两张可点击 `SettingsCard`。
5. 诊断与反馈：“导出诊断包”“提交诊断”两张 `SettingsCard`（各带按钮）；QQ 群与 Discord 两张 `SettingsCard`（复制/打开）。
6. 关于：`SettingsExpander`“关于 Integrated Mod Manager”，标题行显示版本号；展开后是开源组件列表，每项一行 `SettingsCard`（名称 + 链接 `HyperlinkButton`），替换 7 个大按钮。
7. 说明文字不再强行两行截断：`SettingsCard.Description` 本身换行，1000 宽时控件自动移到下一行。

### 阶段与工作量

J2 已规定 `SettingRow` → `SettingsCard`（属于拆分计划的“有意改动”）；本节其余部分（第 1、3–7 条）在 J2 移动提交之后另起一个提交，S–M，同一 PR。

### 宽窗口信息栏（方案 C，2026-11-04）

用户反馈“设置界面不要在大窗口的时候留太多空白”，选了方案 C：宽窗口时主栏 + 右侧信息栏，窄窗口时信息栏移到主栏下面。这取代第 1 条里“单列、`MaxWidth="1000"`”的部分；分组和卡片不变。

- **分组：** 主栏放“要调的”设置：外观与语言，以及新分组“更新设置”（启动时检查、检查间隔、发行说明；发行说明是长文，需要宽度）。信息栏放“主要是看的”内容：应用更新状态（版本、检查/安装按钮、进度、在 GitHub 查看）、仓库在线信息（展开项和两张编辑卡片放在一起）、诊断与反馈、关于（项目和开源组件）。窄窗口时顺序就是主栏再信息栏，和宽窗口的阅读顺序、Tab 顺序一致。
- **断点：** 看设置页自己的宽度（`SettingsScrollViewer.SizeChanged`），不看窗口宽度，因为导航栏会占掉一部分。页宽 ≥ 1180 DIP 时两栏：信息栏最小 360，间距 24，主栏约 800 时俄语卡片标题和右侧控件仍在一行。1000×800 窗口的页宽不到 1000，是单栏；1400×900 的页宽约 1300，是两栏（俄语截图核对过没有截断）。
- **最大化：** 两栏按比例变宽，不居中：主栏 `*`、信息栏 `0.42*` 并限制在 360–520 DIP。居中会在两侧留下空白，正是用户不想要的；信息栏设上限是因为它的卡片内容短，太宽反而难读。
- **页眉：** `PageHeader` 跨两栏，左右边和下面的内容对齐。
- **更新状态卡片：** 信息栏里状态文字和“检查更新”“检查并获取更新”两个按钮放不进一行（俄语、英语都会截断），所以宽布局时按钮在状态下面、一行一个；窄布局恢复成一行。这部分在 `SettingsPage.xaml.cs` 里切换，因为 `VisualState` 的 setter 不能转换工具包的 `ContentAlignment`（会让应用崩溃）。
- 截图脚本 `Capture-UiBaseline.ps1` 新增尺寸 `maximized`（`-Sizes maximized`），用于检查最大化时的样子。

## 对话框（J8）

### 现状

25 个文件直接 `new ContentDialog`（`MainWindow.Dialogs.cs` 等），均未设置 `Style = DefaultContentDialogStyle`，因此显示的是旧样式；有 26 处设置了 `DefaultButton`，其余没有；删除类确认的主按钮和普通确认外观相同。

### 参考

`ContentDialog` 指南：标题直接说明问题或操作；按钮用动词（“删除”“取消”），不用“是/否”；设置默认按钮；破坏性操作不设为默认。

### 改动

1. `DialogService` 统一设置 `XamlRoot`、`Style = DefaultContentDialogStyle`、`DefaultButton`；破坏性确认的默认按钮是“取消”。
2. 按钮文字逐个改成动词（语言文件的键只增不改名，按拆分计划规则）。
3. 内容超过两段的对话框（例如批量导入结果）内容区用 `ScrollViewer`，最大高度 400。
4. 能用页面内 `InfoBar` 或 `TeachingTip` 表达的提示，不再用对话框。

### 阶段与工作量

J8 的对话框类拆出之后单独提交，S。

## 新增阶段与顺序

| 阶段 | 内容 | 工作量 | 依赖 | 提交方式 |
| --- | --- | --- | --- | --- |
| J0 | 额外：基线截图加深色主题，并固定截图用的壁纸；确认 `CommunityToolkit.WinUI.Controls.Segmented`、`Sizers` 也能与 App SDK 1.7 一起打包 | +S | — | 原 J0 内 |
| J1 | 不改外观 | — | J0 | 原 J1 |
| **J1T 设计令牌** | 新建 `Themes/Tokens.xaml`（相当于把 J8 的资源拆分提前一部分）：`App*Brush` 改为引用系统画刷，根背景透明露出 Mica，内容层 `LayerFillColorDefaultBrush`，圆角 4/8，按钮高度回到默认，标题/正文/说明改用系统字体样式，统一 `StatusBadge` 样式；强调色跟随系统，删除品牌蓝；做完用固定壁纸重拍基线 | S–M | J1 | 新 PR |
| **J1N 外壳外观** | `TitleBar` + 标题栏搜索框、`NavigationView` + `InfoBadge`、内容层、`InfoBar` 规则、命令面板材质与结果项 | M | J1T | 新 PR |
| J2 | 移动 + `SettingsCard`（原计划）+ 单列设置页 | S–M | J1T | 同 PR，单独提交 |
| J3 / J3-b | J3 移动；J3-b 状态区 + `SelectorBar` 三视图 | M / M | J1T | J3-b 新 PR |
| J4 / J4-b | J4 移动 + 主卡片、快捷操作、路径折叠；J4-b 首次安装卡片 + `TeachingTip` | S–M / S | J1T | 外观同 PR 单独提交；J4-b 新 PR |
| J5 | 移动 + `InfoBar`、`SettingsCard`、`KeyVisual` | M | J1T | 同 PR，单独提交 |
| J6 / J6-b | J6 移动；J6-b 搜索与筛选、`ItemsView`、详情抽屉、状态 | L / M–L | J1T | J6-b 新 PR |
| J7 / J7-b | J7 移动；J7-b `CommandBar`、三栏平面 + `GridSplitter`、详情窗格、窄窗口抽屉 | L / L | J1T | J7-b 新 PR |
| J8 | 对话框类 + 统一样式与按钮文字 | S–M | 可穿插 | 同 PR，单独提交 |

合计在原拆分计划之外增加约 4–6 周。J1T 和 J1N 之后，任何一页的外观提交都可以独立发布。

## 完成标准

每个外观提交或 `-b` PR：

- 只用系统主题画刷和系统字体样式，不新增固定色值；强调色跟随系统，不再定义品牌蓝。
- 中英俄三种语言 × 1400×900 / 1000×800 × 深浅主题，共 12 张截图（固定壁纸）；PR 里逐张说明与基线的差异。俄语下没有被截断且没有提示的按钮或标签。
- 高对比度主题下能看清所有状态（截一张检查）。
- 所有图标按钮有 `AutomationProperties.Name`；Tab 顺序与视觉顺序一致；用讲述人走一遍这一页的主要操作。
- 界面测试通过；新增或替换控件保留原自动化 ID。

## 已决定（2026-11-04）

维护者 2026-11-04 确认以下 9 项都按推荐执行，正文已按这些结论写定：

1. **强调色**：跟随系统强调色，去掉品牌蓝 `#2563EB`（J1T 删除 `App.xaml` 里覆盖 `AccentFillColor*`、`AccentButton*` 的定义）。
2. **Mica**：露出 Mica，去掉不透明页面底色；截图对比用固定壁纸（J0 记录所用壁纸，之后每次截图都用它）。
3. **外观提交方式**：S–M 的外观改动放在同一个 PR 里单独提交，M 以上单独开 `-b` PR（这里“M 以上”指 M–L 和 L，即 J6-b、J7-b；J5 的外观为 M，留在 J5 的 PR 内）。J3-b（多栏改分视图，需要单独评审）和 J4-b（牵涉首次安装逻辑）按内容单独成 PR。
4. **J1T 的位置**：放在 J1 之后、J2 之前。
5. **导航**：换成 `NavigationView`，用 `AutomationProperties.AutomationId` 保留原来的控件名称（`DashboardNavButton` 等），界面测试照旧能定位。
6. **设置页**：单列长页面，不保留分区导航。
7. **更新页**：用 `SelectorBar` 分 Mod 更新、下载记录、安装与恢复三个视图。
8. **在线详情**：保留右侧抽屉（原方案 A），不做独立详情页。
9. **工作区列宽**：用 `GridSplitter` 拖动，列宽保存在配置里，下次打开恢复。

## 不在范围内

- 换界面引擎；换控件库（只用 App SDK 自带控件和 Windows Community Toolkit）。
- 游戏内浮窗（`ShortcutOverlayWindow`）的重新设计，只要求共用 J1T 令牌。
- 新功能。本方案只改布局、样式和状态呈现，不增删功能；“全部更新”“表头排序”这类利用现有逻辑的小入口除外，若需要新的后端逻辑则另立任务。
- 使用微软应用的图标、插画或品牌元素。

## English summary

This is the companion to the [UI structure plan](./UI-Structure-Plan.md) (stage J): that plan says how the `MainWindow` is split into pages without changing the look; this one says how each page's look is improved afterwards, following Microsoft's published Fluent guidance, WinUI 3 Gallery controls, Windows Community Toolkit controls and patterns from Windows 11 Settings, Microsoft Store, File Explorer and PowerToys (Dev Home, retired in May 2025, is a historical reference only). It stays on WinUI 3 and uses no Microsoft brand assets. Rules: pure moves and appearance changes are separate commits; S–M appearance work stays in the same PR as its own commit, larger work gets its own `-b` PR; every change is checked in Chinese, English and Russian at 1400×900 and 1000×800 (both show only the icon rail today, since the shell expands navigation at 1440), light and dark; automation IDs are kept. Cross-cutting problems — an opaque page brush that hides the Mica backdrop, about 30 fixed `App*Brush` colours with a hard-coded `#2563EB` accent overriding the system accent, 10–14 px corners, 40 px buttons, ad-hoc font sizes and opacity-based secondary text, hand-drawn status pills, descriptions hidden in tooltips — are fixed once in a new stage **J1T (design tokens)**, after which baselines are retaken. A new stage **J1N (shell)** moves to the 1.7 `TitleBar` with a search box, `NavigationView` with `InfoBadge` for downloads and updates, a content layer over Mica, rules for `InfoBar` severities and an Acrylic command palette with icons and grouped results. Per page: **Settings (J2)** becomes a single 1000 px column of `SettingsCard`/`SettingsExpander` groups like Windows Settings, with an update status card and an About expander; **Updates (J3-b)** gets a Windows-Update-style status header and a `SelectorBar` with Mod updates, download history and install & recovery views instead of nested scrolling columns; **Dashboard (J4)** gets one current-repository hero card, clickable quick-action cards, collapsed path settings with a warning `InfoBar`, an adaptive characters grid, and (J4-b) a getting-started card with `TeachingTip`s instead of first-run dialogs; **Shortcuts (J5)** gets a privilege `InfoBar`, `SettingsCard` targets, key-cap visuals and `SettingsExpander` options like PowerToys; **Online (J6-b)** gets Store-style search, removable filter chips, an `ItemsView` grid, skeleton loading, empty and error `InfoBar` states, and a resized details drawer with a system shadow (no separate detail page); **Mod workspace (J7-b)** follows File Explorer and the existing workbench canvas with a `CommandBar`, three flat panes split by `GridSplitter`, a details pane with Deploy as the primary action and a `SelectorBar` instead of `TabView`, and a drawer at 1000 px; **Dialogs (J8)** use `DefaultContentDialogStyle`, verb buttons and safe defaults. This adds about 4–6 weeks. Decided on 2026-11-04 (all as recommended): follow the system accent and drop the brand blue; expose Mica and take comparison screenshots on a fixed wallpaper; S–M appearance work as separate commits in the same PR, anything larger in its own `-b` PR (J3-b and J4-b are split by content); J1T comes after J1 and before J2; navigation moves to `NavigationView`, keeping the old names as automation IDs; Settings is a single long page (on 2026-11-04 the user chose option C: on a page at least 1180 DIP wide the settings you change stay in the main column and a 360–520 DIP rail beside it holds update status, the repository's online information, diagnostics and About; both columns widen in proportion when maximized, and on a narrow page the rail follows the main column); Updates uses a `SelectorBar` with three views; Online keeps the details drawer; workspace columns are resizable with `GridSplitter` and remembered.
