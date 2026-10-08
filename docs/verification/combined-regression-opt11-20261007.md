# Combined regression checkpoint (opt11) / 四项合并验证

Local development checkpoint dated 2026-10-07 (some artifacts use 2026-10-08 UTC). This is **not** a public release, automatic-update change, or real-game compatibility certification.
本轮合并组合包、失败回滚、窗口/性能及在线流程四项检查。仅修改 G 盘开发与隔离测试目录；未发布、未推送、未连接真实游戏/启动器。

## Build identity / 构建标识

- Branch: `optimization/repository-scan`; base HEAD: `a3efa8dd84e3ef1fe6e8c695457dd2647a27af54`, with opt1–11 working-tree changes. This is not a committed revision identifier for the complete checkpoint.
- File version remains `4.0.0`. Clean runtime: `artifacts/combined-opt11-ui-verified-20261007/ReleaseSource`.
- Local ZIP: `artifacts/combined-opt11-ui-verified-20261007/ReleasePackage/Integrated_Mod_Manager-v4.0.0.zip`.
- SHA-256: `03557A8748363C3E259B8FEA7A1E83BA274E70F52427A70FD3AD46708E8A2FDA`.
- Exercised copy: `artifacts/combined-opt11-20261007/App`. Its configurations, cached website data and downloaded Mod files are **not** included in the clean runtime/package.

## Repairs made from observations / 实测发现并修复

1. Bundle preview, manual-file picker and download-destination dialogs now use a shared viewport budget instead of fixed minimum widths/heights. Only content lists scroll inside dialogs; confirmation actions remain visible. Shared message/confirmation dialogs inherit the application's actual theme.
2. Local previews and repository covers now use a bounded file stream, as online cached images do. The old `StorageFile` route reproduced a native `0x800700A1` error for mixed/long local paths. A previously failing downloaded 2560 × 1440 preview visibly loads in both repository cover and detail views after the repair; no further image-load error was appended during those observations.
3. The updates pane refreshes after committed online installation, so tracking counts and entries do not require a page round trip or restart. Independent local installations of the same website entry remain independently tracked.
4. The repository search accessibility name refreshes with the application language instead of retaining its construction-time Chinese name.
5. Short online windows retain a compact vertical character rail, reserving height for results. Narrow but taller windows can retain the horizontal rail. At the observed 975 × 621 logical window, the first Mod's Details and Download actions became usable instead of almost the entire results area being consumed by the character rail.

## 1. Native export and reimport / 窗口导出回读

The user confirmed the owned system folder picker. The application then exported `Exports/Combination-20261008-021046-45fe927d.imm-preset.zip` (936 bytes): `preset.json` plus two unchanged synthetic Mod INIs.

- The selected preset contains two Mods and four saved values: Mod 000 outfit/hair `2/1`, Mod 004 `1/1`. Source INI defaults are `0/0`; the current loader fixture only has an unrelated value `99`. Export therefore uses the **saved snapshot**, not current loader values or Mod defaults.
- Native reimport preview showed two exact reuses. Cancel left profiles, deployment and runtime state unchanged; staging cleared and commands unlocked.
- A subsequent install with the restore option unchecked saved `模拟往返预设 (2)` with a unique ID and all four saved values. No files were overwritten; loader target stayed empty.
- Source INI SHA-256: `7413AE30600C747FA9CA6203F14B33CF2E25AA08F81D8ADEE77AE883C11ED737`.
- Unrelated loader fixture SHA-256 remained `AFEE1DC08C402832EBFF16B716DA8392E2C07FFF8CF3DD2E6A76B0A729E88CE8`.

The system-picker confirmation was **user-assisted**, not fully automated. A separate native sequence of editing runtime values, pressing Update Preset and then exporting again remains to be checked; offline cross-repository re-export tests already cover saved-state/default separation.

## 2. Failure, cancellation and stale decisions / 失败取消与过期计划

| Scenario / 场景 | Observed result / 结果 |
| --- | --- |
| Configuration save failure / 配置保存失败 | Made only the isolated `beta-shell.json` read-only after an eight-Mod preview. Installation reported access denied. After acknowledging the error, all eight newly installed files were rolled back, no new profile remained, two existing profiles survived, and staging was empty. File read-only flag was restored; no ACL/security setting was changed. |
| Large bundle validation cancel / 大包验证中取消 | Generated three 256 MiB random synthetic payloads; ZIP was 849,342,225 bytes. One attempt completed preparation before cancellation and is counted only as preview cancellation. On the next attempt, Cancel was clicked while validation showed 15%; the UI reported cancellation, no profile/source installation was added, staging cleared and actions unlocked. |
| Repository changes after preview / 预览后仓库变化 | Added a comment to one reused synthetic INI after preview. Install refused with “Repository changed after preview. Inspect the bundle again.” Profiles, the other Mod and loader state stayed unchanged. Restored the exact original fixture bytes afterward. |

The read-only failure's rollback is observed **after the error dialog is acknowledged**; files remain during that modal until the operation's finally/disposal runs. The large test proves cancellation during validation/hashing/extraction, not native cancellation during destination folder moves or window close. Core tests separately cover owned-folder rollback after a move and restore failures; do not count those as native UI observations.

## 3. Windows and performance / 窗口与性能

- Observed Chinese/light wide window and English/dark short window. Native bundle, shared error, file-selection and destination dialogs were inspected; the short file-selection list scrolls internally while its two actions remain visible.
- Wide capture was 1908 × 1019 logical pixels; short capture was 975 × 621, from the isolated app's saved 1480 × 940 window placement. This is **not** a completed 100/150/200% DPI matrix; no OS scaling setting was changed.
- A 10-category/1,000-Mod synthetic repository showed the correct total. In category 00 (100 Mods), searching `Mod-0090` selected the expected path; Files → Covers retained that selection without deployment. This observation is not a 1,000-item single-list rendering/FPS measurement.
- Five cold/warm service + SQLite pairs were rerun at 100/500/1,000 Mods. Medians below are local synthetic samples, not native rendering or game performance promises.

| Mods | Cold median (ms) | Warm median (ms) | Warm hits | Collection notifications, old → batch |
| --- | ---: | ---: | ---: | ---: |
| 100 | 14.02 | 3.96 | 100 | 101 → 1 |
| 500 | 64.07 | 16.46 | 500 | 501 → 1 |
| 1,000 | 110.44 | 34.92 | 1,000 | 1,001 → 1 |

Evidence: `artifacts/combined-opt11-performance-20261007/repository-benchmark.json`. Initial restore failed due to blocked NuGet access; rerun with the existing G-drive package cache and audit disabled completed. Do not treat the failed invocation as a benchmark sample.

## 4. Live online flow in isolation / 隔离实际在线流程

The independently configured repository has no deployment target or launcher. Anonymous native browsing loaded live GameBanana data (601 Mods / 16 pages at the observation time), character avatars, list covers and detail images. No account login, upload or executable launch was performed.

Test entry: **SnowShine Food Courier**, GameBanana Mod ID `692549`. These website values are a dated observation, not pinned future catalog data.

- The manual picker listed six archives and selected the current newest `snowshine_food_courier_gui_13.zip` by default (version 1.3, 130.84 MiB). Archived versions remained selectable.
- Destination cancellation returned safely. Creating the isolated Chinese category `昼雪` then caused the next location dialog to recognize Snowshine and recommend that category.
- Latest archive downloaded as 137,195,280 bytes, remained in the character folder and extracted to a separate Mod folder. Automatically saved `preview.jpg` was 2560 × 1440, 705,332 bytes; native repository cover/details displayed it after the local stream repair.
- Manual selection of archived `snowshine_food_courier_switch_1_3.zip` downloaded 135,904,361 bytes to the same character category and extracted to `SnowShine Food Courier-2`, without overwriting the original.
- A repeated latest download used a distinct archive name and `SnowShine Food Courier-3`; tracking count refreshed to three without restart. All three local records remained valid.
- A later latest download was canceled while the visible task was still downloading (last pre-action snapshot 27.66 MiB; action refresh reported about 81.18 MiB of 130.84 MiB, then “Task canceled”). No partial archive or fourth extraction remained, and the three existing tracking records stayed intact. Original latest ZIP SHA-256 stayed `6C3540A7A803C25509DACD8D35D4B37B19098E6E2A93CE5CE7256DD151894A10`.
- Two earlier attempted cancellation checks completed before the cancel action became available; they count as completed downloads, **not** cancellation successes. The post-cancel retry completed as `snowshine_food_courier_gui_13-3.zip` (137,195,280 bytes) and `SnowShine Food Courier-4`. Native success text and four saved tracking records were read back; existing archives/folders remained intact.

This is download/extraction/metadata testing only. Installation tracking is not proof of game compatibility, redistribution permission, absence of malware, or account safety. Downloaded Mod files are kept only in the isolated QA directory and excluded from the deliverable.

## Automated gates / 自动门槛

`scripts/test-all.ps1 -ArtifactsDirectory artifacts/combined-opt11-ui-verified-20261007` completed successfully:

- **747 tests**: Core 696, Data 24, Updater 27; zero failures/skips.
- Four coverage-gate self-checks passed. Core line/branch 96.7%/91.9%; Data 88.6%/89.1%; Updater 60.2%/60.6%. Existing thresholds were not lowered.
- WinUI x64 Release build: zero warnings/errors.
- Minimal release ZIP contract and SHA-256 verification passed.
- New offline cases cover dialog budgets, bounded Unicode/long-path image reads and short/tall character-rail boundaries. Policy tests do not substitute for real DPI, image-decoder or native window evidence.

## Remaining release gates / 仍待补齐

1. Full real DPI/theme/language/window-size matrix and measured native rendering responsiveness, including Settings and Presets at minimum height.
2. Native Update Preset → export, import folder-move/close cancellation, and post-import committed restore failure. Current native export, save-failure rollback, stale-preview and validation-cancel checks are bounded subsets.
3. Online concurrent-task/repository-switch/metadata-cancel and genuine network-failure retry matrix. No intentional public-service disruption or OS network/security change was used.
4. Approved real-game A/B/A combination restoration and upgrade/release-candidate test. No real loader/game validation or publication was requested for this checkpoint.

The four work areas have been consolidated into one local checkpoint, but pending rows remain pending; successful unit/build checks alone do not authorize a release or prove these environments.
