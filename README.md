# [中文](./README.md) | [English](./README.en.md)

# 集成化mod管理器 v3.0.1

> Non-official notice:
> This project is an unofficial fan-made tool. It is not affiliated with, endorsed by, or sponsored by XXMI, any game publisher, or any related developers.

这是一个运行在 Windows 上的 WinUI 3 图形工具，用于管理多仓库 Mod 工作区、浏览和操作两层 Mod 文件夹、导入压缩文件、记录每个 Mod 的快捷键与描述、设置预览图、绑定 Mod 链接，并接入在线 Mod 浏览、下载与更新追踪能力。

当前版本：

- 显示版本：`v3.0.1`
- 文件版本：`3.0.1.0`

## 主要功能

- 支持多仓库工作区，可分别配置 Mod 仓库路径、目标路径和启动器路径
- 支持第一层分类文件夹与第二层 Mod 文件夹浏览
- 支持复制、删除、预览图设置、快捷键记录和 Mod 链接绑定
- 支持导入 `.zip`、`.7z`、`.tar`、`.gz`、`.tgz`、`.bz2`、`.xz`
- 支持在线 Mod 浏览、筛选、分页、详情查看、下载与解压
- 支持记录在线来源、远程 ID、预览图和更新时间
- 支持更新检查、浅色/深色主题切换、中英界面切换

## v3.0.1 更新

- 新增应用程序图标，并同步应用到 WinUI 主程序与启动器
- 重新整理发布包结构，便于直接下载后解压使用
- 修复启动器程序集标题与错误提示中的乱码问题
- 同步更新中英文说明文档与更新日志

## 推荐目录结构

```text
Mod 仓库
├─ 分类A
│  ├─ Mod1
│  ├─ Mod2
│  └─ Mod3
├─ 分类B
│  ├─ Mod4
│  └─ Mod5
└─ 分类C
   └─ Mod6
```

- 第一层：分类文件夹
- 第二层：实际操作的 Mod 文件夹

## 运行方法

1. 下载并完整解压发布压缩包
2. 运行 `ModFolderCopier.exe`
3. 首次启动后配置：
   - `Mod 仓库`
   - `目标文件夹`
   - `启动器`（可选）
4. 点击 `刷新目录`

## 构建环境

- Windows 10 / 11 x64
- .NET SDK `8.x`
- Visual Studio 2022 或 Build Tools 2022
- Windows SDK

构建命令：

```bat
cmd /c build_winui.bat
```

## 发布包内容

- `ModFolderCopier.exe`
- `WinUI3/`
- `README.md`
- `README.en.md`
- `使用说明.md`

## 许可证

本仓库附带许可证文件，仅用于说明源码分发条件。若你的发布平台或素材来源对再分发有额外要求，请以对应平台规则和原作者要求为准。
