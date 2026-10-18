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

## PRD gap pass (2026-10-18)
| item | status | note |
| --- | --- | --- |
| PDF redaction | done (self-test) | Windows.Data.Pdf draws pages, PdfPig finds words, black boxes painted into pixels, pages rewritten as JPEG so text is truly gone. Needed the TFM change to net10.0-windows10.0.19041.0. Refuses when nothing matches. Scanned PDFs have no text to search. |
| parameter prompt | partial | `Op.Prompt` + `InputDialog`; redact only |
| engines (ffmpeg, ImageMagick) | not done | needs downloads/installs on this machine; asked the user instead |
Self-test: 42 checks pass (2 redaction checks added plus a visual one).

## Engine pass (2026-10-18)
Installed ffmpeg (Gyan.FFmpeg) and ImageMagick 7.1.2 via winget. Added `EngineChecks` to the self-test: 3 ImageMagick outputs, 14 ffmpeg outputs, all pass; whole self-test passes. Checks confirm the output exists and is non-empty (WebP also checks its header, VTT its first line), not that it plays.
**Bug found by installing the engines (S3, fixed):** with ImageMagick present, locked, missing and corrupt images fell back to magick and showed its raw error text instead of the clear message. The fallback now only runs for HEIC/HEIF/WebP/AVIF input.
Still not run: pandoc, LibreOffice, Poppler, Ghostscript paths.

## Wheel look pass (2026-10-18)
S01 now a ring of 8 pie wedges with gaps, labels only, centre shows thumbnail (or file icon) and size, light translucent warm palette (own colours). More than 8 options collapse into groups. Screenshot: clone-screens/S01.png. Not yet seen live over Explorer.

Wheel colours matched to the one reference image (sampled): wedges are white at about 55% over whatever is behind, thin gaps, no outlines, dark-brown medium labels, hover goes to near-opaque white, centre is a translucent disc with thumbnail beside the size. Personal use, so the look follows the reference closely; no original assets or copy are used.

## Frosted glass ring (2026-10-18)
Built from the approved mockup: wedges inset and drawn with a 10 px round-joined white rim (rounded corners), top-to-bottom white glass gradient, near-opaque hover, soft shadow under the ring, white labels with a faint halo, glass centre with thumbnail, size and file name. No real backdrop blur: Acrylic/Mica does not work on an AllowsTransparency window, so the glass is translucent, not blurred. Self-test screenshot now renders on a colourful gradient.

## "I love" tools pass (2026-10-18)
Added Resize/enlarge, Compress to… (size or quality), Rotate, Images to PDF, video Resize and Compress to…, audio Compress to…, PDF Compress and PDF to JPG/PNG with the Windows renderer. Prompts are validated in the dialog before a job starts. Self-test 87/87 (outputs decoded with ffprobe and ImageMagick, not just size-checked). Live Ctrl-drag: Rotate, Resize (dialog answered through UI Automation) and Compress to… pass.
**Harder than expected:** e2e runs use the real app database, so they used up the 20 free files and the next run hit the paywall instead of the dialog. Unlocked this machine (personal use). e2e should get its own data folder.

## Trial removed (2026-10-18)
Personal use, so the 20-file allowance, paywall window, license section in Settings and license table (new installs) are gone. Existing databases keep an unused license table. Self-test 82/82, live drag passes.
