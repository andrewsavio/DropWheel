# Test plan

The app is a Windows desktop program, so the web-style plan (Playwright, axe, 5xx checks) does not apply. Three layers instead:

| layer | how | where |
| --- | --- | --- |
| **L1 logic** | `DropWheel.exe --selftest log.txt [--shot wheel.png]` runs operations and the real job queue against real files | `app/DropWheel/SelfTest.cs` (38 checks) |
| **L2 real input** | PowerShell drives the real mouse and keyboard against a real Explorer window and the running app | `e2e/drag.ps1`, `e2e/hotkey.ps1` |
| **L3 by hand** | what cannot be scripted here | checklist at the end |

We test the clone only. The original is never loaded or scripted.

Status: **A** = automated and passing, **M** = needs a person, **E** = needs an engine that is not installed on this machine, **-** = not built.

## F01 Convert: drag, hold Shift, drop on a format

| case | what | status |
| --- | --- | --- |
| F01-H1 | Drag a PNG in Explorer, hold Shift, wheel opens under the cursor, drop on JPG, `pic.jpg` appears | A (drag.ps1 drop) |
| F01-H2 | Same with the keyboard: select file, Ctrl+Alt+T, Enter | A (hotkey.ps1 enter) |
| F01-H3 | Image conversions PNG to JPG, BMP, TIFF, GIF; transparent corner becomes white in JPG | A |
| F01-H4 | Audio, video, subtitles SRT/VTT, documents, office, PDF out, WebP/AVIF/HEIC | E |
| F01-E1 | Drag without the trigger key: no wheel, nothing created | A (nokey) |
| F01-E2 | Press on empty space and rubber-band with Shift held: no wheel | A (marquee) |
| F01-E3 | Release outside the wheel: wheel closes, nothing created | A (cancel) |
| F01-E4 | Esc closes the keyboard wheel | A |
| F01-E5 | Ctrl+Alt+T with nothing selected: no wheel | A |
| F01-E6 | Name with accents, emoji, CJK, spaces | A |
| F01-E7 | 180-character file name | A |
| F01-E8 | Output never overwrites (`a.jpg`, `a (1).jpg`) | A |
| F01-E9 | 8 jobs at once to the same name: 8 different files | A |
| F01-E10 | Chosen output folder is used | A |
| F01-E11 | Wheel under the cursor near a screen edge, on a second monitor, at 150% and 100% scaling | M |
| F01-E12 | Drag from the desktop (not an Explorer window) | M |
| F01-E13 | Several Explorer tabs open, drag from a background tab | M |
| F01-E14 | Mixed types selected (PNG + PDF): no conversion offered | A (logic) |
| F01-E15 | More than 12 options: groups open on hover | M (needs engines to reach >12) |
| F01-E16 | Drag from an admin-elevated Explorer or app | M (known limit: hook cannot see it) |
| F01-E17 | Touch: long-press then drag | - |
| F01-N1 | Corrupt image: failure says the file is damaged, no stray output | A |
| F01-N2 | Zero-byte file fails cleanly | A |
| F01-N3 | Source locked by another program: says so, no stray output | A |
| F01-N4 | Source deleted before the job runs: says so | A |
| F01-N5 | Cancel a running job: state Cancelled, placeholder removed | A |
| F01-N6 | Engine missing: its options are hidden, listed in Settings | A (hidden); M (Settings text) |
| F01-N7 | Disk full, output folder read-only | M |

## F02 Tools (Ctrl)

| case | what | status |
| --- | --- | --- |
| F02-H1 | Drag PNG with Ctrl, drop on Compress, `pic-compressed.jpg` appears | A |
| F02-H2 | Strip metadata keeps the extension (image, PDF) | A |
| F02-H3 | Merge two PDFs, split into pages, strip PDF author | A |
| F02-H4 | Zip a folder, unpack, repack to tar.gz and back, files intact | A |
| F02-E1 | Merge offered only for 2 or more PDFs | A |
| F02-E2 | Folder named `my.folder` zips to `my.folder.zip` | A |
| F02-H5 | Compress video/audio/PDF, crop, trim, redact, QR, target size | E / - |

## F03 Actions (Alt)

| case | what | status |
| --- | --- | --- |
| F03-H1 | Alt-drag, drop on Copy path, clipboard holds the full path | A |
| F03-H2 | Copy to / Move to a folder, name clash gets a number | M |
| F03-H3 | Terminal opens in the file's folder (Windows Terminal, else cmd) | M |
| F03-H4 | Show in folder selects the file | M |
| F03-H5 | Float tile: appears, drag the picture out, Esc closes | M |
| F03-E1 | Actions work over the free limit | A (logic) |
| F03-N1 | AirDrop | - (no Windows equivalent) |

## F04 Keyboard menu

| case | what | status |
| --- | --- | --- |
| F04-H1 | Ctrl+Alt+T, arrows move between slots, Enter runs, 1/2/3 switch wheel | A (Enter, Esc); M (arrows, 1/2/3) |
| F04-N1 | Ctrl+Alt+T already taken by another app: warning toast | M |

## F05 Trial and unlock

| case | what | status |
| --- | --- | --- |
| F05-H1 | A fresh install allows exactly 20 files, the 21st is blocked and the paywall event fires, no job created | A |
| F05-H2 | Failed jobs do not use the allowance; successful jobs count once | A |
| F05-H3 | Wrong key rejected | A |
| F05-H4 | Real key unlocks | - (no license vendor yet) |
| F05-E1 | Editing the database to reset the counter | known: trivially possible, accepted for now |

## Settings, tray, start-up

| case | what | status |
| --- | --- | --- |
| S-1 | Rebind a trigger key; picking a used key swaps the two | M |
| S-2 | Start with Windows on/off writes and removes the Run entry | M |
| S-3 | Pause in the tray stops the wheel; Quit exits | M |
| S-4 | Second launch does not start a second copy | M |
| S-5 | Keyboard only through Settings and the paywall; Narrator names for slots and fields | M |

## L3 manual checklist (for you)
1. Plug in a second monitor at a different scale; drag near each edge; wheel stays fully on screen.
2. Open three Explorer tabs; drag from each.
3. Drag a file from the desktop.
4. Copy to / Move to a folder on another drive; move a folder.
5. Float a PNG, drag it out into a folder.
6. Settings: change keys, output folder, start with Windows; restart and check they stuck.
7. Windows Narrator: tab through Settings and the wheel.
8. Install ffmpeg, ImageMagick, pandoc, LibreOffice, Poppler (or bundle them) and rerun `--selftest`; then convert one file of every kind.
