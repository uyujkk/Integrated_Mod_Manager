# 快捷键面板 test.5 验证

日期：2026-10-21（America/New_York）。基线 3ac3219 / test.4。未发布，不覆盖用户测试目录、正式版或游戏文件。

## 目标

快捷键模块（主窗口“快捷键”页与浮窗）适配英语界面。

## 修改

- 发送服务（ShortcutInputService）与角色截屏（CharacterScreenCapture）原先抛出固定的“中文 / English”混合提示，英文界面也会显示中文。现按当前界面语言只显示一种语言（BetaLocalization.Resolve）。
- 面板和浮窗只在首次进入时创建，切换语言后不会刷新。新增 RebuildShortcutPaneForLanguage：切换语言时重建面板和浮窗，保留所选 Mod、目标窗口、单次／长按时长，浮窗原先打开则重新打开；出于安全，发送开关保持关闭，需重新开启。
- 浮窗标题按语言显示（IMM · Mod shortcuts／IMM · Mod 快捷键，保留 IMM 前缀）。
- WinUI ToggleSwitch 的开／关文字默认跟随 Windows 显示语言（中文系统显示“关”），现为面板内三个开关显式设置 On/Off。
- 修正不可发送原因中的全角括号；VK_CONTROL／VK_SHIFT 显示为 Ctrl／Shift，VK_BACK 显示为 Backspace。

## 实测（临时副本，用户仓库只读）

- 英文启动：主窗口快捷键页、浮窗、开关文字、不可发送原因、角色跟随区域均为英文；Mod 文件夹名属于用户数据，保持原名。
- 用户仓库 192 个绑定在英文模式下生成的描述均不含中文。
- 运行时切换（界面自动化：设置页语言按钮→返回快捷键页）：英文→中文、中文→英文两次，主窗口标题、浮窗标题与文字随之切换，所选 Mod（庄方怡\Zhuang Fangyi Mega Menu Mod!）保持不变，浮窗自动重新打开。

## 自动回归

- Core 967、Data 24、Updater 27、EndfieldLaunch 47，合计 1065 通过、0 失败、0 跳过；覆盖率门槛通过，Core 行 96.1%、分支 88.4%。WinUI x64 Release 构建 0 警告 0 错误。

## 尚未证明

- ColorPicker 等系统控件内部极少量文字可能仍跟随 Windows 语言。
