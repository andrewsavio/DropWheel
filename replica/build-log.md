# Build log

Stack changed from Electron to C# / WPF on .NET 10 (your choice: "Windows native"). App: `app/DropWheel`. Working name DropWheel, not the original's name.

| ID | date | state | missing / notes |
| --- | --- | --- | --- |
| shell | 2026-10-18 | done | tray, single instance, tokens wired as Theme.xaml (generated from tokens.json), SQLite per schema.sql |
| S01 convert wheel | 2026-10-18 | partial | built and rendered (see app/out/wheel.png); not driven by a real Explorer drag yet |
| S02 tools wheel | 2026-10-18 | partial | image, PDF, archive tools; no crop/trim/redact/QR/target size |
| S03 actions wheel | 2026-10-18 | partial | copy path, copy/move, terminal, show in folder, float; no AirDrop |
| S04 keyboard menu | 2026-10-18 | partial | Ctrl+Alt+T, 1/2/3 switch wheel; not driven by hand |
| S05 tray | 2026-10-18 | done | |
| S06 settings | 2026-10-18 | done | modifiers, output folder, start with Windows, license, engine status |
| S07 progress toast | 2026-10-18 | partial | progress for ffmpeg only; others indeterminate; not seen on screen |
| S08 paywall | 2026-10-18 | partial | counter works; no license vendor, so release builds accept no key |
| S09 float tile | 2026-10-18 | partial | built, not seen on screen |

## Harder than expected / decisions
- **PDFsharp 6.5 is AGPL-3.0 and checks a license at runtime.** Unusable in a product you sell. Replaced with PdfPig (Apache-2.0); merge, split and strip-metadata all go through its merger.
- **Selling means checking every bundled engine.** Ghostscript is AGPL (needs a commercial license to ship; kept as a user-installed engine only). ffmpeg (GPL build with x264), pandoc, Poppler are GPL: fine as separate processes but you must ship their notices and offer source. LibreOffice is MPL, 7-Zip LGPL, ImageMagick permissive. Decide before bundling.
- The drag payload cannot be read before the drop, so the wheel learns which file is dragged from Explorer's selection (Shell.Application) and UI Automation, not from the drag itself.
- Output names are reserved on disk before the job runs, so parallel jobs can't collide (found and fixed by the self-test).
- Not verified: the real drag over Explorer and the desktop, Explorer tabs, touch, mixed-DPI placement, admin windows (the hook cannot see them), every ffmpeg/pandoc/LibreOffice/ImageMagick path (those engines are not installed on this machine).
- Self-test: `DropWheel.exe --selftest log.txt [--shot wheel.png]`, 25 checks, all pass. Engines found: 7-Zip only.
