<div align="center">
  <img src="./WinUI3/Assets/AppIcon.png" width="96" alt="集成化 Mod 管理器图标">
  <h1>集成化 Mod 管理器</h1>
  <p>面向 Windows 10/11 的 WinUI 3 Mod 整理、切换、浏览与更新工具</p>

  [![构建与测试](https://github.com/uyujkk/Integrated_Mod_Manager/actions/workflows/build-and-test.yml/badge.svg)](https://github.com/uyujkk/Integrated_Mod_Manager/actions/workflows/build-and-test.yml)
  [![最新版本](https://img.shields.io/github/v/release/uyujkk/Integrated_Mod_Manager?display_name=tag&sort=semver)](https://github.com/uyujkk/Integrated_Mod_Manager/releases/latest)
  [![许可证](https://img.shields.io/github/license/uyujkk/Integrated_Mod_Manager)](./LICENSE)
  [![平台](https://img.shields.io/badge/platform-Windows%20x64-0078D4)](#系统要求)

  **中文** · [English](./README.md)

  [下载最新版](https://github.com/uyujkk/Integrated_Mod_Manager/releases/latest) ·
  [快速手册](./docs/guides/快速使用手册.md) ·
  [完整手册](./docs/guides/用户手册.zh-CN.md) ·
  [更新日志](./docs/releases/CHANGELOG.md) ·
  [界面预览](#界面预览) ·
  [提交问题](https://github.com/uyujkk/Integrated_Mod_Manager/issues/new/choose)
</div>

> [!IMPORTANT]
> 本项目是非官方的爱好者工具，与 XXMI、任何游戏发行商及相关开发者均无隶属、授权、认可或赞助关系。使用 Mod 前，请遵守对应游戏、平台和 Mod 作者的规则。

## 项目简介

**当前正式版：[v4.1.0](https://github.com/uyujkk/Integrated_Mod_Manager/releases/tag/v4.1.0)。**支持将保存好的 Mod 组合、文件和状态参数导出为组合包，在其他仓库预览并安装。操作步骤见[组合预设指南](./docs/guides/Combination-Presets.zh-CN.md)。

集成化 Mod 管理器以独立“仓库”管理不同游戏或不同 Mod 环境。它可以在本地仓库和游戏实际读取的目标目录之间复制或移除完整 Mod 文件夹，并集中管理预览图、来源链接、快捷键说明、在线下载、更新记录、配置方案和安装备份。

主程序、启动器和更新器统一为 **4.1.0**（`4.1.0.0`）。工具维护者为 `uyujkk`，既有 Bandizip 备用解压功能由 [CaramelizedCUDA](https://github.com/CaramelizedCUDA) 贡献；此前的 [4.0-beta](https://github.com/uyujkk/Integrated_Mod_Manager/releases/tag/v4.0-beta) 继续独立保留。

## 界面预览

以下为 **4.1.0 隔离应用的真实截图**，只使用模拟目录和演示预设，未连接游戏或加载器。画面不包含实际在线 Mod 预览、角色头像或本地文件系统路径。截图采用英文界面，应用也支持中文。

**仓库工作台**：按角色浏览和搜索 Mod，在文件列表、封面和组合预设之间切换。紧凑窗口保留浏览空间，详情单独打开。

![仓库工作台：模拟目录，不显示本地路径](./docs/assets/screenshots/repository-workspace-v4.1.jpg)

<details>
<summary>组合预设与组合包</summary>

保存 Mod 组合及持久参数，查看预设成员，导出或安装组合包。恢复前另有预览和确认步骤；图中的演示预设不包含可运行的游戏 Mod。

![组合预设工作区：模拟成员及已保存参数数量](./docs/assets/screenshots/combination-presets-v4.1.jpg)

[查看组合预设使用方法](./docs/guides/Combination-Presets.zh-CN.md)

</details>

<details>
<summary>非最大化窗口中的设置页</summary>

总览放不下时自动改为分区设置，语言、主题、密度和托盘选项保持可见，不再挤压下方模块。

![非最大化窗口：设置页分区显示](./docs/assets/screenshots/settings-sections-v4.1.jpg)

</details>

## 4.1 新功能

- 新增组合包导出与安装：打包已保存的预设、Mod 文件及数值状态，在其他仓库预览安装。
- 文件完全相同的已有 Mod 可直接复用；内容不同的冲突项不覆盖。
- 优化仓库扫描与大型列表刷新。
- 修复在线预览图和角色头像显示，完善下载取消与安装状态即时刷新。
- 优化短窗口排版；设置总览放不下时自动改为分区设置，不再挤压裁切下方模块。

保存状态前先让加载器落盘（通常为 F10）；恢复前退出游戏和加载器。不改写 Mod INI 默认值或其他 Mod 的参数。不保证所有 Mod 兼容，也不承诺账号零风险。

[更新说明](./docs/releases/v4.1.0.md) · [升级指南](./docs/guides/Upgrade-v4.1.zh-CN.md) · [Wiki](https://github.com/uyujkk/Integrated_Mod_Manager/wiki)

## 核心功能

| 功能 | 说明 |
| --- | --- |
| 多仓库管理 | 为不同游戏、角色或 XXMI 配置保存独立路径与在线分类 |
| 本地 Mod 切换 | 使用两层目录浏览、搜索、复制、移除、创建、重命名和删除 Mod |
| 可选目录联接 | 让加载器与仓库使用同一份 Mod 目录；同角色切换时安全断开旧联接 |
| 压缩包导入 | 支持 ZIP、7Z、RAR、ZIPX、CAB、TAR 及常见压缩流格式 |
| 预览与说明 | 为每个 Mod 保存预览图、来源链接、快捷键和功能描述 |
| 仓库工作台 | 文件列表与封面视图可切换，集中显示选择、详情和部署操作 |
| 组合与状态预设 | 保存已启用 Mod 组合和数值型持久参数，先恢复组合，再回写对应的 `d3dx_user.ini` 参数 |
| 组合包迁移 | 导出所选预设及 Mod 文件；校验、预览、安装后可选择恢复组合和状态 |
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
4. 双击根目录中的 `IntegratedModManager.exe`。
5. 创建或选择仓库，设置 Mod 存储文件夹、目标文件夹和可选启动器。

`IntegratedModManager.exe` 是正式启动入口。发布包暂时保留 `ModFolderCopier.exe` 作为从 v3.8.5 自动更新时的兼容入口；WinUI 运行文件位于 `WinUI3/ModFolderCopier.WinUI.exe`。请保留发布包原有目录结构。

### SmartScreen 提示

当前可执行文件没有商业代码签名证书，Windows SmartScreen 可能显示“发布者未知”。请只从本仓库的 Releases 下载，不要为了运行本工具关闭 Microsoft Defender。源码、构建脚本、自动化测试和发布包校验流程均公开在本仓库中。

## 快速开始

1. 在“仪表板”创建或选择仓库。
2. 在仪表板将“Mod 仓库”设置为两层 Mod 目录的根目录。
3. 将“目标文件夹”设置为游戏或 Mod 加载器实际读取的 Mods 目录。
4. 选择第一层分类，再选择第二层 Mod。
5. 双击第二层 Mod 或使用详情里的部署操作完成切换。单击选择或切换视图不会部署 Mod。

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

## v3.9.5 更新摘要

- 感谢 [CaramelizedCUDA](https://github.com/CaramelizedCUDA) 的 [PR #4](https://github.com/uyujkk/Integrated_Mod_Manager/pull/4)：没有可用 7-Zip 时，可自动发现已安装 Bandizip 的 `bz.exe`，作为 RAR、7Z、ZIPX、CAB 等格式的备用解压工具；原有 7-Zip 优先级和 ZIP/TAR 路径保持不变。
- Bandizip 路径使用临时快照和压缩包链接/路径检查；不支持或可疑的归档会明确停止，不会绕过原有安全检查。tar/bz 单次调用设有 10 分钟超时，此限制也适用于原有 TAR 导入。
- 在线 Mod 列表和详情页显示已安装状态；已追踪 Mod 在模块内滚动浏览，安装安全移至右栏，从系统托盘恢复窗口时尝试将应用置前。
- 主程序和独立开发者工具均提供 v3.9.5 ZIP 与 SHA-256 校验文件。开发者工具仍独立发布，不进入主程序自动更新包。

完整细节见 [v3.9.5 发布报告](./docs/releases/v3.9.5.md)。

## v3.9.4 更新摘要

- 重新整理“更新”工作区：宽屏下已追踪 Mod 自动使用双列卡片，配置方案操作合并为紧凑的一行；窄窗口仍自动恢复为单列布局。
- 在线条目包含多个文件时，默认选择最新的可用压缩包；详情页保留手动文件选择，可查看版本、日期、大小和归档状态。
- 在线安装会读取详情图片的真实像素尺寸，从多张候选图中选择清晰、比例正常的一张作为本地 Mod 预览图，不再直接保存低清列表缩略图。
- 恢复下载到角色文件夹的自动路由：结合分类 ID、中英文角色名、Wiki 名称和别名匹配仓库一级目录；歧义结果不会自动猜测。
- 优化在线卡片、设置页和更新页的宽屏利用率与信息层级，并补充开源依赖、项目地址和作者主页入口。
- 新增单独发布的图形化开发者工具，用于检查版本、仓库路径、目录联接、配置、日志、缓存、备份和脱敏诊断；它不进入主程序自动更新包。
- 主程序包与独立开发者工具包分别提供 SHA-256 校验文件。

## v3.9.0 重要更新

- 完整重构仪表板、仓库工作台、在线浏览、更新和设置界面，提供响应式宽屏、半屏和紧凑布局；仪表板路径区与游戏预设区在宽屏下严格对齐。
- 仓库路径改为每个仓库独立配置并直接显示在仪表板；支持最小化到系统托盘，并在托盘提示中保留仓库、Mod 数量、在线来源和版本等基础信息。
- 从 Mod `.ini` 的 `[Key...]` 段只读识别快捷键，自动生成随当前应用语言切换的可读功能说明，并解除原先 10 行编辑限制。
- 补全《明日方舟：终末地》的 GameBanana/Wiki 预设与在线角色目录更新；保留原神、绝区零和崩坏：星穹铁道预设。
- 修复导入文件选择导致的崩溃、下载进度倒退或乱跳，并继续保留安全压缩包校验、目录联接部署和失败更新回滚。
- 应用入口更名为 `IntegratedModManager.exe`，同时保留一代旧入口用于 v3.8.5 到 v3.9.0 的原位自动更新兼容。
- 自动化验证扩展至 **146 项测试**，并继续检查覆盖率、WinUI x64 构建、发布包结构和更新代理事务。

完整版本历史请查看 [CHANGELOG](./docs/releases/CHANGELOG.md)，v3.9.5 发布与验证信息请查看 [发布报告](./docs/releases/v3.9.5.md)。

## 文档导航

| 文档 | 中文 | English |
| --- | --- | --- |
| 快速使用 | [快速使用手册](./docs/guides/快速使用手册.md) | [Quick Start](./docs/guides/Quick-Start.en.md) |
| 完整用户手册 | [详细中文手册](./docs/guides/用户手册.zh-CN.md) | [Complete User Guide](./docs/guides/User-Guide.en.md) |
| 组合与状态预设 | [组合与状态预设](./docs/guides/Combination-Presets.zh-CN.md) | [Combination and State Presets](./docs/guides/Combination-Presets.en.md) |
| 升级到 4.0 | [升级指南](./docs/guides/Upgrade-v4.0.zh-CN.md) | [Upgrade Guide](./docs/guides/Upgrade-v4.0.en.md) |
| 更新历史 | [双语更新日志](./docs/releases/CHANGELOG.md) | [Bilingual Changelog](./docs/releases/CHANGELOG.md) |
| 跨 Mod 状态试验 | [试验路线与当前结论](./docs/research/跨Mod状态保存试验结论.md) | [Current conclusions](./docs/research/跨Mod状态保存试验结论.md) |
| 测试与构建 | [测试说明](./docs/development/TESTING.md) | [Testing Guide](./docs/development/TESTING.md) |
| 4.1 源码优化 | [源码优化记录](./docs/development/Source-Optimization-v4.1.md) | [Source Optimization Record](./docs/development/Source-Optimization-v4.1.md) |
| 参与项目 | [贡献指南](./CONTRIBUTING.md) | [Contributing](./CONTRIBUTING.md) |
| 安全问题 | [安全策略](./SECURITY.md) | [Security Policy](./SECURITY.md) |

## 系统要求

- Windows 10 1809 或更高版本，推荐 Windows 11。
- 64 位 Windows（`x64`）。
- 在线浏览、翻译和更新检查需要网络连接。
- 发布包包含 Windows App SDK，但需要 .NET 8 x64 运行时；普通用户不需要 .NET SDK 或 Visual Studio。
- 7Z、RAR、ZIPX 和 CAB 优先使用发布包内或已安装的 7-Zip；找不到时自动使用已安装 Bandizip 的 `bz.exe`，并由 Windows `tar.exe` 在解压前检查路径和链接。

## 从源码构建

开发环境需要 Windows 10/11 x64、.NET 8 SDK、Visual Studio 2022 或 Build Tools 2022、MSBuild、Windows App SDK 和 Windows SDK。

```powershell
cmd /c build_winui.bat
```

运行完整测试、覆盖率检查、WinUI x64 构建和发布包验证：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-all.ps1
```

GitHub Actions 会在推送到 `main`、Pull Request 和手动触发时执行统一三组验证。测试依赖集中管理，未删除安全用例。当前结果及验证边界见[测试说明](./docs/development/TESTING.md)与[4.1 验证记录](./docs/verification/v4.1.0.md)。

## 数据与安全

- 仓库路径、界面设置和在线缓存保存在本机，不会由本项目自动上传。
- 诊断报告会排除访问凭据并脱敏用户路径，但提交前仍应由用户检查内容。
- 本工具不会绕过付费、订阅、权限验证、验证码或 Mod 作者的访问限制。
- 不要在 Issue、截图或压缩包中公开本地配置、个人目录、访问令牌或其他敏感信息。
- 安全问题请按照 [SECURITY.md](./SECURITY.md) 的方式报告。

## 许可证与声明

本项目以 [MIT License](./LICENSE) 发布，版权所有 `uyujkk`。第三方组件保留各自许可证；发布包中的 7-Zip 文件附带其许可证文本。

本工具不包含游戏文件或 Mod 内容。游戏、角色、图片、Mod 和第三方服务的相关权利归各自权利人所有。
