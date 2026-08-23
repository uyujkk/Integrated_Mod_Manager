<div align="center">
  <img src="./WinUI3/Assets/AppIcon.png" width="96" alt="Integrated Mod Manager icon">
  <h1>Integrated Mod Manager</h1>
  <p>A WinUI 3 app for organizing, switching, browsing, and updating mods on Windows 10/11</p>

  [![Build and Test](https://github.com/uyujkk/Integrated_Mod_Manager/actions/workflows/build-and-test.yml/badge.svg)](https://github.com/uyujkk/Integrated_Mod_Manager/actions/workflows/build-and-test.yml)
  [![Latest Release](https://img.shields.io/github/v/release/uyujkk/Integrated_Mod_Manager?display_name=tag&sort=semver)](https://github.com/uyujkk/Integrated_Mod_Manager/releases/latest)
  [![License](https://img.shields.io/github/license/uyujkk/Integrated_Mod_Manager)](./LICENSE)
  [![Platform](https://img.shields.io/badge/platform-Windows%20x64-0078D4)](#requirements)

  [中文](./README.md) · **English**

  [Download Latest](https://github.com/uyujkk/Integrated_Mod_Manager/releases/latest) ·
  [Quick Start](./Quick-Start.en.md) ·
  [Full Guide](./docs/User-Guide.en.md) ·
  [Changelog](./CHANGELOG.md) ·
  [Report an Issue](https://github.com/uyujkk/Integrated_Mod_Manager/issues/new/choose)
</div>

> [!IMPORTANT]
> This is an unofficial fan-made tool. It is not affiliated with, endorsed by, authorized by, or sponsored by XXMI, any game publisher, or any related developer. Follow the rules of the relevant game, platform, and mod author.

## Overview

Integrated Mod Manager organizes different games or mod environments into independent repositories. It copies or removes complete mod folders between a local library and the target directory read by the game, while keeping preview images, source links, shortcut notes, online downloads, update records, profiles, and installation backups in one application.

The current stable release is **v3.8.0**, with file version `3.8.0.0`. The tool is maintained by `uyujkk`.

## Core Features

| Feature | Description |
| --- | --- |
| Multiple repositories | Keep separate paths and online categories for different games or XXMI setups |
| Local mod switching | Browse, search, copy, remove, create, rename, and delete two-level mod folders |
| Archive import | Import ZIP, 7Z, RAR, ZIPX, CAB, TAR, and common compressed stream formats |
| Previews and notes | Store an image, source link, shortcut keys, and action descriptions for each mod |
| Online mod browser | Browse GameBanana entries, filter by character, view details, and download and extract mods |
| Configuration profiles | Save and apply complete enabled-mod setups without touching unknown target folders |
| Installation safety | Detect file conflicts and create restorable backups for copy, remove, and profile operations |
| Download task center | Monitor download and extraction progress, cancel work, and open output folders |
| Updates and diagnostics | Check mod and app updates, roll back failed updates, and export sanitized diagnostics |
| Accessibility | Chinese/English, light/dark, high contrast, keyboard navigation, and display scaling |

## Download and Run

1. Open the [latest GitHub Release](https://github.com/uyujkk/Integrated_Mod_Manager/releases/latest).
2. Download `Integrated_Mod_Manager-vX.X.X.zip` and its matching `.sha256` file.
3. **Fully extract** the ZIP into a writable folder. Do not run it inside the archive.
4. Run `ModFolderCopier.exe` from the extracted root folder.
5. Create or select a repository, then configure the mod storage folder, target folder, and optional launcher.

`ModFolderCopier.exe` is the launcher. The WinUI application is located at `WinUI3/ModFolderCopier.WinUI.exe`. Keep the release directory structure intact.

### SmartScreen Notice

The executable is not signed with a commercial code-signing certificate, so Windows SmartScreen may report an unknown publisher. Download only from this repository's Releases page, and do not disable Microsoft Defender to run the app. The source, build scripts, automated tests, and package verification flow are public in this repository.

## Quick Start

1. Create a repository on the Repositories page.
2. Set the Mod Storage Folder to the root of a two-level mod directory.
3. Set the Target Folder to the Mods directory read by the game or mod loader.
4. Select a first-level category, then select a second-level mod.
5. Double-click the mod or use the copy action to toggle it.

If the target does not contain a folder with the same name, the app copies the complete mod. If it already exists, running the action again removes it from the target. Deleting the source mod from the repository is a separate action and requires confirmation.

### Recommended Folder Layout

```text
Mod Storage Folder
├─ Character or Category A
│  ├─ Mod A1
│  └─ Mod A2
└─ Character or Category B
   └─ Mod B1
```

The first level is a character, purpose, or other category. The second level contains the complete mod folders managed by the app.

## v3.8.0 Major Update

- Hardened folder copy, deletion, and archive import against traversal, symbolic links, hard links, and reparse-point escapes.
- Hardened automatic updates with package-structure checks, filename-bound SHA-256 verification, a managed-file manifest, and startup rollback.
- Added cancellation for obsolete online requests and timeout, media-type, and size limits for preview downloads.
- Fixed locked update temporary files and strengthened SQLite, backup recovery, cleanup, and error reporting.
- Expanded automated verification to **62 tests**, with CI coverage gates, a WinUI x64 build, and minimal release-package validation.

See [CHANGELOG.md](./CHANGELOG.md) for the complete bilingual history and [更新报告.md](./更新报告.md) for the current release report.

## Documentation

| Document | Chinese | English |
| --- | --- | --- |
| Quick use | [快速使用手册](./快速使用手册.md) | [Quick Start](./Quick-Start.en.md) |
| Complete guide | [详细中文手册](./docs/用户手册.zh-CN.md) | [Complete User Guide](./docs/User-Guide.en.md) |
| Release history | [Bilingual Changelog](./CHANGELOG.md) | [Bilingual Changelog](./CHANGELOG.md) |
| Tests and builds | [Testing Guide](./TESTING.md) | [Testing Guide](./TESTING.md) |
| Contributions | [Contributing](./CONTRIBUTING.md) | [Contributing](./CONTRIBUTING.md) |
| Security | [Security Policy](./SECURITY.md) | [Security Policy](./SECURITY.md) |

## Requirements

- Windows 10 version 1809 or later; Windows 11 is recommended.
- 64-bit Windows (`x64`).
- Online browsing, translation, and update checks require a network connection.
- Release builds are self-contained WinUI 3 applications; regular users normally do not need the .NET SDK or Visual Studio.
- 7Z, RAR, ZIPX, and CAB extraction uses the bundled 7-Zip files, so extract the complete release.

## Build from Source

Development requires Windows 10/11 x64, the .NET 8 SDK, Visual Studio 2022 or Build Tools 2022, MSBuild, Windows App SDK, and Windows SDK.

```powershell
cmd /c build_winui.bat
```

Run the complete test, coverage, WinUI x64 build, and package verification flow:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-all.ps1
```

The repository currently has 62 automated tests. GitHub Actions runs the same verification flow on pushes to `main`, pull requests, and manual dispatches. See [TESTING.md](./TESTING.md) for details.

## Data and Security

- Repository paths, interface settings, and online caches remain on the local computer and are not automatically uploaded by this project.
- Diagnostic reports exclude access credentials and sanitize user paths, but users should still review them before submitting.
- The app does not bypass payments, subscriptions, permissions, CAPTCHAs, or restrictions set by mod authors.
- Do not disclose local configuration files, personal paths, access tokens, or other sensitive information in issues, screenshots, or archives.
- Follow [SECURITY.md](./SECURITY.md) when reporting a security issue.

## License and Notices

This project is available under the [MIT License](./LICENSE), copyright `uyujkk`. Third-party components retain their own licenses; bundled 7-Zip files include their license text.

The tool does not include game files or mod content. Rights related to games, characters, images, mods, and third-party services belong to their respective owners.
