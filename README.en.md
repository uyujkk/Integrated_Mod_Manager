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
  [Quick Start](./docs/guides/Quick-Start.en.md) ·
  [Full Guide](./docs/guides/User-Guide.en.md) ·
  [Changelog](./docs/releases/CHANGELOG.md) ·
  [Report an Issue](https://github.com/uyujkk/Integrated_Mod_Manager/issues/new/choose)
</div>

> [!IMPORTANT]
> This is an unofficial fan-made tool. It is not affiliated with, endorsed by, authorized by, or sponsored by XXMI, any game publisher, or any related developer. Follow the rules of the relevant game, platform, and mod author.

## Overview

Integrated Mod Manager organizes different games or mod environments into independent repositories. It copies or removes complete mod folders between a local library and the target directory read by the game, while keeping preview images, source links, shortcut notes, online downloads, update records, profiles, and installation backups in one application.

The current version is **v3.9.4**, with file version `3.9.4.0`. The tool is maintained by `uyujkk`.

## Core Features

| Feature | Description |
| --- | --- |
| Multiple repositories | Keep separate paths and online categories for different games or XXMI setups |
| Local mod switching | Browse, search, copy, remove, create, rename, and delete two-level mod folders |
| Optional directory junctions | Let the loader and library share one mod directory, safely replacing the previous link for the same character |
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
4. Run `IntegratedModManager.exe` from the extracted root folder.
5. Create or select a repository, then configure the mod storage folder, target folder, and optional launcher.

`IntegratedModManager.exe` is the official launcher. The package temporarily retains `ModFolderCopier.exe` as a compatibility entry point for in-place updates from v3.8.5. The WinUI runtime is located at `WinUI3/ModFolderCopier.WinUI.exe`. Keep the release directory structure intact.

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

## v3.9.4 Update Summary

- Refined the Updates workspace: tracked Mods use a responsive two-column card layout on wide screens, profile actions share one compact row, and narrow windows automatically return to a single column.
- Online items with multiple files now default to the newest usable archive. The details pane retains manual selection with version, date, size, and archive state.
- Online installation decodes real image dimensions and chooses a clear, normally proportioned detail image for the local Mod preview instead of directly saving the low-resolution list thumbnail.
- Restored automatic character-folder routing through category IDs, localized character names, Wiki names, and aliases. Ambiguous results are never guessed.
- Improved wide-screen use and information hierarchy across online cards, Settings, and Updates, with open-source dependency, project, and author links in Settings.
- Added a separately released graphical Developer Tools app for checking versions, repository paths, directory junctions, configuration, logs, caches, backups, and sanitized diagnostics. It remains outside the main updater package.
- The main app and standalone Developer Tools archives each include a SHA-256 checksum file.

## v3.9.0 Major Update

- Rebuilt the Dashboard, repository workspace, online browser, updater, and settings surfaces with responsive wide, split, and compact layouts. Dashboard path and game-preset cards now share an exact baseline on wide screens.
- Moved per-repository path configuration and status to the Dashboard. The app can minimize to the system tray while keeping repository, Mod count, online source, and version details visible.
- Added read-only shortcut discovery from `[Key...]` sections in Mod `.ini` files, readable descriptions localized to the current app language, and an editor without the previous 10-row limit.
- Added Arknights: Endfield GameBanana/Wiki presets and online operator-catalog refresh alongside the existing Genshin Impact, Zenless Zone Zero, and Honkai: Star Rail presets.
- Fixed archive-picker crashes and regressing or jumping download progress while preserving safe archive validation, directory-junction deployment, and failed-update rollback.
- Renamed the public launcher to `IntegratedModManager.exe`; one legacy entry point remains in this release so v3.8.5 installations can update in place.
- Expanded automated verification to **146 tests**, retaining coverage gates, WinUI x64 builds, release-package validation, and update-agent transaction tests.

See the [Changelog](./docs/releases/CHANGELOG.md) for the complete bilingual history and the [v3.9.4 Release Report](./docs/releases/v3.9.4.md) for release and verification details.

## Documentation

| Document | Chinese | English |
| --- | --- | --- |
| Quick use | [快速使用手册](./docs/guides/快速使用手册.md) | [Quick Start](./docs/guides/Quick-Start.en.md) |
| Complete guide | [详细中文手册](./docs/guides/用户手册.zh-CN.md) | [Complete User Guide](./docs/guides/User-Guide.en.md) |
| Release history | [Bilingual Changelog](./docs/releases/CHANGELOG.md) | [Bilingual Changelog](./docs/releases/CHANGELOG.md) |
| Cross-mod state experiments | [Current conclusions](./docs/research/跨Mod状态保存试验结论.md) | [Current conclusions](./docs/research/跨Mod状态保存试验结论.md) |
| Tests and builds | [Testing Guide](./docs/development/TESTING.md) | [Testing Guide](./docs/development/TESTING.md) |
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

The repository currently has 146 automated tests. GitHub Actions runs the same verification flow on pushes to `main`, pull requests, and manual dispatches. See the [Testing Guide](./docs/development/TESTING.md) for details.

## Data and Security

- Repository paths, interface settings, and online caches remain on the local computer and are not automatically uploaded by this project.
- Diagnostic reports exclude access credentials and sanitize user paths, but users should still review them before submitting.
- The app does not bypass payments, subscriptions, permissions, CAPTCHAs, or restrictions set by mod authors.
- Do not disclose local configuration files, personal paths, access tokens, or other sensitive information in issues, screenshots, or archives.
- Follow [SECURITY.md](./SECURITY.md) when reporting a security issue.

## License and Notices

This project is available under the [MIT License](./LICENSE), copyright `uyujkk`. Third-party components retain their own licenses; bundled 7-Zip files include their license text.

The tool does not include game files or mod content. Rights related to games, characters, images, mods, and third-party services belong to their respective owners.
