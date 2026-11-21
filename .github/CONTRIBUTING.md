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
cmd /c scripts\build_winui.bat
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\check-code-style.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-all.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-ui.ps1
```

- **代码风格**：`check-code-style.ps1` 按 `.editorconfig` 编译所有项目，有问题即失败。请在 `test-all` 之前运行，它的重新生成会清理共享的中间文件。
- **测试**：`test-all.ps1` 运行四组单元测试（含覆盖率门槛）、构建 WinUI x64 和启动器，并组装 `artifacts\verification\ReleaseSource` 与验证包。
- **界面测试**：`test-ui.ps1` 用 FlaUI 操作 `ReleaseSource`（或 `-Application <文件夹>`）的副本和 `tests/TestData` 的合成数据，`-Filter` 可以只跑一部分。测试会按全局快捷键，请在解锁的桌面上运行，期间不要操作鼠标键盘。代码里创建的控件请设置 `AutomationProperties.AutomationId`。
- **在线样例**：环境变量 `IMM_ONLINE_FIXTURES` 指向 `tests/TestData/OnlineFixtures` 时，所有在线请求都由本地样例应答，一个也不会发到网上；界面测试和截图会自动设置。不要在测试以外使用。
- **发布**：版本 PR 合并后推送 `vX.Y.Z` 标签，`release.yml` 会测试、打包、从上一版升级并回滚、运行界面测试，并创建带主程序和开发者工具两个压缩包的草稿 Release，检查后手动发布；发布会触发 `discord-release.yml`。见[测试说明](../docs/development/TESTING.md)和[软件结构](../docs/development/Software-Structure.md)。
- **开发者工具**：`tools/DeveloperTools` 是单独发布的工具，用来检查记录、日志、缓存、Mod 按键和翻译。`scripts\build-developer-tools.ps1` 构建，`scripts\package-developer-tools.ps1` 打包；版本与主程序相同。见[开发者工具说明](../tools/DeveloperTools/README.md)。

### Pull Request 要求

- 一个 Pull Request 只处理一组相关问题。
- 说明修改原因、用户可见变化和验证方法。
- 不要提交 `dist/`、`bin/`、`obj/`、本地配置、缓存、日志、令牌或用户 Mod。
- 新增或修复可测试逻辑时，请同步补充测试。
- 提交前运行 `scripts/test-all.ps1`，并确保没有测试或构建错误。
- 界面文字放在 `src/WinUI3/Strings/<语言>.json`，需要同时提供中文、英文和俄语，或说明暂未翻译的原因；开发者工具的“翻译检查”页可以找出缺少的键和占位符问题。键只新增、不改名，不用的键在同一分支删除。
- 修改仓库视图或预设时，请检查宽/窄窗口、模块内滚动、单击不部署、旧配置兼容和其他 Mod 参数保留。不要使用真实游戏文件作为自动测试数据，也不要把文件测试表述为游戏验证。

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
cmd /c scripts\build_winui.bat
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\check-code-style.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-all.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-ui.ps1
```

- **Code style:** `check-code-style.ps1` builds every project with the `.editorconfig` rules and fails on any finding. Run it before `test-all`; its rebuild cleans the shared intermediate files.
- **Tests:** `test-all.ps1` runs the four unit-test suites (with coverage gates), builds WinUI x64 and the launcher, and stages `artifacts\verification\ReleaseSource` and a verification package.
- **UI tests:** `test-ui.ps1` drives a copy of `ReleaseSource` (or `-Application <folder>`) with FlaUI on the synthetic `tests/TestData`; `-Filter` runs a subset. The tests press a global hotkey, so use an unlocked desktop and leave the mouse and keyboard alone. Give controls built in code an `AutomationProperties.AutomationId`.
- **Online samples:** with `IMM_ONLINE_FIXTURES` pointing at `tests/TestData/OnlineFixtures`, every online request is answered from local samples and nothing goes to the network; the UI tests and screenshot script set it for you. Do not use it outside tests.
- **Releases:** after the version PR is merged, push the `vX.Y.Z` tag. `release.yml` tests, packages, upgrades from the previous release and rolls back, runs the UI tests, and creates a draft release with the app's and the Developer Tools' ZIPs; review and publish it by hand, which triggers `discord-release.yml`. See the [Testing Guide](../docs/development/TESTING.md) and [Software Structure](../docs/development/Software-Structure.md).
- **Developer Tools:** `tools/DeveloperTools` is a separately released tool for checking records, logs, caches, Mod keys and translations. Build it with `scripts\build-developer-tools.ps1` and package it with `scripts\package-developer-tools.ps1`; it carries the app's version. See the [Developer Tools README](../tools/DeveloperTools/README.md).

### Pull Request Requirements

- Keep each pull request focused on one related set of changes.
- Explain the motivation, user-visible behavior, and verification performed.
- Do not commit `dist/`, `bin/`, `obj/`, local configuration, caches, logs, tokens, or user mods.
- Add or update tests when changing testable behavior.
- Run `scripts/test-all.ps1` before submitting and resolve all test or build failures.
- UI text lives in `src/WinUI3/Strings/<language>.json`; provide Chinese, English and Russian, or explain why a translation is not yet available. The Developer Tools' Translations page finds missing keys and placeholder problems. Keys are added, never renamed; unused keys are removed in the same branch.
- For repository or preset changes, check wide/compact windows, panel-local scrolling, selection without deployment, old settings, and unrelated values. Use synthetic fixtures, not real game files; distinguish file tests from live game verification.
