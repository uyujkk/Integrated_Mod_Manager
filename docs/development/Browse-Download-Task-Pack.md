# Browse, download and existing-library task pack

Base: the isolated `feature/browse-feedback` checkout, continuing the first feedback implementation.

## Delivery groups

- [x] A. Search counts and removable filters; server-paginated author browsing using submitter IDs; saved authors and read-later Mods.
- [x] B. Duplicate-install guidance that distinguishes file identity from entry identity; task-center retry; bounded concurrent execution; remembered confirmed destinations.
- [x] C. Existing-library import with an explicit preview, non-overwriting copies and a persisted, integrity-checked undo record. Sources stay in place.
- [x] D. Core regression tests, Release/x64 build, isolated native-window checks and a bounded live API/download check. See the remaining native stability observation below.

## Boundaries and acceptance

Use the existing runtime and policies rather than adding a second download manager. Capture repository and confirmed destination before queued work. Queued cancellation must not create an installation; retries must retain the original repository and selected file. Metadata from the website does not authorize executable launches.

Author pagination is a server query, scoped to the repository's configured category and optional character category. Current-page text search remains local. No automatic full-catalog crawl or author notification polling is added.

Existing-library organization copies complete immediate Mod directories into a chosen character folder after showing a plan. Conflicts and filesystem links are rejected; execution revalidates the preview. Undo is limited to output owned by that import and requires an unchanged content snapshot. Modified imports remain intact and are reported. Real user libraries are never used for development verification.

No release/push, updater change, replacement of an existing installation, or distribution ZIP is part of this task pack.

## User entry points

- Online list: type a title/character/author; use `author:name` for a local author-only query. The summary reports current-page matches. Clear filters resets text, author and character conditions.
- Author link: browse that submitter across server pages within the configured category. Old caches resolve `userid` from the existing Mod metadata endpoint; a failed lookup falls back to local search with a notice.
- Card right-click: save the author or save the Mod for later. The star beside the filter summary opens saved items. Authors are grouped by category, Mods by repository. Lists are capped at 100 each and saved in shell configuration.
- Gear beside the filter summary: choose 1–4 concurrent tasks (default 2) and opt into remembering confirmed folders per repository/character. Invalid remembered paths prompt again.
- Task center: queued/running work can be canceled; failed/canceled work can be retried using its original repository, destination and selected file. A removed selected file is not silently replaced. Task failures show a notice and details in the task center instead of blocking browsing with a modal.
- Repository view: the organized-import button beside the view controls offers preview/copy and undo. Select/paste the old-library root and select one target character folder. All candidates are copied as separate folders, preserving their internal structure. A name conflict blocks execution, so existing folders are not overwritten.

## Verification, 2026-10-18

- Core: **803 passed, 0 failed, 0 skipped** in `artifacts/verification/task-pack/core-tests.trx`. This is 27 additional cases beyond the previous 776-case checkpoint. Test temporary data was directed to the G: development checkout.
- Release/x64 build: successful. NU1900 advisory-feed warnings remained; no release packaging or updater changes were made.
- The official [Mod filter configuration](https://gamebanana.com/apiv11/Mod/ListFilterConfig) exposes `Generic_Submitter` and `Generic_Category`. Live queries combining category 42770 and submitter 5878401 returned the expected submitter. The legacy `userid` field was verified against the same author; `Owner().id` was rejected by the endpoint and is not used.
- Native author checks: mock Alice Works results changed from page 1/2 to page 2/2 while retaining author scope. Saving that author and a read-later Mod appeared in the saved-items dialog and in persisted shell JSON.
- Live native download: manually selected `modini.zip` (file ID 1851735, Mod 720357, 2,865 bytes) into the isolated mock repository. The app downloaded/extracted it and saved file identity. Output included `mod.ini` (20,959 bytes) and the normal downloaded preview. No game, launcher, downloaded script or binary was executed. Selecting a different file from that entry correctly displayed the other-version/variant duplicate warning, which was canceled.
- Native task-center retry: a synthetic failed entry generated a new attempt from the Retry button without another folder prompt. The original confirmed folder was retained. The opt-in remembered-folder setting and its repository/character path were verified in persisted shell JSON.
- Import Core tests cover preview-only behavior, nested/empty folders, source changes, destination collisions, cancellation before and after publication, repeat undo, changed-file/extra-file/extra-folder preservation and a traversal/wrong-root record. No real user library was used.
- Native import check on the final build: pasted the generated old-library path, previewed Old Mod A/B (132 bytes), then copied both into Fixture Character. The repository showed both Mods, all four original files remained and the manifest tracked exactly the two copied folders. Native undo entry is present; deletion semantics were verified by the Core fixtures rather than deleting through the desktop UI.

## Remaining verification limits

One native details-page attempt exited with Application Error 1000: `Microsoft.UI.Xaml.dll`, exception `0xc000027b` (WER report `e63dcbbd-de17-4f34-a587-001fb63d54e7`). No managed unhandled exception was logged. After restarting, the same details page, file picker, live download and wide-window details layout worked. An earlier image-cache access error was also logged, but causation has not been established. Do not describe this as a fixed or diagnosed crash, or promote this development build to a stable release on the strength of these checks.

Follow-up investigation: [native crash investigation](Online-Native-Crash-Investigation.md). The accompanying same-image cache publication race was reproduced with a File.Move stack and fixed by coordinating cache resolution through decoding. Two fresh native samples had no cache errors after that change; 806 Core tests and the Release/x64 build passed. The original native crash still lacks a stowed backtrace and remains unresolved. Opt-in diagnostic tracing and a bounded local ProcDump capture script are included for the next occurrence.

Queue limits cover the download/install execution phase, not the metadata request and user confirmation preceding task creation. Tasks are in-memory and do not resume after restarting. Remembered folders are opt-in. Existing-library import does not infer character assignments or move originals. Undo checks file hashes and directory contents and rejects links, but does not provide a cross-process filesystem lock or a crash-recovery transaction; a process crash or manifest-write failure at publication can leave an untracked copied folder. Sources remain intact.
