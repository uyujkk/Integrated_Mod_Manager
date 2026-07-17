# CHANGELOG / 更新日志

This document records release changes in both Chinese and English.

本文档用于用中文和英文记录版本更新内容。

## v3.0.2

### 中文

- 新增常用压缩格式支持：`.rar`、`.zipx`、`.zst`、`.tar.zst`、`.cab`
- 为 `.7z`、`.rar`、`.zipx`、`.cab` 接入 7-Zip 解压后端
- 构建时自动复制可用的 `7z.exe`、`7z.dll` 和许可证到发布目录
- 将应用内 GitHub 更新检查与仓库链接切换到 `Integrated_Mod_Manager`
- 显示版本更新为 `v3.0.2`，文件版本更新为 `3.0.2.0`

### English

- Added common archive formats: `.rar`, `.zipx`, `.zst`, `.tar.zst`, and `.cab`
- Added a 7-Zip extraction backend for `.7z`, `.rar`, `.zipx`, and `.cab`
- Updated the build flow to copy available `7z.exe`, `7z.dll`, and the license into the release package
- Switched the in-app GitHub update check and repository links to `Integrated_Mod_Manager`
- Updated the displayed app version to `v3.0.2` and the file version to `3.0.2.0`
