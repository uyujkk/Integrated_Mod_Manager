# Testing / 测试说明

[中文说明](../../README.md) | [English README](../../README.en.md) | [文档索引 / Documentation Index](../README.md)

The repository uses one verification command for local development and GitHub Actions.

本仓库在本地开发和 GitHub Actions 中使用同一个验证命令。

## Run all checks / 运行全部检查

Requirements: Windows, .NET SDK 8, Visual Studio 2022 or Build Tools with the Windows application build tools workload.

环境要求：Windows、.NET SDK 8，以及安装了“Windows 应用程序生成工具”工作负载的 Visual Studio 2022 或 Build Tools。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-all.ps1
```

The script runs 99 tests in three suites, enforces coverage gates, builds the WinUI x64 application, compiles both launcher executables, and verifies a minimal release ZIP. Results are written to `artifacts/verification`.

脚本会串行运行三组共 99 项测试、执行覆盖率门槛、构建 WinUI x64 应用、编译两个启动程序，并验证精简发布 ZIP。结果位于 `artifacts/verification`。

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
- Minimal release ZIP contract: required executables, managed manifest, and exclusion of user state, caches, logs, PDBs, and manuals / 精简发布 ZIP 契约：必要程序、受管清单，以及排除用户状态、缓存、日志、PDB 和手册

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
