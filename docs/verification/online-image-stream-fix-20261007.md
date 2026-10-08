# Online image cache rendering repair / 在线图片缓存显示修复

Date: 2026-10-07. Local unreleased v4.0.0 development checkpoint. No version bump, push, release, auto-update change or replacement of the user's running app.

## Observed failure

The user's opt9 window showed empty online Mod covers and fallback icons instead of character avatars. Read-only inspection found 131 downloaded cache images (25,485,848 bytes); a sample JPEG opened successfully and the official character PNG cache also existed. Cache file paths were 273 characters long. Therefore an empty download directory was not the cause of the observed failure.

The UI assigned `new BitmapImage(cachedFileUri)` and treated that assignment as success. The URI constructor starts asynchronous loading; its later failure is not caught by a `try` around the constructor. All affected online views used that pattern, whereas the local preview already used explicit stream decoding. Microsoft's [BitmapImage documentation](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.media.imaging.bitmapimage?view=windows-app-sdk-1.7) describes this distinction. The precise native URI-loader error was not captured by the original code; the length correlation alone does not prove that every file URI fails.

## Repair

- A common online-image loader opens cached files through .NET `FileStream` and awaits `BitmapImage.SetSourceAsync` before assigning a successful image source.
- Covers/list rows, character avatars, detail thumbnails/hero and full-size viewer use this loader. Avatar cancellation/generation and detail/viewer generation checks remain in place.
- Unreadable cache files get one bounded refresh attempt. Empty/oversized cache entries are replaced only after a download succeeds; no manual cache purge is required.
- Image downloads have a 15-second request budget. Cache, decoder and HTTP failures are recorded in the existing sanitized diagnostic log. Failed downloads no longer fall back silently to a second WinUI network loader.
- Local repository image selection and real Mod files are not modified by this fix.

## Validation

- 12 new production-adapter tests cover valid cache decoding, non-file/missing sources, corrupt-cache retry, repeated decode/provider failures, failed replacement, cancellation at all stages and long Unicode path handoff.
- Full suite: 720 passed, no failures/skips (Core 669, DataStore 24, UpdateAgent 27). Coverage gates passed; WinUI x64 Release build had zero warnings/errors; minimal package contract passed.
- Native QA copied only online cache data into a separate runtime with empty source/target folders and no launcher. Cached image paths were 274 characters long and contained Chinese characters. Original user cache/configuration were not changed.
- Actual visible list and cover-grid images and character avatars rendered from that cache copy. No manual image redownload was needed to repair those views.
- The online detail hero, its thumbnail strip and the full-size image viewer were also visibly verified in the independent native window. The full-size viewer displayed the selected image and its 1 / 12 position successfully.
- A deliberately longer runtime path also hit a .NET Framework launcher CLR 8007007a error before the app opened. That unsupported executable-path experiment was abandoned; it is not an image decoder failure. The successful QA runtime kept executable paths shorter while image cache paths exceeded 260 characters.

Clean runtime package: `artifacts/online-image-stream-fix-20261007/ReleasePackage/Integrated_Mod_Manager-v4.0.0.zip`.

SHA-256: `D00014A1598A5D493400A866363F05AA46A280761FFCB162782450A0ABE4C4E5`.

No gameplay/loader validation is implied. A CDN outage, missing server image or unsupported codec can still fail; failures should now be diagnosable rather than assumed successful.
