# FreeFlow Expansion Plan — Step by Step

## Context

`KNOWN_ISSUES.md` (drag/select algorithm bugs), `SCALING_NOTES.md` (what breaks at
1000+ levels), and `FEATURE_ROADMAP.md` (new puzzle mechanics + feasibility tiers) were
written independently but describe the same codebase from three angles, and several
items overlap or depend on each other — most importantly, **pair identity is currently
the display color itself** (`PairColorType`, 9 values), which is flagged as both a
correctness bug (`KNOWN_ISSUES` #2) and the #1 must-fix for scaling (`SCALING_NOTES`
#1), and is also a hard blocker for several roadmap mechanics (gates, checkpoints,
mixed cells all need a durable per-pair ID).

This plan sequences all three docs into one ordered set of steps: fix what's broken →
fix what breaks at scale → lay the groundwork new mechanics need → add mechanics in
increasing order of architectural cost. Each step is independently shippable and
testable in the Unity Editor before moving to the next — nothing here should be done
as one giant change.

Confirmed by direct code reading (not just the docs): `GamePlayController.cs` (full
file), `Block.cs`, `BoardGenerator.cs`, `LevelData.cs`/`LevelDataSO.cs`,
`AudioManager.cs`, `PairColorType.cs`, `Direction.cs`, and `LevelScreenController.cs`
(pooling is indeed commented out, confirming the README/code mismatch). No
project-authored test suite exists (`Assets/Script` has none) — verification
throughout is manual play-testing via the Unity Editor (MCP is connected to the
`FreeFlow` instance).

---

## Step 1 — Safe bug-fix pass (no architecture change)

Low-risk cleanups from `KNOWN_ISSUES.md` that don't touch data structures, so they're
a safe warm-up and immediately improve feel/correctness.

- **#3 Tap-without-drag flashes dot black** — in `GamePlayController.OnPointerUp`
  (`:290-330`), skip `HighlightBlockBg()` when `selectedBlocks.Count == 1`, or set
  `highlightedColorType` to the block's own `PairColorType` at selection time.
- **#4 Dead merge branch** — remove the unreachable `RemoveAt(0)` + `AddRange` branch
  in `AddSelectedBlocksToCompletedPairs` (`:565-582`); confirm via the invariant already
  verified in `KNOWN_ISSUES.md` that the guarding key is never present, then collapse
  to just the `else` path.
- **#5 Misleading unused parameter** — drop the unused `type` param from
  `IsPairComplete(Block, Block, PairColorType)` (`:617-620`) and its one call site in
  `CanSelectToAdd` (`:436`).
- **#7 `touchPointer` Raycast Target** — verify in the Unity Editor (Inspector on the
  `touchPointer` prefab/GameObject) that `Raycast Target` is unchecked; if not, uncheck
  it and additionally set `touchPointer.raycastTarget = false` in code
  (`GamePlayController.SetTouchPointerImage`/`Start`) so it can't regress silently.
- **#1 Fast swipes skip cells** — in `GetDirection`/`OnPointerMoved`
  (`:460-482`, `:165-288`), when the new block is not exactly adjacent to the last
  selected block, walk the straight-line path between them and add the intermediate
  cells instead of returning `Direction.None`.

**Verify:** Play a level in the Editor (Play Mode via `manage_editor`), drag slowly and
tap-only to confirm no black flash, fast-flick a long pair to confirm no skipped cells,
confirm `touchPointer` state in Inspector.

---

## Step 2 — Pair-identity refactor (the shared prerequisite)

This is the one change nearly everything else depends on. Give each pair a unique ID
independent of its display color; `PairColorType` becomes purely cosmetic.

- `LevelData.cs`: add `public int[] pairId` (or extend `GridRow` with a parallel
  `int[] pairId` column) alongside the existing `PairColorType[] coloum` — color stays
  for rendering, ID becomes the actual identity.
- `Block.cs`: add a `PairId` field/property alongside `PairColorType`, set in
  `SetBlock`.
- `GamePlayController.cs`: re-key `completedPairs` as `Dictionary<int, List<Block>>`
  (was `Dictionary<PairColorType, ...>`), and change every lookup that currently
  compares `PairColorType`/`HighlightedColorType` (`HighlightSelectedColorTypeBlock`,
  `CanSelectToAdd`, `AddSelectedBlocksToCompletedPairs`, `GetPairCompleteCount`,
  `IsPairComplete`, `OnPointerDown`/`OnPointerMoved`) to compare pair ID instead. Color
  lookups (`GetColor`) stay keyed by `PairColorType` for rendering only.
- Add level-data validation (`KNOWN_ISSUES` #6) as part of this: on load, assert each
  non-zero pair ID appears exactly twice; guard `highlightedBlock[1]` before use instead
  of assuming it's always found.
- Existing hand-authored levels: since colors currently double as IDs 1:1, a one-time
  migration can auto-derive `pairId` from the existing `PairColorType` value — no manual
  re-authoring needed for levels that already have ≤9 pairs.

**Verify:** Play every existing level end-to-end (or at least one per grid size) to
confirm pair completion, reconnect, and steal-cell behavior are unchanged. Then author
one test level with >9 pairs (only possible once `GridSize_8X8` + this fix are both in)
to confirm the old cap is actually gone.

---

## Step 3 — Level-select virtualization (`SCALING_NOTES` must-fix #2)

- `LevelScreenController.SpawnLevelButtons` (`:53`): replace the "instantiate one
  button per level, keep all alive" approach with real virtualization — page the level
  list (current stage ± 1 adjacent), and finish the `ObjectPool<LevelButton>` that's
  currently commented out (`:29`) instead of leaving it half-done. Populate level
  number/completion state on the pooled button on demand as the user scrolls/swipes
  between stages.

**Verify:** Open the level-select screen with a test `LevelDataSO` sized to 100+ levels,
confirm scroll/page performance and that only a small, bounded number of `LevelButton`
GameObjects exist at once (check via Unity Editor hierarchy or `find_gameobjects`).

---

## Step 4 — Level-data & save-system scaling (`SCALING_NOTES` #3–5)

- `LevelDataSO`/`UIController` (`:31`): split the monolithic `LevelData[] levels` array
  into per-level or per-pack-of-N `ScriptableObject`s loaded on demand (`Resources.Load`
  by path is the minimal change; Addressables is the longer-term option noted in
  `SCALING_NOTES`), so memory scales with levels visited, not levels that exist.
- `BoardGenerator.InitializePool` (`:22`): pre-size the pool to 64 (`GridSize_8X8`, the
  max) instead of 16, to remove the first-8×8-level allocation spike.
- `AudioManager.SaveAudioData` (`:97`): debounce so a full `SaveData` read-modify-write
  happens on slider release / after a short delay, not on every `onValueChanged` tick.
- `GamePlayController.SaveLevelData` (`:332`): use a fixed-size `completedlevelMoves`
  array sized to the total level count up front instead of reallocating/copying on every
  level completion.

**Verify:** Load the split level data and confirm only the active level's grid data is
in memory (spot-check via debug logging or the Profiler); complete several levels in a
row and confirm save data persists correctly across an Editor restart.

---

## Step 5 — `BlockType` abstraction (prerequisite for new mechanics)

Also called out in `SCALING_NOTES.md` ("before adding block features"). `Block` today
is a flat bag of bools; `GamePlayController`'s pointer handlers are a dense, stateful
if/else built around exactly two states (dot vs. plain). Bolting more bools on will
compound the fragility already documented in `KNOWN_ISSUES.md`.

- Introduce a `BlockType` enum (`Normal`, `Blocked`, `Checkpoint`, ... extendable) on
  `Block`, alongside the existing `PairId`/`PairColorType`.
- Rework `CanSelectToAdd` and `GetDirection` to ask the block "can a path enter/exit me,
  and how" (e.g. `Block.CanEnter(Direction)` ) rather than the controller hardcoding
  dot-vs-plain checks inline.
- Extend `GridRow`/`LevelData` to carry a `BlockType[]` column per row alongside
  `coloum` and `pairId`.

This step adds no new gameplay on its own — it's the seam every mechanic in Steps 6-8
plugs into. Keep it minimal (just enough indirection for "blocked" to prove the pattern
works) rather than speculatively building for mechanics not yet scheduled.

**Verify:** Re-play existing levels to confirm zero behavior change (this step should be
invisible to players); add one `Blocked` cell to a test level and confirm it can't be
entered.

---

## Step 6 — Feature tier 1: cheap mechanics (data/validation only)

From `FEATURE_ROADMAP.md`'s cheap tier — no drag-algorithm changes beyond what Step 5
already added:

- Blocked / non-walkable cell (uses `BlockType` from Step 5 directly)
- Required checkpoint cell (path must pass through it — checked at completion time)
- Exact-length constraint (path must use exactly N cells — checked at completion time)
- Forbidden-cell-per-color (a cell is off-limits to one pair ID but not others)

**Verify:** Author one test level per mechanic, confirm the constraint is enforced and
that pair-completion/level-complete logic still triggers correctly.

## Step 7 — Feature tier 2: moderate mechanics (extends `GetDirection`)

- One-way passage, arrow/turn-only/straight-only cells — extend the
  `Block.CanEnter(Direction)` predicate from Step 5.
- Wall between cells — needs genuinely new data (an edge property, not a cell property).
  Simplest representation: a per-cell bitmask of blocked edges (`N/S/E/W`) stored
  alongside `BlockType` in `GridRow`, checked in `GetDirection` before returning a
  direction.

**Verify:** Author test levels exercising each; confirm `GetDirection` correctly
refuses movement across a walled edge or into a cell from a disallowed direction.

## Step 8 — Feature tier 3: mechanics needing a new subsystem

- Gate / key, breakable, timer, pressure plate, dynamic (disappearing/appearing)
  obstacles.
- These need a small dependency-tracking layer — e.g. "has pair ID X completed" state
  exposed from `GamePlayController` that a gate's `BlockType` checks — which doesn't
  exist today. Scope this step's design once Steps 6-7 are stable; don't build it
  speculatively now.

**Verify:** Author a level where solving pair A is required to unlock a gate blocking
pair B; confirm solve-order dependency actually works.

## Step 9 — Feature tier 4: mixed/shared cells only

Scoped down to just mixed/shared cells (fixed mixed cell and/or two-layer cell from
`FEATURE_ROADMAP.md` §2) — the other biggest-lift mechanics (splitters, color switches,
crossing, bridges, portals, tunnels, rotating intersections) are out of scope for this
plan.

Mixed/shared cells break "one `Block` = one cell = owned by at most one path" — the
core invariant behind `grid[,]`, `completedPairs`, and the cell-stealing logic. This is
a genuine data-model change, not an incremental add:

- Allow a cell to register more than one pair ID as "using" it (e.g. `Block` tracks a
  small list of `(pairId, highlightedColorType)` entries instead of a single
  `highlightedColorType`), and both owning paths must be able to render their own
  direction highlight through the same cell without clobbering each other.
- `HighlightSelectedColorTypeBlock`/`CanSelectToAdd`/the cell-stealing logic in
  `OnPointerMoved` all currently assume single ownership per cell and need to check
  "is *this pair's* path present" rather than "is *any* path present."
- Completion logic (`GetPairCompleteCount`, `IsPairComplete`) needs to independently
  verify each pair passes through the mixed cell, not just adjacent cells.

**Verify:** Author a level with one fixed mixed cell shared by two pairs; confirm both
pairs can independently draw through it, complete independently, and that reconnecting
one pair's path doesn't clear the other pair's highlight on the shared cell.

---

## Not scheduled here (noted for later, per `SCALING_NOTES.md`)

- Procedural level generation needs a paired solvability validator, and a decision on
  sequential-int vs. GUID level IDs (current save indexing assumes sequential) — only
  relevant if/when procedural levels are pursued.
- Addressables/asset streaming and additive scene loading are optional longer-term
  items, not required for any step above.
