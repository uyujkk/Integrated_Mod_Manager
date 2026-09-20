# Integrated Mod Manager Developer Tools

独立的 WinUI 3 图形化开发者工具。该项目生成单独的 `IntegratedModManager.DeveloperTools.exe`，不会编译进 Integrated Mod Manager 主程序。

当前版本与主程序同步为 `v3.9.4` / `3.9.4.0`，但使用单独的发布包，不进入主程序自动更新载荷。

## 主要功能

- 选择并记忆 Integrated Mod Manager 主程序目录。
- 查看主程序版本、仓库数量、关键文件和缓存/备份占用。
- 解析 `beta-shell.json` 并逐仓库检查 Mod 仓库、目标目录、启动器和目录链接状态。
- 浏览 `beta-shell.json`、`config.ini`、启动日志、错误日志和更新日志。
- 在当前文件内搜索、校验 JSON、复制内容或另存为文本。
- 创建不修改主程序的配置快照。
- 导出替换主程序目录、用户目录和用户名的脱敏诊断 ZIP。
- 深浅色、中英文和可选的五秒自动刷新。

开发者工具不执行任意命令，不访问游戏进程内存，不注入游戏，也不会自动上传诊断数据。

## 构建

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-developer-tools.ps1
```

输出：

```text
DeveloperTools\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\IntegratedModManager.DeveloperTools.exe
```

生成独立发布包与 SHA-256：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\package-developer-tools.ps1
```

---

Standalone WinUI 3 graphical developer tools. This project produces a separate `IntegratedModManager.DeveloperTools.exe` and is not compiled into the main Integrated Mod Manager application.

Its current version follows the main app at `v3.9.4` / `3.9.4.0`, but it uses a separate release archive and is excluded from the main updater payload.

It provides target-directory selection, runtime and storage summaries, repository/path/link validation, configuration and log browsing, search, JSON validation, copy/save actions, read-only configuration snapshots, and sanitized diagnostic export. It does not execute arbitrary commands, access game memory, inject into games, or upload diagnostic data automatically.
