# [中文](./README.md) | [English](./README.en.md)

# 集成化mod管理器 v3.0.2

> Non-official notice:
> This project is an unofficial fan-made tool. It is not affiliated with, endorsed by, or sponsored by XXMI, any game publisher, or any related developers.

这是一个运行在 Windows 上的 WinUI 3 图形工具，用于管理多仓库 Mod 工作区、浏览和操作两层 Mod 文件夹、导入压缩文件、记录每个 Mod 的快捷键与描述、设置预览图、绑定 Mod 链接，并接入在线 Mod 浏览、下载与更新追踪能力。

当前版本：

- 显示版本：`v3.0.2`
- 文件版本：`3.0.2.0`

## 主要功能

- 支持多仓库工作区，可分别配置 Mod 仓库路径、目标路径和启动器路径
- 支持第一层分类文件夹与第二层 Mod 文件夹浏览
- 支持复制、删除、预览图设置、快捷键记录和 Mod 链接绑定
- 支持导入 `.zip`、`.zipx`、`.7z`、`.rar`、`.tar`、`.tar.gz`、`.tar.bz2`、`.tar.xz`、`.tar.zst`、`.gz`、`.tgz`、`.bz2`、`.xz`、`.zst`、`.cab`
- 支持在线 Mod 浏览、筛选、分页、详情查看、下载与解压
- 支持记录在线来源、远程 ID、预览图和更新时间
- 支持更新检查、浅色/深色主题切换、中英界面切换

## v3.0.2 更新

- 新增常用压缩格式支持，补充 `.rar`、`.zipx`、`.zst`、`.tar.zst`、`.cab`
- 为 `.7z`、`.rar`、`.zipx`、`.cab` 接入 7-Zip 解压后端
- 构建时会自动打包可用的 7-Zip 工具文件到发布目录
- 将应用内 GitHub 更新与仓库跳转地址切换到 `Integrated_Mod_Manager`

## 运行方法

1. 下载并完整解压发布压缩包
2. 运行 `ModFolderCopier.exe`
3. 首次启动后配置：
   - `Mod 仓库`
   - `目标文件夹`
   - `启动器`（可选）
4. 点击 `刷新目录`
