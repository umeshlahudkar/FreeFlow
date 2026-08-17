# Scaling Notes — Expanding to 1000+ Levels

Notes for expanding FreeFlow to 1000+ levels, including dynamic (procedurally
generated) levels and additional block features. Grounded in the current
implementation as of this review; see also [`KNOWN_ISSUES.md`](KNOWN_ISSUES.md) for
drag/select algorithm bugs.

---

## Must fix before scaling (these will break, not just slow down)

### 1. `PairColorType` is the pair-identity key — hard cap of 9 simultaneous pairs

Also flagged as issue #2 in `KNOWN_ISSUES.md`, but at 1000+ levels with denser or
dynamic boards this stops being theoretical. Bigger, more interesting levels are
exactly where more than 9 colors on screen becomes likely.

**Fix:** give each pair a unique ID separate from its display color. Let
`PairColorType`/color become purely cosmetic (looked up by ID, reusable across many
IDs), and key `completedPairs` and the "find the matching dot" logic off pair ID
instead of color.

### 2. Level-select screen instantiates a button per level, for *all* levels, up front

`LevelScreenController.SpawnLevelButtons` (`Assets/Script/UI/LevelScreenController.cs:53`)
does one `Instantiate` per level and keeps every button alive in `levelButtons` for the
life of the app. At 20 levels that's nothing; at 1000+ that's 1000 live
Button/TMP/Image GameObjects created the moment the main scene loads.

Note: the README claims object pooling is used "for Level buttons and Grid blocks,"
but the pool for level buttons is actually commented out in this file
(`//private ObjectPool<LevelButton> objectPool;`) — it was only ever finished for grid
`Block`s, not level buttons.

**Fix:** real virtualization — instantiate only the current stage page (± 1 adjacent),
recycle buttons via a pool as the user swipes between stages, and populate level
number/state on demand instead of pre-creating every button.

---

## Should do as part of the level-data expansion

### 3. `LevelDataSO` is one monolithic asset holding every level's grid data

`UIController` (`Assets/Script/UI/UIController.cs:31`) holds a direct scene reference
to `levelDataSO`, so the entire array — all 1000+ levels' `GridRow[]` data —
deserializes into memory as soon as the scene loads, even though only one level plays
at a time.

**Fix:** split into per-level (or per-pack-of-N) ScriptableObjects loaded on demand
(`Resources.Load` by path, or Addressables), so memory scales with levels *visited*,
not levels that exist. This also avoids the Inspector pain of editing a single array
with 1000 elements.

### 4. Pre-size the block pool to the max grid, not 16

`BoardGenerator.InitializePool` (`Assets/Script/GamePlay/BoardGenerator.cs:22`) starts
the pool at 16 (a 4×4 grid). It auto-grows the first time an 8×8 level loads (64
cells) and never shrinks after — fine long-term, but that first big level will eat a
mid-play allocation/instantiate spike.

**Fix:** size the pool to 64 up front (the max `GridSize`).

### 5. Save file does a full read-modify-write on every settings tick, and grows an array by copy on every level completion

`AudioManager.SaveAudioData` (`Assets/Script/AudioManager.cs:97`) round-trips the
*entire* `SaveData` struct through `JsonUtility` on every slider `onValueChanged` call.
Fine at today's size, but will matter more as `completedlevelMoves` grows toward 1000
ints and richer per-level data (stars, times, etc.) is likely to be added.
`GamePlayController.SaveLevelData` also reallocates and copies the moves array on
every single level completion.

**Fix:** debounce slider-driven saves (save on slider release / after a short delay,
not on every value-changed event). Use a fixed-size array sized to the total level
count from the start instead of growing it by copy per completion.

---

## Before adding "extra features" to blocks

This is the one to treat most carefully given what the drag-algorithm review turned
up. `Block` today is a flat bag of fields (`isPairBlock`, `pairColorType`,
`highlightedColorType`), and `GamePlayController`'s `OnPointerDown/Moved/Up` is
already a dense, stateful if/else chain built around exactly two cell states (dot vs.
plain) — with fragile implicit invariants (e.g. "index 0 of a stored path is always a
dot") documented in `KNOWN_ISSUES.md`. Bolting walls, bridges, locked/ice cells,
teleporters, etc. onto this as more bools and more branches will compound that
fragility, not just add code.

**Recommended refactor before adding block features:**
- Introduce a `BlockType` enum or small interface (e.g. `IBlockBehaviour`) so each new
  mechanic is its own isolated piece of logic instead of a new branch in the shared
  controller.
- Rework `CanSelectToAdd` / `GetDirection` to ask the block "can a path enter/exit me,
  and how" rather than having the controller hardcode "dot vs. plain" everywhere.

---

## For dynamic (procedural) levels specifically

There is currently no level generator and no solvability validator anywhere in the
codebase — completion is purely "did the player's moves connect all pairs," with no
independent check that a board *can* be solved in the first place.

- If levels are generated rather than hand-authored, a generator (random
  non-overlapping paths per pair) needs a paired validator/solver run at generation
  time, not just relied on at play time.
- Decide now whether procedural levels get sequential int IDs (matching the current
  save-array model) or need GUID/string-style IDs. The current
  `completedlevelMoves[level - 1]` indexing assumes a small, fixed, sequential level
  count and won't hold up if generated levels aren't simply "1 through N."

---

## Optional / longer-term

- **Addressables / asset streaming:** everything currently loads via `Resources` and
  direct scene references, which is fine at today's scale but loads the entire content
  set into memory with no async unload. Worth adopting once per-level assets (custom
  art per block feature, per-level backgrounds, etc.) start adding real weight.
- **Scene splitting:** all screens (main menu, level select, gameplay, pause,
  settings, game over) live in one `MainScene` and are toggled via `SetActive`. Fine
  today; if level content grows heavy, consider additive scene loading so unused
  screens' memory can actually be freed.
