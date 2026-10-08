# Upgrade to 4.1.0

[中文](./Upgrade-v4.1.zh-CN.md) · [Preset guide](./Combination-Presets.en.md)

Download `Integrated_Mod_Manager-v4.1.0.zip` and its exact `.zip.sha256` sidecar from the official Release. Fully extract into a writable folder and run `IntegratedModManager.exe`. Keep the full directory structure; this is a portable ZIP, not an MSI.

1. Close the manager and back up its installation folder. Also back up your library, target Mods directory and loader's `d3dx_user.ini` before restoring presets.
2. Stable 4.0 users can use Settings → app update after publication. For manual migration, extract into a new folder and, with both apps closed, copy your own `WinUI3/config.ini` and `WinUI3/beta-shell.json` into the matching locations; copy caches/backups only if needed. Do not overwrite new binaries with old ones.
3. Check repository paths, launcher and deployment mode on first launch. Never run two managers against the same target.
4. New Export Bundle / Install Bundle buttons migrate saved combinations and complete Mod folders, not machine-specific repository paths. Update the preset first if you want newer saved state.

Original Mod INI defaults and unrelated loader values are not rewritten. Close the game/loader before restoring. Import validates files but never executes them. Review private content and author permissions before sharing. Normal Mod archives are not combination bundles.

Windows x64 and the .NET 8 x64 runtime remain required. Developer Tools stays separate at 3.9.5; do not use its checksum for the main app. Historical beta installations have no self-update: migrate your own settings manually, not their demo repositories.

```powershell
Get-FileHash -LiteralPath ./Integrated_Mod_Manager-v4.1.0.zip -Algorithm SHA256
Get-Content -LiteralPath ./Integrated_Mod_Manager-v4.1.0.zip.sha256
```

The hash and filename must match. On failure, retain the old app and logs; do not delete your configuration to troubleshoot.
