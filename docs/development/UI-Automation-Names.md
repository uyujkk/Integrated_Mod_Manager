# 界面测试依赖的自动化名称 / UI automation names

2026-11-04，阶段 J0 第 2 项，基于 `f6c2800`。列出 `tests/UI.Tests`（`SmokeTests.cs`、`AppSession.cs`）定位界面元素用到的全部名称。阶段 J 拆分页面、换控件时，这些名称必须保持不变；确实要改的，在同一个 PR 里同时改测试并在 PR 说明里列出。见[界面结构拆分计划](./UI-Structure-Plan.md)和[界面外观优化方案](./UI-Fluent-Design-Plan.md)。

测试用三种方式找元素：

- `Find(id)` / `Shows(id)` / `ByAutomationId`：按 UI 自动化的 AutomationId。WinUI 3 里 `x:Name` 默认就是 AutomationId；代码里建的控件用 `AutomationProperties.SetAutomationId` 设置。**把控件移到别的页面或换成别的控件时，`x:Name` 一变 AutomationId 就跟着变**，所以要么保留 `x:Name`，要么显式设置 `AutomationProperties.AutomationId`。
- `FindByName(text)`：按可见文字（语言文件里的键，测试从 `Strings/*.json` 读）或测试数据里的名字。
- `ByClassName`：按控件类型名。

合计：**39 个 AutomationId**（其中 1 个是按前缀拼出的一组）、6 个语言键加窗口标题、3 个测试数据名字、1 个控件类名、1 个窗口标题前缀。

## AutomationId

### 外壳导航（外壳；J1N 换 `NavigationView` 时受影响）

| AutomationId | 来源 | 定义位置 | 用到的测试 |
| --- | --- | --- | --- |
| `DashboardNavButton` | `x:Name` | `MainWindow.xaml:101` | Navigation_EveryPageOpensWithoutErrors |
| `RepositoryNavButton` | `x:Name` | `MainWindow.xaml:102` | Navigation_EveryPageOpensWithoutErrors |
| `OnlineNavButton` | `x:Name` | `MainWindow.xaml:103` | Navigation_EveryPageOpensWithoutErrors |
| `UpdatesNavButton` | `x:Name` | `MainWindow.xaml:104` | Navigation_EveryPageOpensWithoutErrors |
| `ShortcutsNavButton` | `x:Name` | `MainWindow.xaml:105` | Navigation_EveryPageOpensWithoutErrors、Shortcuts_SummaryShowsTheSavedHotkey、Shortcuts_InputMethodIsSaved |
| `SettingsNavButton` | `x:Name` | `MainWindow.xaml:150` | Navigation_EveryPageOpensWithoutErrors |

J1N：每个 `NavigationViewItem` 设 `AutomationProperties.AutomationId` 为上面的原名（已决定，见设计方案“已决定”第 5 项）。测试用 `Invoke` 打开页面，`NavigationViewItem` 支持的是 `SelectionItem` 模式，`AppSession.Invoke` 已经会退到 `Select()`，不用改测试。

### 仪表盘（J4）

| AutomationId | 来源 | 定义位置 | 用到的测试 |
| --- | --- | --- | --- |
| `SourceTextBox` | `x:Name` | `MainWindow.xaml:280` | Dashboard_ShowsTheRepositoryPaths（读取值，应以 `ModRepository` 结尾） |
| `TargetTextBox` | `x:Name` | `MainWindow.xaml:295` | Dashboard_ShowsTheRepositoryPaths |

注意：J4 外观改动把路径收进默认折叠的 `SettingsExpander`。折叠的内容不在自动化树里，测试会失败；路径未设置时才自动展开。J4 需要在测试里先展开“仓库路径”，或让 `SettingsCard` 把路径同时显示在标题行（外观方案第 3 条）。

### Mod 工作区（J7 / J7-b）

| AutomationId | 来源 | 定义位置 | 用到的测试 |
| --- | --- | --- | --- |
| `RefreshButton` | `x:Name` | `MainWindow.xaml:571` | Repository_ShowsTheCharacterAndItsMods |
| `FirstLevelSearchTextBox` | `x:Name` | `MainWindow.xaml:681` | Repository_ShowsTheCharacterAndItsMods |
| `SecondLevelListView` | `x:Name` | `MainWindow.xaml:738` | Repository_ViewButtonsSwitchAndAreSaved（只在列表视图显示） |
| `BatchModsButton` | 代码 | `MainWindow.LocalTags.cs:55` | Repository_BatchDialogOpensWithTheListedMods |
| `RepositoryViewCovers`、`RepositoryViewPresets`、`RepositoryViewList` | 代码，`"RepositoryView" + 视图名` | `MainWindow.RepositoryWorkspace.cs:197` | Repository_ViewButtonsSwitchAndAreSaved |
| `PresetShareImportButton` | 代码 | `MainWindow.CombinationBundles.cs:32` | Presets_ShareButtonsAreThere、Repository_ViewButtonsSwitchAndAreSaved |
| `PresetShareExportButton` | 代码 | `MainWindow.CombinationBundles.cs:31` | Presets_ShareButtonsAreThere（无预设时应禁用） |

J7-b：工具卡片换 `CommandBar` 时，`RefreshButton` 变成 `AppBarButton`，保留 `x:Name` 即可；视图按钮换 Toolkit `Segmented` 时，每个 `SegmentedItem` 设同样的 AutomationId，`SegmentedItem` 支持 `SelectionItem`，`Invoke` 可用。

### 在线（J6 / J6-b）

| AutomationId | 来源 | 定义位置 | 用到的测试 |
| --- | --- | --- | --- |
| `OnlineSearchTextBox` | `x:Name` | `MainWindow.xaml:1158` | Online_LayoutButtonsSwitchAndAreSaved |
| `OnlineListLayoutButton` | `x:Name`，测试拼成 `"Online" + 布局 + "LayoutButton"` | `MainWindow.xaml:1055` | Online_LayoutButtonsSwitchAndAreSaved |
| `OnlineGridLayoutButton` | 同上 | `MainWindow.xaml:1064` | Online_LayoutButtonsSwitchAndAreSaved |
| `OnlineVirtualizedListView` | `x:Name` | `MainWindow.xaml:1182` | Online_LayoutButtonsSwitchAndAreSaved |
| `OnlineVirtualizedGridView` | `x:Name` | `MainWindow.xaml:1217` | Online_LayoutButtonsSwitchAndAreSaved |

J6-b：搜索框换 `AutoSuggestBox` 后，AutomationId 落在外层控件上，测试只检查存在，不受影响；网格视图换 `ItemsView` 时保留 `OnlineVirtualizedGridView` 这个名字（或同时改测试）；布局按钮进 `Segmented` 的处理同工作区。

### 快捷键（J5）

| AutomationId | 来源 | 定义位置 | 用到的测试 |
| --- | --- | --- | --- |
| `ShortcutInputModePicker` | 代码 | `MainWindow.ShortcutPanel.cs:330` | Shortcuts_InputMethodIsSaved |
| `OverlayClickThroughButton` | 代码（浮窗里） | `MainWindow.OverlayAppearance.cs:121` | Overlay_ClickThroughIgnoresTheMouseUntilTheHotkey |

### 更新（J3 / J3-b）

| AutomationId | 来源 | 定义位置 | 用到的测试 |
| --- | --- | --- | --- |
| `CheckModUpdatesButton` | `x:Name` | `MainWindow.xaml:1510` | Updates_ShowsItsSectionsAndSavesTheCheckInterval |
| `ClearDownloadTasksButton` | `x:Name` | `MainWindow.xaml:1592` | Updates_ShowsItsSectionsAndSavesTheCheckInterval |
| `ModUpdateIntervalComboBox` | `x:Name` | `MainWindow.xaml:1532` | Updates_ShowsItsSectionsAndSavesTheCheckInterval |
| `ConflictDetectionToggleSwitch` | `x:Name` | `Pages/UpdatesPage.xaml` | Updates_ConflictDetectionSwitchIsSaved（J3 新增的冒烟测试） |
| `OpenCombinationPresetsButton` | `x:Name`（J3 起写在 XAML 里，原来由工作区代码临时加上） | `Pages/UpdatesPage.xaml` | 同上 |

J3 之后这几个控件都在 `Pages/UpdatesPage.xaml`，名称不变。

J3-b 完成后：页面顶部是状态区，下面 `SelectorBar` 分三个视图，`SelectorBarItem` 的名称是 `UpdatesViewMods`、`UpdatesViewDownloads`、`UpdatesViewRecovery`（讲述人读的是视图名称）。没选中的视图是折叠的，不在自动化树里，所以 `Updates_ShowsItsSectionsAndSavesTheCheckInterval` 先切到“下载记录”再找 `ClearDownloadTasksButton`，`Updates_ConflictDetectionSwitchIsSaved` 先切到“安装与恢复”。`RollbackLastInstallButton`、`OpenCombinationPresetsButton` 现在是可点击的 `SettingsCard`，名称不变；“刷新备份列表”按钮在折叠的“备份”`SettingsExpander` 里，AutomationId 仍是 `RefreshBackupsButton`。新测试 `Updates_ViewsSwitchByKeyboardAndAreSaved` 用 Tab 进入选择栏、方向键和空格切换视图，并检查视图会保存（`beta-shell.json` 的 `UpdatesView`）。

### 设置（J2）

| AutomationId | 来源 | 定义位置 | 用到的测试 |
| --- | --- | --- | --- |
| `LanguageComboBox` | `x:Name` | `Pages/SettingsPage.xaml` | Settings_NarrowWindowHasEveryGroupOnOnePage、Settings_LanguageListComesFromTheFilesAndSwitchesTheWindow、Settings_WideWindowShowsEveryCard |
| `ThemeComboBox` | `x:Name` | `Pages/SettingsPage.xaml` | Settings_ThemeChoiceIsSaved（J2 新增的冒烟测试） |
| `CheckUpdatesButton` | `x:Name` | `Pages/SettingsPage.xaml` | Settings_NarrowWindowHasEveryGroupOnOnePage、Settings_WideWindowShowsEveryCard |
| `EditOnlineRepositoryButton` | `x:Name`（现在是可点击的 `SettingsCard`） | `Pages/SettingsPage.xaml` | 同上 |
| `ExportDiagnosticsButton` | `x:Name`（现在是可点击的 `SettingsCard`） | `Pages/SettingsPage.xaml` | 同上 |
| `ProjectResourcesTitleTextBlock` | `x:Name`（“关于”分组标题，不在折叠区里） | `Pages/SettingsPage.xaml` | 同上 |

J2 完成后：设置页是单列长页面，`SettingsSection0`–`4` 已删除；`Settings_SectionButtonsShowOneCardAtATime` 改写为 `Settings_NarrowWindowHasEveryGroupOnOnePage`（1000×760 下五个分组都在页面里，并确认分区按钮不在了），`Settings_WideWindowShowsEveryCard` 去掉了对 `SettingsSection0` 的断言。`ProjectResourcesTitleTextBlock` 成了“关于”分组的标题，所以默认折叠的“关于”`SettingsExpander` 不影响测试。

### 对话框（J8）

| AutomationId | 来源 | 定义位置 | 用到的测试 |
| --- | --- | --- | --- |
| `CloseButton` | `ContentDialog` 模板里的按钮部件 | WinUI 控件模板 | Repository_BatchDialogOpensWithTheListedMods |
| `FirstRunGame_<预设 Id>`（测试用 `FirstRunGame_genshin`） | 代码，`"FirstRunGame_" + preset.Id` | `MainWindow.FirstRun.cs:22` | FirstInstall_ChoosingAGameFillsTheRepository |

J8：对话框统一设置 `DefaultContentDialogStyle` 后，模板部件名 `CloseButton` 不变。J4-b 把首次安装弹窗改成页面内卡片时，保留 `FirstRunGame_<Id>` 并把“使用”“以后再说”按钮的文字键保持不变。

## 按可见文字查找

| 文字 | 语言键或来源 | 用到的测试 | 定义位置 |
| --- | --- | --- | --- |
| 首次安装“以后再说” | `FirstRun.Later` | FirstInstall_SkippingTheGameLeavesTheRepositoryWithoutOne | `MainWindow.FirstRun.cs:31` |
| 首次安装“使用” | `FirstRun.Use` | FirstInstall_ChoosingAGameFillsTheRepository | `MainWindow.FirstRun.cs:31` |
| 批量操作对话框标题 | `BatchMods.Title` | Repository_BatchDialogOpensWithTheListedMods | `MainWindow.BatchModActions.cs:45` |
| 仪表盘仓库卡片“切换到此仓库” | `Dashboard.SwitchToThisRepository`（用 `FindAllDescendants` 找全部） | Dashboard_SwitchingToARepositoryWithoutFoldersKeepsRunning | `MainWindow.Dashboard.cs:38` |
| 快捷键页浮窗热键摘要 | `ShortcutPanel.OverlayHotkeySummary`，填入 `Ctrl+Alt`、`F9` | Shortcuts_SummaryShowsTheSavedHotkey | `MainWindow.ShortcutPanel.cs:1246` |
| 输入方式选项“虚拟键（旧版兼容）” | `ShortcutPanel.VirtualKeysLegacyCompatibility` | Shortcuts_InputMethodIsSaved | `MainWindow.ShortcutPanel.cs:332` |
| 窗口标题（切换语言后） | `Common.IntegratedModManager`（英文，取 `{` 之前的部分） | Settings_LanguageListComesFromTheFilesAndSwitchesTheWindow | 窗口标题 |
| `TestCharacter`、`TestModA`、`TestModB` | `tests/TestData/ModRepository` 里的文件夹名 | Repository_ShowsTheCharacterAndItsMods | 测试数据 |

规则：拆分计划规定语言文件的键只增不改名，所以按文字查找不受移动影响。外观改动若把“切换到此仓库”合并进点击整行（J4 外观方案第 4 条），要保留一个带这个名称的可调用元素，或改写测试。

## 按控件类型或窗口查找

| 条件 | 用到的测试 | 受影响的阶段 |
| --- | --- | --- |
| 类名 `Microsoft.UI.Xaml.Controls.Expander`：展开快捷键页所有折叠区，才能看到 `ShortcutInputModePicker` | Shortcuts_InputMethodIsSaved | **J5**：设置栏换成 Toolkit `SettingsExpander` 后类名变为 `CommunityToolkit.WinUI.Controls.SettingsExpander`（它内部仍有 `Expander`，但要以实际自动化树为准）。J5 里改为按 AutomationId 展开指定的分组 |
| 顶层窗口标题以 `IMM ` 开头（浮窗） | Overlay_ClickThroughIgnoresTheMouseUntilTheHotkey | 不在阶段 J 范围（浮窗不改） |

## 按阶段的检查清单

| 阶段 | 必须保留或同步改的名称 |
| --- | --- |
| J1 / J1N | 6 个 `*NavButton` |
| J2 | `LanguageComboBox`、`ThemeComboBox`、`CheckUpdatesButton`、`EditOnlineRepositoryButton`、`ExportDiagnosticsButton`、`ProjectResourcesTitleTextBlock`；两个设置页测试已改写（`SettingsSection*` 已删除） |
| J3 / J3-b | `CheckModUpdatesButton`、`ClearDownloadTasksButton`、`ModUpdateIntervalComboBox`；J3-b 加视图切换 |
| J4 / J4-b | `SourceTextBox`、`TargetTextBox`、文字 `Dashboard.SwitchToThisRepository`；J4-b 保留 `FirstRunGame_<Id>`、`FirstRun.Use`、`FirstRun.Later` |
| J5 | `ShortcutInputModePicker`、`OverlayClickThroughButton`、文字 `ShortcutPanel.OverlayHotkeySummary`、`ShortcutPanel.VirtualKeysLegacyCompatibility`；改写按 `Expander` 类名展开的逻辑 |
| J6 / J6-b | `OnlineSearchTextBox`、`OnlineListLayoutButton`、`OnlineGridLayoutButton`、`OnlineVirtualizedListView`、`OnlineVirtualizedGridView` |
| J7 / J7-b | `RefreshButton`、`FirstLevelSearchTextBox`、`SecondLevelListView`、`BatchModsButton`、`RepositoryView*`、`PresetShareImportButton`、`PresetShareExportButton`、文字 `BatchMods.Title` |
| J8 | `CloseButton`（模板部件） |

## English summary

The UI tests find elements by 39 automation IDs (24 from `x:Name`, 5 explicit `SettingsSection0`–`4`, 9 set in code including the `RepositoryView*` and `FirstRunGame_<id>` families, and the `ContentDialog` template part `CloseButton`), by six language keys plus the window title, by three test-data folder names, by the `Expander` class name and by the overlay window's `IMM ` title prefix. Moving a control changes its automation ID if its `x:Name` changes, so stage J keeps the names or sets `AutomationProperties.AutomationId` explicitly. Known test changes the decided design forces: J1N sets the old IDs on `NavigationViewItem`s; J2 removes the Settings section buttons, so two Settings tests are rewritten; J3-b must switch `SelectorBar` views before looking for the download-history button; J4 collapses the paths and J2 collapses the About section, so tests expand them first; J5's `SettingsExpander` changes the class name the input-method test expands by.
