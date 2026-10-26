# 升级到 4.1.1

> 归档文档，只适用于对应的旧版本；当前说明见[文档索引](../../README.md)。Archived: applies only to the version it names; see the [documentation index](../../README.md) for current guides.

[English](./Upgrade-v4.1.1.en.md) · [桥接器与风险](../zh-CN/终末地启动.md)

下载 `Integrated_Mod_Manager-v4.1.1.zip` 和精确同名的 `.zip.sha256`。这是便携 ZIP，不是 MSI；完整解压并保留全部目录结构，运行 `IntegratedModManager.exe`。需要 Windows x64 和 .NET 8 x64 运行时。

1. 在旧版“设置”检查更新，或手动解压到新目录。更新前退出应用并备份现有应用目录；不要让两个管理器操作同一目标。
2. 自动更新保留仓库、预设、路径、界面设置和用户缓存。手动迁移时，关闭两边应用，复制自己的 `WinUI3/config.ini` 和 `WinUI3/beta-shell.json`；按需迁移缓存与备份，不用旧程序覆盖新程序。
3. 有终末地仓库时，会出现一次桥接器选择。可以不下载，继续使用原有加载器。风险确认不默认勾选。
4. 选择下载后，桥接器独立校验并安装，首次只打开配置界面。配置完成后，“运行启动器”才按所选方式调用。

如果已经使用桥接器并适配 EFMI，移动目录、手动迁移或切回原有加载器前，先在桥接器中恢复 EFMI，备份其 `data`，再按桥接器说明重新配置。不要直接删除整个缓存目录。

第三方启动与 Mod 可能导致账号处罚或封禁；签名、校验和测试不保证账号安全。不接受风险时，使用官方启动器且不加载第三方 Mod。

```powershell
Get-FileHash -LiteralPath ./Integrated_Mod_Manager-v4.1.1.zip -Algorithm SHA256
Get-Content -LiteralPath ./Integrated_Mod_Manager-v4.1.1.zip.sha256
```

哈希和附件文件名均应一致。开发者工具保持独立版本，不要用它的校验文件验证主程序。更新失败时保留旧应用与日志，不删除配置排查。
