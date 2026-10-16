# EndfieldBridge optional integration — local verification

Date: 2026-10-15 (America/New_York). Unreleased development change; published manager packages were not replaced.

## Scope

The manager offers a separate download and selects a launch entry per Endfield repository.
No bridge launch implementation was copied, no EFMI configuration was changed, no XXMI
authorization was accepted, and no game or downloaded bridge executable was run in this verification.

## Automated checks

- 49 new bridge integration tests: pinned release metadata; Endfield-only eligibility; one-time offer policy; explicit loader-path preservation; default-path resolution; SHA-256 rejection; HTTP errors; size limits; bounded ZIP extraction; traversal, ADS, reserved Windows names, duplicate entries, links and reparse entries; required licenses/notices/source/risk files; cancellation and existing recovery-data preservation.
- Complete test suite: **808 passed, 0 failed, 0 skipped** — Core 757, Data 24, Updater 27.
- All three coverage gates passed: Core 96.7% lines / 91.9% branches; Data 88.6% / 89.1%; Updater 60.2% / 60.6%.
- WinUI x64 Release build passed. The full validation run reported zero build warnings and errors.
- Existing release-package smoke/contract checks passed. The smoke ZIP is a verification artifact, not a user-facing release package.

Evidence is under `artifacts/bridge-integration-20261015/full-verification` and `VerifiedTests`.

## Actual release package

Checked the public EndfieldBridge v0.3.2 GitHub Release. The portable ZIP is 42,927,295 bytes.
Published and independently computed SHA-256 both match:

```text
008daa343e61e3f4d83085e946a06c8eb189fb445f52edf529ef003cc2d00668
```

Exercised the manager Core's actual `DownloadAsync` implementation against the pinned GitHub URL,
including download, verification and extraction to `VerifiedNetwork`.
Also exercised the installer against the separately downloaded archive in `VerifiedPackage`.
Both recognized a complete installation and preserved license, notices and corresponding-source metadata.
Neither installation was executed; neither created a bridge `data` directory.

## Native isolated UI

Used the computer-use skill against a copied application with an isolated `beta-shell.json`,
blank game/Mod paths, and automatic update checks disabled.

- Upgrade offer appeared; the risk checkbox was unchecked, Download and Use was disabled,
  and the default button did not download.
- Risk text and all three actions were visible in a non-maximized window; longer content is scrollable.
- Choosing the existing loader persisted `UseEndfieldBridge=false` and offer revision 1.
- No guessed launcher path was persisted when the default XXMI locations did not exist.
- Closing and restarting the isolated application did not repeat the offer.
- Inspection identified a clipped bridge button when nested inside the narrow path field.
  The button was moved to its own full-width row below the launcher fields, outside the generic
  path-row reflow, with wrapped text.
- Rechecked the final full-width button in the same short window: the label was fully visible,
  and the button reopened the launch-method/risk dialog successfully.

The final UI-only adjustments were rebuilt after the full automated run.
Core and updater behavior was unchanged by those final layout adjustments.

## Remaining boundaries

No live game, Mod rendering, UAC, EFMI adaptation/recovery or XXMI authorization was exercised.
The graphical consented-download-to-configuration path still needs a user-driven acceptance test;
the real transport/install path was verified separately without executing its result.
Technical checks do not prove account safety or official approval.

Before publishing: choose the manager release version, package the actual runtime, and perform
an upgrade acceptance test with existing settings. Publication was not requested in this change.
