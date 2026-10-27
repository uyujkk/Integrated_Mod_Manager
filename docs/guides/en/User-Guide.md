# Integrated Mod Manager User Guide

> For 4.6.0 on Windows 10/11 x64. Tool author: uyujkk.
>
> This is an unofficial fan-made tool. It is not affiliated with, authorized, endorsed, or sponsored by XXMI, any game publisher, or related developers.

[中文](../zh-CN/用户手册.md) · [Quick Start](./Quick-Start.md) · [Documentation index](../../README.md) · [Changelog](../../releases/CHANGELOG.md) · [Latest release](https://github.com/uyujkk/Integrated_Mod_Manager/releases/latest)

**Topic guides:** [Shortcut panel and overlay](./Shortcut-Panel.md) · [State slots](./State-Slots.md) · [Combination presets](./Combination-Presets.md) · [Browse and download](./Browse-and-Download.md) · [Endfield launch](./Endfield-Launch.md)

## 1. What the app does

Integrated Mod Manager organizes locally stored Mods into independent repositories and copies or links a selected complete Mod folder into the directory read by a game or Mod loader. It also provides character avatars, archive import, previews and source links, a shortcut panel and always-on-top overlay, single-Mod state slots, combination presets, online browsing and downloading, update tracking, conflict checks, transactional rollback, an integrated Endfield launch, app updates, and diagnostics.

The left navigation contains:

| Area | Purpose |
| --- | --- |
| Dashboard | Repositories, Mod counts and path health; create or switch repositories; Endfield launch method |
| Repository | Files, Covers and combination presets; deployment, previews, shortcut notes and state slots |
| Online | Browse GameBanana by character, inspect details, images, requirements and installed status, then download |
| Updates | Tracked Mods, downloads, install safety and backups |
| Shortcuts | Click to send a Mod's keys, open the always-on-top overlay, follow the active character |
| Settings | Language, theme, motion, online categories, app updates, backups and diagnostics |

## 2. Installation, startup and upgrades

### 2.1 Requirements

- Windows 10 version 1809 or later (Windows 11 recommended), 64-bit (x64).
- The .NET 8 x64 runtime. The Windows App SDK is bundled; regular users do not need Visual Studio or the .NET SDK.
- Network access for online Mods, translation, update checks and GitHub reports.

### 2.2 Install and run

1. Download `Integrated_Mod_Manager-vX.X.X.zip` and its `.sha256` from [GitHub Releases](https://github.com/uyujkk/Integrated_Mod_Manager/releases/latest).
2. Fully extract it to a short, writable folder. Do not run it from inside the archive.
3. Keep `IntegratedModManager.exe`, `LocalUpdateAgent.exe`, the compatibility entry `ModFolderCopier.exe` and the `WinUI3` folder together.
4. Run `IntegratedModManager.exe`.

The executable is not commercially code-signed, so SmartScreen may report an unknown publisher. Download only from this project's Releases page and do not disable Microsoft Defender to run it.

### 2.3 Upgrading

- 4.x stable releases can update in the app (section 10), or you can extract the new release to a new folder. To migrate by hand, close both versions, then copy `WinUI3/config.ini` and `WinUI3/beta-shell.json`.
- Back up the app folder, the target Mods folder and the loader's active `d3dx_user.ini` first.
- Do not let two managers write to the same target folder.
- Upgrade notes for 4.0, 4.1 and 4.1.1 are kept in the [archive](../archive/).

## 3. Repositories and folder model

Each repository stores its own name, Mod storage folder, target folder, optional launcher, online source and category IDs, Wiki address and notes. Use separate repositories for different games, loaders or test setups.

```text
Mod Storage Folder
├─ Character or category A       <- first level
│  ├─ Mod A1                     <- second level, the deployment unit
│  └─ Mod A2
└─ Character or category B
   └─ Mod B1
```

- The first level is a category, usually a character; the second level is a complete Mod folder.
- The target folder is usually the `Mods` directory read by XXMI or the game.
- Double-click a dashboard card to switch repositories; edit a repository to change paths and online metadata.

## 4. Local Mod management

### 4.1 First setup

1. Create a repository on the Dashboard, or edit the default one.
2. Set **Mod Storage Folder** to the root of the two-level library and **Target Folder** to the directory read by the game or loader.
3. Optionally turn on directory-link deployment (4.4) and choose an external launcher.
4. Refresh, then select a character and a Mod.

### 4.2 Character list and avatars

- Search characters by name; the plus button creates one and the edit button renames it; each item shows its Mod count.
- Each character shows an avatar, taken from, in order:
  1. an `.imm_avatar.*` or `avatar.*` image in the character folder;
  2. a character you bound the folder to;
  3. the catalog character whose Chinese name, English name or alias equals the folder name (case, width and spaces are ignored).
- The catalog is shared with the Online page: the official operator list for Endfield (the built-in list fills gaps), the wiki plus GameBanana character categories for other games. A catalog older than a day refreshes in the background, so new characters appear within a day.
- Select a character and click the **avatar button** above the list (or right-click → Avatar) to choose an image (copied into the folder; the old one is kept as `.imm_avatar.previous.*`), bind the folder to a character, restore the previous avatar, or remove the custom one.

### 4.3 Deploying and removing

- Switch between Files, Covers and Presets at the top; switching views never deploys Mods.
- Single-click only selects a Mod and refreshes its preview, link, shortcuts and deployment state. Wide windows show details inline; narrow windows use Details.
- Double-click a Mod or use the deployment button at the bottom of Details to toggle it: a missing target folder is deployed, an existing same-named folder is removed.
- The trash button deletes the source Mod from the library after a confirmation.
- Deploy, remove and profile changes create transactional backups and roll back on failure.

### 4.4 Directory links and several Mods per character

- With **Deploy Mods with directory junctions** on, the loader and library share one copy of each Mod.
- By default each character keeps one enabled Mod: enabling another replaces the links of its other Mods.
- Select a character and click the **multi-select toggle** to the right of the avatar button (or right-click → Allow several enabled Mods) to keep several enabled for it. A highlighted toggle means several are allowed.
- The repository editor can make several Mods the default, with individual characters turned back to one.
- Copy mode never removes sibling copies automatically. Restoring a combination preset asks first when it enables several Mods of a single-selection character.

### 4.5 Archive import

Supported: `.zip .zipx .7z .rar .cab .tar .tar.gz .tar.bz2 .tar.xz .tar.zst .gz .tgz .bz2 .xz .zst`. Select a character and use **Import to selected folder**, or drag an archive onto the Mod area (when running as administrator Windows blocks drag-and-drop; use the file picker).

- ZIP uses the built-in extractor; 7Z, RAR, ZIPX and CAB prefer bundled or installed 7-Zip; TAR formats use Windows `tar.exe`.
- Without 7-Zip, an installed Bandizip console tool `bz.exe` can be used (looked up in the app's `Tools` folder, the app folder, registered installations, standard folders and `PATH`). Windows `tar.exe` first checks every entry for unsafe paths, links and special files and stops if the check fails or is unsupported; RAR5 also gets a header check that rejects redirections, multi-volume archives, encrypted headers and self-extracting archives. A failed 7-Zip extraction is never retried with Bandizip.
- Each `tar.exe` or `bz.exe` call is limited to 10 minutes.
- Encrypted, damaged or incomplete multi-volume archives may fail.

### 4.6 Previews and source links

The app looks for `preview.*`, `cover.*`, `thumbnail.*` and `image.*`, then the first image in the Mod folder (PNG, JPG, BMP, GIF, WebP). Drag an image onto the preview area to set it. Each Mod keeps its own source link, opened in the default browser. Previews are kept when a Mod is updated.

### 4.7 Shortcut notes

- The Shortcuts tab in Details reads `[Key…]` sections from the Mod's INI files and formats action descriptions in the app language; you can edit the list without a row limit. Automatic scanning keeps up to 512 bindings.
- These are notes. To send keys by clicking, use the Shortcuts page on the left; see [Shortcut panel and overlay](./Shortcut-Panel.md).

### 4.8 External launcher

Choose a launcher executable and use **Run Launcher**; its folder is used as the working directory, which suits XXMI Launcher and similar tools. Endfield repositories can use the integrated launch instead; see [Endfield launch](./Endfield-Launch.md).

## 5. Single-Mod state slots

Select a Mod and open the **State Slots** tab to save the Mod's current `global persist` values as named slots, several per Mod. You can also write a slot as the Mod's defaults, import a variant file, snapshot the whole `d3dx_user.ini`, and hot-apply while the game runs (experimental). See [State slots](./State-Slots.md).

## 6. Shortcut page and overlay

The Shortcuts page lists a Mod's INI key bindings and sends them to the game window with a click. The always-on-top overlay (Ctrl + Alt + K) works over the game: switch enabled Mods, save a combination or single-Mod preset (F10 is sent first), refresh, hot-switch state slots, and follow the active character. Endfield needs the manager to run as administrator. See [Shortcut panel and overlay](./Shortcut-Panel.md).

## 7. Combination presets and bundles

Open **Presets** at the top of Repositories to save the enabled Mod set, optionally with `d3dx_user.ini` values; restoring brings back the set first, then the values. Bundles export a preset with its Mod files for installation in another repository. See [Combination presets](./Combination-Presets.md).

## 8. Online browsing and downloads

The Online page browses GameBanana by character, with favorite characters and authors, download-later lists, batch downloads, search and sorting, details and a full-size gallery. Downloads are extracted and installed automatically and their source is recorded; the task center is on the Updates page. See [Browse and download](./Browse-and-Download.md).

The app never bypasses logins, captchas, payments, subscriptions, permission checks or author restrictions; requirement hints are best-effort and the author's page is authoritative.

## 9. Updates area: Mod updates, safety and tasks

### 9.1 Tracked Mod updates

Online installs record the remote ID, source link, preview and update time. Check manually or every day, three days or week; when an update exists you are notified and can **Download and replace** (the old files are backed up first). Deleting a tracking record never deletes local files.

### 9.2 Conflict detection

Before installing or applying a profile, target paths and INI conflicts are checked. Review conflicts so different Mods don't overwrite the same files or keys. Broken directory links are detected and can be cleaned up.

### 9.3 Backups and rollback

- Deploy, remove and profile changes create transactional backups and roll back on failure; the latest change can be undone by hand.
- The backup list shows time, affected Mods and size, with a Restore action. Set a 0.5–100 GB cap in Settings; older backups are pruned beyond it.
- Parameter writes keep their own backups (`d3dx_user.ini.imm-persist-*.bak`, `.imm-snapshot-*.bak`) beside the parameter file. Undoing a deployment does not undo parameters.

### 9.4 Download task center

Watch download and extraction progress, cancel, retry failed tasks, open output folders or clear entries.

## 10. App updates

- Startup update checks can be turned on or off in Settings. A new release always asks first; nothing is installed silently.
- After confirmation the app downloads the Release ZIP, shows progress, verifies it, and the separate updater replaces the files and restarts. Repositories, paths, UI settings, Mod notes, presets, slots and SQLite data are kept.
- Old files are backed up, and a failed start rolls back. The updater supports paths longer than 260 characters.
- You can also place a correctly named newer ZIP in the app folder and restart; the app detects it and asks.
- Use the `.zip.sha256` that matches the exact ZIP file name.

## 11. Settings, data and privacy

### 11.1 Interface and accessibility

Chinese/English, light/dark and high contrast, comfortable/compact density, reduced motion, Tab navigation, screen-reader names and display scaling. Restoring a maximized window shrinks it to 80% of the work area, centered.

### 11.2 Local data

Most data is under `WinUI3`:

| File or folder | Contents |
| --- | --- |
| `config.ini` | Theme, language, shortcut notes, Mod links and online install sources |
| `beta-shell.json` | Repositories, current page, online categories, update options, `ConfigurationProfiles` presets and `ModPersistentSlots` state slots |
| SQLite database (`cache/app-index.db`) | Online pages, characters, details, metrics, favorites, versions and file index |
| `cache/online-images` | Re-creatable online image cache |
| `snapshots/user-ini` | Whole `d3dx_user.ini` snapshots |
| `backups` | Configuration, install transaction, app update and Endfield launch backups |
| `diagnostics` | Local diagnostics such as the shortcut send log |

Configuration writes are atomic with rotated backups; a damaged main configuration is recovered from the latest valid backup when possible. The loader's `d3dx_user.ini` may be outside the app folder; select the active file and back it up separately.

### 11.3 Diagnostic report

**Export Diagnostic Report** collects the app version, a system summary, error logs and a sanitized configuration summary, excluding private paths and credentials. The GitHub option opens a pre-filled issue; nothing is submitted automatically, so review it first.

## 12. Keyboard

| Key | Action |
| --- | --- |
| `Alt+1` – `Alt+5` | Dashboard, Repository, Online, Updates, Settings |
| `Ctrl+F` | Focus the main search box |
| `Ctrl+U` | Check for app updates |
| `F5` | Refresh the current module |
| `Ctrl + Alt + K` | Show or hide the overlay (can be changed or disabled) |
| `Enter` | Run the selected item's main action |
| `Esc` | Close online details or the image viewer |
| `Left` / `Right` | Change image in the full-size viewer |

## 13. Troubleshooting

- **Online data is empty or fails to load:** check the category ID and that GameBanana is reachable; HTTP 429 means rate limiting, so wait before refreshing. A proxy may be needed if pages load slowly.
- **Downloads temporarily show zero:** some list endpoints omit download counts; the app fills them from details and may show cached data or zero first.
- **RAR, 7Z or ZIPX extraction fails:** make sure `WinUI3/Tools/7z.exe` and `7z.dll` weren't removed or quarantined, and check for encryption, damage or missing volumes.
- **A deployed Mod shows as not deployed again:** deployment is a toggle; a second activation removes an existing same-named folder.
- **No preview appears:** name the image `preview.png` or `cover.jpg`, or drag it onto the preview area.
- **The game ignores shortcut keys:** Endfield needs the manager to run as administrator; see [Shortcut panel troubleshooting](./Shortcut-Panel.md#troubleshooting).
- **An app update fails:** make sure the folder is writable, the ZIP is complete and the app isn't running inside an archive. The updater rolls back; if needed, extract the new release to a new folder and migrate your backed-up configuration.

## 14. Safety and usage boundaries

- The tool contains no game files or Mod content. Third-party Mods and loaders may lead to account penalties or bans.
- It does not bypass payments, subscriptions, logins, permission checks, author restrictions or platform rules.
- Detection, translation and automatic extraction are aids; the original author page is authoritative. Inspect and scan unknown Mods first.
- Review screenshots, configuration and diagnostics for personal paths, accounts or credentials before sharing.

**Feature status (4.6.0):** directory-link deployment, loader state restore and combination presets are verified, stable features; variant file import still needs a real exported file; F10 hot apply and online auto-translation remain experimental.

Source code, issues and full release history: <https://github.com/uyujkk/Integrated_Mod_Manager>
