# Repository scan opt1 / 仓库扫描优化第一阶段

Date: 2026-10-05. Branch: `optimization/repository-scan`.
Base revision: `a3efa8dd84e3ef1fe6e8c695457dd2647a27af54` plus the local changes described below. No remote push, tag or release was performed.
基于 4.0 正式主线加本地修改，没有推送、打标签或发布。

## Result / 结果

- Core: 320/320 passed; line 95.18%, branch 90.13%.
- DataStore: 9/9 passed; Data assembly line 87.03%, branch 82.69%.
- UpdateAgent: 27/27 passed; line 60.24%, branch 60.60%.
- Total: 356/356, no skipped/failed cases; 31 new cases relative to stable 4.0.0.
- Four coverage-gate script checks passed; original per-component thresholds unchanged.
- WinUI x64 Release build passed with 0 warnings and 0 errors.
- Launcher/update-agent compilation, version consistency, managed manifest, package contract and SHA-256 sidecar passed.
- Packaged Core, Data and WinUI DLL hashes match the freshly built x64 outputs.

自动验证共 356 项通过，新增 31 项；另有四项覆盖率脚本检查通过。构建、版本、运行文件清单、包契约和哈希核对通过。

Evidence: `artifacts/optimization-verified-r2-20261005/TestResults` and `ReleasePackage`.
The baseline rerun used freshly compiled original Core tests (291) and the previous stable compiled DataStore/Updater suites (7/27). The optimized suites above were all rebuilt. A first restore attempt used an incomplete account-local NuGet cache; successful verification used existing `G:/Integrated_Mod_Manager_Development/tools/nuget-packages`. Online vulnerability auditing was disabled for this offline verification and was not claimed as passed. No dependency versions changed.
基线复测中 Core 为原代码重新编译，数据和更新使用上一正式构建程序集；优化版三组均重新构建。初次默认缓存还原失败，之后使用现有 G 盘依赖缓存；离线验证未执行在线漏洞审计，也没有改变依赖版本。

## Synthetic scan samples / 模拟扫描样本

Measured inside the final Core test run, with ten categories and two immediate files per Mod:
最终 Core 测试中，10 个分类，每个 Mod 两个直接文件：

| Mods | Cold / 首扫 | Warm / 再扫 | Warm file-list/size reads / 再扫文件列表和大小读取 |
| ---: | ---: | ---: | ---: |
| 100 | 16 ms | 3 ms | 0 / 0 |
| 500 | 51 ms | 12 ms | 0 / 0 |
| 1,000 | 112 ms | 25 ms | 0 / 0 |

These are local synthetic service timings, not a comparison against the previous app, a recursive real-Mod workload, a UI responsiveness measurement or a speed guarantee. Warm caching existed before this extraction; this run verifies that it remains intact.
这是本地模拟服务耗时，不是旧版对比、真实大型 Mod 的递归扫描或界面流畅度测试。热缓存原来已有，本次确认提取后保留，不能据此声称新版比旧版快若干倍。

## Local package / 本地测试包

- ZIP: `artifacts/local-test-repository-scan-20261005/Integrated_Mod_Manager-v4.0.0-repository-opt1.zip`
- SHA-256: `1DC47C69F14EC7184FF4CA5B3E9A61D88178746796B28B77780DF5E52A967FF4`
- Runnable entry: `artifacts/local-test-repository-scan-20261005/App/IntegratedModManager.exe`
- Existing file/display version remains 4.0.0. This is an isolated test build, not a replacement public release.
- Original stable ZIP hash remains `6099EC3CB19BAA655F0E2509246E313C1714A702FB33A2BEB952302901935E31` and matches its existing checksum file.

测试包与正式包分开，未修改正式发行 ZIP。测试目录未复制用户配置或 Mod，不要覆盖正式安装目录。

## Manual status and next slice / 手动状态与下一步

Generated 100/500/1,000-Mod fixture directories; existing-directory and outside-repository rejection checks passed. These are text-only synthetic data, never game Mods.
已生成三档模拟目录，验证重复目录拒绝覆盖及越界路径拒绝。模拟目录不是可加载的游戏 Mod。

Visible native-window interaction, rapid switching, tray behavior, live downloads and real-game/preset restoration have **not** been newly verified in this slice. Follow the [regression checklist](../development/Regression-Checklist.md); do not mark these as passed based on unit tests or package hashes.
本轮尚未重新验证可见原生窗口交互、快速切换、托盘、实际下载或真实游戏恢复，见[回归清单](../development/Regression-Checklist.md)。

Next priority: preset restore-plan preview and missing/ambiguous item checks, preserving targeted d3dx writes and existing deployment safety. Do not combine that work with a wholesale page rewrite.
下一阶段优先做组合预设恢复前预览与缺失/歧义检查，继续保持定向回写和部署安全，不同时重写全部页面。
