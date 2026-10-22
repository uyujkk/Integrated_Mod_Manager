# 快捷键面板独立测试版验收

日期：2026-10-21（America/New_York）。基线 v4.3.1 / f071d45，独立分支 feature/mod-shortcut-panel，显示版本 v4.3.1-hotkeys-test.1，软件自身更新关闭。未发布、未修改正式版目录或真实 Mod 库。

## 自动验证

- 四组件完整测试 1009 通过，0 失败、0 跳过：Core 911、Data 24、Updater 27、EndfieldLaunch 47。
- 新增 40 个 Core 用例，覆盖按键映射与约束、拒绝部分解析和系统组合、禁用 INI 过滤、角色匹配阈值与歧义、连续确认、空画面拒绝及框选区域提取。
- 全部覆盖率门槛通过；Core 行 96.0%、分支 88.1%。
- WinUI x64 Release 构建成功，日志无警告或错误。

## 隔离 Windows 界面与输入

使用两个虚构角色目录及自建 WinForms 接收窗口，不向真实游戏发送按键。

- 模块读取每个 Mod 的 4 个启用绑定；DISABLED-old.ini 不展示，鼠标绑定展示但禁用。
- 点击 Num5，接收窗口记录 DOWN NumPad5 / UP NumPad5；数字行 5 与小键盘 5 映射分开。
- 点击 Alt+Apps，接收窗口记录 DOWN Menu Alt → DOWN Apps Alt → UP Apps Alt → UP Menu None，释放顺序正确。
- Ctrl+Alt+K 两次切换，原生窗口检查分别确认浮窗 visible=False / True，前台始终是 Receiver。
- 框选模拟 HUD 区域并保存 A/B 参考，重启加载 2 个参考且自动跟随默认关闭。
- 最终构建开启自动跟随并返回 Receiver 后，识别 B 并自动选择 角色B/测试Mod；截图保留。返回管理器时提示等待目标游戏前台，保持当前 Mod。
- 非激活浮窗被 computer-use 窗口枚举过滤，浮窗自身按钮的直接鼠标点击仍需用户验收；本轮点击输入在主模块中完成。浮窗显示和不抢焦点由原生窗口状态核对。

完整日志位于开发根 artifacts/hotkey-panel-all-tests.log、hotkey-panel-build.log，界面、参考图和原生状态证据在 artifacts/hotkey-panel-acceptance。

## 尚未证明的部分

真实游戏接收、实际 HUD 的识别准确率、独占全屏、不同权限级别、复杂布局及长期运行尚未验证。游戏或 HUD 变化后需重新校准。条件表达式只显示、不求值，识别角色不会自动发送 Mod 快捷键。

打包时排除虚构仓库、目标窗口选择、参考图、配置、日志和本地数据库。测试包单独解压使用，不覆盖正式版。
