# Combination preview opt2 / 组合恢复预览第二阶段

Date: 2026-10-05. Branch: `optimization/repository-scan`.
Base revision: `a3efa8dd84e3ef1fe6e8c695457dd2647a27af54` plus local stage 1 and stage 2 changes. Changes remain in the local working tree; no commit, remote push, tag, GitHub release or automatic-update change was performed in this slice.
基于 4.0 正式主线和第一阶段修改；第二阶段保留本地工作区修改，没有提交、推送、打标签、发布或更改自动更新。

## Implemented / 已实现

- Read-only preparation aggregates missing/disabled Mods, unsafe roots and paths, linked sources, duplicate paths/target names, inconsistent state snapshots and parameter declaration failures.
- 核心层只读准备汇总缺失/禁用 Mod、不安全路径和根目录、链接源、重复路径和部署名、快照不一致及参数声明问题。
- Missing items block the entire restore; no automatic renaming, guessing, partial recovery or fallback to INI defaults.
- 有问题时阻止整体恢复，不自动改名、猜测匹配、部分恢复或使用 INI 默认值代替保存值。
- Bounded WinUI dialog includes complete Mod, parameter and check tabs. The action label is “Preview and Restore”. Parameter rows show current → saved values and include added/unchanged values. ListViews are used for panel-local scrolling and virtualization.
- 有界 WinUI 弹窗提供完整 Mod、参数和检查结果三标签，入口改为“预览并恢复组合”，参数显示当前值到保存值；采用 ListView 做内部滚动和虚拟化。
- Validate already deployed copies before transaction. After all confirmation dialogs, regenerate and compare the plan, including external inventory, parameter declarations and exact loader bytes. The existing apply path verifies bytes again.
- 已部署副本在事务前检查；所有确认后再生成并比较计划、外部目录、参数声明及精确加载器字节，原应用流程继续检查写前字节。
- Keep targeted `d3dx_user.ini` patching, original Mod INIs read-only, deployment/state backups, rollback and explicit closed-game/loader acknowledgement. Stored preset schema is unchanged.
- 保留定向参数回写、原 Mod INI 只读、部署与参数备份、回滚和退出加载器/游戏确认，保存格式不变。

## Automated verification / 自动验证

Final run: `artifacts/optimization-presets-verified-r2-20261005`.
The earlier run without the final three cases remains at `artifacts/optimization-presets-verified-20261005`; use r2 for this package.
最终包使用 r2 验证产物；初次验证目录保留，但不用于本包。

| Suite / 测试组 | Passed / 通过 | Line / 行覆盖率 | Branch / 分支覆盖率 |
| --- | ---: | ---: | ---: |
| Core | 384 / 384 | 95.64% | 91.65% |
| DataStore | 9 / 9 | 87.03% | 82.69% |
| UpdateAgent | 27 / 27 | 60.24% | 60.60% |
| Total / 合计 | **420 / 420** | — | — |

64 new Core preview cases relative to opt1; zero failed/skipped tests. Four independent coverage-gate script checks passed; original component thresholds are unchanged. WinUI x64 Release build: 0 warnings, 0 errors. Launcher, updater, version consistency and minimal release ZIP contract passed.
相对 opt1 新增 64 项预览测试，无失败或跳过；另有四项覆盖率脚本检查通过、门槛不变；x64 构建零警告零错误，入口、更新器、版本与最小包契约通过。

Tests cover full 100-Mod/value lists, multiple missing entries, case/slash normalization, duplicate namespace/runtime keys, source/category/root junction rejection, valid deployment links, foreign links, existing copied declarations, stale approval, read-only preparation, UTF-8/BOM/UTF-16 preservation, selected-key-only writes and exact parameter backup/rollback. Incomplete persistent values now fail with a validation error rather than a null-reference error.
覆盖 100 Mod/参数完整清单、多缺失项、大小写和分隔符、重复命名空间/运行键、源/分类/根目录联接拒绝、有效部署联接、外部链接、副本声明、过期计划、只读准备、编码、定向写入与精确参数备份回滚。不完整参数值返回验证失败，不再空引用报错。

Build used existing G-drive NuGet cache. Online vulnerability auditing was disabled for the offline run, not claimed as passed. No package dependency versions were changed.
使用现有 G 盘依赖缓存完成离线验证，没有运行或声称通过在线漏洞审计，没有更换依赖包版本。

## Independent local package / 独立本地测试包

- ZIP: `artifacts/local-test-combination-preview-20261005/Integrated_Mod_Manager-v4.0.0-presets-opt2.zip`
- SHA-256: `EC38D5328E23EF66CF55E5BA10AD038F62DD36486F4CF69EC608A02841FC1EA6`
- Size: 35,315,436 bytes.
- Runnable entry: `artifacts/local-test-combination-preview-20261005/App/IntegratedModManager.exe`
- Instructions: `App/LOCAL_TEST.md`.
- Source staging: `artifacts/local-test-combination-preview-source-20261005`.
- All four executable file versions: 4.0.0.0; display version remains 4.0.0. Identify the local build by its opt2 package/folder name. Do not install over stable.
- Core, Data and WinUI DLL hashes match the freshly built x64 files; manifest entries and exact ZIP checksum sidecar verified.
- Includes only generated offline `TestData`: 32 synthetic Mods / 64 values, one unknown target folder and one unrelated value of 99. No game assets, launcher or real user state included. This test fixture is intentionally not part of any public release.
- Fixture generation rejects an existing destination and outside-repository paths; rejection left the existing fixture hash unchanged.
- Stable 4.0.0 ZIP remains unchanged: `6099EC3CB19BAA655F0E2509246E313C1714A702FB33A2BEB952302901935E31`.

包独立存放，正式发行 ZIP 未变。32 个模拟 Mod / 64 参数仅用于离线测试，不是游戏 Mod；没有真实用户配置或资源。测试包不要覆盖正式安装目录，也不要接入真实启动器。

## Remaining verification and limits / 待验证和边界

Visible native-window behavior, dialog row binding/layout, actual ListView virtualization, Chinese/English at real scaling, live downloads, tray behavior, automatic updates and real-game restoration have **not** been newly tested here. Follow the [manual checklist](../development/Regression-Checklist.md); unit tests/build/package hashes do not replace those checks.
可见窗口、弹窗绑定排版、实际虚拟化、双语言和缩放、实际下载、托盘、自动更新与游戏内恢复，本阶段均未重新实测。按[手动清单](../development/Regression-Checklist.md)检查，不能用自动测试替代。

The acknowledgement is not an automatic game-closed detector. The preview/freshness checks are not an OS process/filesystem lock or a Mod content-integrity scan. Already copied deployments still follow existing folder-name identity rules, not a content hash. Never claim zero race risk or universal Mod compatibility.
退出确认不等于自动检测游戏关闭。预览和新鲜度检查不是系统进程/文件锁，也不检查整个 Mod 内容完整性。复制部署仍沿用目录名身份规则，不是内容哈希，不宣称消除了所有竞争或兼容所有 Mod。

Next gate: inspect this synthetic package in visible native windows, then profile the large-repository fixtures at the user's actual scaling. Keep those changes local until explicit release approval.
下一关先在实际原生窗口检查模拟包，再测大仓库和实际缩放；明确要求发布前继续保留本地开发状态。
