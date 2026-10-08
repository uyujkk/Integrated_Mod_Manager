# Online metadata opt6 verification / 在线元数据 opt6 验证

Date: 2026-10-05. Local checkpoint on `optimization/repository-scan`; no push, release, version bump, preset-schema change or automatic-update change. This completes the Mod detail/file-metadata checkpoint, not extraction of the entire online subsystem.

日期：2026-10-05。本地完成 Mod 详情与文件元数据检查点，不推送、不发布、不提升版本、不改预设格式或自动更新；不声称全部在线服务已经拆分完毕。

## Changes / 改动

- Core owns bounded HTTP reads, parsing, timeout/cancellation and detail-cache freshness. WinUI keeps character/language inference, display, shortcut extraction, access warnings and existing file-selection policy.
- Default HTTP limit: 4 MiB; request timeout: 30 seconds. Details expire after three days in memory and persistent caches. File metadata always requests fresh data. Memory cache has 128-entry / 16 MiB estimated text-and-URL limits, not a global application-memory limit.
- Data adapter uses the existing SQLite table with new `online-details-raw-v1` / `metadata-mod-<id>.json` names. Legacy cache records/files are fallback reads only. Failed HTTP results are not cached; cache I/O failure does not discard valid network results.
- File writes clean only a successfully created, owned partial file. The final r2 build supersedes the preliminary r1 artifacts after this ownership guard was tightened.
- Added 54 Core cases and 15 DataStore cases. Tests use fake HTTP and isolated real SQLite/files; no website/game requests.

职责与限制详见 [Online Metadata Service](../development/Online-Metadata-Service.md)。新缓存保留旧数据；不修改游戏、启动器、Mod 配置或已发布的软件。

## Final automated verification / 最终自动验证

Evidence directory: `artifacts/optimization-metadata-verified-opt6-r2-20261005`.

Log: `artifacts/optimization-metadata-opt6-r2-build.log`.

| Component | Passed | Failed / skipped | Line / branch coverage |
| --- | ---: | ---: | --- |
| Core | 527 | 0 / 0 | 96.4% / 91.5% |
| DataStore | 24 | 0 / 0 | 88.6% / 89.1% |
| UpdateAgent | 27 | 0 / 0 | 60.2% / 60.6% |
| Total | 578 | 0 / 0 | All component gates passed |

Coverage-gate fixture checks passed. WinUI x64 Release build: **0 warnings, 0 errors**. Release manifest, sidecar and package-contract checks passed. NuGet audit was disabled for the offline verification run; this is not evidence of a successful vulnerability audit.

Early targeted runs exposed a shared-output parallel-build collision and pooled SQLite handles in test cleanup; verification was rerun sequentially after fixture cleanup was corrected. A screenshot dot-name regression test exposed a parser guard error, which was corrected before the final full run.

Service recreation with a real SQLite cache proves reuse at the service/adapter boundary. It does not prove real application restart behavior, visible window interactions or live API compatibility.

## Local package / 本地测试包

Clean runtime ZIP:

`artifacts/optimization-metadata-verified-opt6-r2-20261005/ReleasePackage/Integrated_Mod_Manager-v4.0.0.zip`

SHA-256: `E0D945B4D8D7BC935FC6B105CD33FAA2FB122FF874C9FF424B0366B4E94B4423`.

Independent test ZIP:

`artifacts/local-test-online-metadata-opt6-r2-20261005/Integrated_Mod_Manager-v4.0.0-metadata-opt6.zip`

SHA-256: `8D90F150FDA3E76FBF35D0F4E49197EB8F62145409AC2739BBF7365C8D1AFEFE`.

Freshly extracted application:

`artifacts/local-test-online-metadata-opt6-r2-20261005/App/IntegratedModManager.exe`

The package was produced from a clean final runtime source, not a previously used App directory. `IntegratedModManager.exe`, `ModFolderCopier.exe`, `LocalUpdateAgent.exe` and `WinUI3/ModFolderCopier.WinUI.exe` all report **4.0.0.0**. It includes 1,000 mock `sample.ini` files and `LOCAL_TEST.md`; no `config.ini`, `beta-shell.json` or `performance.enabled` was present in the fresh App root. Mock paths must be explicitly selected as described in the guide; no real Mod configuration was copied into the package.

The preserved stable ZIP at `artifacts/release-v4.0.0-stable-20261005/package/Integrated_Mod_Manager-v4.0.0.zip` still hashes to `6099EC3CB19BAA655F0E2509246E313C1714A702FB33A2BEB952302901935E31`.

## Not verified / 尚未验证

- No application window was launched for this checkpoint; native layout, visible loading/cancellation and installed-state workflows remain manual checks.
- Live GameBanana response shapes, redirects, rate limits and network behavior were not exercised.
- Feed/category catalogs, avatar/image binary fetching and translation are not all moved into this service.
- Same-ID request coalescing, page revision guards and duplicate-button handling remain the next checkpoint.
- SQLite operations already in progress are synchronous/cooperative. Legacy database blobs retain existing deserialization behavior; the streaming size limit is not claimed to apply before their deserialization.

下一步：在线页面状态与编排，包括真实窗口适配方法的离线样例、防重复点击与过期回调隔离；之后再进入可迁移组合预设。
