# v3.8.1 Hot Injection Lab / 热注入实验

> **Archive status / 归档状态:** Unreleased, unsupported experiment. This branch is preserved for research and source history only. It is excluded from supported release packages. / 未发布、未受支持的实验分支，仅用于研究和代码追溯，不进入正式发布包。

## 中文

### 目标

本实验尝试把 XXMI/3DMigoto 的整个 `d3dx_user.ini` 当作跨 Mod 状态快照。保存时不推断 Mod 的变量名或快捷键；恢复时完整替换该文件，然后向前台窗口发送 F10 请求重载。

### 已实现

- 按原始字节保存完整 `d3dx_user.ini`。
- 记录文件长度和 SHA-256，恢复前进行完整性校验。
- 通过同目录临时文件执行原子替换。
- 替换前备份当前文件，失败时尝试回滚。
- 写入后倒计时，并通过键盘事件向前台窗口发送 F10。
- 添加了快照哈希、长度校验和原子写入的自动化测试。

### 得到的线索

- 真实状态边界不是“当前 Mod 的几个变量”，而可能是多个 Mod 共用的用户状态文件。
- 文件层面的快照、校验、备份和原子替换可以可靠实现。
- 能成功替换磁盘文件，并不等于游戏内运行状态已经切换。

### 未解决的问题与失败原因

- `d3dx_user.ini` 是共享文件，整文件恢复会同时覆盖其他 Mod 的状态，粒度过大。
- F10 的具体顺序由 3DMigoto/加载器决定。它可能先把当前内存里的旧变量写回文件，再重载配置，导致刚恢复的快照被覆盖。
- 不同 XXMI/3DMigoto 分支对 F10、退出保存和状态加载的行为可能不同。
- 没有完成可重复的真实游戏端到端验证；当时观察不到稳定、可见的恢复效果。
- 向前台窗口发送 F10 也依赖焦点时机，不能作为可靠的配置接口。

因此，这个分支只证明了“文件快照机制”本身可行，没有证明“游戏内状态恢复”可行。它也不构成账号安全或兼容性保证。

## English

### Goal

This experiment treated the entire XXMI/3DMigoto `d3dx_user.ini` as a cross-Mod state snapshot. Saving did not infer Mod variable names or hotkeys. Restoring replaced the complete file and then sent F10 to the foreground window to request a reload.

### Implemented

- Preserve the complete `d3dx_user.ini` byte-for-byte.
- Record file length and SHA-256 and validate integrity before restore.
- Replace the destination atomically through a temporary file in the same directory.
- Back up the current file first and attempt rollback on failure.
- Start a countdown after writing and synthesize F10 for the foreground window.
- Test snapshot hashing, length validation, and atomic writes.

### Findings

- The real state boundary may be a shared user-state file used by several Mods, not a few variables owned by the selected Mod.
- File snapshots, integrity validation, backups, and atomic replacement are straightforward to implement reliably.
- Successfully replacing the disk file does not mean the running game state changed.

### Unresolved problems and failure mode

- `d3dx_user.ini` is shared, so a whole-file restore also overwrites unrelated Mod state and has overly broad scope.
- The F10 sequence is controlled by 3DMigoto/the loader. It may save old in-memory variables to disk before reloading configuration, overwriting the snapshot that was just restored.
- F10, exit-save, and state-load behavior can differ across XXMI/3DMigoto variants.
- No repeatable real-game end-to-end validation was completed; the experiment did not produce a stable, visible restoration result.
- Synthesizing F10 depends on foreground focus timing and is not a reliable configuration interface.

This branch proves only that the file-snapshot mechanism works. It does not prove that in-game state restoration works, and it is not an account-safety or compatibility guarantee.
