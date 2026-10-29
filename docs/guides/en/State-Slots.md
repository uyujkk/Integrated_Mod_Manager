# Single-Mod State Slots

> For 4.5 and later. [中文](../zh-CN/状态槽位.md) · [User guide](./User-Guide.md)

Many Mods keep their look (outfit, hair, accessories) in `global persist` variables, and the loader stores the current values in `d3dx_user.ini`. State slots save those values for **one Mod** as named sets you can switch back to.

<img src="../../assets/screenshots/state-slots-v4.6.png" width="300" alt="State Slots tab: a selected slot marked current with its saved values">

## Saving a slot

1. Set the Mod up in game and press **F10** so the loader writes its values to `d3dx_user.ini`.
2. In the manager, select the Mod and open the **State Slots** tab. The first time, choose the `d3dx_user.ini` the loader actually uses.
3. Click **Save Slot** and give it a name.

- Only the numeric `global persist` values declared by this Mod are saved; other Mods are untouched.
- The values must already be in `d3dx_user.ini`; missing ones are never replaced by the Mod's defaults.
- Each Mod can have several slots; the one saved, updated or restored last is marked current.
- A Mod newly added to the repository that declares `global persist` values gets a slot named "Default" when the repository is scanned, holding the defaults from its own INI files (0 where none is written). In the Chinese UI it shows as "默认"; once renamed, the name no longer follows the language. Deleting it does not bring it back, and Mods already in the repository do not get one.
- In game you can also use the overlay's **Save single-Mod preset**, which sends F10 for you; see [Shortcut panel and overlay](./Shortcut-Panel.md).

## Restoring a slot

- **Game closed:** select the slot and click **Restore Loader State**. Only that slot's keys are written; other Mods' values stay as they are, and the file is backed up first.
- **Game running:** check "The game is running: send F10 to the game window after writing (experimental)"; see Hot apply below.

## The More… menu

- **Update selected slot with current values:** reads `d3dx_user.ini` again and replaces the selected slot.
- **Rename selected slot.**
- **Write selected slot as Mod defaults:** writes the values into `global persist $variable = value` in the Mod's own INI files, so they survive clearing or deleting `d3dx_user.ini`.
  - Each file is first backed up beside it as `.imm-persist-*.bak`; the result is read back, and a failure restores the originals.
  - In copy mode the deployed copy changes too.
  - Updating the Mod replaces these edits; the old files stay in the update backup.
- **Import variant file (unverified):** reads a variant JSON exported by another tool from the Mod folder (or a chosen file) and adds one slot per variant; the variant marked active becomes current. Keys that don't match this Mod's declarations are skipped and counted. This hasn't been checked with a real exported file yet, so confirm the result in game.
- **Whole `d3dx_user.ini` snapshots:** save, restore and open the snapshot folder.
  - A snapshot keeps the entire file, per repository, in `WinUI3/snapshots/user-ini/`, as a manual restore point.
  - Restoring returns **every** Mod's values to the snapshot. The current file is backed up as `.imm-snapshot-*.bak` first; close the game completely or use hot apply.

## Hot apply (experimental)

While the game runs, the loader writes its in-memory values back to `d3dx_user.ini` on F10 and on exit, so editing the file alone does nothing. Hot apply writes the slot's values, keeps `d3dx_user.ini` read-only for a few seconds around F10 so the loader reads the new values, then restores the file's attributes.

- Choose the game window and turn input on in the Shortcuts page first; Endfield needs the manager to run as administrator.
- In the main window, check "The game is running: send F10 to the game window after writing (experimental)" when restoring a slot or snapshot.
- With "Switch state slots in the overlay" on (Shortcuts page), the overlay lists the current Mod's slots and switches with one click.
- It relies on the loader saving and then reading `d3dx_user.ini` on F10 (the 3DMigoto/XXMI default). If F10 doesn't arrive, the written values are overwritten when the game exits, and the status line says so.

## Data and backups

- Slots are stored in `WinUI3/beta-shell.json` under `ModPersistentSlots`. Renaming folders, changing namespaces or updating the Mod may require saving again.
- Parameter writes keep backups beside the parameter file: `d3dx_user.ini.imm-persist-*.bak` (slot restores) and `.imm-snapshot-*.bak` (snapshot restores). Undoing a deployment does not undo parameters; don't overwrite newer valid changes with an old whole-file backup.
- A successful file write doesn't prove the game renders correctly; check in game.

Developer testing with a simulated loader: [Testing state slots and F10 hot apply](../../development/State-Slots-Hot-Apply-Testing.en.md).
