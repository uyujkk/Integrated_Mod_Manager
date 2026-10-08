# Testing / 测试说明

[English README](../../README.md) · [中文首页](../../README.zh-CN.md) · [4.1 verification / 验证记录](../verification/v4.1.0.md)

## One entry point / 统一入口

Requires Windows, .NET SDK 8 and Visual Studio 2022/Build Tools with Windows application build tools. Local development and CI use the same command.
需要 Windows、.NET SDK 8 和 Windows 应用构建工具。本地与 CI 使用同一入口。

```powershell
./scripts/test-all.ps1 -ArtifactsDirectory artifacts/my-verification
./scripts/test-all.ps1 -SkipBuild -ArtifactsDirectory artifacts/my-tests
```

Use a new repository-local output directory: the script clears that directory. Never use user-data or running-app directories.
建议每次使用新的仓库内输出目录，脚本会清理该目录；不能指向用户数据或运行中的测试应用。

## Three suites / 三组测试

| Project | Scope / 范围 |
| --- | --- |
| Tests | Paths, presets/bundles, archive safety, scanning, downloads, metadata, adapters, layouts / 路径、预设与组合包、解压安全、扫描、下载、元数据、适配器及布局 |
| DataStoreTests | Actual isolated SQLite cache/index transactions / 真实隔离 SQLite 缓存与索引事务 |
| UpdaterTests | Checksums, downloads, extraction, configuration preservation and rollback / 校验、下载、解压、配置保留与回滚 |

Shared references live in `Tests/TestDependencies.props`. Each suite remains independent; safety cases are retained. Developer-only fixtures/harnesses are excluded from release payloads.
依赖统一放在 `Tests/TestDependencies.props`，保留三组独立测试和安全用例。开发测试数据及基准工具不随主程序发布。

| Assembly | Line minimum | Branch minimum |
| --- | ---: | ---: |
| Core | 90% | 85% |
| Data | 70% | 60% |
| UpdateAgent.Core | 60% | 55% |

Coverage selects each named assembly, not aggregate coverage. Four script self-checks cover missing/duplicate assemblies and misleading aggregates. Full verification also builds WinUI x64 and all four EXEs, checks versions, and validates the minimal ZIP. TRX, coverage and build/package outputs go under the selected directory.
覆盖率按程序集计算，四项脚本自测验证缺失、重复及汇总值干扰。完整验证另包含 WinUI x64、四个 EXE、版本一致性和精简 ZIP；结果位于所选输出目录。

## Release and native checks / 发布与窗口检查

Package only clean runtime files and user guides with `scripts/package-release.ps1`. Check the managed manifest, exact ZIP/SHA-256 sidecar pairing, versions and exclusion of settings, caches, test Mods and logs. Keep Developer Tools separate. Verify public metadata and downloaded hashes after publication.
只从干净运行目录打包，核对受管清单、精确同名校验附件、EXE 版本及用户数据排除；开发者工具独立。发布后核对公开下载与哈希。

Native windows, live websites, DPI, real games and actual user-installation upgrades are distinct checks, not proved by unit tests. Use isolated data and the [regression checklist](./Regression-Checklist.md). Historical journals remain in verification, including [opt11](../verification/combined-regression-opt11-20261007.md); this guide no longer repeats their running totals.
实际窗口、网站、DPI、真实游戏和用户安装目录升级分别验证，不能用单测代替。使用隔离数据和回归清单。阶段日志保留在验证目录，本页不再重复流水账。
