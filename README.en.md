[中文](./README.md) | [English](./README.en.md)

# Integrated Mod Manager v3.0.1

> Non-official notice:
> This project is an unofficial fan-made tool. It is not affiliated with, endorsed by, or sponsored by XXMI, any game publisher, or any related developers.

This is a WinUI 3 desktop tool for Windows that helps manage multi-repository mod workspaces, browse and operate on two-level mod folders, import archives, store per-mod shortcut notes and descriptions, set preview images, bind mod links, and use online mod browsing, downloading, and update tracking workflows.

Current version:

- Display version: `v3.0.1`
- File version: `3.0.1.0`

## Main Features

- Multiple repository workspaces with separate mod source, target, and launcher paths
- First-level category browsing and second-level mod browsing
- Copy, delete, preview image, shortcut note, and per-mod link management
- Archive import support for `.zip`, `.7z`, `.tar`, `.gz`, `.tgz`, `.bz2`, and `.xz`
- Online mod browsing, filtering, paging, detail preview, download, and extraction
- Online source tracking with remote ID, preview image, and update timestamp
- Update checks, light/dark theme switching, and Chinese/English UI switching

## What's New In v3.0.1

- Added a dedicated application icon for both the WinUI app and the launcher
- Refreshed the release package layout for easier download-and-run usage
- Fixed mojibake in the launcher assembly title and startup error messages
- Updated the Chinese and English documentation plus the changelog

## Recommended Folder Structure

```text
Mod Storage
├─ CategoryA
│  ├─ Mod1
│  ├─ Mod2
│  └─ Mod3
├─ CategoryB
│  ├─ Mod4
│  └─ Mod5
└─ CategoryC
   └─ Mod6
```

## How To Run

1. Download and fully extract the release archive
2. Run `ModFolderCopier.exe`
3. Configure:
   - `Mod Storage`
   - `Target Folder`
   - `Launcher` (optional)
4. Click `Refresh`

## Build Requirements

- Windows 10 / 11 x64
- .NET SDK `8.x`
- Visual Studio 2022 or Build Tools 2022
- Windows SDK

Build command:

```bat
cmd /c build_winui.bat
```

## Release Contents

- `ModFolderCopier.exe`
- `WinUI3/`
- `README.md`
- `README.en.md`
- `使用说明.md`
