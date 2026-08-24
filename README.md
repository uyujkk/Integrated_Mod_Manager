<div align="center">
  <img src="./WinUI3/Assets/AppIcon.png" width="96" alt="集成化 Mod 管理器图标">
  <h1>集成化 Mod 管理器</h1>
  <p>面向 Windows 10/11 的 WinUI 3 Mod 整理、切换、浏览与更新工具</p>

  [![构建与测试](https://github.com/uyujkk/Integrated_Mod_Manager/actions/workflows/build-and-test.yml/badge.svg)](https://github.com/uyujkk/Integrated_Mod_Manager/actions/workflows/build-and-test.yml)
  [![最新版本](https://img.shields.io/github/v/release/uyujkk/Integrated_Mod_Manager?display_name=tag&sort=semver)](https://github.com/uyujkk/Integrated_Mod_Manager/releases/latest)
  [![许可证](https://img.shields.io/github/license/uyujkk/Integrated_Mod_Manager)](./LICENSE)
  [![平台](https://img.shields.io/badge/platform-Windows%20x64-0078D4)](#系统要求)

  **中文** · [English](./README.en.md)

  [下载最新版](https://github.com/uyujkk/Integrated_Mod_Manager/releases/latest) ·
  [快速手册](./docs/guides/快速使用手册.md) ·
  [完整手册](./docs/guides/用户手册.zh-CN.md) ·
  [更新日志](./docs/releases/CHANGELOG.md) ·
  [提交问题](https://github.com/uyujkk/Integrated_Mod_Manager/issues/new/choose)
</div>

> [!IMPORTANT]
> 本项目是非官方的爱好者工具，与 XXMI、任何游戏发行商及相关开发者均无隶属、授权、认可或赞助关系。使用 Mod 前，请遵守对应游戏、平台和 Mod 作者的规则。

## 项目简介

集成化 Mod 管理器以独立“仓库”管理不同游戏或不同 Mod 环境。它可以在本地仓库和游戏实际读取的目标目录之间复制或移除完整 Mod 文件夹，并集中管理预览图、来源链接、快捷键说明、在线下载、更新记录、配置方案和安装备份。

当前稳定版本为 **v3.8.5**，文件版本为 `3.8.5.0`，工具作者为 `uyujkk`。

## 核心功能

| 功能 | 说明 |
| --- | --- |
| 多仓库管理 | 为不同游戏、角色或 XXMI 配置保存独立路径与在线分类 |
| 本地 Mod 切换 | 使用两层目录浏览、搜索、复制、移除、创建、重命名和删除 Mod |
| 可选目录联接 | 让加载器与仓库使用同一份 Mod 目录；同角色切换时安全断开旧联接 |
| 压缩包导入 | 支持 ZIP、7Z、RAR、ZIPX、CAB、TAR 及常见压缩流格式 |
| 预览与说明 | 为每个 Mod 保存预览图、来源链接、快捷键和功能描述 |
| 在线 Mod 浏览 | 浏览 GameBanana 条目，按角色筛选，查看图片与说明并下载解压 |
| 配置方案 | 保存并应用整套已启用 Mod，不影响无法识别的目标目录 |
| 安装安全 | 安装前检测冲突，为复制、移除和方案切换创建可恢复备份 |
| 下载任务中心 | 查看下载和解压进度，取消任务、打开目录并清理记录 |
| 更新与诊断 | 检查 Mod 和软件更新，回滚失败更新，导出脱敏诊断报告 |
| 辅助功能 | 中文/English、浅色/深色、高对比度、键盘导航和缩放适配 |

## 下载与运行

1. 打开 [Releases 最新版本](https://github.com/uyujkk/Integrated_Mod_Manager/releases/latest)。
2. 下载 `Integrated_Mod_Manager-vX.X.X.zip` 和对应的 `.sha256` 校验文件。
3. 将 ZIP **完整解压**到一个可写文件夹，不要直接在压缩包内运行。
4. 双击根目录中的 `ModFolderCopier.exe`。
5. 创建或选择仓库，设置 Mod 存储文件夹、目标文件夹和可选启动器。

`ModFolderCopier.exe` 是启动入口，WinUI 主程序位于 `WinUI3/ModFolderCopier.WinUI.exe`。请保留发布包原有目录结构。

### SmartScreen 提示

当前可执行文件没有商业代码签名证书，Windows SmartScreen 可能显示“发布者未知”。请只从本仓库的 Releases 下载，不要为了运行本工具关闭 Microsoft Defender。源码、构建脚本、自动化测试和发布包校验流程均公开在本仓库中。

## 快速开始

1. 在“仓库”页面创建仓库。
2. 将“Mod 存储文件夹”设置为两层 Mod 目录的根目录。
3. 将“目标文件夹”设置为游戏或 Mod 加载器实际读取的 Mods 目录。
4. 选择第一层分类，再选择第二层 Mod。
5. 双击第二层 Mod 或使用复制按钮完成切换。

目标文件夹中不存在同名目录时，程序复制整个 Mod；已经存在同名目录时，再次操作会将它从目标文件夹移除。删除仓库中的源 Mod 是另一项独立操作，并会在执行前要求确认。

### 推荐目录结构

```text
Mod 存储文件夹
├─ 角色或分类 A
│  ├─ Mod A1
│  └─ Mod A2
└─ 角色或分类 B
   └─ Mod B1
```

第一层是角色、用途或其他分类；第二层是程序实际复制、移除和记录信息的完整 Mod 文件夹。

## v3.8.5 重要更新

- 新增可选的 Windows 目录联接部署，加载器对 Mod 文件夹内部文件的写入可直接保留在仓库；原复制模式继续保留。
- 同一角色切换到另一个链接 Mod 时自动、安全地断开旧联接；普通复制目录与其他角色不受影响。
- 修复新版 7-Zip 的 RAR 空链接字段误报、选择 Mod 导致程序崩溃，以及下载中心进度条倒退和乱跳。
- 明确跨 Mod `$变量` 保存的实验结论：v3.8.1/v3.8.2 原型暂时无法可靠通用于任意第三方 Mod，不包含在发布包中。
- 自动化验证扩展至 **99 项测试**，CI 同时检查覆盖率、WinUI x64 构建和精简发布包。

完整版本历史请查看 [CHANGELOG](./docs/releases/CHANGELOG.md)，当前版本摘要请查看 [更新报告](./docs/releases/更新报告.md)。

## 文档导航

| 文档 | 中文 | English |
| --- | --- | --- |
| 快速使用 | [快速使用手册](./docs/guides/快速使用手册.md) | [Quick Start](./docs/guides/Quick-Start.en.md) |
| 完整用户手册 | [详细中文手册](./docs/guides/用户手册.zh-CN.md) | [Complete User Guide](./docs/guides/User-Guide.en.md) |
| 更新历史 | [双语更新日志](./docs/releases/CHANGELOG.md) | [Bilingual Changelog](./docs/releases/CHANGELOG.md) |
| 跨 Mod 状态试验 | [试验路线与当前结论](./docs/research/跨Mod状态保存试验结论.md) | [Current conclusions](./docs/research/跨Mod状态保存试验结论.md) |
| 测试与构建 | [测试说明](./docs/development/TESTING.md) | [Testing Guide](./docs/development/TESTING.md) |
| 参与项目 | [贡献指南](./CONTRIBUTING.md) | [Contributing](./CONTRIBUTING.md) |
| 安全问题 | [安全策略](./SECURITY.md) | [Security Policy](./SECURITY.md) |

## 系统要求

- Windows 10 1809 或更高版本，推荐 Windows 11。
- 64 位 Windows（`x64`）。
- 在线浏览、翻译和更新检查需要网络连接。
- 发布包为自包含 WinUI 3 应用，普通用户通常不需要安装 .NET SDK 或 Visual Studio。
- 7Z、RAR、ZIPX 和 CAB 解压依赖发布包内的 7-Zip 文件，请完整解压发布包。

## 从源码构建

开发环境需要 Windows 10/11 x64、.NET 8 SDK、Visual Studio 2022 或 Build Tools 2022、MSBuild、Windows App SDK 和 Windows SDK。

```powershell
cmd /c build_winui.bat
```

运行完整测试、覆盖率检查、WinUI x64 构建和发布包验证：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-all.ps1
```

当前自动化验证包含 99 项测试。GitHub Actions 会在推送到 `main`、Pull Request 和手动触发时执行相同流程。详细信息见 [测试说明](./docs/development/TESTING.md)。

## 数据与安全

- 仓库路径、界面设置和在线缓存保存在本机，不会由本项目自动上传。
- 诊断报告会排除访问凭据并脱敏用户路径，但提交前仍应由用户检查内容。
- 本工具不会绕过付费、订阅、权限验证、验证码或 Mod 作者的访问限制。
- 不要在 Issue、截图或压缩包中公开本地配置、个人目录、访问令牌或其他敏感信息。
- 安全问题请按照 [SECURITY.md](./SECURITY.md) 的方式报告。

## 许可证与声明

本项目以 [MIT License](./LICENSE) 发布，版权所有 `uyujkk`。第三方组件保留各自许可证；发布包中的 7-Zip 文件附带其许可证文本。

本工具不包含游戏文件或 Mod 内容。游戏、角色、图片、Mod 和第三方服务的相关权利归各自权利人所有。
