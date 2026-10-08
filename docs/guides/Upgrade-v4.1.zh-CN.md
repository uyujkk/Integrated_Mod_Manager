# 升级到 4.1.0

[English](./Upgrade-v4.1.en.md) · [组合预设指南](./Combination-Presets.zh-CN.md)

从官方 Release 下载 `Integrated_Mod_Manager-v4.1.0.zip` 和精确同名的 `.zip.sha256`。完整解压到可写目录，运行 `IntegratedModManager.exe`。保留全部目录结构；这是便携 ZIP，不是 MSI。

1. 退出管理器，备份现有应用目录。恢复预设前另备份 Mod 仓库、目标 Mods 目录和加载器的 `d3dx_user.ini`。
2. 发布后，4.0 正式版可从设置检查更新。手动迁移时解压到新目录，关闭两边应用，把自己的 `WinUI3/config.ini` 和 `WinUI3/beta-shell.json` 复制到对应位置；缓存和备份按需迁移，不用旧程序覆盖新运行文件。
3. 首次打开核对仓库路径、启动器和部署模式，不要让两个管理器同时操作同一目标。
4. 新增“导出组合包 / 安装组合包”，迁移保存的组合与完整 Mod 文件，不迁移机器路径。需要最新参数时先更新预设再导出。

不改原 Mod INI 默认值、不改无关加载器参数；恢复前关闭游戏与加载器。导入会校验文件但不会执行其中程序。分享前检查私人文件和作者许可；普通 Mod 压缩包不能作为组合包安装。

仍需要 Windows x64 和 .NET 8 x64 运行时。开发者工具独立保持 3.9.5，不要使用其校验附件验证主程序。历史 Beta 不支持自动更新，请只手动迁移自己的配置，不复制模拟仓库。

```powershell
Get-FileHash -LiteralPath ./Integrated_Mod_Manager-v4.1.0.zip -Algorithm SHA256
Get-Content -LiteralPath ./Integrated_Mod_Manager-v4.1.0.zip.sha256
```

哈希和文件名均应一致。更新失败请保留旧应用和日志，不要通过删除配置排查。
