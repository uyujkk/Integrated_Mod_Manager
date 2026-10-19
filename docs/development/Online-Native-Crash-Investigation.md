# 在线详情页原生崩溃调查

调查日期：2026-10-18。所有开发、样本和输出位于 G: 的 `feature/browse-feedback` 隔离工作区。

## 当前结论

原生闪退是真实记录，根因尚未确认。已定位并修正同时出现的同图缓存竞争，但不能将这一修正称为原生崩溃已修复。

## 原始证据

- 14:01:09 EDT，Application Error 1000 / RecordId 108221：实验版 `20261018-135911-b4e752`，`Microsoft.UI.Xaml.dll` 3.1.7.0，异常 `0xc000027b`，偏移 `0x39dd55`。报告 ID：`e63dcbbd-de17-4f34-a587-001fb63d54e7`。
- 对应 WER Report.wer 的内部签名：`StackHash12_ec6` / `8000ffff`。保留了报告，未找到对应原始转储。`0xc000027b` 是 WinRT stowed exception；原始错误和回溯应从转储中的 stowed 信息提取，不能单靠故障模块名称认定 XAML 自身有缺陷。
- 14:01:07.874，应用记录 `Online image cache: UnauthorizedAccessException (0x80070005)`。原日志没有堆栈，时间相近不能证明其导致闪退。
- 从本聊天的操作记录还原顺序：最大化 → 作者 Alice Works → 第二页 → 收藏作者与稍后下载 → 打开并关闭收藏面板 → 清空筛选 → 打开条目 720357 详情。崩溃发生在最后一步之后。

原始事件和 WER 报告保存在 `artifacts/verification/crash-investigation/application-events.json`、`original-Report.wer`。

## 隔离复现

| 样本 | 覆盖路径 | 观察结果 |
| --- | --- | --- |
| 原始 135911 样本重启 | 已有缓存、宽窗口详情、列表/方块切换、最大化 | 详情和图片正常；未捕获同类崩溃；正常退出码 0 |
| 142304 冷缓存样本 | 最大化、作者筛选、首次详情、清空筛选、恢复窄窗口、收藏弹窗后重开详情 | 首次主图加载失败，重开后正常；同类缓存访问错误；无原生闪退，正常退出码 0 |
| 142833 诊断样本 | 完整重走原始操作顺序；启用 High 布局循环追踪和 ProcDump | 复现缓存错误并取得 File.Move 堆栈；主图成功而一张缩略图空白；未捕获原生崩溃，正常退出码 0 |

第三轮详细日志：`artifacts/verification/crash-investigation/capture-20261018-142842`；界面阶段日志位于该样本 App/ui-diagnostics.log。原生闪退在修正前也未重现，因此后续未闪退不能用作修复原生崩溃的因果证据。

## 已定位的图片缓存问题

新追踪取得的堆栈：

```text
System.UnauthorizedAccessException: Access to the path is denied.
  at System.IO.FileSystem.MoveFile(..., Boolean overwrite)
  at MainWindow.GetCachedOnlineImageUriAsync(...)
```

详情主图和第一张缩略图会同时请求同一个 URL。旧流程在各自发现缓存缺失后都启动下载，并用 `File.Move(..., overwrite: true)` 发布到同一路径；其中一个调用可能在另一条加载流程已经开始读图时覆盖文件。这是代码和冷缓存复现支持的缓存竞争机制；具体阻止覆盖的文件句柄持有者尚未通过句柄级跟踪确认。它可以独立出现而不引发闪退。

修正为每个窗口按 URL 协调加载，同图的缓存解析、必要刷新和完整解码共用一把异步锁，不同 URL 仍可并行。等待取消不启动下载；失败与取消均释放锁；无使用者时移除锁条目，避免长期浏览积累。没有吞掉原生异常或设置 `UnhandledException.Handled = true`。

## 后续捕获方式

`scripts/start-ui-crash-capture.ps1` 只允许启动当前 checkout artifacts 下的实验程序，要求 ProcDump 的有效微软签名，记录 exe/dll SHA-256，默认最多捕获 600 秒。转储和日志留在本地，不上传、不安装系统级崩溃处理器。程序需从已完成构建生成新的隔离样本。

启用 `IMM_UI_DIAGNOSTICS=1` 后：

- XAML `LayoutCycleTracingLevel=High`，在出现布局循环迹象时提供详细 measure/arrange 信息。
- ui-diagnostics.log 记录详情打开、发布、图片赋值以及布局尺寸和线程；日志约 2 MiB 轮转一次。
- ProcDump 捕获 `C000027B` 或布局循环调试信息；拿到转储后检查原始 HRESULT、stowed backtrace 和涉及的控件，分别验证布局、图像和其他 WinRT 路径。

此机制用于定位，不代表已经证明存在布局循环。默认启动时诊断关闭。主线源码与既有发布版未修改。

参考：[微软 stowed exception 调试说明](https://learn.microsoft.com/en-us/shows/inside/c000027b)、[XAML 布局循环追踪](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.debugsettings.layoutcycletracinglevel?view=windows-app-sdk-1.7)、[ProcDump](https://learn.microsoft.com/en-us/sysinternals/downloads/procdump)。

## 验证

- 图片加载相关测试：15 通过，包括新增的同图互斥/异图并行、取消等待和异常释放三项。
- 完整 Core 回归：806 通过，0 失败，0 跳过。结果：`artifacts/verification/crash-investigation/core-tests.trx`。
- Release/x64 最终构建成功；仍有 NuGet 漏洞信息源不可访问的 NU1900 警告。
- 修正前两份独立冷缓存样本均记录一次缓存覆盖错误；修正后 143513 和 143615 两份新样本均未记录缓存错误，主图与可见缩略图正常显示。计数和二进制 SHA-256 保存在 `artifacts/verification/crash-investigation/native-results.json`。这是图片缓存修正的有限原生验收，样本量为两轮。
- 143522 捕获规则曾误匹配启动文字 `LayoutCycleTracingLevel=High`，生成一份正常启动转储；同目录 `NOT-A-CRASH.txt` 已标明，不能作为崩溃证据。最终规则改为匹配原生 `LayoutCycleCountdown`。143629 捕获运行没有启动误触发、布局循环输出或原生崩溃转储；检查完成后正常关闭测试应用。
- 本轮没有获得原始闪退的 stowed 回溯，故该闪退继续保持“未确认根因”。不能以此次测试中未出现闪退作为稳定发布依据。
