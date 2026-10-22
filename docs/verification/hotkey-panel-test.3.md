# 快捷键面板 test.3 验证

日期：2026-10-21（America/New_York）。基线 8808fe6 / test.2。未发布，不覆盖用户测试目录、正式版或游戏文件。

## 根因

- `F:\Hypergryph Launcher\games\Endfield Game\Endfield.exe` 清单声明 `requestedExecutionLevel level="requireAdministrator"`，游戏始终以高完整性级别运行。
- 管理器默认以中完整性级别运行。UIPI 会丢弃发往更高完整性窗口的 SendInput，且返回值与 GetLastError 都不反映丢弃，所以 test.1 显示“已发送（PID 32720）”而游戏无响应。
- 物理键盘不经过 UIPI；经典 osk.exe 具有 `uiAccess=true`，可跨完整性级别输入，与用户观察一致。
- test.2 已观察到读取游戏令牌被拒绝，但把它当作“未知”继续发送。本机验证：中完整性进程对更高完整性进程的 OpenProcess／OpenProcessToken 返回错误 5，对同级 explorer 可读。

## 修改

- 新增 Core `ShortcutPrivilegePolicy`：目标级别更高，或目标令牌拒绝访问（错误 5）且管理器未提权时，判定为会被丢弃并拒绝发送；管理器已提权、具有 UIAccess 或同级时允许。其他查询错误不阻断。
- 发送前给出明确原因，不再显示误导性的“已发送”。“快捷键”模块显示当前权限与所选窗口的判定结果。
- 新增“以管理员身份重启管理器”：通过 UAC 以 `--restart-after <pid>` 启动新进程，原进程正常关闭并保存配置；新进程最多等待 30 秒，待旧进程退出后再获取单实例键，避免激活请求被转回正在关闭的实例。拒绝 UAC 时保持当前进程。

## 自动回归

- Core 939（新增 15 项权限判定）、Data 24、Updater 27、EndfieldLaunch 47，合计 1037 通过、0 失败、0 跳过。test.2 中被游戏会话锁阻断的 4 项本轮在游戏未运行时全部通过。
- 覆盖率门槛通过；Core 行 96.0%、分支 88.1%。
- WinUI x64 Release 构建成功，0 警告 0 错误。

## 尚未证明

- 未在本轮实际启动游戏，也未点击 UAC 确认提权重启；游戏内响应需用户以管理员管理器确认。
- 提权后若仍无响应，下一步怀疑 AntiCheatExpert 过滤注入输入（经典屏幕键盘的输入同样带注入标记，用户反馈其可用，因此可能性较低）。
- 提权运行时无法从普通权限资源管理器拖放文件到管理器（UIPI 同理）。

日志：开发根 artifacts/hotkey-panel-test3-tests.log；测试结果在仓库 artifacts/verification/hotkeys-test.3。
