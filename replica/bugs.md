# Bugs

Only things reproduced. Severity: S1 data loss, security, legal or core flow blocked; S2 feature broken, no workaround; S3 wrong with a workaround; S4 cosmetic.

## Fixed

| ID | sev | title | steps and result | cause and fix |
| --- | --- | --- | --- | --- |
| B1 | S1 | PDF library cannot be sold | PDFsharp 6.5 throws "The loaded license is not valid" at the first `new PdfDocument()`; its license is AGPL-3.0 | replaced with PdfPig (Apache-2.0); merge, split and strip all use its merger. Found by the self-test |
| B2 | S2 | Zip tool crashed | Zip a folder: `IOException: ... already exists` | the output name is reserved on disk first, and `ZipFile.Open(Create)` refuses an existing file. Now writes through a stream. Found by the self-test |
| B3 | S2 | Wheel opened in the middle of the screen during a drag | drag.ps1 drop: wheel centre was 400+ px from the cursor | placement used "no anchor" to mean keyboard mode, and the drag path also passes no anchor. Now keyed on keyboard mode. After fix: 1 px from cursor. Found by drag.ps1 |
| B4 | S3 | Wheel took about 0.7 s to appear after the trigger | trace: trigger 22:06:27.098, wheel 22:06:27.787 | selection and UI Automation lookup started only at the trigger; now starts when the drag starts. After fix: about 0.2 s |
| B5 | S3 | "Unpack" of `a.tar.gz` made a folder named `a.tar` | self-test | output naming only removed the last extension |
| B6 | S3 | Locked, missing and corrupt images all said "Install the matching Image Extension" | self-test messages | only the decode step was wrapped; now file-access errors and damaged files get their own messages, and the Store hint appears only for HEIC/WebP/AVIF |

Test-script mistakes, not app bugs, found along the way: `SetCursorPos` does not reach mouse hooks (use `mouse_event`); `Move` is an alias of `Move-Item`; `SetFocus` selects an Explorer item; Explorer reuses an open window and its old selection.

| B10 | S3 | New pie wedges were invisible to screen readers and UI Automation | wedges were plain Shapes, which have no automation peer, so Narrator and the e2e scripts could not see them | `Wedge` shape with an automation peer; self-test checks every wedge has a name (found while re-testing after the wheel redesign) |

| B11 | S2 | Shift pressed mid-drag did nothing unless the mouse moved again | user trace: a 2 s drag with Shift never registered (`shift=False` on every move); the trigger was only checked on mouse moves, through GetAsyncKeyState | keyboard hook tracks Shift/Ctrl/Alt and opens the wheel on key-down during a drag; new e2e scenario `still` (key down, no movement) passes, trace shows `trigger Convert (key)` |
| B12 | S3 | White wheel labels unreadable over dark windows | glass over dark Explorer goes mid-grey; white text about 2.8:1 | labels are dark brown on glass, readable on dark and bright desktops |

| B13 | S1 | Opening a wheel would freeze the app | found by the self-test hanging: checking which formats ImageMagick can write blocked the UI thread on async work that needed the UI thread | plain blocking process call; no blocking waits on async code left in UI paths |
| B14 | S2 | PSD to PNG made an empty file | ImageMagick writes one file per PSD layer unless told to take the merged picture | PSD/PSB read as frame 0 (the merged picture) |
| B15 | S4 | Crop accepted 0:0 | 0 divided by 0 is NaN, which passed the range check | NaN rejected |
| B16 | S3 | Strip metadata re-saved JPGs at quality 95 | lossy for a tool that should only remove tags | jpegtran / oxipng; pixels proven unchanged |

## Open

None at S1 or S2.

| ID | sev | title | notes |
| --- | --- | --- | --- |
| B7 | S3 | No progress percentage except for ffmpeg jobs | other engines show a moving bar with "Working" |
| ~~B8~~ | - | ~~Release builds accept no license key~~ (gone: trial removed) | by design until a vendor is chosen; the debug key is `DEV-UNLOCK` |
| B9 | S3 | Mixed file types (for example PNG + PDF) get no options | would need grouping by type |

## To check (not reproduced)
- Drag from the desktop, from a background Explorer tab, on a second monitor at another scale, and from an elevated window. The code handles the first three; the last cannot work by design.
- Whether Alt as the Actions key shows the Windows menu bar in some apps.
- The 20-file counter counts per input file, but a batch Zip of 5 files counts 5. Decide if that is what you want.
- Engine paths (ffmpeg, ImageMagick, pandoc, LibreOffice, Poppler, Ghostscript) are written but have never run.

- Live drag e2e (`e2e/drag.ps1`) was not re-run on the pie wheel: the run failed because a full-screen browser was in front of Explorer, so no wheel could be dragged over. Not a product result. Run it with the desktop clear, using the Release exe: `pwsh e2e/drag.ps1 -Exe <Release exe> -Slots 8`. Note the script defaults to the Debug path.

- e2e drag tests write job history to the real `%LOCALAPPDATA%\DropWheel` database. Harmless now the trial is gone; a test data folder would still be cleaner.
- Resize without ImageMagick (Windows scaler fallback) is not covered by the self-test on this machine, since ImageMagick is installed.
