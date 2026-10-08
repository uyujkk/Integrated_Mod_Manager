# Combination bundles (4.1) / 组合预设压缩包

Included in 4.1.0 following isolated opt8–opt11 development checks. Historical checkpoint records below retain their original scope. Existing `ConfigurationProfiles` remain compatible.
4.1.0 纳入此前 opt8–opt11 隔离开发验证后的组合包功能。下方历史检查记录保留当时的验证范围；兼容现有组合配置格式。

## User workflow / 使用流程

1. In Repository → Combination Presets, select a saved preset and choose **Export Bundle**. Export uses its saved values, not live loader state. To export current settings, update the combination after the loader has flushed its values. Choose a folder outside the repository; a unique `Combination-*.imm-preset.zip` is generated without overwriting another ZIP.
2. In another repository, choose **Install Bundle** and select that ZIP. Preparation validates and stages payloads; no Mod is installed until the preview is accepted. Preview lists every Mod with new-install/reuse/conflict status in an internally scrolling, virtualized list. Game labels are hints only.
3. **Install and Save Preset** moves new Mod folders into the current category/Mod structure and saves a new local preset. Identical existing file inventories and SHA-256 values are reused. Different files, extra files, occupied paths or same deployment name in another enabled category block the whole import; no overwrite or automatic rename option.
4. The default checked option continues to the existing **Restore Preview**. After the user confirms the game/loader is closed and approves the plan, the existing copy/link and backup workflow restores the combination, then patches its selected numeric keys in the local `d3dx_user.ini`. Mod INI defaults and unrelated loader keys remain unchanged.
5. Uncheck the restore option to install and save only. Cancelling/failing the separate restore step retains the successfully imported repository files and preset for later retry. Import never launches a game/loader or executes bundled files.

仓库 → 组合预设：选保存好的方案，点“导出组合包”；迁移时在目标仓库点“安装组合包”。预览确认后先安装文件并保存新预设，默认再进入原有“恢复前预览”。确认关闭游戏/加载器后才恢复部署和参数；也可取消勾选，只安装和保存。整页不增加滚动，清单在模块/弹窗内部浏览。重名预设追加序号，不覆盖原预设。

Opt9 places Export/Install/active Cancel in a shared top toolbar instead of the narrow preset footer. Short viewports keep recovery beside the browser when there is sufficient width; tall narrow windows stack recovery with its own scroll region. See [native checkpoint](../verification/combination-bundle-opt9-20261007.md).
opt9 将导出、安装和进行中的取消按钮放到工作区顶部，避免挤占预设列表。短窗口宽度足够时并排显示恢复模块，窄长窗口则下置并独立滚动；实际验证范围见检查记录。

## Contents / 内容

```text
Combination-*.imm-preset.zip
  preset.json                  versioned manifest + selected saved values
  mods/000000/<relative file>  complete first selected Mod file tree
  mods/000001/<relative file>  complete second selected Mod file tree
```

`preset.json` has `format: imm-combination-bundle`, `version: 1`, `name`, `game`, `includesPersistentState` and `mods`. Each Mod lists its canonical `category/Mod` path, payload relative paths, byte lengths, SHA-256 hashes and persistent values (`relativeIniPath`, `variable`, `runtimeKey`, `value`). It does not serialize app repository IDs, absolute repository/launcher paths or the entire global loader state. Original Mod files, including INIs, are copied unchanged. A set-only preset has no persistent values; empty combinations are supported with an explicit removal warning before restoration.

配置只带所选组合、Mod 文件清单及保存的参数，不打包整份 `d3dx_user.ini`，不导出应用仓库 ID 和机器路径配置。Mod 内部自带文件可能仍包含私人内容，请自行检查；不能将“未导出应用配置”理解为已自动清除所有隐私。

All files in selected Mod folders are included, including nested files and locally added metadata. Empty top-level Mod folders are recreated, but nested empty folders, timestamps, ACLs, alternate data streams and file links are not preserved. Selected Mods with junctions/symlinks are refused, not followed. Export outside the repository to prevent recursive inclusion.

Bundles are unencrypted. Hashes provide consistency, not provenance, signatures, malware scanning or redistribution permission. Install only trusted sources; check authors' permission before sharing. Import does not execute payloads, but later use of untrusted Mods/loaders remains a separate risk. No game-version or account-safety guarantee is made.
压缩包不加密；SHA-256 只验证清单与内容一致，不证明来源、安全或作者授权。请只安装可信来源，分享前确认作者允许再分发。

## Safety and transaction boundaries / 保护与事务边界

- Reject absolute/traversal/ADS/reserved Windows paths, duplicate aliases, file/directory collisions, duplicate/unknown JSON properties, unsupported schema, extra/missing payloads, wrong lengths/hashes and non-regular ZIP entries. Source/destination ancestor and selected-file links are rejected.
- Saved state is validated against each staged Mod's declarations and namespace using the existing engine; duplicate runtime keys fail. No guessed cross-Mod mapping, renamed namespaces or fallback to defaults.
- Default bounds: 2,048 Mods, 100,000 files, 32 GiB expanded total, 8 GiB per file, 16 GiB ZIP and 8 MiB manifest. Suspicious compression ratios are rejected. These are hard limits, not a promise of enough free space/memory. Export uses streaming compression/hash; source inventory is bounded and rehashed before publication.
- Export owns a uniquely created `.partial`, deletes it on failure/cancellation, and publishes with a no-overwrite move. Unique output names avoid overwriting earlier exports.
- Import owns a staging child under `.imm-bundle-staging` beside the repository, on the same volume. The empty container may remain. Mod files are rehashed and repository decisions revalidated after preview, before folder moves.
- The window saves the new preset with the existing atomic configuration writer in strict failure-reporting mode. Without successful configuration save, disposal removes only newly installed, still-identical folders and empty categories it created. Reused Mods stay untouched. A folder externally edited after installation is retained with a cleanup warning.
- Configuration publication and the ownership commit have no await/cancellation gap. After commit, a refresh/restore failure is reported separately and does not delete installed Mods or remove the preset. The following deployment/state restore retains its existing separate backups and rollback.
- Cancel is cooperative; hashing/IO/native picker/dialog in progress is not instantly stopped. Closing the window requests cancellation before commit. No cross-process lock or full crash/power-loss journal exists; stop external editors first. A directory-link race after a check cannot be eliminated by these checks alone.
- Required temporary space can include the ZIP, staged copies and later deployment backups. No disk-space reservation. IO/permission/disk-full errors fail visibly; cleanup warnings require manual inspection.

导入不默默覆盖或跳过冲突；保存失败则清理本次新增且未被外部修改的文件。复用目录不删除，成功提交后不因后续刷新/恢复失败撤销导入。暂存目录位于仓库同卷旁边。取消不是强制结束 IO/系统弹窗；没有断电恢复日志或跨进程互斥，不宣称绝对防竞态。

## Verification / 验证

See [opt8 evidence](../verification/combination-bundle-opt8-20261007.md) and [manual checklist](./Regression-Checklist.md). Offline tests cover real ZIP export/readback/install, reuse, cancellation, rollback, links, invalid manifests and deployment + targeted restoration with text-only fixtures. They do not prove the whole WinUI tree, native pickers, DPI layout, real game rendering or live websites.

To generate fresh mock data after building Core Release, use **PowerShell 7 / .NET 8+**:

```powershell
pwsh -File scripts/new-combination-bundle-fixture.ps1 -ModCount 8 -OutputDirectory artifacts/bundle-demo/TestData
```

The generator refuses existing/linked/out-of-repository output, creates a ZIP through the production exporter, and reads it through the production importer without installing. `ImportRepository` starts empty; `ImportLoader/Mods` and `ImportLoader/d3dx_user.ini` are synthetic only. Select these paths explicitly in a separate local test app. Never connect this fixture to a real game/launcher.

For bounded cancellation stress data, optionally add `-PayloadMegabytesPerMod 256 -ModCount 3` using another new output folder. Random incompressible payloads are 0–512 MiB per Mod, with an aggregate generator cap of 1,024 MiB; these are not game assets. This creates sizable files and a ZIP, so allow enough temporary space. Native export/reimport (user-assisted folder confirmation), configuration-save rollback, stale-preview refusal and large validation cancellation are recorded in [opt11](../verification/combined-regression-opt11-20261007.md); folder-move/close cancellation remains separate.
