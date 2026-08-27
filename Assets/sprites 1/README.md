# FreeFlow board sprites

PNGs with a Unity `.meta` beside each. Drop the whole folder into
`Assets/Sprites/Board/` — import settings, 9-slice borders and Full Rect mesh come with the
files, so nothing needs setting by hand.

## What is here

Flat single-tone icons, authored **white with transparency at 256×256**, tinted at runtime.
`cell_bg.png` is the tile frame and the **only** sprite carrying a border (3px at 256); every
other file is just the mark that goes inside it.

| File | Used by | 9-slice (L,B,R,T) |
|---|---|---|
| `cell_bg.png` | the board tile itself | 4, 4, 4, 4 |
| `cell_blocked.png` | `BlockType.Blocked` | — |
| `edge_wall.png` | `GridRow.wallMask` | 8, 0, 8, 0 |
| `edge_oneway.png` | `BlockType.OneWay` | 96, 0, 96, 0 |
| `mark_checkpoint.png` | `BlockType.Checkpoint` | — |
| `mark_forbidden.png` | `BlockType.ForbiddenForPair` | — |
| `mark_bridge.png` | `BlockType.Bridge` *(proposed)* | — |
| `mark_arrow.png` | `BlockType.Arrow` *(proposed)* | — |
| `dot_half.png` | shared destination | — |

## Import settings baked into the `.meta` files

Sprite (2D and UI) · Single · **Full Rect** (required for 9-slicing) · pivot centre ·
256 pixels per unit · **Alpha Is Transparency on** · mip maps off · wrap Clamp · filter
Bilinear · max size 512 · packing tag `FreeFlowBoard`.

If Unity re-imports without them, the guids regenerate — that only matters if you have already
wired the sprites into prefabs, so import the folder before assigning anything.

## Wiring notes

**Centre markers** — `mark_*.png` and `dot_half.png` all swap on `specialMarkerImage`, sized
to **50% of the cell**, square. They are drawn to read at that size; do not shrink them
further.

**Edge bars** — `edge_wall.png` and `edge_oneway.png` are authored **for the top edge** and
rotated per direction by the `wallImages` slot. The one-way chevrons point **into** the cell:
they show the direction of travel, not the edge the path enters through.

**`mark_arrow.png` points UP** in the source art. `MarkerRotationFor` rotates it.

**`dot_half.png` is used twice** — the second Image is the same sprite flipped on X and tinted
to the other pair.

## Four code changes these assume

These are not pure art drops:

1. **One-way stops sharing `wallImages`.** It needs its own Image, or a cell that is both
   walled and one-way on the same edge fights over one sprite.
2. **Shared destination becomes two Images**, both `dot_half.png`, the second flipped on X.
3. **`Block.WallColor` goes from `rgb(0.05)` to about `rgb(0.45)`**, and the blocked cell up
   from `rgb(0.2)`. Flat icons carry no internal highlight, so the tint does the legibility
   work — this is what fixes the invisible-wall bug.
4. **Forbidden drops its 45° rotation.** `ShowSpecialMarker(pairId, 45f)` becomes `0f`; the
   mark is a barred circle now, not a rotated square.

## Orphaned

These sprites are still in the folder but nothing references them any more, their mechanics
having been removed: `plate_length.png`, `rotator_tap.png`, `mark_splitter.png`,
`mark_mixed.png`, `gate_locked.png`, `gate_open.png`.

## Still open

**Bridge z-order.** The art implies over/under, but path bars are per-cell with no per-pair
z-order, so which pair actually went over still is not drawn.

**Nothing marks a completed pair.** Not a mechanic, so no sprite here — but a finished path
still looks like a longer half-drawn one.
