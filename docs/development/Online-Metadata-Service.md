# Online metadata service / 在线元数据服务

Local opt6 continues stage 4 and the first of the six remaining checkpoints. It separates **Mod detail and file metadata** transport, parsing and caching from the window. Online feed/category catalogs, avatar/image binary fetching, translation and complete page-state orchestration are not all extracted yet. No layout/version/preset-schema change, public release, push or automatic-update change.
本地 opt6 拆分 Mod 详情与文件元数据，不声称已拆完在线列表、分类目录、头像/图片二进制、翻译和全部页面状态。不改布局、正式版本或预设格式，不推送、不发布、不改自动更新。

## Responsibilities / 职责

| Component | Responsibility |
| --- | --- |
| Core `GameBananaMetadataService` | Existing fixed API fields/endpoint, HTTP lifetime, byte/timeout limits, cancellation, freshness and bounded memory cache. / 沿用 API 字段，管理请求、限制、取消及缓存新鲜度。 |
| Core `GameBananaMetadataParser` | Wrapped/direct JSON, scalar conversion, file metadata, plain description and screenshot URLs. / 解析 JSON、数字、文件、纯文本及截图地址。 |
| Data `OnlineMetadataCacheAdapter` | Existing SQLite cache table, new raw namespace, file fallback and read-only legacy compatibility. / 沿用 SQLite 表，新增原始缓存范围及文件回退，只读兼容旧缓存。 |
| WinUI adapters | Character/language inference, hotness display, existing file-selection policy, access warnings, shortcut extraction and translation. / 保留角色、语言、热度、文件选择、访问提示、快捷键及翻译。 |

`FetchGameBananaModCardAsyncV2` and `GetOnlineModDetailsAsync` now adapt service results instead of requesting/parsing/storing them directly. `StripHtmlToPlainText` delegates to the Core formatter with the existing whitespace/HTML rules. Localized derived details are rebuilt from cached original text, not persisted into the raw cache.
窗口适配方法只转换结果；原始文字缓存不混入中文翻译或派生快捷键说明，既有格式化规则保留。

## HTTP and parsing / 请求与解析

- Borrow the existing HttpClient; the service does not dispose it or add login/retry rules. The source API endpoint is fixed. Other returned HTTP(S) URLs are not domain trust/malware guarantees.
- 复用客户端，不释放、不新增登录或重试规则。元数据接口固定，但返回的 HTTP(S) 地址不等于可信站点或恶意代码扫描结论。
- Headers-first reads; default request timeout is 30 seconds. Caller cancellation remains an OperationCanceledException, while timeout becomes TimeoutException.
- 默认请求超时 30 秒，区分主动取消与超时。
- Maximum HTTP body is 4 MiB, checked from Content-Length and actual streaming bytes (including absent/misleading lengths). UTF-8 JSON/BOM supported; malformed/invalid UTF-8, non-object and no-recognized-field responses are rejected before caching. This is a response limit, not a global application-memory quota.
- 响应上限 4 MiB，同时检查声明和实际读取；支持 UTF-8/BOM，拒绝格式错误、非对象及没有已知字段的响应。不等于整个应用内存配额。
- HTML-to-text regex calls have a two-second per-call timeout. The HTTP timeout does not include every parser/cache/SQLite operation.
- HTML 转文字的每次正则调用有两秒限制；请求超时不代表全部解析与缓存操作的总时限。
- Optional malformed screenshots do not discard valid text. Accept encoded screenshot arrays and native arrays; preserve 800 → 530 → original priority, preview-first ordering and deduplication. Plain screenshot filenames are URI-escaped; directory traversal, non-web URL schemes and dot names are skipped.
- 可选截图损坏不丢弃文字；兼容字符串内数组与直接数组，保留分辨率优先级、预览顺序与去重，跳过不安全文件名和非网页协议。
- Parse file objects and arrays; tolerate string/numeric counts, clamp negative/overflowed counts and reject non-HTTP(S) download addresses. Invalid dates stay unknown. Default newest supported archive/manual ordering still uses `OnlineDownloadSelectionPolicy`.
- 文件对象/数组及字符串数字可读；计数限制范围，无效日期不猜测；默认最新压缩包及手选排序仍沿用原策略。

## Cache policy / 缓存规则

- Details expire after three days in **memory as well as persistent caches**. Future timestamps/invalid entries are not used. File lists and update timestamps always request fresh metadata; they do not use the details cache. `forceRefresh` is available at service level, not a new UI control.
- 详情内存和持久缓存均为三天；未来时间及无效记录不使用。文件列表和更新时间始终请求最新数据。强制刷新仅为服务接口，本次未增加界面按钮。
- Memory cache: at most 128 entries and 16 MiB estimated UTF-16 text/URL payload, evicting oldest cached entries. This estimate does not include all runtime objects. Return copied image arrays so callers cannot mutate cached originals.
- 内存最多 128 条，文字/URL 的 UTF-16 估算量上限 16 MiB；不包含全部对象开销，返回图片数组副本。
- New cache kind `online-details-raw-v1`, file `metadata-mod-<id>.json`. Existing `online-details` SQLite values and `mod-<id>.json` files are fallback reads only; extra old UI fields are ignored. No old entry/file or table schema is replaced.
- 使用独立新缓存名称；旧 SQLite 记录和旧 JSON 只作回退读取，不覆盖旧内容，不改表结构。
- File writes use a unique CreateNew `.download` partial and move only after write/flush/cancellation checks. File publication failure cleans the owned partial and still allows SQLite fallback. Cache I/O failures must not discard a successful network result.
- 写入本次专属临时文件，写完检查后发布；文件保存失败清理本次临时项，SQLite 可独立回退。缓存失败不丢弃正常在线结果。
- No partial/failed HTTP response is published as a successful cache. A fully written valid cache can remain if cancellation happens after publication; cache publication is not an install-record transaction or cross-process lock. SQLite reads/writes already in progress remain synchronous/cooperative. Legacy SQLite cache reads retain the store's existing deserialization behavior; the 4 MiB streaming bound applies to HTTP and file-cache reads, not old database blobs before deserialization.
- 失败/不完整响应不写成功缓存；发布完毕后取消可保留有效缓存。不是安装记录事务或跨进程锁，已有 SQLite 操作不能立即中断。4 MiB 流读取限制针对 HTTP 和文件，不宣称旧数据库字段反序列化前也有相同限制。
- Requests for the same uncached ID are not coalesced. Subsequent [opt7 adapters](./Online-Page-State.md) add detail revisions and download admission, not HTTP coalescing.
- 同 ID 的并发未命中请求尚未合并；后续 opt7 已接入详情代次和下载入口防重，但不是 HTTP 请求合并。

## Verification / 验证

54 Core and 15 DataStore cases use fake HTTP plus real isolated SQLite/files. They cover JSON variants, images, latest-file policy, missing/error fields, counts/dates, size/UTF-8, timeout/cancellation, freshness, copied arrays, forced refresh, cache faults, legacy shape, failed publication and service recreation. Total local regression count: 578 (527 / 24 / 27).
新增 69 项，完整 578 项；使用模拟 HTTP 与隔离 SQLite/文件，不访问网站或游戏。

See [opt6 evidence](../verification/online-metadata-opt6-20261005.md). Live API shape/redirect/rate-limit behavior and visible WinUI loading/cancel/installed-state behavior remain separate manual checks. Subsequent [opt7 adapters](./Online-Page-State.md) have offline fixtures; portable presets are the next development checkpoint.
实际接口、跳转、限流和可见窗口行为仍需独立手测。后续 opt7 已补适配器离线测试，下一开发检查点为可迁移预设。
