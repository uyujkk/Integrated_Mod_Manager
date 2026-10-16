# Upgrade to 4.1.1

[中文](./Upgrade-v4.1.1.zh-CN.md) · [Bridge setup and risks](./Endfield-Bridge.md)

Download `Integrated_Mod_Manager-v4.1.1.zip` and its exact `.zip.sha256` sidecar. This is a portable ZIP, not an MSI. Extract the entire folder and run `IntegratedModManager.exe`. Windows x64 and .NET 8 x64 runtime are required.

1. Check for updates in the previous version's Settings, or extract manually to a new directory. Exit the app and back up its folder first. Do not run two managers against the same target.
2. Automatic updates retain repositories, presets, paths, interface settings and user cache. For manual migration, close both apps and copy your own `WinUI3/config.ini` and `WinUI3/beta-shell.json`; migrate cache and backups as needed, without copying old program files over new ones.
3. An Endfield repository receives a one-time optional bridge offer. Decline to keep the existing loader. Risk acknowledgement is not checked by default.
4. Downloading verifies and installs the bridge separately. First use opens configuration only. Once configured, Run Launcher uses the chosen method.

If EFMI was adapted, restore it inside the bridge before moving the bridge, migrating directories or switching to the original loader. Back up its `data` directory and reconfigure as instructed. Do not delete the entire cache.

Third-party launching and Mods may lead to account penalties or bans. Signatures, checksums and tests do not guarantee account safety. If you cannot accept the risk, use the official launcher without third-party Mods.

```powershell
Get-FileHash -LiteralPath ./Integrated_Mod_Manager-v4.1.1.zip -Algorithm SHA256
Get-Content -LiteralPath ./Integrated_Mod_Manager-v4.1.1.zip.sha256
```

Both the hash and filename must match. Developer Tools keeps its separate version and checksum; do not use that sidecar to verify the main app. Preserve the old app and logs when troubleshooting; do not delete configuration.
