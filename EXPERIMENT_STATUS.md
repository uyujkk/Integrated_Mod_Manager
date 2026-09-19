# v3.8.2 EFMI Resident Controller Lab / EFMI 常驻控制器实验

> **Archive status / 归档状态:** Unreleased, unsupported experiment. This branch is preserved for research and source history only. It is excluded from supported release packages. / 未发布、未受支持的实验分支，仅用于研究和代码追溯，不进入正式发布包。

## 中文

### 目标

这一轮实验放弃替换 `d3dx_user.ini` 和模拟 F10，转而让相关 Mod 在 EFMI 中保持常驻。管理器生成一个控制器，以 `global persist $active_profile` 保存当前组合，并给兼容的 Mod 入口增加方案门控。这样各 Mod 自己的 `global persist` 变量可继续由 XXMI/3DMigoto 管理，切换组合只改变 EFMI 内部的方案变量。

### 已实现

- 扫描配置方案引用的 Mod，并识别常见 EFMI Tools 结构。
- 检查 `$mod_enabled`、`$object_detected`、注册命令以及覆盖段是否能够安全门控。
- 为标准运行条件和快捷键入口增加控制器条件。
- 生成带 `[Key] type = cycle` 的方案控制器和受控 Mod 副本。
- 默认以 `Ctrl + Right` / `Ctrl + Left` 切换方案。
- 不直接写入真实 `EFMI\Mods`，不修改仓库源 Mod，非空输出目录拒绝覆盖。

### 得到的线索

- 相比复制整个共享状态文件，让 3DMigoto 自己持有每个 Mod 的持久变量，状态边界更清晰。
- “常驻 + 门控”可以绕开 F10 的保存/重载顺序竞争。
- 自动生成前必须能够证明所有实际入口都受到门控；不能只修改快捷键段。

### 尚未证明

- 未在真实游戏中完成生成目录的端到端验证。
- 未验证多个高资源 Mod 同时常驻时的显存、内存和帧率影响。
- 手写、旧式或非标准 INI 可能无法被安全分析，因此实现会拒绝自动生成。
- 尚未实现带备份和回滚的真实 EFMI 部署。

因此，本分支记录的是一个更有希望的设计方向，不代表功能已经适用于任意 Mod，也不是账号安全、兼容性或性能保证。

## English

### Goal

This experiment stopped replacing `d3dx_user.ini` and stopped synthesizing F10. Instead, related Mods remain resident in EFMI. The manager generates a controller whose `global persist $active_profile` selects the active combination and adds profile gates to compatible Mod entry points. Each Mod's own `global persist` values can remain under XXMI/3DMigoto control while switching changes only the EFMI profile variable.

### Implemented

- Scan Mods referenced by saved configurations and recognize common EFMI Tools structures.
- Inspect `$mod_enabled`, `$object_detected`, registration commands, and override sections for gateability.
- Add controller conditions to standard execution paths and Mod hotkey entry points.
- Generate controlled Mod copies plus a `[Key] type = cycle` profile controller.
- Use `Ctrl + Right` / `Ctrl + Left` as the default profile controls.
- Avoid writing to the real `EFMI\Mods` directory, avoid modifying repository sources, and refuse to overwrite a non-empty output directory.

### Findings

- Letting 3DMigoto retain each Mod's persistent variables gives a cleaner state boundary than copying a shared state file.
- A resident-and-gated design avoids the F10 save/reload ordering race.
- A generator must prove that every runtime entry point is gated; changing only hotkey sections is insufficient.

### Not proven

- No end-to-end validation of generated output in the real game was completed.
- VRAM, memory, and frame-rate cost with several resource-heavy Mods resident was not measured.
- Handwritten, legacy, or nonstandard INI layouts may not be safely analyzable and are therefore rejected.
- Deployment into the real EFMI directory with backup and rollback was not implemented.

This branch preserves a promising design direction, not a claim of compatibility with arbitrary Mods and not an account-safety, compatibility, or performance guarantee.
