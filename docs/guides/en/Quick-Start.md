# Quick Start

> For 4.6.1. [中文](../zh-CN/快速上手.md) · [Full user guide](./User-Guide.md) · [Documentation index](../../README.md)

## Install and set up

1. Install the .NET 8 x64 runtime, then fully extract the release into a short, writable folder. Do not run it from inside the ZIP.
2. Run `IntegratedModManager.exe` and keep `WinUI3`, `LocalUpdateAgent.exe` and the other files beside it.
3. Create or select a repository on the Dashboard: set **Mod Storage Folder** to the root of your two-level library and **Target Folder** to the Mods directory read by the game or XXMI.
4. Optional: turn on directory-link deployment and choose an external launcher; Endfield repositories can use the integrated launch.
5. Open the Repositories page. While the repository isn't set up, a **Get started** card lists the remaining steps.

Recommended structure: `Mod Storage\Character\Specific Mod\files…`. The first level is a character or category; the second level is the complete Mod folder that gets deployed.

## Features at a glance

- **Navigation and search:** `Ctrl+K` opens a command palette over the pages and the current repository's characters and Mods; the navigation collapses to an icon rail, and download progress sits at its foot.
- **Repositories and character avatars:** one repository per game; characters show avatars you can replace or bind to a catalog character.
- **Deploying Mods:** Mod rows show a thumbnail, tags, the number of key bindings, size and deploy state, with deploy-state filters. Double-click a Mod or use the deployment button in Details; do it again to remove. With directory links, each character keeps one enabled Mod by default; allow several per character from the character menu.
- **Import:** ZIP, 7Z, RAR, TAR and more, with Import or drag-and-drop onto the Mod area.
- **Previews and information:** previews are detected or dragged in; the information table shows and edits the source link and tags, copies the location and shows the size.
- **State slots:** save a Mod's values as named slots, write them as Mod defaults, import a variant file, or snapshot the whole `d3dx_user.ini`. [Details](./State-Slots.md)
- **Shortcuts and overlay:** click to send a Mod's keys; the overlay (Ctrl + Alt + K) switches Mods, saves presets (sending F10 first), refreshes and hot-switches slots in game. [Details](./Shortcut-Panel.md)
- **Combination presets:** save the enabled Mod set with its values; restore the set, then the values; export bundles. [Details](./Combination-Presets.md)
- **Online browsing:** browse GameBanana by character, keep favorite characters and authors, download later or in batches, with automatic install. [Details](./Browse-and-Download.md)
- **Mod updates and safety:** tracked online installs notify you of updates, and the Updates page filters and searches them; conflicts are checked and failures roll back.
- **App updates:** download, install and restart after confirmation, with rollback on failure.
- **Settings:** Chinese/English, a theme that can follow Windows, high contrast, density and reduced motion; export sanitized diagnostics.

## Common actions

| Action | How |
| --- | --- |
| Deploy or remove a Mod | Double-click it, or use the deployment button in Details |
| Change an avatar, allow several Mods | Select a character, then use More above the list or right-click it |
| Save a state slot | Select a Mod → State Slots tab |
| Save or restore a combination | Presets at the top of Repositories |
| Work in game | Press Ctrl + Alt + K for the overlay |
| Import an archive | Select a character, then Import or drag onto the Mod area |
| Search pages, characters and Mods | `Ctrl+K` |
| Set a preview | Select a Mod and drag an image onto Preview |
| Refresh the page | `F5` |
| Switch sections | `Alt+1` – `Alt+5` |
| Focus search / open the Updates page | `Ctrl+F` / `Ctrl+U` |

## Important notes

- Endfield needs the manager to run as administrator for keys to reach the game; while elevated, drag-and-drop is blocked, so use the file picker.
- The app is not commercially code-signed, so SmartScreen may warn about an unknown publisher. Download only from the official GitHub Releases page.
- Online features never bypass logins, payments, subscriptions, permissions or author restrictions.
- Back up the app folder and the loader's active `d3dx_user.ini` before upgrading. Undoing a deployment does not undo parameters.
- Third-party Mods and loaders may lead to account penalties. Check configs, screenshots and diagnostics for personal paths or account details before sharing.

Project: <https://github.com/uyujkk/Integrated_Mod_Manager>
