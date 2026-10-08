# Repository performance opt3 / 仓库性能第三阶段验证

Date: 2026-10-05. Local branch `optimization/repository-scan`, base `a3efa8dd84e3ef1fe6e8c695457dd2647a27af54` plus uncommitted stage 1–3 work. No push, release, auto-update change or stable package replacement. File versions remain 4.0.0.0.
日期：2026-10-05。本地第三阶段检查点，包含前两阶段未提交的改动，不发布、不改自动更新、不覆盖正式包。

## Automated evidence / 自动验证

- Final run: `artifacts/optimization-performance-verified-r2-20261005`; log: `artifacts/optimization-performance-r2-build.log`.
- Core **404/404**, Data **9/9**, Updater **27/27**: **440 passed**, zero failures. Stage 3 adds 20 cases for batch snapshots and optional timing scopes.
- Component coverage gates passed: Core line 95.7% / branch 91.7%; Data 87.0% / 82.7%; Updater 60.2% / 60.6%. These are component rates, not whole-app or WinUI coverage.
- WinUI x64 Release build: zero warnings/errors. NuGet vulnerability audit disabled for offline validation; dependency versions unchanged. This is not a vulnerability-audit pass.
- Minimal package contract passed. Final clean runtime package SHA-256: `D6E9829BF7106143818C6E60BF3F30A26D33248690233F1D08423DD9075F2E71`.
- Test-app WinUI/Core/Data DLL hashes match this build. Four entrypoint EXEs remain 4.0.0.0. Git diff whitespace check passed with the repository's CRLF convention.
- Benchmark script rejected existing output and an out-of-repository path. The existing result JSON hash stayed unchanged.

## Service benchmark / 服务层基准

Results: `artifacts/repository-performance-opt3-20261005/repository-benchmark.json`. Five cold/warm pairs per size, each using a fresh SQLite index. Median milliseconds below. “Cold” means empty application index; OS disk caches were not cleared. Projection here is a lightweight model/deployment-existence probe, not the full WinUI projection or rendered frames.
每档五组，冷指索引为空；未清系统缓存。轻量投影不等于窗口完整模型构造，更不等于渲染速度。

| Mods | Cold scan + SQLite / 冷扫描 | Warm / 热扫描 | Lightweight projection / 轻量投影 | Legacy notifications / 原通知 | Batch / 批量通知 |
| --- | ---: | ---: | ---: | ---: | ---: |
| 100 | 13.00 ms | 3.68 ms | 0.94 ms | 101 | 1 |
| 500 | 69.25 ms | 17.29 ms | 4.41 ms | 501 | 1 |
| 1,000 | 116.14 ms | 35.59 ms | 8.45 ms | 1,001 | 1 |

Equal snapshots produce zero batch notifications. One Reset can still recreate realized controls or reset scroll state; event reduction is proven, universal UI speedup is not. No old-build versus new-build native frame benchmark was performed.
相同快照零通知。一次 Reset 仍可能重建可见控件或重置滚动；证明的是通知数量减少，不是所有场景提速。未做旧版/新版原生帧耗时对照。

## Native window checks / 原生窗口检查

Used the computer-use workflow to inspect visible native windows and accessibility controls, one UI action at a time. No real game/launcher or real Mod directory selected. Screenshots were observed through the tool, not exported as artifact files.
使用 computer-use 技能逐步检查可见窗口和控件树，仅操作独立模拟应用；截图用于观察，未另存为文件证据。

Final app: `artifacts/local-test-repository-performance-r2-20261005/App`. A single synthetic category contains 1,000 Mods without preview images. Initial ZIP omitted an empty target directory; a fixture marker fixed the test source and a new r2 package was generated. The first package is superseded, not a tested deliverable.
最终 r2 模拟包使用单分类 1,000 个 Mod，无预览图片。初版模拟包的空目标目录未被 ZIP 保留，补标记后使用新的 r2 包，不以初版作为可用测试交付物。

| Check / 操作 | Observed / 结果 |
| --- | --- |
| First startup / 首次启动 | Offline repository auto-loaded; 1 category, exactly 1,000 Mods; target ready / 自动加载模拟仓库、数量正确 |
| Search `0999` / 搜索末项 | Exactly 1/1,000 result; selecting it shows Synthetic-Mod-0999 / 精确一项、选择正确 |
| Clear search / 清空搜索 | 1,000/1,000 restored; current selected path remains 0999 / 恢复全部列表且保留选择 |
| Files → Covers → Files / 文件封面往返 | Selection remains 0999; count stays 1,000; placeholder cards appear / 选择和数量一致、占位封面显示 |
| Cover internal scroll / 封面内部滚动 | Scrollbar advanced from 0 to 451; cards advanced while toolbar, category and detail stayed fixed / 网格内部滚动、外层保持固定 |
| Refresh / 刷新 | Exact count, no duplicate rows observed; selected 0999 retained after completion / 数量与选择正确，未观察到重复项 |
| Maximize and restore / 最大化还原 | List, detail and deployment control remain usable; selection unchanged / 排列及操作区可见、选择不变 |
| Close / 关闭 | Test window removed from subsequent window listing / 窗口正常关闭 |

Actual XAML rasterization scale: **1.5**. RootGrid logical viewport changed from approximately **1905.33 × 987.33** to **2560 × 1369.33**, then restored. This does not verify other display scales, compact minimum widths, multiple monitors or DPI transitions.
实际缩放 150%，仅验证本次宽窗口及最大化/还原，不代表其他 DPI、最小宽度或跨屏切换均通过。

Local opt-in log: `App/WinUI3/repository-performance.log`. Three 1,000-item scan/refresh samples were recorded: scan median 38.83 ms (max 148.95), full projection median 29.52 ms (max 29.67), refresh median 111.11 ms (max 202.56). One completed explicit warm refresh: scan 31.88 ms, projection 22.26 ms, refresh 73.96 ms. Nested scopes must not be added together; samples include initialization and optional logging overhead, not frame latency or a statistically representative performance promise.
本地计时包含初始化和记录开销，不是帧延迟；范围嵌套，不能相加。仅三次样本，不作普适性能保证。

No error log was found in the inspected test runtime. Deployment target still contains only its fixture marker; no Mod deployment was triggered.
本次测试运行目录未发现错误日志；模拟目标仅有保留标记，没有执行部署。

### Stage 2 follow-up / 上阶段补充

The separate opt2 app was also opened this session: saved a synthetic profile, opened its English restore preview, observed complete 33-Mod inventory (32 keep + 1 external keep) and 64 unchanged values with visible tabs/acknowledgement and Restore disabled before acknowledgement. Cancelled with Escape and observed normal navigation/idle state. No Restore was executed. This verifies visible binding in that configuration, not actual game restoration, every dialog size, dual-language layout or every destructive branch.
另检查 opt2 英文模拟预览：33 项目录、64 个已一致参数，标签及确认可见，未确认时恢复按钮禁用；Esc 取消后恢复正常导航。未执行恢复，不能代替游戏实测或全部异常分支检查。

## Deliverables and preservation / 交付与保留

- Local test ZIP: `artifacts/local-test-repository-performance-r2-20261005/Integrated_Mod_Manager-v4.0.0-performance-opt3.zip`.
- Test ZIP SHA-256: `435481EB56D0E1BABDDD23A3E1C310C6FB40AFD1F37238A98B2C49B7087F4346`.
- Contains only authored runtime, synthetic TestData, local instructions and the explicit diagnostics marker. Packaging source is the clean validated build, not a used app directory with config/cache/logs.
- Stable 4.0.0 ZIP still hashes to `6099EC3CB19BAA655F0E2509246E313C1714A702FB33A2BEB952302901935E31`.
- Not published or connected to automatic updates. Launcher/game, network download, image-heavy performance, tray/Alt+Tab, fast multi-repository switching and other DPI/compact layouts remain unverified this stage.

See [performance guide](../development/Repository-Performance.md) and [regression checklist](../development/Regression-Checklist.md) for repeatable steps and remaining checks.
