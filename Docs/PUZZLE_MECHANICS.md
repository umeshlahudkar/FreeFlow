# Puzzle Mechanics — Reference

Every board mechanic in FreeFlow: the eight shipped in
[`Enums/BlockType.cs`](../Assets/Script/Enums/BlockType.cs) /
[`ScriptableObject/LevelData.cs`](../Assets/Script/ScriptableObject/LevelData.cs) today, and
four proposed additions. Written against the code, not the roadmap docs — every file and
symbol name here is the real one.

See also [`SPRITE_REQUIREMENTS.md`](SPRITE_REQUIREMENTS.md) (what art each mechanic draws today
and what it wants), [`MECHANICS_IMPLEMENTATION_PLAN.md`](MECHANICS_IMPLEMENTATION_PLAN.md) (this
doc audited against the code, plus the build plan for the proposed mechanics),
[`FEATURE_ROADMAP.md`](FEATURE_ROADMAP.md) (the wider candidate list this narrows down),
[`EXPANSION_PLAN.md`](EXPANSION_PLAN.md) (build order across all four docs),
[`KNOWN_ISSUES.md`](KNOWN_ISSUES.md) and [`SCALING_NOTES.md`](SCALING_NOTES.md).

Companion artifact: **Mechanics Lab.dc.html**, which has a playable 5×5 demo level per
mechanic below.

## At a glance

| # | Mechanic | `BlockType` / data | Where it is enforced | Cost |
|---|---|---|---|---|
| 2.1 | Blocked cell | `Blocked` | `Block.CanEnter` | shipped |
| 2.2 | Wall between cells | `GridRow.wallMask` | `Block.HasWall` | shipped |
| 2.3 | Required checkpoint | `Checkpoint` + cell `pairId` | completion check | shipped |
| 2.4 | Forbidden colours | `ForbiddenForPair` + cell `pairId`/`secondPairId` | `Block.CanEnter` | shipped |
| 2.5 | One-way passage | `OneWay` + `requiredEntryDirection` | `Block.CanEnterFrom` | shipped |
| 2.6 | Gate | `Gate` + cell `pairId` | `Block.CanEnter` → `IsPairSolved` | shipped |
| 2.7 | Mixed cell | `Mixed` | `ProcessBlockStep` + `Block` occupancy | shipped |
| 2.8 | Exact path length | `PairConstraint.requiredPathLength` | completion check | shipped |
| 2.9 | Arrow cell | `Arrow` + `forcedExitDirection` | `Block.CanExit` + forced step | shipped |
| 2.10 | Bridge & crossing | `Bridge` | `CanAcceptEntry` + `CanExitFrom` | shipped |
| 2.11 | Splitter | `Splitter` + 3-dot pair | connectivity-based completion | shipped |
| 2.12 | Rotating intersection | `Rotator` + `initialRotation` | runtime rotation + tap input | shipped |
| 2.13 | Shared destination | `GridRow.secondPairId`/`thirdPairId`/`fourthPairId` | `Block.IsDotFor` | shipped |
| 2.14 | Permitted colours | `AllowedForPairs` + cell `pairId`/`secondPairId` | `Block.CanEnter` | shipped |

**Out of scope on purpose:** *turn-only* and *straight-only* cells. They constrain the
relationship between entry *and* exit like the arrow does, but unlike the arrow they do not
name the exit, which makes them ambiguous to read on a board and awkward to teach. The arrow
covers the same design need. `BlockType.cs` already notes this family as a follow-up; treat
it as closed unless a level design specifically needs "must bend here".

---

## 1. The model these mechanics plug into

Everything below is a variation on four facts about the current implementation.

**One cell = one `Block`.** The board is `Block[,] grid` in
[`GamePlayController`](../Assets/Script/GamePlay/GamePlayController.cs), laid out by
`BoardGenerator.LayoutBoard` from the square play area's measured rect. There is no notion of
an edge object between two cells; `wallMask` is stored per cell precisely because of that.

**Pair identity is an int, not a colour.** `Block.PairId` is the identity; `PairColorType` is
only rendering. `completedPairs` is keyed `Dictionary<int, List<Block>>`.
[`BoardGenerator`](../Assets/Script/GamePlay/BoardGenerator.cs) derives `pairId` from the
cell's colour when level data leaves the column empty, so old levels keep working, and levels
can now exceed 9 simultaneous pairs by authoring `pairId` explicitly.

**A path is a list of blocks, committed on release.** `selectedBlocks` grows during the drag;
`OnPointerUp` commits it into `completedPairs`, counts one move, and washes the cells at
`PathHighlightAlpha = 0.2f`. Direction bars are capsules grown by width
(`CommitFillDuration = 0.18f`), not `fillAmount`, so joints stay round.

**Three gates on every step.** `Block.CanEnter(int enteringPairId)` answers "may this pair be
in this cell at all", direction-independent. `Block.CanEnterFrom(Direction)` answers "may it
enter while travelling this way" — `GetDirection` consults both, which is why nothing
downstream needs to know about walls or one-way cells. `Block.CanExit(Direction)` answers "may
a path here leave that way", which the other two structurally cannot: they see a cell and an
approach, never where the path goes next. It is consulted by `CanTakeStep` at the call sites
that own `selectedBlocks`, since a two-block function cannot know how the path arrived. Almost
every mechanic here is one of those three predicates.

### Level-data columns

`LevelData` → `GridRow[] gridRows` + `PairConstraint[] pairConstraints`. Per row, all optional
except `coloum`:

| Column | Type | Meaning when empty |
|---|---|---|
| `coloum` | `PairColorType[]` | required — `None` for a non-dot cell |
| `pairId` | `int[]` | derived from `coloum` |
| `blockType` | `BlockType[]` | `Normal` everywhere |
| `wallMask` | `int[]` | no walls |
| `requiredEntryDirection` | `Direction[]` | `Direction.None` |
| `forcedExitDirection` | `Direction[]` | `Direction.None` |

`PairConstraint { int pairId; int requiredPathLength; }` — `0` means unconstrained.

`BlockType` values in file order: `Normal = 0`, `Blocked = 1`, `Checkpoint = 2`,
`ForbiddenForPair = 3`, `OneWay = 4`, `Gate = 5`, `Mixed = 6`, `Arrow = 7`, `Bridge = 8`,
`Splitter = 9`, `Rotator = 10`, `AllowedForPairs = 11`.

Five of those repurpose `PairId` on a cell that is *not* a dot, to mean "which pair this rule
is about": `Checkpoint`, `ForbiddenForPair`, `Gate`, `Splitter`, `AllowedForPairs` — and the last
also uses `SecondPairId`, for the second colour it permits. Worth knowing when reading
`Block.SetBlock` — the same field means two different things depending on `blockType`.

---

## 2. Shipped mechanics

### 2.1 Blocked cell — `BlockType.Blocked`

**Rule.** The cell is not part of the board. No pair may ever enter it.

**Data.** `blockType[c] = Blocked`. Nothing else.

**Runtime.** First check in `Block.CanEnter`, before any pair-specific logic. It cannot be
stolen, gated open, or shared — there is no state that makes a blocked cell passable.

**Visual.** `cell_blocked_hatch` across the whole tile in grey, via `SetObstacleVisual`: 45° hazard
stripes and nothing else — no outline, no plate behind them, no inset — so a blocked cell is
distinguishable
from a merely dark one at a glance rather than by shade alone. Only the stripes carry alpha, because
`SetObstacleVisual` tints the sprite one flat colour: alpha is the sole source of contrast *inside*
the cell, and the tint (`BlockedColor`, 0.42 grey) is its ceiling.

Two properties are baked in rather than chosen. The stripes run diagonally, which survives any cell
size — sprite and cell are both square, so nothing shears. And the stripe period divides the
one-cell step exactly, so stripes stay in phase across neighbouring blocked cells: with the sprite
also reaching the cell edge, a group of blocked cells reads as one continuous hazard field instead
of a mosaic with a seam at every boundary.

The stripes stop 2 units short of the cell boundary, but that is the prefab's doing, not the
sprite's: `BgHighlight` carries `sizeDelta -4`, which is roughly the half of the board grid line
that falls inside this cell. Take it to 0 and blocked cells start painting over their own grid
lines.

That image slot is shared with the path wash, so `HighlightBlockBg` clears the sprite back to a
plain fill — an obstacle's art would otherwise linger on a cell that later carries a path.

**Gotchas.** Blocked cells shrink the reachable area, so a board can go unsolvable.
`LevelValidator` now flood-fills each pair over the cells it may legally occupy and errors when
its dots cannot reach each other, which catches one-blocked-cell-too-many. It is a lower bound,
not a solver: it walks one pair at a time and knows nothing about two pairs competing for the
same cells. That gap is still the main argument for a real solver before procedural levels.

**Combos.** Blocked cells are the frame the interesting mechanics sit in: a gate, a one-way or
a bridge only matters when blocked cells make it *the* route rather than *a* route.
Blocked + `wallMask` builds a corridor without spending the whole board on grey.

**Cost.** Zero. Already the cheapest way to reshape a board.

---

### 2.2 Wall between cells — `GridRow.wallMask`

**Rule.** A specific *edge* is impassable. Both cells either side stay fully usable; only the
crossing is refused.

**Data.** `wallMask[c]` as a bitmask: `Left = 1`, `Right = 2`, `Up = 4`, `Down = 8`. A wall
belongs to the boundary between two cells, not to either cell, so either side may declare it:
`BoardGenerator.NormalizeWalls` mirrors a one-sided wall onto its neighbour at load. Movement
never needed that (`GetDirection` accepts either side), but the art did — only the declaring
cell drew a bar, so the same wall looked solid from one side and open from the other.

**Runtime.** `Block.HasWall(Direction)`, consulted by `GetDirection` before a direction is
returned. Independent of `blockType`, so any cell type can carry walls.

**Visual.** One bar per set bit, from the `wallImages` array indexed `(int)Direction - 1`,
coloured `Block.WallColor` = `rgb(0.45, 0.45, 0.45)`. Each bar is anchored to its own edge and
centred *on* it, so it straddles the boundary; both cells draw their copy and the two land on top
of each other, which is what makes a wall read as a single rectangle in the gap between two cells.
`ApplyWallGeometry` sets the thickness from the cell size (10% of it), so a wall carries the same
weight on a 4×4 board and an 8×8 one.

The bar is drawn as bevelled masonry: a dark lip, a highlight, the body, shading, a darker lip
across its thickness, with a few grooves cut perpendicular to its length. That is what separates it
from the grid: a wall straddles a grid line, so a flat bar of a single grey reads as a *thicker
line* — or, when it is darker than the line as it was at `rgb(0.45)`, as a gap between two cells.
Edges and a highlight read as an object sitting on top of the board instead. `WallColor` is now a
warm bone, brighter than the grid.

There are **two** sprites, `edge_wall` for the Up/Down bars and `edge_wall_vertical` for Left/Right,
the second the exact transpose of the first. The shading has to run across the bar's thickness, and
thickness is the y axis on one pair of edges and the x axis on the other — one sprite is necessarily
smeared along one of them. Transposing is also what keeps the light coming from the same corner for
both. `Tools/make_wall_sprite.py` generates the pair and records why 9-slicing was rejected: the
bar's thickness is a *fraction of the cell*, so a rim pinned to a fixed unit size is wrong at every
board size but one.

The shading lives in the sprite's RGB, at full alpha, not in its alpha channel. `NormalizeWalls`
mirrors a wall onto both neighbouring cells, so two coincident copies of the bar are drawn; shading
held in alpha would composite twice and flatten out, and a wall on the board's boundary — drawn once
— would then not match an interior one. Opaque is idempotent.

Two things had to be fixed before that was true. The colour was `rgb(0.05)` against a pure-black
cell — invisible in play (Step 1). And the four `wallImages` in the prefab were not edge bars at
all: all four were identical 100×100 squares centred in the cell, so a wall drew a grey box in the
*middle* of each of the two cells. The code had always indexed them per direction; nothing had
ever given them per-edge geometry.

**Gotchas.** `wallImages` is shared with the one-way mechanic (same array, different colour).
A cell that is both `OneWay` and walled on the one-way's entry edge now keeps the *wall* art:
the cell is unenterable either way, since `GetDirection` refuses a walled crossing before it
ever asks `CanEnterFrom`, so painting it green would advertise an opening that does not
exist. The combination is an authoring error and belongs in validation, not rendering.

**Combos.** The most under-used thing already in the codebase: walls change routing without
removing cells, so the board stays open-looking while getting much harder. Walls + one-way or
walls + arrow shrink the legal move set to a single option, which is how you force an exact
solution without filling the grid with grey.

**Cost.** Zero — shipped. One visual fix.

---

### 2.3 Required checkpoint — `BlockType.Checkpoint`

**Rule.** The cell names a pair. Any pair may cross it, but the named pair is not complete
unless its path includes it.

**Data.** `blockType[c] = Checkpoint`, and the cell's `pairId` set to the pair the rule is
about (the cell is not a dot — its `coloum` stays `None`).

**Runtime.** Not an entry check: it is evaluated at completion time, against the pair's
committed block list. A pair that connects both dots but skips its checkpoint stays incomplete
and the pair counter does not tick.

**Visual.** `mark_checkpoint` on the shared centre marker, tinted to that pair's colour.

**Gotchas.** `ShowSpecialMarker` looks the colour up as `GetColor((PairColorType)pairId)`,
i.e. it assumes `pairId` doubles as a valid colour enum value. True for every level authored
so far; a checkpoint whose pair id is outside 1–9 (possible now that ids are independent of
colour) needs a real pair-id → colour lookup first.

**Combos.** Checkpoint + `requiredPathLength` is the tightest cheap constraint available — one
forced cell plus one exact count usually leaves a single solution. Two checkpoints for one
pair at opposite corners turns a 5×5 into a real routing problem with no new code.

**Cost.** Zero — shipped.

---

### 2.4 Forbidden colours — `BlockType.ForbiddenForPair`

**Rule.** The cell names one or two pairs. Those pairs may never enter it; every other pair uses
the cell normally.

**Data.** `blockType[c] = ForbiddenForPair`, cell `pairId` = the first excluded pair,
`secondPairId` = the optional second. Same two columns, and the same caveat about them, as 2.14 —
see the Data note there.

**Runtime.** Second check in `Block.CanEnter(enteringPairId)`, via `NamesPair`. That predicate is
shared with 2.14 because the two rules read the same two ids and differ only in the conclusion:
this one refuses the pairs it names, that one refuses the pairs it does not. Refused during the
drag, so the player feels it immediately rather than at completion.

A cell with no `pairId` names nobody, which makes this rule a no-op — the safe direction for a
denylist, where 2.14's equivalent is a wall. `LevelValidator` errors on either.

**Visual.** `mark_forbidden` on the shared centre marker, tinted to the first excluded colour: a
ring with an **X** inside. It was a ring with a single diagonal slash, which was legible on its own
but reads as merely a *different angle* next to 2.14's ring-and-check rather than its opposite. Two
crossed strokes against a check is a pair the eye resolves without being taught.

When the cell names two colours it draws `mark_ring_half` — the right-hand arc of the same ring —
over the first marker in the second colour, so the ring reads two-tone and the X stays in the first
colour. That arc is one asset shared with 2.14, and `Tools/make_ring_markers.py` generates all three
glyphs from one ring, so the overlay cannot drift out of alignment with either rule's marker.

**Gotchas.** The `(PairColorType)pairId` assumption, now doubled: the marker tints from both ids, so
a cell naming a pair id outside 1–9 draws that half of the ring black. See §5.

Note also that on a board with **two** colours this rule and 2.14 are the same rule wearing
different art — "forbidden for pair 1" *is* "only pair 2 may pass". Prefer this one there: one id to
read instead of two. See 2.14's Gotchas for where they genuinely diverge.

**Combos.** The cheapest way to stop one pair — or two — taking the obvious lane, with no extra
geometry. Forbidden cells around a `Mixed` or `Gate` cell decide *which* pair gets the shared route
without blocking anyone outright.

**Cost.** Zero — shipped.

---

### 2.14 Permitted colours — `BlockType.AllowedForPairs`

**Rule.** The cell names one or two pairs. Only those may enter; every other pair is refused.
The inverse of 2.4: a denylist of one becomes an allowlist of one or two.

**Data.** `blockType[c] = AllowedForPairs`, cell `pairId` = the first permitted pair,
`secondPairId` = the optional second. No new column — it reuses the pair of ids the shared
destination (2.13) already added.

That reuse is the one thing to be careful of, and it has already caused two bugs. `secondPairId`
means **two different things** depending on `blockType`: a second pair the cell is a *dot* for
(2.13), or a second pair the cell *names* (2.4 and here). Only that one column is dual-purpose —
2.13's `thirdPairId` and `fourthPairId` are dot identities and no rule reads them. Every reader has to decide which, and the
decision is now a single predicate — `Block.SecondIdNamesAPair(BlockType)` — rather than a list of
type comparisons scattered across the readers, so a third rule adopting the column cannot be
half-adopted. The readers that were already correct guard on `isPairBlock` — `CollectDots`,
`IsDotFor`, `IsSharedGoal`, `ShowSecondDot` — which a permission cell fails, since it carries no
colour. The two that were not:

* `UIController.DescribeMechanics` treated any non-zero `secondPairId` as proof the level teaches a
  shared destination, so level 14 announced both mechanics.
* `LevelValidator.ValidateSharedGoals` did the same and errored with *"has a secondPairId but is not
  a dot at all"* on a perfectly valid permit cell — and the Python mirror in `Tools/` had no
  shared-goal pass at all, so it reported the level clean and hid the bug. The pass has been added
  there too.

Both now ask `SecondIdNamesAPair`. A third meaning for this column should not be added without
revisiting every reader.

**Runtime.** One more clause in `Block.CanEnter(enteringPairId)`, beside the forbidden check, via
`IsPermittedPair`. Refused during the drag like the forbidden cell, so the player feels it on the
finger rather than at completion.

A cell with **no** `pairId` permits nobody, which makes it a `Blocked` cell by another name. That is
deliberate: a half-authored permit cell fails closed and visibly rather than silently admitting
everyone. `LevelValidator` errors on it.

**Visual.** `mark_permit` — a ring with a check inside — on the shared centre marker, tinted to the
first permitted colour. Chosen to be legible *against* 2.4: the board's markers are a family of
rings told apart by their contents (empty for `Mixed`, a crosshair for a checkpoint), so the two
inverse rules take inverse glyphs, check against X.

The second permitted colour needs a second tint, and an `Image` has one, so a cell naming two draws
`mark_ring_half` — the right-hand arc of the same ring — on a second marker Image over the first.
The ring then reads two-tone and the check stays in the first colour. Same idiom as `dot_half` over
a pair dot on a shared destination. The arc is one asset shared with 2.4, and
`Tools/make_ring_markers.py` generates all three glyphs from one ring, so the overlay cannot drift
out of alignment with either marker.

**Gotchas.** Two, and the first is a design trap rather than a bug:

*On a two-colour board this mechanic is indistinguishable from 2.4* — "only pair 2 may pass" **is**
"forbidden for pair 1". Three colours make it distinct but still expressible as one forbidden cell
if it permits two of the three. It genuinely earns its place from **four** colours up, where
permitting two means excluding two and no single forbidden cell can say that. Two-pair levels should
keep using the forbidden cell: fewer ids to read.

Second, the usual `(PairColorType)pairId` assumption, now doubled — the marker tints from both
`pairId` and `secondPairId`, so a permit cell naming a pair id outside 1–9 draws that half of the
ring black. See §5.

A third permitted colour is deliberately not modelled: two fit the existing columns and the marker
can carry two tints, while the honest form beyond that is a bitmask no other mechanic needs.

**Combos.** It is the tool for a chokepoint on a busy board — a corridor two colours share and the
rest cannot use. Against `Mixed`, note the difference: `Mixed` says *how many* paths may occupy a
cell, this says *which*, and a cell that wanted both would need to be both types.

**Cost.** Zero — shipped.

---

### 2.5 One-way passage — `BlockType.OneWay`

**Rule.** The cell may only be entered while travelling in one specific direction. Any other
approach is refused, for every pair.

**Data.** `blockType[c] = OneWay` and `requiredEntryDirection[c]` = the direction a path must
be *moving* to get in (not the edge it comes through).

**Runtime.** `Block.CanEnterFrom(Direction incomingDirection)` — returns true when
`requiredEntryDirection` is `None` or matches. This is the direction-dependent half of the
entry test and the only mechanic currently using it.

**Visual.** `edge_oneway` — a bar with chevrons pointing the way a path must be travelling — on
the edge *opposite* the required direction, because that is the edge the path physically comes
through. It has **its own** Image (`oneWayImage`), positioned at the centre of the target edge and
rotated in 90° steps: a RectTransform's position and rotation are independent, so rotating one bar
serves all four edges. It no longer shares `wallImages`, which could not work once the art became
directional — a wall bar is symmetric and can live in a slot pinned to a fixed edge, chevrons
cannot.

**Gotchas.** One-way constrains entry only, so a path may still leave in any direction,
including straight back out the way it came. If a level needs "in one side, out the other",
that is the arrow ([§2.9](#29-arrow-cell--blocktypearrow)), not this.

`CanEnterFrom` tests `requiredEntryDirection` without looking at `blockType`, so the column
constrains *any* cell that carries it while only a `OneWay` cell draws the marker — an
invisible rule. `LevelValidator` rejects that combination, along with a `OneWay` that has no
direction (a no-op) and one whose only entry edge is walled (unenterable).

**Combos.** One-way is the shipped half of the arrow. Pair them and corridors become
directional end to end. One-way into a `Gate` makes the dependency spatial as well as ordered:
the gate is only reachable from one side.

**Cost.** Zero — shipped.

---

### 2.6 Gate — `BlockType.Gate`

**Rule.** The cell names a pair. While that pair is unsolved the gate blocks *everyone*; the
moment it completes, the gate opens for everyone. Break the dependency pair and the gate
re-locks, taking any path through it with it.

**Data.** `blockType[c] = Gate`, cell `pairId` = the pair the gate depends on.

**Runtime.** Third check in `Block.CanEnter`, reading
`GamePlayController.IsPairSolved(pairId)`. Re-evaluated on every selection check, so it opens
and re-locks live rather than at level load. `RefreshGateVisuals` runs on every pointer release
to keep the art in sync.

**Visual.** `gate_locked` across the whole tile in amber while locked, swapped for `gate_open` in
a muted green once the dependency pair is solved. It used to draw *nothing* when open, so the one
moment the mechanic pays off looked like an empty cell.

**Gotchas.** Level design must guarantee the dependency pair is solvable *without* the gate, or
the board deadlocks. A gate whose dependency pair itself needs to cross that gate is unsolvable
and nothing detects it.

**Combos.** This is the mechanic the roadmap is right about: difficulty from dependency, not
density. Every hard board should have one. Gate + checkpoint on the dependency pair controls
both the order *and* the route taken to unlock.

**Cost.** Zero — shipped. It is also the template for every future condition cell (key,
breakable, pressure plate): same cross-pair state query, different trigger.

---

### 2.7 Mixed cell — `BlockType.Mixed`

**Rule.** More than one pair may occupy the cell at once. Neither can steal it from the other,
and each pair's highlight state is independent.

**Two paths, not more.** A path crossing a cell claims two of its four direction slots — the edge
it entered by and the one it leaves by — so two paths fill a Mixed cell exactly, whether they run
straight or turn. `CanAcceptEntry` refuses a third: each slot records its owner, so a third path
would take part of one pair's line, and clearing it later would tear a hole in a line the player
never touched.

**Data.** `blockType[c] = Mixed`.

**Runtime.** Entry and completion need no special-casing, because each pair already tracks
its own `List<Block>` and list membership does not conflict. What a shared cell does need is
per-pair *cell* state, and `Block` carries it in three places:

- `directionOwnerPairId[4]` — which pair owns each direction bar, so
  `ResetAllHighlightDirection(int pairId)` can clear one pair's bars and leave the other's.
- a two-slot **occupancy list** (`AddOccupant` / `RemoveOccupant`, exposed as
  `IsOccupiedBy`, `OccupantCount`, `GetOccupantColorType`) — which pairs are in the cell and
  how each is drawn. Two slots is the ceiling, not a guess: a committed path always owns at
  least two direction slots, so a third occupant has nowhere to draw.
- `RefreshPathWash` — the single decision point for the full-cell wash: nobody, nothing;
  one occupant, that pair's colour; two, nothing at all, since two washes at
  `PathHighlightAlpha` cannot both be seen and showing either alone claims a cell that is
  only half-owned.

`GamePlayController.ProcessBlockStep` exempts `Mixed` from cell-stealing, and
`ResolveGrabbedPairId` decides which of two occupants a press on the cell grabbed (the one
whose path *ends* there, falling back to most recent).

Until Step 2 of [`MECHANICS_IMPLEMENTATION_PLAN.md`](MECHANICS_IMPLEMENTATION_PLAN.md), the
occupancy list was a single `(highlightedPairId, highlightedColorType)` field pair overwritten
by every `HighlightBlockDirection` call — so a shared cell's wash, its "who owns me" answer
and every guard reading it were last-writer-wins.

**Visual.** A neutral light ring, via `ShowMixedMarker` — the shared `specialMarkerImage`
with `mixedMarkerSprite` swapped in and tinted `rgb(0.85)` at 0.9 alpha. Deliberately not a
pair colour: the two pair-tinted marker shapes already mean "this rule is about that pair",
while a mixed cell belongs to whoever crosses it. `Mixed` shipped with no art at all until
Step 1 of [`MECHANICS_IMPLEMENTATION_PLAN.md`](MECHANICS_IMPLEMENTATION_PLAN.md).

**Gotchas.** Any new code that resets bars must use the pair-scoped overload, never the
blanket `ResetAllHighlightDirection()` (that one is only safe when a pooled cell is being
repurposed between levels). Anything asking "is this pair here?" wants `IsOccupiedBy`;
`HighlightedPairId` answers the narrower "who arrived last", which on a shared cell is a
different question.

**Combos.** Mixed is the permissive sibling of the bridge ([§2.10](#210-bridge--crossing--blocktypebridge)). Mixed + arrow gives a
readable crossing without the bridge's occupancy rules.

**Cost.** Zero — shipped, and correct as of Step 2.

---

### 2.8 Exact path length — `PairConstraint.requiredPathLength`

**Rule.** A pair must use exactly N cells, counting both dots. Shorter or longer and the pair
does not count as complete.

**Data.** A `PairConstraint` entry on the level: `{ pairId, requiredPathLength }`. Absent or
`0` means unconstrained; the whole `pairConstraints` array may be empty.

**Runtime.** `GamePlayController.SetLevelConstraints` at generate time,
`GetRequiredPathLength(pairId)` at query time, checked when the pair is evaluated for
completion. `Block.SetBlock` puts the number on the dot so the player knows which colour it
applies to.

**Visual.** `lengthLabel` (TextMeshPro) on the pair dot, showing the required count.

**Gotchas.** The label appears on *both* dots of the pair, which is correct but visually heavy
on a small grid. There is no partial feedback: the player only discovers the count is wrong
when the pair refuses to complete. A live "7 / 9" readout would make this mechanic much kinder,
and is UI-only work.

**Combos.** The one constraint that makes an *empty* board hard — cheapest difficulty per byte
of level data in the game. Length + checkpoint approaches a single-solution puzzle. Length + a
splitter branch ([§2.11](#211-splitter--blocktypesplitter)) would be brutal, and is the
natural late-game pairing.

**Cost.** Zero — shipped.

---

### 2.9 Arrow cell — `BlockType.Arrow`

**Rule.** However the path enters, it must leave in the printed direction. Entering against the
arrow is refused outright.

**Data.** `blockType[c] = Arrow` plus `forcedExitDirection[c]`. Its own column, deliberately
not a second meaning for `requiredEntryDirection`: a cell constrained on entry *and* exit is a
legitimate thing to author later, and one column meaning two things is exactly how
`requiredEntryDirection` ended up enforceable on cells that never draw it.

**Runtime.** Three pieces:

- `Block.CanExit(Direction)` — the third predicate (§1). Inert on every other cell type.
- `Block.CanEnterFrom` refuses a head-on entry, since the forced exit would send the path
  straight back into the cell it just came from — an illegal self-overlap. An arrow reads as a
  current, and you cannot swim into one.
- `ArrowChainIsLegal` refuses entry when the arrow's forced exit cannot be taken — off the
  board, through a wall, into a cell this pair may not enter, into another pair's dot, or back
  onto the path. A path is never committed onto a cell it could not leave. It walks the whole
  chain, since arrows may point into arrows.

`CanTakeStep` gates both of the drag's entry points (the adjacent step and the fast-swipe
interpolation) on two things a two-block function cannot see: whether the cell being *left* is
an arrow being left the wrong way, and whether the cell being *entered* is an arrow whose
forced exit is legal. A path is never committed onto a cell it cannot leave — off the board,
through a wall, into a cell this pair may not enter, into another pair's dot, or back onto
itself all refuse the whole entry.

**Visual.** The shared `specialMarkerImage` with `arrowMarkerSprite` swapped in, neutral white,
rotated by `MarkerRotationFor` to the forced direction. Neutral because the rule applies to
every pair. This is the one marker whose meaning *is* its rotation, which is why the glyph is
reserved for it — a `OneWay` cell marks its entry *edge* instead, so the two directional
mechanics never look alike.

**The arrow does not move the path for you.** It used to: entering one committed the forced step
immediately, so the stroke "continued through in one motion". That flourish cost three bugs, all
the same root — the drag loop is written around *the head of the path being the cell under the
finger*. It reads the head's position as the player's intent, drives the entry bar's fill from the
pointer's distance into that cell, and infers a retreat from raycasting an earlier cell. Advance
the path by itself and all three misfire: the bar was zeroed the frame after it was drawn, and the
finger still resting on the arrow was read as pulling back, so the forced cell was committed and
undone on alternating frames.

The rule loses nothing by dropping it. `CanExitFrom` already refuses every direction but the
printed one, so a path on an arrow has exactly one legal continuation — the player makes that move,
the arrow only decides which move exists. A path may rest on an arrow between drags, and
`OnPointerUp` treats a release there like any other.

**Gotchas.** A mid-path reconnect can trim a path so it *ends* on an arrow; that is legal and
playable, and `CanExitFrom` keeps the only continuation the forced one.

**Combos.** Arrow + `wallMask` leaves exactly one legal continuation, which is how you force an
exact solution without blocking cells. Arrow + `OneWay` on adjacent cells makes a corridor
directional end to end. Arrow into a `Mixed` cell is a readable crossing. Two arrows on the
cells flanking a dot make every route out of it one the board chooses — which is how `Level_9`
teaches the mechanic without borrowing a second one.

**Cost.** Shipped. One new predicate, one enum value, one column.

---

### 2.10 Bridge & crossing — `BlockType.Bridge`

**Rule.** Two pairs may occupy the cell at once, but on strict terms: one horizontally, one
vertically, each passing straight through. Neither may turn on it; neither may steal it.

**Player experience.** The classic Flow-Free-style crossing, and the strict sibling of
`Mixed` — level design now has a dial between "share freely" and "share on terms".

**Data.** `blockType[c] = Bridge`. No extra column: the axis is not authored, it is whichever
way each occupant happens to cross.

**Runtime.** Nothing new was needed, which is the point of having built `Mixed` and the arrow
first:

- `Block.CanAcceptEntry(Direction, int pairId)` — one lane per axis. A second pair crossing the
  same way is refused, because it would have nowhere to draw. Reads occupancy through
  `OwnsAxis`, i.e. which direction bars a pair owns here.
- `Block.CanExitFrom(entry, exit)` — straight through only. A path that turned on a bridge
  would be changing lanes in mid-air. Same predicate the arrow introduced, with a second rule
  inside it rather than a second predicate beside it.
- `Block.IsShareable` replaces `blockType != Mixed` in `ProcessBlockStep`'s cell-stealing
  guard. The two shareable types differ in their *terms*, not in whether they share, so the
  guard asks the block instead of comparing against a list of enum values that grows every time
  one is added.
- Per-pair occupancy (§2.7) is what makes the cell hold two paths at all, and the pair-scoped
  highlight reset is what lets one of them leave without erasing the other.

`CanExit(exitDirection, pairId)` is the runtime face of `CanExitFrom`: it reads the pair's entry
direction back off the single direction bar it owns here, since the bar sits on the edge the
path came through. Both live in `Block` so the rule has one definition — `LevelValidator` walks
hypothetical boards through `CanExitFrom` directly.

**Visual.** The shared `specialMarkerImage` with `bridgeMarkerSprite` (a crossing glyph),
tinted a cool neutral against the `Mixed` ring's warm grey, so the permissive and strict
shareable cells read apart at a glance.

> **Known gap.** The art shows a crossing, not an over/under. Which pair went "over" is not
> drawn, because the direction bars are per-cell with no per-pair z-order — that needs two
> overlapping images and a way to order them, which the bar system does not have today. The rule
> is fully enforced; only the depth cue is missing.

**Gotchas.** A bridge whose lanes are not both open is a one-lane corridor wearing crossing
art; `LevelValidator` rejects that, including a bridge on the board edge, where one lane has no
far side. A bridge on a pair dot is also refused — a path starts at a dot rather than crossing.

**Combos.** Gate + bridge makes solve order matter twice. `ForbiddenForPair` on a bridge
reserves one lane for a specific colour. Arrow into a bridge fixes which axis a pair arrives on.

**Cost.** Shipped, and cheap in the end: `Mixed` had already broken the one-cell-one-path
invariant and the arrow had already introduced the exit predicate.

---

### 2.11 Splitter — `BlockType.Splitter`

**Rule.** A splitter pair has three dots instead of two. The junction cell is where its branches
meet, and the pair is complete only when every dot reaches it.

**Data.** `blockType[c] = Splitter` with the cell's `pairId` naming the pair (the fourth mechanic
to repurpose `PairId` that way), and a pair whose `pairId` appears three times.

**Runtime.** The deepest change in this document, because the rule is not on the cell at all —
it is in how completion is measured. Two things changed:

- **A pair holds a set of segments, not one path.** `pairSegments` is
  `Dictionary<int, List<List<Block>>>`. Each segment is identified by the dot it starts from, so
  re-drawing a branch replaces it rather than piling up. The old store assigned one list per pair
  wholesale on commit, so a second branch of the same pair did not add — it silently replaced the
  first, which is why this could not be bolted on.
- **Completion is connectivity.** `IsPairSatisfied(pairId)` walks the pair's segments as a graph —
  cells are nodes, a segment joins consecutive cells, and two segments meet by sharing a cell —
  then requires every dot of the pair in one component, plus checkpoints, plus length. The old
  test asked whether the first and last cell of the one stored path were both dots of that pair:
  positional, and unable to describe three dots at all.

A two-dot pair drawn as a single segment is the trivial case of the general check, so the ten
levels that came before this behave exactly as they did.

**Why every branch has to reach the junction.** Completion never mentions the junction cell — it
asks only whether all of the pair's dots are in one connected component. But two segments join
*only* by sharing a cell, and a pair may not cross its own path anywhere else, so the junction is
the one place branches can meet. Reaching it is therefore forced by the geometry rather than by a
special rule, which is the shape worth keeping: one rule (connectivity) rather than two.

That also means the self-overlap guard in `CanSelectToAdd` needs an exemption for a pair's own
junction, and `Block.IsShareable` has to include `Splitter` so an arriving branch is never treated
as stealing the cell from its siblings. Without both, the second branch is refused at the junction
and a splitter pair cannot be completed at all.

Undo follows from the data shape: tapping a dot clears the segments that *touch that dot*, so a
splitter branch comes off on its own and leaves its siblings — and the junction they share —
alone. That needs `ClearSegmentVisuals` to un-draw bar by bar rather than cell by cell, because at
a junction two branches of the *same* pair own bars in the same cell and clearing by pair id would
take both.

**Visual.** The shared `specialMarkerImage` with `splitterMarkerSprite` — three endpoints meeting —
in warm gold. Distinct from the `Mixed` ring and the `Bridge` crossing by shape and tint, which
matters more here than anywhere else: all three are "more than one path here" cells, and a player
who confuses them cannot reason about the board.

**Gotchas.** A branch may *pass through* the junction rather than stop at it, so a three-dot pair
needs two drags at minimum (one stroke through the junction linking two dots, one branch joining
it) and three at most. One stroke can never collect all three: reaching a second dot of the same
pair makes `CanSelectToAdd` treat the pair as connected and refuses any further step.

The junction being the only legal meeting point also means a player can strand themselves — connect
two dots on a route that *avoids* the junction and the third branch has nothing to join, so the pair
cannot be completed and nothing says why. Legal to draw, impossible to finish, recoverable only by
clearing a branch. Worth designing around: place a junction so that routes bypassing it are
unattractive, or the level teaches frustration rather than the mechanic.

`Block`'s occupancy list went from two slots to three: a Mixed cell or a bridge holds
two *pairs*, but a splitter junction holds three *segments of one pair*. Occupancy is keyed by
pair, so the junction needs one slot for its own pair — the extra headroom covers a junction that
another pair also crosses.

Level validation had to learn the rule too: dot counts are two per pair *or three* when the pair
has a junction (the assertion this mechanic was always going to break), and every dot of a splitter
pair must be able to reach its junction, which pairwise dot-to-dot reachability says nothing about.

**Open call.** `requiredPathLength` on a splitter pair counts the distinct cells of the whole
figure, junction included once — the honest generalisation of "path length", and unchanged for
two-dot pairs. Per-*branch* lengths would be the sharper puzzle but need a `PairConstraint` that
can name a branch. Still undecided.

**Combos.** A checkpoint on one branch tells the player which branch is the awkward one.
`ForbiddenForPair` around the junction stops the splitter degenerating into a wide blob. Gate +
splitter makes two of the three branches wait on another pair.

**Cost.** Shipped. Big lift, front-loaded into one refactor — and the connectivity check is what
`Mixed` and the bridge wanted all along, since it verifies each pair passes through a shared cell
independently rather than treating the cell as owned.

---

### 2.12 Rotating intersection — `BlockType.Rotator`

**Rule.** The cell joins exactly two of its four edges. Tapping it turns that elbow a quarter
turn. A path may only enter and leave through the two joined edges.

**Player experience.** The only mechanic where the player changes the *board* rather than the
path — a second kind of decision on top of drawing, and the reason it was worth building last.

**Data.** `blockType[c] = Rotator` plus `initialRotation[c]`, 0-3, clockwise from Up+Right. The
initial rotation is level data; the current one is not.

**Runtime.** Three things no other mechanic needed:

- **Runtime board state.** `Block.currentRotation` is neither level data nor path data. `SetBlock`
  seeds it from the authored value and `ResetBlock` clears it, so a pooled cell cannot carry a
  rotation into the next level and the level asset is never written back to.
- **A tap that is not a drag.** `OnPointerDown` assumed every press begins a path. A press on a
  rotator now calls `RotateBlock` and returns without setting `isClicked`, which consumes the
  whole gesture — `OnPointerMoved` ignores it and no path starts. This depends on
  `touchPointer.raycastTarget` staying false, or the press never reaches the `Block` at all.
- **Path re-validation.** Rotating may invalidate a path already drawn through the cell, so
  `ClearSegmentsThrough` cuts every segment back to just before it. Cheaper and far more
  predictable than trying to repair a path the player did not redraw.

Entry and exit are then the same shape as every other mechanic: `CanEnterFrom` refuses an entry
through an unjoined edge, and `CanExitFrom` requires the exit to be the *other* joined edge.

**Every rotation is an elbow** — Up+Right, Right+Down, Down+Left, Left+Up — so a rotator always
turns a path ninety degrees. There is no straight-through state, and a level built around one has
to want the turn.

**Visual.** The two joined edges drawn as dim gold bars in the cell's own direction slots. Not a
marker glyph: those slots are already exactly the right geometry, so the elbow lines up with the
path that will run through it, and the shape *is* the information. Gold marks the cell as board
furniture the player can touch. The hint is only written to slots no pair owns, so a path covers
it with its own colour and `RefreshPathWash` brings it back when the path leaves.

**A rotation counts as a move.** That is a design call, not a technical one: it is what makes a
move budget or a star rating mean anything on a board with rotators. One line to reverse.

**Gotchas.** Solvability now depends on rotation state, so `LevelValidator` walks rotators in
their *most permissive* form — any entry edge, any ninety-degree turn — via
`CanEnterFromUnderAnyRotation` / `CanExitFromUnderAnyRotation`. Asking about the authored rotation
would report a level whose whole point is "turn this" as unsolvable. That is also the honest
warning about a future solver: it would have to search rotations, not just routes.

**Combos.** Two rotators in series is the cheapest hard puzzle in this document — the second is
only reachable through the first. Gate + rotator lets one cell serve two pairs in sequence.
Rotation-as-a-move makes `requiredPathLength` and move budgets bite.

**Cost.** Shipped. A small new subsystem, but genuinely new: runtime board state plus tap input.

---

### 2.13 Shared destination — `GridRow.secondPairId` / `thirdPairId` / `fourthPairId`

**Rule.** One cell is the endpoint for **two, three or four** pairs. Each colour has its own source
and all of them run to the same goal; none has a partner dot of its own.

**Player experience.** A hub. Reads as one dot wearing several colours, and it inverts the splitter:
that one is a single pair branching to three ends, this is several pairs converging on one.

**Data.** `secondPairId[c]`, `thirdPairId[c]` and `fourthPairId[c]` name the further pairs a dot
belongs to, filled in that order. Not a `BlockType` — this is not a kind of *cell*, it is extra
*identity* on an ordinary dot, and `blockType` already holds one value per cell that several
mechanics need for something else.

**Four is a ceiling with a reason, not a taste call.** A path that *ends* in a cell claims the one
edge it arrived through, and a cell has four edges. A fifth colour could not reach the hub without
reusing an edge another path already owns. `Block.MaxOccupants` is the same four, and was raised
from three for exactly this; raising it loosened nothing else, because `Mixed` is capped by
`FreeDirectionSlots() >= 2` and a bridge by axis ownership, neither of which consults it. Level 13
is built to show the limit being reached: four sources, one hub, one colour per side.

**Runtime.** Almost none, because the connectivity model already generalises. `Block.IsDotFor(int)`
replaces "compare `PairId`" everywhere the question is really *is this cell a dot of my pair* —
`DotsOfPair`, `IsPairComplete`, the dot clauses in `CanSelectToAdd`, `ArrowExitTarget`, and the
validator's other-pair's-dot rule. Completion then needs no changes at all: red's dots are
{source, hub} and blue's are {source, hub}, and each pair's own connectivity check answers
independently.

Two smaller consequences: the cell is `IsShareable`, so a later pair does not steal it from an
earlier one; and each arriving path terminates there using a single direction slot, which is what
makes four fit exactly.

**A shared destination is not a starting point.** A press on it could not say which colour was
meant, so drags begin at the sources. That removes the ambiguity rather than resolving it, which is
the better trade: any rule for guessing ("start whichever has no path") would be a rule the player
has to learn.

**Visual.** The single dot gives way to a cluster of **equal, mutually tangent circles, one per
colour**, arranged around the cell centre: two read as a sideways figure 8, three as a triangle,
four as a tangent 2×2 — which is the same figure both ways up. It was one full circle plus a half
circle in the second colour, which said "two" clearly and could not say "three".

The two sizes are derived rather than tuned, from one requirement. N centres sit on a ring of radius
`ring`, and neighbours touch without overlapping; the chord between adjacent centres is
`2·ring·sin(π/N)` and tangency makes that exactly two radii, so `radius = ring·sin(π/N)`. Asking the
cluster to fill its space exactly (`ring + radius = 1`) closes it. Circles therefore shrink as
colours are added while the cluster always occupies the same disc — no per-count constants, and
nothing to re-tune if the ceiling ever moved.

It is written as **anchors**, not sizes, so the cluster is laid out once in `SetBlock` and then
follows the cell through every board size and resize with no geometry pass. The circle sprite is
taken from `pairDotImage` at runtime, so the cluster is drawn with the same circle as every other
dot on the board and cannot drift from it. Four `sharedDotImages` on the prefab, because an `Image`
tints once and four colours need four of them; `dot_half` is no longer referenced.

The grab pulse follows whichever the cell is drawing — `HighlightBlock` scales the cluster on a
shared destination, since the single dot it used to scale is now hidden.

**Gotchas.** Every extra colour comes from `(PairColorType)<id>`, the same cast the markers make, so
they inherit the same limitation for pair ids of 10 or more — now on up to three ids per cell.

Validation rejects extra ids on a non-dot, a cell naming any pair more than once, a named pair with
no other dot on the board, and a **skipped slot** — a cell filling `thirdPairId` while leaving
`secondPairId` empty is a level that meant to name a colour and did not.

Note also that `secondPairId` is dual-purpose: the two permission rules (2.4, 2.14) read it as *a
pair the rule is about*, not a dot identity. `thirdPairId` and `fourthPairId` are dot identities
only. See `Block.SecondIdNamesAPair` and the Data note in 2.14.

**Combos.** `requiredPathLength` on one of the colours makes the race to the hub asymmetric. A gate
on one approach forces an order. Two hubs is a genuinely hard board and needs no new code. A
four-colour hub is already tight on its own: with every edge claimed, the four approaches cannot be
rerouted, so the rest of the board has to bend around them.

**Cost.** Small, and only because Step 7 landed first: before connectivity-based completion this
would have needed its own completion rule.

---

## 3. Nothing proposed remains

All thirteen mechanics in this document now ship. `FEATURE_ROADMAP.md` still lists candidates that
were never in scope here — teleport, portal, ice, colour-switch, and the key/breakable/timer/
pressure-plate family that are all variations on the gate's cross-pair state query. Each needs its
own design pass; none is blocked by anything above.

**Out of scope on purpose,** and still closed: *turn-only* and *straight-only* cells. They want the
same two-direction check the arrow and the bridge use, but they do not name the exit, which makes
them ambiguous to read and awkward to teach. The arrow covers the design need legibly, and the
rotator covers "must bend here" with a rule the player can see and change.

---

## 4. Suggested order

1. **Fix the two visual gaps** — `Mixed` has no art, `wallMask` bars are invisible at
   `rgb(0.05)`. Both are already-shipped rules that players cannot see. Cheapest possible win.
2. **Author levels using what exists.** Checkpoint, forbidden, one-way, gate and
   `requiredPathLength` are all in and all under-used. A pack built on gates and length
   constraints alone would raise difficulty with zero new code — and it tells you which
   mechanics actually need building.
3. ~~**Arrow**~~ — done: one new predicate (`CanExit`) plus the forced step, and the
   prerequisite for the bridge.
4. ~~**Bridge**~~ — done: `Mixed` + axis lock + `CanExit`, no new machinery.
5. ~~**Splitter**~~ — done: the connectivity refactor, which also retired the positional
   completion test.
6. ~~**Rotator**~~ — done, and last for the right reasons: the only new input verb, the only
   runtime board state, and the only one that complicates a future solver.

Every step above is complete. What is left is content, not mechanics.

The through-line, and the thing worth defending in review: **difficulty should come from
dependencies between mechanics, not from obstacle density.** A 6×6 with one gate, one
checkpoint and one exact-length pair is harder and more satisfying than an 8×8 packed with
blocked cells.

---

## 5. Open questions

- **Solvability validation.** `LevelValidator` now flood-fills each pair over the cells it may
  legally occupy — direction-aware, so arrows, bridges and rotators are modelled exactly, and
  rotators permissively since their orientation is the player's. It is still a *lower bound*: it
  walks one pair at a time and knows nothing about two pairs competing for the same cells. Real
  solvability needs a solver, and a solver would now have to search rotations too.
- **Undo.** Still no undo stack, only reconnect-by-tapping-a-dot — but that now clears *per
  branch* rather than per pair, which is most of the bookkeeping an undo stack would need.
- **`(PairColorType)pairId` assumption.** `ShowSpecialMarker` tints checkpoint and forbidden
  markers by casting the pair id to a colour. The pair-id refactor makes ids ≥ 10 legal, at
  which point those markers render wrong. One lookup table, worth doing before authoring any
  level with more than 9 pairs.
- ~~**Move counting.**~~ **Decided:** a rotation counts as a move (`RotateBlock`), so move
  budgets and star ratings can be layered on top later. One line to reverse if play disagrees.
