# Online browsing feedback implementation

This isolated development checkout starts from local v4.1.1 commit `7d0ed62`.

## Behavior

- Download preparation, progress and completion refresh existing cards in place. Refreshing an unchanged result set no longer clears/repopulates the shared collection behind the virtualized list and grid.
- Incremental reconciliation preserves surviving object instances and emits no collection notifications for an unchanged sequence. Actual filtering, sorting and new metadata still update the results.
- Search matches title, character, author and existing Endfield character aliases, ignoring case. `author:name` restricts matching to the author field. Clicking a card author fills that query. Clear the search box to show the page again.
- Search is explicitly scoped to the loaded page and current character selection. It does not query the complete GameBanana author catalog.
- Loaded cards show download phase/result, disable duplicate actions, and update installed badges without rebuilding the list. Byte-progress updates reuse installed-state information instead of probing tracked directories repeatedly. Failed downloads expose the existing download action for retry; failure details remain in the tooltip/task center.
- List rows reserve badge space so installation does not change their height. Both layouts keep status text to one line.
- Theme/language/density changes rebuild realized card content independently of the collection. Recycled cards create their content using the current presentation settings.

## Verification

The Core test project links the exact production `OnlineBrowseStateAdapter.cs`. Regression cases cover an unchanged 50-card sequence throughout download states (zero collection events), sorting/filtering without Reset, replaced metadata instances, empty results, author-only matching, punctuation, Unicode, missing metadata and character aliases.

Run `dotnet test Tests/IntegratedModManager.Core.Tests.csproj --configuration Release` and build `WinUI3/ModFolderCopier.WinUI.csproj` with Visual Studio MSBuild, Release/x64. The test assertions verify the collection/search logic; actual scrolling remains a native-window check.

After building, `scripts/new-online-browse-fixture.ps1` creates a fresh runnable directory under `artifacts/browse-feedback-preview/`, with 40 synthetic cached entries, three fictional authors, and an empty mock repository. It copies no user configuration. The fixture's links use `example.invalid` and must never be treated as real downloadable Mods.

Manual checks:

1. Scroll down in list view, start a synthetic entry's download, dismiss the expected metadata error, and verify the same cards remain visible. Repeat in grid view.
2. Search `Alice`, `author:Alice Works`, and `画师甲`; click an author link; clear the search and verify the current page returns. An author click must not open the detail panel.
3. Open and close details, switch themes/languages, scroll to force container recycling, then confirm card labels and author actions remain correct.
4. Separately, in an isolated repository, exercise real download/extraction completion, failure/retry/cancel, concurrent tasks and switching repository during a task. Synthetic metadata errors and collection tests do not prove real network/archive behavior.

No release version, updater endpoint or existing runtime directory is changed. No archive is generated.

## Local results, 2026-10-18

- Core tests: **776 passed, 0 failed, 0 skipped**, including **19** new browsing regressions. TRX: `artifacts/verification/browse-feedback/core-tests.trx`.
- WinUI Release/x64 build succeeded. NuGet emitted NU1900 because the advisory feed could not be reached; package restoration and compilation succeeded.
- The first sandboxed test attempt aborted before assertions because VSTest's loopback socket was denied (10013). The complete successful run used the approved local execution route. The diagnostic files remain in the verification directory.
- Native Windows checks used the computer-use plugin and generated mock data: list view stayed at entry 12 after starting and failing a synthetic task; card status changed from preparing to failed/retry; clicking Alice Works filled `author:Alice Works`, showed only that author's entries and did not open details; Chinese author search also filtered correctly, and clearing it returned the page.
- Grid layout and recycled cards were inspected with the same fixture. After scrolling to the row containing entry 22, starting and failing that synthetic task preserved the same visible rows and changed its status to failed/retry. Native checks use deliberately invalid item ID 0, producing a metadata-validation failure before any real archive download. They do not validate live GameBanana transport, extraction completion or game behavior.
