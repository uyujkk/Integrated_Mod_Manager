# Online download transport opt4 / 在线下载传输验证

Date: 2026-10-05. Branch `optimization/repository-scan`, base `a3efa8dd84e3ef1fe6e8c695457dd2647a27af54` plus uncommitted stage 1–4 changes. Local checkpoint only; no push, public release, stable replacement or automatic-update change. EXE versions remain 4.0.0.0.
本地第四阶段第一小步，保留前三阶段改动，不推送、不发布、不覆盖正式版、不接自动更新。

## Implemented / 本次实现

- Core HTTP archive transport separated from WinUI controls. Borrowed HttpClient, existing headers/referrer, streamed writes and approximately 125 ms intermediate-progress throttle.
- Core 提取 HTTP 压缩包传输，复用客户端及请求头，流式写入和中间进度节流。
- Safe Windows leaf-name handling for server/preferred/URI/fallback names. Owned CreateNew partial files; no-overwrite final/extension moves use numbered names. Task stores the corrected actual archive path.
- 统一安全文件名；专属临时文件，无覆盖落盘和补扩展名；任务记录实际路径。
- Bounded 512-byte web-response sniff replaces whole-file loading. Empty/web responses, expected-size errors, cancellation and stream failures clean this attempt's partial file, best effort.
- 网页检测限制读取量；空文件、网页、大小异常、取消和断流尽力清理本次临时文件。
- Latest eligible archive selection, manual-file picker, character destination confirmation, extraction policies, tracked metadata and installed detection remain in their existing paths. This is not the whole download/page-state extraction.
- 最新文件、手选文件、角色目录确认、解压及安装记录保留原流程，本次不声称已完成全部下载/页面状态拆分。

## Automated evidence / 自动证据

Final run: `artifacts/optimization-download-verified-opt4-r2-20261005`; log: `artifacts/optimization-download-opt4-r2-build.log`.

| Suite / 测试组 | Passed / 通过 | Failed / 失败 |
| --- | ---: | ---: |
| Core | 454 | 0 |
| DataStore | 9 | 0 |
| UpdateAgent | 27 | 0 |
| Total / 合计 | **490** | **0** |

- 50 new download cases, 440 previous regression cases retained. An initial test fixture used a Content-Disposition string rejected by .NET before reaching the service; corrected the HTTP fixture and tested whitespace normalization separately. Final targeted suite: 50/50.
- 新增 50 项，保留原 440 项。初次一项 HTTP 测试头格式被 .NET 拒绝，修正模拟头后将空白规范测试独立验证，最终全部通过。
- Component line/branch coverage: Core 95.8% / 91.5%; Data 87.0% / 82.7%; Updater 60.2% / 60.6%. All configured gates passed, not whole-UI coverage.
- WinUI x64 Release: zero warnings/errors; minimal runtime ZIP contract and SHA-256 sidecar passed.
- Clean runtime ZIP hash: `E340582F980F1F50243932240994F552A8E5AC2424DF6A67A2D728B3E9C1FB4C`.
- NuGet vulnerability audit disabled for offline validation; no dependency version changes. This is not a vulnerability-audit pass.

The tests use a fake in-process HttpMessageHandler and `example.invalid`, **not** live network access. They verify headers/name precedence, Unicode, slash paths/ADS/device names, bounded names, file/directory collisions, known/unknown size, HTTP failure, webpage/empty content, archive signatures, borrowed-client reuse, multiple cancellation points and failure after a written chunk.
测试使用进程内模拟响应，不访问网络；覆盖名字、路径、冲突、大小、响应、签名、客户端生命周期及多处取消/断流。

One smoke test creates a real synthetic ZIP with an INI, selects a Chinese character folder using the unchanged match policy, downloads into that folder and reads the content after .NET ZIP extraction. It verifies service + policy + ZIP readback, **not** WinUI's complete extraction/security/metadata chain. Signature-only fixtures in the other tests are explicitly not installable Mods.
真实模拟 ZIP 测试验证服务、角色选择规则和 ZIP 回读，不代替窗口完整解压、压缩包安全检查和记录流程；其他签名测试数据不是可安装 Mod。

## Local deliverable / 本地交付

Final independent package: `artifacts/local-test-online-download-opt4-r2-20261005/Integrated_Mod_Manager-v4.0.0-download-opt4.zip`.
SHA-256: `FBCAD936C516B9FD8B5AB6227C85DEE33A1600615288E85788EE917F52EC9C76`.

Extracted app: `artifacts/local-test-online-download-opt4-r2-20261005/App/IntegratedModManager.exe`. Includes the 1,000-Mod text-only mock repository with target-directory marker and synthetic INI, no real launcher/configuration. Performance diagnostics marker is not included. The clean validated runtime is the package source, not a previously used app directory. The initial local package is superseded by r2, which clarifies the bundled guide's source-only evidence reference; runtime binaries are identical.
独立应用带纯文本模拟仓库，不含真实启动器/配置，也不含性能诊断开关标记；由干净构建源打包。

All four EXEs stay 4.0.0.0. Stable ZIP hash remains `6099EC3CB19BAA655F0E2509246E313C1714A702FB33A2BEB952302901935E31`. This stage does not replace earlier native-window evidence or claim a newly observed opt4 window/download session.
四个入口版本保持不变，正式 ZIP 哈希保持一致。本阶段未做新 opt4 原生窗口下载实测。

## Not verified / 未验证

Live GameBanana transfers, auth/redirect/rate-limit behavior, visible progress/cancel timing, complete extraction/routing/metadata, installed badges, image-heavy performance, real game effects and automatic updates. Cancellation of existing metadata requests/external extraction tools is still cooperative under the existing behavior. Another process changing parent directories/links is not locked out; partial cleanup can fail under OS permissions.
真实站点、跳转限流、窗口进度取消、完整解压归类、安装标识、图片性能、游戏和自更新仍待验证；原元数据及外部解压工具不保证即时取消；无父目录进程锁，权限问题可导致临时文件残留。

Next: isolate task execution/state and repository/metadata coordination in a separate tested slice. See [download service scope](../development/Online-Download-Service.md).
