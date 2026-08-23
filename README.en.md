# [中文](./README.md) | [English](./README.en.md)

[![Build and Test](https://github.com/uyujkk/Integrated_Mod_Manager/actions/workflows/build-and-test.yml/badge.svg)](https://github.com/uyujkk/Integrated_Mod_Manager/actions/workflows/build-and-test.yml)

# Integrated Mod Manager

A WinUI 3 mod management tool for Windows 10/11. It organizes local mods into repositories and provides two-level folder browsing, copy-based switching, archive import, image previews, shortcut notes, online mod browsing and downloads, and update tracking for installed mods.

- Current version: `v3.8.0`
- File version: `3.8.0.0`
- Tool author: `uyujkk`

[Download Latest Release](https://github.com/uyujkk/Integrated_Mod_Manager/releases/latest) ·
[All Releases](https://github.com/uyujkk/Integrated_Mod_Manager/releases) ·
[Full Changelog](./CHANGELOG.md) ·
[Complete English Guide](./docs/User-Guide.en.md) ·
[English Quick Start](./Quick-Start.en.md) ·
[Report an Issue](https://github.com/uyujkk/Integrated_Mod_Manager/issues)

> Unofficial notice: This is an unofficial fan-made tool. It is not affiliated with, endorsed by, authorized by, or sponsored by XXMI, any game publisher, or any related developer. Follow the rules of the relevant game, platform, and mod author before using mods.

## What It Does

Integrated Mod Manager is designed to:

- Organize scattered mods into independent repositories by game, character, or purpose.
- Copy or remove mods between a storage library and the folder actually read by the game.
- Create or rename categories, delete selected mods, and import common archives without leaving the app.
- Store an image, source link, shortcut keys, and action descriptions for each mod.
- Browse GameBanana mods, inspect images and descriptions, then download and extract them into a local repository.
- Track mods installed through the online browser and check whether their source pages were updated.
- Manage multiple games or multiple XXMI setups from one application.

## Main Features

| Area | Capabilities |
| --- | --- |
| Dashboard | Repository, category, mod, and path-status summaries |
| Repositories | Create, edit, rename, delete, and switch between mod repositories |
| Local management | Browse first-level categories and second-level mods, search categories, copy or remove mods |
| File actions | Create or rename categories and delete a second-level mod after confirmation |
| Archives | Import common archive formats by picker or drag and drop |
| Shortcut notes | Capture single keys, combinations, function keys, and symbols, with up to 10 rows |
| Images and links | Find preview images, accept dropped images, and store a separate web link per mod |
| Online mods | Load, search, filter, sort, page, preview, download, and extract GameBanana mods |
| Content detection | Best-effort detection of access requirements and shortcut instructions |
| Mod updates | Manually or periodically check online-installed mods for updates |
| Configuration profiles | Save and apply complete enabled-mod setups per repository without touching unknown target folders |
| Installation safety | Detect file conflicts before installation and create undoable transaction backups |
| Download tasks | Track download and extraction progress, cancel work, open folders, and clear finished entries |
| App updates | Check GitHub Releases or apply a newer release ZIP placed in the app folder |
| Interface | Chinese/English, light/dark themes, comfortable/compact density, reduced motion, and responsive layout |

## v3.8.0 Major Update

- Completed a security-focused review of file operations, archive import, online resources, and the local update path.
- ZIP, 7-Zip, and TAR imports now preflight paths and link entries, rejecting directory traversal, symbolic links, and hard links; folder copy and deletion also block reparse-point escapes.
- Automatic updates now enforce release-package structure, bind SHA-256 entries to the expected filename, maintain a managed-file manifest, and roll back automatically if the updated runtime does not start.
- Online requests cancel obsolete work, while preview downloads enforce timeout, media-type, and size limits; important failures are recorded in sanitized logs instead of being silently ignored.
- SQLite writes, backup recovery, and update cleanup have stronger boundary checks and error reporting.
- Fixed the `.zip.download` temporary file remaining locked after an automatic-update download, and now reject symbolic-link and reparse-point entries in update ZIP packages.
- Expanded automated coverage to 62 tests. The local script and GitHub Actions share one verification path for core safety logic, SQLite persistence, update transactions, coverage gates, the WinUI x64 build, and the minimal release package. See [Testing](./TESTING.md).

## v3.6.1 Highlights

- Refined online character favorite buttons with reserved layout space, theme-aware states, and clearer selection feedback so long names remain readable.
- Added stable spacing between character cards and the vertical scroll thumb.
- Endfield repositories now use the same GameBanana download-count enrichment, cache, and heat-score recalculation path as the other presets.

## v3.6.0 Highlights

- Optional startup update checks now prompt before automatically downloading, showing progress, installing, and restarting.
- Online characters can be favorited per repository and favorites stay pinned at the top.
- The backup manager lists creation time, affected mods, disk usage, and manual restore actions, with a configurable storage cap.
- SQLite now stores online pages, character catalogs, details, download metrics, version data, favorites, and the local mod file index while retaining legacy JSON fallback.
- Settings can export a sanitized diagnostic ZIP or open a prefilled GitHub issue for user review and submission.
- Added keyboard navigation, system focus visuals, screen-reader names, responsive layouts, high-contrast handling, and system animation preference support.

## v3.5.1 Highlights

- Fixed the online mod list collapsing into a narrow centered column with a large blank area after opening details.
- The online workspace now uses the width allocated by SplitView and recalculates the character rail and card columns after pane changes.

## v3.5.0 Highlights

- Added per-repository configuration profiles that can be saved, updated, applied, and deleted.
- Applying a profile only manages mods recognized in the active repository and preserves unknown target folders.
- Added pre-install conflict detection that reports duplicate relative file paths before continuing.
- Copy, remove, and profile operations now use transaction backups, automatically roll back on failure, and support manually undoing the latest change.
- Added a download task center with progress, cancellation, open-folder actions, and finished-task cleanup.

## v3.4.2 Highlights

- Obsolete online requests are cancelled, rate limits trigger exponential cooldown, and usable cache remains visible.
- The status bar distinguishes live data, cache age, stale cache, cooldown, and stopped requests.
- Online list and grid layouts now use virtualized containers and only create visible cards.
- Configuration files use atomic writes and rotating backups, with automatic recovery from a valid backup.
- App updates retain the latest three version backups and automatically roll back if the updated runtime cannot start.
- Automatic retries for the same online configuration stop after a timeout, preventing repeated loading and notification loops.
- Fixed mod cards moving over the search, sort, and pagination controls after selecting a character.
- Fixed right-edge clipping on the online page, standardized pagination button sizing, and removed redundant clipped tooltips.

## v3.4.1 Highlights

- Character catalogs use local cache first, with stale data shown immediately while a background refresh runs.
- Character avatars prefer the disk cache, limit uncached downloads to three concurrent requests, and cancel obsolete work.
- Image requests retry after rate limits or temporary server errors.
- Refreshing online mods no longer waits for the character catalog refresh.

## v3.4.0 Highlights

- Fixed character filters that loaded category counts but displayed no mods for Genshin Impact, Honkai: Star Rail, and Zenless Zone Zero.
- Uses official global English character names for better GameBanana category matching while retaining Chinese Wiki avatars, names, and links.
- Keeps online mod loading independent from the monthly Wiki character catalog refresh.
- Fills missing download counts sequentially at a low request rate and caches them for 12 hours.
- Repository dashboard cards support double-click switching.
- Release archives now contain only the application and required runtime files, without README or user-guide documents.

## v3.3.2 Highlights

- Missing download counts for Genshin Impact, Honkai: Star Rail, and Zenless Zone Zero mods are filled sequentially and cached locally.
- Repository dashboard cards support double-click switching.

- Uses official HoYoWiki English character names for Genshin Impact, Honkai: Star Rail, and Zenless Zone Zero category matching.
- Keeps Chinese names, character avatars, and Wiki links from the official Chinese Wiki with a monthly catalog cache.

- Reworked online browsing into character, mod-list, and details columns on wide windows, with a responsive character rail on smaller windows.
- Added Endfield Wiki character avatars and bilingual names, mapped to their corresponding GameBanana categories.
- Character filters now show a complete page of online results and avoid category index and missing-item errors.
- Improved the proportions and layout of mod cards, the details panel, image previews, and action buttons.
- Fixed scrollbars covering character or mod content and made webpage/download buttons visually consistent.

## Requirements

- Windows 10 version 1809 or later; Windows 11 is recommended.
- 64-bit Windows (`x64`).
- Online browsing, translation, and update checks require an internet connection.
- Release builds are self-contained WinUI 3 packages, so normal users generally do not need the .NET SDK or Visual Studio.
- `.7z`, `.rar`, `.zipx`, and `.cab` extraction uses the bundled 7-Zip files. Extract the entire release before running it.

## Download and Run

1. Open the [latest Release](https://github.com/uyujkk/Integrated_Mod_Manager/releases/latest).
2. Download `Integrated_Mod_Manager-vX.X.X.zip`.
3. Extract the whole archive to a writable folder. Do not run it from inside the archive.
4. Run `ModFolderCopier.exe` from the root folder.
5. On first launch, create or edit a repository and configure its mod storage folder, target folder, and optional launcher.

`ModFolderCopier.exe` is the launcher. The main WinUI executable is `WinUI3/ModFolderCopier.WinUI.exe`. Keep the release directory structure intact.

### SmartScreen

The application does not currently have a commercial code-signing certificate, so Windows SmartScreen may show an unknown-publisher warning. The source and build scripts are public in this repository. Download only from this project's Releases page; after verifying the source, use SmartScreen's "More info" option to decide whether to run it. Do not disable Microsoft Defender for this tool.

## Recommended Folder Layout

Each repository uses a two-level structure:

```text
Mod storage folder
├─ Character or category A
│  ├─ Mod A1
│  ├─ Mod A2
│  └─ Mod A3
├─ Character or category B
│  ├─ Mod B1
│  └─ Mod B2
└─ Character or category C
   └─ Mod C1
```

- First-level folders are categories such as games, characters, or outfit types.
- Second-level folders are the actual mods copied, removed, previewed, and annotated by the app.
- The target folder is usually the Mods directory read by XXMI or the game.

## Quick Start

1. Open the Repository section and create or select a repository.
2. Set Mod Storage to the root of the two-level structure above.
3. Set Target Folder to the directory read by the game or mod loader.
4. Optionally select a launcher executable such as XXMI Launcher.
5. Select Refresh.
6. Select a first-level category and then a second-level mod.
7. Double-click the mod or use the copy action to toggle it.

Toggle behavior:

- If the target folder does not contain a folder with the same name, the entire second-level mod is copied there.
- If a matching target folder already exists, repeating the action removes that target copy.
- Deleting the source mod is a separate action and always asks for confirmation.

## Interface and Workflows

### Dashboard

The dashboard summarizes all repositories or the selected repository, including category count, mod count, and path readiness. It provides a quick overview when managing several games.

### Repositories and Local Mods

Each repository stores its own:

- Repository name.
- Mod storage folder.
- Target folder.
- External launcher path.
- GameBanana source, target game, and skin category ID.
- Repository notes.

The first-level panel supports search, category creation, and rename. The second-level panel selects an individual mod and supports deletion, copy toggling, and archive drop import.

### Archive Import

Supported formats:

```text
.zip  .zipx  .7z  .rar  .cab
.tar  .tar.gz  .tar.bz2  .tar.xz  .tar.zst
.gz   .tgz     .bz2      .xz      .zst
```

Import methods:

- The top Import to Selected Folder action imports into the selected first-level category.
- After selecting a category, drop an archive on the second-level panel.
- Online downloads create a separate folder and extract automatically.

Extraction backends:

- `.zip` uses the built-in extraction API.
- `.7z`, `.rar`, `.zipx`, and `.cab` use the bundled 7-Zip executable.
- `.tar`, `.gz`, `.tgz`, `.bz2`, `.xz`, `.zst`, and `.tar.*` use Windows `tar.exe`.
- A single top-level folder is preserved when possible. Loose files or multiple roots are placed in a new mod folder.

### Shortcut Keys and Descriptions

- Each second-level mod can store up to 10 shortcut/action rows.
- Focus a shortcut field and press the desired key or combination.
- Single keys such as `1`, `Q`, and `F1` are supported.
- Combinations such as `Ctrl+1`, `Shift+F2`, and `Alt+Q` are supported.
- Symbols such as `/`, `;`, `[`, `]`, and `\` are supported.
- This is a note-taking feature for a mod's own controls; it does not register global Windows hotkeys.

### Image Preview and Mod Link

The app checks these names first:

```text
preview.*  cover.*  thumbnail.*  image.*
```

If none is found, it tries the first supported image in the mod folder. Supported image formats are `.png`, `.jpg`, `.jpeg`, `.bmp`, `.gif`, and `.webp`.

Select a second-level mod and drop an image from File Explorer onto the preview panel to copy and assign it. The link section below the preview stores a separate URL for the selected mod and opens it in the default browser.

## Online Mod Browser

The online browser currently focuses on GameBanana:

1. Select a repository in Settings and open its online category configuration.
2. Enter the source site, target game, and GameBanana skin category ID.
3. Open Online and refresh the list.
4. Use character filtering, keyword search, sorting, and pagination.
5. Inspect images, description, access warnings, and download actions in the independent detail panel.
6. Select a local destination, then download and extract.

Category names vary between games. The current filter accepts entries whose category name contains `Skins`; it does not require an exact `Skins` match.

Online entries can be sorted by hotness, downloads, likes, views, or update time. Hotness is capped at 10 and currently uses:

```text
min(10, 2 × (
    0.55 × log10(downloads + 1)
  + 0.30 × log10(likes + 1)
  + 0.15 × log10(views + 1)
))
```

Online content processing is best effort:

- Text mentioning Patreon, subscriptions, payments, like-based unlocks, delayed public releases, and similar requirements is surfaced as a possible access warning.
- The app attempts to extract shortcut keys and action descriptions from the mod text.
- In Chinese mode, action descriptions are translated when possible; English mode keeps the source text.
- Site changes, rate limits, authentication, CAPTCHA, custom download flows, and network conditions may prevent parsing or downloading.
- The application does not bypass payments, subscriptions, permission checks, or restrictions set by mod authors.

## Mod Updates and App Updates

After an online mod is downloaded successfully, the app stores its source URL, remote ID, preview URL, and last-known update time. The Updates section can:

- Check all tracked mods manually.
- Check on startup daily, every three days, weekly, or only manually.
- Deduplicate repeated source records.
- Open the online page or local folder.
- Remove a tracking record without deleting the local mod.

The app update checker in Settings reads the latest GitHub Release, displays its version and notes, and opens the Release page. After downloading a newer ZIP, place it in the current app folder. The next launch detects it and asks for confirmation; a separate updater then replaces files after the app exits and restarts it.

## Configuration and Backup

Local configuration is stored beside the WinUI executable:

- `config.ini`: theme, language, shortcut rows, mod links, and tracked online origins.
- `beta-shell.json`: repository list, selected page, online categories, and update-check settings.
- `startup.log`: created only when the launcher needs to record startup information.

Local ZIP updates preserve `config.ini` and `beta-shell.json` automatically. Backups are still recommended before moving the app manually. These files may contain local paths and should not be attached to issues or shared publicly without review.

## Build from Source

Development requirements:

- Windows 10/11 x64.
- Visual Studio 2022 or Visual Studio Build Tools 2022.
- .NET 8 SDK.
- MSBuild, Windows App SDK, and Windows SDK build support.
- Optional 7-Zip installation; the build script copies available `7z.exe`, `7z.dll`, and license files into the output.

Build:

```powershell
cmd /c build_winui.bat
```

Output:

```text
dist/
├─ ModFolderCopier.exe
└─ WinUI3/
   ├─ ModFolderCopier.WinUI.exe
   └─ ...
```

## Automated Tests

The repository now has 62 automated tests covering path boundaries, update verification, request cooldown state, SQLite caches/favorites/file indexes, file replacement, preserved configuration, and update rollback. Tests exercise the production assemblies referenced by the app instead of duplicate test-only implementations.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-all.ps1
```

GitHub Actions runs the following checks for pushes to `main`, pull requests, and manual workflow dispatches:

- Run three suites with 62 tests and collect Cobertura coverage.
- Enforce separate line and branch coverage gates for the core, data, and updater assemblies.
- Build WinUI 3 x64 Release, the outer launcher, and the local updater, then verify version consistency.
- Create a minimal ZIP without configuration, caches, logs, PDBs, or manuals and validate its required files and managed-file manifest.
- Upload `.trx` reports, coverage, verified build outputs, and the checked release ZIP.

## Repository Layout

```text
WinUI3/            WinUI 3 application source and assets
IntegratedModManager.Core/  Independently testable safety logic
IntegratedModManager.Data/  SQLite cache, favorites, and mod file index
Tests/             Core xUnit test project
DataStoreTests/    Real temporary SQLite database tests
UpdaterTests/      Local update transaction and rollback tests
scripts/           Unified tests, coverage gates, and package verification
.github/workflows/ GitHub Actions build and test workflow
WinUILauncher.cs   Outer launcher
build_winui.bat    Windows build and output preparation script
README.md          Chinese documentation
README.en.md       English documentation
快速使用手册.md     Chinese quick-start manual
Quick-Start.en.md  English quick-start manual
docs/              Complete Chinese and English user guides
使用说明.md         Compatibility entry for older links
CHANGELOG.md       Full bilingual release history
更新报告.md         Current release report
LICENSE            MIT License
```

`dist/`, build intermediates, and local configuration are excluded from source commits.

## Troubleshooting

### Why does a mod become "Not copied" after double-clicking it?

Copying is a toggle. If a matching mod already exists in the target folder, the second action removes the target copy.

### Why is the online list empty?

Verify the repository's game and category ID, confirm that the category name contains `Skins`, and check whether GameBanana is reachable. Retry later if the site is rate-limiting requests.

### Why does RAR extraction fail?

Make sure `WinUI3/Tools/7z.exe` and `7z.dll` were not removed or quarantined, and verify that the archive is not damaged or password-protected.

### Why is there no preview image?

Name an image `preview.png` or `cover.jpg`, or select the mod and drop an image onto the preview panel.

### Where is the online image cache?

It is stored in `WinUI3/cache/online-images` under the app directory. The app limits its size and removes older files automatically. Deleting this folder does not affect repositories or mod settings.

### Does update checking install updates automatically?

Never silently. After you place a downloaded newer release ZIP in the existing app folder, the next launch asks for confirmation before applying it.

## Release History

- [Full bilingual changelog](./CHANGELOG.md)
- [GitHub Releases](https://github.com/uyujkk/Integrated_Mod_Manager/releases)
- [Current Chinese update report](./更新报告.md)

## License and Notices

This project is released under the [MIT License](./LICENSE), copyright `uyujkk`. Third-party components retain their own licenses; bundled 7-Zip files include the relevant license text.

This tool manages local files and reads publicly available page data. It does not include game files or mod content. Copyright, licensing, and usage terms for each mod remain with its respective author.
