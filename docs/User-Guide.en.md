# Integrated Mod Manager v3.8.0 - Complete User Guide

> For Windows 10/11 x64. Tool author: uyujkk.
>
> This is an unofficial fan-made tool. It is not affiliated with, authorized, endorsed, or sponsored by XXMI, any game publisher, or related developers.

[中文详细手册](./用户手册.zh-CN.md) | [English Quick Start](../Quick-Start.en.md) | [Full Changelog](../CHANGELOG.md) | [Latest Release](https://github.com/uyujkk/Integrated_Mod_Manager/releases/latest)

## 1. What the app does

Integrated Mod Manager organizes locally stored mods into independent repositories and copies a selected complete mod folder into the directory read by a game or mod loader. It also provides archive import, preview images, source links, shortcut notes, online browsing and downloading, update tracking, profiles, conflict detection, transactional rollback, a download task center, app updates, and privacy-sanitized diagnostics.

The left navigation contains:

| Area | Purpose |
| --- | --- |
| Dashboard | Review repositories, mod counts, and path health; create or switch repositories |
| Repository | Manage two-level local folders, copy toggles, previews, links, and shortcut notes |
| Online | Browse GameBanana by character, inspect images and requirements, then download |
| Updates | Manage profiles, conflicts, rollback backups, download tasks, and tracked mod updates |
| Settings | Configure language, theme, motion, online categories, app updates, backups, and diagnostics |

## 2. Installation and startup

### 2.1 Requirements

- Windows 10 version 1809 or later; Windows 11 is recommended.
- 64-bit Windows (x64).
- Network access for online mods, translation, update checks, and GitHub reports.
- The release is self-contained; regular users do not need Visual Studio or the .NET SDK.

### 2.2 Install and run

1. Download `Integrated_Mod_Manager-vX.X.X.zip` from [GitHub Releases](https://github.com/uyujkk/Integrated_Mod_Manager/releases/latest).
2. Fully extract it to a writable folder. Do not run it from inside the archive.
3. Keep `ModFolderCopier.exe`, `LocalUpdateAgent.exe`, and the `WinUI3` folder together.
4. Run `ModFolderCopier.exe`.

The executable is not currently signed with a commercial code-signing certificate, so SmartScreen may report an unknown publisher. Download only from this project's Releases page, inspect the source if desired, and do not disable Microsoft Defender just to run the app.

## 3. Repositories and folder model

Each repository stores its own name, mod storage folder, target folder, optional launcher, online source/category IDs, Wiki address, and notes. Use separate repositories for different games, loaders, or test environments.

```text
Mod Storage Folder
├─ Character or category A       <- first level
│  ├─ Mod A1                     <- second level and copy unit
│  └─ Mod A2
└─ Character or category B
   └─ Mod B1
```

- First-level folders are categories such as a character or outfit type.
- Second-level folders are complete mods operated on by the app.
- The target folder is usually the `Mods` directory read by XXMI or the game.
- Double-click a dashboard card to switch repositories; edit a repository to change paths and online metadata.

## 4. Local mod management

### 4.1 First setup

1. Create a repository or edit the default one.
2. Set **Mod Storage Folder** to the root of the two-level structure.
3. Set **Target Folder** to the directory read by the game or loader.
4. Optionally select an external launcher executable.
5. Refresh, then select a first-level category and a second-level mod.

### 4.2 First-level folders

- Search categories by name.
- Use the plus button to create a category.
- Use the edit button to rename the selected category.
- Each item shows its second-level mod count.

### 4.3 Second-level mods and copy toggling

- Select a mod to load its preview, link, shortcut notes, and current copy state.
- Double-click it or use the primary copy button to toggle it.
- If the target has no folder with the same name, the whole mod folder is copied.
- If the same folder already exists, toggling again removes it from the target.
- The delete button removes the source mod after a confirmation prompt. This is separate from removing a copied target folder.
- Copy, remove, and profile operations create transactional backups and automatically roll back on failure.

### 4.4 Archive import

Supported formats:

```text
.zip  .zipx  .7z  .rar  .cab
.tar  .tar.gz  .tar.bz2  .tar.xz  .tar.zst
.gz   .tgz     .bz2      .xz      .zst
```

Select a first-level folder, then use **Import to selected folder**, or drag an archive onto the second-level area. Online downloads also create an independent folder and extract automatically.

ZIP uses the built-in extractor; 7Z, RAR, ZIPX, and CAB use the bundled 7-Zip files; TAR-family formats use Windows `tar.exe`. Encrypted, damaged, or incomplete multi-volume archives may fail.

### 4.5 Preview images and source links

The app prioritizes `preview.*`, `cover.*`, `thumbnail.*`, and `image.*`, then the first supported image in the mod folder. PNG, JPG, JPEG, BMP, GIF, and WebP are supported.

After selecting a mod, drag an image onto the preview area to set it. The source-link field is stored independently for every mod and opens in the Windows default browser.

### 4.6 Shortcut and action notes

- Store up to 10 shortcut/action rows per mod.
- Focus the shortcut field and press the desired key or chord.
- Single keys, combinations, function keys, and symbols are supported, for example `Q`, `1`, `F1`, `Ctrl+1`, `Shift+F2`, `Alt+Q`, `/`, `;`, `[`, `]`, and `\`.
- These are notes for the mod; the app does not register system-wide hotkeys.
- Online downloads try to extract shortcut instructions. Chinese UI attempts to translate the action text; English UI keeps the source text.

### 4.7 External launcher

Select a launcher executable and use **Run Launcher**. The launcher's directory is used as its working directory, which suits XXMI Launcher and similar tools.

## 5. Online browser and downloads

### 5.1 Online source configuration

The online module primarily reads GameBanana. Every repository can define its target game, skin category ID, source, and Wiki URL in Settings. Presets are provided for Endfield, Zenless Zone Zero, Genshin Impact, and Honkai: Star Rail; custom games can use their own category.

Category names containing `Skins` are accepted instead of requiring an exact `Skins` match. The Wiki character catalog is cached and refreshed separately from GameBanana data; it supplies portraits, localized names, and character order.

### 5.2 Characters, favorites, search, and layout

- Browse characters using portraits and names matching the selected app language.
- Use the star button to favorite a character. Favorites are stored per repository and pinned to the top.
- **All** shows every mod in the configured category.
- Search matches both character names and mod titles.
- Sort by heat, downloads, likes, views, or update time.
- Switch between information-dense list view and image-oriented grid view.
- Previous and Next move through full pages.

Heat is capped at 10 and uses logarithmically weighted downloads, likes, and views:

```text
min(10, 2 x (
    0.55 x log10(downloads + 1)
  + 0.30 x log10(likes + 1)
  + 0.15 x log10(views + 1)
))
```

### 5.3 Details, gallery, and access requirements

**Details** opens a dedicated right-side pane with author, character, category, update date, gallery, description, and fixed actions. Click the hero image to open the centered full-size viewer. Use arrows or Left/Right to change images; press Escape or click outside the image to close.

The app looks for Patreon, subscription, payment, like-to-unlock, delayed free release, and dependency wording and presents possible extra-access requirements. Detection is best-effort; always read the original author page.

### 5.4 Download and extract

1. Open Details and review author instructions and requirements.
2. Select **Download and Extract**.
3. Choose a first-level category or local destination.
4. Monitor download, extraction, completion, or failure in the Download Task Center.
5. Cancel active tasks, open output folders, or clear completed entries as needed.

The app does not bypass logins, captchas, payments, subscriptions, permission checks, or author restrictions. During rate limits or timeouts, it keeps usable cached data and allows a later refresh.

## 6. Updates area: profiles, safety, and tasks

### 6.1 Profiles

A profile records a repository's enabled mod set. Create, update, apply, or delete profiles to switch complete combinations for characters, game versions, or test setups.

### 6.2 Conflict detection

Before installation or profile application, the app checks target paths and file conflicts. Review conflicts before proceeding so unrelated mods do not overwrite the same files.

### 6.3 Backups and rollback

- Copy, remove, and profile changes create transactional backups.
- Failed operations roll back automatically; the most recent successful change can also be undone manually.
- The backup list shows creation time, affected mods, disk usage, and a Restore action.
- Set a 0.5-100 GB storage cap in Settings. Old backups are pruned by age when the cap is exceeded.

### 6.4 Tracked mod updates

Successful online installations store the remote ID, source URL, preview, and last update time. The Updates page can check manually or on a daily, three-day, or weekly schedule, merge duplicate records, open the source/local folder, and delete a tracking record. Deleting tracking does not delete local mod files.

## 7. Application updates

- Startup update checks can be enabled or disabled.
- A new release always prompts first; updates are never installed silently.
- After confirmation, the app downloads the Release ZIP, reports progress, verifies it, and invokes the separate update agent.
- The main app exits, files are replaced, and the app restarts. Repositories, paths, UI settings, per-mod notes, and SQLite data are preserved.
- Old files are backed up and startup failure triggers rollback.
- You may also place a correctly named newer release ZIP beside the current executable and restart; the app will detect it and ask whether to update.

## 8. Settings, data, and privacy

### 8.1 Interface and accessibility

- Chinese/English interface switching.
- Light/dark themes and Windows high-contrast adaptation.
- Comfortable/compact density.
- Reduced motion for page transitions, details, and list changes.
- Tab navigation, visible focus indicators, screen-reader names, and Windows display scaling.

### 8.2 Local data

| File or folder | Purpose |
| --- | --- |
| `config.ini` | Theme, language, shortcuts, mod links, and online-install metadata |
| `beta-shell.json` | Repositories, current page, online categories, and update options |
| SQLite database | Online pages, characters, details, metrics, favorites, versions, and file index |
| `cache/online-images` | Re-creatable online image cache |
| `backups` | Configuration, installation transaction, and software-update backups |
| `startup.log` | Launcher startup diagnostics when needed |

Configuration writes are atomic and rotated backups are retained. A damaged main configuration is recovered from the latest valid backup when possible.

### 8.3 Diagnostic report

**Export Diagnostic Report** collects app version, system summary, error logs, and a sanitized configuration summary while excluding private local paths and access credentials. The GitHub option opens a pre-filled issue for the user to review; it does not submit a report automatically. Inspect every attachment before uploading.

## 9. Keyboard navigation

| Key | Action |
| --- | --- |
| `Alt+1` through `Alt+5` | Dashboard, Repository, Online, Updates, Settings |
| `Ctrl+F` | Focus the main search field on the current page |
| `Ctrl+U` | Check for application updates |
| `F5` | Refresh the current module |
| `Enter` | Run the primary action for the selected item |
| `Esc` | Close online details or the image viewer |
| `Left` / `Right` | Change image in the full-size viewer |
| `Tab` / `Shift+Tab` | Move focus between controls |

## 10. Troubleshooting

### Online data is empty or fails to load

Confirm the category ID, ensure the category name contains `Skins`, and verify that GameBanana is reachable. HTTP 429 means the site is rate-limiting requests; wait for the cooldown and use cached data before refreshing again. Repository or character switches cancel stale requests.

### Downloads temporarily show zero

Some GameBanana list endpoints omit download counts. The app uses the same detail-enrichment and SQLite cache path for every preset. During network restrictions it may initially show cached data or zero, then refresh after enrichment succeeds.

### RAR, 7Z, or ZIPX extraction fails

Verify that `WinUI3/Tools/7z.exe`, `7z.dll`, and their license were not removed or quarantined. Also check for encryption, damage, or missing archive volumes.

### A copied mod becomes “Not copied”

Copy is a toggle. If a same-named target folder already exists, a second activation removes it from the target.

### No preview appears

Use `preview.png` or `cover.jpg`, or select the mod and drag an image onto the preview area.

### An app update fails

Ensure the app directory is writable, the ZIP is complete, and the app is not running inside an archive. The update agent attempts rollback. If necessary, extract the latest release to a new folder and back up old configuration before moving it.

## 11. Safety and usage boundaries

- The tool contains no game files or mod content.
- It does not bypass payments, subscriptions, logins, permission checks, author restrictions, or platform rules.
- Detection, translation, and automatic extraction are assistance features; the original author page is authoritative.
- Inspect and scan unknown mods before installation.
- Review screenshots, configs, and diagnostics before sharing them to avoid exposing personal paths, accounts, or credentials.

Source code, issues, and full release history: <https://github.com/uyujkk/Integrated_Mod_Manager>
