# Combination Presets and Bundles

> For 4.6.0. [中文](../zh-CN/组合预设.md) · [User guide](./User-Guide.md)

A combination preset records **which Mods are enabled** in the current repository, optionally with those Mods' values from `d3dx_user.ini`. Restoring brings back the Mod set first, then writes the values. A bundle exports a preset together with its Mod files so it can be installed in another repository.

To keep several sets of values for **one Mod**, use [state slots](./State-Slots.md) instead.

![Combination preset workspace with synthetic members and saved-value counts](../../assets/screenshots/combination-presets-v4.1.png)

## Saving a combination

Switch to **Presets** at the top of Repositories.

1. Deploy the Mods you want in the current repository.
2. To include values: set them up in game and press **F10** so the loader writes them.
3. Enable persistent-state capture and select the `d3dx_user.ini` the loader actually uses.
4. Use **New Profile** to name and save it; **Update Profile** overwrites the selected one.

Turn state capture off to save only the enabled set. The left panel lists this repository's presets; the middle shows members and value counts.

**Saving in game:** the overlay's **Save preset** sends F10 to the game window first and saves only after the loader has written (two matching reads); if nothing is written within 5 seconds, nothing is saved. Without a `d3dx_user.ini`, it saves the Mod set alone. See [Shortcut panel and overlay](./Shortcut-Panel.md).

## Restoring a combination

1. Close the game and loader.
2. Select a preset, choose **Restore Combination**, and review the Mods to install or remove and the value changes.
3. On confirmation, recognized deployments are restored first, then matching numeric persistent values are written; other Mods' values stay as they are.

- With directory links, if the preset enables several Mods of a single-selection character, you are asked first.
- Unknown target folders, namespace conflicts, duplicate deployment names or concurrent changes stop the restore and leave things as they were.
- Restoring never rewrites a Mod's original INI and never sends keys to the game.

## Bundles

**Export Bundle** and **Install Bundle** in Presets move combinations between repositories:

- **Export** packs the selected preset's **saved** values and all its Mod files. Update the profile first to include the latest state.
- **Install** previews first, listing new Mods, identical Mods that can be reused, and conflicts. Existing Mods with different contents are never overwritten or renamed.
- After confirmation the bundle is saved as a new preset and, by default, the restore preview opens; uncheck it to install and save only. Close the game and loader before restoring.
- Only combination ZIPs made by this app are accepted, not ordinary Mod archives. Bundles are not encrypted; check private files and the authors' permissions before sharing.

Format and testing details: [Combination bundles](../../development/Combination-Bundles.md).

## Values and backups

- Only recognized numeric `global persist` values are supported, and they must already be in `d3dx_user.ini` when saved; Mod defaults never stand in for missing values.
- Only recognized deployments in this repository are managed. Copy mode matches a unique folder name and can't tell a hand-made copy with the same name apart, so review removals; directory links are checked against their real targets.
- Presets are stored in `WinUI3/beta-shell.json` under `ConfigurationProfiles`. Renaming folders, changing namespaces or updating Mods may require saving again.
- Value writes create `d3dx_user.ini.imm-persist-*.bak` beside the file; deployment backups are on the Updates page. Undoing a deployment does not undo values; don't overwrite newer changes with an old whole-file backup.
- A file check is not an in-game check. This feature doesn't guarantee compatibility with every Mod, loader or game, or account safety.
