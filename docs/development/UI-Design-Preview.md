# v3.9.0 界面设计记录 / v3.9.0 UI Design Record

本设计最初在 `design/refined-winui` 分支上以 v3.8.5（`443f864`）为基线隔离开发，完成验证后作为 v3.9.0 的界面与交互实现发布。本文件保留设计范围、边界和验证依据。

## 设计范围

- 蓝灰色浅色 / 深色主题，统一卡片、边框、圆角和标题层级。
- 导航始终提供图标与文字；宽窗口使用横向标签，较窄窗口使用上下排列，设置入口固定底部。
- 应用页头和 Mod 工作台标题改为单行，不再显示标题下方的说明小字；页头直接显示重新设计的 Windows 11 Fluent 风格文件夹切换图标。图标采用透明背景、简化的双文件夹轮廓和双向箭头，Windows 标题栏会显式加载同一份多尺寸 `.ico`，不再回退为通用程序图标。
- 仓库操作栏固定在滚动区域上方，刷新、导入、启动器、部署及进度反馈保持可见。
- 路径与部署设置已集成到仪表板；每个仓库分别保存源目录、目标目录和启动器路径，切换仓库时同步显示当前仓库的配置。
- 仓库工作区改为“分类 → Mod → 详情”的三段式工作台；宽屏采用三栏，中等宽度采用两栏并将详情置于下方，窄屏改为单栏纵向流程。
- 详情区集中显示当前选择与部署状态，并通过“预览与链接 / 快捷键”标签页降低页面长度和视觉干扰。
- 工作台的分类数和 Mod 数不再使用圆角胶囊外框，改为无外框的文件夹 / 文件图标、标签和数字，并以细分隔线区分两组信息；其他轻量标签也收敛为小圆角矩形。
- Mod 切换工作台锁定在当前视口，不再整页上下滑动；状态栏固定在底部，角色与 Mod 列表保留各自的内部滚动。不同窗口宽度会重新分配三段工作区的行列和高度。
- 仓库列表高度会随可用视口增长；最大化时充分利用纵向空间。小型操作按钮使用图标、提示和可访问名称，避免文字被截断。
- 仪表板宽屏采用“当前仓库 + 指标”“仓库路径 + 游戏预设”的双区布局；较窄窗口自动改为单列。预设区包含绝区零、原神、崩坏：星穹铁道和明日方舟：终末地四项预设；宽屏侧栏内纵向等高填充，中、窄窗口继续按可用宽度横向排列或折行。
- 设置页在宽屏与半屏使用双栏总览，按内容高度组合“界面与语言 / 仓库在线”和“软件更新 / 诊断工具”；两列改为共享高度的网格，底部卡片会拉伸到同一条基线。设置行与卡片间距更紧凑，动态来源名称可在半屏统计卡中换行。超宽视口会放宽内容宽度并使用约 86% 的可用高度，短卡片内容垂直平衡，版本说明区同步扩展。更窄窗口切换为侧栏或 2×2 分类导航和单卡片内容。
- 终末地预设内置 GameBanana 分类 `42770` 和官方森空岛 Wiki；旧的终末地仓库缺少 Wiki 时会在读取配置时自动补齐。在线页先使用内置角色目录兜底，再按月读取终末地官网“干员情报”页面的中英文名称、头像和排序，与 GameBanana 分类合并后写入本地缓存。
- 可选择“最小化到系统托盘”；启用后最小化隐藏主窗口，首次通知、托盘悬停提示和右键菜单会显示当前仓库、Mod 数量及就绪状态、在线来源和软件版本等基础信息，双击托盘图标或使用托盘菜单恢复。
- 在线下载和本地导入完成后，会只读扫描 Mod 的 `.ini` 文件，从 `[Key...]` 段提取按键、行为与目标，且不会覆盖已有手工或在线快捷键说明。自动结果会把 `hairacc`、`img_x` 等变量整理成“发饰”“图像横向选项”等可读名称，并随当前中英文界面实时切换；旧版自动生成的原始变量说明会在重新扫描时迁移，手工说明保持原样。
- 快捷键编辑区取消固定 10 行上限，按识别结果和用户新增行数动态创建控件。INI 扫描仅保留 256 项异常文件安全阈值，正常多快捷键 Mod 不再被截断到前 10 项。
- 导入文件选择器对复合压缩格式使用兼容的末级扩展名，并在选择器初始化或显示失败时保留程序进程、记录错误并显示提示。
- 保留目录链接、复制部署、下载、数据存储和更新器核心实现。

## 使用与边界

在本分支运行 `build_winui.bat`，启动 `dist/IntegratedModManager.exe`。必须保留整个 `dist` 目录，不能只复制入口 EXE。

开发工作目录：`G:\Integrated_Mod_Manager_Development\Integrated_Mod_Manager-ui-design`。测试配置与用户正式安装相互独立。首次使用需要选择自己的仓库和目标路径。配置真实路径后，部署和删除等操作仍是实际操作，不是模拟器。

正式应用标题不再带“界面预览 / UI Preview”，页头显示 WinUI 3 与当前版本。发布包使用 `IntegratedModManager.exe` 作为用户入口，并暂时保留旧入口以支持从 v3.8.5 自动更新。

## 验证

- WinUI x64 Release 构建：0 警告、0 错误。
- `scripts/test-all.ps1`：146/146 项通过（Core 112、DataStore 7、Updater 27），覆盖率门槛通过；其中包含多于 10 项的 Mod 快捷键扫描、双语可读说明、旧说明迁移判断、终末地官网目录解析和新入口更新包识别测试。
- 终末地官网实时解析验证：33/33 名干员，包含提弗洛斯、噗切娜和两种管理员形态。
- 实际窗口检查包括程序启动、最大化与半屏状态、仪表板和设置分类切换、仓库页面纵向空间利用、详情标签切换、中英文切换、深浅主题切换、响应式布局，以及启用托盘选项后的最小化隐藏行为。Windows 通知区域原生菜单仍需人工查看文字和点击恢复。
- 自动化测试覆盖既有核心逻辑，不等于完整 GUI 或游戏内回归。没有在真实游戏中执行 Mod 部署、删除或下载测试。

## English

The v3.9.0 implementation was developed in an isolated branch from the v3.8.5 baseline before promotion. It introduces a neutral blue-gray light/dark palette, labeled responsive navigation, bottom-anchored settings, a persistent repository command surface, per-repository path settings on the Dashboard, optional minimize-to-tray behavior, and accessible icon buttons. The app header and Mod workspace heading are now single-line and use a redesigned Windows 11 Fluent-style folder-transfer icon with a transparent background, simplified dual-folder silhouette, and two-way arrows. A matching multi-resolution `.ico` is explicitly applied to the native Windows title bar. Workspace category and Mod totals use unframed folder/file indicators with a divider instead of rounded pill containers; remaining lightweight labels use restrained small-radius rectangles. The repository is reorganized as a focused Category → Mod → Details workflow: three columns on wide screens, two rows at medium widths, and three compact rows on narrow screens. The entire Mod workspace is locked to the viewport, with the status strip anchored at the bottom and scrolling confined to the category, Mod, shortcut, and detail regions. Selection and deployment status stay together, while Preview & Link and Hotkeys use tabs to reduce clutter. The Dashboard uses two balanced wide-screen groups and collapses to one column at narrower widths; its preset card now includes Zenless Zone Zero, Genshin Impact, Honkai: Star Rail, and Arknights: Endfield. Settings use a tighter two-column overview at full and half-screen widths, pairing Appearance & Language with Repository Online and App Updates with Diagnostics. Both columns share a common grid height so their bottom cards end on the same baseline. Ultra-wide layouts expand toward the viewport width, use roughly 86 percent of its available height, vertically balance short cards, and show more release-note content. Narrow layouts retain focused section navigation.

Existing controls and event handlers are reused. Deployment, junction handling, data storage, and updater core code remain unchanged. When minimize-to-tray is enabled, the notification, tooltip, and native context menu summarize the current repository, Mod count/readiness, online source, and app version while keeping restore and exit available. Downloaded or locally imported Mods are scanned read-only for `.ini` `[Key...]` sections, and inferred shortcuts are added only when no manual or online shortcut metadata already exists. Raw targets such as `hairacc` and `img_x` are formatted as readable, language-specific labels; generated descriptions update when the app language changes, legacy generated descriptions can be migrated, and manual notes remain untouched. The shortcut editor now creates rows dynamically instead of imposing the previous 10-row product limit; only a 256-entry defensive scan ceiling remains for malformed or hostile INI input. The archive picker now translates compound formats into compatible picker extensions and guards the complete picker flow so a picker error no longer terminates the app. Build with `build_winui.bat`, then run `dist/IntegratedModManager.exe` with the entire `dist` folder intact. Preview settings are separate; operations against real paths are still real operations.

The Endfield preset includes GameBanana category `42770` and the official SKLAND Wiki. Missing Wiki metadata on existing Endfield repositories is upgraded automatically. The online browser keeps the bundled list as an immediate fallback, refreshes localized operator names, portraits, and order from the official Endfield operator page on the same monthly cache cycle as the other game presets, and merges those records with GameBanana categories.

The Release build succeeds with zero warnings and errors. All 146 automated tests pass (Core 112, DataStore 7, Updater 27), including more-than-10 shortcut scanning, bilingual readable descriptions, legacy-description migration checks, Endfield catalog parser tests, and renamed-launcher package discovery; all coverage gates pass. A live parsing check returned all 33 operators listed by the official site at verification time. Native-window smoke checks cover startup, maximized and half-screen layouts, Dashboard and Settings-category navigation, maximized repository height, detail tabs, both languages, both themes, and responsive layout; they do not constitute complete in-game regression testing. The validated implementation is included in the v3.9.0 release line.
