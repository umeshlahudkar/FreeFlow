# Sprite Requirements

**Status: the set in `Assets/sprites 1/` is wired.** 12 of the 15 sprites are assigned on
`Block.prefab` and drawn by `Block.SetBlock`. Two are not used: `plate_length` (see *Not wired*)
and **`cell_bg`** — the grid is drawn by four plain edge rects per cell instead, one per boundary,
which is pixel-identical because the frame art was a single flat tone, and needs none of the
texture-pixel-to-rect-unit conversion a 9-sliced frame does. The table below is kept as the brief
the art was made from.

What art each mechanic draws today, and what it actually wants. Everything below is playable
right now — the placeholders are borrowed from the `Dark UI` and `FreeButtonSet` packs already
in the project, or are Unity's default white quad. Nothing here is blocking; this is the list
for making the board look designed rather than assembled.

## How the art is used (read this first)

- **Sprites are tinted in code.** Author them **white or greyscale with transparency** — every
  colour in the game is applied at runtime (`Block.WallColor`, pair colours, and so on). A
  coloured sprite cannot be re-tinted correctly.
- **Cell size is dynamic.** The board measures itself at runtime; a cell is roughly 125 px on an
  8×8 board and 250 px on a 4×4. Author at **256×256** and let it scale down.
- **Centre markers share one Image.** `Checkpoint`, `ForbiddenForPair`, `Mixed`, `Bridge`,
  `Splitter`, `Arrow` and the shared-destination inner dot all swap sprites on the *same*
  `specialMarkerImage`, which is 50% of the cell, square. So each of those sprites must read
  clearly at ~half a cell and must not rely on filling the whole tile.
- **Edge bars stretch, and only on one axis.** The four `wallImages` are pinned to an edge and
  stretched along it, so a bar meets the screen crushed across its thickness (~15 units) and close
  to 1:1 along its length (~100). Detail *along* the length survives; detail across the thickness
  has room for a handful of broad bands and no more. And the two axes swap roles between the
  Up/Down bars and the Left/Right ones, which is why walls ship as a sprite **and its transpose**
  rather than one shared image. 9-slicing does not rescue a single sprite here: the border is a
  fixed unit size while the bar's thickness scales with the cell.
- **Two visual conventions already in the code**, worth keeping:
  - **pair-coloured** marker = this rule is about *that* colour (checkpoint, forbidden).
  - **neutral** marker = applies to everyone (mixed, bridge, arrow, splitter, rotator).

## The list

| Mechanic | Drawn today | What it wants |
|---|---|---|
| **Blocked cell** | `cell_blocked_hatch`, grey `rgb(0.42)` | **generated** — 45° hazard stripes only: no outline, no plate, no inset, edge to edge, phased to run continuously across adjacent blocked cells. `cell_blocked` (thin hatch in a rounded frame) is still in the folder, unreferenced; swapping back is one guid on `blockedSprite`. |
| **Wall** | `edge_wall` + `edge_wall_vertical`, `rgb(0.93, 0.89, 0.81)` | **generated** — bevelled masonry bar, 128×32 and its transpose. Opaque greyscale, shading in RGB (the bar is drawn twice, once per neighbouring cell). Brighter than the grid lines on purpose. |
| **One-way** | green edge bar, same plain quad, shares the wall images | **its own sprite** — a chevron sitting on the edge, pointing the way in. Must stay an *edge* mark: the centred arrow glyph belongs to the arrow |
| **Checkpoint** | plain white quad → a square, tinted to the pair | a "must pass here" mark that is obviously *not* the forbidden one — a ring, a target, a footprint |
| **Forbidden cell** | `mark_forbidden` + `mark_ring_half`, pair-tinted | **generated** — ring with an X, against the permit cell's ring with a check. Was a single diagonal slash, which read as a different angle rather than the opposite of a check. `mark_ring_half` recolours the right arc when the cell names two colours. `Tools/make_ring_markers.py`. |
| **Permitted colours** | `mark_permit` + `mark_ring_half`, pair-tinted | **generated** — ring with a check, against the forbidden cell's ring with an X. `mark_ring_half` is the right arc of the same ring, shared by both rules and drawn on a second marker Image in the cell's second colour. `Tools/make_ring_markers.py`. |
| **Gate** | full-cell wash, orange `rgb(0.8,0.5,0.1)`; hidden when open | **two** sprites: locked and unlocked. Right now "open" is drawn as *nothing*, so an opened gate is indistinguishable from an empty cell |
| **Mixed (shared) cell** | `Dark UI/Free/CIRCLE4PXLAR` — a plain ring | a "share freely" mark, clearly different from the bridge: a dashed or double ring |
| **Exact length** | TextMeshPro number on the dot | optional: a small plate behind the number, so it does not sit directly on the pair colour |
| **Arrow** | `FreeButtonSet/icons/64x64/arrow_up`, rotated in code | a chevron/arrow designed for a board tile rather than a UI button. **Must point UP** in the source art — `MarkerRotationFor` turns it |
| **Bridge** | `FreeButtonSet/icons/64x64/plus` — a plain cross | a real crossing with an **over/under** — one lane visibly above the other. See the known gap below |
| **Splitter** | `FreeButtonSet/icons/64x64/share` | a three-stub junction. Must not read like the mixed ring or the bridge cross — all three mean "more than one path here" |
| **Rotator** | no sprite: two gold bars in the cell's own direction slots | the bars work and stay. Optional addition: a small "tap to turn" affordance in the corner, since nothing says the cell is interactive |
| **Shared destination** | cluster of tangent circles, one per colour, pair-tinted | **no art needed** — the cluster reuses `pairDotImage`'s circle at runtime and is positioned by anchors, so it scales with the cell. `dot_half` is no longer referenced. |

## Not wired

**`cell_bg`** — superseded. A cell now shows only the edges it *owns* (always its top and left; the
board's last row and column add the two rims nobody else would), so every boundary is drawn exactly
once and an interior line comes out the same weight as the rim. A frame sprite draws all four of its
own edges, so two neighbours doubled every interior line, and undoubling it meant making their
outlines coincide to the pixel — stretching each frame half a line past its cell and converting the
9-slice border through the canvas and sprite scales to know what half a line was. Keep the file: it
is the sprite to come back to if cells ever want rounded corners or a textured edge, which flat rects
cannot do.

**`plate_length`** — the plate meant to sit behind the exact-length number. It needs one new Image
child on `Block.prefab`, ordered just before `LengthLabel` so it draws behind the text. Everything
else reused an image that was already there.

**Bridge over/under** still is not drawn — see below. That one is not a sprite problem.

## Known gaps this art would close

**The bridge has no over/under.** The rule is fully enforced — one lane per axis, no turning —
but which pair went "over" is not drawn, because the path bars are per-cell with no per-pair
z-order. Fixing it properly needs two overlapping images plus a way to order them, so it is a
prefab and drawing change, not only a sprite. Worth deciding whether the art implies depth
(shadow under one lane) or the code learns to order bars.

**An open gate looks like an empty cell.** `RefreshGateVisual` hides the wash when the
dependency pair is solved, so the only feedback that a gate opened is that you can now draw
through it. An "unlocked" sprite would say so.

**Nothing marks a completed pair.** Not a mechanic, but the same kind of gap: a finished path
looks exactly like a half-drawn one, only longer. The counter and a sound are the only signals.

## Priority, if it helps

1. **Gate, open state** — a rule the player cannot see having changed.
2. **Bridge** — the placeholder cross does not say "crossing", and the depth cue is missing.
3. **One-way** — legible, but still reads as a plain bar; the chevrons need to carry more.
   (Wall is now generated: `Tools/make_wall_sprite.py`, deliberately a plain bar.)
4. Mixed, splitter, arrow — readable placeholders; replace for polish. Blocked is generated,
   not a placeholder: `Tools/make_blocked_sprite.py` re-renders it, and the stripe width, spacing
   and slab alpha are constants at the top of that script.
5. Length plate, rotator affordance, split disc — nice to have.

The three teaching pairs in the level set are where readability matters most, because each pair
is the same board with one cell changed: **3 vs 4** (one-way vs forbidden), **5 vs 10** (mixed
vs bridge), and **11 vs 13** (splitter vs shared destination). If a player cannot tell those
apart at a glance, the art is not done.
