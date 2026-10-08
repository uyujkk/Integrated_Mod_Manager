# Download execution checkpoint / 下载执行检查点

Local opt5 is the second slice of stage 4, after [archive transport](./Online-Download-Service.md). No page redesign, schema/version change, release, push or automatic-update change.
opt5 为第四阶段第二小步，不改页面布局、配置格式和正式版本，不发布、不推送、不接自动更新。

## Boundaries / 边界

| Component | Responsibility |
| --- | --- |
| `OnlineDownloadContext` | Captures repository ID/path/name before metadata awaits or the manual picker; game metadata mode and character aliases are also captured by WinUI. / 首次等待前固定仓库，窗口同时固定游戏元数据模式与角色别名。 |
| `OnlineDownloadSession` | Owns confirmed destination, actual archive path, acquired extraction path, prepare/commit boundary and cleanup plan. / 管理确认目录、本次实际资源、准备与提交分界、清理清单。 |
| `MainWindow.OnlineInstall.cs` | Prepares preview and shortcut data without modifying shared install dictionaries; commits in one non-awaiting UI-thread step. / 暂存预览和快捷键，随后在界面线程内一次提交记录。 |
| `OnlineTrackingScopePolicy` | Groups stale records by the most-specific repository root, preserves distinct existing installations. / 按最具体的仓库根目录处理失效重复记录，保留不同有效安装。 |

Default newest archive/manual selection, character-directory confirmation, installed-state identity, archive safety checks and external extractor priority remain in their existing policies. The task context is immutable; changing the active repository does not retarget the task. After refresh, selection is restored only if both repository ID and path still match the captured context.
保留默认最新压缩包、手选文件、角色目录确认、已安装判断和解压安全规则。任务不会跟随当前仓库切换；完成刷新后，也会重新核对仓库再选择新 Mod。

## Cancellation and ownership / 取消与资源归属

- Before commit, cancellation throws at preparation boundaries and does not write source/link/shortcut install records. Details HTTP requests and preview requests receive cancellation; translation waits may stop while their cache request finishes in the background.
- 提交前取消不写安装来源、链接和快捷键记录。详情与图片请求传入取消；翻译等待可终止，但已有翻译缓存请求可能继续完成。
- Extraction and shortcut scanning remain cooperative: wait for the existing worker/tool to finish before cleaning its folder. This is not immediate external-process termination.
- 解压和扫描仍为协作式取消，需要等已有工具/工作线程结束，再清理其目录，不声称即时终止外部进程。
- Owned resources must be direct children of the user-confirmed destination. The caller registers the actual downloaded/corrected file and only a newly created extraction directory. Cancellation cleans those resources; failure retains the downloaded archive for diagnosis but removes incomplete extraction, best effort.
- 仅登记确认目录下本次获得的直接子项；取消清理本次压缩包和解压目录，失败保留下载包以便诊断，清理不完整解压目录。清理失败会记录日志。
- Commit is synchronous, without another await/cancellation point. After successful commit, cleanup returns no resources—even if a later refresh/notification fails or cancellation arrives. A failed tracking-config save is a visible warning, not grounds to delete an installed Mod.
- 提交不包含等待；提交成功后，刷新失败或迟到的取消均不删除安装结果。配置保存失败会显示警告，不删除已经安装的 Mod。
- The generic commit callback must not partially mutate and then throw. The WinUI callback performs dictionary assignments and reports the existing tracking-config save failure as metadata. This is **not** a multi-file disk transaction or crash recovery protocol; shell config save still has its existing independent error handling.
- 通用提交回调要求不能修改一半后抛错；窗口保存错误以警告返回。此处不是多文件磁盘事务或崩溃恢复机制，仓库壳配置仍沿用独立错误处理。
- Lexical path checks and a pre-creation existence check are not a lock against another process swapping directories, links or files. No claim of cross-process race-proof ownership; OS-denied cleanup can leave temporary resources.
- 路径校验及创建前检查不等于文件系统锁，不能保证抵御其他进程并发替换目录、联接或文件；系统拒绝清理时可残留临时资源。

## Tracking records / 安装记录

A GameBanana ID identifies a website entry, not one local installation. Installing the same entry in another repository, or a second existing folder, no longer removes the first valid record. During configuration cleanup, only missing records with a duplicate source identity **within the same scope** are deduplicated; the newest missing record wins when all are missing. Unscoped downloads use the parent directory, and nested repository roots use the most-specific match. Existing record format is unchanged.
同一个网站 ID 不代表唯一的本地安装。跨仓库或另一有效目录中的安装记录都会保留；仅清理同一范围内来源相同的失效重复项。全部失效时保留时间较新的记录。未落在仓库内的下载按父目录分组，嵌套仓库使用最具体根目录；数据格式不变。

## Verification / 验证

19 new offline cases cover captured context, delayed preparation, cancellation before commit, late cancellation, preparation/commit errors, duplicate commit attempts, direct-child ownership, corrected archive paths, nested roots, stale tracking and real text-only cleanup fixtures. No network, real Mod, launcher or game is used. See [opt5 evidence](../verification/online-execution-opt5-20261005.md).
新增 19 项离线测试，不使用网站、真实 Mod、启动器或游戏。模拟目录只含文本文件。

Manual checks remain required: live newest/manual file downloads, cancel during details/preview/extraction, switch repository mid-download, multiple installed copies, restart/readback of tracking config, warning on denied config writes, and completed-task directory/installed badge behavior. Do these only in an isolated app and mock repository. Compilation and Core tests do not prove the visible WinUI flow.
窗口与网站实测仍独立进行：最新/手选下载、多个阶段取消、下载中切仓库、多安装副本、重启回读记录、拒绝配置写入警告、完成目录与已安装标识。只使用隔离应用和模拟仓库；编译及 Core 测试不代表完整窗口流程已实测。

The subsequent [opt6 metadata checkpoint](./Online-Metadata-Service.md) extracts Mod details/file metadata fetching, parsing and caching. [Opt7 page-state adapters](./Online-Page-State.md) now have headless fixtures linked from production source. Native-window/picker/live-website verification remains separate; next development checkpoint is preset portability.
后续 opt6 已拆元数据，opt7 已补生产适配器无窗口测试；原生窗口/系统弹窗/网站检查仍待完成，下一开发检查点为可迁移预设。
