# 4.0 Repository and Combination Presets

Choose Files, Covers, or Presets at the top of Repositories. New configurations default to Files. Your view is remembered; switching views does not deploy Mods.

## Files and Covers

Select a character on the left, then search and select a Mod in the middle. Wide windows show Preview, Shortcuts, and State Slots on the right; narrow windows keep these behind Details. Deployment is at the bottom of Details. Selecting a Mod or opening Details does not deploy it. Double-click keeps the existing deployment action.

## Save a Combination with State

1. Deploy the desired Mods in the current repository.
2. Adjust them in game, press F10, and wait for the loader to finish saving persistent values.
3. Open Presets, enable persistent-state capture, and select the `d3dx_user.ini` actually used by that loader.
4. Use New Profile to name and save it. Update Profile updates the selected profile.

Disable state capture to save only the enabled Mod set. The left panel lists this repository's profiles; the middle shows members and saved-value counts.

## Restore

Close the game and loader first. Select a profile, choose Restore Combination, and review the proposed changes before confirming. Recognized deployments are restored first; matching numeric persistent values are written afterwards, preserving other Mods' values. Original Mod INIs are not rewritten, and the manager does not send game hotkeys.

Existing checks guard unknown target folders, namespace conflicts, duplicate deployment names, and concurrent changes. Backups remain accessible in Updates. This is not a promise of compatibility with every Mod, loader, or game, or a guarantee against account risk.

## Upgrade

### 4.1: portable combination bundles

Starting with 4.1.0, Presets has Export Bundle and Install Bundle. Export the selected preset's saved state and all corresponding Mod files; update the profile first for newer state. Installation preview lists new, identical/reusable and conflicting Mods. Different existing contents are not overwritten and folders are not automatically renamed.

Install and Save Preset creates a new profile, then optionally opens the existing restore preview. Close the game/loader before confirming restore. Deployment precedes targeted writes to local `d3dx_user.ini`; original Mod INIs and unrelated keys remain unchanged. Uncheck the option to install/save only. Bundles are unencrypted; inspect private files and redistribution permissions before sharing. Only this application's combination ZIP format is supported, not arbitrary Mod archives. See [bundle details](../development/Combination-Bundles.md).

4.1.0 is launched through `IntegratedModManager.exe`; it does not include demo repositories. Existing `config.ini`, `beta-shell.json`, profiles, and state-slot formats are retained; see the [upgrade guide](./Upgrade-v4.1.en.md). Do not copy simulated-repository test settings into a real game environment.

## Parameters and Backup Boundaries

- Only recognized numeric `global persist` values are supported. They must exist in the active parameter file when captured; Mod INI defaults do not substitute for missing state.
- Only recognized deployments in this repository are managed. Copy mode matches a unique folder name and cannot distinguish a manually created same-name copy, so review removals; junction destinations are verified.
- Individual state slots are in Mod Details. They store multiple states of the same Mod, not values transplanted to unrelated Mods.
- Combinations are stored in `WinUI3/beta-shell.json` under `ConfigurationProfiles`; individual slots under `ModPersistentSlots`. Renames, namespace changes, or new Mod versions may require recapturing.
- Parameter writes create adjacent `d3dx_user.ini.imm-persist-*.bak` files; deployment backups are in Updates. Deployment Undo does not undo parameters. Do not replace a file containing newer changes with an older whole-file backup.
- The manager does not inject, access game memory, send F10, or forcibly end the game. Confirm that the game and loader have exited yourself. File verification is not visual game verification.
