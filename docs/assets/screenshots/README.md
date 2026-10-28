# Homepage screenshots / 主页截图

## 4.6 pages / 4.6 各页面

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

Earlier screenshots (4.1 settings, 4.4 shortcut panel and overlay, 4.5 repository and state slots) were replaced by the 4.6 set. They remain in the repository history and under the release tags.

These images demonstrate layout, not real-game compatibility, rendering, or a completed preset restore. The fixture Mods are not playable assets.
