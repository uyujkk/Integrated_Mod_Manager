# Upgrade to 4.0.0

[中文](./Upgrade-v4.0.zh-CN.md) · [Combination and State Presets](./Combination-Presets.en.md) · [Stable Download](https://github.com/uyujkk/Integrated_Mod_Manager/releases/tag/v4.0.0)

## New Installation

Requires Windows 10 version 1809 or later, x64, and the .NET 8 x64 runtime. Windows App SDK files are bundled; Visual Studio and the .NET SDK are not required.

Download `Integrated_Mod_Manager-v4.0.0.zip` and its matching `.zip.sha256`, fully extract to a writable folder, and run the root `IntegratedModManager.exe`. This is a portable ZIP application, not an MSI installer. Do not copy only the EXE or run inside the ZIP.

## From Stable 3.x

1. Exit the manager and back up its entire folder. For presets, also back up the Mod library, target Mods directory, and active loader's `d3dx_user.ini`.
2. Reopen the old stable app, check app updates in Settings, and confirm 4.0.0. The updater preserves settings, caches, and backups and attempts rollback on failure.
3. Alternatively, extract the stable package separately. With both apps closed, copy the old `WinUI3/config.ini`, `WinUI3/beta-shell.json`, and any needed `WinUI3/cache` and `WinUI3/backups` into the matching locations. Do not overwrite new application binaries.
4. On first launch, check the selected repository, source and target paths, launcher, and deployment mode before making changes.

Repositories and presets remain usable when their paths stay valid. After changing computers, drives, Mod names, or namespaces, correct the paths and recheck compatibility; old state may no longer match.

## From the Separate 4.0-beta

The beta has no app self-updates. Fully extract the stable package into a new folder. Close both apps, then copy your own `WinUI3/config.ini` and `WinUI3/beta-shell.json` into the stable app's matching locations. The combination and state-slot formats are retained.

Do not copy the beta's demo repository, TestData, or test-app settings over a real game setup. Launch through `IntegratedModManager.exe`, not `IntegratedModManager-Beta.exe`. Never let two managers operate on the same deployment or parameter file at once.

## First Preset Check

Use a nonessential set first: deploy → adjust in game → F10/save → capture the set and values → close the game and loader → review restoration → restore → restart and check appearance. Confirm that unrelated values and original Mod INIs remain unchanged.

Only recognized numeric `global persist` values are supported. This is not universal live restoration for arbitrary Mods. Parameter backups are beside `d3dx_user.ini`; deployment Undo does not undo parameters. Do not manually bypass checks when a preset is unrecognized or conflicting.

## Verify the Download

In the ZIP directory:

```powershell
Get-FileHash -LiteralPath .\Integrated_Mod_Manager-v4.0.0.zip -Algorithm SHA256
Get-Content -LiteralPath .\Integrated_Mod_Manager-v4.0.0.zip.sha256
```

Both the hash and filename must match. Do not use the standalone Developer Tools checksum; that tool remains separately released at 3.9.5 and is not included here.

If upgrading fails, preserve the old app and logs and try a full extraction in a new folder. Do not delete user settings to troubleshoot. Redact private paths and parameter content before reporting a problem.
