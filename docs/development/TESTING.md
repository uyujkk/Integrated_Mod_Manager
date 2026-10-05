# Testing / 测试说明

[中文说明](../../README.zh-CN.md) | [English README](../../README.md) | [文档索引 / Documentation Index](../README.md)

The repository uses one verification command for local development and GitHub Actions.

本仓库在本地开发和 GitHub Actions 中使用同一个验证命令。

## Run all checks / 运行全部检查

Requirements: Windows, .NET SDK 8, Visual Studio 2022 or Build Tools with the Windows application build tools workload.

环境要求：Windows、.NET SDK 8，以及安装了“Windows 应用程序生成工具”工作负载的 Visual Studio 2022 或 Build Tools。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-all.ps1
```

The 4.0.0 script runs 325 tests in three suites (Core 291, DataStore 7, UpdateAgent 27), enforces coverage gates, builds the WinUI x64 application, compiles the official launcher, legacy update-compatibility launcher, and update agent, and verifies a minimal release ZIP. Results are written to `artifacts/verification`.

4.0.0 脚本会串行运行三组共 325 项测试（Core 291、DataStore 7、UpdateAgent 27）、执行覆盖率门槛、构建 WinUI x64 应用、编译正式入口、旧版更新兼容入口和更新代理，并验证精简发布 ZIP。结果位于 `artifacts/verification`。

## Test only / 只运行测试

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-all.ps1 -SkipBuild
```

## Covered areas / 覆盖范围

- Safe path resolution and directory traversal rejection / 安全路径解析和目录穿越拦截
- Update checksum parsing / 更新包校验和解析
- Update download writing, cancellation, handle release, and SHA-256 / 更新下载写入、取消、文件句柄释放和 SHA-256
- Request cooldown, exponential backoff, server retry delay, and reset / 请求冷却、指数退避、服务器重试时间和状态重置
- Real SQLite cache expiry, upsert, favorites, repository isolation, and mod index replacement / 真实 SQLite 缓存过期、更新写入、收藏、仓库隔离和 Mod 索引替换
- Update ZIP extraction, symbolic-link rejection, and payload discovery / 更新 ZIP 解压、符号链接拦截和负载识别
- Successful update transaction, obsolete-file cleanup, preserved configuration, and failed-update rollback / 成功更新事务、旧文件清理、配置保留和失败回滚
- WinUI x64 build, launcher compilation, updater compilation, and version consistency / WinUI x64 构建、启动器编译、更新器编译和版本一致性
- Numeric persistent-state capture, namespace matching, targeted d3dx restoration, concurrent-write rejection, combination planning, and repository-view policies / 数值持久状态捕获、命名空间匹配、定向参数回写、并发写入拒绝、组合规划及仓库视图策略
- Minimal release ZIP contract: required executables, managed manifest, and exclusion of user state, caches, logs, and PDBs / 精简发布 ZIP 契约：必要程序、受管清单，以及排除用户状态、缓存、日志和 PDB

## Coverage gates / 覆盖率门槛

| Assembly / 程序集 | Line / 行 | Branch / 分支 |
| --- | ---: | ---: |
| `IntegratedModManager.Core` | 90% | 85% |
| `IntegratedModManager.Data` | 70% | 60% |
| `IntegratedModManager.UpdateAgent.Core` | 60% | 55% |

The verification command fails when a test, coverage gate, build, version check, or package contract fails.

测试、覆盖率门槛、构建、版本一致性或发布包契约任一失败，统一验证命令都会返回失败。

## Build a checked package / 生成并校验发布包

After `dist` has been built, the package script copies only runtime files, regenerates `.managed-files.txt`, creates and validates the ZIP, and writes a matching `.sha256` file.

完成 `dist` 构建后，打包脚本只复制运行文件，重新生成 `.managed-files.txt`，创建并校验 ZIP，同时写出配套的 `.sha256` 文件。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\package-release.ps1
```

GitHub Actions uploads TRX results, Cobertura coverage files, verified build outputs, and a verified release ZIP for every successful run.

GitHub Actions 会上传 TRX 测试结果、Cobertura 覆盖率文件、已验证的构建产物，并在成功时上传已校验的发布 ZIP。

## Release checks / 正式发布检查

The minimal CI ZIP proves the runtime contract; the public ZIP also includes guides and notices. Package with `scripts/package-release.ps1` from a clean source folder. Verify all four executable versions, the managed manifest, no user data, and an exact `<main ZIP>.sha256` sidecar. Keep Developer Tools separate. Verify the public latest endpoint and downloaded hash after publishing a non-prerelease.

CI 精简 ZIP 验证运行文件契约；公开包还提供指南与声明。使用干净源目录和 `scripts/package-release.ps1` 打包，检查四个 EXE 版本、受管清单、用户数据排除及与主程序 ZIP 精确同名的校验文件。开发者工具独立发布。正式版不能标为预发布，发布后核对 latest 接口与公开下载哈希。

Check Files/Covers/Presets in visible native windows with isolated fixtures, wide/compact layouts, and panel-local scrolling. Tests and hashes do not prove real-game rendering, live downloads, or self-update behavior in a user's actual installation. See the [4.0 verification record](../verification/v4.0.0.md).

原生布局应通过可见窗口及独立数据检查三视图、宽/窄窗口与模块内滚动。测试及哈希不代表真实游戏画面、在线下载或实际用户安装目录中的自更新已验证。详见[4.0 验证记录](../verification/v4.0.0.md)。
