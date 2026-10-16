# 可选终末地启动桥接 / Optional Endfield Bridge

**适用于管理器 4.1.1 及以上 / Available in manager 4.1.1 and later.**

## 中文

升级后，首次检测到终末地仓库时，管理器询问是否下载独立的 EndfieldBridge。
下载不是必选项；风险确认默认不勾选，回车不会默认同意下载。
选择按仓库保存，不影响其他游戏，也不覆盖原有加载器路径。

- **下载并使用**：确认风险后，下载固定版本 0.3.2 便携包（约 41 MB），通过 SHA-256 校验后安装。首次打开配置界面，不自动启动游戏。
- **使用原有加载器**：不下载。保留已配置的启动器；未配置时查找 XXMI 标准安装位置。找不到则手动选择，不猜测其他磁盘位置。
- **暂不设置**：保持当前选择，本次引导不再自动重复。以后可到“仪表板 → 当前仓库路径 → 启动方式”重新选择。

### 配置和启动

1. 在桥接器中选择实际游戏和 XXMI 路径。需要 Mod 时，先退出游戏和 XXMI，再按桥接器说明备份并适配 EFMI。
2. 核对 XXMI 的自定义命令授权提示。管理器不会替你适配、签名或确认授权。
3. 配置完成后，管理器的“运行启动器”使用桥接入口。打开管理器本身不会自动启动桥接器、加载器或游戏。
4. 桥接器缺失时停止并提示，不静默改用另一条启动链；配置不完整或与仓库加载器不同则打开配置界面。

**关闭桥接选项只改变管理器入口，不自动撤销 EFMI 适配。** 如果此前适配过 EFMI，请先在桥接器中恢复，再切回原有加载器。
移动或卸载桥接器前也必须处理恢复，因为自定义命令可能引用桥接器的绝对路径。

### 风险与维护

桥接器未经游戏官方认可，会改变正常启动链；XXMI / EFMI 加载第三方 Mod 也可能导致账号处罚或封禁。
不下载桥接器不意味着原有 Mod 加载方式没有风险。签名验证、SHA-256 校验和技术测试均不能保证账号安全。
不能接受风险时，请使用官方启动器且不加载第三方 Mod。

桥接器可能请求管理员权限。请检查来源和命令，不要关闭安全软件或添加排除作为安装步骤。
配置、日志和恢复信息留在桥接器自身的 `data` 目录，分享前注意其中的本地路径。

- [EndfieldBridge 0.3.2 发布与对应源码](https://github.com/uyujkk/EndfieldBridge/releases/tag/v0.3.2) · [完整风险说明](https://github.com/uyujkk/EndfieldBridge/blob/main/RISK.zh-CN.md)
- 桥接器为独立的 AGPL-3.0-only 程序。管理器不包含其启动实现，下载保留许可证、来源说明和对应源码入口。
- 固定下载已核对的版本，不自动跟随第三方 `latest`。SHA-256 校验只用于核对文件，不代表官方认可。
- 安装在管理器运行目录的 `cache/optional-tools/EndfieldBridge-0.3.2-win-x64`，作为用户数据保留，不随管理器主包分发。不要把整个 `cache` 当作图片缓存清理。
- 取消、校验或解压失败时不启用。已有目录和恢复数据不覆盖；重新安装前先按桥接器说明恢复 EFMI、备份数据并处理旧目录。

## English

The first eligible startup after upgrading offers the standalone EndfieldBridge.
Downloading is optional, with an unchecked risk acknowledgement and a non-installing default button.
The choice is saved per repository; other games and the original loader path remain unchanged.

- **Download and use:** explicitly consent, download pinned version 0.3.2 (about 41 MB), verify SHA-256 and install. First use opens configuration, not the game.
- **Use existing loader:** do not download. Preserve your configured launcher; when empty, check standard XXMI locations. Select a path manually if none exists.
- **Not now:** keep the current choice and dismiss the one-time introduction. Reopen under Dashboard → Current Repository Paths → Launch Method.

Configure the actual game and XXMI inside the bridge. For Mods, close the game and loader before backing up and setting up EFMI there.
Review XXMI command authorization yourself: the manager does not adapt EFMI, sign commands or approve prompts.
After configuration, **Run Launcher** invokes the bridge. Opening the manager does not automatically start the bridge, loader or game.
A missing bridge stops with an error; incomplete configuration or a loader mismatch opens configuration instead of launching.

**Restore EFMI in the bridge before switching back** if previously adapted. Switching off only changes the manager entry;
it does not undo adaptation. Also restore before moving or uninstalling the bridge because XXMI may reference its absolute path.

The unofficial launch chain and third-party DLL Mods may result in account penalties or bans. The existing Mod loader is not risk-free either.
Signatures, checksums and successful tests do not establish official authorization or account safety. If you cannot accept the risk,
use the official launcher without third-party Mods. Do not disable security protection or add exclusions as an installation step.
The bridge may request elevation; its local data/logs may contain private paths.

The bridge remains a separate AGPL-3.0-only program; its launch implementation is not incorporated into the manager.
The [pinned release](https://github.com/uyujkk/EndfieldBridge/releases/tag/v0.3.2) provides the corresponding source.
Its license, notices and source link are preserved. See the [risk statement](https://github.com/uyujkk/EndfieldBridge/blob/main/RISK.md).

Installed under the manager runtime's `cache/optional-tools/EndfieldBridge-0.3.2-win-x64`, it is preserved user data and excluded from the manager package.
Do not delete the entire cache as though it only held disposable images. Failures leave launch mode unchanged.
Existing files and recovery data are never overwritten; follow the bridge's restore and backup instructions before replacing an installation.
