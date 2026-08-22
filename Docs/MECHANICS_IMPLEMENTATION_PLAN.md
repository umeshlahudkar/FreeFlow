# Mechanics Implementation Plan

An audit of what the game actually does today against
[`PUZZLE_MECHANICS.md`](PUZZLE_MECHANICS.md), followed by an ordered, independently
shippable plan for the four proposed mechanics (arrow, bridge, splitter, rotator) and the
fixes they depend on.

**How this was verified.** Direct reading of
[`Block.cs`](../Assets/Script/GamePlay/Block.cs) (all 663 lines),
[`GamePlayController.cs`](../Assets/Script/GamePlay/GamePlayController.cs) (all 1184),
[`BoardGenerator.cs`](../Assets/Script/GamePlay/BoardGenerator.cs),
[`LevelData.cs`](../Assets/Script/ScriptableObject/LevelData.cs),
[`BlockType.cs`](../Assets/Script/Enums/BlockType.cs), the `Block` prefab
(`Assets/Prefab/Block.prefab`), plus decoding all 12 level assets in
`Assets/Resources/Levels/` to see which mechanics are authored in real content. No test
suite exists, so every "verify" below is a manual Editor play-test.

**Status.** **Every step of this plan is built.** All twelve mechanics in
[`PUZZLE_MECHANICS.md`](PUZZLE_MECHANICS.md) ship, and the level set has been replaced
(S4.3/S4.5): the twelve original levels are gone and **twelve** new ones each teach exactly one
mechanic, with the mechanic named on screen.

Play-testing has started and has already paid for itself: it found that the wall and one-way
"edge bars" had no per-edge geometry in the prefab (S1.7). That is the whole point of the
outstanding play-tests — Step 7 rewrote every completion path and Step 8 added an input verb, and
no automated test covers either. What is left
after the play-test is content (S4.1, S4.6) and the two decisions in §4 that are still open.

Companion docs: [`PUZZLE_MECHANICS.md`](PUZZLE_MECHANICS.md) (the mechanic reference),
[`EXPANSION_PLAN.md`](EXPANSION_PLAN.md) (the earlier plan this continues from),
[`KNOWN_ISSUES.md`](KNOWN_ISSUES.md), [`SCALING_NOTES.md`](SCALING_NOTES.md).

---

## 1. Audit — what is actually in the build

All eight documented mechanics are implemented and all eight are exercised by exactly one
authored cell in one level each. That is the single most important finding: **the mechanics
are built but the content is a demo, not a difficulty curve.**

> The "Authored in real levels" column below describes the **original twelve levels**, which
> S4.3 deleted and replaced. It is kept because it is the evidence the audit's central finding
> rested on — one demo cell per mechanic. The current level set is tabulated in
> [§6, Step 4](#step-4--author-a-real-pack-on-the-shipped-mechanics).

| Mechanic | Code | Authored in real levels (pre-Step 4) |
|---|---|---|
| `Blocked` | `Block.CanEnter` `:248` | `Level_3` (1,2); `Level_4` (2,2),(2,3); `Level_11` (1,3) |
| `wallMask` | `Block.HasWall` `:285`, folded into `GetDirection` `:551` | `Level_6` (5,3) Right; `Level_11` (5,3) Right |
| `Checkpoint` | `PathSatisfiesCheckpoints` `:1077` | `Level_4` (4,2) → pair 4; `Level_11` (4,3) → pair 4 |
| `ForbiddenForPair` | `Block.CanEnter` `:248` | `Level_8` (4,3) → pair 4 |
| `OneWay` | `Block.CanEnterFrom` `:277` | `Level_7` (4,3) Down; `Level_12` (2,2) Down |
| `Gate` | `Block.CanEnter` → `IsPairSolved` `:1022` | `Level_9` (6,3) → pair 6; `Level_12` (6,2) → pair 7 |
| `Mixed` | `ProcessBlockStep` `:608`, `ResetAllHighlightDirection(int)` `:538` | `Level_10` (4,4); `Level_12` (4,4) |
| `requiredPathLength` | `PathSatisfiesLength` `:1093` | `Level_5` only — pair 3, length 6 |

Every rule cell in shipped content is authored correctly (each `Checkpoint`/`Gate`/
`ForbiddenForPair` cell has a real `pairId`), and the `Level_10` mixed cell is a genuine
crossing: pair 4 runs horizontally through (4,4) and pair 3 vertically through it. Nothing
in the shipped levels is broken. It is just twelve levels with one idea each.

### Where the doc and the code disagree

> This section is a **snapshot of what was true when the audit ran**, kept as written so the
> reasoning behind the plan stays readable. Deltas closed since then carry a *→ closed by*
> marker; live status always lives in [§6](#6-execution-tracker), never here.

`PUZZLE_MECHANICS.md` is accurate on rules, data columns, constants and visuals. Seven
deltas are worth recording, in descending order of consequence.

**1. `Mixed` needs more than two places to be correct — `Block`'s occupant identity is a
single slot.** The doc says entry and completion need no special-casing because each pair
tracks its own `List<Block>`. True for the *path lists*. But `Block` stores
`highlightedPairId` and `highlightedColorType` as single fields, written unconditionally by
`HighlightBlockDirection` (`Block.cs:434-437`). Only `directionOwnerPairId[4]` is per-slot.
Four consequences, all live today on `Level_10`:

- `HighlightBlockBg` (`Block.cs:598`) washes the cell in `highlightedColorType`, so a
  shared cell shows whichever pair committed last; the other pair's wash is silently lost.
- `ResetAllHighlightDirection(int pairId)` (`:538`) only clears the wash when
  `highlightedPairId == pairId` — so clearing the *earlier* occupant leaves a wash in the
  other pair's colour, and clearing the *later* one strips the wash while the earlier
  pair's bars are still drawn.
- `CanSelectToAdd`'s "this cell already belongs to my pair" guard
  (`GamePlayController.cs:526`) reads `block.HighlightedPairId` — unreliable on a shared
  cell for anyone but the last occupant.
- `HighlightSelectedColorTypeBlock` (`:436`) resolves which pair you grabbed from
  `HighlightedPairId`, so tapping a shared cell mid-path picks the last occupant, not
  necessarily the one under the finger.

This is exactly the "list of `(pairId, colorType)` entries" that `EXPANSION_PLAN.md` Step 9
called for and that never landed. It is the prerequisite for the bridge, and it is a
correctness fix for a mechanic already in the build.

*→ Closed by Step 2 (S2.1–S2.5b).*

**2. The arrow's `prevDir` does not exist as tracked state.** The doc says
`GamePlayController` "already tracks `prevDir`". It does not — `ProcessBlockStep` *computes*
`oldEntryDir` on the fly from `selectedBlocks[Count-2]` (`:608-655`). `GetDirection` is a
pure two-block function with no idea how the path arrived, so `CanExit` cannot be consulted
from inside it. The check has to sit at the call sites that own `selectedBlocks`:
`OnPointerMoved`'s adjacent-step branch *and* its `GetStraightLinePath` interpolation loop
(`:221-288`). Cheap, but a different edit than the doc implies.

**3. `CanEnterFrom` does not check `blockType`.** It tests `requiredEntryDirection` alone
(`Block.cs:277`). Any cell with an authored `requiredEntryDirection` behaves as a one-way,
whatever its `blockType` — but only a cell whose `blockType` is `OneWay` draws the green
bar. Mis-author the column and you get an invisible rule. No shipped level does this;
nothing prevents it.

**4. A rule cell with `pairId` 0 fails silently, three different ways.** `ValidateLevelPairs`
(`:93`) only counts *dots*, so it never looks at `Checkpoint`/`ForbiddenForPair`/`Gate`
cells. Forget the `pairId` and: a checkpoint is never enforced (`cell.PairId == pairId`
never matches a real pair id), a forbidden cell never blocks anyone, and a **gate locks
permanently** — `IsPairSolved(0)` can never be true because `completedPairs` is only ever
keyed by a dot's id. The last one makes a board unsolvable with no error logged.

**5. The wall/one-way image collision is worse than "fighting over one image".** `SetBlock`
draws wall bars first, then overwrites the same `wallImages[idx]` with one-way green
(`Block.cs:147-171`). A cell that is `OneWay` *and* walled on that same edge renders green
— it reads as "enter here" while `GetDirection` still refuses the crossing. The wall rule
survives; only its art is eaten.

*→ Closed by Step 1 (S1.2): the wall keeps the shared image, and S3.2 will reject the
combination outright.*

**6. Gate re-evaluation is O(pairs × grid) per selection check.** `CanEnter` → `IsPairSolved`
→ `IsPathFullyComplete` → `PathSatisfiesCheckpoints`, which scans the whole grid, plus
`path.Contains` linear scans. Called for every gate cell on every drag step, and
`GetPairCompleteCount` re-runs the same work for every pair on every pointer release.
Invisible at 8×8 with 7 pairs; it is the wrong shape to hand a solver or a bigger board.

**7. Wall authoring convention is not what shipped content does.** The doc recommends
authoring both sides of an edge. `GetDirection` accepts either side, and both shipped
walls (`Level_6`, `Level_11`) are authored one-sided. Either normalise at load or drop the
recommendation — right now the doc and the content disagree.

Also confirmed exactly as documented, for the record: the invisible `rgb(0.05)` wall bar
against a pure-black cell background (prefab `Background` is `rgb(0,0,0)`, *→ closed by
S1.1*), `Mixed` drawing no art at all (*→ closed by S1.3*), the `(PairColorType)pairId` cast in `ShowSpecialMarker`, `PathHighlightAlpha`
= 0.2, `CommitFillDuration` = 0.18, `GridLineWidth` = 2, and the absence of any
solvability check or undo.

---

## 2. Plan

Eight steps. Steps 1–4 are fixes and content on what already exists; steps 5–8 are the new
mechanics in the doc's recommended order. Each step is shippable and play-testable alone.

Every step lists the concrete edit sites. Where a step needs new prefab children, that is
called out — `Block.prefab` currently has exactly `LengthLabel`, four `*DirectionImage`s,
four `*WallImage`s, `Background`, `BgHighlight`, `DotImage`, `SpecialMarkerImage`, and
nothing else, so any new visual is a prefab change plus a `[SerializeField]`.

---

### Step 1 — Make the invisible rules visible

Two shipped mechanics that players cannot see, plus the art collision behind them.

**Changes**

- `Block.SetBlock` (`Block.cs:147-171`): raise the wall bar from `rgb(0.05)` to something
  legible on black — a mid grey around `rgb(0.45)`, or keep it dark and add a light rim.
  Pick one and use it for every wall.
- Give `OneWay` its own art instead of borrowing `wallImages`: add a
  `[SerializeField] private Image oneWayMarkerImage` (a chevron in the cell, pointing in
  `requiredEntryDirection`) and stop writing green into the wall bar. This removes delta 5
  outright rather than forbidding the combination in authoring.
- Add a `Mixed` visual: a new `[SerializeField] private Image mixedMarkerImage`, drawn in
  `SetBlock`'s type switch. A dashed or double ring reads as "shared" and stays distinct
  from the square/diamond `specialMarkerImage`. Reserve the crossing-bars look for the
  bridge in Step 6 — the two must not look alike.
- `Block.ResetBlock` (`:638`): deactivate both new images, or a pooled cell carries them
  into the next level.

**Verify.** Play `Level_6` (wall) and `Level_7` (one-way) and confirm both rules are
visible before you hit them. Play `Level_10` and confirm the mixed cell is identifiable
without knowing the level data. Author one throwaway cell that is both `OneWay` and walled
on the entry edge and confirm both marks now render.

**Risk.** Cosmetic only, no logic touched. Prefab edits need care with pooling — every new
image must be reset in `ResetBlock`.

---

### Step 2 — Per-pair occupancy on `Block` (delta 1)

The correctness fix for `Mixed`, and the prerequisite for the bridge. Nothing new for the
player except that the shared cell stops lying about who is on it.

**Changes**

- `Block.cs`: replace the single `highlightedPairId` / `highlightedColorType` pair with a
  small occupancy list — two entries is enough for every mechanic in this document
  (`Mixed`, bridge) so a fixed-size `(int pairId, PairColorType color)` array of 2 with a
  count avoids per-cell allocation. Keep `HighlightedPairId` as a property returning the
  *most recent* occupant so existing call sites keep compiling, and add
  `bool IsOccupiedBy(int pairId)` plus `int OccupantCount` for the new logic.
- `HighlightBlockDirection` (`:434`): register the occupant instead of overwriting.
- `HighlightBlockBg` (`:598`): when there are two occupants, decide the shared-cell look
  once — simplest honest option is to skip the wash entirely on a multi-occupant cell and
  let the direction bars carry the colour, since two 0.2-alpha washes cannot both show.
- `ResetAllHighlightDirection(int pairId)` (`:538`): drop that pair's occupancy entry;
  clear the wash only when the last occupant leaves.
- `CanSelectToAdd` (`GamePlayController.cs:526`) and `HighlightSelectedColorTypeBlock`
  (`:436`): ask `IsOccupiedBy(...)` rather than comparing against a single
  `HighlightedPairId`.
- `Block.ResetBlock` (`:638`): clear the occupancy list.

**Verify.** On `Level_10`: draw pair 4 through (4,4), then pair 3 through it, and confirm
both render. Tap pair 4's dot to clear it and confirm pair 3's bars *and* its wash survive
untouched; then the reverse order. Re-play `Level_1`–`Level_9` to confirm single-occupant
cells behave exactly as before.

**Risk.** Touches the drag/select hot path, which `KNOWN_ISSUES.md` already documents as
fragile. Keep `HighlightedPairId`'s old meaning intact so the blast radius stays inside the
call sites listed above.

---

### Step 3 — Authoring safety net

The audit's delta 3, 4 and 7 are all "level data can be wrong and nothing says so". Cheap
to close, and it has to exist before anyone authors the pack in Step 4.

**Changes**

- Rename/extend `ValidateLevelPairs` (`GamePlayController.cs:93`) into a full level-data
  validation pass, still logging errors rather than throwing. Add:
  - every `Checkpoint` / `ForbiddenForPair` / `Gate` cell must have a non-zero `pairId`
    that exists as a real dot pair on this board (catches delta 4, including the
    permanently-locked gate);
  - `requiredEntryDirection` set on a cell whose `blockType` is not `OneWay` is an error
    (catches delta 3) — or make `CanEnterFrom` check `blockType` and treat the column as
    ignored elsewhere. Pick one; validating is the smaller change.
  - a `PairConstraint` whose `pairId` is not on the board, or whose `requiredPathLength` is
    shorter than the pair's Manhattan distance + 1, is unsatisfiable.
- Add a reachability check: for each pair, flood-fill from one dot over cells that pair
  *could* legally occupy (not `Blocked`, not `ForbiddenForPair` for it, walls respected)
  and error if the other dot is unreachable. This is a lower bound, not a solver — it
  cannot see pair-vs-pair contention — but it catches the whole class of "one blocked cell
  too many" mistakes for free.
- Decide the wall convention (delta 7): either normalise both sides at load in
  `BoardGenerator.GenerateBoard` (`:49`) after the grid is populated, or state one-sided
  authoring as the convention and fix `PUZZLE_MECHANICS.md` §2.2. Normalising is better —
  it makes level data readable in either direction.

**Verify.** Deliberately break a copy of `Level_9` (blank the gate's `pairId`) and confirm
an error is logged at generate time. Confirm all 12 shipped levels validate clean.

**Risk.** None to gameplay — logging only. Do not let the reachability flood-fill grow into
a solver here; that is a separate decision (§4).

---

### Step 4 — Author a real pack on the shipped mechanics

The highest difficulty-per-hour in this document, and it tells you which new mechanics are
actually needed. Eight mechanics currently have one demo cell each and one length
constraint in the whole game.

**Changes** (content, not code, except one UI kindness fix)

- Author a pack that *combines*: gate + checkpoint on the dependency pair; exact length +
  checkpoint; forbidden cells shaping which pair takes a shared lane; walls doing the
  routing work instead of blocked cells. `PUZZLE_MECHANICS.md`'s "Combos" lines are the
  brief.
- Add the live length readout the doc asks for: `Block.lengthLabel` currently shows a
  static required count set once in `SetBlock` (`:120`). Show `current / required` for the
  pair being dragged, updated from `ProcessBlockStep` and the retreat branch in
  `OnPointerMoved`. UI-only, no rule change, and it turns exact-length from guesswork into
  a puzzle.
- Note the ordering dependency while you are in here: `SetLevelConstraints` must run before
  blocks are created, because `SetBlock` reads `GetRequiredPathLength` to build the label.
  `BoardGenerator` does this correctly (`:59` before the loop at `:61`) — worth a comment
  so a future reorder does not silently blank every label.

**Verify.** Play the pack cold. Any level you can solve without noticing its mechanic is a
level that does not need the mechanic.

**Risk.** None technical. This step is where you find out whether steps 5–8 are worth
building at all.

---

### Step 5 — Arrow (`BlockType.Arrow`)

First new mechanic. One new predicate, and it is the bridge's prerequisite.

**Changes**

- `BlockType.cs`: add `Arrow = 7`. Replace the "deliberately NOT modeled" comment on
  `OneWay` with a pointer to the new value, and keep the turn-only/straight-only family
  closed.
- `LevelData.cs` `GridRow` (`:17`): add `public Direction[] forcedExitDirection`, optional
  like every other column. Do **not** reuse `requiredEntryDirection` — a cell that is both
  entry- and exit-constrained is a legitimate thing to author later, and delta 3 shows what
  happens when one column means two things.
- `BoardGenerator.GenerateBoard` (`:82-92`): read the new column with the same
  null/length-guarded fallback as the others, and pass it to `SetBlock`.
- `Block.cs`: store `forcedExitDirection`; add
  `bool CanExit(Direction entryDir, Direction exitDir)` returning true for every non-arrow
  cell. For an arrow: `exitDir == forcedExitDirection`, and refuse entry that would force
  an immediate reversal (`entryDir == OppositeDirection(forcedExitDirection)`) — the doc's
  chosen resolution. Treat `entryDir == Direction.None` (path starts here, or resumes here
  after a mid-path reconnect) as unconstrained on entry but still forced on exit.
- `GamePlayController.OnPointerMoved` (`:221-288`): consult `CanExit` where the entry
  direction is known — the adjacent-step branch and the `GetStraightLinePath` loop both.
  `GetDirection` (`:551`) stays a pure two-block function; do not push this into it.
- `ProcessBlockStep` (`:608`): after committing a step onto an arrow, immediately commit the
  forced step out of it, reusing the same multi-cell path the fast-swipe interpolation
  already walks, so the stroke visibly continues through in one motion. Refuse the whole
  entry when the forced exit is illegal (off-board, walled, `CanEnter` false, or already in
  the path) rather than parking the path on a cell it cannot leave.
- `OnPointerUp`'s below-50%-fill undo (`:323-360`): the released cell may be the *forced*
  half of an arrow step. Undo the arrow entry and its forced exit together, or the path is
  left sitting on an arrow it never left.
- Prefab: an arrow glyph child image, neutral white, rotated per direction.
  `FreeButtonSet/Textures/Icons` has directional arrows. Reset it in `ResetBlock`.
- Step 3's validator: an `Arrow` cell with `forcedExitDirection == None` is an error.

**Verify.** Author a 5×5 with one arrow mid-corridor. Confirm: entering with the arrow
carries the stroke straight through in one motion; entering against it is refused; entering
across it exits the printed way, not the way you were heading; a fast swipe across the
arrow behaves the same as a slow drag; releasing the pointer exactly on the arrow leaves a
consistent path. Then arrow + wall, and arrow + `Mixed`.

**Risk.** Moderate — this is the first mechanic that *writes* to the path rather than
refusing a step, and it lands in the drag code `KNOWN_ISSUES.md` warns about. The forced
step interacting with the 50% undo rule and with mid-path reconnect is where bugs will be.

---

### Step 6 — Bridge (`BlockType.Bridge`)

`Mixed` plus an axis lock plus no turning. Needs Step 2 (real occupancy) and Step 5
(`CanExit`).

**Changes**

- `BlockType.cs`: add `Bridge = 8`. No new level-data column — the axis is whatever each
  occupant used.
- `Block.cs`: record the axis each occupant entered on, alongside the Step 2 occupancy
  entry. `CanEnter` refuses a second occupant on an axis already taken. `CanExit` on a
  bridge requires `exitDir == entryDir` (straight through, no turns) — the same predicate
  the arrow introduced, with a different rule inside it.
- `ProcessBlockStep` (`:608`): the `BlockType != Mixed` steal exemption (`:611`) must cover
  `Bridge` too. Prefer a `Block.IsShareable` property over a growing enum comparison — this
  is the second mechanic to need it and the splitter will be the third.
- Prefab/art: two crossing bars, one drawn over the other, so over/under is in the art and
  not in a rule the player memorises. Must not resemble Step 1's `Mixed` ring.
- Step 3's validator: nothing new required, but a bridge that no pair can cross straight
  (walls or blocked cells on both axes) is worth flagging.

**Verify.** Author a 5×5 with one bridge and two pairs that must cross on it. Confirm: both
cross; neither can turn on it; a third pair cannot take an occupied axis; clearing one pair
leaves the other's crossing intact (this is the Step 2 fix being load-bearing); and the
over/under art matches which pair actually went over.

**Risk.** Low-moderate given Steps 2 and 5. The completion check must verify each pair
passes through independently rather than treating the cell as owned — if Step 7's
connectivity refactor lands first, that part is free.

---

### Step 7 — Splitter (`BlockType.Splitter`) and connectivity-based completion

The deepest change here, and the one that pays for itself by making completion honest.

**The blocker the doc understates.** `AddSelectedBlocksToCompletedPairs` (`:993`) is
`completedPairs[selectedBlocks[0].PairId] = new List<Block>(selectedBlocks)` — one list per
pair id, wholesale replaced. A second branch of the same pair does not add, it *overwrites*.
So the splitter is not "a completion-rule change"; it is a change to what `completedPairs`
stores.

**Changes**

- `completedPairs` (`:24`) becomes a per-pair record holding a *set of segments* rather than
  one list — `Dictionary<int, List<List<Block>>>` at its simplest, or a small `PairPaths`
  type if the call sites get noisy. Every current consumer moves with it: `OnPointerDown`'s
  clear-on-dot-tap (`:152`) and resume-from-middle (`:175`), `ProcessBlockStep`'s steal
  resolution (`:611`), `GetPairCompleteCount` (`:1003`), `IsPairSolved` (`:1022`).
- Completion becomes connectivity: replace `IsPathFullyComplete(List<Block>)` (`:1063`) —
  which today just checks `IsPairComplete(path[0], path[last])`, i.e. positional endpoints —
  with `IsPairSatisfied(int pairId)`: all of that pair's dots lie in one connected component
  of the union of its segments, then checkpoints, then length. Two-dot pairs are the trivial
  case, so nothing regresses; `IsPairComplete(Block, Block)` (`:1049`) stops being the
  definition of "solved" and goes back to being a cheap endpoint helper.
- `BlockType.cs`: add `Splitter = 9`. Level data: a pair whose `pairId` appears three times.
- Step 3's validator learns about splitter pairs: exactly 2 occurrences *unless* the pair
  has a splitter junction, then 3. This is the assertion the doc flags as breaking.
- Undo/reconnect: tapping one of three dots clears that dot's branch only. Natural with
  per-segment storage, impossible without it — which is the argument for doing this properly
  rather than special-casing.
- Constraint semantics: decide whether `requiredPathLength` is per-pair or per-branch. The
  doc's suggestion (per-branch, once connectivity lands) is the better puzzle; it needs a
  `PairConstraint` shape that can name a branch.
- Prefab/art: a three-stub junction, distinct from both the `Mixed` ring and the bridge's
  crossing bars.

**Verify.** Re-play all 12 shipped levels first — this refactor touches every completion
path and must be invisible on two-dot pairs. Then author one 5×5 with a single three-dot
pair and confirm: the pair completes only when all three dots connect; clearing one branch
leaves the others; the pair counter and level-complete goal still tick correctly.

**Risk.** Highest in this document. Do it as its own change with nothing else in flight, and
lean on the 12 existing levels as the regression suite.

---

### Step 8 — Rotator (`BlockType.Rotator`)

Last, because it is the only mechanic that adds an input verb, the only one with runtime
board state, and the only one that would complicate a future solver.

**Changes**

- `BlockType.cs`: add `Rotator = 10`. Level data: an initial rotation (0–3) — a new
  `int[] initialRotation` column, or reuse `wallMask`'s slot pattern.
- `Block.cs`: a `currentRotation` field that is runtime state, never written back to level
  data, cleared in `ResetBlock` (`:638`) so a pooled cell cannot carry it into the next
  level. Allowed directions derive from the rotation; entry/exit is then the same shape as
  the arrow — `CanEnterFrom` for entry, `CanExit` for exit.
- Tap input: `OnPointerDown` (`:138`) currently assumes a press starts a path. Handle a
  press on a rotator by rotating and returning without setting `isClicked`. Note `Update`
  (`:114`) dispatches on `GetMouseButtonDown`/`GetMouseButton`/`GetMouseButtonUp`, so a tap
  is a Down with no movement — an early return in `OnPointerDown` is the whole change. The
  `touchPointer.raycastTarget = false` care in `Start` (`:68`) is what makes the tap reach
  the `Block` at all; do not disturb it.
- Path re-validation on rotate: clear the affected pair's path from the rotator onward —
  cheaper and more predictable than repairing it. With Step 7 landed this is "drop the
  segment from this cell on"; before Step 7 it is a partial `ResetBlockToRemove` (`:953`).
- Move counting: `moves++` happens once per committed drag in `OnPointerUp` (`:349`). If a
  rotation counts as a move, increment there too — this is a design decision, see §4.
- Prefab/art: an elbow in the two connected directions, in a colour reserved for
  "tappable board furniture" so players learn the convention. Must not read as a path bar.

**Verify.** Author a 5×5 with two rotators in series. Confirm: tapping rotates and does not
start a path; a path drawn through a rotator is cleared from that cell on when it rotates;
the rotation resets when the level reloads (play, rotate, reset, confirm the initial
rotation is back); rotations count as moves if that is the decision.

**Risk.** Contained, but genuinely new. The tap-vs-drag disambiguation is the part most
likely to annoy players — a rotator adjacent to a dot is where to test it.

---

## 3. Dependencies and order

| Step | Depends on | Why |
|---|---|---|
| 1 Visual gaps | — | independent |
| 2 Occupancy | — | fixes shipped `Mixed`; prerequisite for 6 |
| 3 Validation | — | wanted before 4; every later step adds rules to it |
| 4 Content pack | 1, 3 | authoring invisible rules is pointless; validation catches the mistakes |
| 5 Arrow | 3 (for the new column's validation) | introduces `CanExit` |
| 6 Bridge | 2, 5 | needs real occupancy and `CanExit` |
| 7 Splitter | — technically; 6 benefits | rewrites `completedPairs` and completion |
| 8 Rotator | 5 (`CanExit`), 7 (clean path clearing) | new input verb, new runtime state |

Steps 1, 2 and 3 are independent of each other and can go in any order or in parallel.
Steps 4 onward are sequential as listed.

---

## 4. Decisions needed before building

Four of these change the work, not just the polish. They are worth settling now.

- **Is a solver coming?** Step 3's reachability check is a lower bound, not solvability.
  Hand-authored content survives that; procedural levels cannot, and the rotator (Step 8)
  makes the search space include board state. If the answer is yes, build it before Step 8;
  if no, say so in the doc and accept hand-checking.
- **Does a rotation count as a move?** Determines whether move budgets or star ratings can
  ever sit on top. `PUZZLE_MECHANICS.md`'s lab demo counts it; the code counts one move per
  committed drag.
- **Per-pair or per-branch length constraints** for the splitter (Step 7). Per-branch is the
  better puzzle and needs a `PairConstraint` that can name a branch.
- **`(PairColorType)pairId`** in `ShowSpecialMarker` (`Block.cs:221`). Pair ids ≥ 10 are
  legal now; `GetColor` (`:915`) falls back to `Color.black` for an unknown enum value, so a
  checkpoint or forbidden marker for pair 10+ renders black on black. One lookup table, and
  it must land before any level authors more than 9 pairs — which Step 4 might well want.

## 5. Out of scope

- **Turn-only / straight-only cells** — closed, per `PUZZLE_MECHANICS.md`. The arrow covers
  the design need and reads on a board; these do not.
- **Teleport, portal, ice, colour-switch** — in `FEATURE_ROADMAP.md`'s candidate list, not
  in this plan. Each needs its own design pass; none is a prerequisite for anything here.
- **Key / breakable / timer / pressure plate** — all variations on the gate's cross-pair
  state query. Cheap to add once one of them has a designed trigger condition; nothing in
  this plan blocks them.
- **Undo** — no undo exists, only reconnect-by-tapping-a-dot. Step 7 makes per-branch
  clearing natural, which is most of what an undo stack would need. Worth deciding after
  Step 7 rather than before.
- **Level-select virtualization and level-data splitting** — `EXPANSION_PLAN.md` Steps 3–4,
  unrelated to mechanics.

---

## 6. Execution tracker

The step-by-step build order, broken into tasks small enough to do and verify one at a
time. **Scope: all twelve mechanics in `PUZZLE_MECHANICS.md`** — the eight shipped (two of
which need fixes) plus the four proposed. `FEATURE_ROADMAP.md`'s remaining candidates
(teleport, portal, ice, colour-switch, key/breakable/timer/pressure-plate) stay out of
scope per §5; say the word and they get their own plan.

**Current position: all eight steps landed, nothing played. Next task: the play-tests (S1.5/S1.6, S2.7, S4.4, S5.9, S6.7, S7.8, S8.8), then S4.1/S4.6 content.**

### Division of labour

Worth settling before we start, because it decides how each task ends:

| Work | Who | Notes |
|---|---|---|
| `.cs` edits | Claude | all logic, all of `Block`/`GamePlayController`/`BoardGenerator`/`LevelData` |
| Level `.asset` authoring | Claude | the level YAML stores int arrays as little-endian hex strings; they are scriptable, so new levels and test levels can be written directly rather than clicked into the Inspector |
| `Block.prefab` new children | Claude drafts, user confirms | the prefab is text and editable, but new `Image` children plus their `[SerializeField]` wiring are much safer eyeballed in the Editor once |
| Sprite choice | User | arrow glyph, mixed ring, bridge bars, rotator elbow — art calls, not code calls |
| Play-testing | User | no Unity MCP server is connected to this session and there is no test suite, so every "verify" below is you in the Editor |

If a Unity MCP connection is available later, the play-test tasks can move to Claude; until
then treat each step as "Claude lands the code, you play it before we move on".

### Step 1 — Make the invisible rules visible

| | Task | Done when |
|---|---|---|
| S1.1 | ✅ Wall bar raised to `WallColor` = `rgb(0.45)` in `Block.SetBlock`'s wall loop | a wall is visible against the black cell background |
| S1.2 | ✅ Wall/one-way edge collision resolved: the wall keeps the shared image, since that cell is unenterable either way | the art can no longer advertise an opening that does not exist |
| S1.3 | ✅ `Mixed` draws a neutral ring — `ShowMixedMarker` swaps `mixedMarkerSprite` onto the shared marker | `Mixed` is identifiable without reading level data |
| S1.4 | ✅ `ResetBlock` restores the marker's sprite, scale and tint | a pooled cell carries nothing into the next level |
| S1.5 | Confirm in the Editor: `mixedMarkerSprite` = `CIRCLE4PXLAR` (pre-wired in the prefab), and the marker rect now scales with the cell (anchors 0.25–0.75, sizeDelta 0) instead of a fixed 100×100 | Inspector matches; checkpoint/forbidden markers still look right at 4×4 and 8×8 |
| S1.6 | Play the wall, one-way and shared-cell levels | every rule is visible before you hit it |
| S1.7 | ✅ *(found by play-testing)* Wall/one-way bars given real per-edge geometry — see below | a wall reads as one rectangle between two cells |

**S1.7, and a correction to §1.** Play-testing `Level_2` showed a wall as **two grey boxes, one
inside each cell**. The cause was not the normalisation that makes both cells draw it: the four
`wallImages` in `Block.prefab` were never edge bars at all — all four were identical 100×100
squares centred in the cell. The audit read `wallImages[(int)Direction - 1]` in the code and took
"edge bar" at face value; I never checked the RectTransforms, and §2.2 of
[`PUZZLE_MECHANICS.md`](PUZZLE_MECHANICS.md) described intent as if it were fact.

Each bar is now anchored to its own edge and centred *on* the boundary, so the two cells' copies
coincide and a wall reads as one rectangle in the gap — which is also what makes the mechanic look
like what it is, an edge that cannot be crossed rather than a thing sitting in a cell.
`ApplyWallGeometry` sets thickness proportionally (10% of the cell) and re-runs on every resize,
alongside the direction bars. The one-way marker shares these images, so its "edge bar" — until now
a green square in the middle of the cell — is fixed by the same change.

**Deviation from the original S1.2.** The plan said give `OneWay` its own image — a centred
chevron. Dropped: the arrow mechanic (Step 5) needs the centred arrow glyph, and two centred
directional glyphs meaning different things (entry constraint vs forced exit) is exactly the
readability problem this step exists to fix. One-way keeps its edge bar; the collision is
resolved by precedence instead, and S3.2 will reject the combination outright.

### Step 2 — Per-pair occupancy on `Block` (fixes shipped `Mixed`)

| | Task | Done when |
|---|---|---|
| S2.1 | ✅ Single `highlightedPairId`/`highlightedColorType` replaced by a 2-slot occupancy list; `HighlightedPairId`/`HighlightedColorType` now derive from the most recent occupant, so no existing caller changed meaning on a single-occupant cell. Added `IsOccupiedBy`, `OccupantCount`, `GetOccupantPairId`, `GetOccupantColorType` | two slots is the real ceiling: a committed path always owns ≥2 of the 4 direction slots, so a third occupant has nowhere to draw |
| S2.2 | ✅ `HighlightBlockDirection` calls `AddOccupant` instead of overwriting | second occupant no longer erases the first |
| S2.3 | ✅ `RefreshPathWash` is the single decision point — nobody: nothing; one: that pair's colour; two: nothing. `HighlightBlockBg` carries the same guard, since `OnPointerUp` washes committed cells directly | a shared cell no longer claims to belong to whoever committed last |
| S2.4 | ✅ `ResetAllHighlightDirection(int)` removes the occupant unconditionally and re-derives the wash | clearing either pair leaves the other's bars *and* wash intact |
| S2.4b | ✅ *(not in the original list)* `ResetHighlightDirection` drops a pair's occupancy once it owns no bar here | a retreating drag no longer leaves the cell claiming that pair is still on it |
| S2.5 | ✅ `ResolveGrabbedPairId` prefers the occupant whose path *ends* on the pressed cell, falling back to most-recent; `OnPointerDown`, `HighlightSelectedColorTypeBlock` and the touch-pointer colour all use it | grabbing a shared cell extends the path you actually grabbed |
| S2.5b | ✅ *(not in the original list)* `GetCurrentDragColorAndPairId` reads the pair off `selectedBlocks[0]` — always a dot — instead of the last block's occupant identity | removes the last ambiguous read; **the one behaviour-equivalence claim worth play-testing** (resume-from-middle and resume-from-last) |
| S2.6 | ✅ `ResetBlock` clears occupancy via the blanket reset | no leak across pooled levels |
| S2.7 | Regression play `Level_1`–`Level_9`, then `Level_10`/`Level_12` clearing each pair first | single-occupant behaviour unchanged; crossings survive either clear order |

`IsOccupiedBy` has no caller yet — it is the accessor Step 6's axis rule needs, and the
honest way to ask the question everywhere `HighlightedPairId` is currently used as a proxy.

### Step 3 — Authoring safety net

| | Task | Done when |
|---|---|---|
| S3.1 | ✅ New [`LevelValidator`](../Assets/Script/GamePlay/LevelValidator.cs) replaces `ValidateLevelPairs`; `GamePlayController.ValidateLevelData` hands it the board. Rule cells must name a pair that has dots, must not be dots themselves, and `pairId` 0 is an error | a blanked gate `pairId` logs an error instead of locking forever |
| S3.2 | ✅ Errors on `requiredEntryDirection` on a non-`OneWay` cell, on a `OneWay` with no direction, and on a `OneWay` whose only entry edge is walled | delta 3 caught at generate time; the S1.2 collision is now rejected, not just drawn safely |
| S3.3 | ✅ `PairConstraint` sanity: pair exists, length ≥ Manhattan + 1, and same parity — every detour costs two cells, so an even/odd mismatch is unsatisfiable however the player routes it | an unsatisfiable length is caught before play |
| S3.4 | ✅ Directed per-pair flood fill honouring walls, blocked cells, one-way entry and other pairs' dots; gates count as passable, since a gate's job is to open. Checkpoints must also be reachable | an over-blocked board logs an error |
| S3.5 | ✅ Walls normalised at load: `BoardGenerator.NormalizeWalls` mirrors a one-sided authored wall onto the neighbour via the new `Block.AddWall` | a wall now looks solid from both sides, however it was authored |
| S3.6 | ✅ All eight levels validate clean; four deliberately broken boards (gate with no `pairId`, stray `requiredEntryDirection`, wrong-parity length, sealed pair) each produce the expected error | no false positives on real content, no false negatives on broken |

**Verified without Unity.** [`Tools/validate_levels.py`](../Tools/validate_levels.py) mirrors
`LevelValidator`'s checks against the assets on disk, so level data can be checked before the
Editor ever opens. It stands in for the test suite this project does not have — it validates
the *data*, not the C#, so the Editor console is still the real confirmation that the two
agree.

### Step 4 — Author a real pack on the shipped mechanics

| | Task | Done when |
|---|---|---|
| S4.1 | Live `current / required` readout on `lengthLabel`, driven from `ProcessBlockStep` and the retreat branch | exact-length stops being guesswork |
| S4.2 | Comment the `SetLevelConstraints`-before-`SetBlock` ordering dependency in `BoardGenerator` | a future reorder can't silently blank every label |
| S4.3 | ✅ **Level set replaced.** The twelve original levels (assets + metas) deleted; twelve new 5×5 levels authored, one mechanic each, `totalLevelCount` 12 → 12 | every mechanic has a level that cannot be solved by ignoring it |
| S4.4 | Play all twelve cold | each mechanic teaches itself without explanation |
| S4.5 | ✅ **Mechanic named on screen.** New `MechanicText` row on `GameplayScreen`, `UIController.DescribeMechanics` derives the name from the level data itself | the label cannot disagree with the board |
| S4.6 | Author the *combining* pack — gate + checkpoint, length + checkpoint, forbidden shaping a shared lane | difficulty from dependencies, per §4's through-line |

**The current level set.** Twelve boards, every one 5×5 with two pairs, and every one was hand-verified
solvable — there is no solver, so the intended route is recorded here as the evidence. Pair 1
is red, pair 2 is blue; coordinates are (row, col) with row 0 at the top.

| # | Mechanic | The cell | Why the mechanic is unavoidable |
|---|---|---|---|
| 1 | Blocked cell | `Blocked` (0,2) | Pair 1 owns the top row and has to dip into row 1 and come back up |
| 2 | Wall | edge (2,1)–(2,2) | Pair 1's own row is cut in half; it detours through row 3 |
| 3 | One-way | `OneWay` (2,2), entry Down | Pair 2 crosses it vertically, which is the one legal approach; pair 1 travelling sideways cannot use it at all |
| 4 | Forbidden cell | `ForbiddenForPair` 1 at (2,2) | The same cell that stops pair 1 lets pair 2 straight through — the rule is per-pair, and the board says so |
| 5 | Shared cell | `Mixed` (2,2) | Both pairs must cross the middle. Without a shared cell this board is a deadlock, which is the entire point |
| 6 | Checkpoint | `Checkpoint` 1 at (4,2) | Pair 1's dots are three cells apart on one row, but it is not complete until it has been to the bottom row |
| 7 | Exact length | `requiredPathLength` 9 on pair 1 | Straight across is 5 cells, so the player has to deliberately take the long way |
| 8 | Gate | `Gate` 2 at (0,1) **and** (1,0) | Two gates seal pair 1's corner dot, so pair 2 must be solved first. A single gate anywhere on an open board is just walked around — sealing a dot forces the order without borrowing a second mechanic |
| 9 | Arrow | `Arrow` (0,1) → Right **and** (1,0) → Down | Both cells next to pair 1's dot are arrows, so every route out is one the board chooses. Coming back through one is refused outright — an arrow cannot be entered head-on |
| 10 | Bridge | `Bridge` (2,2) | **Level 5's board with one token changed.** Both pairs still have to cross the middle, but now each may only pass straight through — the strict version of sharing, next door to the permissive one so they can be compared |
| 11 | Splitter | `Splitter` 1 at (2,2), pair 1 with **three** dots | Pair 1 branches three ways and is complete only when all three dots reach the junction. Pair 2 keeps to the far column so the branching is the only thing to think about |
| 12 | Rotator | `Rotator` (0,1) at r=0 **and** (1,0) at r=1 | Neither elbow starts joined to pair 1's corner dot, so **the board has to be turned before a path can leave at all**. Verified by simulation: every rotation of both cells refuses entry until the player taps |

Two pairs of levels are deliberately the same board with one cell changed: 3 and 4 separate
"nobody may enter this way" from "this pair may not enter at all", and 5 and 10 separate sharing
freely from sharing on strict terms.

The level assets are generated from an ASCII spec in
[`Tools/make_levels.py`](../Tools/make_levels.py) rather than clicked into the Inspector —
the `coloum`/`pairId`/`blockType`/`wallMask`/`requiredEntryDirection` columns are serialised as
little-endian packed hex, which is unreadable by hand but trivial to write from a script.
Re-authoring a level means editing the spec and regenerating, not hand-editing YAML.

**Stale save data.** `SaveData.completedLevel` can still say 12, which shows all eight levels
as already completed. Nothing breaks (`SaveLevelData` only ever grows its array and indexes
within it), but for a clean run delete
`C:\Users\<user>\AppData\LocalLow\DefaultCompany\Free Flow\SaveData.json`.


### Step 5 — Arrow

| | Task | Done when |
|---|---|---|
| S5.1 | ✅ `BlockType.Arrow = 7`; the "deliberately NOT modeled" comment now documents why turn-only/straight-only stay closed and the arrow does not | enum documents the decision |
| S5.2 | ✅ `GridRow.forcedExitDirection` column + `BoardGenerator` read + `SetBlock` parameter — its own column, not a second meaning for `requiredEntryDirection` | authorable, defaults inert |
| S5.3 | ✅ `Block.CanExit(Direction)` (exit only — the entry half belongs in `CanEnterFrom`, which now refuses a head-on arrow) | predicate checkable by eye, inert on every other type |
| S5.4 | ✅ New `CanTakeStep` gates both `OnPointerMoved` branches — the adjacent step and the `GetStraightLinePath` interpolation | fast swipe and slow drag behave identically |
| S5.5 | ⚖️ **Reversed after play-testing.** `ArrowChainIsLegal` still refuses entry when the forced exit cannot be taken, but the exit is no longer committed for the player — see below | path never parks on a cell it can't leave |
| S5.6 | ⚖️ `UndoLastStep` stays (it is used by the rotator too), but the arrow-chain unwind is gone with the auto-commit: a release on an arrow is now an ordinary release | release-on-arrow leaves a consistent path |

**S5.5 reversed: the arrow no longer takes its own exit.** The spec asked for the forced step to
commit on entry "so the path visibly continues through the arrow in one motion". It plays badly and
it cost three bugs, each a different consequence of one assumption the drag loop makes everywhere:
**the head of the path is the cell under the finger.**

| Symptom | Which part of that assumption broke |
|---|---|
| gap in the line at a one-way (`Level_3`) | `GetDirection` re-asked the rules about a step already taken |
| forced cell's bar flashing and vanishing | the fill was driven by a pointer no longer at the head |
| forced cell committed and undone every frame | a finger still on the arrow was read as *pulling back* |

I fixed the first two as one-offs and the third as another one-off, and each fix needed more state
to prop up the flourish (`forcedPastBlock`, `forcedHeadBlock`, a snapped fill, two preview gates).
That is the signal that the flourish was wrong, not the loop.

Dropping it costs the rule nothing: `CanExitFrom` already refuses every direction but the printed
one, so a path on an arrow has exactly one legal continuation. The player makes that move; the
arrow decides which move exists. All the propping-up state is gone, a path may rest on an arrow,
and `ArrowChainIsLegal` still keeps a path off any arrow it could not leave.

*If the one-motion feel is wanted back later, it needs the drag loop to track the head and the
pointer as two separate things — a real change, not a flag.*
| S5.7 | ✅ `arrowMarkerSprite` on the shared marker, rotated by `MarkerRotationFor`; `FreeButtonSet` `arrow_up` pre-wired (verified pointing up by decoding the PNG) | rule readable with no tutorial text |
| S5.8 | ✅ Validator: no direction, arrow-on-a-dot, and a forced exit that leaves the board / crosses a wall / enters a blocked cell. Reachability is now arrow-aware — an arrow expands one way only, and head-on entry is refused | a stranded path is caught before play |
| S5.9 | ✅ `Level_9` authored (arrow); ⏳ play matrix still to run: with / against / across the arrow, fast swipe, release on it, arrow + wall, arrow + `Mixed` | all behave as designed |

**What the negative tests showed.** Arrow-off-the-board and arrow-with-no-direction both error
as intended. A third case I expected to fail — two arrows flanking a corner dot, both pointing
*away* — validated clean, and correctly: the player can draw that pair from the **other** dot,
travelling with the arrow instead of into it. That is exactly why reachability floods from both
dots, and it is worth remembering when authoring: with arrows, which end you start from is part
of the puzzle. The generator also refuses to emit an arrow with no direction at all, so that
class of mistake cannot reach an asset by the normal route.

### Step 6 — Bridge

| | Task | Done when |
|---|---|---|
| S6.1 | ✅ `BlockType.Bridge = 8` | — |
| S6.2 | ✅ Axis derived from the direction bars a pair owns (`OwnsAxis`) rather than stored separately — the bars already say which way each occupant crosses, so a second copy of that fact could only drift | axis known per occupant, no new state |
| S6.3 | ✅ `Block.CanAcceptEntry(dir, pairId)` refuses a second occupant on a taken axis. Not `CanEnter`: that one is direction-independent by design, and the axis rule needs the direction — so it sits in `CanTakeStep` alongside the arrow's chain check | one horizontal, one vertical, no more |
| S6.4 | ✅ `CanExitFrom(entry, exit)` requires straight-through on a bridge — the same predicate the arrow introduced, with a second rule inside it rather than a second predicate beside it | no turning on a bridge |
| S6.5 | ✅ `Block.IsShareable` replaces the `!= Mixed` comparison in `ProcessBlockStep` | a third shareable type will not need a third comparison |
| S6.6 | ⚠️ Crossing glyph shipped, distinct from the Mixed ring in shape *and* tint (cool vs warm). **Over/under not drawn** — see below | rule visible; depth cue outstanding |
| S6.7 | ⏳ Play: both cross, neither turns, a same-axis second pair is refused, clearing one leaves the other | S2 proven load-bearing |

**S6.6 is a partial.** The art shows a crossing, not which pair went over. Drawing that needs
two overlapping images with a per-pair z-order, and the direction bars are per-cell with no
z-ordering between pairs — so it is prefab surgery plus a change to how bars are drawn, not a
sprite swap. The rule is fully enforced; only the depth cue is missing. Filed rather than
faked.

**The exit rule got split in two, deliberately.** `CanExitFrom(entry, exit)` is the rule as a
pure function of two directions; `CanExit(exit, pairId)` is its runtime face, recovering the
entry direction from the single direction bar that pair owns here (the bar sits on the edge the
path came through). That split is what let `LevelValidator` reuse the real rule instead of
reimplementing it — and reimplementing it was the obvious trap, since the validator walks
hypothetical boards where no bars are drawn.

**The validator's reachability walk is now direction-aware.** States are (cell, arrival
direction) instead of just cell: an arrow and a bridge both decide where a path may go next from
how it got in, so a cell alone cannot answer "can I leave it". Four states per cell at worst.
This also made the arrow handling exact rather than approximate.

### Step 7 — Splitter and connectivity-based completion

| | Task | Done when |
|---|---|---|
| S7.1 | ✅ `pairSegments` is `Dictionary<int, List<List<Block>>>`, each segment identified by the dot it starts from | a second branch adds instead of overwriting |
| S7.2 | ✅ Every consumer migrated: both `OnPointerDown` branches, `ProcessBlockStep`'s steal, `ResolveGrabbedPairId`, `CanSelectToAdd`'s overlap guard, `GetPairCompleteCount`, `IsPairSolved`, `ResetGameplay` | no `completedPairs` references left |
| S7.3 | ✅ `IsPairSatisfied(pairId)` walks the segments as a graph and requires every dot in one component, then checkpoints, then length | the positional test is gone |
| S7.4 | ✅ `BlockType.Splitter = 9`; validator expects three dots for a pair with a junction, two otherwise, and checks every dot can reach that junction | the assertion the doc flagged as breaking now knows better |
| S7.5 | ✅ Tapping a dot clears the segments touching *that dot*; `ClearSegmentVisuals` un-draws bar by bar so a shared junction keeps the sibling branch's bar | per-branch undo works |
| S7.6 | ⚖️ Implemented as distinct cells of the whole figure (junction counted once) — unchanged for two-dot pairs. Per-branch lengths remain the open call | constraint shape settled *for now*, decision recorded |
| S7.7 | ✅ `splitterMarkerSprite` (three endpoints meeting) in warm gold — distinct in shape *and* tint from the Mixed ring and the Bridge crossing | three "shared cell" concepts stay distinguishable |
| S7.8 | ⏳ Regression play: all ten earlier levels first, then `Level_11` | two-dot pairs provably unaffected |
| S7.9 | ✅ *(found by play-testing)* An ordinary pair holds exactly **one** segment again — see below | drawing from the second dot replaces the first attempt |
| S7.10 | ✅ *(found by play-testing)* A pair's branches may meet on its own junction — the splitter was **unplayable** without this | branch two is no longer refused at the junction |

**S7.10 — the splitter never worked.** `CanSelectToAdd` refuses entry into any cell the same pair
already occupies, which is right everywhere except the one cell built for a pair's branches to
meet. Draw branch one to the junction, start branch two, and it was refused on arrival: the
branches could never join, so `Level_11` could not be completed by any route.

Two fixes, both narrow: the self-overlap guard now exempts a cell that is a `Splitter` naming the
dragging pair, and `Block.IsShareable` includes `Splitter` so an arriving branch is not read as
stealing the cell from a sibling. The second matters for a layout `Level_11` happens to avoid — a
dot directly beside a junction, where the branch's first step lands on the junction while its own
dot still has no bars, so the steal check would see another pair's cell and trim a sibling.

Worth noting how this got through: the validator checks every dot can *reach* the junction, and it
could. Reachability says nothing about whether the drag rules permit the meeting once two branches
are actually drawn. Static validation cannot see a rule that only bites in the second drag.

**S7.9, the regression this refactor introduced.** Play-testing `Level_2` produced a pair with two
disconnected fragments, one grown from each dot, and no way to finish it: `CanSelectToAdd` refuses
entry into your own pair's cells, so neither fragment could ever reach the other, and the pair
stayed at 0/2 until one was cleared by hand.

The cause was over-generalising. "One segment per starting dot" is right for a splitter pair and
wrong for every other pair — an ordinary pair holds exactly one path, and drawing from either end
has always meant *replace*. `ClearSegmentsTouching` now clears **all** of a non-branching pair's
segments when either dot is pressed, and only clears per branch when the pair actually branches
(`PairBranches`, i.e. more than two dots). Clearing at press time rather than commit time matters:
the new path's bars are already on screen by the time a drag commits, and the old and new paths can
share a dot cell, so clearing later could wipe a bar the new path had just drawn.

**Two things this refactor forced that were not on the list.** `Block`'s occupancy went from two
slots to three — a Mixed cell or a bridge holds two *pairs*, but a splitter junction holds three
*segments of one pair*. And `GetDirection` had to be split: `AdjacentDirection` is now the pure
geometry, because un-drawing a path needs the direction the path *took*, and re-asking the rules
can answer None for a step that was perfectly legal when it was made (a one-way or an arrow
refuses the reverse reading of the same two cells). Clearing a path through an arrow would have
silently left bars on screen.

**What the connectivity check bought beyond the splitter.** It is the check `Mixed` and the bridge
wanted all along: it verifies each pair passes through a shared cell independently, rather than
treating the cell as owned. The positional test it replaced is the one `KNOWN_ISSUES.md` blames for
the merge/steal confusion.

### Step 8 — Rotator

| | Task | Done when |
|---|---|---|
| S8.1 | ✅ `BlockType.Rotator = 10` + `initialRotation` column (0–3, clockwise from Up+Right) | authorable |
| S8.2 | ✅ Runtime `currentRotation`, seeded by `SetBlock`, cleared by `ResetBlock`, never written back | rotation resets with the level, not the path; pooled cells carry nothing |
| S8.3 | ✅ `CanEnterFrom` refuses an unjoined edge; `CanExitFrom` requires the *other* joined edge | same shape as the arrow and bridge |
| S8.4 | ✅ `RotateBlock` runs from `OnPointerDown` and returns without setting `isClicked`, consuming the gesture | tap rotates, drag still draws |
| S8.5 | ✅ `ClearSegmentsThrough` cuts every segment back to just before the rotator | no stale path through an elbow that no longer exists |
| S8.6 | ✅ **A rotation counts as a move.** Design call, recorded in §4 as decided | move budgets and stars can be layered later |
| S8.7 | ✅ The two joined edges drawn as dim gold bars in the cell's own direction slots — no new art, and the elbow lines up with the path that will use it | players learn that gold means tappable |
| S8.8 | ⏳ `Level_12` authored; play: tap does not start a path, the path clears on rotate, the rotation resets on reload | new verb feels deliberate |

**Why the elbow is drawn with direction bars instead of a glyph.** Those four slots are already
exactly the right geometry — centre to edge — so the hint sits where the path will, and the shape
carries the rule rather than symbolising it. It only ever writes to slots no pair owns, so a path
covers it and `RefreshPathWash` restores it afterwards. The alternative was a marker sprite that
would have to *represent* an orientation the bars can simply be.

**Two latent bugs this step surfaced, both now fixed.** `ResetBlockToRemove` reset whole cells by
pair id, which after Step 7 could clear a *sibling branch's* bar from a shared splitter junction;
it now un-draws bar by bar. And it asked `GetDirection` for the direction of an existing step —
re-running the movement rules, which can answer None for a step that was legal when it was made
(a one-way, an arrow, or a just-rotated elbow refuses the reverse reading of the same two cells),
leaving bars stuck on screen. It now uses `AdjacentDirection`, the pure geometry extracted in
Step 7.

**The validator asks a deliberately different question about rotators.** Their orientation belongs
to the player, so reachability walks them in their most permissive form — any entry edge, any
ninety-degree turn — through `CanEnterFromUnderAnyRotation` / `CanExitFromUnderAnyRotation`.
Asking about the *authored* rotation would flag `Level_12`, whose entire point is that the board
starts closed, as unsolvable. This is the "solvability now depends on rotation state" gotcha the
plan predicted, and it is also the shape a future solver would have to take.

### Working agreement

- One step per change, in the order above; steps 1–3 are independent and can be reordered
  freely, 4 onward are sequential.
- Every step ends with you play-testing before the next one starts. I'll say exactly what
  to look at.
- Step 7 goes in alone with nothing else in flight — it touches every completion path, and
  the 12 shipped levels are its only regression suite.
- The four decisions in §4 get settled when their step comes up, not before, except
  `(PairColorType)pairId` — that one lands before Step 4 if the new pack wants more than
  nine pairs.
