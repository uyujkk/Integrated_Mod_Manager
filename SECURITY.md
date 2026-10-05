# 安全策略 / Security Policy

[中文](#中文) | [English](#english)

## 中文

### 支持版本

安全修复只保证应用到最新发布版本。报告前请先确认问题能在 [最新 Release](https://github.com/uyujkk/Integrated_Mod_Manager/releases/latest) 中复现。

4.0 状态预设会在用户确认后写入选定的加载器 `d3dx_user.ini`。请先退出游戏与加载器并保留备份；参数文件可能包含多个 Mod 状态和个人路径，不要公开整文件。管理器不访问游戏进程内存或注入游戏，但不保证使用第三方 Mod 没有账号风险。

### 报告安全问题

请不要在公开 Issue 中发布可直接利用的漏洞细节、个人路径、访问令牌或用户数据。如果仓库已启用 GitHub Private Vulnerability Reporting，请优先使用 Security 页面中的私密报告入口；否则请先创建一个不包含漏洞细节的普通 Issue，请求作者提供私密联系方式。

报告建议包含：

- 受影响的版本与 Windows 版本。
- 可复现的最小步骤。
- 预期结果和实际结果。
- 影响范围与可能的利用条件。
- 已脱敏的日志或示例文件。

请不要测试不属于你的系统、绕过第三方服务权限，或在报告前公开漏洞利用方式。

## English

### Supported Versions

Security fixes are guaranteed only for the latest release. Confirm that the issue reproduces in the [latest GitHub Release](https://github.com/uyujkk/Integrated_Mod_Manager/releases/latest) before reporting it.

4.0 presets write to the selected loader's `d3dx_user.ini` after confirmation. Exit the game and loader and keep backups first. The shared file may contain multiple Mods' values and private paths; do not publish it in full. The manager does not access game memory or inject, but cannot guarantee that third-party Mod use is free of account risk.

### Reporting a Vulnerability

Do not publish directly exploitable details, personal paths, access tokens, or user data in a public issue. If GitHub Private Vulnerability Reporting is enabled, use the private reporting option on the Security page. Otherwise, open a public issue without vulnerability details and ask the maintainer for a private contact method.

A useful report includes:

- The affected app and Windows versions.
- Minimal reproduction steps.
- Expected and actual behavior.
- Impact and required exploitation conditions.
- Sanitized logs or sample files.

Do not test systems you do not own, bypass third-party permissions, or disclose exploit details before the report is addressed.
