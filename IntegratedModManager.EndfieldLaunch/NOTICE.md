# Copyright, provenance and modifications / 版权、来源与修改说明

`IntegratedModManager.EndfieldLaunch` — the Endfield launch component of
Integrated Mod Manager. Modified on 2026-10-21.

This directory is licensed under the **GNU Affero General Public License version 3
only (AGPL-3.0-only)**; see `LICENSE` in this directory. Because Integrated Mod
Manager links this component into its application, **distributed builds of
Integrated Mod Manager that include it are conveyed as a whole under
AGPL-3.0-only**. Files outside this directory that carry no other notice remain
available under the MIT License in the repository root. The program is provided
WITHOUT ANY WARRANTY. You may copy, modify and redistribute it under the license.
No account-risk acknowledgment shown by the application restricts those rights.

Copyright (c) 2026 EndfieldBridge contributors and Integrated Mod Manager
contributors. This notice claims only the contributors' own additions. All upstream
rights remain with their respective authors and contributors.

## Origin: EndfieldBridge 0.3.2

This component is derived from **uyujkk/EndfieldBridge** 0.3.2 (AGPL-3.0-only):
https://github.com/uyujkk/EndfieldBridge/tree/c3608fe

### Launch-core provenance (inherited from EndfieldBridge)

- **BlockyDeer/endfield-nolauncher**, by BlockyDeer and contributors, AGPL-3.0:
  https://github.com/BlockyDeer/endfield-nolauncher/tree/fcda770937cc58711801c2c3a58d93ff2d4b16a8
  Reviewed files: `src/launch.cpp`, `src/launch.h`, `src/main.cpp`,
  `tests/options_tests.cpp`.
- **Dr-hydra/Better-Endfield**, by Dr-hydra and contributors, AGPL-3.0:
  https://github.com/Dr-hydra/Better-Endfield/tree/514f6d631088a9d4d52eb68a40914674c52ff79b
  Upstream file: `native/loaders/shared/official_launcher.cpp`.

The launch approach and the structure of the argument-quoting helper were adapted
after reading the intermediate source. This is not a clean-room implementation.

### Interoperability reference

**SpectrumQT/XXMI-Launcher** (GPL-3.0) public configuration and signature
validation code was reviewed to interoperate with its Custom Launch fields and
ECDSA/SHA-256 command signatures. No XXMI source, binaries, private keys,
injectors, game files or Mods are included. Users obtain those separately.

## Modifications on 2026-10-21

- Moved from a standalone Windows Forms executable into a class library that the
  Integrated Mod Manager WinUI application calls in-process or through its own
  executable with `--endfield-launch` / `--endfield-game-only`. The separate
  download, the `StartFromModManager.cmd` entry and the Windows Forms UI are
  removed; configuration now happens in the manager.
- Targets the manager's .NET 8 runtime instead of a self-contained .NET 9
  single-file package.
- The official launcher's signature is verified once per launch, while its file
  handle is held, instead of once during discovery and again before creation.
  The last verified `Games.exe` is cached per game path and tried first.
- Bilingual messages are carried as Chinese/English pairs instead of a
  post-translation table.
- XXMI custom-launch commands written by EndfieldBridge 0.3.x are recognised so the
  original restore point is kept when migrating to the integrated command.
- Profiles, logs, restore metadata and launch status are stored in the manager's
  `backups\endfield-launch` folder.

## Corresponding source

The complete corresponding source of each Integrated Mod Manager release,
including this component and its build scripts, is available from the tagged
commit and the source archive on the release page:
https://github.com/uyujkk/Integrated_Mod_Manager/releases

## 中文说明

本目录以 AGPL-3.0-only 授权，源自 EndfieldBridge 0.3.2；其启动核心参考并改写自
endfield-nolauncher / Better-Endfield（均为 AGPL-3.0），不属于 clean-room 实现。
管理器链接了本组件，因此包含本组件的管理器发布包整体按 AGPL-3.0-only 分发；仓库中
本目录以外、没有其他声明的文件仍可按根目录的 MIT 许可证使用。

2026-10-21 的修改：并入管理器、改用 .NET 8、移除独立下载与 Windows Forms 界面、签名
只在持有文件句柄时校验一次并缓存已验证的 Games.exe、识别旧版桥接写入的 XXMI 命令以
保留原始恢复点、数据改存于管理器的 backups\endfield-launch。

程序无担保，可依许可证复制、修改与再分发。风险确认不是附加许可限制，也不代表游戏
官方授权。
