# 内置终末地启动 / Integrated Endfield launch

**适用于尚未发布的下一版本。管理器 4.1.1–4.2.0 使用需要单独下载的 EndfieldBridge，说明见本文末尾。**
**For the next, unreleased version. Manager 4.1.1–4.2.0 used a separately downloaded EndfieldBridge; see the end of this page.**

## 中文

终末地仓库可以改用**内置终末地启动**：管理器以官方签名的 `Games.exe` 作为父进程直接创建游戏，需要 Mod 时再交给 XXMI / EFMI 注入。启动组件已内置，不再需要单独下载 EndfieldBridge。

入口：仪表板 → 当前仓库路径 → **启动方式**。选择按仓库保存，不影响其他游戏；打开设置页不会启动游戏。

### 设置

1. 选择**游戏程序**（游戏目录中的 `Endfield.exe`，同目录必须有 `Endfield_Data`）。
2. **官方启动器**可以留空：管理器会查找并校验官方 `Games.exe` 的签名。留空时会记住上次通过校验的文件，下次优先使用。
3. 需要 Mod 时，选择实际的 **XXMI 启动器**，打开“通过 XXMI / EFMI 加载 Mod”。
4. 点击**检测配置**：检查游戏路径、官方签名；开启 Mod 时还会检查 EFMI 适配是否完整。
5. 首次使用 Mod：先退出游戏和 XXMI，点击**备份并适配 EFMI**。管理器会备份 XXMI 配置，只改 EFMI 的七个启动字段，并记下首次恢复点。
6. 勾选风险确认后保存。之后“运行启动器”会弹出一次 UAC，然后在后台启动，不再弹出桥接窗口。

首次通过 XXMI 启动时，XXMI 会提示自定义命令未验证。请核对命令指向本管理器的 `ModFolderCopier.WinUI.exe --endfield-game-only --from-xxmi`，然后选择**保持**；选择“重置”会清空命令。

### 切回原有加载器

在设置页选择“原有加载器”只改变管理器的入口。如果之前适配过 EFMI，请先点击**恢复 EFMI**，把七个字段还原为适配前的值，再切换。
移动管理器目录前也要先恢复：XXMI 的自定义命令里写的是管理器的绝对路径。

### 从旧版独立 EndfieldBridge 迁移

打开设置页或运行启动器时，管理器会自动读取 `cache/optional-tools/EndfieldBridge-0.3.2-win-x64` 中的旧设置，并接管旧桥接器的 EFMI 恢复点。
如果之前用旧桥接器适配过 EFMI，请点击一次**备份并适配 EFMI**，把命令改为指向管理器，原始恢复点会保留；随后在 XXMI 中再选择一次“保持”。
确认无误后，可以自行删除旧的桥接器目录。

### 风险

内置启动改变了正常启动链；XXMI / EFMI 加载 Mod 涉及第三方 DLL 注入，可能导致账号处罚或封禁。签名校验、启动成功和测试通过都不代表官方认可或账号安全。不能接受风险时，请使用官方启动器，且不加载第三方 Mod。

启动需要管理员权限。设置、日志、恢复点和启动状态保存在管理器运行目录的 `backups/endfield-launch`，分享前请注意其中的本机路径。管理器不读取账号密码，不上传日志，不读取 XXMI 私钥。

### 许可证

启动组件源自 EndfieldBridge 0.3.2，以 AGPL-3.0-only 授权，其启动核心参考并改写自 endfield-nolauncher / Better-Endfield。包含该组件的管理器发布包整体按 AGPL-3.0-only 分发，仓库中其他文件仍是 MIT。详见 [LICENSING.md](../../LICENSING.md) 与 [组件 NOTICE](../../IntegratedModManager.EndfieldLaunch/NOTICE.md)。

## English

Endfield repositories can use the **integrated Endfield launch**. The manager creates the game directly under the officially signed `Games.exe`, and hands off to XXMI / EFMI when Mods are wanted. The component is built in, so EndfieldBridge no longer needs a separate download.

Open it from Dashboard → Current Repository Paths → **Launch method**. The choice is saved per repository and does not affect other games. Opening the settings never starts the game.

### Setup

1. Choose the **game executable**: `Endfield.exe` in the game folder, next to `Endfield_Data`.
2. The **official launcher** may stay empty. The manager then finds the official `Games.exe` and verifies its signature, and remembers the last verified file to try first next time.
3. For Mods, select the actual **XXMI launcher** and turn on *Load Mods through XXMI / EFMI*.
4. Use **Check** to verify the game path and the official signature. With Mods on, it also checks that the EFMI setup is complete.
5. Before the first Mod launch, exit the game and XXMI, then use **Back up and set up EFMI**. The XXMI configuration is backed up, only seven EFMI launch fields change, and the first restore point is kept.
6. Confirm the risk and save. **Run Launcher** then shows one UAC prompt and launches in the background, without a bridge window.

On the first XXMI launch, XXMI reports an unverified custom command. Check that it points to this manager's `ModFolderCopier.WinUI.exe --endfield-game-only --from-xxmi`, then choose **Keep**. Choosing *Reset* clears the command.

### Switching back

Choosing *Existing loader* only changes the manager's entry. If EFMI was set up, use **Restore EFMI** first, which returns the seven fields to their values before setup. Restore before moving the manager folder too, because XXMI's command contains the manager's absolute path.

### Migrating from the standalone EndfieldBridge

Opening the settings or running the launcher imports settings from `cache/optional-tools/EndfieldBridge-0.3.2-win-x64` and adopts the old bridge's EFMI restore point. If EFMI was set up with the old bridge, use **Back up and set up EFMI** once to point the command at the manager; the original restore point is kept. Then choose *Keep* in XXMI once more. Afterwards you may delete the old bridge folder yourself.

### Risks

The integrated launch changes the normal launch chain. Loading Mods through XXMI / EFMI involves third-party DLL injection and may lead to account penalties or bans. Signature checks, successful launches and passing tests do not establish official approval or account safety. If you cannot accept the risk, use the official launcher without third-party Mods.

Launching requires administrator rights. Settings, logs, restore points and launch status are stored under the manager's `backups/endfield-launch` folder and may contain local paths. The manager does not read account passwords, upload logs, or read XXMI private keys.

### License

The launch component derives from EndfieldBridge 0.3.2 and is licensed AGPL-3.0-only. Its launch core was adapted from endfield-nolauncher / Better-Endfield. Manager builds that include it are distributed as a whole under AGPL-3.0-only; other repository files remain MIT. See [LICENSING.md](../../LICENSING.md) and the [component NOTICE](../../IntegratedModManager.EndfieldLaunch/NOTICE.md).

## Manager 4.1.1–4.2.0 (standalone bridge) / 旧版说明

These versions offered a separately downloaded EndfieldBridge 0.3.2 (pinned, SHA-256 verified) installed under `cache/optional-tools`, with its own configuration window and `--launch` entry. Its release, risk statement and source remain at [uyujkk/EndfieldBridge](https://github.com/uyujkk/EndfieldBridge/releases/tag/v0.3.2).

这些版本提供需单独下载的 EndfieldBridge 0.3.2（固定版本、SHA-256 校验），安装在 `cache/optional-tools`，有独立的配置窗口和 `--launch` 入口。其发布、风险说明与源码仍在 [uyujkk/EndfieldBridge](https://github.com/uyujkk/EndfieldBridge/releases/tag/v0.3.2)。
