# Testing State Slots and F10 Hot Apply

How to test saving and restoring single-Mod state slots, and F10 hot apply, without starting a game. You use the repository's synthetic test data and a simulated loader, `scripts/fake-3dmigoto-loader.ps1`. Once that passes, confirm with a real game as in the last section.

[中文](./State-Slots-Hot-Apply-Testing.zh-CN.md)

## What the simulated loader does

A real 3DMigoto/XXMI loader handles `d3dx_user.ini` like this:

- `global persist` values changed in game live only in memory;
- a config reload (**F10**) first writes memory to `d3dx_user.ini`, then reads the file;
- exiting the game writes memory to the file.

So editing the file while the game runs does nothing on its own: the next F10 or exit overwrites it. Hot apply keeps the file read-only for a few seconds around F10, so the loader's write fails and the reload reads the new values.

The simulated loader reproduces that in a window:

- the grid is the loader's memory, and editing it is like toggling in game;
- pressing F10 with the window in front saves, then reloads, and the log says when the save was skipped because the file was read-only;
- closing the window saves memory to the file, like exiting the game.

## 1. Set up

1. Copy the test data to a new folder:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\scripts\fake-3dmigoto-loader.ps1 -CreateFixture D:\IMM-HotTest
   ```

2. In the manager, create a repository:
   - Mod storage folder: `D:\IMM-HotTest\ModRepository`
   - Target Mods folder: `D:\IMM-HotTest\Loader\Mods`
   - Leave directory links off; the test Mods are already deployed as copies.
3. Select `TestCharacter` → `TestModA`, open the State Slots tab, and set `d3dx_user.ini` to `D:\IMM-HotTest\Loader\d3dx_user.ini`.
4. Start the simulated loader:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\scripts\fake-3dmigoto-loader.ps1 -UserIni D:\IMM-HotTest\Loader\d3dx_user.ini
   ```

## 2. Save slots

1. In the loader, set `...\TestModA\mod.ini\hair` to `2` and press **F10** with its window in front. The log shows `saved memory` and `reloaded file -> hair=2`.
2. In the manager, click Save slot and name it "Hair 2". The list shows "Hair 2 · 2 · current".
3. Set `hair` to `0`, press F10, and save "Hair 0".
4. Try More… → Update selected slot, and Rename selected slot.

## 3. Restore with the game closed

1. Close the loader window; this is like exiting the game, and it writes memory to the file.
2. Select "Hair 2", click Restore loader state, check "I have fully closed the game", and confirm.
3. Check the result:
   - `d3dx_user.ini` contains `hair = 2`;
   - a `d3dx_user.ini.imm-persist-*.bak` backup sits beside it;
   - restarting the loader shows `hair` = 2.

## 4. F10 hot apply while running

1. Keep the loader running. On the Shortcuts page, choose the `Fake 3DMigoto loader (IMM test)` window and turn input on.
2. Select "Hair 0", click Restore loader state, check "The game is running: send F10 … after writing", and confirm.
3. Expected:
   - the loader comes to the front;
   - its log shows `F10: save skipped, d3dx_user.ini is read-only`, then `reloaded file -> hair=0`;
   - the manager's status line says F10 was sent;
   - the file is no longer read-only.
4. Control test:
   - restore "Hair 2" with only "game closed" checked, while the loader still runs;
   - press F10 in the loader yourself;
   - `hair` goes back to 0, because the loader's save overwrote the write.

## 5. Overlay hot switching

1. On the Shortcuts page, turn on "Switch state slots in the overlay" and select `TestModA`.
2. Press **Ctrl + Alt + K**; the overlay lists both slots, with ● on the current one.
3. Click the other slot. Expected: the same loader log as in section 4, and ● moves to the clicked slot.

## 6. Other actions

- **Write selected slot as Mod defaults**: `global persist $hair = …` in `ModRepository\TestCharacter\TestModA\mod.ini` changes to the slot's value, with a `.imm-persist-*.bak` beside it. In copy mode, `Loader\Mods\TestModA\mod.ini` changes too.
- **Whole snapshots**: save one, change values in the loader and press F10, then restore the snapshot. Either check hot reload, or close the loader first.
- **Import variant files (unverified)**: put the `variants.json` example from the Chinese guide in `ModRepository\TestCharacter\TestModA` and import it. Expected: two slots, with the active one marked current.

## 7. Confirm with a real game

1. Back up the loader's `d3dx_user.ini`, and use a test Mod with `global persist` variables.
2. Confirm the reload key is F10: 3DMigoto's `d3dx.ini` normally has `reload_config = no_modifiers VK_F10` in `[Hunting]`. Check your own file; hot apply currently always sends F10.
3. Games running as administrator (such as Endfield) need "Restart manager as administrator" on the Shortcuts page, or Windows drops the keys.
4. Toggle states in game, press F10, and save two slots.
5. Arm the game window, switch slots from the overlay, and watch the game change.
6. Restart the game and confirm it keeps the last switched slot.

When nothing changes, the status line says whether F10 did not arrive or the file was rewritten. Report that text together with the logs in `WinUI3\diagnostics`.
