# Integrated Mod Manager v3.8.5 - Quick Start

[中文快速手册](./快速使用手册.md) | [Complete English Guide](https://github.com/uyujkk/Integrated_Mod_Manager/blob/main/docs/User-Guide.en.md)

## Install and configure

1. Fully extract the release archive. Do not run from inside the ZIP.
2. Run `ModFolderCopier.exe` and keep `WinUI3` and `LocalUpdateAgent.exe` beside it.
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
- **Local switching**: Select a category and mod, then double-click or use Copy. A missing target folder is copied; an existing same-named folder is removed on the next toggle.
- **Folder tools**: Search, create, and rename first-level categories; deleting a source mod requires confirmation.
- **Archive import**: ZIP, ZIPX, 7Z, RAR, CAB, TAR, GZ, BZ2, XZ, ZST, and common `tar.*` formats. Use Import or drag an archive onto the second-level area.
- **Preview and link**: Auto-detect previews or drag in an image. Store a separate source URL for every mod and open it in the default browser.
- **Shortcut notes**: Up to 10 rows per mod. Focus the field and press a single key, chord, function key, or symbol. These are notes, not global hotkeys.
- **Online mods**: Browse by character portraits, pin favorite characters, search, sort, switch list/grid layouts, inspect details and galleries, then download and extract.
- **Requirement hints**: Best-effort detection of Patreon, subscriptions, payment, like unlocks, delayed free releases, and dependencies. The original author page remains authoritative.
- **Profiles**: Save and apply complete enabled-mod combinations.
- **Conflicts and rollback**: Check conflicts before install. Copy, removal, and profile changes create backups and roll back on failure; historical backups can be restored manually.
- **Download Task Center**: View progress, cancel, open output folders, and clear completed entries.
- **Tracked updates**: De-duplicate and check online installs, open their pages, or remove tracking without deleting local files.
- **Application updates**: Optional startup checks. After confirmation, download with progress, install, restart, and roll back on failure. A newer release ZIP placed beside the app can also be detected on restart.
- **UI and accessibility**: Chinese/English, light/dark, high contrast, density, reduced motion, Tab navigation, and display scaling.
- **Diagnostics**: Export a sanitized report or open a pre-filled GitHub issue. The user reviews and submits it; nothing is uploaded automatically.

## Common actions

| Action | How |
| --- | --- |
| Toggle a mod | Select a second-level mod, then double-click or use Copy |
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
- RAR/7Z/ZIPX/CAB extraction requires the bundled 7-Zip files under `WinUI3/Tools`.
- Configuration, SQLite data, cache, and backups are stored under `WinUI3`; updates preserve them when possible.
- Review configs, screenshots, and diagnostics before sharing them for personal paths or account information.

Project: <https://github.com/uyujkk/Integrated_Mod_Manager>

This is an unofficial fan-made tool. It is not affiliated with, authorized, endorsed, or sponsored by XXMI, any game publisher, or related developers.
