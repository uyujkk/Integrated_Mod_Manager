# 贡献指南 / Contributing Guide

[中文](#中文) | [English](#english)

## 中文

感谢你帮助改进集成化 Mod 管理器。你可以提交 Bug、功能建议、文档修正或代码改进。

### 提交问题前

1. 使用最新 Release 确认问题仍然存在。
2. 搜索现有 Issues，避免重复提交。
3. 尽量使用仓库提供的 Bug 或功能建议模板。
4. 删除日志、截图和配置中的个人路径、用户名、令牌及其他敏感信息。

### 本地开发

开发环境需要 Windows x64、.NET 8 SDK、Visual Studio 2022 或 Build Tools 2022、MSBuild、Windows App SDK 和 Windows SDK。

```powershell
git clone https://github.com/uyujkk/Integrated_Mod_Manager.git
cd Integrated_Mod_Manager
cmd /c build_winui.bat
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-all.ps1
```

### Pull Request 要求

- 一个 Pull Request 只处理一组相关问题。
- 说明修改原因、用户可见变化和验证方法。
- 不要提交 `dist/`、`bin/`、`obj/`、本地配置、缓存、日志、令牌或用户 Mod。
- 新增或修复可测试逻辑时，请同步补充测试。
- 提交前运行 `scripts/test-all.ps1`，并确保没有测试或构建错误。
- 界面文字需要同时提供中文和英文，或说明暂未翻译的原因。

## English

Thank you for helping improve Integrated Mod Manager. Contributions may include bug reports, feature proposals, documentation corrections, or code changes.

### Before Opening an Issue

1. Confirm the problem still exists in the latest release.
2. Search existing issues to avoid duplicates.
3. Use the provided bug or feature template whenever possible.
4. Remove personal paths, usernames, tokens, and other sensitive data from logs, screenshots, and configuration excerpts.

### Local Development

Development requires Windows x64, the .NET 8 SDK, Visual Studio 2022 or Build Tools 2022, MSBuild, Windows App SDK, and Windows SDK.

```powershell
git clone https://github.com/uyujkk/Integrated_Mod_Manager.git
cd Integrated_Mod_Manager
cmd /c build_winui.bat
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-all.ps1
```

### Pull Request Requirements

- Keep each pull request focused on one related set of changes.
- Explain the motivation, user-visible behavior, and verification performed.
- Do not commit `dist/`, `bin/`, `obj/`, local configuration, caches, logs, tokens, or user mods.
- Add or update tests when changing testable behavior.
- Run `scripts/test-all.ps1` before submitting and resolve all test or build failures.
- Provide both Chinese and English interface text, or explain why a translation is not yet available.
