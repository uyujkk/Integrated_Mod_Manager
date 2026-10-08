# Combination bundle opt8 / 组合包本地验证

> Superseded for running the app: opt9 native testing found a constructor-time control-parenting crash in the opt8 test build. Use the opt9 package instead. The offline test results below remain historical evidence, not startup proof. See [opt9 native checks](./combination-bundle-opt9-20261007.md).
> 运行测试程序请改用 opt9：实际窗口检查发现 opt8 存在构造阶段重复设置控件父级导致的崩溃。以下离线结果保留，但不代表能正常启动。

Date: 2026-10-07. Local `optimization/repository-scan` checkpoint, based on 4.0.0. No GitHub push/release, version bump, automatic-update change or modification of real Mods/game/launcher paths. Existing uncommitted opt1–opt7 changes retained.
本地开发验证，不发布、不推送、不修改自动更新；未访问真实游戏部署。正式包保留。

## Implemented / 本次实现

- Core `CombinationBundleService` and an owned `CombinationBundleImport` session: versioned preset JSON + corresponding Mod file trees, stream/hash export, strict archive validation, same-volume staging, complete preview, no-overwrite install/reuse and rollback.
- Saved parameters only, not the whole `d3dx_user.ini`; original Mod INIs unchanged. Namespace/declaration matching reuses the existing engine. Duplicate deployment basenames in the bundle or another enabled repository category block import.
- WinUI Presets left-panel Export Bundle / Install Bundle / active-task Cancel actions. The existing fixed-page/internal-list layout remains. Bilingual controls/preview, saved-profile snapshots, progress on the UI synchronization context, close cancellation, new profile-name suffixes and strict atomic configuration save before ownership commit.
- After successful import/save, the checked option opens the existing restore approval workflow. Deployment + selected d3dx-key writes remain separate approved transactions. Import-only and cancel-after-import retain installed files and the saved preset.
- No embedded ZIP payload execution, game launch, auto-F10, injection, global state overwrite, automatic namespace remapping, original-Mod state rewrite or arbitrary archive-as-preset fallback.

Details: [bundle contracts and user workflow](../development/Combination-Bundles.md).

## Final automated run / 最终自动验证

Directory: `artifacts/optimization-combination-bundle-verified-opt8-r2-20261007`.

Log: `artifacts/optimization-combination-bundle-opt8-r2-build.log`.

| Suite | Passed | Failed / skipped | Line / branch coverage |
| --- | ---: | ---: | --- |
| Core | 638 | 0 / 0 | 96.7% / 91.8% |
| DataStore | 24 | 0 / 0 | 88.6% / 89.1% |
| UpdateAgent | 27 | 0 / 0 | 60.2% / 60.6% |
| Total | 689 | 0 / 0 | All existing component gates passed |

**82 new cases** exercise production bundle APIs: actual ZIP export/readback/install, saved-state migration, identical reuse, whole-install conflicts, original-file preservation, post-preview repo/staging changes, checksum/length/inventory errors, traversal/ADS/Windows naming, malformed/unsupported/duplicate JSON, schema/version, duplicate Mod/runtime keys, invalid declarations/namespaces, limits, pre/mid-operation cancellation, category collisions, owned-folder rollback, refusal to delete externally edited folders, invalid commit lifecycle, empty Mods/sets, Unicode nested files, immutable public manifest copies, compressible buffers and real Windows junction refusal. Roundtrip fixture deploys mock Mods using the existing policy, applies selected values via the existing persistent engine, preserves unrelated keys/default INIs and exactly rolls back the parameter patch. Mock profile persistence is not a test of the whole WinUI save dialog.

Coverage-gate fixtures, WinUI x64 Release build (**0 warnings, 0 errors**), executable version checks, managed manifest and minimal runtime ZIP contract passed. NuGet audit was disabled for local offline build; a vulnerability audit was not verified. The r1 run also passed 685 tests; final r2 adds compressible payload and junction tests plus early category/basename conflict checks, and uses fast streaming compression. r2 is the delivered build.

## Artifacts / 文件

Clean runtime ZIP:

`artifacts/optimization-combination-bundle-verified-opt8-r2-20261007/ReleasePackage/Integrated_Mod_Manager-v4.0.0.zip`

SHA-256: `0544E5FBCE638AE23D4975A5F4E40DABE56EADCAD0B157B6E9AFC66A625F467A`.

Independent local test ZIP:

`artifacts/local-test-combination-bundle-opt8-20261007/Integrated_Mod_Manager-v4.0.0-combination-bundle-opt8.zip`

SHA-256: `2517155D308EE9418F429930643E2242C04B60896A38A6B3B22AA7373B8A453A`.

Fresh extracted app:

`artifacts/local-test-combination-bundle-opt8-20261007/App/IntegratedModManager.exe`

The local test source was assembled from the clean verified r2 ZIP, not a launched application folder. Four executable file versions are **4.0.0.0**: main launcher, compatibility launcher, update agent and WinUI runtime. Fresh App contains no config.ini, beta-shell.json, performance.enabled or startup.log. The ZIP is a separately named test package, not a new v4.0.0 public update.

Demo:

`App/TestData/Demo-Combination.imm-preset.zip`: **8 text-only mock Mods, 16 values, 9 ZIP entries**; format `imm-combination-bundle`, version 1. SHA-256 `10B6C11C54C153D2454EAFEA38BC4170F0F557C3D122D5525F0E656BA164A9FA`.

`scripts/new-combination-bundle-fixture.ps1` created the ZIP using the production export service and read it through the production importer with no installation. Staging cleaned without warnings; ImportRepository remains empty. Mock ImportLoader state begins with an unrelated value of 99; no game assets/launcher/private configuration. LOCAL_TEST.md explains explicit mock path selection and pending manual checks.

Preserved stable ZIP was rehashed unchanged:

`artifacts/release-v4.0.0-stable-20261005/package/Integrated_Mod_Manager-v4.0.0.zip`

SHA-256: `6099EC3CB19BAA655F0E2509246E313C1714A702FB33A2BEB952302901935E31`.

## Not verified / 尚未验证

- No native app window was launched for opt8. Button wiring, system file/folder pickers, complete visible list virtualization, cancel/close while dialogs are open and actual DPI/wide/narrow layout remain pending. Compile success does not prove these behaviors.
- No real game/XXMI/EFMI, live website, real Mod import/deployment or app self-update tested. Prior user-confirmed real-game persistence does not prove this new bundle workflow.
- No full crash/power-loss recovery journal, cross-process lock, active-game detection, ZIP author authentication or malware scanning. Staging and new-file rollback are cooperative in-process operations, not an absolute race guarantee.
- Import has strict same-content reuse/no-overwrite behavior. Mod folder/name/version changes are not automatically guessed or migrated. Game metadata is a label, not validation. Nested empty folders/ACLs/alternate data streams are not preserved.
- ZIPs are unencrypted and contain every file in selected Mod folders. Review private content and redistribution rights. Hashes and file checks are not account-risk or game compatibility guarantees.

Next: [native regression checklist](../development/Regression-Checklist.md), broader window/performance matrix, isolated workflow validation, then release-candidate checks only after approval.
