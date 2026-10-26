# Testing / 测试说明

[English README](../../README.md) · [中文首页](../README.zh-CN.md) · [4.1.1 verification / 验证记录](../verification/v4.1.1.md)

## One entry point / 统一入口

Requires Windows, .NET SDK 8 and Visual Studio 2022/Build Tools with Windows application build tools. Local development and CI use the same command.
需要 Windows、.NET SDK 8 和 Windows 应用构建工具。本地与 CI 使用同一入口。

```powershell
./scripts/test-all.ps1 -ArtifactsDirectory artifacts/my-verification
./scripts/test-all.ps1 -SkipBuild -ArtifactsDirectory artifacts/my-tests
```

Use a new repository-local output directory: the script clears that directory. Never use user-data or running-app directories.
建议每次使用新的仓库内输出目录，脚本会清理该目录；不能指向用户数据或运行中的测试应用。

CI runs on every branch push (documentation-only pushes are skipped) and on pull requests to `main`.
CI 在每个分支推送时运行（只改文档的推送除外），指向 `main` 的 PR 也会运行。

## Code style / 代码风格

```powershell
./scripts/check-code-style.ps1
```

Builds every project with the `.editorconfig` rules enabled (for example IDE0005, unnecessary `using`) and fails on any finding. Shared compiler settings live in `Directory.Build.props`. CI runs this before `test-all`; locally, run it before `test-all` too, because its rebuild cleans the shared intermediate files.
按 `.editorconfig` 规则（例如 IDE0005 多余的 `using`）编译所有项目，有任何问题即失败。公共编译设置在 `Directory.Build.props`。CI 在 `test-all` 之前运行它；本地也请先运行它再运行 `test-all`，因为它的重新生成会清理共享的中间文件。

## Four suites / 四组测试

| Project | Scope / 范围 |
| --- | --- |
| tests/Core.Tests | Paths, presets/bundles, archive safety, scanning, downloads, metadata, adapters, layouts / 路径、预设与组合包、解压安全、扫描、下载、元数据、适配器及布局 |
| tests/DataStore.Tests | Actual isolated SQLite cache/index transactions / 真实隔离 SQLite 缓存与索引事务 |
| tests/UpdateAgent.Tests | Checksums, downloads, extraction, configuration preservation and rollback / 校验、下载、解压、配置保留与回滚 |
| tests/EndfieldLaunch.Tests | Isolated launch, configuration and lifecycle tests / 隔离启动、配置和生命周期测试 |

Shared references live in `tests/TestDependencies.props`. Each suite remains independent; safety cases are retained. Developer-only fixtures/harnesses are excluded from release payloads.
依赖统一放在 `tests/TestDependencies.props`，保留四组独立测试和安全用例。开发测试数据及基准工具不随主程序发布。

| Assembly | Line minimum | Branch minimum |
| --- | ---: | ---: |
| Core | 90% | 85% |
| Data | 70% | 60% |
| UpdateAgent.Core | 60% | 55% |

Coverage selects each named assembly, not aggregate coverage. Four script self-checks cover missing/duplicate assemblies and misleading aggregates. Full verification also builds WinUI x64 and all four EXEs, checks versions, and validates the minimal ZIP. TRX, coverage and build/package outputs go under the selected directory.
覆盖率按程序集计算，四项脚本自测验证缺失、重复及汇总值干扰。完整验证另包含 WinUI x64、四个 EXE、版本一致性和精简 ZIP；结果位于所选输出目录。

## Release and native checks / 发布与窗口检查

Package only clean runtime files and user guides with `scripts/package-release.ps1`. Which files count as user state is defined once in `scripts/release-file-policy.ps1`; `test-all` uses the same rule when it stages `ReleaseSource`, and starts from an empty folder each time. Check the managed manifest, exact ZIP/SHA-256 sidecar pairing, versions and exclusion of settings, caches, test Mods and logs. Keep Developer Tools separate. Verify public metadata and downloaded hashes after publication.
只从干净运行目录打包，核对受管清单、精确同名校验附件、EXE 版本及用户数据排除；开发者工具独立。哪些文件属于用户数据统一定义在 `scripts/release-file-policy.ps1`，`test-all` 准备 `ReleaseSource` 时也用同一规则，并且每次都从空目录开始。发布后核对公开下载与哈希。

Native windows, live websites, DPI, real games and actual user-installation upgrades are distinct checks, not proved by unit tests. Use isolated data and the [regression checklist](./Regression-Checklist.md). Historical journals remain in verification, including [opt11](../verification/combined-regression-opt11-20261007.md); this guide no longer repeats their running totals.
实际窗口、网站、DPI、真实游戏和用户安装目录升级分别验证，不能用单测代替。使用隔离数据和回归清单。阶段日志保留在验证目录，本页不再重复流水账。

## Shortcut performance / 快捷键性能

`tests/Performance/ShortcutUiBenchmark.cs` is compiled into a separate WinUI test build only with `ImmShortcutBenchmark=true`. It generates 100/200/300-binding fixtures, measures INI scanning, button creation, actual layout, overlay opening and status updates, and checks deployment replacement and stale-action removal. `LoaderFlushReceiver.cs` is a standalone synthetic F10 target compiled with the .NET Framework compiler and `IMM_FLUSH_RECEIVER`. Normal release builds include neither test entry point. Record foreground/input failures separately from the synthetic flush/capture contract; this never proves real-game input.

快捷键基准只在专用构建中运行；正常包不包含测试入口。Windows 拒绝前台切换时应记录真实按键验证未完成，不能把模拟写文件当成游戏响应。`scripts/test-preview-upgrade.ps1` 可用旧公开包的更新器，在新隔离目录验证升级、旧业务字段保留和副本回退。
