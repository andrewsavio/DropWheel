# Components (Windows file converter)

Plain CSS custom properties from `tokens.css` (no Tailwind: the stack is Electron with a small renderer, so a utility framework adds nothing). Icons: Lucide (ISC), never the original's. Font: Inter bundled, Segoe UI Variable fallback. All copy is original.
Two themes: **app** (light: settings, paywall, toast) and **overlay** (dark: wheel, float tile, keyboard menu). Overlay colors use the `overlay-*` roles.

Wheel
  variants   convert (S01), tools (S02), actions (S03)
  geometry   --wheel-diameter 360, centre hole 96 shows file name + count; 6-12 slots, each a pie wedge; >12 slots paginate by 2-ring or scroll groups by category (Image, Video, Audio, Document...)
  states     opening (scale .92->1, fast), idle, slot-hover (overlay-accent border, label up), slot-drop-target (filled overlay-accent, on-overlay-accent text), disabled slot (muted, aria-disabled), no-valid-ops (centre message), cancel (drag leaves wheel for 400 ms or Esc)
  tokens     overlay-bg, overlay-surface, overlay-border, overlay-accent, shadow wheel
  a11y       role="menu", slots role="menuitem", arrow keys rotate, Enter confirms, Esc cancels; respects prefers-reduced-motion (no scale); never takes focus while a drag is active
  used on    S01 S02 S03

Keyboard menu (S04)
  a list version of the wheel: same ops, vertical list, type-to-filter, arrow/Enter/Esc. role="listbox".

WheelSlot
  anatomy    icon 24px (Lucide) + label xs/500, hit area >= --slot-min (56px)
  states     default, hover, drop-target, disabled, busy
  note       a conversion label shows the target format only ("JPG"), with source type in the centre

Progress toast (S07)
  width 320, bottom-right, radius md, shadow pop (app theme)
  states     queued, running (determinate bar from ffmpeg progress, else indeterminate), done (Show in folder, Open), failed (reason + Retry + Copy details), batch (n of m, expandable list)
  a11y       role="status" polite; failed uses role="alert"; Cancel is a real button

Button
  variants   primary (accent / on-accent), secondary (surface / text, border), ghost, danger
  sizes      sm 32, md 40, lg 48
  states     default, hover, active, focus-visible (2px accent ring, 2px offset), disabled, loading (label kept)
  used on    S06 S08 S07

Key recorder (Settings)
  records a modifier combination; shows keycaps; conflicts flagged inline ("Alt is also used by Windows menus"); Reset to default
  a11y       button that enters "press keys" mode, announces the recorded combo

Select / Toggle / Folder picker row
  native-feeling controls: toggle role="switch", select = real <select>, folder row = text + Browse button.

Settings window (S06)
  width 560, sections: Triggers (3 key recorders + hotkey), Output (beside source / chosen folder, overwrite rule), Engines (status of ffmpeg/LibreOffice, install LibreOffice), Startup, About + license.

Paywall sheet (S08)
  shown at the limit; plain explanation, one primary action (Unlock), secondary (Enter key), no dark patterns; shows files used.

Float tile (S09)
  frameless, overlay theme, 96px thumbnail + name, draggable out, close on Esc or x, always on top.

Destination picker (Copy/Move)
  recent folders list + Browse; opens as a small overlay list.

Tray menu
  Open settings, Pause (disables hooks), Check engines, Quit. Tray icon has an "paused" state.

Empty / error states to write
  no valid ops for this file, engine missing (offer install), source locked, disk full, output exists (auto-rename, no prompt), elevated-window warning (hooks cannot see it).
