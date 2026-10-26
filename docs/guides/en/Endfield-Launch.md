# Integrated Endfield Launch

> For 4.3 and later. [中文](../zh-CN/终末地启动.md) · [User guide](./User-Guide.md)

Endfield repositories can use the **integrated Endfield launch**. The manager creates the game directly under the officially signed `Games.exe`, and hands off to XXMI / EFMI when Mods are wanted. The component is built in, so EndfieldBridge no longer needs a separate download.

Open it from Dashboard → Current Repository Paths → **Launch method**. The choice is saved per repository and does not affect other games. Opening the settings never starts the game.

## Setup

1. Choose the **game executable**: `Endfield.exe` in the game folder, next to `Endfield_Data`.
2. The **official launcher** may stay empty. The manager then finds the official `Games.exe` and verifies its signature, and remembers the last verified file to try first next time.
3. For Mods, select the actual **XXMI launcher** and turn on *Load Mods through XXMI / EFMI*.
4. Use **Check** to verify the game path and the official signature. With Mods on, it also checks that the EFMI setup is complete.
5. Before the first Mod launch, exit the game and XXMI, then use **Back up and set up EFMI**. The XXMI configuration is backed up, only seven EFMI launch fields change, and the first restore point is kept.
6. Enable integrated launch and confirm the risk before setting up EFMI, then save. Setup immediately writes both the XXMI configuration and the profile its callback needs; cancelling or closing the settings does not undo completed setup. **Run Launcher** then shows one UAC prompt and launches in the background, without a bridge window.

After setup, XXMI callbacks can read the saved paths and arguments without a prior launch from the manager. Run XXMI as administrator when using it independently, and save after editing settings. An active launch session blocks replacement of its callback profile.

On the first XXMI launch, XXMI reports an unverified custom command. Check that it points to this manager's `ModFolderCopier.WinUI.exe --endfield-game-only --from-xxmi`, then choose **Keep**. Choosing *Reset* clears the command.

## Switching back

Choosing *Existing loader* only changes the manager's entry. If EFMI was set up, use **Restore EFMI** first, which returns the seven fields to their values before setup. Restore before moving the manager folder too, because XXMI's command contains the manager's absolute path.

Restore checks the fields installed during setup. Later changes to the game folder, launch mode or process settings stop restoration and leave the current configuration intact. XXMI's later command authorization signature and unrelated settings do not block restoration. For an older restore point without an installed-field record, run **Back up and set up EFMI** again before restoring; the original pre-bridge fields are retained. Restore the current XXMI installation before adapting a different one, so its restore point cannot be lost. If that installation has been removed, its restore point can no longer be used; setting up the new installation retires it automatically (the file stays in `backups/endfield-launch` and is not deleted). The game folder is compared as a path, so XXMI rewriting its case or separators does not block restoration. Next to the XXMI configuration, the first `.imm-endfield-*.bak` backup and the five newest are kept.

## Migrating from the standalone EndfieldBridge

Opening the settings or running the launcher imports settings from `cache/optional-tools/EndfieldBridge-0.3.2-win-x64` and adopts the old bridge's EFMI restore point. If EFMI was set up with the old bridge, use **Back up and set up EFMI** once to point the command at the manager; the original restore point is kept. Then choose *Keep* in XXMI once more. Afterwards you may delete the old bridge folder yourself.

## Risks

The integrated launch changes the normal launch chain. Loading Mods through XXMI / EFMI involves third-party DLL injection and may lead to account penalties or bans. Signature checks, successful launches and passing tests do not establish official approval or account safety. If you cannot accept the risk, use the official launcher without third-party Mods.

Launching requires administrator rights. Settings, logs, restore points and launch status are stored under the manager's `backups/endfield-launch` folder and may contain local paths. The manager does not read account passwords, upload logs, or read XXMI private keys.

## License

The launch component derives from EndfieldBridge 0.3.2 and is licensed AGPL-3.0-only. Its launch core was adapted from endfield-nolauncher / Better-Endfield. Manager builds that include it are distributed as a whole under AGPL-3.0-only; other repository files remain MIT. See [LICENSING.md](../../LICENSING.md) and the [component NOTICE](../../../src/IntegratedModManager.EndfieldLaunch/NOTICE.md).

## Manager 4.1.1–4.2.0 (standalone bridge)

These versions offered a separately downloaded EndfieldBridge 0.3.2 (pinned, SHA-256 verified) installed under `cache/optional-tools`, with its own configuration window and `--launch` entry. Its release, risk statement and source remain at [uyujkk/EndfieldBridge](https://github.com/uyujkk/EndfieldBridge/releases/tag/v0.3.2).
