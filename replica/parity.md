# Parity report: DropWheel (Windows) vs the Mac file converter

**Verdict: not shippable yet.** 3 of 5 must-haves are only "partial": image formats beyond the five native ones, audio and video need engines that have never run on this machine. No open S1 or S2 bugs.

| measure | result |
| --- | --- |
| Feature parity | **65.5 / 100** (24 counted features; AirDrop and the pricing model are `skip`) |
| Layout parity | **not measured** (see below) |
| Open bugs | none at S1 or S2; 3 at S3 (`replica/bugs.md`) |

## What the one reference image shows (S01, the conversion wheel)
Source: the AVIF screenshot you added to Downloads (273x210 px, a small marketing graphic with a caption: "I hold shift, then drag the file"). Observed:
- The wheel is a **ring of 8 pie-wedge segments** that fill the ring with thin gaps, labels only (JPG, WEBP, HEIC, TIFF, AVIF, BMP, PDF, DOCX). Mine is a ring of 4 to 12 separate rounded tiles on a dark disc.
- The **centre shows a thumbnail of the dragged file and its size** ("2.2 MB"). Mine shows the file name and the mode word.
- The dragged file follows the cursor as a **thumbnail with a blue name chip** ("tangerine tree.png"). That is Windows' own drag image on my side, so I get the thumbnail but not a name chip.
- Light, translucent, warm peach on a coloured backdrop. Mine is dark. Colour is their trade dress and `replica-brand` changes ours anyway, so this is a keep.

## Layout score
`imgdiff` scored S01 at 15 / 100, but that number is not meaningful: the reference is a small marketing graphic on a gradient with a caption, and mine is a 420 px render on grey, with 4 slots against 8. For a fair number I need a real screenshot of the original wheel at the same size and state (for example a PNG file dragged with Shift held on a Mac). Only S01 has a reference; S02 to S09 have none.

## Feature parity
## Parity: 65.5 / 100

features 65.5  (24 counted, must-haves 2 of 5 done)

Not shippable yet: 3 must-have features are not done.

## By area, weakest first
- convert                       55.9  (9 features)
- tools                         56.2  (6 features)
- core                          75.0  (6 features)
- actions                       75.0  (2 features)
- privacy                      100.0  (1 features)

## Missing, in build order
- [must] convert: Convert audio formats, partial  (needs ffmpeg; code written, untested)
- [must] convert: Convert image formats, partial  (JPG PNG BMP TIFF GIF tested; WebP AVIF HEIC need ImageMagick (not installed here))
- [must] convert: Convert video formats, partial  (needs ffmpeg; code written, untested)
- [should] convert: Document convert (DOCX MD TXT EPUB HTML RTF), partial  (needs pandoc; untested)
- [should] core: Batch multiple files, partial  (same-type batches only; mixed types show no options)
- [should] core: Keyboard menu (Enter/arrows/Esc), partial  (Ctrl+Alt+T, Enter, Esc, empty selection tested with real keys; arrows and 1/2/3 not scripted)
- [should] tools: Compress image/video/audio/PDF incl target size, partial  (image tested; video/audio need ffmpeg, PDF needs Ghostscript; target size not built)
- [could] core: Touch drag/hold, no  (not built or tested)
- [could] tools: Crop / trim / redact, no  (needs a parameter UI)
- [could] tools: Read QR, no  (not built)
- [could] actions: Float file, partial  (built, not seen on screen yet)
- [could] convert: PDF to DOCX/PPTX/XLSX/EPUB, partial  (DOCX PPTX EPUB best effort via LibreOffice, untested; XLSX not offered)
- [could] convert: Slides PPT/PPTX to PDF/JPG/PNG, partial  (needs LibreOffice (+ Poppler for images); untested)
- [could] convert: Spreadsheet convert (XLSX ODS CSV TSV), partial  (needs LibreOffice; untested)
- [could] convert: Subtitle SRT VTT TXT, partial  (SRT to TXT tested; SRT<->VTT needs ffmpeg)
- [could] tools: Edit or strip metadata, partial  (strip for images and PDF tested; editing not built)

## Left out on purpose (not scored)
- AirDrop: no Windows equivalent; Share/Nearby Share needs a packaged helper
- Free 20 files then paid unlock: own pricing decision; counter + paywall built and tested, no license vendor yet

## Behaviour differences
| flow | original | clone | fix or keep |
| --- | --- | --- | --- |
| F01 trigger | hold Shift while dragging | hold Shift while dragging (rebindable) | same |
| F01 centre | thumbnail + file size | file name + mode | **fix**: show thumbnail and size |
| F01 wedges | pie segments, 8 options | separate tiles | **fix** (look), low priority |
| F01 many formats | one wheel of 8 for an image | groups open on hover above 12 | keep |
| F02 tools key | site says Command, App Store says Option+Shift | Ctrl | keep, rebindable |
| F03 AirDrop | present | not possible on Windows | skip, say so in the listing |
| keyboard | Enter on a selected Finder file | Ctrl+Alt+T then Enter | keep |
| trial | 20 free files, then $29.99 once | 20 free files, no vendor yet | decide pricing |
| clicks | drag, hold key, drop | same | tie |

## About DeskRest_n.exe (not used)
This file is **not** the Tangerine app. Its metadata says product "DeskRest", description "DeskRest installer", company AppSalt, signed by Beellet UAB, and Windows records that it came from a Softonic download page (`tangerine.en.softonic.com`). It is a third-party download-manager style installer for a different program, so it has nothing to teach about Tangerine's interface. I did not run it, and the replica rules also rule out decompiling the original's binary. I would delete it, or at least not run it. If you want the real Windows UI of a product like this, run the official app yourself and send screenshots.

## Top five to build next
1. Run the engines for real (ffmpeg, ImageMagick, pandoc, LibreOffice, Poppler): this is what moves the 3 must-haves from partial to done.
2. Centre of the wheel: thumbnail and file size.
3. Pie-wedge wheel look (after `replica-brand` picks the palette).
4. Compress with a target size, plus crop/trim.
5. Mixed-type batches (group by type) and a real license vendor.
