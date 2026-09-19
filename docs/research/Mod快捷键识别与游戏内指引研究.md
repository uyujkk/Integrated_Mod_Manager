# Mod 快捷键识别与游戏内指引研究 / Mod Hotkey Detection and In-Game Guidance

## 当前实现 / Implemented

管理器在在线下载或本地压缩包导入成功后，以只读方式递归检查 Mod 目录中的 `.ini` 文件，并识别 3DMigoto / XXMI 常用的 `[Key...]` 段与 `key = ...` 定义。识别结果包括：

- 组合键，例如 `ctrl no_alt /` 转换为 `Ctrl+/`；
- 常见鼠标、方向键、功能键和 Xbox 手柄键；
- `type = cycle`、`type = hold` 等行为；
- `condition`、变量赋值或 `run` 命令所指向的功能线索；
- 原始文件名和段名，便于用户核对。

扫描器不写入 Mod 文件，不执行 INI 命令，不跟随目录联接，并限制文件数量、文件大小和显示条目数。已有手工快捷键或下载中心提供的快捷键说明优先，自动识别不会覆盖它们。用户首次选择一个尚无快捷键说明的本地 Mod 时也会触发一次识别。

This build performs a read-only scan of `.ini` files after an online download or local archive import. It recognizes common 3DMigoto / XXMI `[Key...]` sections and `key = ...` declarations, normalizes keyboard, mouse, and Xbox input tokens, and records behavior and target hints. It does not execute or rewrite configuration, does not traverse reparse points, and applies file-count, file-size, and result limits. Existing manual or online-provided metadata always takes precedence.

## 为什么可以识别 / Why Detection Is Feasible

3DMigoto 的标准配置把热键定义为 INI 中的 `[Key...]` 段，并允许按键组合、变量赋值、命令运行以及 `cycle` 等行为。实际 Mod 往往沿用同一语法，因此静态读取可以为用户生成有用的快捷键索引。

限制也很明确：第三方 Mod 可以使用自定义命名、条件表达式、跨文件变量、`run` 链和作者自己的脚本约定。管理器能可靠识别“按了什么键”，但对“最终产生什么视觉效果”的描述只能是线索，不能假装理解任意脚本的完整语义。

3DMigoto's normal configuration model places bindings in `[Key...]` INI sections and supports key combinations, variable assignments, command execution, and behaviors such as cycling. This makes static detection useful. However, arbitrary Mods can use custom naming, conditions, cross-file variables, and command chains, so the manager can identify the input more reliably than the final visual effect.

## 游戏内显示方案比较 / In-Game Display Options

| 方案 / Option | 可行性 / Feasibility | 风险与结论 / Risk and decision |
|---|---|---|
| 管理器创建置顶透明窗口 / External always-on-top overlay | 窗口化、无边框窗口模式下可行 | 独占全屏不可靠；涉及窗口捕获、焦点、DPI、多显示器和反作弊兼容性。本版不实现。 |
| 注入额外图形钩子 / Additional graphics hook | 技术上可行 | 会扩大注入和账号安全风险，与 XXMI/3DMigoto 的现有渲染链冲突可能性最高。本项目不采用。 |
| 使用 3DMigoto 自身的绘制路径 / Native 3DMigoto drawing | 可行，且已有 Mod 使用 `CustomShader`、`draw_2d.hlsl` 和图片资源绘制帮助面板 | 需要针对游戏、Importer 版本和 Mod 命名空间生成资源；自动修改任意 Mod 容易造成变量、资源和按键冲突。本版只识别已有帮助键，不自动注入。 |
| 启动前或切换后在管理器显示指引 / Manager-side guidance | 稳定、安全 | 已通过快捷键标签页实现，是当前默认方案。 |

本仓库的实际样本中已经存在 `[KeyHelp]` 和自定义着色器绘图配置，证明“由 3DMigoto 在游戏画面内显示帮助图”是可行的。合理的后续路线是做一个**明确选择加入、按游戏和 Importer 版本适配**的 Overlay Companion 模板，并在真实游戏中验证后再提供；不能把一段通用 INI 无条件写进所有 Mod。

The inspected local sample already contains a `[KeyHelp]` binding and a custom-shader image path, demonstrating that a 3DMigoto-rendered help panel is possible. A responsible follow-up would be an explicit opt-in Overlay Companion template tied to a known game and Importer version, followed by real in-game validation. A universal INI block should not be injected into arbitrary Mods.

## 本版边界 / Current Boundary

本版完成“读取并整理已有快捷键”，不创建额外游戏注入、不改写下载的 Mod，也不宣称已完成真实游戏内覆盖层验证。若 Mod 自带 `[KeyHelp]`，管理器会把它作为普通快捷键展示，用户可以使用作者原有的游戏内帮助功能。

This version detects and presents existing hotkeys. It does not add another game hook, rewrite downloaded Mods, or claim a validated universal in-game overlay. If a Mod already provides `[KeyHelp]`, the manager exposes that binding so the user can invoke the author's own help panel.

## 参考 / References

- [3DMigoto standard `d3dx.ini` configuration](https://github.com/bo3b/3Dmigoto/blob/master/Dependencies/d3dx.ini)
- [3DMigoto DirectX 11 overlay rendering source](https://github.com/bo3b/3Dmigoto/blob/master/DirectX11/HackerDXGI.cpp)
- [3DMigoto releases](https://github.com/bo3b/3Dmigoto/releases)
