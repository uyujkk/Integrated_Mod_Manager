# Testing / 测试说明

[English README](../../README.md) · [中文首页](../README.zh-CN.md) · [4.1.1 verification / 验证记录](../verification/v4.1.1.md)

## One entry point / 统一入口

Requires Windows, .NET SDK 8 and Visual Studio 2022/Build Tools with Windows application build tools. Local development and CI use the same command.
需要 Windows、.NET SDK 8 和 Windows 应用构建工具。本地与 CI 使用同一入口。

```powershell
./scripts/test-all.ps1 -ArtifactsDirectory artifacts/my-verification
./scripts/test-all.ps1 -SkipBuild -ArtifactsDirectory artifacts/my-tests
```

Use a new repository-local output directory: the script clears that directory. Never use user-data or running-app directories.
建议每次使用新的仓库内输出目录，脚本会清理该目录；不能指向用户数据或运行中的测试应用。

CI runs on every branch push (documentation-only pushes are skipped) and on pull requests to `main`.
CI 在每个分支推送时运行（只改文档的推送除外），指向 `main` 的 PR 也会运行。

## GitHub Actions

| Workflow | When / 何时运行 | What / 做什么 |
| --- | --- | --- |
| `build-and-test.yml` | Every push and PR / 每次推送和 PR | Code style, `test-all`, UI smoke tests, a table of UI text by language in the job summary, the verified package as an artifact / 代码风格、`test-all`、界面冒烟测试、作业摘要里的各语言文案统计，并上传已验证的包 |
| `release.yml` | A pushed tag `vX.Y.Z`; a PR that changes the release scripts / 推送 `vX.Y.Z` 标签；修改发布脚本的 PR | Tests, `package-stable-release.ps1` with this run as the CI link, upgrade and rollback from the previous published release, UI smoke tests, a build provenance attestation, then a **draft** release with the notes from `docs/releases/vX.Y.Z.md`, re-downloaded and hash-checked. A PR runs the same steps for the current version without the attestation or a release. / 测试、以本次运行为 CI 链接打包、从上一个已发布版本升级并回退、界面冒烟测试、构建来源证明，然后用 `docs/releases/vX.Y.Z.md` 创建**草稿** Release，重新下载核对哈希。PR 只演练同样的步骤，不生成证明也不建 Release。 |
| `online-sources.yml` | Daily and on demand / 每天一次，也可手动 | `tests/Online.Tests` asks the miHoYo wiki, HoYoWiki, the Endfield site and GameBanana what the app asks, with the Core clients. A failure means a site changed or is down. / 用 Core 客户端请求米游社 Wiki、HoYoWiki、终末地官网和 GameBanana；失败说明网站变了或暂时不可用。 |
| `codeql.yml` | PRs, `main`, weekly / PR、`main`、每周 | CodeQL security analysis of the C# sources without a build; results under Security → Code scanning. / 不构建直接分析 C# 源码，结果在 Security → Code scanning。 |
| `dependabot.yml` | Weekly / 每周 | Grouped pull requests for minor and patch NuGet updates and for GitHub Actions; major NuGet updates are left for a deliberate upgrade. / 合并的 NuGet 小版本更新与 Actions 更新 PR；NuGet 大版本升级另行安排。 |

To release: merge the version PR, then push the tag. Review and publish the draft the workflow creates; code signing is not set up yet.
发布方式：合并版本 PR 后推送标签，检查工作流生成的草稿后手动发布。代码签名尚未接入。

```powershell
git tag v4.7.3
git push origin v4.7.3
```

## Code style / 代码风格

```powershell
./scripts/check-code-style.ps1
```

Builds every project with the `.editorconfig` rules enabled (for example IDE0005, unnecessary `using`) and fails on any finding. Shared compiler settings live in `Directory.Build.props`. CI runs this before `test-all`; locally, run it before `test-all` too, because its rebuild cleans the shared intermediate files.
按 `.editorconfig` 规则（例如 IDE0005 多余的 `using`）编译所有项目，有任何问题即失败。公共编译设置在 `Directory.Build.props`。CI 在 `test-all` 之前运行它；本地也请先运行它再运行 `test-all`，因为它的重新生成会清理共享的中间文件。

## UI automation / 界面自动化测试

```powershell
./scripts/test-all.ps1
./scripts/test-ui.ps1
./scripts/test-ui.ps1 -Filter "FullyQualifiedName~Overlay"
```

`tests/UI.Tests` drives the built application through Windows UI Automation (FlaUI). Each test copies the runtime that `test-all` staged in `artifacts/verification/ReleaseSource` (or `-Application <folder>`) and the synthetic `tests/TestData` into a temporary folder, starts it, and checks windows, the saved configuration and the error logs. Tests run one at a time and press a global hotkey, so use an unlocked desktop and leave the mouse and keyboard alone. They are not part of `test-all`; CI runs them after `test-all`. There are 19 tests: first install, every page, the repository views and batch dialog, both settings layouts, the online layout buttons, the updates check interval, the Shortcuts page and overlay click-through. Controls built in code carry an `AutomationProperties.AutomationId`; give new ones an id too, so tests do not depend on the display language.
`tests/UI.Tests` 通过 Windows UI Automation（FlaUI）操作已构建的程序。每个测试把 `test-all` 在 `artifacts/verification/ReleaseSource` 生成的运行文件（或 `-Application <文件夹>`）和合成数据 `tests/TestData` 复制到临时目录后启动，检查窗口、保存的配置和错误日志。测试逐个运行并会按全局快捷键，请在解锁的桌面上运行，期间不要操作鼠标键盘。它不属于 `test-all`，CI 会在 `test-all` 之后运行。共 19 个测试：首次安装、各页面、仓库视图和批量对话框、设置页两种布局、在线页布局按钮、更新页检查间隔、快捷键页和浮窗鼠标穿透。代码里创建的控件设有 `AutomationProperties.AutomationId`；新增控件也请设置，测试就不依赖界面语言。

### Online samples: `IMM_ONLINE_FIXTURES` / 在线样例：`IMM_ONLINE_FIXTURES`

For screenshots and UI tests only. When the environment variable `IMM_ONLINE_FIXTURES` names a folder, the app answers every online request from the synthetic samples listed in that folder's `fixtures.json` (longest matching URL prefix) and sends nothing to the network; a request without a sample fails and is logged to `app-errors.log` as "Online test fixture mode (测试样例模式)". The folder is only read. Without the variable the app uses its normal HTTP client, with the same requests, frequency and caching as before (`OnlineHttpClientFactory`, unit tested). The samples in `tests/TestData/OnlineFixtures` are made up — names, numbers and placeholder images — and are regenerated with `python tools/online-fixtures/make_fixtures.py tests/TestData/OnlineFixtures`. Never point the variable at real data or use it outside tests.

只供截图和界面测试使用。环境变量 `IMM_ONLINE_FIXTURES` 指向一个文件夹时，程序的所有在线请求都由该文件夹 `fixtures.json` 里列出的合成样例应答（按 URL 前缀最长匹配），一个请求也不会发到网上；样例里没有的请求一律失败，并在 `app-errors.log` 记为“Online test fixture mode（测试样例模式）”。文件夹只读，不写入也不删除。不设置这个变量时，程序用原来的 HTTP 客户端，请求内容、频率和缓存规则都不变（`OnlineHttpClientFactory`，有单元测试）。`tests/TestData/OnlineFixtures` 里的样例全部是编造的名称、数字和生成的占位图，用 `python tools/online-fixtures/make_fixtures.py tests/TestData/OnlineFixtures` 重新生成。不要指向真实数据，也不要在测试以外使用。

## Four suites / 四组测试

| Project | Scope / 范围 |
| --- | --- |
| tests/Core.Tests | Paths, presets/bundles, archive safety, scanning, downloads, metadata, adapters, layouts / 路径、预设与组合包、解压安全、扫描、下载、元数据、适配器及布局 |
| tests/DataStore.Tests | Actual isolated SQLite cache/index transactions / 真实隔离 SQLite 缓存与索引事务 |
| tests/UpdateAgent.Tests | Checksums, downloads, extraction, configuration preservation and rollback / 校验、下载、解压、配置保留与回滚 |
| tests/EndfieldLaunch.Tests | Isolated launch, configuration and lifecycle tests / 隔离启动、配置和生命周期测试 |

Shared references live in `tests/TestDependencies.props`. Each suite remains independent; safety cases are retained. Developer-only fixtures/harnesses are excluded from release payloads.
依赖统一放在 `tests/TestDependencies.props`，保留四组独立测试和安全用例。开发测试数据及基准工具不随主程序发布。

| Assembly | Line minimum | Branch minimum |
| --- | ---: | ---: |
| Core | 90% | 85% |
| Data | 70% | 60% |
| UpdateAgent.Core | 60% | 55% |

Coverage selects each named assembly, not aggregate coverage. Four script self-checks cover missing/duplicate assemblies and misleading aggregates. Full verification also builds WinUI x64 and all four EXEs, checks versions, and validates the minimal ZIP. TRX, coverage and build/package outputs go under the selected directory.
覆盖率按程序集计算，四项脚本自测验证缺失、重复及汇总值干扰。完整验证另包含 WinUI x64、四个 EXE、版本一致性和精简 ZIP；结果位于所选输出目录。

## Release and native checks / 发布与窗口检查

Package only clean runtime files and user guides with `scripts/package-release.ps1`. Which files count as user state is defined once in `scripts/release-file-policy.ps1`; `test-all` uses the same rule when it stages `ReleaseSource`, and starts from an empty folder each time. Check the managed manifest, exact ZIP/SHA-256 sidecar pairing, versions and exclusion of settings, caches, test Mods and logs. Keep Developer Tools separate. Verify public metadata and downloaded hashes after publication.
只从干净运行目录打包，核对受管清单、精确同名校验附件、EXE 版本及用户数据排除；开发者工具独立。哪些文件属于用户数据统一定义在 `scripts/release-file-policy.ps1`，`test-all` 准备 `ReleaseSource` 时也用同一规则，并且每次都从空目录开始。发布后核对公开下载与哈希。

Native windows, live websites, DPI, real games and actual user-installation upgrades are distinct checks, not proved by unit tests. Use isolated data and the [regression checklist](./Regression-Checklist.md). Historical journals remain in verification, including [opt11](../verification/combined-regression-opt11-20261007.md); this guide no longer repeats their running totals.
实际窗口、网站、DPI、真实游戏和用户安装目录升级分别验证，不能用单测代替。使用隔离数据和回归清单。阶段日志保留在验证目录，本页不再重复流水账。

## Shortcut performance / 快捷键性能

`tests/Performance/ShortcutUiBenchmark.cs` is compiled into a separate WinUI test build only with `ImmShortcutBenchmark=true`. It generates 100/200/300-binding fixtures, measures INI scanning, button creation, actual layout, overlay opening and status updates, and checks deployment replacement and stale-action removal. `LoaderFlushReceiver.cs` is a standalone synthetic F10 target compiled with the .NET Framework compiler and `IMM_FLUSH_RECEIVER`. Normal release builds include neither test entry point. Record foreground/input failures separately from the synthetic flush/capture contract; this never proves real-game input.

快捷键基准只在专用构建中运行；正常包不包含测试入口。Windows 拒绝前台切换时应记录真实按键验证未完成，不能把模拟写文件当成游戏响应。`scripts/test-preview-upgrade.ps1` 可用旧公开包的更新器，在新隔离目录验证升级、旧业务字段保留和副本回退。
