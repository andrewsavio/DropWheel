# Recon: Tangerine (File Converter) for Mac

Status: public sources only. No app binary, no account, no screenshots yet (screens/ is empty).
Scope assumption (unconfirmed): desktop; core loop = drag file + modifier key -> wheel menu -> local conversion. Audience unconfirmed.

## Sources
| source | url | gives |
| --- | --- | --- |
| marketing site | https://tangerineformac.com/ | feature list, formats, modifier keys, privacy claim |
| Mac App Store | https://apps.apple.com/us/app/tangerine-file-converter/id6813034065?mt=12 | pricing, tools list, changelog, reviews |
| (not read yet) | site: press kit, privacy, support, demo video | demo video = real flow; watch it for screens |

## Pricing (gates what matters)
Free download, first 20 files free, $29.99 one-time "Full Unlock". File actions (AirDrop etc.) do not count against the allowance.

## Screens
| ID | screen | how you get there | purpose | components | states |
| --- | --- | --- | --- | --- | --- |
| S01 | Conversion wheel | hold Shift while dragging a file | pick output format | radial menu, format labels | open, hover, cancel; unseen: no valid formats |
| S02 | Tools wheel | Command (site) / Option+Shift (App Store) while dragging | pick a file tool | radial menu | per file type |
| S03 | Actions wheel | Control while dragging (v2.0) | AirDrop, Float, Copy, Move, Copy Path, Terminal | radial menu | works on folders |
| S04 | Keyboard menu | select file, press Enter | same menus without dragging | list menu, arrow/Enter/Esc | |
| S05 | Menu bar item | always | app lives in menu bar | status item, menu | unseen |
| S06 | Settings | menu bar | rebind modifier keys | key pickers | unseen |
| S07 | Progress / result | after drop | show conversion running and done | unseen | unseen: progress, done, error |
| S08 | Paywall / unlock | after 20 files | buy Full Unlock | unseen | unseen |
| S09 | Floating file | "Float" action | keep a file on top of windows | floating tile | unseen |
Touch variant: drag to move, hold then drag to convert.

## Flows
- F01 Convert: drag file -> hold Shift (S01) -> drop on format -> S07 output. Edge: unsupported type, name clash, 20-file limit, multi-file batch of mixed types.
- F02 Tool: drag -> tools modifier (S02) -> drop on tool -> parameters? (unseen, e.g. target size, crop region) -> output.
- F03 Action: drag -> Control (S03) -> Copy / Move / AirDrop / Float / Copy Path / Terminal.
- F04 Keyboard: select file in Finder -> Enter (S04) -> arrows -> Enter.
- F05 Unlock: hit limit -> S08 -> purchase.
Happy path F01: roughly 0 clicks (drag, hold key, drop). That is the number to beat.
OPEN: Finder integration mechanism unknown (how a drag in Finder is detected globally).

## Discrepancy to resolve
Site says Command opens tools; App Store says Option+Shift. Likely changed in 2.0 when Control was added. Check in the real app or demo video.

## Components
Radial wheel menu (items, hover, nested?), menu bar item, settings key recorder, progress toast, paywall sheet, floating tile.

## Inferred data model (all low-medium confidence)
- Job: id, source_path(s), operation (convert|tool|action), target_format, params (json), status (queued|running|done|failed), output_path. evidence: wheel flow, batching. confidence: medium
- Operation catalog: id, kind, input_types[], output_type, params schema. evidence: 275 conversions / 26 tools / 6 actions. confidence: medium
- Settings: modifier_convert, modifier_tools, modifier_actions. evidence: site. confidence: high
- License: free_files_used (int), unlocked (bool). evidence: pricing. confidence: high

## Formats (site, as listed)
Images JPG PNG WebP HEIC TIFF AVIF BMP, PDF out, SVG in. Audio MP3 M4A WAV FLAC OGG Opus AIFF WMA. Video MP4 MOV MKV WebM AVI WMV GIF, MP3 out. PDF -> DOCX Pages PPTX XLSX JPG PNG EPUB TXT MD. Docs DOCX Pages MD TXT EPUB (RTF/RTFD in), HTML out. Slides PPT/PPTX -> PDF JPG PNG; PDF -> PPTX. Sheets XLSX ODS CSV TSV, PDF out, XLS in. Subtitles SRT VTT, TXT out. Archives ZIP TAR GZIP, RAR in.
Tools (App Store): compress image/video/audio/PDF incl. target size and percentage; crop, redact, trim; edit/strip metadata; merge/split PDF; read QR.

## Cannot / should not clone
- Apple ecosystem pieces: AirDrop, Pages and RTFD handling, Mac App Store distribution and its purchase flow.
- Their brand, name, icon, copy.
- Quality of PDF -> DOCX/PPTX: hard, the hardest conversions on the list.

## Size
Screens 9, flows 5, entities 4. Hard parts: (1) global drag detection + overlay wheel, (2) document conversions (PDF <-> Office, Pages), (3) 275 conversions behind one engine layer. Likely engines (guess, unverified): ffmpeg, ImageMagick/libvips, pandoc, LibreOffice.
Size: M for wheel + media/image/archive core; L with Office/PDF conversions. A cross-platform (Windows) clone changes the overlay/Finder parts entirely.
