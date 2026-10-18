# Architecture: Tangerine clone for Windows

Target: Windows 10/11 x64, local-only, every recon feature. The skill's default web stack doesn't apply (no server, no accounts), so this is a desktop app. Stack needs your confirmation (see Open questions).

## Stack
| layer | choice | why |
| --- | --- | --- |
| shell | Electron + TypeScript | transparent always-on-top overlay windows and file drops with no native UI code; you already have Node |
| global input | uiohook-napi | low-level mouse/keyboard hook: sees modifiers held and a left-button drag anywhere, including Explorer |
| overlay UI | HTML/CSS/SVG wheel in a transparent BrowserWindow | the wheel is radial buttons that are drop targets |
| storage | SQLite via better-sqlite3 (`schema.sql`) | job history, settings, trial counter |
| job queue | in-process p-queue, engines as child processes | no service; cancel = kill child tree |
| packaging | electron-builder (NSIS), engines in `resources/bin` | users install nothing else |
| tray | Electron Tray | equivalent of the menu-bar item |
| license | local counter now; Lemon Squeezy/Paddle key check later | the $29.99 model is a business choice |

## Engines (bundled binaries, no network)
| need | engine |
| --- | --- |
| video, audio, GIF, SRT/VTT, MP3 extract, trim, compress, target size | ffmpeg |
| JPG PNG WebP TIFF AVIF BMP, crop, compress, metadata | sharp (libvips) |
| HEIC in/out, SVG in | ImageMagick (Windows build with libheif) |
| ZIP TAR GZIP, RAR input | 7-Zip |
| MD TXT HTML EPUB DOCX | pandoc |
| PPT/PPTX/XLS/XLSX/ODS/RTF to PDF and between each other, CSV/TSV | LibreOffice headless, optional first-use download (~300 MB) |
| PDF merge/split/redact/metadata | pdf-lib (qpdf for repair) |
| PDF compress | Ghostscript |
| PDF to JPG/PNG/TXT | poppler (pdftoppm, pdftotext) |
| QR read | jsQR on a decoded image |

## Windows mapping of the Mac behaviors
| original | Windows implementation |
| --- | --- |
| Shift + drag a Finder file opens the wheel | uiohook: left button down + movement + Shift held with Explorer/desktop focused shows the overlay under the cursor. The overlay is a drop target; Explorer stays the drag source and delivers the file list on release. Shift-drag normally means "move" in Explorer, but nothing moves unless dropped on a real folder. |
| Command / Control for tools and actions | Ctrl = tools, Alt = actions by default, rebindable in Settings (Alt can trigger menu focus; test, fall back to Ctrl+Shift) |
| select file, Enter opens menu | global hotkey (default Ctrl+Alt+T); selection read from the focused Explorer window via PowerShell `Shell.Application` `SelectedItems()`; arrows/Enter/Esc inside the menu |
| menu-bar app | tray icon, start with Windows |
| touch drag / hold | overlay uses Pointer Events; long-press-then-drag opens the wheel. Explorer touch drag needs testing |
| batch | drop carries all files; wheel shows ops valid for every type, else grouped by type |
| AirDrop | no equivalent. Substitute: Windows Share (Nearby Share) via a small DataTransferManager helper. Degraded and packaging-dependent |
| Float | small frameless always-on-top window with the file tile (thumbnail, drag-out) |
| Copy / Move | wheel opens a destination picker (recent folders + browse) |
| Copy Path | `clipboard.writeText` |
| Terminal | `wt.exe -d <dir>`, fallback `cmd /k` |
| output | beside the source with a suffix, never overwriting (`name (1).jpg`) |

## Schema
`replica/schema.sql`: 4 tables (settings, batches, jobs, license). One local user, so no owner columns or row security. Status fields use CHECK constraints; the operation catalog is code.

## "API": IPC channels
| channel | does | caller | input | output | flow |
| --- | --- | --- | --- | --- | --- |
| overlay:show/hide | open/close wheel at cursor | main (hook) | mode, files | | F01 F02 F03 |
| ops:list | valid ops for the file types | overlay | paths[] | ops grouped by kind | F01 F02 F03 |
| job:run | queue ops | overlay | op_id, paths, params | batch_id | F01 F02 |
| job:progress | stream status | main | | job rows | F01 |
| job:cancel | kill child tree | toast | job_id | | F01 |
| action:run | copy/move/path/terminal/float/share | overlay | action, paths, dest | | F03 |
| settings:get/set | modifiers, hotkey | settings window | key, value | | |
| license:status | trial count / unlock | main | | files_used, unlocked | F05 |

Operation registry entry: `{id, kind, inputs[], output, paramsSchema, run()}`. The 275 conversions come from per-engine format-pair tables, not hand-written. No network, webhooks or private APIs.

## Parts that bite
- **Global drag detection is the riskiest part.** Elevated (admin) windows ignore user-level hooks and block drops (UIPI), so the overlay won't work over them. Antivirus can flag low-level hooks; sign the build.
- The overlay must appear fast without stealing focus (`focusable: false`, `showInactive`), or the drag cancels. Prototype this first.
- Per-monitor DPI: cover only the cursor's monitor.
- **PDF to DOCX/PPTX/XLSX/EPUB:** no good free local engine. LibreOffice's PDF import is low fidelity; PDF to TXT/MD is fine; PDF to EPUB loses layout. Label these "best effort".
- **Pages:** `.pages` input only yields its embedded preview; Pages output isn't feasible, so drop it. RTFD and text clippings are Mac-only, so skip them.
- Long paths, Unicode names, locked files: write to a temp file then rename; check disk space before big video jobs.
- Mixed-type batches, name clashes; cancel must kill the whole child tree (`taskkill /T`).
- Progress: ffmpeg `-progress pipe:1`.
- A local trial counter is trivially editable. Fine for a clone.

## Build order
1. **Vertical slice** (S01, S07; jobs, settings): tray app; uiohook detects Shift+drag; transparent overlay with a wheel; drop a HEIC/PNG; convert; toast when done. Proves drag detection over Explorer.
2. **Must-haves:** image, audio and video conversions via the registry; local-only guarantee.
3. **Should-haves:** tools wheel (S02), compress and target size, keyboard menu (S04), batch, Settings with rebinding (S06), archives, pandoc documents, LibreOffice office conversions, trial counter and unlock (S08).
4. **Could-haves:** actions wheel (S03) with Float (S09) and Share, PDF tools, crop/trim/redact, metadata editor, QR, subtitles, touch, best-effort PDF to Office.
5. Fixes from `/replica-entrepreneur`, then installer and code signing.

## Open questions
- Confirm Electron. Alternatives: Tauri (Rust), or WPF/C# for the most robust hooks and OLE drag handling.
- Personal use or to sell? That decides code signing, licensing, and naming (don't reuse the Tangerine name, icon or copy).
