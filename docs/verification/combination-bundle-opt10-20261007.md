# Combination bundle opt10 / 组合包复用与取消检查点

Date: 2026-10-07. Branch `optimization/repository-scan`, internal version 4.0.0. Local verification only: no push, public release, version bump or automatic-update change. Existing development changes are retained. No real repository, game, Mod asset or XXMI configuration was used.

## Isolated native workflow / 隔离窗口流程

The test application was copied from the locally verified online-image stream-fix runtime into `artifacts/native-bundle-opt10-20261007/App`. `scripts/new-combination-bundle-fixture.ps1` generated fresh `TestData` with eight text-only Mods, four categories and sixteen saved numeric values. Repository `opt10-mock-import` points only to its `ImportRepository`, `ImportLoader/Mods` and `ImportLoader/d3dx_user.ini`. Startup update checks were disabled and no launcher was configured.

Computer-use checks operated the visible Chinese dark-theme window, approximately 1908 × 1019 logical pixels. This was not a new measurement of display scaling and does not prove the 100/150/200% matrix.

| Native action | Observed result and disk readback |
| --- | --- |
| Open import picker and cancel | Page returned to idle; no initial source installation or profile. |
| Import demo ZIP with restore option unchecked | Preview listed eight new installs. Saved one profile with eight members/sixteen values and eight source INIs. Deployment target stayed empty; runtime state stayed byte-identical. |
| Import same ZIP again, keeping optional restore enabled | Preview listed eight exact-file reuse rows. Saved `Synthetic bundle demo (2)` with a different profile ID, eight members and sixteen values. Source file count remained eight. |
| Cancel the subsequent restore confirmation | Restore initially disabled before closed-game acknowledgement. Cancelling retained both profiles and source files. No target files, state changes or restore backup were created. |
| Deliberately edit one synthetic source INI and import again | Preview listed one content conflict and seven reuse rows. Install and Save Preset was disabled, blocking the entire bundle. Cancelling retained the modified file and two existing profiles; staging was empty and controls unlocked. |
| Confirm export warning, open owned folder picker, cancel | Cancel returned to idle, retained profiles and unlocked Export/Install. Export directory remained empty. Completing export through this picker is **not verified**. |
| Normal close | Isolated test window disappeared from the computer-use window inventory. |

Both persisted profiles had unique IDs:

- `Synthetic bundle demo`: `a628f35c197144b1b0aa1ed851fd310b`
- `Synthetic bundle demo (2)`: `6bad7b6bebcc4fc3a6e922223d7ad909`

Runtime SHA-256 before import, after install-only, after repeat/cancel and after conflict/cancel:
`AFEE1DC08C402832EBFF16B716DA8392E2C07FFF8CF3DD2E6A76B0A729E88CE8`.
The sole external entry remained `$\Mods\External-keep\mod.ini\unrelated = 99 ; keep untouched`.

The exercised test app deliberately retains the edited `Synthetic-Mod-000/mod.ini` as conflict evidence; it is not a clean redistributable package. Seven other payloads were read back with SHA-256 equal to their generated originals. This deliberate test edit must not be described as application corruption or a successful final eight-file hash match.

## Automation boundary / 自动化边界

The owned `PickerHost.exe` dialogs did not expose independent targetable windows. Direct clicks on their controls were rejected as belonging to a different process than the selected main window. For the ZIP picker, the observed editable filename control supported SetValue and the documented Open accelerator successfully returned the ZIP to the application.

The folder picker accepted its destination text, but bounded keyboard/observed accessibility attempts did not confirm the selected folder. It was safely cancelled. This is a computer-use targeting limitation, not evidence that the application's export service failed. No new input driver, UIA helper or unbounded desktop clicking was introduced. Native export completion followed by reimport remains an explicit pending item.

## Offline additions / 离线回归补充

`ReexportImportedPreset_ToAnotherRepositoryPreservesSavedStateAndReusesFiles` adds two cases: with and without persistent state. They install a bundle, serialize/read its saved snapshot, export from the imported repository, install into a second Unicode-named repository and repeat import with exact reuse. They verify saved values/manifests, unchanged Mod INI defaults, no whole d3dx file or absolute workspace path in the package, unchanged unrelated runtime bytes, empty deployment target and clean staging. These are production-service/file-contract tests, not proof of the native export picker or actual in-game behavior.

## Remaining / 待完成

- Native export completion and reimport of that newly exported ZIP; export of a changed saved profile.
- Unblocked installation-preview cancellation, large copy/hash cancellation, configuration disk-write failure, restore failure after commit and stale preview handling at the window layer.
- Complete language/theme/window-size/scaling and large-repository responsiveness checks.
- Actual game/XXMI restoration and release-candidate upgrade/rollback only after separate approval.

No feature implementation was changed merely to bypass the owned picker. This checkpoint strengthens evidence and regression coverage; it is not a public release.

## Automated final run / 最终自动验证

Command: `scripts/test-all.ps1 -ArtifactsDirectory artifacts/optimization-bundle-opt10-verified-20261007`, with NuGet packages and temporary files directed to G-drive development folders.

- 722 tests passed, none failed/skipped: Core 671, DataStore 24, UpdateAgent 27. The two new re-export cases passed.
- Coverage gates passed: Core line 96.7% / branch 91.8%; DataStore 88.6% / 89.1%; UpdateAgent 60.2% / 60.6%. Four coverage-gate self checks passed.
- WinUI x64 Release build: zero warnings/errors. Launcher/updater builds and minimal release ZIP contract passed.
- Clean local runtime ZIP: `artifacts/optimization-bundle-opt10-verified-20261007/ReleasePackage/Integrated_Mod_Manager-v4.0.0.zip`.
- SHA-256: `3C714B7E2C21B64CAD20B49536634EC1192A1EA54E96BAAD6F94870D6C550934`.
- Build transcript and TRX/coverage reports remain under the matching log and artifacts directory. `git diff --check` returned zero; existing LF/CRLF conversion notices were warnings, not failed tests.

The clean runtime package is separate from the exercised app and does not include its deliberately modified fixture or persisted profiles. Extract it into a new directory and retain the complete runtime folder. Internal version 4.0.0 does not mean this local checkpoint has replaced the public stable package. Native observations used the earlier image-fix runtime; this rebuild changes regression tests/documentation, not the application implementation.
