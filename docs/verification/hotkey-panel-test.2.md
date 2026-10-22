# 快捷键面板 test.2 验证

日期：2026-10-21（America/New_York）。基线 fd8af40 / test.1。未发布，不覆盖用户测试目录、正式版或游戏文件。

## 问题与修改

- 已确认旧版发送后调用 RefreshShortcutOverlay，替换整个 Content 和 ScrollViewer，丢失滚动位置。test.2 复用根布局、滚动容器与按键按钮，只更新状态文字；只有绑定集合变化才重建按键。浮窗按钮关闭点击抢焦点。
- 旧版使用虚拟键 SendInput 和约 80 毫秒短按，不能从普通窗口接收 WM_KEYDOWN 推断游戏可用。test.2 默认使用 KEYEVENTF_SCANCODE、目标线程键盘布局和正确 E0 扩展标志，保留虚拟键模式；短按默认 200 毫秒可调。
- 增加发送前完整性等级检查，拒绝已确认的低权限→高权限输入。未知权限不冒充可用。记录接受的按下／松开数量、键状态与前台窗口；状态不再直接称游戏输入成功。
- 本机经典 osk.exe 清单声明 uiAccess=true，而管理器清单为 false；普通管理器与管理员管理器并存。目标游戏令牌查询被拒绝，不能据此确定游戏完整性等级，也未确定现代触摸键盘无效的具体机制。

## 直接浮窗验收

独立虚构库共 63 个快捷键，发送目标为自建 Receiver，不向真实游戏发送测试按键。

- 滚动到末尾后点击 F6：滚动值发送前后均为 5796.666664123535，按钮实例保持，状态变化不跳顶部。
- F6：Vk=0、Scan=0x40、Flags=0x08；Receiver 有成对 DOWN/UP，KeyStateObserved=true。
- Right：Vk=0、Scan=0x4D、Flags=0x09，扩展键正确；成对接收，键状态为真。
- Num Lock 关闭时拒绝扫描码 Num5，并提示开启数字锁；用模块按钮开启后，Num5 为 Scan=0x4C / Flags=0x08，接收 NumPad5 而非数字行 5，键状态为真。验收后恢复了原 Num Lock 状态。
- 上述成功样本 DownAccepted=1 / UpAccepted=1，实际按压约 200 毫秒，目标前台保持 Receiver；输入诊断正常写入。

## 自动回归与限制

- Core 924、Data 24、Updater 27 通过，覆盖率门槛通过；Core 行 96.0%、分支 88.0%。新增 13 项扫描码包回归覆盖普通键、扩展键、小键盘差异、松开对称性与虚拟键兼容。
- EndfieldLaunch 43 通过、4 失败：均在检查活动启动会话时被已有游戏会话锁阻断，涉及设置保存、运行标记和 EFMI 设置。未终止用户游戏、未移除会话锁，也未将这轮完整回归记为通过。合计 1018 通过、4 失败、0 跳过。
- WinUI x64 Release 构建成功，日志无警告或错误。
- 游戏内响应仍待用户用新包确认；未宣称扫描码可以绕过权限或游戏自身输入限制。

日志：开发根 artifacts/hotkey-panel-test2-tests.log、hotkey-panel-test2-build.log；浮窗与输入证据在 artifacts/hotkey-panel-test2-acceptance。
