# Sample state (synthetic)

Made-up content for screenshots of pages "with data" (stage J4). Nothing here is real user data, and
nothing here makes the app go online: the addresses use the reserved `example.invalid` domain, no
tracked Mod has a preview image, and the update check stays manual.

`tools\ui-baseline\Capture-UiBaseline.ps1 -SampleData` copies it into the app copy before each picture:

- `tracked-mods.json`: tracked online Mods, written into `config.ini` as `mod_origin=` lines. Paths are
  relative to the copied `TestData` folder.
- `download-queue\tasks.json`: finished, failed and canceled downloads for the download history
  (`backups\download-queue\tasks.json` next to the app). `{TestData}` is replaced with the copied folder.
- `mod-installs\*\transaction.json`: two install backups (`backups\mod-installs`), the same way.
