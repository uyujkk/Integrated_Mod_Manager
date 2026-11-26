# Homepage screenshots / 主页截图

## 4.8.6 pages / 4.8.6 各页面

The homepage, both READMEs and the user guides use this set. 主页、两份 README 和用户手册都使用这一组。

Captured on 2026-11-26 from the Release build of v4.8.6 (`fb3ec72`; `artifacts\verification\ReleaseSource`) in a private copy at `G:\IMM-Demo-4.8.6` with the synthetic test data, so the only paths shown belong to that folder. Window 1600 × 920 DIP at 150% scaling (2382 × 1371 physical pixels), dark theme, English UI. Each page was opened through UI Automation (navigation items, view buttons and tabs), the window was copied from the screen, and every picture was trimmed by 4 physical pixels on every side (2374 × 1363). Page tips were closed in the configuration so they do not cover the pages.

| File | Surface |
| --- | --- |
| `dashboard-v4.8.6.png` | Dashboard with the First steps card (three of five done), the active repository, quick actions and the paths section |
| `repository-v4.8.6.png` | Mod workspace: the sample character, the Mod card heading with rename, open folder, delete and Several at once, the two test Mods, and the details of `TestModA` |
| `state-slots-v4.8.6.png` | State Slots tab cropped from the Mod workspace (562 × 952): the Everyday slot marked current, the update and rename buttons, its two values |
| `combination-presets-v4.8.6.png` | Combination presets: two sample presets, the selected preset's Mods and value counts, Save & Restore |
| `feature-search-v4.8.6.png` | The search (Ctrl+K) in its detailed style: the start screen of tiles with the General, Dashboard and Repositories groups |
| `feature-search-results-v4.8.6.png` | The search after typing "slot", cropped to the search panel (1230 × 610); the text was typed as "SLOT" |
| `online-v4.8.6.png` | Online page in sample mode: categories, sample characters and Mod cards with Save for later, open page and download |
| `online-details-v4.8.6.png` | The same with the details drawer of "Sample Mod 01": Save author, All works by …, Download Latest Archive and Save for later |
| `shortcut-panel-v4.8.6.png` | Shortcuts page: the Mod, window and input cards, the status bar and two key-cap bindings |
| `shortcut-overlay-v4.8.6.png` | The overlay alone (604 × 686), opened with its Open button: the sample state slots and two key caps |
| `updates-v4.8.6.png` | Updates page, Mod updates view: two sample tracked Mods (never checked) with open folder, open page and More |
| `updates-downloads-v4.8.6.png` | Updates page, Download history view: one completed, one failed and one canceled sample |
| `updates-recovery-v4.8.6.png` | Updates page, Install and recovery view |
| `settings-v4.8.6.png` | Settings page with the search style, cropped above the feedback card (2374 × 1090), whose text names real games |

The configuration is the one `tools/ui-baseline/Capture-UiBaseline.ps1 -SampleData` writes, plus two sample combination presets ("Both test Mods" with two saved values, "Only Test Mod A"), the page tips marked closed and, for the dashboard, the First steps card turned on. The data is the repository's own synthetic test data (see the 4.8 section below); no game, loader, real Mod or online content was used, no target window was selected and no input was sent. `Capture-UiBaseline.ps1` itself produced black pictures on this machine that day, so the pictures were taken with the same steps by hand.

2026-11-26 用 v4.8.6 的 Release 构建（`fb3ec72`）在带模拟测试数据的独立副本 `G:\IMM-Demo-4.8.6` 中截取，画面里的路径只属于这个文件夹。窗口 1600 × 920 DIP、150% 缩放，深色主题、英文界面；用 UI 自动化切换页面、视图和标签页，从屏幕复制窗口区域，四边各裁掉 4 个物理像素。配置与 `Capture-UiBaseline.ps1 -SampleData` 相同，另加两个示例组合预设、关闭页面提示，仪表板截图打开了上手清单。设置页裁到反馈卡片上方（其中提到真实游戏名）。未连接游戏或加载器，未选择目标窗口，也未发送按键。

## 4.8 pages / 4.8 各页面

The homepage used this set until 4.8.6; the files stay for history. 4.8.6 之前主页使用这一组，文件保留作为历史。

Captured on 2026-11-10 from a Release build of the 4.8.0 release branch (`45eff17`) with `tools/ui-baseline/Capture-UiBaseline.ps1`, which copies the app and `tests/TestData` into `%TEMP%\imm-baseline` and never touches the user's own Mods or settings. Window 1600 × 920 DIP on a 3840 × 2160 screen at 150% scaling (144 DPI); the script works in physical pixels and scales the window size by the system DPI. Dark theme, English UI.

| File | Surface |
| --- | --- |
| `dashboard-v4.8.png` | Dashboard: the sample repository, quick-action cards, the paths section, one character and the repository card |
| `repository-v4.8.png` | Mod workspace: the sample character, its two test Mods with size and deploy state, and the details of `TestModA` |
| `online-v4.8.png` | Online page in sample mode: three sample characters and sample Mods with generated pictures |
| `command-palette-v4.8.png` | Command palette (Ctrl+K) over the Mod workspace, searching for "test" |
| `shortcut-panel-v4.8.png` | Shortcuts page: the Mod, window and input cards, the status bar and two key-cap bindings |
| `updates-v4.8.png` | Updates page: the status area, the three views and two sample tracked Mods (never checked) |
| `settings-v4.8.png` | Settings page, cropped below Diagnostics |
| `shortcut-overlay-v4.8.png` | The 4.8.2 overlay (taken separately, see below): title bar, Mod, window and send switch, state slots, two key caps and Save preset |

How to take them again:

```powershell
.\scripts\test-all.ps1   # or any Release build in artifacts\verification\ReleaseSource
.\tools\ui-baseline\Capture-UiBaseline.ps1 -OutputDirectory artifacts\readme-shots\final -Languages en-US -Themes dark -Sizes 1600x920 -Pages dashboard,repository,online,shortcuts,updates,settings,palette -SampleData
```

- `-SampleData` adds the synthetic tracked Mods, download history and install backups of `tests/TestData/SampleState`, and starts each copy with `IMM_ONLINE_FIXTURES` pointing at `tests/TestData/OnlineFixtures`. The repository uses the sample category 90001 and the game name "Sample Game", so every online request is answered from the samples or fails; nothing goes to the network.
- `palette` opens the command palette with Ctrl+K and types "test" before the picture is taken.
- The pictures are taken from the screen. The window's outer 1–3 pixels and rounded corners show whatever is behind it, so the homepage copies are trimmed by 4 physical pixels on every side. The Settings copy is also cut at 936 physical pixels from the top, above the feedback card, whose text names real games.
- Leave the desktop unlocked and the mouse still while the script runs.

The data is the repository's own synthetic test data: the character `TestCharacter` with `TestModA` and `TestModB`, the sample tracked Mods "Test Mod A (sample)" and "Test Mod B (sample)", and the Online samples "Sample Hero Aster/Briar/Cobalt" and "Sample Mod 01–12" by "Sample Author 1–4" with generated striped pictures. No game, loader, real Mod or online content was used, no target window was selected and no input was sent. The source name shown on the repository card is the app's own label for its online source, not content from it.

2026-11-10 使用 4.8.0 发布分支（`45eff17`）的 Release 构建和 `tools/ui-baseline/Capture-UiBaseline.ps1` 截图：脚本把应用和 `tests/TestData` 复制到 `%TEMP%\imm-baseline` 中运行，不碰用户自己的 Mod 和设置。窗口 1600 × 920 DIP，屏幕 3840 × 2160、150% 缩放（144 DPI），脚本按物理像素工作并按系统 DPI 换算窗口大小。深色主题、英文界面。

- `-SampleData` 加入 `tests/TestData/SampleState` 里的模拟已追踪 Mod、下载记录和安装备份，并让每次启动都把 `IMM_ONLINE_FIXTURES` 指向 `tests/TestData/OnlineFixtures`。仓库使用样例分类 90001 和游戏名 “Sample Game”，所有在线请求都由样例应答或直接失败，不访问网络。
- `palette` 会先按 Ctrl+K 打开命令面板并输入 “test” 再截图。
- 截图取自屏幕，窗口最外 1–3 像素和圆角会露出后面的内容，所以主页用的图片四边各裁掉 4 个物理像素；设置页另外裁到距顶部 936 个物理像素，去掉提到真实游戏名的反馈卡片。
- 截图时请保持桌面解锁、不要移动鼠标。
- 角色、Mod、在线样例和图片都是仓库自带的模拟数据；未连接游戏或加载器，未选择目标窗口，也未发送按键。仓库卡片上的来源名称是应用自己的标签，不是来自网站的内容。

## 4.8.4 Online page / 4.8.4 在线页

`online-v4.8.4.png` replaces `online-v4.8.png` on the homepage. It shows the 4.8.4 Online page: category tabs, subcategory chips, and the labelled Show, Authors & saved and Download options buttons. It was captured on 2026-11-24 from the Release build of the 4.8.4 preparation branch (`release/v4.8.4`, based on `11d8aa4`). The command was the one above, with `-Pages online` and the build in `artifacts\verification\v4.8.4\ReleaseSource`. Same window size, scaling, theme, language and synthetic samples, and the same 4-pixel trim. `online-v4.8.png` stays for history.

`online-v4.8.4.png` 取代主页上的 `online-v4.8.png`，显示 4.8.4 的在线页：分类标签、子分类标签，以及写明用途的“显示”、“作者与收藏”和“下载选项”按钮。2026-11-24 用 4.8.4 准备分支（`release/v4.8.4`，基于 `11d8aa4`）的 Release 构建截取。命令同上，改为 `-Pages online`，构建在 `artifacts\verification\v4.8.4\ReleaseSource`。窗口大小、缩放、主题、语言和模拟样例都相同，四边同样各裁掉 4 个物理像素。`online-v4.8.png` 保留作为历史。

## 4.8.2 overlay / 4.8.2 浮窗

`shortcut-overlay-v4.8.png` was captured on 2026-11-16 from the Release build of the 4.8.2 preparation branch (`release/v4.8.2`, based on `f85e2c4`) by a local UI test that starts an isolated copy with `tests/TestData`, adds the synthetic records of `tests/UI.Tests/OverlaySamples.cs` (copy records for `TestModA` and `TestModB`, so both count as enabled, and the state slots Battle, Casual and Everyday), opens the overlay with its hotkey and captures only the overlay window. 150% scaling, dark theme, English UI; trimmed by 4 physical pixels on every side. No target window was selected and no input was sent.

`shortcut-overlay-v4.8.png` 于 2026-11-16 用 4.8.2 准备分支（`release/v4.8.2`，基于 `f85e2c4`）的 Release 构建截取：本地界面测试启动带 `tests/TestData` 的独立副本，加入 `tests/UI.Tests/OverlaySamples.cs` 的模拟记录（`TestModA`、`TestModB` 的复制记录，使它们算作已启用，以及 Battle、Casual、Everyday 三个状态槽位），用快捷键打开浮窗，只截浮窗窗口。150% 缩放、深色主题、英文界面，四边各裁掉 4 个物理像素。未选择目标窗口，也未发送按键。

## 4.6 pages / 4.6 各页面

The homepage and the user guides now use the 4.8.6 set; these files stay for history. 主页和用户手册已改用 4.8.6 截图，这些文件保留作为历史。

Captured on 2026-10-27 from an isolated copy of the published v4.6.0 package (native window at 1600 × 920 DIP, 150% scaling, dark theme, English UI) with `PrintWindow`, so only the app's own window is in the images.

| File | Surface |
| --- | --- |
| `dashboard-v4.6.png` | Dashboard: the demo repository strip, paths, four characters, the repository card and quick actions |
| `repository-v4.6.png` | Repository page: four characters with avatars, Demo A's three Mods with thumbnails, tags, size and deploy state, and the details of `Outfit-Casual` |
| `state-slots-v4.6.png` | State Slots tab cropped from the repository page: the current slot and its three values |
| `command-palette-v4.6.png` | Command palette (Ctrl+K) over the repository page, searching for "out"; cropped |
| `shortcut-panel-v4.6.png` | Shortcuts page: toolbar, three key-cap bindings and the collapsed modules; cropped below the content |
| `updates-v4.6.png` | Updates page: six tracked Mods (not checked), the download history with outcome filters, install safety |
| `settings-v4.6.png` | Settings page: two-column overview |

The fixture is synthetic and lives in `G:\IMM-Demo-4.6`, the only path shown:

- Characters `Demo A`–`Demo D` with generated letter-on-colour avatars.
- Ten Mods with generated abstract preview images; their `mod.ini` files contain only demonstration `[Constants]` and `[Key…]` sections. Five are linked into a fixture `Mods` folder.
- `d3dx_user.ini`, the state slots, tags and download history were written by the fixture script.
- Source links use the placeholder `example.com`.
- The tracked Mods were never checked, so no request reached an online service for them.

The repository's game is a neutral "Demo Game". No game, loader, real Mod or online content was used, no target window was selected and no input was sent. The Online page is not shown. Apart from the crops noted above, images are unedited.

2026-10-27 使用已发布 v4.6.0 包的隔离副本截图（窗口 1600 × 920 DIP、150% 缩放、深色主题、英文界面），用 `PrintWindow` 只截取应用自身窗口。

- 角色、头像（生成的字母图）、Mod 与预览图（生成的抽象图）、`d3dx_user.ini`、状态槽位、标签和下载记录都是模拟数据。
- 来源链接使用占位地址 `example.com`；已追踪 Mod 从未检查，没有访问在线服务。
- 画面中的路径只属于演示文件夹 `G:\IMM-Demo-4.6`。
- 未连接游戏或加载器，未选择目标窗口，也未发送按键；不包含在线页。
- 除表中说明的裁剪外未做修改。

## 4.5 overlay / 浮窗

| File | Surface |
| --- | --- |
| `shortcut-overlay-v4.5.png` | Overlay at 100% background opacity, cropped below the last binding: save buttons, Refresh, state slot buttons and three demonstration bindings. The overlay did not change in 4.6. |

Captured on 2026-10-26 from an isolated Release build after v4.5.1 with the synthetic fixture `G:\IMM-Demo-4.5b`, in the same way as above. 2026-10-26 使用 v4.5.1 之后的隔离构建和模拟数据 `G:\IMM-Demo-4.5b` 截图，方法同上。

## 4.1 combination presets / 组合预设

| File | Surface |
| --- | --- |
| `combination-presets-v4.1.png` | User-provided original 1924 × 1440 PNG: demonstration saved sets and numeric-state counts; bundle export/install entry points |

Captured on 2026-10-07 from an isolated copy of the unmodified v4.1.0 application with synthetic fixture folders and seeded demonstration presets. No game, launcher, deployment target or loader-state file was connected. The image is the user's original PNG, copied byte-for-byte. 2026-10-07 使用独立 4.1.0 应用和模拟数据截图，未连接游戏、加载器或目标目录；图片为用户提供的原始 PNG，逐字节复制。

Earlier screenshots (4.1 settings, 4.4 shortcut panel and overlay, 4.5 repository and state slots) were replaced by the 4.6 set, then by the 4.8 set, and the homepage now uses the 4.8.6 set. They remain in the repository history and under the release tags.

These images demonstrate layout, not real-game compatibility, rendering, or a completed preset restore. The fixture Mods are not playable assets.
