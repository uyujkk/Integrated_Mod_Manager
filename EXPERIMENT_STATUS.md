# v3.8.1 Persistent State Lab / 持久变量状态实验

> **Archive status / 归档状态:** Unreleased, unsupported, and excluded from every Integrated Mod Manager release package. / 未发布、未支持，不包含在任何集成化 Mod 管理器正式发布包中。

## 中文

### 目标

仅捕获第三方 Mod 明确声明为 `global persist`、并以标量数值写入 `d3dx_user.ini` 的状态，避免恢复整个共享配置文件。

### 实现路线

- 等待 EFMI 状态文件的长度和修改时间连续稳定后再读取。
- 解析当前启用 Mod 的 `global persist` 声明，只保存合法变量名和有限数值。
- 离线模式在游戏退出后合并目标变量、原子替换文件、验证结果并启动 XXMI，不使用 F10。
- 运行中模式生成带固定标记的临时桥接 INI，由用户手动执行两次 F10，再验证目标值。
- 不读写游戏进程内存，不自动模拟按键，不修改第三方 Mod 的源 INI。

### 得到的线索

EFMI 可以在游戏快捷键改变持久变量时实时重写 `d3dx_user.ini`；这证明“内存状态可以写到文件”，但不证明 EFMI 会实时接受管理器对文件的外部修改。离线合并比运行中覆盖更可控。

### 未解决问题

- 纯内存状态、字符串、向量、命令型状态和未声明为 `global persist` 的变量无法处理。
- Mod 的后续 `post` 命令、命名空间和加载顺序可能再次覆盖恢复值。
- 双 F10 桥接依赖具体 XXMI/3DMigoto 分支的重载语义。
- 没有完成任意第三方 Mod、多个 Mod 组合和重新启动后的真实游戏验收。

本分支只保留研究源码、测试和说明。请勿将它作为正式应用或账号安全保证。

## English

### Goal

Capture only scalar values explicitly declared as `global persist` by third-party Mods and written to `d3dx_user.ini`, avoiding replacement of the entire shared configuration file.

### Prototype approach

- Wait for consecutive stable file length and modification-time samples before reading EFMI state.
- Parse `global persist` declarations from currently enabled Mods and accept only constrained names and scalar values.
- In offline mode, merge target variables after the game exits, atomically replace and verify the file, then start XXMI without F10.
- In the live experiment, generate a marked bridge INI, ask the user for two manual F10 reloads, and verify the resulting values.
- Do not read or write process memory, synthesize keyboard input, or modify the source INI files of third-party Mods.

### Evidence and limits

EFMI can rewrite `d3dx_user.ini` when in-game shortcuts change persistent variables. That proves memory-to-file persistence, not live acceptance of external file edits. Offline merging is more controlled, but memory-only state, strings, vectors, commands, undeclared variables, later `post` overrides, namespace rules, and loader ordering remain outside the model. No universal real-game acceptance test was completed.

This branch preserves research source, tests, and documentation only. It is not a supported build or an account-safety guarantee.
