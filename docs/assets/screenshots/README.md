# Homepage screenshots / 主页截图

## 4.5 avatars, state slots and overlay / 头像、状态槽位与浮窗

Captured on 2026-10-26 from an isolated copy of the Release build of `main` after v4.5.1 (the same application code later merged as PR #29; native windows, dark theme, English UI) with `PrintWindow`, so only the app's own windows are in the images.

| File | Surface |
| --- | --- |
| `repository-avatars-v4.5.png` | Repository page: three placeholder characters (`Demo A`–`Demo C`) with avatars, the avatar and multi-select buttons, and two linked Mods for `Demo A`, which allows several Mods |
| `state-slots-v4.5.png` | State Slots tab cropped from the same window: three saved slots, the current one selected with its values |
| `shortcut-overlay-v4.5.png` | Overlay at 100% background opacity, cropped below the last binding: save buttons, Refresh (next release), state slot buttons and three demonstration bindings |

The fixture is synthetic: avatars are generated letter-on-colour images stored as custom character avatars; the Mods contain only demonstration `[Constants]` and `[Key…]` sections; `d3dx_user.ini` and the saved slots were written by the fixture script. The only paths shown belong to the fixture folder `G:\IMM-Demo-4.5b`. No game, loader, real Mod or online content was used, no target window was selected and no input was sent. Apart from the crops noted above, images are unedited.

2026-10-26 使用 v4.5.1 之后 `main` 的 Release 构建隔离副本截图（与之后合并的 PR #29 应用代码相同），用 `PrintWindow` 只截取应用自身窗口。角色、头像（生成的字母图）、Mod、`d3dx_user.ini` 和状态槽位均为模拟数据；画面中的路径只属于演示文件夹 `G:\IMM-Demo-4.5b`。未连接游戏或加载器，未选择目标窗口，也未发送按键。除表中说明的裁剪外未做修改。

## 4.4 shortcut panel / 快捷键面板

Captured on 2026-10-22 from an isolated copy of the v4.4.0 Release build (native window, dark theme, English UI).

| File | Surface |
| --- | --- |
| `shortcut-panel-v4.4.png` | Shortcuts page bindings list for a synthetic Mod, including one mouse and one controller binding with their "Cannot send" reasons |
| `shortcut-overlay-v4.4.png` | Always-on-top overlay at 55% background opacity over the manager's own window |

The repository held three synthetic Mods whose `mod.ini` files contain only demonstration `[Key…]` sections; folder names such as `Demo-Operator-A` are placeholders. No game, loader, real Mod, online content or local path is shown, no target window was selected and no input was sent. The overlay was captured from the screen with only the manager's window behind it. Images are unedited.

2026-10-22 使用独立的 4.4.0 Release 构建截图，仓库中只有 3 个模拟 Mod，INI 仅含演示用按键节。未连接游戏或加载器，未选择目标窗口，也未发送按键；画面不含真实 Mod、在线内容或本地路径。浮窗截图时背后只有管理器自身窗口。截图未修改。

## 4.1 screenshots

Captured on 2026-10-07 from an isolated copy of the unmodified v4.1.0 application. These are native-window screenshots, not UI concepts or rendered mockups. The main English README and Chinese README share the same English-language images.

| File | Surface |
| --- | --- |
| `combination-presets-v4.1.png` | User-provided original 1924 × 1440 PNG: demonstration saved sets and numeric-state counts; bundle export/install entry points |
| `settings-sections-v4.1.jpg` | Appearance controls with section navigation in a non-maximized window |

Privacy/content review before publication:

- Only synthetic fixture folders and seeded demonstration presets were used. No actual online images, character avatars, downloaded Mods, source links or website caches were copied into the screenshot app.
- No game, launcher, deployment target or loader-state file was connected. No restoration or game operation was performed for these captures.
- Screenshots were visually inspected; automated captures also had their accessibility text checked for drive-letter paths. Path-bearing Dashboard/details/dialog surfaces were not included. `d3dx_user.ini` is a placeholder filename, not a local path.
- Only the app window was captured; no desktop, browser, terminal, file picker or other application was included. The settings image retains its original JPEG capture. The combination-presets image is the user's original PNG, copied byte-for-byte without resizing, recompression or format conversion, and is displayed directly as the homepage's main screenshot. The old repository screenshot has been removed. No screenshots were retouched or had game content obscured.
- The app configuration, SQLite database, fixture files, logs and executable copies stay in ignored local artifacts. They are not screenshot assets and are not uploaded with this documentation change.

These images demonstrate layout, not real-game compatibility, rendering, or a completed preset restore. The fixture Mods are not playable assets.

2026-10-07 使用独立 4.1.0 应用和模拟数据截图。未连接真实游戏、加载器或目标目录，不包含实际在线内容、本地路径或其他窗口；仅上传审核过的截图，配置、缓存和测试文件不上传。主页直接展示用户提供的组合预设截图，采用 1924 × 1440 原始 PNG，逐字节复制，不缩放、不重新压缩、不转码；旧工作台截图已删除，设置页保留原始 JPG。截图用于介绍界面，不作为真实游戏或参数恢复验证。
