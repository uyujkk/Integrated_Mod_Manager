# 界面基线截图 / UI baseline screenshots

2026-11-04，阶段 J0 第 1 项。阶段 J 每个 PR 都要和这组截图对比：“纯移动”提交应与基线一致，外观提交在 PR 里逐张说明差异。见[界面结构拆分计划](./UI-Structure-Plan.md)和[界面外观优化方案](./UI-Fluent-Design-Plan.md)。

## 怎么拍

先用 `scripts\test-all.ps1` 构建（它会把运行文件放到 `artifacts\verification\ReleaseSource`），然后：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\ui-baseline\Capture-UiBaseline.ps1
```

默认输出到 `artifacts\ui-baseline\<时间>`（`artifacts\` 已被 `.gitignore` 忽略，截图不提交）。常用参数：

| 参数 | 默认 | 说明 |
| --- | --- | --- |
| `-Application` | `artifacts\verification\ReleaseSource` | 含 `WinUI3\ModFolderCopier.WinUI.exe` 的文件夹 |
| `-OutputDirectory` | `artifacts\ui-baseline\<时间>` | 相对仓库根目录 |
| `-Languages` | `zh-CN,en-US,ru-RU` | |
| `-Sizes` | `1400x900,1000x800` | 窗口外框大小，单位是 DIP（与界面测试的 `Resize` 一致），脚本按系统 DPI 换算成像素 |
| `-Themes` | `light,dark` | 应用自己的主题设置 |
| `-Pages` | `dashboard,repository,online,shortcuts,updates,settings,whatsnew,firstrun` | `whatsnew` 是带“新功能”对话框的仪表盘；`firstrun` 是首次安装弹窗 |
| `-SettleSeconds` | `7` | 窗口出现后等待多久再截图 |

例如只重拍俄语设置页：`-Languages ru-RU -Pages settings`。

完整一组 86 张（6 个页面加“新功能”共 7 种 × 3 种语言 × 2 种尺寸 × 2 种主题 = 84 张，加 2 张首次安装），约 20 分钟。

## 它做什么、不做什么

- 把构建好的应用和 `tests\TestData`（合成的 Mod 仓库）复制到 `%TEMP%\imm-baseline-<随机>`，每张截图前重写这份副本里的 `beta-shell.json` 和 `config.ini`（仓库指向测试数据、页面、语言、主题、窗口大小），结束后删除副本。**不读写用户真实的 Mod 目录、`beta-shell.json`、`config.ini`。** 应用按程序目录区分单实例，副本和用户正在运行的应用互不影响。
- 深浅色用应用自己的 `ThemePreference` 设置切换。**不改系统主题、壁纸或其他系统设置**，只把它们记进 `manifest.json`。
- 每张图单独启动一次应用，把窗口临时置顶、放到 (40, 40)，用屏幕复制（`Graphics.CopyFromScreen`，即 BitBlt）截取 DWM 可见边框范围，然后关闭应用。所以截图时桌面要解锁，不要动鼠标键盘，也不要让其他窗口弹到最前。
- 截图尺寸是可见边框，不含不可见的缩放边框：150% 缩放下 1400×900 截出来约 2082×1341，1000×800 约 1482×1191。
- `manifest.json` 记录拍摄时间、提交号、Windows 版本、系统 DPI、壁纸路径、系统深浅色，以及每张图的像素尺寸和应用日志里的异常行数（不为 0 的要看 `startup.log`）。

## 已知限制

- **首次安装弹窗只有一种语言和主题。** 首次安装的判断是“`config.ini` 和 `beta-shell.json` 都不存在”，此时语言跟随 Windows 显示语言、主题跟随 Windows，脚本不改系统设置，所以每种尺寸只拍一张（文件名 `firstrun-system-system-<尺寸>`）。要拍其他语言，需要在对应显示语言的 Windows 账户下运行。
- **在线页没有在线数据。** 测试仓库没有在线分类，在线页显示的是空状态，不访问网络；有数据的在线页截图另需离线样例。
- 首次运行可能出现 Windows 防火墙或 SmartScreen 提示，遮住截图时重拍。

## 当前基线

| 项目 | 值 |
| --- | --- |
| 位置 | `artifacts\ui-baseline\j0-f6c2800\`（本机，不提交） |
| 基于 | `f6c2800` 加 J0 的工具包引用（引用了但没有使用，不影响界面） |
| 系统 | Windows 11 26200，系统 DPI 144（150%），系统为深色模式 |
| 壁纸 | `C:\Windows\Web\Wallpaper\ThemeA\img20.jpg`（Windows 自带壁纸，填充方式 `WallpaperStyle=10`）。之后的截图对比都用这张壁纸 |

现在根背景不透明，壁纸只会从窗口边缘透出一点；J1T 露出 Mica 后壁纸才明显影响截图，所以从 J1T 起截图前先确认壁纸还是这一张（看 `manifest.json` 的 `Wallpaper`）。

## 对比方法

拍新的一组后，和基线逐张对比（文件名相同）。纯移动的提交应当只有文字光标、时间这类差异；PR 说明里列出有意改动的图片名。

## English summary

`tools\ui-baseline\Capture-UiBaseline.ps1` takes the stage J baseline: 86 screenshots (seven views including the what's-new dialog, in Chinese, English and Russian, at 1400×900 and 1000×800 DIP, light and dark, plus the first-install dialog in the system language at both sizes). It copies the built app from `artifacts\verification\ReleaseSource` and the synthetic `tests\TestData` into `%TEMP%`, rewrites that copy's settings before each launch, places the window topmost and captures its visible frame from the screen (BitBlt), so it never touches the user's Mods, settings, theme or wallpaper; the desktop must stay unlocked and untouched while it runs (about 20 minutes). Output goes to the git-ignored `artifacts\ui-baseline\<time>` with a `manifest.json` recording the commit, DPI, system theme, wallpaper and per-picture log errors. The current baseline is `artifacts\ui-baseline\j0-f6c2800` on the maintainer's machine, taken at 150% scaling with the Windows wallpaper `C:\Windows\Web\Wallpaper\ThemeA\img20.jpg`, which later comparisons should keep. Limits: the first-install dialog follows the Windows language and theme, and the Online page shows its empty state because the test repository has no online category.
