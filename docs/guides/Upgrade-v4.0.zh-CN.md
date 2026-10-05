# 升级到 4.0.0

[English](./Upgrade-v4.0.en.md) · [组合与状态预设](./Combination-Presets.zh-CN.md) · [下载正式版](https://github.com/uyujkk/Integrated_Mod_Manager/releases/tag/v4.0.0)

## 新安装

需要 Windows 10 1809 或更高版本的 x64 系统和 .NET 8 x64 运行时。Windows App SDK 随包提供，不需要 Visual Studio 或 .NET SDK。

下载 `Integrated_Mod_Manager-v4.0.0.zip` 及同名 `.zip.sha256`，完整解压到可写目录，运行根目录 `IntegratedModManager.exe`。这是解压即用的应用包，不是 MSI 安装器。不要只复制 EXE，也不要在 ZIP 中运行。

## 从 3.x 正式版升级

1. 退出管理器，备份整个应用目录。涉及预设时，同时备份 Mod 仓库、目标 Mods 目录及加载器实际使用的 `d3dx_user.ini`。
2. 重新打开原正式版，在设置中检查软件更新，确认升级到 4.0.0；更新会保留配置、缓存和备份，并在失败时尝试回滚。
3. 也可单独解压正式包，关闭两个版本后，将旧 `WinUI3/config.ini`、`WinUI3/beta-shell.json` 和需要保留的 `WinUI3/cache`、`WinUI3/backups` 复制到相应位置。不要覆盖新程序文件。
4. 首次打开先检查当前仓库、源目录、目标目录、启动器和部署方式，再进行操作。

目录保持原路径时可以继续使用已有仓库与预设。迁移电脑、盘符、Mod 名称或命名空间后，请先修改实际路径并重新检查；旧状态不一定仍匹配。

## 从独立 4.0-beta 升级

Beta 不接入软件自动更新，请完整解压正式版到新目录。关闭两个版本，将自己的 `WinUI3/config.ini`、`WinUI3/beta-shell.json` 复制到正式版对应位置。两者使用相同的组合与状态槽位格式。

不要复制 beta 的模拟仓库、TestData 或测试应用配置去覆盖真实游戏路径。正式入口为 `IntegratedModManager.exe`，不是 `IntegratedModManager-Beta.exe`。不要同时运行两个管理器操作同一套部署或参数。

## 第一次验证组合预设

先用不重要的组合验证：部署 → 游戏内调整 → F10 落盘 → 保存组合及参数 → 退出游戏与加载器 → 检查恢复预览 → 恢复 → 重启并检查画面。确认其他 Mod 参数未变，Mod 原始 INI 没有被改写。

只支持可识别的数值型 `global persist`；不是任意 Mod 通用的热恢复。参数备份位于 `d3dx_user.ini` 旁，部署撤销不等于撤销参数。无法识别或发生冲突时不要强行手改预设绕过检查。

## 校验文件

在 ZIP 所在目录运行：

```powershell
Get-FileHash -LiteralPath .\Integrated_Mod_Manager-v4.0.0.zip -Algorithm SHA256
Get-Content -LiteralPath .\Integrated_Mod_Manager-v4.0.0.zip.sha256
```

哈希及文件名应精确一致。不要使用独立开发者工具的校验文件；该工具仍单独发布为 3.9.5，不包含在本包。

如升级失败，保留旧目录及日志，先尝试新目录完整解压，不要删除用户配置来排查。报告问题时脱敏个人路径和参数内容。
