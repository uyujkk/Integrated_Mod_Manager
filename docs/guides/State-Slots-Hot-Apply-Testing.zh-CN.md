# 状态槽位与 F10 热应用测试

本文说明如何在不启动游戏的情况下，测试单 Mod 状态槽位的保存、恢复和 F10 热应用。测试用的是仓库自带的合成测试数据，以及一个模拟加载器脚本 `scripts/fake-3dmigoto-loader.ps1`。通过之后，再按最后一节在真实游戏里确认。

[English](./State-Slots-Hot-Apply-Testing.en.md)

## 模拟加载器做什么

真实的 3DMigoto / XXMI 加载器是这样处理 `d3dx_user.ini` 的：

- 游戏里切换的 `global persist` 变量先只保存在内存中；
- 按 **F10** 重新加载配置时，先把内存里的值写回 `d3dx_user.ini`，再读取文件；
- 退出游戏时，把内存里的值写回文件。

所以游戏运行时，直接改 `d3dx_user.ini` 没有用：下一次 F10 或退出时，文件会被内存里的旧值覆盖。热应用的做法是在发送 F10 的几秒内把文件设为只读，让加载器写回失败，重新读到的就是刚写入的值。

模拟加载器用一个窗口重现这套行为：

- 表格里的值相当于加载器内存，直接修改就相当于在游戏里切换；
- 窗口在前台时按 F10，会先保存再读取，日志会写明是否因为只读而跳过了保存；
- 关闭窗口时把内存写回文件，相当于退出游戏。

## 一、准备

1. 在仓库目录打开 PowerShell，复制一份测试数据（目标文件夹不能已存在）：

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\scripts\fake-3dmigoto-loader.ps1 -CreateFixture D:\IMM-HotTest
   ```

   脚本会打印下面要填的三个路径。
2. 打开测试包里的管理器，新建一个仓库：
   - Mod 存储文件夹：`D:\IMM-HotTest\ModRepository`
   - 目标 Mods 文件夹：`D:\IMM-HotTest\Loader\Mods`
   - 不要勾选目录联接。测试数据里的 Mod 已经以复制方式部署好了。
3. 在仓库页选择 `TestCharacter` → `TestModA`，打开详情的“状态槽位”页，把 `d3dx_user.ini` 设为 `D:\IMM-HotTest\Loader\d3dx_user.ini`。
4. 启动模拟加载器：

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\scripts\fake-3dmigoto-loader.ps1 -UserIni D:\IMM-HotTest\Loader\d3dx_user.ini
   ```

## 二、保存槽位

1. 在模拟加载器里把 `...\TestModA\mod.ini\hair` 改成 `2`，然后让窗口在前台并按 **F10**。日志应显示 `saved memory` 和 `reloaded file -> hair=2`。这一步相当于在游戏里按 F10，让加载器把当前状态写进文件。
2. 在管理器里点“保存槽位”，命名为“发型 2”。列表应显示“发型 2 · 2 · 当前”（2 个变量：`hair`、`coat`）。
3. 在模拟加载器里把 `hair` 改成 `0`，按 F10，再保存一个槽位“发型 0”。
4. 打开“更多…”，分别试一下“用当前值更新所选槽位”和“重命名所选槽位”。

## 三、关闭游戏后恢复（原有方式）

1. 关闭模拟加载器窗口，相当于退出游戏，它会把内存写回文件。
2. 在管理器里选择“发型 2”，点击“恢复加载器状态”，勾选“我已完全关闭游戏”，然后确认。
3. 检查结果：
   - `d3dx_user.ini` 里 `hair = 2`；
   - 旁边多了一个 `d3dx_user.ini.imm-persist-*.bak` 备份；
   - 重新启动模拟加载器，表格里 `hair` 为 2。

## 四、F10 热应用（游戏运行中）

1. 保持模拟加载器运行。在管理器的“快捷键”页，目标游戏窗口选择 `Fake 3DMigoto loader (IMM test)`，并开启输入。
2. 在“状态槽位”选择“发型 0”，点击“恢复加载器状态”。这次勾选“游戏正在运行：写入后发送 F10 热重载”，然后确认。
3. 预期结果：
   - 模拟加载器窗口被切到前台；
   - 日志依次出现 `F10: save skipped, d3dx_user.ini is read-only`，然后是 `reloaded file -> hair=0`；
   - 管理器状态行显示“已发送 F10……”；
   - 文件 `d3dx_user.ini` 不再是只读。
4. **对照实验**（说明为什么需要热应用）：
   - 加载器保持运行，恢复“发型 2”，只勾选“我已完全关闭游戏”；
   - 然后在模拟加载器里手动按 F10；
   - `hair` 会变回 0，因为加载器先把内存里的旧值写回了文件。

## 五、置顶浮窗热切换

1. 在“快捷键”页打开“在置顶浮窗中热切换状态槽位”，Mod 选择 `TestModA`。
2. 按 **Ctrl + Alt + K** 打开浮窗，浮窗会列出“发型 0”“发型 2”，带 ● 的是当前槽位。
3. 点击另一个槽位。预期：模拟加载器日志与第四节相同，浮窗里的 ● 移到这个槽位，浮窗状态行显示结果。

## 六、其他操作

- **写入 Mod 默认值**：
  - 选择一个槽位，点击“更多…” → “把所选槽位写入 Mod 默认值”。
  - 检查 `ModRepository\TestCharacter\TestModA\mod.ini` 中的 `global persist $hair = …` 是否改成了槽位里的值，旁边是否有 `.imm-persist-*.bak`。
  - 复制模式下，`Loader\Mods\TestModA\mod.ini` 也应该一起改。
- **整份快照**：
  - 点击“更多…” → “保存整份 d3dx_user.ini 快照”，然后在模拟加载器里改值并按 F10。
  - 再点击“恢复整份 d3dx_user.ini 快照”。加载器运行时可以勾选热重载，否则先关闭模拟加载器。
- **导入 JASM 变体**：在 `ModRepository\TestCharacter\TestModA` 下新建 `.JASM_Variants.json`：

  ```json
  {
    "ActiveVariantName": "短发",
    "Variants": [
      { "Name": "短发", "Preferences": { "$\\mods\\testchar\\testmoda\\mod.ini\\hair": "1", "$\\mods\\testchar\\testmoda\\mod.ini\\coat": "1" } },
      { "Name": "长发", "Preferences": { "$\\mods\\testchar\\testmoda\\mod.ini\\hair": "2" } }
    ]
  }
  ```

  然后点击“更多…” → “导入 JASM 变体”。预期结果：导入 2 个槽位，“短发”标为当前。

## 七、在真实游戏里确认

模拟测试通过后，再用真实的加载器验证：

1. 先备份加载器目录下的 `d3dx_user.ini`，只用带 `global persist` 变量的测试 Mod。
2. 确认加载器的重新加载键是 F10。3DMigoto 的 `d3dx.ini` 默认在 `[Hunting]` 段写有 `reload_config = no_modifiers VK_F10`，请以你实际的文件为准；如果改过这个键，热应用目前仍然只会发送 F10。
3. 终末地等以管理员权限运行的游戏，需要在“快捷键”页点击“以管理员身份重启管理器”，否则发送的按键会被 Windows 丢弃。
4. 在游戏里切换 Mod 状态，按 F10，在管理器里保存两个槽位。
5. 在“快捷键”页选择游戏窗口并开启输入，用浮窗切换槽位，观察游戏里是否立即变化。
6. 退出游戏后重新进入，确认状态保持在最后切换的槽位。

如果切换后游戏没有变化，状态行会说明是 F10 没有送达，还是文件被改写。遇到问题时，请把状态行文字和 `WinUI3\diagnostics` 下的日志一起反馈。
