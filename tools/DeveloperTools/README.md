# Integrated Mod Manager Developer Tools

[中文](#中文) · [English](#english)

## 中文

独立的 WinUI 3 工具，帮助开发者、Mod 作者和翻译者查看集成化 Mod 管理器的数据。版本与主程序相同（现为 4.8.6）。从 4.8.3 起，每次发布都附带单独的附件 `Integrated_Mod_Manager_Developer_Tools-vX.Y.Z.zip`；更早的版本请从源码构建（见下面的“构建”）。它不进入主程序的安装包，也不会被主程序的更新器安装。

### 安装

把压缩包解压到主程序所在的文件夹（有 `IntegratedModManager.exe` 的那个）。压缩包里只有一个 `DeveloperTools` 文件夹，解压后是：

```text
IntegratedModManager.exe
WinUI3\
DeveloperTools\IntegratedModManager.DeveloperTools.exe
```

主程序的“设置 → 诊断与问题报告 → 开发者工具”和命令面板（Ctrl+K）里的“打开开发者工具”会从这里打开它；没有安装时会打开发布页。也可以把它放在任何地方单独运行，再在界面上选择主程序的数据文件夹（`WinUI3`）。

### 页面

| 页面 | 作用 |
| --- | --- |
| 概览 | 主程序版本、仓库数量、占用空间、关键文件、运行环境；主程序是否在运行。 |
| 仓库 | 每个仓库的 Mod 文件夹、目标文件夹、启动器和 `d3dx_user.ini` 是否存在，部署方式与目标里的目录链接是否一致。 |
| 状态记录 | 检查复制记录（4.8 起）、状态槽位和目标文件夹里的目录链接：记录指向的文件夹是否还在、槽位的 Mod 和图片是否还在、链接是否断开；可以打开对应的文件夹。 |
| 日志 | `startup.log`、`app-errors.log` 和更新器日志按时间合并，可按文件、级别、时间和文字筛选，“跟随”每 2 秒读取新内容。 |
| 缓存 | `cache` 文件夹里各项的大小。在线数据缓存可以清除（主程序会重新下载）；点赞用的 GameBanana 网页登录可以按主程序“清除 GameBanana 登录”的同样规则清除。都要先确认，主程序运行时不能清除，链接不会被跟随；索引数据库（含收藏）不能在这里清除。 |
| Mod 按键 | 给 Mod 作者：用主程序同样的解析器读取一个 Mod 的 INI 按键，显示快捷键页和浮窗会读到什么，以及每个按键能否发送、为什么不能。 |
| 翻译检查 | 给翻译者：比较一个文件夹里的语言文件（如源码的 `src\WinUI3\Strings`）和参考语言：缺少的键、多余的键、主程序无法填入的占位符、空文字。 |
| 文件 | 以文字查看设置文件和日志：搜索、检查 JSON、复制、另存。 |
| 诊断 | 导出脱敏的诊断 ZIP（替换本机路径和用户名；含 4.5 以后数据的统计：复制记录、槽位、浮窗设置、缓存大小、记录检查）；给设置文件做快照。 |

界面有中文、英文和俄语，浅色、深色或跟随系统，外观和主程序一致。

### 不会做的事

开发者工具默认只读：不修改主程序的设置、仓库或 Mod；唯一的写入是它自己的设置、诊断报告和快照，以及你确认后清除的缓存。它不运行任意命令、不读写游戏内存、不注入游戏、不上传任何内容。

### 命令行

供脚本和截图使用，不会保存：

```text
IntegratedModManager.DeveloperTools.exe --root <主程序数据文件夹> --page overview|repositories|records|logs|caches|modkeys|translations|files|diagnostics --language zh-CN|en-US|ru-RU --theme light|dark --mod <Mod 文件夹>
```

### 构建

```powershell
.\scripts\build-developer-tools.ps1
.\scripts\package-developer-tools.ps1 -OutputDirectory artifacts\developer-tools
```

打包脚本会检查版本与主程序一致、所有文件都在 `DeveloperTools` 文件夹里、不含主程序文件和调试符号，并写出 `.sha256`。发布工作流 `release.yml` 会把它作为单独的附件加到草稿 Release。界面文字在 `Strings\*.json`（中文、英文、俄语）；外观来自主程序的 `Themes/Tokens.xaml` 和 `Styles/*.xaml`。

## English

A standalone WinUI 3 tool that helps developers, Mod authors and translators look at Integrated Mod Manager's data. It has the app's version (4.8.6 now). From 4.8.3 on, every release has it as a separate asset, `Integrated_Mod_Manager_Developer_Tools-vX.Y.Z.zip`; for older versions, build it from source (see Build below). It is not part of the app's package, and the app's updater never installs it.

### Install

Extract the ZIP into the app's folder (the one with `IntegratedModManager.exe`). The ZIP holds a single `DeveloperTools` folder, so you get:

```text
IntegratedModManager.exe
WinUI3\
DeveloperTools\IntegratedModManager.DeveloperTools.exe
```

The app's Settings → Diagnostics and issue report → Developer Tools card and the command palette's (Ctrl+K) Open Developer Tools open it from there, or the release page when it is not installed. It also runs on its own from anywhere; then choose the app's data folder (`WinUI3`) in the tools.

### Pages

| Page | What it does |
| --- | --- |
| Overview | App version, repositories, storage, key files, runtime; whether the app is running. |
| Repositories | Whether each repository's Mod folder, target folder, launcher and `d3dx_user.ini` exist, and whether the deployment mode matches the directory links in the target. |
| State records | Checks the copy records (since 4.8), state slots and directory links in target folders: whether the folders a record names still exist, whether a slot's Mod and picture are still there, whether a link is broken; opens the folder to look at. |
| Logs | `startup.log`, `app-errors.log` and the updater's logs merged in time order, filtered by file, level, time and text; Follow reads new lines every 2 seconds. |
| Caches | The size of each item in the `cache` folder. Online data caches can be cleared (the app downloads them again); the GameBanana page's sign-in for likes can be cleared under the same rules as the app's Clear GameBanana sign-in. Both ask first, are not possible while the app runs, and never follow a link; the index database (with favorites) cannot be cleared here. |
| Mod keys | For Mod authors: reads a Mod's INI key bindings with the app's own parser, showing what the Shortcuts page and overlay read and whether each key can be sent, and if not, why. |
| Translations | For translators: compares the language files in a folder (such as a source checkout's `src\WinUI3\Strings`) with a reference language: missing keys, extra keys, placeholders the app cannot fill, empty texts. |
| Files | The settings files and logs as text: search, check JSON, copy, save a copy. |
| Diagnostics | Exports a sanitized diagnostic ZIP (this computer's paths and your user name replaced; with counts of the 4.5+ data: copy records, slots, overlay settings, cache sizes, record checks); snapshots the settings files. |

The interface is in Chinese, English and Russian, light, dark or following Windows, and looks like the app.

### What it never does

The tools are read-only by default: they do not change the app's settings, repositories or Mods; their only writes are their own settings, diagnostic reports and snapshots, and caches you confirm clearing. They run no arbitrary commands, never read or write game memory, never inject into a game and never upload anything.

### Command line

For scripts and screenshots; nothing is saved:

```text
IntegratedModManager.DeveloperTools.exe --root <app data folder> --page overview|repositories|records|logs|caches|modkeys|translations|files|diagnostics --language zh-CN|en-US|ru-RU --theme light|dark --mod <Mod folder>
```

### Build

```powershell
.\scripts\build-developer-tools.ps1
.\scripts\package-developer-tools.ps1 -OutputDirectory artifacts\developer-tools
```

The package script checks that the version matches the app's, that every file is in the `DeveloperTools` folder, and that no app files or debug symbols are included, then writes the `.sha256`. The release workflow `release.yml` adds it to the draft release as a separate asset. UI text is in `Strings\*.json` (Chinese, English, Russian); the look comes from the app's `Themes/Tokens.xaml` and `Styles/*.xaml`.
