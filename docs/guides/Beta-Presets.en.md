# 4.0-beta: mod combinations and persistent-state presets

> Historical guide for the separately archived beta package. Current stable users should read the [4.0 preset guide](./Combination-Presets.en.md) and [upgrade guide](./Upgrade-v4.0.en.md). The beta launcher/demo instructions below do not apply to the stable package.

## Install separately

Extract the entire ZIP into a new writable folder and run `IntegratedModManager-Beta.exe`. Keep the adjacent `WinUI3` and `TestData` folders. Windows x64 and the .NET 8 x64 runtime are required. Windows App SDK runtime files are included.

This beta cannot check, download, or install application updates, even if imported settings enable them. Do not extract it over the stable application. Online mod updates are a separate feature and remain available.

On first start, an offline demo repository is created. Its INIs are synthetic fixtures, not playable mods. Use them before testing an important real setup.

## Save and restore a combination

1. Configure a separate repository with the correct two-level `Character/Mod` library and loader `Mods` target. Select the active loader's `d3dx_user.ini` on the Updates page.
2. Load your chosen mods in the game, adjust their appearance, and have the loader save the latest values (normally F10; check your loader's behavior and the file timestamp).
3. On Updates, enable “Include d3dx_user.ini values when creating/updating” and create a named combination. It saves recognized deployed mods and their numeric persistent values, matched by full namespace.
4. To restore, fully exit the game and loader. Choose the preset, inspect its install/remove list and parameter file, confirm that both have exited, then restore.
5. The app backs up affected deployment folders, restores the mod combination, verifies it, then patches and re-reads the saved values. Restart the loader and game to verify the actual appearance.

Disable the parameter checkbox when creating/updating to save only an enabled-mod list. Existing presets retain their own saved contents. Updating replaces the selected preset after confirmation.

Only recognized members of the selected repository are managed. Unknown target folders and other repositories are preserved. Copy deployments are identified by a unique matching folder name; review the removal list because a manually created same-name copy cannot be distinguished. Junction deployments are checked against their actual destination. Missing or disabled sources, duplicate names, namespace collisions, malformed values, or concurrent changes stop restoration.

## Individual mod slots

On Repositories, select a deployed mod and open State Slots. Capture named A/B slots from the active loader's saved `d3dx_user.ini`. Restore after exiting the game and loader, while that mod is correctly deployed. Slots are isolated by repository and mod path; they do not copy one mod's settings into an unrelated mod.

## Offline demo

The initial demo deploys TestModA and TestModB. Save combination A from `TestData/Loader/d3dx_user.ini`, then combination B from `TestData/StateB/d3dx_user.ini`. Select Loader's working file again and restore B: A's hair=2/coat=1 and B's style=3. Restore A: hair=1/coat=0/style=2. Temporary and unrelated values, comments, and source Mod INIs must remain unchanged. A no-op restore must not write another backup.

## Safety, backups, and limits

- Mod INI files are read for declarations and namespaces, never rewritten. Only matching numeric `global persist` values in the selected `d3dx_user.ini` are patched. UTF-8, BOM UTF-8, and BOM UTF-16 are supported; other encodings and ambiguous declarations are rejected.
- Saved values must exist in the runtime file when capturing. Default values are not substituted for missing live values. Stale keys alone do not prove that the loader has saved the latest state.
- A successful file check is not proof of game rendering. The user confirmed successful combination and state restoration in their current setup; other setups require real testing.
- No game memory access, DLL injection, synthetic F10, or forced game termination is used. The app cannot prove that you exited the loader; you must confirm it yourself.
- Parameter backups are `d3dx_user.ini.imm-persist-*.bak`. They contain the entire file before that write. Do not overwrite later changes with an older backup without comparing them first. Deployment Undo does not undo parameter changes.
- Slots live in `WinUI3/beta-shell.json` under `ModPersistentSlots`; combinations under `ConfigurationProfiles`. Back up this file and your mod sources. Renames and namespace changes may require recapturing.
- To migrate from R2/R3, close both apps and copy their `WinUI3/config.ini` and `WinUI3/beta-shell.json` into this beta's WinUI3 directory. Never overwrite the stable app or distribute your private configuration in a release.

For feedback, include the game/loader version, mod name, copy/junction mode, captured value count, and before/after behavior. Redact private paths and do not upload your entire runtime configuration.
