# Browse and Download

> For 4.5.2. [中文](../zh-CN/浏览与下载.md) · [User guide](./User-Guide.md)

The Online page browses GameBanana Mods by character; downloads are extracted and installed into the repository with their source recorded, so updates can be tracked. This page also covers the download task center, importing an old library, and local tags.

## Online source

Each repository can set its target game, skin category ID, source site and Wiki address in Settings. Presets cover Endfield, Zenless Zone Zero, Genshin Impact and Honkai: Star Rail; other games can use their own category. Any category name containing `Skins` is accepted. The character catalog (avatars, localized names, order) is cached separately from the Mod list and refreshes in the background after a day.

Online data comes from external sites; a proxy may be needed if loading is slow or fails. During HTTP 429 rate limiting, cached data is shown and you can refresh later.

## Browsing

- **Characters:** browse by character with avatars and names in the app language; the star button pins a favorite character (stored per repository). **All** shows the whole category.
- **Search and sort:** search matches character and Mod names; sort by heat, downloads, likes, views or update time. Heat is capped at 10 and uses log-weighted downloads, likes and views.
- **Layout:** list view for details, grid view for images; paging loads the full next page.
- **Authors:** click an author to browse their work across pages in the current category; right-click a card to favorite the author. `author:name` searches only the current page.
- **Page filters:** the count of matches on the current page appears under the search box; **All on page** toggles showing only installed or saved-for-later items.
- **Browsing position:** author, character, page, search, sort, layout and scroll position are kept per repository and restored after restarting, without downloading anything. Changing a filter or page starts at the top.

## Details and images

**Details** opens a side panel with author, character, category, update date, images, description and actions. Click the main image for the full-size viewer; use Left/Right and `Esc` to close. Descriptions can be translated automatically (experimental) and may be inaccurate.

The app tries to detect Patreon, subscriptions, payment, like-to-unlock, delayed free releases and dependencies and shows a hint; the author's page is authoritative. It never bypasses logins, captchas, payments, subscriptions or permissions.

## Download and install

1. Click **Download** on a card or in Details. When an item has several files, the newest usable archive is chosen by default; you can pick another.
2. Confirm the destination character folder; if the character has no folder yet, you can create one after confirming. With **Remember confirmed download folders per repository and character** on, confirmed locations are reused (and asked again if a folder no longer exists).
3. The archive is extracted and installed, its source link and preview are recorded, and it is marked installed.

- **Duplicate hints:** you are told when the same file is already installed, when another variant of the same item is installed, or when an old record can't be matched.
- **Save for later:** right-click a card and choose **Save for later** (stored per repository, up to 100 items); in **Authors and saved Mods** (star button) select several, review files, folders and duplicates once, then queue them.
- **Concurrency:** **Concurrent download tasks** can be 1–4 (default 2), set with the gear button on the Online page.

## Download task center

On the Updates page, watch download and extraction progress and results, cancel, open output folders or clear entries. **Retry** a failed task with its original repository, folder and file; you are told if the folder no longer exists or the file has been removed from the site. Tasks unfinished at exit are marked interrupted; after restarting, confirm the folder to download again. Nothing resumes automatically, and partial downloads are not continued.

## Importing an old library

**Organized import**, next to the view buttons on the Repository page, copies old Mod folders into the current repository:

1. Choose the old library and a target character folder. Direct subfolders of the old library that contain an INI become candidate Mods.
2. Preview, then copy. The Mod's inner structure is kept and the originals are untouched. An existing folder with the same name stops the import; nothing is merged or overwritten.
3. **Undo organized import** removes unmodified imported copies; copies that were changed, gained files or can't be accessed are kept and reported.

Characters are never guessed. After an unexpected exit, use **Check interrupted import** in the import menu.

## Local tags and filters

**Tags**, next to the repository search box, adds, removes or replaces tags in bulk; right-click a Mod to edit its tags. **Filters** filters by tag and can save a character, search and tag combination. Each Mod can have up to 16 tags, and tags are never written into Mod files.

## Update tracking

Online installs are tracked. The Updates page checks manually or on a schedule, tells you when a new version exists, and can **Download and replace** (old files are backed up first and the preview is kept). Deleting a tracking record never deletes the local Mod.
