# Licensing / 许可证说明

Integrated Mod Manager uses two licenses. This file explains which applies where.
集成化 Mod 管理器使用两种许可证，本文件说明各自的适用范围。

| Part / 部分 | License / 许可证 |
| --- | --- |
| `src/IntegratedModManager.EndfieldLaunch/` and `tests/EndfieldLaunch.Tests/` — the integrated Endfield launch, derived from EndfieldBridge 0.3.2 / 内置终末地启动组件，源自 EndfieldBridge 0.3.2 | **AGPL-3.0-only** ([LICENSE](src/IntegratedModManager.EndfieldLaunch/LICENSE), [NOTICE](src/IntegratedModManager.EndfieldLaunch/NOTICE.md)) |
| Every other file in this repository without a different notice / 仓库中其他没有另行声明的文件 | **MIT** ([LICENSE](LICENSE)) |
| **Release packages and any build that includes the Endfield launch component** / **发布包及任何包含终末地启动组件的构建** | **AGPL-3.0-only, as a whole / 整体按 AGPL-3.0-only 分发** |

## What this means / 含义

- The application links the AGPL component, so the program you download is a combined
  work distributed under AGPL-3.0-only. When you redistribute it, modified or not, provide
  the corresponding source as the AGPL requires. The source of every release is the tagged
  commit of this repository.
- MIT-licensed files keep their MIT license on their own. You may reuse them separately
  under the MIT terms, as long as you keep the MIT notice.
- Releases up to and including v4.2.0 did not contain the component and remain available
  under MIT.
- The account-risk confirmation shown in the application is not a license restriction.

- 管理器链接了 AGPL 组件，因此下载到的程序是按 AGPL-3.0-only 分发的组合作品。再分发（无论是否修改）时，请依 AGPL 提供对应源码；每个版本的源码就是本仓库对应标签的提交。
- MIT 文件单独使用时仍是 MIT，只要保留 MIT 声明，就可以单独按 MIT 复用。
- v4.2.0 及之前的版本不含该组件，仍按 MIT 提供。
- 应用内的账号风险确认不是许可证限制。

## Third-party components / 第三方组件

The release package includes the Windows App SDK (MIT), Microsoft.Data.Sqlite (MIT) and,
when present, 7-Zip with its own license text. Bandizip and XXMI are run as separate
programs and are not distributed with the manager. The upstream projects credited by
the Endfield launch component are listed in its [NOTICE](src/IntegratedModManager.EndfieldLaunch/NOTICE.md).

发布包包含 Windows App SDK（MIT）、Microsoft.Data.Sqlite（MIT），以及附带自身许可证的 7-Zip（如有）。Bandizip 和 XXMI 作为独立程序调用，不随管理器分发。终末地启动组件致谢的上游项目见其 [NOTICE](src/IntegratedModManager.EndfieldLaunch/NOTICE.md)。
