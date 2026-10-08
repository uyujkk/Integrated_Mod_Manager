# Combination bundle opt9 / 组合包窗口检查点

Date: 2026-10-07. Local development branch, based on v4.0.0. No push, GitHub release, version bump or update-channel change. All paths and synthetic assets are under the G-drive workspace.

## Repairs found by native testing / 实际窗口发现并修复

- The opt8 app crashed during MainWindow construction: CreateConfigurationProfileButton was added to a second parent while its original detached grid was not visible to the global detach traversal. The opt9 implementation keeps Create in its original parent and places new bundle actions in a separate shared toolbar. The preserved initial failure log is `artifacts/native-combination-bundle-opt9-20261007/startup-initial-failure.log`.
- Export/Install/active Cancel no longer occupy three/four vertical rows below the narrow preset list. Short windows use inline recovery when at least 720 logical workspace pixels are available; tall narrow windows stack recovery. Member and recovery details scroll independently, with primary actions outside those scroll regions.
- Chinese localization trimmed boundary whitespace from fragments, joining summary counts, backup paths and instructions. Resolve now retains those separators while keeping text normalization and fallback semantics. Production localization source is linked into regression tests.
- During combination operations, refresh/import/launcher controls and the persistent-state path editor are explicitly disabled, not just navigation and profile controls.

## Native observations / 可见窗口结果

Windows computer-use checks operated only the independently extracted test app. App configuration selected `App/TestData/ImportRepository`, `ImportLoader/Mods` and `ImportLoader/d3dx_user.ini`; no real game/loader path was configured, and startup app-update checks were disabled.

1. Original opt8 startup failure reproduced; fixed app reached a visible main window. Final opt9-r2 runtime also restarted successfully.
2. Native ZIP picker opened the demo combination; installation preview showed all 8 Mods and 16 saved values as new installs.
3. Install and Save Preset created the repository files and one persisted profile. The subsequent restore preview showed enable 8, remove 0, add 16 parameters. Restore stayed disabled until the closed-game/loader checkbox was checked.
4. Approved restore completed in the mock target. Read-only disk checks verified 8 source and 8 deployed mod.ini files; every source/target payload SHA-256 matched the original manifest, all 16 runtime entries matched the saved profile, and `$\Mods\External-keep\mod.ini\unrelated = 99 ; keep untouched` remained present. A state backup was created. This proves synthetic disk behavior, not in-game rendering.
5. Normal close and restart retained the profile, 8 members and 16 saved parameters. Final short window was about 903 x 591 logical pixels, with all primary actions visible; scrolling changed the member-list position without moving the page toolbar/footer. A wide window and tall narrow recovery layout were also inspected.
6. Export confirmation and the native folder picker were opened against the mock repository. Closing the picker cancelled export, returned the page to idle and reenabled its actions; no ZIP was created and the existing profile remained. Completing export and reimporting that newly exported ZIP remain pending native checks; the production export/file/state contracts passed offline tests. The native picker was larger than the short main window, so the automation's bounded targeting required moving its title bar before closing it; this is not evidence of application export failure.

The current display's full scaling matrix was not changed or measured anew. These observations are not proof of 100/150/200% compatibility. Chinese light-theme layouts and the English dark-theme maximized combination workspace were visibly inspected after switching language/theme through Settings. Some existing shared dialogs still use the application default/dark appearance when the main window is light. Theme consistency and the full bilingual/window-size matrix remain pending.

## Automated final run / 最终自动验证

`scripts/test-all.ps1 -ArtifactsDirectory artifacts/optimization-native-bundle-verified-opt9-r2-20261007`

- 708 tests: Core 657, DataStore 24, UpdateAgent 27; none failed or skipped.
- Coverage gates passed: Core line 96.7% / branch 91.8%; DataStore 88.6% / 89.1%; UpdateAgent 60.2% / 60.6%. Four coverage-gate self checks also passed.
- WinUI x64 Release and all launcher/updater builds passed, zero warnings/errors. Minimal runtime ZIP contract passed.
- Final clean runtime ZIP: `artifacts/optimization-native-bundle-verified-opt9-r2-20261007/ReleasePackage/Integrated_Mod_Manager-v4.0.0.zip`.
- SHA-256: `312EFAB531BAC4C8E41B8A869A284A972F807B8631CE2262026132DC1F62BFEC`.
- Existing stable v4.0.0 ZIP remained unchanged: `6099EC3CB19BAA655F0E2509246E313C1714A702FB33A2BEB952302901935E31`.

The clean local test package contains fresh synthetic data, not the exercised window's config, backups/cache or runtime state. See its `开始测试.md` for manual path setup after extraction. Internal version remains 4.0.0; opt9 labels distinguish this unreleased checkpoint.

Local test ZIP: `artifacts/local-test-combination-bundle-opt9-20261007/Integrated_Mod_Manager-v4.0.0-combination-bundle-opt9.zip`, SHA-256 `083752C4F3AC3AF468F0D0CF29517A41D889AB614A15136266B8A4C141141350`. Every payload was read back, expected runtime/demo/instructions entries were checked, and exercised `config.ini`, `beta-shell.json` and startup logs were absent. The exercise app remains separate in `artifacts/native-combination-bundle-opt9-20261007/App`.

## Still pending / 后续

- Finish native export/reimport, reuse/conflict/import-only and cancel-after-commit workflows, and cancellation during large file copy/hash operations.
- Simulate disk/config write failure and stale previews at the window layer; offline tests already cover the corresponding core contracts.
- Complete DPI/language/theme and large-repository responsiveness matrices, including checking shared-dialog theme inheritance.
- Real-game/XXMI validation only after user approval. Never claim compatibility, account safety or live restoration from this mock run.
- Release-candidate upgrade/rollback and publication only after explicit approval; preserve the stable app and automatic-update endpoint meanwhile.
