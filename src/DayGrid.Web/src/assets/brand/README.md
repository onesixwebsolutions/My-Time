# DayGrid brand assets

The One Six constellation mark, coloured from `src/styles.scss` — nothing here
was eyeballed. Seven circles, pure vector geometry, no fonts.

## Files

| File | Colours | Use |
|---|---|---|
| `logo-tile.svg` | `--accent` `#6366f1` → `purple-500` `#a855f7` | the sidebar tile and app icon, dark theme |
| `logo-tile-light.svg` | `#4f46e5` → `#a855f7` | same, light theme (`html[data-theme="light"]`) |
| `logo-mark.svg` | ring `--text` `#e6e8ee`, centre `--accent` `#6366f1` | mark on its own, dark theme |
| `logo-mark-light.svg` | ring `#0f172a`, centre `#4f46e5` | mark on its own, light theme |
| `logo-mark-mono.svg` | `currentColor` | inline in a template; inherits text colour |
| `logo-tile-{32,64,128,192,512}.png` | gradient tile | raster fallbacks, PWA icons |
| `logo-mark-{128,512}.png` | dark theme | raster fallback |
| `apple-touch-icon.png` | gradient tile, 180px | iOS home screen |
| `../../favicon.ico` | gradient tile, 16–256 | the `favicon.ico` `index.html` already links to but which was missing from the repo |

## Geometry

128×128 viewBox. Six ring dots of r 9.5 at radius 40 from centre, 60° apart,
first at 12 o'clock. Centre dot r 15 — exactly 1.58× a ring dot. The tile uses
rx 36, which is the 9px radius of the 32px sidebar tile scaled to the viewBox.

Do not rotate it, re-space the dots, or make the centre dot the same size as the
ring — the size difference is what makes it "one and six" rather than a pattern.
Minimum size 16px.

## What changed in the app

`src/app/layout/app-shell.component.ts` — the sidebar tile keeps its exact
footprint (`h-8 w-8`, `rounded-[9px]`, `bg-gradient-to-br from-accent
to-purple-500`, the same shadow); only the glyph changed. The mark is inline SVG
rather than an `<img>` so it needs no asset request and cannot flash.

To put the "D" back, restore this block:

```html
          <div
            class="grid h-8 w-8 place-items-center rounded-[9px] bg-gradient-to-br from-accent to-purple-500 text-[15px] font-extrabold text-white shadow-[0_4px_12px_rgba(99,102,241,.35)]"
          >
            D
          </div>
```

## Not done yet

`index.html` links `favicon.ico` only. To pick up the rest, add inside `<head>`:

```html
<link rel="apple-touch-icon" href="assets/brand/apple-touch-icon.png" />
<link rel="icon" type="image/svg+xml" href="assets/brand/logo-tile.svg" />
```
