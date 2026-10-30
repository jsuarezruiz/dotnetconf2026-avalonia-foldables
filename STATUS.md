# Status

## Bot vs Bugs

### Tested

| | iPhone Duo simulator (iOS 27.1) | Pixel Fold emulator (API 34) |
| --- | --- | --- |
| Cover display / closed | Menu and play by touch | Not tried |
| Flat, game across the fold | Yes | Yes, menus split at the fold |
| Book: fold arms the sling, opening fires | Yes, with Device Hub's Half open and Open buttons | Yes, with exact angles (120° armed 58%, fired on the way to 180°) |
| Tabletop: game on top, control deck below | Yes, layout and hinge gauge | Yes: aim dial, armed at 97%, fired, bug squashed |

The desktop head uses `Nova.Avalonia.Foldable.Simulator` from the repo (3D fold, animated postures, draggable hinge). Rendered headless: book posture arms the sling, Open fires it and squashes the bug.

### Notes

- Device Hub only moves the iPhone Duo's hinge between presets: closed, half open (about 128°) and
  open. For exact angles use the Pixel Fold emulator (`adb emu sensor set hinge-angle0 <degrees>`, or
  Extended controls > Virtual sensors) or the desktop simulator's hinge slider.
- Avalonia 12 pads a mobile app's content by the safe area on its own. The game turns that off
  (`TopLevel.AutoSafeAreaPadding="False"`), draws edge to edge, and pads only the HUD, deck and menus.

### Not done

- No sound: Avalonia has no audio API, and Kenney's sounds are .ogg, which iOS can't play.

## Inkfold

### Tested

| | Desktop simulator | iPhone Duo simulator (iOS 27.1) | Pixel Fold emulator (API 34) |
| --- | --- | --- | --- |
| Every brush, fill, picker, eraser | Yes, with the mouse | Pen and watercolor by touch | Pen by touch |
| Layers: add, hide, opacity, move, duplicate, delete, undo and redo | Yes | Not tried | Not tried |
| Symmetry, zoom, Fit page, export to PNG, gallery | Yes | Pinch zoom and Fit page | Not tried |
| Two-finger tap to undo | No touch | Yes | Not tried |
| Book or flat: the page across the fold | Yes | Not tried (Device Hub refuses automated clicks) | Yes, flat |
| Press: armed, closed, opened, printed | Yes, both fold directions | Not tried (needs Device Hub) | Yes, with `adb emu posture 1` then `3` |
| Tabletop: page on top, tools below | Yes; click the turned half to bring the tools to the front | Yes, layout and touch | Yes, layout and drawing |
| Closed: cover display | Yes | Not tried | Yes, with the press banner |

### Notes

- The press locks the fold direction when it's armed. The desktop simulator turns the device upright
  before closing it, which a hand doesn't, so reading the fold later pressed the wrong way.
- `adb shell input motionevent` moves in straight runs between the points given, so injected strokes
  look polygonal; the app's smoothing is fine with real touch and with sparse points.
- A Debug build takes about ten seconds to show its first frame on the emulator.

### Not done

- Pen pressure is read from Avalonia's pointer, but not tried with an Apple Pencil or a stylus.
- The press on the iPhone Duo: Device Hub's posture buttons can't be driven by automation here.
