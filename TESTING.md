# Testing / 测试说明

The repository uses one verification command for local development and GitHub Actions.

本仓库在本地开发和 GitHub Actions 中使用同一个验证命令。

## Run all checks / 运行全部检查

Requirements: Windows, .NET SDK 8, Visual Studio 2022 or Build Tools with the Windows application build tools workload.

环境要求：Windows、.NET SDK 8，以及安装了“Windows 应用程序生成工具”工作负载的 Visual Studio 2022 或 Build Tools。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-all.ps1
```

The script runs tests serially, then builds the WinUI x64 application and compiles both launcher executables. Results are written to `artifacts/verification`.

脚本会串行运行测试，再构建 WinUI x64 应用并编译两个启动程序。结果位于 `artifacts/verification`。

## Test only / 只运行测试

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-all.ps1 -SkipBuild
```

## Covered areas / 覆盖范围

- Safe path resolution and directory traversal rejection / 安全路径解析和目录穿越拦截
- Update checksum parsing / 更新包校验和解析
- Update download writing, cancellation, handle release, and SHA-256 / 更新下载写入、取消、文件句柄释放和 SHA-256
- Update ZIP extraction, symbolic-link rejection, and payload discovery / 更新 ZIP 解压、符号链接拦截和负载识别
- Preserved configuration and managed-file manifest validation / 配置保留和受管文件清单验证
- WinUI x64 build, launcher compilation, updater compilation, and version consistency / WinUI x64 构建、启动器编译、更新器编译和版本一致性

GitHub Actions uploads TRX results, Cobertura coverage files, and verified build outputs for every run.

GitHub Actions 会为每次运行上传 TRX 测试结果、Cobertura 覆盖率文件和已验证的构建产物。
