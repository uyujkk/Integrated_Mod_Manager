# 后续路线图 / Roadmap

更新于 2026-11-02，基于 v4.6.1 之后的 main（`5af54eb`）。本文把此前分散的计划和遗留项整合到一处，是规划，不是已实现功能。工作量为粗估：S ≈ 1–2 天，M ≈ 3–5 天，L ≈ 2 周以上。

Updated 2026-11-02 against main after v4.6.1. This brings the earlier plans and leftovers into one place. It is a plan, not shipped features. Effort is a rough guess: S ≈ 1–2 days, M ≈ 3–5 days, L ≈ 2+ weeks.

## 总览 / Overview

版本号只是建议分组，发布时间和范围由维护者决定。

| 阶段 | 内容 | 工作量 | 依赖 |
| --- | --- | --- | --- |
| **A. 4.6.2 收尾** | 真实环境核对、不稳定测试、Discord 入口、按键相关的界面问题 | S | 需要真实游戏和维护者配合 |
| **B. 4.7 浮窗** | 元素底板和文字阴影 → 预设截图第一阶段 → 背景模式、鼠标穿透、HUD → 待生效队列 | M–L | 建议先完成 A 的 F10 核对 |
| **C. 4.8 功能** | 批量操作 → 预设截图第二阶段；瞬态变量过滤、轻量方案分享、自动保存间隔 | L | 无 |
| **D. 工程清理（穿插）** | 语言文件合并、快捷键页 ViewModel、`async void`、空 `catch` 等 | 每项 S–M | 无，不单独发版 |
| **F. 测试整合与兼容** | 整合压缩测试；补配置和格式的兼容测试；明确支持范围 | M–L | 建议排在 B 之前，详见 F 节 |
| **G. 适配其他游戏** | 按系统语言选界面语言（G1 已实现）；首次安装弹窗选择默认仓库预设（G2 已实现）；其他游戏通过 XXMI 一键启动（G3 部分实现） | M | G3 需要实机核对；建议在面向其他游戏发布前完成 |
| **搁置** | #15 原生闪退（等转储）、C3 终末地修复器集成（#8）、#9 角色定向修复（等 #8）、E Linux 尝试 | — | 见下 |

## 未完成内容 / Open work（2026-11-02 整理）

已完成：A2、A3、A4（#61）；B1（#62）；F3 的配置样本和未知字段（#63）；B2 和 G1（#64）；G2 和 G3 的主要部分（#65）。B2 的自动截图和框选已由维护者实测可用；锁定大小、默认区域和管理员权限下选文件尚待确认。

| 优先级 | 项目 | 状态或缺口 |
| --- | --- | --- |
| 1 | 确认 #64 未实测的部分 | 管理员权限选文件、点图切换和动画流畅度尚待确认 |
| 2 | 切换耗时 | 用 `startup.log` 的 `Slot switch` 记录分清管理器和游戏各占多少；3 秒只读保持是否能缩短，取决于 A1 的 F10 核对 |
| 3 | A1 实机核对 | F10 热应用、变体导入、默认槽位、更新设置写回等未逐项勾选 |
| 4 | G2、G3 | 已合并（#65）；G3 待实机启动验证，自定义仓库选导入器未做 |
| 5 | B3、B4 | 鼠标穿透、紧凑布局和外观预设已实现（分支 `feature/overlay-hud`）；背景模式、自动对比度、待生效队列未做 |
| 6 | F 其余 | F1 压缩、F4 Windows 版本实测未做；键名测试、跳过机制和 Endfield 测试隔离已完成（分支 `feature/overlay-hud`） |
| 7 | C、D | C1、C2、C4–C6；语言文件合并、快捷键页 ViewModel、`async void` 等清理 |

B2 第二阶段的取景框差异已在 B2 节说明；待确认的小项：图片切换列表是否改用固定深色背景；组合预设不进图片切换。

---

## A. 4.6.2 收尾 / Closing out 4.6

### A1. 真实环境核对（只有实机能验证）

这些功能都只用合成数据、夹具或单元测试验证过。

| 核对项 | 来源 | 通过后 |
| --- | --- | --- |
| 真实游戏中浮窗保存：F10、等待写入、写入超时后选择读取现有文件 | #29、#56 | 更新验证记录 |
| 真实游戏中的按键发送和 F10 热应用 | #29 | 决定热应用是否继续标记“实验” |
| 用其他工具真实导出的变体文件导入 | v4.5 | 去掉“待验证”标记 |
| 新 Mod 的“默认”槽位与真实 Mod 声明 | #56 | 更新验证记录 |
| “更新”导航徽标、快捷键页的权限说明 | #49、#57 | 用夹具截图即可 |
| 英文界面和浅色主题下的仪表盘芯片跳转到工作区 | UI 重构 | 用夹具截图即可 |
| 真实预览版安装包走一遍更新设置写回 | v4.6.0 | 更新验证记录 |

实机清单已放进 [回归检查清单](./Regression-Checklist.md) 的“4.6.2 维护者清单”，由维护者试玩时逐项勾选。

### A2. 不稳定测试（S）

`ArchiveProcessRunnerTests.Run_TimeoutTerminatesHangingProcessAndItsChild` 在 CI 上偶发失败，#53 和 #56 都要重跑。之前已改过一次夹具（`bc44ca7`），问题仍在。失败日志（2026-10-27 至 10-29 的三次）都是 20 秒内没等到进程标记文件，也就是启动慢，不是子进程没有结束。做法：超时改为在夹具就绪后才开始计时，时长改为 1 秒；不靠加大超时掩盖。同一批日志里 `LoaderStateCaptureTests` 的一次失败发生在 2 秒上限下，现在的测试已是 10 秒并带重试，未再改动。

### A3. Discord 入口（S，已完成）

两个主页已有邀请链接（#59、#60）。设置页的 QQ 群旁边已加一行“Discord”（#61），Wiki 首页的“反馈与交流”已加同一链接（2026-11-02）。

### A4. 按键相关的界面问题（S）

来自 2026-11-02 的用户反馈：想改 Mod 按键，试了状态槽位和浮窗快捷键设置，都没有效果。以下问题是对照代码确认的；对方的 Mod 还没看到，也没有复现。

| 问题 | 现状 | 做法 |
| --- | --- | --- |
| 热应用提示说得太肯定 | 按键发送成功且文件未被改写，就显示“加载器已重新读取”（`MainWindow.HotReload.cs` 的 `HotReload.ReloadSent`）；游戏没有响应 F10 时，结果也一样 | 改成“已发送 F10，请到游戏里确认”，中英文都改 |
| 界面没说明 Mod 按键不能在这里改 | 按键绑定只读、要改需编辑 INI，这些只写在文档里；状态槽位也没说明它不包含按键 | Shortcuts 页直接提示，并加“打开所在 INI”按钮；状态槽位说明它只保存参数值 |
| 浮窗快捷键容易漏点“应用” | 下拉框选择后不生效，也没有未应用提示 | 选择后立即生效，或在未应用时给出提示 |

“在管理器内改 Mod 按键”不列入：等确认用户需求后再评估。它要写入 Mod 自身的 INI，必须备份和回滚，还要处理 Mod 更新后改动被覆盖的问题。

---

## B. 4.7 浮窗 / Overlay

### 现状

| 部分 | 现状 | 代码 |
| --- | --- | --- |
| 浮窗透明 | 只有背景随“不透明度”（10%–100%）变化，文字和按钮始终不透明；文字颜色可自定义，没有描边或阴影 | `ShortcutOverlayWindow.SetBackgroundOpacity`、`TransparentBackdrop` + 空模糊区域让 DWM 按像素混合 |
| 画面截取 | 角色跟随用 GDI `BitBlt` 截目标窗口客户区；要求游戏在前台、窗口完整在屏幕内；带 `CAPTUREBLT`，会截到覆盖在上面的浮窗 | `CharacterScreenCapture.Capture` |
| 预设数据 | 组合预设 `ConfigurationProfiles`、单 Mod 槽位 `ModPersistentSlots` 都在 `beta-shell.json`，没有图片字段 | `Models/ModPersistentSlot.cs` 等 |

### B1. 透明模式：元素底板和文字阴影（S，已实现）

已实现并用测试图案截图核对过 20% 和 100% 背景；真实游戏画面和屏幕阅读器仍待核对。按钮文字本身没有阴影，靠底板保证可读。

**问题**：不透明度调低后，浅色或花哨的游戏画面上文字难以辨认；整块背景要么挡画面，要么太透。

1. **元素底板**：每个按钮、标签和槽位卡片保留一层独立的半透明圆角底板，透明度和整体背景分开设置（新增“元素底板不透明度”）。整体可以很透，文字下面仍有底色。
2. **文字阴影**：WinUI 的 `TextBlock` 没有描边属性。可以用 Composition `DropShadow` 给文字加 1–2 像素的深色阴影（`TextBlock.GetAlphaMask()`），只在不透明度低于 60% 时启用，避免影响性能。

### B2. 预设截图预览，第一阶段（M，已实现）

**已实现**：`PresetPreviewStore` 及其测试；可选字段 `PreviewFile`；默认关闭的“在浮窗保存预设时截图”和隐私提示；浮窗保存时在 F10 之前截图，近乎纯黑的画面不保存；浮窗状态槽位的缩略图、悬停放大和只显示文字的开关；主窗口状态槽位下拉框和组合预设列表的缩略图；选中预设后可从游戏截取、选择本地图片或清除；恢复确认框里的“保存时”图片；删除预设时删图，启动时清理孤立文件。

**与下面设计的差异**：只在截图的一瞬间设置 `WDA_EXCLUDEFROMCAPTURE`，然后恢复；一直设置会让浮窗从录屏和直播画面里消失。主窗口保存预设时不自动截图，要在保存后用“设置预览图”。

**框选角色（2026-11-02 新增）**：浮窗开启槽位切换后，“框选角色”把浮窗动画变成带边框的透明取景框，用户框住角色并截取，图片归当前槽位；之后点图片就切换到该槽位。框选结果只取落在游戏窗口内的部分。

**图片切换（2026-11-02 新增）**：浮窗的“图片切换”以动画移到屏幕中央，横向列出当前 Mod 各状态槽位的图片，选择后切换到对应槽位并恢复浮窗。组合预设要关游戏才能恢复，所以不在列表里。

**锁定大小和默认区域（2026-11-02 新增）**：取景框可以锁定大小，并设为默认区域（按游戏窗口的比例保存）。设好后，保存单 Mod 预设时自动截取这个区域；组合预设仍截整个窗口。

**切换耗时**：F10 发送后文件固定保持 3 秒只读（`LoaderHotReload.DefaultHold`），期间不能再次切换。这段时间不会推迟游戏生效，但原来界面要等它结束才更新；现在按键发出就更新。图片切换也不再等退出动画才开始切换。游戏重载 Mod 的时间不受管理器控制。

**已验证**：维护者实测自动截图和框选可用（2026-11-02）。

**未验证**：窗口化、无边框、独占全屏三种模式逐一核对。

**目标**：保存组合预设或单 Mod 槽位时附带一张游戏画面缩略图；浮窗和主窗口的预设列表用缩略图辨认；恢复前可以对照“保存时的样子”。

**截取**

1. **时机**：浮窗保存流程在发送 F10 **之前**截取。F10 会让画面短暂重载，之前的画面就是要保存的状态；截图失败不影响保存，只是没有预览。
2. **排除浮窗自身**：对浮窗窗口调用 `SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE)`（Windows 10 2004 起），截图里就不会出现浮窗，也不用临时隐藏它。旧系统退回到截图前短暂隐藏浮窗。
3. **方式**：沿用 `BitBlt`，适用于窗口化和无边框全屏。检测到整张近乎纯黑时提示“此画面无法截取”。
4. **主窗口**：状态槽位和组合预设增加“设置预览图”：从游戏截取（需要选中游戏窗口）或选择本地图片；可以清除。

**存储与数据**

- 文件放在 `WinUI3/snapshots/preset-previews/<仓库 ID>/<预设 ID>.jpg`：长边缩到 640 像素，JPEG 质量 80，约 40–80 KB。
- 预设对象增加可选字段 `PreviewFile`（相对路径），旧配置没有这个字段也能用。
- Core 新增 `PresetPreviewStore`：路径规则、写入（临时文件替换）、删除预设时一起删、启动时清理孤立文件。都可以单元测试；缩放和编码在界面层用 `BitmapEncoder`。
- 安装包和升级不覆盖 `snapshots/`（现有规则已排除）。

**隐私**

- 截图可能包含 UID、昵称和聊天内容。“保存预设时截图”默认**关闭**，首次开启时说明这一点。
- 诊断报告和问题反馈不包含预览图。

**界面**

- 浮窗：槽位从文字按钮改为“缩略图 + 名称”的卡片，可以切换回紧凑的文字列表；鼠标悬停放大。
- 主窗口：状态槽位的下拉框改为带缩略图的列表；组合预设卡片显示预览图；恢复确认对话框显示“保存时”的图片。

**验证**

- Core：路径、写入、清理的单元测试。
- 夹具：用测试窗口验证截图内容正确，并且排除了浮窗自身。
- 实机：窗口化、无边框和独占全屏三种模式各截一次，确认黑屏检测。

### B3. 透明模式：其余项（M）

鼠标穿透、HUD 紧凑布局和外观预设（清晰、平衡、极简）已实现。穿透不保存到配置，用显示/隐藏浮窗的快捷键恢复，没开快捷键时不允许开启。紧凑布局暂不支持调整字号。真实游戏里未验证。

按收益排序：

1. **背景模式（M）**：把“透明 / 模糊 / 实色”做成三选一。模糊用 DWM 模糊或 `DesktopAcrylicController`；独占全屏下模糊无效，退回为透明；需要实测对游戏帧率的影响。
2. **鼠标穿透（S）**：可选“只显示，不响应点击”（`WS_EX_TRANSPARENT`），用全局快捷键切换回可点击。适合把按键提示常驻在画面角落。
3. **HUD 紧凑布局（S）**：只显示当前 Mod、槽位缩略图和按键，隐藏设置行；字号可以调整。
4. **外观预设（S）**：清晰、平衡、极简三档，一键套用上述组合。
5. **自动对比度（M，可选）**：每 1–2 秒截取浮窗下方的一小块画面（复用截图代码，排除浮窗自身），按平均亮度切换浅色或深色文字和底板。默认关闭，只在透明度低时运行。

**B1、B3 的验证**：在亮、暗、高饱和度三类场景截图对比；屏幕阅读器名称；100%、150%、200% 缩放；鼠标穿透开关后焦点不被抢走。

### B4. 待生效队列（M）

**问题**：运行中连续切换多个 Mod 的槽位时，每次都重载会造成多次卡顿。开始前先核实浮窗现在的连续切换是否已经合并重载。

1. 增加“切换槽位后立即重载”开关。关闭时，选择只进入队列，同一个 Mod 只保留最后一次选择。
2. 浮窗和主窗口标出待生效的槽位；重载按钮显示数量，例如“立即生效 (3)”。
3. 确认后一次写入所有值，只发送一次 F10，继续走 `LoaderHotReload`；发送失败或文件未保留时，队列不清空，并提示原因。
4. 用户可以清空队列。退出软件或切换仓库时不静默丢弃：有待生效项就提示。

**验证**：Core 测试去重、部分失败和清空；实机确认一次 F10 后所有 Mod 都生效。依赖 A1 的 F10 核对。

---

## C. 4.8 功能 / Features

### C1. 仓库批量操作（M）

设计稿里还没做的部分：Mod 列表多选后批量加标签、加入组合预设、导出、删除。删除走现有的确认和回收流程。设计稿中 Ctrl+K 的部署和在线搜索动作之前决定不做，仍然不列入。

### C2. 预设截图预览，第二阶段（M）

- **Windows.Graphics.Capture（WGC）**：独占全屏、HDR 和部分 DirectX 游戏下 `BitBlt` 会得到黑屏或错误颜色。
- **只截取框选区域**：复用角色跟随的框选界面，只保存角色所在的区域，也能避开 UID。
- **组合包包含预览图**：可选。现有版本会拒绝 `Version` 不是 1 的清单和未知字段，所以新增内容的组合包无法被旧版导入，不能“旧版忽略图片”。做法见 F3。

### C3. 终末地修复器集成（已搁置）

2026-11-02 决定先搁置，见“搁置”一节。

### C4. 瞬态变量过滤（S–M）

**问题**：菜单开关、页码、拖拽坐标这类变量会随操作变化。它们参与比较时，预设容易被误判为“已改动”；参与恢复时，会把 UI 状态一起写回。

- Core 新增可单元测试的过滤规则，用在槽位的保存、比较和恢复中。
- 规则宁可少过滤：名称含 `slider`、`page` 的变量可能正是外观参数。保存时列出被过滤的变量，用户可以逐项保留，也可以整体关闭过滤。
- 已保存槽位里的值不删除，升级后不改变恢复结果，除非用户重新保存。
- 用真实 Mod 的变量名建测试样本，同时覆盖应过滤和不应过滤的例子。

### C5. 轻量方案分享（M）

**目标**：只分享组合预设的选择和参数值，不带 Mod 文件；对方自己已有这些 Mod 时可以直接套用。它与组合包并存，也避开了 Mod 文件的再分发问题。

- 使用独立的文件格式和版本号，不改组合包的校验规则（见 F3）。
- 导入前先预览：匹配率、缺失的 Mod，以及仓库不是同一游戏时的警告。
- 导入时可以重命名。需要的状态槽位不存在就新建，同名时不覆盖。
- 按不可信输入校验：路径、键名和数值都要检查，与组合包的安全要求一致。

### C6. 自动保存间隔设置（S）

在设置中调整 `d3dx.ini` 的 `settings_auto_save_interval`，缩短后状态能更快写入 `d3dx_user.ini`，减少“文件还没写入”的情况。

- 提供几个固定选项，并可恢复原值；写入前备份，缺少该键时明确提示，不猜测文件位置。
- 说明修改后需要重载才生效，并实机确认；同时测量较短间隔是否造成卡顿。

---

## D. 工程清理 / Cleanup

穿插在功能分支之间做，不单独发版。数字是 2026-10-31 在 `src/` 统计的。

| 项 | 现状 | 做法 | 工作量 |
| --- | --- | --- | --- |
| `BetaLocalization.EnToZh` | 247 条，英文相同时会静默覆盖 zh JSON | 合并进 `zh-CN.json` 后删除 | S–M |
| 快捷键页 ViewModel | 只有布局策略，是 UI 重构唯一没有 ViewModel 的主要页面 | 按其他页面的方式抽出并补测试；最好在 B 之前或同时做 | M |
| `async void` 处理器 | 67 个 | 套上 `UiActionGuard`，改动时顺手做 | M |
| 运行时 `L(中, 英)` | 约 11 处 | 迁到语言文件 | S |
| 空 `catch` | 约 7 处 | 加日志，或注明为什么可以忽略 | S |
| `MainWindow.RefinedWorkspace.cs` | 按位置重组界面 | 改回 XAML | M |
| 大文件 | `RepositoryWorkspace`（1514 行）、`OnlineCharacters`（1407 行）等 | 只在相关功能改动时拆分 | — |
| 在线数据层 | GameBanana 和 Wiki 的抓取与缓存仍在窗口层 | 移到 Core，用模拟 HTTP 测试 | M |
| 已合并的远程分支、旧工作树 | 13 个远程分支；`foundation`、`layout`、`online`、`tests` 等工作树的分支都已合并 | 维护者确认后清理 | S |

不再列入：`SectionCard` 控件（UI 重构已完成，没有页面需要它）。

---

## E. Linux 尝试 / Linux experiment（已搁置）

2026-11-02 决定先搁置。以下方案保留，恢复时开始前要按当时的代码重新核对。

### 已确认的范围（2026-10-29）

Linux 只是一次**跨平台开发尝试**，在 **Ubuntu 虚拟机**里开发和测试，只做基础功能，不连接游戏。

| 做 | 不做 |
| --- | --- |
| 仓库设置与扫描（角色、Mod、标签） | 按键发送、F10 热应用、全局快捷键 |
| Mod 部署与移除（符号链接；复制模式） | 置顶浮窗、截图、角色跟随 |
| 压缩包导入（系统的 `7z` / `bsdtar`） | 内置终末地启动 |
| 组合预设、状态槽位的文件读写（对测试用的 `d3dx_user.ini`） | 软件自更新、安装包更新器 |
| 可选：在线浏览与下载（Core 已有，网络可用即可） | 发布到正式渠道；Windows 版不受影响 |

### 步骤

0. **虚拟机环境（约半天）**：Ubuntu 24.04 LTS。
   - `sudo apt install dotnet-sdk-8.0 p7zip-full libarchive-tools git`，然后克隆仓库。
   - `dotnet build src/IntegratedModManager.Core` 和 `dotnet test tests/Core.Tests` 应能直接构建：测试项目是 `net8.0`，只链接了几份不依赖 WinUI 的适配器源码。
   - 14 个测试文件涉及目录联接、`tar.exe`、`7z.exe` 或 Bandizip，在 Linux 上可能失败，这是第 1 步要处理的清单。
1. **平台抽象（S–M）**：
   - 部署链接：抽出接口，Windows 用目录联接，Linux 用符号链接（`Directory.CreateSymbolicLink`）；“读取链接目标”“判断是否指向仓库”同样按平台实现。
   - 压缩工具查找：Linux 找 `7z`/`7zz`/`bsdtar`，不提供 Bandizip。
   - 数据目录：Linux 放在 `~/.local/share/IntegratedModManager`（XDG）。
   - 测试：只适用于 Windows 的用例改为按系统跳过，或者改写成两个平台都能跑；新增符号链接部署的测试。
2. **Linux CI（S）**：在 GitHub Actions 加一个 `ubuntu-latest` 任务，只跑 Core 和 Data 测试。现有 Windows 任务和发布流程不变。
3. **界面原型（M）**：新建一个独立的 Avalonia 项目（例如 `src/Linux.Avalonia`），不改 WinUI 项目。
   - 只做三页：仓库设置、角色和 Mod 列表、部署与移除，再加一个状态槽位读写页。
   - 现有 ViewModel 依赖 WinUI 类型（例如图片源），不能直接复用；新建小而独立的 ViewModel，逻辑都调用 Core。
   - 选 Avalonia 而不是 Uno：要的功能少，Avalonia 在 Linux 上更成熟，也不用处理 WinUI 代码里的 Win32 调用。
4. **打包（S）**：`dotnet publish -r linux-x64 --self-contained -p:PublishSingleFile=true`，打成 tar.gz，只在虚拟机里手动测试，不进 GitHub Release。

### 如果以后要连接游戏（背景，不在当前范围）

游戏在 Linux 上通常通过 Wine 或 Proton 运行，Mods 目录和 `d3dx_user.ini` 在 Wine 前缀里。能否运行取决于各游戏的反作弊，本项目无法保证。

| 功能 | Linux 上 | 说明 |
| --- | --- | --- |
| 按键发送与 F10 | 受限 | X11 或 XWayland 可用 XTest；纯 Wayland 需要 `uinput`（要权限）或桌面门户 |
| 全局快捷键 | 受限 | X11 可用；Wayland 需要 GlobalShortcuts 门户，各桌面支持不一 |
| 置顶浮窗 | 受限 | X11 可以置顶；Wayland 没有通用做法，KDE 和 wlroots 系可以用 layer-shell |
| 截图与角色跟随 | 受限 | 通过 PipeWire 屏幕共享门户，需要用户授权 |
| 内置终末地启动 | 不适用 | 依赖 Windows 签名进程链 |
| 软件自更新 | 需重写 | 改用 AppImage 或 Flatpak 的更新方式 |

用 Wine 运行 Windows 版不可行：WinUI 3 / Windows App SDK 在 Wine 下基本无法运行。

---

## F. 测试整合与兼容 / Test consolidation and compatibility

### 现状（v4.6.1 的测试结果，2026-11-01 统计）

| 项目 | 测试方法 | 运行用例 | 行 / 分支覆盖率 | 门槛 |
| --- | --- | --- | --- | --- |
| Core | 698 | 1375 | 94.1% / 88.0% | 90% / 85% |
| Data | 19 | 24 | 88.6% / 89.1% | 70% / 60% |
| 更新器 | 23 | 32 | 61.9% / 63.7% | 60% / 55% |
| 终末地启动 | 36 | 47 | 81.7% / 76.5% | 75% / 65% |

结论：

- **不是慢，也没有明显的重复。** 全部用例累计 67 秒，其中 40 秒是 `ArchiveProcessRunnerTests` 的两个 20 秒超时测试，其中一个也是 CI 上不稳定的那个。没有函数体完全相同的测试，只有 11 个方法仅字面量不同。
- **CI 的主要时间不在测试。** 一次约 5.5 分钟：代码风格检查 2 分 45 秒，测试和构建 1 分 50 秒。
- **所以压缩主要换来可读性，不是速度。** 预计能减少约 10% 的测试代码，这是粗估，以实际整合结果为准。
- **真正的缺口在兼容。** 见 F2、F3。

### F1. 整合压缩（M）

按风险从低到高分批做，每批一个 PR：

1. **两个 20 秒测试（S，与 A2 合并）**：先查清失败原因。夹具把启动 PowerShell 和等待超时混在一起，考虑改成等子进程启动后再计时，或者用短超时的小辅助程序。目标是这个类从 40 秒降到几秒，并且不再偶发失败。
2. **合并为 `[Theory]`（S–M）**：11 个只差字面量的方法，以及约 60 个前缀相同的短测试（`PathSafety`、`ParseSha256`、`Rar5.Validate` 等）中确实结构相同的部分。用例数不减少，只减少代码。
3. **共用夹具（S–M）**：临时目录、Mod 目录和 `d3dx_user.ini` 的搭建代码抽成共用助手，先从最大的 `CombinationBundleTests`（674 行）、`XxmiIntegrationTests`（459 行）、`LocalUpdateAgentTests`（439 行）开始。
4. **压缩参数矩阵（S）**：`CombinationBundleTests` 84 个用例、`RepositoryWorkspacePolicyTests` 71 个、`ShortcutKeyPlanTests` 59 个、`Rar5ArchiveValidatorTests` 57 个。同一分支的多组等价数据只留代表值。
5. **删除（S，最后做）**：逐个测试收集覆盖率，列出“没有独占覆盖”的候选，再人工审核。覆盖率只是线索：没有独占覆盖也可能在检查不同的结果。

**删除规则**

- 可以删：只验证框架行为或常量；等价的重复数据；已被上层测试完全包含，并且失败时不会提供额外信息的测试。
- 不删：路径逃逸和压缩包安全、回滚和崩溃恢复、配置读写、更新器、已修复问题的回归测试，以及这次新增的兼容测试。
- 四组覆盖率门槛不降低。更新器只比门槛高约 2 个百分点，这部分只合并不删。
- 每个 PR 说明删了什么、为什么，并列出前后的用例数和覆盖率。

此前整理目录时定的是“只整理不删减”；这一节是新的决定，范围以上述规则为准。

### F2. 跨平台兼容（S–M，也是 E 的第 1 步）

E 搁置期间，只做下面“顺序和完成标准”里列出的跳过机制和键名测试，其余随 E 恢复。

| 问题 | 规模 | 处理 |
| --- | --- | --- |
| 非 Windows 时测试直接 `return`，结果显示为通过 | 18 个测试 | 改成统一的 `WindowsFact` / `WindowsTheory`，显示为跳过 |
| 路径字面量写死反斜杠或盘符 | 59 个、19 个测试 | 属于路径行为的改成 `Path.Combine`；专门测 Windows 路径规则的标记为只在 Windows 运行 |
| 目录联接、PowerShell、`tar.exe` 等 | 23 个、13 个测试 | 抽象部署链接和工具查找后，分成共用契约测试和平台实现测试 |
| `DirectoryLinkDeployment` 的 P/Invoke、`Environment.SystemDirectory` 里的 `tar.exe` | Core 里 2 类 | 按 E 第 1 步抽象 |
| 路径比较大多不区分大小写 | Core 46 个文件用到 `OrdinalIgnoreCase` | Linux 文件系统区分大小写。逐类确认，并为“只差大小写的两个目录”加测试 |

**必须保留的约定**：3DMigoto 的键名固定用反斜杠。现在已经把 `GetRelativePath` 的结果转成 `\`，要加一个 Linux 上也运行的测试锁定键名，避免符号链接或路径抽象改动后，已保存的槽位失效。

### F3. 版本和数据兼容（M）

| 问题 | 风险 | 处理 |
| --- | --- | --- |
| `beta-shell.json` 没有任何测试；模型在 WinUI 项目里，但不依赖 WinUI 类型 | 重命名或改类型时，旧配置静默丢设置 | 已完成：模型链接进 Core 测试；`tests/Core.Tests/Configuration/Samples` 有 4.5.2、4.6.1 的样本，测试读入和写回 |
| 配置没有 `ExtensionData` | 新版配置经旧版保存后，新字段丢失；回退版本时会发生 | 已完成：顶层、在线偏好、仓库、组合预设和状态槽位保留未知字段。从这一版起才生效，更早的版本仍会丢字段 |
| 组合包只接受 `Version` 1，并拒绝未知字段 | C2 加预览图后，旧版无法导入 | 预览图放进单独的可选条目；先确认旧版校验是否接受额外文件，不接受就升到版本 2，并在界面说明需要新版 |
| 升级测试只从上一版开始 | 跨多个版本升级没有验证 | 发布前额外从最早支持的版本升级一次 |
| 旧格式代码没有退役规则：`config.ini`、4.1.1–4.2.0 的 EndfieldBridge 迁移 | 只增不减 | 定支持范围。建议：配置一直可读；4.x 期间保留迁移，5.0 再评估 |

### F4. Windows 版本（S）

- 主页写的是 Windows 10 1809 以上，`TargetPlatformMinVersion` 也是 17763，但 CI 只有 `windows-latest`，验证记录和测试文档里也没有 Windows 10 的记录。
- 建议在虚拟机上用 Windows 10 22H2 和 1809 各做一次启动和部署冒烟测试。过不了就把主页的最低版本改成实测结果。
- 新功能使用新系统 API 时要有退路，例如 B2 的 `WDA_EXCLUDEFROMCAPTURE` 需要 2004 以上。

### 顺序和完成标准

1. F1 第 1 项（并入 A2）。
2. F3 的配置样本和未知字段：在 B2 给预设加 `PreviewFile` 之前完成。
3. F2：跳过机制和键名测试先做，其余随 E 一起搁置。
4. F1 第 2–5 项，穿插进行。
5. F4，在下一个正式版之前。

完成标准：CI 不再偶发失败；Core 测试累计时间明显低于现在的 65 秒；覆盖率门槛不变；旧配置样本全部能读入并写回；只适用于 Windows 的测试不再“假通过”。

---

## G. 适配其他游戏 / Other games

**目的**：软件要在其他游戏的发布页上架，第一次打开不能默认是中文和终末地，其他游戏也要能一键启动。2026-11-02 提出，尚未实现。

**现状**

| 部分 | 现状 | 代码 |
| --- | --- | --- |
| 界面语言 | 固定默认中文，不读取系统语言；只能在设置页手动切换 | `MainWindow.xaml.cs` 的 `_currentLanguage`，`LoadConfig` |
| 默认仓库 | 没有配置时自动建一个“默认仓库”，在线来源和 Wiki 都指向终末地 | `LoadShellConfig` |
| 其他默认值 | 新建仓库、编辑对话框和在线页都回退到 `DefaultOnlineGameName` | `MainWindow.Repositories.cs`、`MainWindow.Dialogs.cs`、`MainWindow.OnlineFeed.cs` |

### G1. 按系统语言选择界面语言（S，已实现）

系统界面语言是中文（含繁体）时用中文，其他语言一律用英文。规则在 Core 的 `UiLanguagePolicy`，有测试。结果写入 `config.ini`，之后不再跟随系统变化。中文系统的首次启动已实测；英文系统未实测，该分支只有单元测试。

### G2. 选择默认仓库预设（S–M，已实现）

已按下面的做法实现。空的游戏和分类现在表示“没有游戏”；旧配置的空字段在第一次读入时写成原来的终末地值（`RepositoryGameMigration`，只运行一次），所以已有仓库不变。

1. 主窗口显示后弹窗，列出 `BuiltInRepositoryPresets` 中的预设，并提供“自定义 / 稍后设置”。弹窗文字使用 G1 选出的语言。
2. 选择预设后按所选游戏建立仓库；选择“稍后设置”或关闭弹窗时，建立不绑定任何游戏的空仓库，下次不再弹出。
3. 去掉回退到终末地的默认值：逐处核对 `DefaultOnlineGameName`、`DefaultOnlineCategoryId` 和 `EndfieldOfficialWikiUrl`。已有仓库的配置不能因此改变，字段为空的旧仓库如何处理要先确认。

### G3. 其他游戏的一键启动（M，部分实现）

**已实现**：原神、崩坏：星穹铁道、绝区零的仓库，启动器是 XXMI（或未填写但能找到默认路径）时，“运行启动器”调用 `--nogui --xxmi <导入器>`。启动前检查 XXMI 配置里有没有导入器和游戏目录。导入器名称已对照本机 XXMI 配置确认。启动器是其他程序时行为不变。

**未实现**：自定义仓库手动选择导入器。

**未验证**：三个游戏的实机启动和 Mod 加载；账号风险提示是否也要显示；`--nogui --xxmi` 对这三个导入器的实际效果。

**原有方案**：XXMI 集成只支持终末地。启动时固定调用 `--nogui --xxmi EFMI`（`EndfieldLauncher.cs`），配置读写也只认 `Importers.EFMI`（`XxmiIntegration.cs`），入口只对终末地仓库显示（`CanUseEndfieldBridge`）。其他游戏的仓库只能直接运行填写的启动器程序，不会指定游戏。

**做法**

1. 内置预设增加对应的 XXMI 导入器：原神 `GIMI`、崩坏：星穹铁道 `SRMI`、绝区零 `ZZMI`。名称和命令行参数要先对照当前版本的 XXMI 确认。
2. 其他游戏直接调用 `XXMI Launcher.exe --nogui --xxmi <导入器>`，由 XXMI 自己启动游戏，不改写 XXMI 配置。终末地的适配、回调和恢复点是它特有的启动方式需要的，不推广到其他游戏。
3. 复用现有的 XXMI 路径查找。XXMI 没有这个导入器或未配置游戏目录时，提示用户到 XXMI 里设置，不静默失败。
4. 自定义仓库可以手动选择导入器，或继续直接运行自己填写的程序。没有选择时行为不变。

**验证**：Core 测试预设到导入器的对应关系和命令行参数；三个游戏的实机启动需要维护者或用户配合，要确认 Mod 确实加载，并核对账号风险提示是否也要显示。

### 只在第一次安装时生效

G1 和 G2 用同一个判断：`config.ini` 和 `beta-shell.json` 都不存在。升级不会触发，因为这两个文件会保留。两个文件分开加载，判断要在加载之前算好，不能在其中一个已写入后再算。

**验证**：Core 测试语言规则和首次安装判断；用空目录分别以中文和英文系统语言启动；从 4.6.1 升级，确认语言、仓库都不变且不弹窗；关闭弹窗后重启，确认不再弹出。

---

## 搁置 / On hold

- **#15 在线详情页原生闪退（`0xc000027b`）**：未再复现，没有转储，根因未确认。等到能捕获 stowed exception 转储再处理；不把其他崩溃的修复算作解决了它。
- **C3 终末地修复器集成（#8，M–L）**：2026-11-02 决定先搁置。如果恢复：先做可选安装、打开原工具和只读扫描报告；管理器内确认修复以后再评估；下载完成后不自动修复。细节和已有静态核查见 [#8](https://github.com/uyujkk/Integrated_Mod_Manager/issues/8)。开始前要重新核查上游当前版本。
- **#9 角色定向修复**：依赖 #8 的集成和结果协议，随 C3 一起搁置。
- **E Linux 尝试**：2026-11-02 决定先搁置，方案保留在 E 节。F2 中依赖它的部分一起搁置。
- **`[Present]` 一次性注入（备用方案）**：在 Mods 下放一个隐藏的桥接 ini，重载后第一帧注入目标值，等重载结束再写回 `d3dx_user.ini`。代价是多一个文件，还要配置等待时间。只有 A1 证明只读方案不可靠时才评估，现在不做。

## 已完成，不再跟踪 / Done

这些出现在旧计划（`FollowUp-Plan.md`、[FollowUp-2026-10-24](./FollowUp-2026-10-24.md)）里，现在都已合并：角色头像、单选或多选链接规则、状态槽位和变体、写入 Mod 默认值、整份快照、F10 热应用、浮窗保存前 F10、浮窗刷新、多参数响应速度、中性名称和功能标记、`src/` `tests/` 结构、UI 重构第 0–6 阶段、默认槽位（#56）、排版修复（#57）。

## 仍然有效的决定 / Standing decisions

- 部署路径保持 `Mods/<Mod 名>`，已有参数键名和槽位不受影响。
- 自己实现，不移植 GPL 代码；许可证不变。界面和文档不出现其他管理器的名称，变体文件名只保留在导入筛选条件里。
- 写入 Mod 自身 ini 必须备份，并且能回滚。
- F10 热应用和自动翻译保持“实验”标记，直到通过实机核对。

## English summary

- **A. 4.6.2 close-out (S):** real-game checks (overlay save and F10, key sending, real variant files, Default slots), plus the fixture-checkable Updates badge and privilege note; find the cause of the flaky archive timeout test; add the Discord link to Settings and the wiki home page; from user feedback, stop the hot apply message claiming the loader reloaded when only F10 was sent, say in the UI that Mod keys are read-only (with a button to open the INI) and that slots hold no key bindings, and make the overlay hotkey picker hard to leave unapplied.
- **B. 4.7 overlay (M–L):** per-element backing and text shadows first; then preset screenshots phase 1 (capture before F10, exclude the overlay, 640 px JPEGs under `snapshots/preset-previews/`, off by default because screenshots can show UIDs); then background modes, click-through, a compact HUD and appearance presets; then a pending queue that applies several slot choices with one F10.
- **C. 4.8 features (L):** batch actions in the Mod list; screenshots phase 2 (Windows.Graphics.Capture, region-only capture, previews in bundles); a reviewable filter for transient variables such as menus and page numbers; lightweight sharing of presets without Mod files, with a match preview on import; a setting for the loader's auto-save interval.
- **D. Cleanup (woven in):** fold `BetaLocalization` into `zh-CN.json`, add a shortcuts page ViewModel, guard the 67 `async void` handlers, finish string and empty `catch` leftovers, move online fetching to Core, remove merged branches after confirmation.
- **E. Linux experiment (1–2 weeks, on hold since 2026-11-02; the parts of F2 that depend on it wait too):** Ubuntu VM, basic features only, no game connection. VM setup, platform abstraction (symlinks, archive-tool lookup, XDG, OS-specific tests), an `ubuntu-latest` CI job, a small separate Avalonia prototype, and an unreleased `linux-x64` tar.gz. The Windows build is unaffected.
- **F. Tests and compatibility (M–L):** the suite is neither slow nor visibly duplicated: 67 s in total, 40 s of it in two timeout tests, one of them the flaky one. Consolidate in low-risk batches (fix those two, merge into `[Theory]`, shared fixtures, smaller data matrices), then delete only reviewed candidates, keeping safety, rollback, config and updater tests and all coverage gates. The real gaps are compatibility: `beta-shell.json` has no tests and drops unknown fields, bundles reject other versions and unknown fields, 18 tests pass silently off Windows, and Windows 10 is claimed but has no recorded test.
- **G. Other games (M, before publishing for other games):** on a first install only, pick the UI language from the system language (Chinese or English), and ask for the default repository preset instead of assuming Endfield; upgrades are unaffected. One-click launch for Genshin Impact, Honkai: Star Rail and Zenless Zone Zero through XXMI's own importers, without rewriting XXMI's configuration. Needs real-game checks.
- **On hold:** #15 (needs a crash dump), C3 the optional Endfield repair tool (#8, set aside 2026-11-02), #9 (needs #8), E the Linux experiment, and one-shot `[Present]` injection, a fallback to evaluate only if A1 shows the read-only method is unreliable.
- **Suggested next step:** A, then B1.
