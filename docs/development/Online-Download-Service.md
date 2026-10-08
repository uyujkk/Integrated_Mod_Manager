# Online download transport / 在线下载服务

Local opt4 is the first small slice of download/online separation. It does not redesign the page, change preset formats, publish a release or enable automatic updates.
opt4 是下载/在线拆分的第一小步，不重构页面、不改预设格式、不发布、不改自动更新。

## Boundaries / 职责边界

| Component / 组件 | Responsibility / 职责 |
| --- | --- |
| Existing selection and destination UI / 原文件选择与目录界面 | Refresh metadata, default newest eligible archive, manual file choice, character aliases, explicit destination confirmation / 更新元数据、默认文件、手选文件、角色别名、位置确认 |
| Core `OnlineArchiveDownloadService` | HTTP headers, streamed copy, throttled byte progress, response checks, owned-partial cleanup and no-overwrite publication / HTTP、流式写入、节流字节进度、响应检查、清理本次临时文件、无覆盖落盘 |
| WinUI adapter / 窗口适配 | Marshal progress to dispatcher, guard stale phase callbacks, localize byte counts/size errors and update task path / 切回界面线程、阻止旧阶段回调、语言显示、更新实际路径 |
| Existing extraction/metadata path / 原解压与记录流程 | Supported-format decision, extraction validation, tracked source/preview/shortcut metadata and repository refresh / 格式判断、解压验证、来源预览快捷键记录、仓库刷新 |

The service borrows the existing HttpClient; it neither creates new retry/auth rules nor disposes that client. No WinUI, localization, SQLite or game dependencies are introduced into Core.
复用原 HttpClient，不引入新的重试/登录策略，也不释放借用客户端。Core 不依赖界面、翻译、SQLite 或游戏。

## Preservation and fixes / 保留与修复

- Default file ordering, archived-file fallback and manual selection remain governed by `OnlineDownloadSelectionPolicy`. The character match policy and confirmation dialog remain unchanged; ambiguous matches are not guessed.
- 默认最新可用压缩包、归档文件回退、手动选择、角色匹配及目录确认保留；歧义不自动猜测。
- Filenames use server `filename*` / `filename`, preferred file, response URI and title fallback. Each passes Windows leaf-name normalization: slash paths lose parent segments, invalid/ADS characters are replaced, device names prefixed and length bounded.
- 文件名仍沿用优先顺序，但统一规范为单个 Windows 文件名，不能把 HTTP 文件名作为路径；处理 ADS 字符、设备保留名和超长名称。
- Download to a unique `.imm-<id>.download` file using CreateNew. Validate before publishing with a no-overwrite move; existing files/directories force a numbered filename. Partial and failed response checks clean only the file created by this attempt, best effort.
- 先写本次专属临时文件，检查后无覆盖移动；同名文件/目录使用编号。失败或取消仅尽力清理本次临时文件，不删除原有压缩包。
- Unknown length remains indeterminate. The expected file size still takes precedence over Content-Length, with the existing 1,024-byte tolerance. Initial/final progress is emitted and intermediate updates are throttled to approximately 125 ms.
- 未知大小保持不定进度，优先 API 声明大小，沿用 1,024 字节容差；初末通知、中途约 125 ms 节流。
- MIME/text-page detection reads at most 512 bytes instead of loading an entire response file. Recognized archive signatures retain the old precedence; this check does not prove a complete archive is valid or trustworthy.
- 网页误下载检查最多读取 512 字节，避免整文件读取；签名优先级保留，但文件头不代表压缩包完整或内容可信。
- Extension correction uses the same no-overwrite move and stores the **returned actual path** in the task, so later cancellation targets the corrected archive rather than the old name.
- 补扩展名也不覆盖同名文件，并记录实际新路径，避免后续取消清理旧文件名。

## Verification / 验证

50 new Core tests use an in-process fake HttpMessageHandler, never the network. Cover Unicode/header precedence, unsafe filenames, collisions, known/unknown sizes, HTTP errors, web/empty responses, broken streams, multiple cancellation points and archive signatures. One test builds a real synthetic ZIP, downloads it into the existing policy's matched Chinese character directory and reads its extracted INI using .NET ZIP extraction. This is a service/policy smoke test, **not** the WinUI downloader's complete extraction/metadata workflow.
新增 50 项测试使用进程内模拟 HTTP，不联网；其中一项生成真实模拟 ZIP，按现有规则选中文角色目录，下载并用 .NET 解压读取模拟 INI，不等于完整窗口下载流程验证。

Run all tests in a fresh artifact directory using `scripts/test-all.ps1`. See [opt4 evidence](../verification/online-download-opt4-20261005.md). Do not change the published 4.0.0 package to test this slice.
完整验证使用新的输出目录，保留正式包。

## Still outside this slice / 仍需后续工作

- GameBanana metadata fetching/parsing, image fetching, extraction and tracking remain in existing code. Only archive transport has been extracted.
- 元数据、图片、解压和安装记录仍在原实现中，本次仅提取压缩包传输服务。
- Destination confirmation authorizes the selected directory; this is not protection against another process changing parent links/directories concurrently.
- 用户选择目录的权限边界沿用原流程，不声称防止其他进程并发修改父目录或链接。
- Cancellation is cooperative. Existing external archive tools and metadata requests are not newly made instantaneously cancellable. Cleanup can leave an owned temporary file if the OS denies removal.
- 取消仍是协作式；本次没有把外部解压工具和元数据请求改成即时取消。系统拒绝删除时可能保留本次临时文件。
- After success the caller owns the downloaded file and extraction cleanup. Existing extraction-failure behavior and archive security policies are unchanged. This is not malware scanning, quota enforcement, resumable download or integrity-by-hash verification.
- 成功返回后由调用方负责下载文件及解压处理；不新增恶意代码扫描、配额、断点续传或哈希完整性验证。
- Real online downloads, auth/redirect/rate-limit behavior, visible progress/cancel timing, extraction destinations, installed badges and manual-file-picker behavior still need live checks in an isolated app.
- 实际网站、跳转、限流、窗口进度取消、完整角色解压归类、安装标识和手选文件仍待实测。

The subsequent opt5 slice adds [execution context and metadata coordination](./Online-Download-Execution.md), with captured repository identity and explicit cancellation/cleanup ownership tests. The limitations above describe the opt4 transport checkpoint; opt5 cancellation improvements and remaining limits are documented separately.
后续 opt5 已补任务上下文和元数据协调；上述边界描述 opt4 传输检查点，取消改进与保留限制见独立文档。
