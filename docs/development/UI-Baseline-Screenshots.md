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
- **不要紧接着界面测试拍。** 界面测试会模拟按键；之后 Windows 认为“上一次输入是键盘”，应用启动时导航栏的搜索按钮会画出键盘焦点框，每张图都多出约 1000 个像素的差异。先用鼠标点一下桌面或等一会儿再拍。
- J0 基线的副本文件夹名是随机的（`imm-baseline-<随机>`），仪表盘路径框里能看到它；之后脚本改用固定的 `%TEMP%\imm-baseline`，所以与 J0 基线比较时仪表盘和首次安装图的路径框总会有差异，可以忽略。

## 对比脚本

```powershell
python -I .\tools\ui-baseline\compare_baselines.py artifacts\ui-baseline\j0-f6c2800 artifacts\ui-baseline\<新的一组> --diff artifacts\ui-baseline\<差异图目录>
```

逐张比较同名图片，每个颜色通道差超过 24 才算不同（吸收抗锯齿），忽略窗口边缘 3 像素；打印每张不同图片的差异像素数和范围，`--diff` 把差异像素用洋红色标在淡化的新图上。需要 Pillow 和 NumPy。

## 启动时间

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\ui-baseline\Measure-Startup.ps1 -Application <旧构建>,artifacts\verification\ReleaseSource -Runs 7
```

各构建轮流启动（同样在 `%TEMP%` 副本里，打开仪表盘），读 `startup.log`，输出中位数：`ConstructorMs` 是 `MainWindow constructor entered` 到 `completed`，`ToRootLoadedMs` 是进程启动到 `RootGrid loaded`。

## 当前基线

| 项目 | 值 |
| --- | --- |
| 位置 | `artifacts\ui-baseline\j0-f6c2800\`（本机，不提交） |
| 基于 | `f6c2800` 加 J0 的工具包引用（引用了但没有使用，不影响界面） |
| 系统 | Windows 11 26200，系统 DPI 144（150%），系统为深色模式 |
| 壁纸 | `C:\Windows\Web\Wallpaper\ThemeA\img20.jpg`（Windows 自带壁纸，填充方式 `WallpaperStyle=10`）。之后的截图对比都用这张壁纸 |

现在根背景不透明，壁纸只会从窗口边缘透出一点；J1T 露出 Mica 后壁纸才明显影响截图，所以从 J1T 起截图前先确认壁纸还是这一张（看 `manifest.json` 的 `Wallpaper`）。

## J1T 之后的基线（之后各页对比用这一组）

J1T（设计令牌）有意改变了整体外观，所以从 J2 起，各页的“纯移动”提交和外观提交都和这一组比较，不再和 J0 比较。

| 项目 | 值 |
| --- | --- |
| 位置 | `artifacts\ui-baseline\j1t-835b495\`（本机，不提交），86 张，日志无异常 |
| 基于 | 分支 `claude/ui-j1t-design-tokens` 的 `835b495` |
| 副本文件夹 | 固定的 `%TEMP%\imm-baseline`，路径框在各组之间一致 |
| 壁纸 | 仍是 `C:\Windows\Web\Wallpaper\ThemeA\img20.jpg`；现在 Mica 透出，壁纸会影响颜色 |
| 系统强调色 | 拍摄机器的强调色是红色，所以主按钮、选中描边和列表选中条都是红色。换一台强调色不同的机器拍，这些地方都会不同，属于预期差异 |
| 高对比度 | 还没有拍：需要在 Windows 设置里打开高对比度主题，脚本不改系统设置。打开后运行 `-Languages zh-CN -Sizes 1400x900 -Themes dark -Pages dashboard,settings` 补拍，拍完再关掉 |

与 J1（`j1-8910cb9`）相比，86 张全部不同，原因按类别：

- **背景**：根背景从不透明的 `#F6F7F9`／`#0D1117` 改成透明，露出 Mica；页面区多了一层 `LayerFillColorDefault`。深色图差异最大（36–82% 的像素），浅色图 1–25%。
- **强调色**：品牌蓝 `#2563EB` 删掉，改用系统强调色（这台机器是红色）。
- **卡片和控件**：卡片 12 → 8 px 圆角，控件、内嵌区域 10 → 4 px；按钮回到 Windows 默认高度和内边距；卡片、描边、次要文字改用 Fluent 的颜色。
- **文字**：区块标题 18 → 20 px；次要文字用次要文字颜色代替 0.8 透明度。
- **状态标签**：仪表盘、仓库卡片和 Mod 列表的状态标签统一样式。

布局没有改动。俄语图里已有的截断在 J1 里就存在，不是 J1T 引入的，留给对应页面的外观提交：在线页 1000×800 下标题行右侧的视图切换和刷新按钮被挤出；仪表盘 1400×900 下路径行的标签和“打开…”按钮被截断；在线页顶部状态文字、快捷操作按钮用省略号截断且没有提示。

## 对比方法

拍新的一组后，和基线逐张对比（文件名相同）。纯移动的提交应当只有文字光标、时间这类差异；PR 说明里列出有意改动的图片名。

## English summary

`tools\ui-baseline\Capture-UiBaseline.ps1` takes the stage J baseline: 86 screenshots (seven views including the what's-new dialog, in Chinese, English and Russian, at 1400×900 and 1000×800 DIP, light and dark, plus the first-install dialog in the system language at both sizes). It copies the built app from `artifacts\verification\ReleaseSource` and the synthetic `tests\TestData` into `%TEMP%`, rewrites that copy's settings before each launch, places the window topmost and captures its visible frame from the screen (BitBlt), so it never touches the user's Mods, settings, theme or wallpaper; the desktop must stay unlocked and untouched while it runs (about 20 minutes). Output goes to the git-ignored `artifacts\ui-baseline\<time>` with a `manifest.json` recording the commit, DPI, system theme, wallpaper and per-picture log errors. The current baseline is `artifacts\ui-baseline\j0-f6c2800` on the maintainer's machine, taken at 150% scaling with the Windows wallpaper `C:\Windows\Web\Wallpaper\ThemeA\img20.jpg`, which later comparisons should keep. Limits: the first-install dialog follows the Windows language and theme, and the Online page shows its empty state because the test repository has no online category.
