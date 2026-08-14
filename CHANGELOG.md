# CHANGELOG / 更新日志

[中文说明](./README.md) | [English README](./README.en.md) | [GitHub Releases](https://github.com/uyujkk/Integrated_Mod_Manager/releases)

本文档记录集成化 Mod 管理器已发布版本的主要变化。

This document records the major changes in published versions of Integrated Mod Manager.

## v3.4.2

### 中文

- 在线列表支持取消过期请求，切换仓库、角色或重复刷新时不再保留旧请求。
- GameBanana 返回 `429` 后启用指数冷却并遵循服务器重试时间，避免连续请求加重限流。
- 新增实时数据、缓存时间、离线缓存、冷却倒计时和超时停止状态提示。
- 在线列表与方块模式改用 WinUI 虚拟化容器，只为可见项目创建卡片，降低长列表内存与图片请求压力。
- 配置文件改为原子写入，最近备份自动轮换；主配置损坏时会恢复最近有效备份。
- 本地更新会在安装目录保留最近三个版本备份；新版本启动失败时自动回滚并重新启动旧版本。
- 在线列表超时后暂停同一配置的自动重试，避免 5 秒超时通知和加载流程无限循环。
- 修复角色筛选动画重置布局坐标，导致 Mod 卡片覆盖搜索、排序和翻页控件的问题。
- 修复在线工作区宽度与右侧内边距计算，统一上一页/下一页按钮尺寸，并移除被裁切的重复提示。

### English

- Online list requests can now be cancelled, so repository, character, and refresh changes do not leave obsolete requests running.
- `429` responses trigger exponential cooldown while respecting server retry guidance.
- Added visible live-data, cache-age, stale-cache, cooldown, and timeout status indicators.
- List and grid modes now use WinUI virtualized containers, creating cards only for visible items.
- Configuration files use atomic writes and rotating backups, with automatic recovery from the latest valid copy.
- Local updates retain the latest three version backups and automatically restore the previous version if the new runtime cannot start.
- Automatic retries for the same online configuration now pause after a timeout, preventing repeated five-second timeout notifications and reload loops.
- Fixed character-filter animations resetting layout coordinates and moving mod cards over the search, sort, and pagination controls.
- Corrected online workspace width and right-padding calculations, made both pagination buttons consistent, and removed their clipped duplicate tooltips.

## v3.4.1

### 中文

- 角色列表优先显示本地目录缓存，缓存超过 30 天时继续显示旧数据并在后台更新。
- 角色头像优先使用磁盘缓存，未缓存头像限制为最多 3 个并发下载。
- 切换仓库、语言或重建角色栏时取消旧头像任务，避免错位与无效请求。
- 图片请求遇到 `429` 或临时服务器错误时自动退避重试。
- 手动刷新在线 Mod 时同步触发后台角色目录更新，但不阻塞 Mod 列表加载。

- 在线 Mod 列表请求超过 5 秒会自动停止；优先显示本地缓存，没有缓存时保留当前内容并关闭加载遮罩。
- 修复方块模式在窗口或详情面板宽度变化后沿用旧宽度、导致卡片偏左和右侧留白的问题。

### English

- Character lists now show the local catalog cache first and keep stale data visible while refreshing it in the background after 30 days.
- Character avatars use the disk cache first, with at most three uncached avatar downloads running concurrently.
- Obsolete avatar tasks are cancelled when switching repositories, changing language, or rebuilding the character rail.
- Image requests retry with backoff after `429` responses or temporary server errors.
- Manually refreshing online mods also refreshes the character catalog in the background without blocking the mod list.
- Online mod list requests now stop automatically after five seconds; local cache is preferred, otherwise current content is preserved and the loading overlay closes.
- Fixed grid view retaining a stale width after window or details-pane changes, which could shift cards left and leave unused space on the right.

## v3.4.0

### 中文

- 修复原神、崩坏：星穹铁道和绝区零角色分类可读取数量但不显示 Mod 的匹配问题。
- 三款游戏改用官方外服英文角色名辅助匹配，同时保留中文 Wiki 头像、中文名和角色链接。
- 恢复稳定的在线 Mod 列表读取方式，角色目录按月独立更新，避免与 Mod 请求互相阻塞。
- 修复三款游戏下载量偶发长期缺失的问题，缺失数据改为低频顺序补全并缓存 12 小时。
- 仓库总览卡片支持双击切换当前仓库。
- 发布 ZIP 改为仅包含应用与必要运行文件，不再附带 README 和使用文档。

### English

- Fixed character filters that returned category counts but displayed no mods for Genshin Impact, Honkai: Star Rail, and Zenless Zone Zero.
- Added official global English character names for matching while preserving Chinese Wiki avatars, names, and character links.
- Restored the stable online mod list loading path and kept the monthly character catalog refresh independent from mod requests.
- Fixed intermittently missing download counts by filling absent values sequentially at a low request rate and caching them for 12 hours.
- Added double-click repository switching on dashboard cards.
- Release ZIPs now contain only the application and required runtime files, without README or user-guide documents.

## v3.3.2

### 中文

- 修复原神、崩坏：星穹铁道和绝区零在线 Mod 下载量长期显示为 0 的问题。
- 缺失的下载量改为低频顺序补全，并使用 12 小时本地缓存，避免高并发和重复请求。
- 仓库总览卡片支持双击切换当前仓库，卡片内按钮不会误触发切换。

### English

- Fixed download counts remaining at zero for online Genshin Impact, Honkai: Star Rail, and Zenless Zone Zero mods.
- Missing counts are now filled sequentially at a low request rate and cached locally for 12 hours.
- Repository dashboard cards can now be double-clicked to switch the active repository without interfering with card buttons.

## v3.3.1

### 中文

- 原神、崩坏：星穹铁道和绝区零改为读取 HoYoWiki 官方外服英文角色名。
- 通过官方页面 ID 配对中英文角色，提升 GameBanana 角色分类匹配准确度。
- 修复选择中文角色后，已读取的英文分类 Mod 被界面二次过滤隐藏的问题。
- 保留中文 Wiki 的头像、中文名和链接，未配对角色继续使用原翻译回退。
- 角色目录仍按月独立缓存，不增加 GameBanana 列表请求并发。

### English

- Added official HoYoWiki English character names for Genshin Impact, Honkai: Star Rail, and Zenless Zone Zero.
- Paired Chinese and English names by official entry page ID for more accurate GameBanana category matching.
- Fixed loaded mods being hidden by a second exact-name filter when a Chinese character was selected.
- Preserved Chinese Wiki avatars, names, and links, with the existing translation fallback for unmatched entries.
- Kept the character catalog on its independent monthly cache without increasing GameBanana list concurrency.

## v3.3.0

### 中文

- 在线 Mod 页面重构为角色、Mod 列表与详情预览三栏布局，并可在中小窗口自动调整。
- 接入终末地 Wiki 角色头像与中英文名称，角色筛选直接读取对应 GameBanana 分类。
- 角色筛选后每页完整显示在线结果，修复分类数组越界、漏项和数据读取不完整问题。
- 优化 Mod 卡片、独立详情面板、预览区域与操作按钮，修复滚动条遮挡内容的问题。
- 应用显示版本、主程序、启动器和本地更新组件统一提升至 `v3.3.0`。

### English

- Reworked Online Mods into character, mod-list, and detail-preview columns with responsive behavior on smaller windows.
- Added Endfield Wiki character avatars and bilingual names mapped to the corresponding GameBanana categories.
- Character filters now show complete page results and avoid category index, missing-item, and incomplete-data errors.
- Improved mod cards, the independent details panel, image previews, and action buttons, and fixed scrollbars covering content.
- Advanced the displayed version, main app, launcher, and local update agent to `v3.3.0`.

## v3.2.1

### 中文

- 重构在线 Mod 浏览布局，宽屏使用角色、Mod 列表和详情三栏，中小窗口自动调整角色栏方向。
- 接入终末地 Wiki 角色头像与中英文名称，并将角色映射到对应 GameBanana 分类。
- 角色筛选后每页完整显示在线结果，修复部分分类的数组越界、漏项和读取不完整问题。
- 优化 Mod 卡片、详情预览和操作按钮排版，修复滚动条覆盖角色或 Mod 内容的问题。

### English

- Reworked online browsing into character, mod-list, and details columns, with a responsive character rail on smaller windows.
- Added Endfield Wiki character avatars and bilingual names mapped to the corresponding GameBanana categories.
- Character filters now show complete page results and avoid category index, missing-item, and incomplete-data errors.
- Improved mod cards, detail previews, and action buttons, and fixed scrollbars covering character or mod content.

## v3.2.0

### 中文

- 设置页改为统一的 Fluent 行式布局，新增舒适/紧凑界面密度与减少动态效果选项。
- 自动记住窗口位置、窗口大小和界面密度，并在启动时将窗口恢复到可用屏幕范围内。
- 在线 Mod 图片加入本地缓存与自动清理机制，提升重复浏览速度并减少网络调用。
- 预览图查看器支持滚轮缩放和拖动查看，文件夹列表新增右键快捷操作。
- 继续统一浅色/深色模式下按钮、列表项、焦点和选中状态的视觉表现。

### English

- Reworked Settings into consistent Fluent rows and added comfortable/compact density plus reduced-motion options.
- Added window size, position, and density persistence with safe on-screen restoration.
- Added a self-trimming local cache for online mod images to improve repeat browsing and reduce network calls.
- Added wheel zoom and panning to the preview viewer plus right-click actions for local folder lists.
- Further unified button, list-item, focus, and selection visuals across light and dark themes.

## v3.1.3

### 中文

- 新增本地 ZIP 更新：把规范命名的新版发布包放入现有程序文件夹，启动后即可自动识别并确认安装。
- 更新在主程序退出后由独立组件完成，并在完成后自动重新启动程序。
- 更新时保留 `config.ini` 与 `beta-shell.json`，仓库、路径、语言、主题、快捷键和 Mod 链接配置不会被发布包覆盖。
- 新增更新包结构和解压路径安全校验；替换失败时会尝试回滚原文件。

### English

- Added local ZIP updates: place a correctly named newer release package in the existing app folder, then launch the app to detect and install it.
- Updates are applied by a separate component after the main app exits, followed by an automatic restart.
- `config.ini` and `beta-shell.json` are preserved, so repositories, paths, language, theme, shortcuts, and mod links are not overwritten.
- Added package-structure and safe-extraction validation, with best-effort rollback if file replacement fails.

## v3.1.2

### 中文

- 修复浅色模式下快捷键输入框在普通或焦点状态错误使用深色主题资源的问题。
- 快捷键输入框的背景、边框和只读文字现在会严格跟随应用当前主题。

### English

- Fixed the shortcut input incorrectly using dark-theme resources in light mode.
- Shortcut backgrounds, borders, and read-only text now consistently follow the active app theme.

## v3.1.1

### 中文

- 修复设置页更新按钮没有显示文字的问题。
- 修复英文模式下在线 Mod 页的刷新、上一页和下一页按钮仍显示中文的问题。
- 修复浅色模式导航按钮错误使用深色主题资源的问题，并统一选中边框颜色。
- 统一按钮文案刷新逻辑，确保切换语言和在线加载结束后保持正确语言。

### English

- Fixed the missing label on the Settings update button.
- Fixed Chinese refresh, previous-page, and next-page labels appearing in the English online-mod interface.
- Fixed navigation buttons incorrectly using dark-theme resources in light mode and standardized selected borders.
- Centralized button-label refresh logic so language changes and completed online loads keep the correct language.

## v3.1.0

### 中文

- 新增按仓库配置在线来源、目标游戏和 GameBanana 皮肤分类 ID。
- 不同仓库现在可以使用不同游戏和分类的在线 Mod 数据。
- 放宽在线分类过滤规则，分类名称中包含 `Skins` 即可显示。
- 同步更新应用显示版本、文件版本、发布包和中英文文档。

### English

- Added per-repository online source, target game, and GameBanana skin category ID settings.
- Different repositories can now use online data from different games and categories.
- Relaxed online category filtering so any category containing `Skins` is accepted.
- Synchronized the app version, file version, release package, and bilingual documentation.

## v3.0.2

### 中文

- 新增 `.rar`、`.zipx`、`.zst`、`.tar.zst` 和 `.cab` 支持。
- 为 `.7z`、`.rar`、`.zipx` 和 `.cab` 接入 7-Zip 解压后端。
- 构建脚本会把可用的 `7z.exe`、`7z.dll` 和许可证复制到发布目录。
- 应用内 GitHub 更新检查和仓库链接切换到 `Integrated_Mod_Manager`。
- 发布包改用当前仓库名称。

### English

- Added support for `.rar`, `.zipx`, `.zst`, `.tar.zst`, and `.cab`.
- Added a 7-Zip extraction backend for `.7z`, `.rar`, `.zipx`, and `.cab`.
- Updated the build script to copy available `7z.exe`, `7z.dll`, and license files into the release output.
- Switched in-app GitHub update checks and repository links to `Integrated_Mod_Manager`.
- Renamed the release package to match the repository.

## v3.0.1

### 中文

- 新增专用应用图标，并应用到 WinUI 主程序和外层启动器。
- 重新整理 GitHub 发布包，使用户可以下载后直接解压运行。
- 修复启动器程序集标题、产品名称和启动失败提示中的乱码。
- 同步更新 README、英文说明、使用说明和更新报告。

### English

- Added a dedicated application icon to both the WinUI app and launcher.
- Reorganized the GitHub release package for direct download and extraction.
- Fixed mojibake in launcher metadata and startup error messages.
- Synchronized the README, English guide, user guide, and update report.

## v3.0

### 中文

- 将单仓库界面升级为多仓库工作区，支持创建、编辑、重命名、删除和切换仓库。
- 新增仪表板、仓库、在线、更新和设置五个主要模块。
- 每个仓库可独立保存 Mod 存储路径、目标路径、启动器和在线配置。
- 在线 Mod 模块接入 GameBanana 分类读取、分页、角色筛选、搜索和多种排序。
- 将在线 Mod 列表和固定详情预览拆分为独立面板，并扩大详情显示区域。
- 支持在线详情图片、说明、页面访问、下载和自动解压。
- 自动识别介绍文本中的付费、订阅、点赞解锁、延时公开等潜在访问要求。
- 尝试从在线介绍中提取快捷键，并按界面语言保存功能描述。
- 在线安装后记录来源链接、远程 ID、预览图和更新时间。
- 新增已安装 Mod 更新模块，支持手动或定期检查、查重和删除追踪记录。
- 新增 GitHub 软件版本检查和 Release 更新说明显示。
- 改进在线缓存与详情加载，减少重复网络请求。
- 优化浅色、深色主题和窗口自适应排版。
- 完成中文界面与文档乱码修复。

### English

- Upgraded the single-repository UI to a multi-repository workspace with create, edit, rename, delete, and switch actions.
- Added five primary areas: Dashboard, Repository, Online, Updates, and Settings.
- Each repository can store separate mod, target, launcher, and online settings.
- Connected Online Mods to GameBanana category loading, paging, character filters, search, and multiple sort modes.
- Split the online list and fixed detail preview into independent panels with a larger detail area.
- Added online images, descriptions, page access, downloading, and automatic extraction.
- Added best-effort detection of payment, subscription, like-unlock, delayed-public-release, and similar access requirements.
- Added best-effort shortcut extraction from online descriptions with language-aware action text.
- Recorded source links, remote IDs, previews, and update timestamps after online installation.
- Added tracked mod updates with manual or scheduled checks, deduplication, and record removal.
- Added GitHub app-version checks and Release note display.
- Improved online caching and detail loading to reduce repeated network requests.
- Refined light/dark themes and responsive window layouts.
- Fixed Chinese UI and documentation encoding issues.

## v2.2.8

### 中文

- 顶部导入操作统一为导入到当前选中的第一层文件夹。
- 保留第二层区域拖放压缩包并自动解压导入。
- 点击输入框外部可清除输入焦点。
- 快捷键输入框只在当前选中时显示高亮。

### English

- Standardized the top import action to target the selected first-level folder.
- Preserved drag-and-drop archive extraction in the second-level area.
- Added click-outside focus clearing for text inputs.
- Limited shortcut highlighting to the currently selected field.

## v2.2.7

### 中文

- 调整信息区与主模块宽度，使整体排版对齐。
- 修复深色模式下快捷键和描述输入框颜色不协调。
- 支持将压缩包拖到第二层区域后自动解压导入。
- 单顶层文件夹会直接导入；散文件或多目录会按压缩包名称创建 Mod 文件夹。

### English

- Aligned the information area with the main panels.
- Fixed shortcut and description field colors in dark mode.
- Added archive drag-and-drop extraction in the second-level panel.
- Preserved a single archive root folder, or created a mod folder for loose or multiple roots.

## v2.2.6

### 中文

- 将第一层“新建”和“重命名”改为模块右上角的小图标按钮。
- 将第二层“删除 Mod”改为模块右上角的小图标按钮。
- 为图标按钮增加悬浮提示和无障碍名称。
- 优化模块标题和操作区排版。

### English

- Moved first-level New and Rename actions to compact top-right icon buttons.
- Moved second-level Delete Mod to a compact top-right icon button.
- Added tooltips and accessibility names to icon buttons.
- Refined panel headers and action layouts.

## v2.2.5

### 中文

- 新增第一层文件夹创建功能。
- 新增第一层文件夹重命名功能。
- 新增删除选中第二层 Mod 的功能。
- 删除源 Mod 前增加确认弹窗，降低误删风险。

### English

- Added first-level folder creation.
- Added first-level folder rename.
- Added deletion of the selected second-level mod.
- Added a confirmation dialog before source-mod deletion.

## v2.2.4

### 中文

- 快捷键录入新增符号键支持，修复 `/` 等按键显示为数字的问题。
- 快捷键说明明确支持单键、组合键和符号键。
- 压缩包导入目标改为当前第一层分类。
- 新增 `.7z`、`.tar`、`.gz`、`.tgz`、`.bz2` 和 `.xz` 导入。
- 新增第一层文件夹搜索。
- 底部署名文案改为“工具作者”。

### English

- Added symbol-key capture and fixed `/` and similar keys appearing as numeric codes.
- Clarified support for single keys, combinations, and symbol keys.
- Changed archive import to target the current first-level category.
- Added `.7z`, `.tar`, `.gz`, `.tgz`, `.bz2`, and `.xz` import.
- Added first-level folder search.
- Changed the footer label to Tool Author.

## v2.2.3

### 中文

- 移除第二层列表名称后的数字列，只保留名称和复制状态。
- 长 Mod 名称改为单行省略显示，减少布局挤压。
- 快捷键与描述宽度调整为约 `1/3` 和 `2/3`。
- 修复快捷键输入重复字符的问题，并支持单键或组合键。
- 将路径名称改为“Mod 存储文件夹”和“目标文件夹”。

### English

- Removed the trailing number column from the second-level list.
- Added single-line ellipsis trimming for long mod names.
- Adjusted shortcut and description widths to approximately `1/3` and `2/3`.
- Fixed duplicate shortcut input and supported both single keys and combinations.
- Renamed path labels to Mod Storage and Target Folder.

## v2.2.2

### 中文

- 新增中文和英文界面切换。
- 调整布局，减少文本拥挤和说明裁切。
- 在预览图下增加每个 Mod 独立的链接模块和快速访问按钮。
- 固定预览区域尺寸，避免切换图片时界面跳动。
- 配置文件新增语言和 Mod 链接保存。

### English

- Added Chinese and English interface switching.
- Refined the layout to reduce text crowding and clipping.
- Added a per-mod link panel and quick-access button below the preview.
- Fixed the preview area size to prevent layout jumps.
- Added language and mod-link persistence.

## v2.2.1

### 中文

- 修复 README 和中文使用说明的 UTF-8 编码问题。
- 改进外层启动器启动 WinUI 3 主程序的行为和错误提示。
- 整理 GitHub 上传目录和用户发布包。

### English

- Fixed UTF-8 encoding in the README and Chinese user guide.
- Improved the outer launcher's WinUI startup behavior and error reporting.
- Cleaned up the GitHub source and user release layouts.

## v2.2

### 中文

- 完成 WinUI 3 图形界面迁移，采用更接近 Windows 11 Fluent 的视觉样式。
- 支持两层 Mod 目录浏览和第二层 Mod 整体复制或移除。
- 新增复制进度显示。
- 支持 ZIP 导入、图片预览和拖放图片设置预览。
- 每个 Mod 可保存最多 10 行快捷键与功能说明。
- 支持外部启动器路径配置与快速启动。
- 支持浅色和深色主题。
- 新增作者署名、版本信息、README、使用说明和许可证。

### English

- Completed the WinUI 3 migration with a Windows 11 Fluent-inspired interface.
- Added two-level mod browsing and whole-folder copy/remove toggling.
- Added copy progress reporting.
- Added ZIP import, image previews, and drag-and-drop preview assignment.
- Added up to 10 shortcut/action rows per mod.
- Added external launcher configuration and quick launch.
- Added light and dark themes.
- Added author credit, version metadata, README, user guide, and license.
