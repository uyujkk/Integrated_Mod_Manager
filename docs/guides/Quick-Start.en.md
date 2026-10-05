# Integrated Mod Manager v4.0.0 - Quick Start

[中文快速手册](./快速使用手册.md) | [Complete English Guide](./User-Guide.en.md) | [Documentation Index](../README.md)

## Install and configure

1. Install the .NET 8 x64 runtime on Windows x64, then fully extract the release archive. Do not run from inside the ZIP.
2. Run `IntegratedModManager.exe` and keep `WinUI3`, `LocalUpdateAgent.exe`, and the packaged compatibility files beside it.
3. Create or select a repository on the Dashboard.
4. Set **Mod Storage Folder** to the root of your two-level mod library and **Target Folder** to the Mods directory read by the game or XXMI.
5. Optionally select an external launcher, then refresh.

Recommended structure:

```text
Mod Storage\Character or category\Specific mod\files...
```

The first level is a category; the second-level folder is the complete mod copied by the app.

## Feature summary

- **Repository dashboard**: Keep separate repositories for games, review counts and path health, and double-click a card to switch.
- **Three repository views**: Files is the default; switch to Covers or Presets. Single-click selects only. Lists scroll within panels; narrow windows expose previews, shortcuts, and state slots through Details.
- **Local switching**: Select a character and Mod, then double-click or use the deployment action in Details. A missing target folder is deployed; an existing same-named folder is removed on the next toggle. The trash action deletes library source files instead.
- **Folder tools**: Search, create, and rename first-level categories; deleting a source mod requires confirmation.
- **Archive import**: ZIP, ZIPX, 7Z, RAR, CAB, TAR, GZ, BZ2, XZ, ZST, and common `tar.*` formats. Use Import or drag an archive onto the second-level area.
- **Preview and link**: Auto-detect previews or drag in an image. Store a separate source URL for every mod and open it in the default browser.
- **Shortcut notes**: Read-only Mod INI detection with descriptions matching the app language. The editor no longer has a 10-row limit; automatic scanning retains a 256-result safety cap. These are notes, not keys sent to the game.
- **Online mods**: Browse by character portraits, pin favorite characters, search, sort, switch list/grid layouts, inspect details and galleries, then download and extract.
- **Requirement hints**: Best-effort detection of Patreon, subscriptions, payment, like unlocks, delayed free releases, and dependencies. The original author page remains authoritative.
- **Combination and state presets**: Save the enabled Mod set, optionally with matching numeric `d3dx_user.ini` values. Restore the set first, then its values, or capture individual Mod state slots. Have the loader save before capture; exit the game and loader before restore. [Detailed steps](./Combination-Presets.en.md).
- **Conflicts and rollback**: Check conflicts before install. Copy, removal, and profile changes create backups and roll back on failure; historical backups can be restored manually.
- **Download Task Center**: View progress, cancel, open output folders, and clear completed entries.
- **Tracked updates**: De-duplicate and check online installs, open their pages, or remove tracking without deleting local files.
- **Application updates**: Optional startup checks. After confirmation, download with progress, install, restart, and roll back on failure. A newer release ZIP placed beside the app can also be detected on restart.
- **UI and accessibility**: Chinese/English, light/dark, high contrast, density, reduced motion, Tab navigation, and display scaling.
- **Diagnostics**: Export a sanitized report or open a pre-filled GitHub issue. The user reviews and submits it; nothing is uploaded automatically.

## Common actions

| Action | How |
| --- | --- |
| Toggle a mod | Select a second-level Mod, then double-click or use the deployment action in Details |
| Save or restore a combination | Select Presets at the top of Repositories |
| Import an archive | Select a first-level category, then Import or drag onto the second-level area |
| Set a preview | Select a mod and drag an image onto Preview |
| Open online details | Select **Details** on an online item |
| Favorite a character | Use the star button on the character item |
| View full-size images | Click the details image, use Left/Right, press Escape to close |
| Refresh current page | `F5` |
| Switch sections | `Alt+1` through `Alt+5` |
| Focus search | `Ctrl+F` |
| Check app updates | `Ctrl+U` |

## Important notes

- The executable is not commercially code-signed, so SmartScreen may report an unknown publisher. Download only from the official GitHub Releases page and do not disable Defender.
- Online features do not bypass logins, captchas, payments, subscriptions, permissions, or author restrictions.
- RAR/7Z/ZIPX/CAB prefer bundled or installed 7-Zip. If it is missing, an installed Bandizip with `bz.exe` can be used after Windows `tar.exe` checks the archive.
- Configuration, SQLite data, cache, and backups are stored under `WinUI3`; updates preserve them when possible.
- Back up the full app and active loader's `d3dx_user.ini` before upgrading; see the [4.0 upgrade guide](./Upgrade-v4.0.en.md). Parameter backups are separate from install backups: deployment Undo does not undo parameter changes.
- Review configs, screenshots, and diagnostics before sharing them for personal paths or account information.

Project: <https://github.com/uyujkk/Integrated_Mod_Manager>

This is an unofficial fan-made tool. It is not affiliated with, authorized, endorsed, or sponsored by XXMI, any game publisher, or related developers.
