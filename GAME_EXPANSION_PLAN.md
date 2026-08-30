# FreeFlow → Full Offline Flow Puzzle Game: Feasibility & Plan

Status: **Living document — updated after every phase.** Originally a feasibility audit with no code changed; now also the running record of what's actually been built, what's still pending, and any issues found along the way. See the Progress Log immediately below for the at-a-glance status, and §6 for the detailed per-phase notes.

**Campaign rescoped (superseding §12 of the original spec and Phase 7's "Worlds 1–11, 500+ levels" framing):** the user replaced the original 500+/11-world campaign design with a detailed **200-level** campaign spec — a 50-level Learning Phase teaching all 9 mechanics one at a time (Basic Flow → Blocked → Wall → One-Way → Arrow → Forbidden → Permitted → Bridge → Checkpoint → Shared Destination, levels 47–50 combining them), followed by a 150-level Mastery Phase that only recombines what's already been taught, scaling grid size (7×7→12×12), mechanic count per level (2–3 up to 5–7), and difficulty — never introducing anything new after level 50. See §6.6–6.22 for what's actually built against this new structure (**50/200 — the whole Learning Phase, all nine mechanics**) and §7 for where it stands and what is still open.

> **Coming back to this after a break? Read [§0 — How levels are generated](#0-how-levels-are-generated--start-here-when-picking-this-up-again) first.** It is the runbook: how to run generation, the two design regimes, how to verify a range, and how to add the next mechanic. Everything else is history and reasoning.

## Progress Log

| Phase | Status | One-line summary |
|---|---|---|
| 0 — Foundation hardening | ✅ Done | Rules-engine test suite added (31 tests), zero production code changed |
| 1 — Full-board coverage as a real rule | ✅ Done | Win condition now requires full coverage, not just connected pairs |
| 2 — The solver core | ✅ Done | `PuzzleSolver` built and tested; one real bug found and fixed (see §6) |
| 3 — Solver-backed validator + uniqueness + duplicates | ✅ Done | `LevelValidator.ValidateSolvability`, multi-solution search, `LevelCanonicalizer`; all correct on first test run (see §6) |
| 4 — Level generator (Editor tool) | ✅ Done | Solution built by `TryGeneratePathPartition` (grows every colour's path at once, Warnsdorff most-constrained-first) after the Hamiltonian-snake constructor was deleted for not scaling past 6x6 (§6.15); **generates all nine mechanics** — seven derived from the finished solution, plus Bridge and Shared Destination built into the partition itself via node splitting (§6.20, §6.22); acceptance driven by path length, uniqueness, necessity and a wrong-route floor, NOT DifficultyAnalyzer's score (§6.14) |
| 5 — Difficulty analyzer | ✅ Done | `DifficultyAnalyzer` built, tested, and wired into `LevelGenerator` as the actual acceptance gate — see §6 for the achievable-range finding this surfaced |
| 6 — Required-mechanic validator | ✅ Done | `RequiredMechanicValidator` built and tested (a real mathematical correction to the spec's naive reading surfaced along the way, see §6) — wired into `LevelGenerator` as a HARD REJECT (§6.18) for Blocked, Wall, One-Way, Arrow, Forbidden, Permitted, Checkpoint and Bridge. Shared Destination is deliberately exempt — it is dot identity, not a strippable rule, so there is no board without it to compare against (§6.22) |
| 7 — Content generation (rescoped: 200-level campaign) | 🟡 Partial | **50/200 levels built — the entire Learning Phase, all nine mechanics taught** (1–10 Basic+Blocked, 11–15 Wall, 16–20 One-Way, 21–25 Arrow, 26–30 Forbidden, 31–35 Permitted, 36–40 Bridge, 41–45 Checkpoint, 46–50 Shared Destination). Levels 1–10 keep the strict full-coverage rule as the tutorial; 11+ relax it so wrong routes exist and mechanics can matter — every mechanic instance load-bearing, every level uniquely solvable, boards 6×6–7×7 — see §6.6–6.22. Remaining: levels 51–200 (Mastery), which recombines rather than introduces — see §7 |
| 8 — Hint system | ⬜ Not started | |
| 9 — Player skill system + save-data expansion | ⬜ Not started | |
| 10 — Daily challenge | ⬜ Not started | |
| 11 — Campaign/world UI + stars/rewards + statistics | ⬜ Not started | |
| — Full-coverage generation rules audit | ✅ Done | User supplied a detailed "hard rules" spec for the generator; audited against it — 2 real gaps fixed (single source of truth, graduated uniqueness), 2 deferred with reasoning. See §6.5 |
| 12 — Mobile UX polish + performance pass | ⬜ Not started | |
| 13 — Test suite hardening + final QA gate | ⬜ Not started | |

## 0. How levels are generated — start here when picking this up again

Everything below is history and reasoning. **This section is the runbook.** Read it first after a break; the rest is only needed when something behaves unexpectedly.

### Running it

Unity menu → **FreeFlow / Level Generator / …** — one entry per range:

| Menu entry | Levels | Board | Colours | Mechanic |
|---|---|---|---|---|
| Generate Levels 1-10 (Basic Flow + Blocked Cell) | 1–10 | 4×4 → 6×6 | 3–6 | Blocked (from 6) |
| Generate Levels 11-15 (Wall) | 11–15 | 6×6 | 4 | Wall ×2–4 |
| Generate Levels 16-20 (One-Way) | 16–20 | 6×6 | 4 | One-Way (+Wall on 19–20) |
| Generate Levels 21-25 (Arrow) | 21–25 | 7×7 | 6 | Arrow (+Wall on 24–25) |
| Generate Levels 26-30 (Forbidden) | 26–30 | 7×7 | 6 | Forbidden (+Wall on 29–30) |
| Generate Levels 31-35 (Permitted) | 31–35 | 7×7 | 6 | Permitted (+Wall on 34–35) |
| Generate Levels 36-40 (Bridge) | 36–40 | 7×7 | 6 | Bridge (+Wall on 39–40) |
| Generate Levels 41-45 (Checkpoint) | 41–45 | 7×7 | 6 | Checkpoint (+Wall on 44–45) |
| Generate Levels 46-50 (Shared Destination) | 46–50 | 7×7 | 6 | Shared Destination (+Wall on 49–50) |
| Generate Levels 51-55 (Mastery: 8x8) | 51–55 | **8×8** | 8 | **one per level**, rotating; 10 blocked cells, no walls |

Each writes `Assets/Resources/Levels/Level_N.asset` and logs a per-level report to the Console. **Generation blocks the editor** — it is a synchronous `[MenuItem]`. A cancellable progress bar shows `Level 13 — attempt 800 / 20000`; Cancel aborts and keeps whatever was already saved. After adding levels, set `UIController.totalLevelCount` on the scene's UIController object and save the scene, or the new levels will not appear.

### The two regimes — the single most important thing to know

- **Levels 1–10: strict.** `RequireEveryPairingCoversBoard = true`. Connecting the pairs *cannot* leave a cell empty. This is the tutorial; a new player who connects everything and faces empty cells has no idea what the game wants. The cost is that these boards have exactly one possible pairing, so they are easy by construction — which is correct here.
- **Levels 11+: relaxed.** That rule is **off**; `Uniqueness = Require` instead. Each level still has exactly one *winning* solution, and `IsBoardFullyCovered` still gates completion, so wrong routes are attempts, not alternative wins. This is what gives the player something to search, and what lets mechanics matter at all. **Do not turn the strict rule back on for 11+** without re-reading §6.18 — it silently makes every mechanic decorative.

### Pipeline, in order

`TryBuildCandidate` builds a board: place Blocked cells → `TryGeneratePathPartition` grows every colour's path at once (Warnsdorff most-constrained-first, so no cell is stranded) → place Wall / One-Way / Arrow / Forbidden onto that solution → assemble `LevelData`.

`TryGenerateLevel` then filters each candidate. **Order is deliberate — cheap and selective first:**
1. solve (full coverage) — reject if unsolvable
2. `MinPathCells` — cheap, kills trivial 2-cell pairs
3. canonical-key dedup
4. coverage rule (only levels 1–10) — ~0.5 ms, rejects ~99%
5. uniqueness (hard reject under `Require`) — free, `solveResult` already knows
6. **mechanic necessity — hard reject**, headline mechanic first, Blocked last. Each clones the board and re-solves it *twice per mechanic instance*; the most expensive thing here by far
7. `DifficultyAnalyzer` + path-length band → ranking

**If you change or disable any gate, re-check the ordering of everything after it.** Getting this wrong cost hours twice (§6.17, §6.18).

### Verifying a range after generating

Never trust the generation log alone — it reports what the generator believed, not what shipped. Load each `Level_N.asset` via `BuildBlockGrid` and check:

- **Coverage (1–10 only):** solve with `AllowPartialCoverage = true`; every pairing must cover all usable cells.
- **Unique win:** `ValidateSolvability(..., MaxSolutionsToFind: 2)` → `SolutionsFound == 1 && SearchExhausted`.
- **Mechanics load-bearing:** `RequiredMechanicValidator.CheckBlockTypeMechanicRequired` / `CheckWallRequired` → `Required` for every instance. Target is 100%.
- **Wrong routes exist (11+):** solve with `AllowPartialCoverage = true`; expect tens to hundreds. A count of 1 means the board is a trace, not a puzzle. `MinWrongRoutes` enforces a floor during generation (Bridge range on; earlier ranges satisfy it already).
- **Construction-time guarantees, re-checked against the SOLVED board.** If a mechanic's property is established while building (only Bridge so far), the dots it derives may admit a different unique solution that lacks it — see §6.20.
- **No path ≤ 2 cells**, and **blocked cells off the outer ring**.

### Adding the next mechanic

**All nine mechanics are now built.** The recipe each placement mechanic followed (Bridge and Shared Destination are the two exceptions — see §6.20 and §6.22):

1. Check `Block.cs` — the rules engine already implements all nine mechanics. This is generator work, not gameplay work.
2. Write `Place<Mechanic>Cells(paths, excluded, …)` deriving the mechanic **from the intended solution** — One-Way/Arrow record the direction the solution travels; Forbidden names a colour that does *not* cross the cell. Use `InteriorPathCells` so dots are excluded structurally.
3. Add `<Mechanic>Count` to `GenerationSpec`, place it in `TryBuildCandidate`, write it into the grid, and add its necessity check to the hard-reject block.
4. If it needs a new `LevelData` column, check `BoardGenerator` reads it **and** that the generator's own `BuildBlockGrid` does — Forbidden's `pairId` was missing from the latter, which would have let the offline validation pass boards whose rule did nothing.
5. Add tests for the placement invariant (the "never bar its own colour" class of bug).
6. **Measure a sample before a full run:** count rejections per gate and time per attempt. Do not reuse another range's numbers — see the gotchas.

**Bridge broke step 2** — it was the one mechanic that could not be a placement pass. See §6.20; the recipe above holds for Checkpoint and Shared Destination, which are per-cell rules again.

### Gotchas that cost real time

- **Tuning does not transfer between configurations.** 4 colours is fine at 6×6 and disastrous at 7×7 (127 ms/candidate, zero unique solutions, vs 9 ms at 6 colours). Fewer colours on a bigger board means long paths, and proving uniqueness gets rare and slow. Always sample the actual configuration.
- **The RNG is seeded per range.** Re-running a failed range reproduces the same failure exactly — change `MaxAttempts` or the seed, or nothing will differ.
- **Board size is capped by the COLOUR PALETTE, not by the solver.** Uniqueness needs short paths, short paths need many pairs relative to cells, and `PairColorType` holds exactly 12 colours. That chain sets the ceiling at about 8×8. Measured at 12 colours: 8×8 solves in **6 ms** with 6 unique boards in 50, while 9×9 jumps to **1243 ms** with 0 in 9 and starts exceeding the step budget. Raising the ceiling means adding colours (and their art), not pruning the solver. Never conclude a board size is unreachable from one colour count — 8×8 at 8 colours looks hopeless (224 ms, 0 unique) and at 12 colours is cheaper than 7×7.
- **The progress bar's cancel flag is sticky.** All generate methods call `ClearProgressBar()` on entry for this reason; without it a cancelled run makes the *next* run abort instantly, reported as "CANCELLED by user" when nobody cancelled.
- **Editing specs:** use targeted edits, not file-wide substitutions. A scripted replace silently dropped `RequireMechanicsNecessary` from the Arrow spec, and those levels shipped with the gate never running.
- **`DifficultyAnalyzer.Score` is not a generation target.** It rewards packing in many short paths, so optimising it makes levels *feel* easier while measuring harder. Path length is the honest proxy.

---

> **Sections 1–5 are the original survey, written before any of this was built, and are kept as the record of what was decided and why.** They describe a codebase that no longer exists: §3 lists as "confirmed absent" the solver, the generator, the coverage rule, difficulty scoring, uniqueness and duplicate detection, and required-mechanic validation — all of which are now built — and it describes level gating that has since been removed entirely. **For what is actually true today, read §0 and §6.** Nothing in §1–§5 should be trusted as a statement about the current code.

## 1. Verdict

**Yes, this is achievable in Unity/C#, and the project is a better starting point than it looks from the outside.** The hardest, most bug-prone part of a Flow-style game — a correct per-cell rules engine for walls, one-way, arrows, forbidden/permitted colors, bridges, checkpoints, and multi-way shared destinations — is **already built and reasonably mature** in [`Block.cs`](Assets/Script/GamePlay/Block.cs). That engine is the foundation everything else (solver, generator, hints, validator) has to sit on, and it doesn't need to be reinvented.

What's missing is almost everything *around* that engine: a real solver, a generator, full-board-coverage as an actual rule, difficulty/uniqueness/duplicate analysis, hints, daily challenges, skill tracking, richer save data, and campaign/world UI. That's a large but very buildable scope — it's a second, independent subsystem (offline tooling + solver) layered on top of the existing renderer/input/gameplay code, not a rewrite of it.

The one design gap worth flagging up front: **the current game does not require full-board coverage to solve a level** — completion is "each pair's two dots are connected," full stop. The spec's rule #7 (100% of usable cells occupied) is central to Flow-style difficulty and to how a solver/generator would work. This has to be added as a real rule, not just a validator afterthought — see §4.1.

## 2. What already exists (verified by reading the code)

| Spec area | Status | Where |
|---|---|---|
| Grid sizes 4×4–12×12, board art per size | ✅ Done | `GridSize` enum, `BoardGenerator` |
| 12-color palette, data-driven | ✅ Done | `PairColorDataSO` / `Assets/Resources/PairColorData.asset` |
| Blocked cell | ✅ Done | `BlockType.Blocked`, `Block.CanEnter` |
| Wall (per-edge bitmask, independent of cell type) | ✅ Done | `Block.wallMask`, `HasWall`/`AddWall`, `BoardGenerator.NormalizeWalls` |
| One-Way (entry-direction only, exit free) | ✅ Done | `Block.requiredEntryDirection`, `CanEnterFrom` |
| Arrow (forced exit direction, blocks head-on entry) | ✅ Done | `Block.forcedExitDirection`, `CanExitFrom` |
| Forbidden cell (deny one/two pairs) | ✅ Done | `BlockType.ForbiddenForPair`, `NamesPair` |
| Permitted colors (allow-list, deny rest) | ✅ Done | `BlockType.AllowedForPairs` |
| Bridge (two lanes, straight-through only, per-axis occupancy) | ✅ Done | `BlockType.Bridge`, `CanAcceptEntry`/`CanExitFrom`, `occupantPairId[]` |
| Checkpoint (must-pass-through, checked at completion) | ✅ Done | `BlockType.Checkpoint`, `GamePlayController.IsPairSatisfied` |
| Shared destination (up to 4 pairs per cell) | ✅ Done | `secondPairId/thirdPairId/fourthPairId`, `Block.ShowSharedDotCluster` |
| Data-driven level format | ✅ Mostly | `LevelData`/`GridRow` struct — has cells, walls, mechanics, pairs. **Missing**: solution, difficulty, requiredMechanics, introducedMechanics, seed, metadata |
| Touch drag-to-draw, backtrack, replace, fast-swipe interpolation | ✅ Done | `GamePlayController` drag state machine |
| Structural level validation (dot counts, mechanic self-consistency, per-pair reachability) | ✅ Partial | `LevelValidator.cs` — see caveat below |
| Local JSON save | ✅ Minimal | `SavingSystem.cs` — completedLevel, per-level moves, audio settings only |
| Level select UI | ✅ Flat only | `LevelScreenController`/`LevelButton` — paginated flat list, no worlds |

**Important caveat on `LevelValidator`**: its `ValidateReachability` check is explicitly documented in its own code as *"a lower bound, not a solver: it walks one pair at a time and knows nothing about pairs competing for the same cells."* It cannot tell you a board is actually solvable as a whole, and it does not check full-board coverage at all. This is the biggest functional gap, not just a missing nice-to-have.

## 3. What does not exist (confirmed absent, not just unfinished)

- **Any general-purpose solver.** Nothing determines whether a full board (all pairs simultaneously, competing for cells, full coverage required) has a valid solution. This blocks generation, validation, difficulty scoring, hints, and daily challenges alike — they all need this one piece.
- **Procedural level generation.** `BoardGenerator` only renders an already-authored `LevelData`; there is no code that invents one. All 7 shipped levels are hand-authored, 5×5, 2–3 pairs, almost no mechanic usage.
- **Full-board-coverage rule.** Not enforced as a win condition, not checked by the validator.
- **Difficulty scoring**, **solution-uniqueness detection**, **duplicate/near-duplicate detection** (canonical hashing across rotation/flip/color-remap).
- **Required-mechanic validation** (generate → strip mechanic → confirm now-unsolvable). Depends entirely on the solver existing first.
- **Hint system.** Zero code; one unused icon asset.
- **Daily challenge / deterministic seeded generation.**
- **Player skill rating**, mechanic-specific skill tracking.
- **Star rating, level timer.** Only move-count is tracked today.
- **World/campaign structure.** Level select is a flat, linearly-gated (`N unlocked iff N ≤ completedLevel+1`) paginated grid — no worlds, no per-world mechanic gating, no world-challenge levels.
- **Statistics screen, daily-streak UI.**
- **Automated test suite** for any of the above.

## 4. Key design/engineering decisions this plan is built on

### 4.1 Full-board coverage becomes a real rule, not a bolt-on

This changes three things that already exist:
- **Win condition** (`GamePlayController`, currently `OnPointerUp` checking `GetPairCompleteCount() >= goal`) needs an added check that every usable cell (excluding `Blocked`) is occupied by exactly one path (or, on a `Bridge` cell, exactly one path per axis).
- **`LevelValidator`** needs a real full-board solver behind it (see 4.2), not just per-pair BFS.
- **Level data** needs `requiredCoverage`/usable-cell count as explicit metadata so a partially-blocked board (spec §"Blocked Cell") computes the right target.

This is a rules change to existing gameplay, so it needs its own careful pass and its own tests before generation work starts on top of it — get this wrong and every generated level inherits the bug.

### 4.2 One shared solver, reused everywhere

The spec is explicit about this (§8) and it's the right call: build one constraint-solving core (something like backtracking search over path segments per cell, with the existing `Block.CanEnter`/`CanEnterFrom`/`CanExitFrom`/`CanAcceptEntry` predicates as the legality oracle — those functions are *already* pure and side-effect-free, which is exactly what a solver needs to call directly instead of re-implementing rules a second time) and reuse it for:
- Level validation (does a solution exist at all, with full coverage)
- Level generation (accept/reject candidate boards)
- Difficulty scoring (search effort, forced-move count, branching factor)
- Solution-uniqueness (bounded search for a second distinct solution)
- Hints (walk the stored/re-derived solution)
- Daily challenge generation

This is the single largest new engineering piece in the whole plan. Everything else is either straightforward Unity/UI work or offline tooling around this core.

### 4.3 Levels are generated offline, not on-device

Per spec §38 — and this matters practically: a full board-coverage solver over a 12×12 grid with 8+ colors and multiple mechanics is a real combinatorial search. It has to run as an Editor tool / standalone generation pass during development, get validated through the full pipeline (§37 of the spec), and ship as baked `LevelData` assets. The player's device only ever loads pre-validated data — it never runs the generator or a heavy solver during play, only the lightweight hint/gameplay-check paths. Daily challenges are the one place a bounded, fast, fail-safe generation-or-cache step can run on-device (spec §39), because their board sizes/complexity should be tuned to stay fast.

### 4.4 Extend `LevelData`, don't replace it

The existing struct-of-arrays `GridRow` format is unusual (Unity-serialization-friendly parallel arrays) but works and is already wired through `BoardGenerator`/`Block`/`LevelValidator`. Add fields rather than redesigning: `solution`, `difficultyScore`, `requiredMechanics`, `introducedMechanics`, `seed`, `worldId`, `metadata`. Existing hand-authored levels (7 today) keep working since new fields default sensibly, matching the pattern already used for `pairId`/`blockType`/`wallMask` (all optional, all default to a safe value).

## 5. Feasibility risks (things that could go sideways)

1. **Solver performance at 12×12 with many mechanics.** This is the real technical risk in the whole project. Flow-Free-style full-coverage multi-path problems are NP-hard in general; a naive backtracker will not scale to a fully mechanic-loaded 12×12 board without good constraint propagation (cell-degree pruning, connected-component/parity checks, dead-end detection) — this needs real solver engineering, not just "add DFS." Mitigated by point 4.3 (generation is offline/pre-baked, so a slow solver during generation is acceptable; only *gameplay-time* hint lookups need to be fast, and those replay a pre-computed solution rather than re-solving).
2. **500+ genuinely distinct, validated levels is a content pipeline problem as much as a code problem.** Even with a working generator, tuning it to reliably produce levels that are solvable, uniquely-solvable, difficulty-appropriate, and require their newly-introduced mechanic (spec §10) across 10 worlds and 8 grid sizes will take real iteration — expect the generator itself to need tuning passes per world, not a single universal knob.
3. **Required-mechanic validation is solver-call-heavy** (solve with mechanic, strip it, solve again, per candidate level) — generation throughput will be bounded by solver speed squared, roughly. Budget for this in the generation pipeline's runtime, not just its correctness.
4. **Full-board-coverage retrofit risk.** Changing the win condition is a behavior change to existing gameplay code that currently ships and works for the 7 existing levels — needs test coverage before it's touched, not after.

None of these are blockers; they're the reason this is a multi-phase project rather than a single feature add.

## 6. Proposed phased roadmap

Reordered from the spec's phase list to reflect what's already done and to front-load the highest-risk piece (the solver) before content work depends on it.

**Phase 0 — Foundation hardening** ✅ Done
Add automated tests around the existing rules engine (`Block.CanEnter`/`CanEnterFrom`/`CanExitFrom`/`CanAcceptEntry`, `LevelValidator`) before changing anything, so later phases have a safety net.
- **Done:** `Assets/Tests/Editor/` — `BlockTestHarness.cs` (reflection-based Block setup, no scene/prefab/singleton needed), `BlockRulesTests.cs` (14 tests), `LevelValidatorTests.cs` (17 tests). 31/31 passing. No production file touched.
- **Issues found:** None in production code. Three first-draft `LevelValidatorTests` assumed wrong semantics for shared destinations and failed against the real validator; corrected once the actual rule was understood (a shared-destination cell counts as one of *each* named pair's two required dots, not a fresh pair) — a test-authoring correction, not a game bug.

**Phase 1 — Full-board coverage as a real rule** ✅ Done
Update win condition, validator, and level data metadata (§4.1). Re-validate the 7 existing levels still complete correctly.
- **Done:** `GamePlayController.OnPointerUp`'s level-complete check now requires `GetPairCompleteCount() >= CurrentLevelGoal && IsBoardFullyCovered()`. New `IsBoardFullyCovered()` treats every non-`Blocked` cell as needing ≥1 occupant (a Bridge only needs one of its two lanes used). 3 new tests (`BoardCoverageTests.cs`), 34/34 total passing.
- **Deviation from the original plan (intentional, an improvement):** did *not* add a stored `requiredCoverage` field to `LevelData` as §4.1 originally suggested — the usable-cell count is computed live (grid size minus `Blocked` cells) instead, so it can never drift out of sync with the actual board the way a separately-authored count could.
- **Issues found:** None. Note: since all 7 old hand-authored levels were deleted before this phase (per an earlier session request), there was nothing left to re-validate against the new rule — that verification is deferred to Phase 4's generated content.

**Phase 2 — The solver core** ✅ Done
Build the shared constraint solver (§4.2): given a `LevelData`, produce a valid full-coverage multi-pair solution or prove none exists, reusing the existing `Block` predicates. This is the critical-path phase — generation, validation, difficulty, hints, and daily challenges all wait on it.
- **Done:** `BoardTopology.cs` (new) — `Neighbor`/`Opposite`/`CollectDots` extracted out of `LevelValidator` so `PuzzleSolver` isn't a second copy of the same three helpers; `LevelValidator` now delegates to it, behavior-preserving. `PuzzleSolver.cs` (new) — per-pair backtracking DFS with its own occupancy bookkeeping (kept separate from `Block`'s real occupant fields, which exist to drive gameplay visuals), a step budget (`Solved`/`Unsolvable`/`Inconclusive`), and one cheap direction-agnostic reachability prune. 15 new tests covering every mechanic plus full-coverage-forces-a-detour and a parity-based unsolvable case, 49/49 total passing.
- **Issues found (real bug, caught by tests not inspection):** the first draft computed "have we reached the target dot" by re-deriving it from the *current* wandering cell every step, which is self-defeating — the instant the search actually reached the target, that same logic computed the target as "whichever dot isn't here," i.e. the start, so arrival was never detected. Every "should be solvable" test failed identically (reported `Unsolvable`). Reproduced live in the Editor via `execute_code` rather than guessed at, then fixed to use the pair's fixed target dot (always index 1, since every caller starts a pair's search from index 0).
- **Known limitation, flagged not hidden:** no advanced pruning beyond the one reachability check; tested only on small boards (up to 3×3). Performance on large, heavily-mechanic-loaded boards (12×12) is unproven — expected to need real tuning once Phase 4 asks it to solve at generation scale, per the risk already called out in §5.1.

**Phase 3 — Solver-backed validator + solution-uniqueness + duplicate detection** ✅ Done
Wire the solver into `LevelValidator`. Add bounded second-solution search. Add canonical-hash duplicate detection (rotation/flip/color-remap normalization).
- **Done:** `LevelValidator.ValidateSolvability(grid, rows, cols, options)` (new) — runs `PuzzleSolver.Solve` and logs an `Error` on `Unsolvable`/`Inconclusive`, matching the class's existing "loud but not fatal" convention. `PuzzleSolver` gained `SolverOptions.MaxSolutionsToFind` and `SolveResult.SolutionsFound`/`SearchExhausted` — reusing the exact same backtracking (recording a full-coverage arrangement and telling the search to keep hunting rather than stopping, instead of a second search implementation). `LevelCanonicalizer.cs` (new) — an 8-way dihedral (rotate/reflect) + canonical-pair-relabeling key, so two boards that are "the same puzzle" under rotation, mirroring, or color/id renaming produce an identical string. 11 new tests (3 solvability, 2 uniqueness, 6 canonicalizer), 60/60 total passing.
- **Deliberate design decision (not in the original plan wording):** `ValidateSolvability` is a separate, opt-in method, **not** folded into `Validate` (which still runs automatically on every `GamePlayController.LoadLevel` and stays cheap/structural-only). Running the solver on every level load would risk stalling live gameplay — it has no performance guarantee (Phase 2's own caveat) — which conflicts with §4.3/§40's "gameplay must stay responsive" principle. `ValidateSolvability` is meant for the Phase 4 generator, editor tooling, and tests; nothing in the runtime load path calls it yet, by design.
- **Issues found:** None — this was the first phase where every test passed on the first full run, including the hand-derived rotation/reflection math in `LevelCanonicalizerTests` (each expected "rotated" grid was worked out by hand from the same coordinate/direction formulas the production code uses, then checked against it independently).
- **Scope note:** canonical-key duplicate detection is built and tested in isolation; there is no level corpus yet to actually deduplicate against (that arrives with Phase 4's generator, which is expected to maintain a `HashSet<string>` of canonical keys and reject any candidate that collides).

**Phase 4 — Level generator (Editor tool)** 🟡 Partial — World 1 only
Solution-first generation pipeline (§6 of the spec): partition board → assign colors/endpoints → add mechanics → validate → solve → score difficulty → check uniqueness/duplicates → save. Ships as a Unity Editor tool, not runtime code.
- **Done:** `Assets/Script/Editor/LevelGenerator.cs` (new, menu item `FreeFlow/Level Generator/Generate World 1`) — a real, reusable pipeline: generate a Hamiltonian path over the whole board ("snake" visiting every cell once) → cut it into one contiguous segment per colour (each segment's two ends become that colour's dots — this is the "generate the solution first" approach, not "author a puzzle and hope") → `LevelValidator.Validate` (structural) → `LevelValidator.ValidateSolvability` (solver confirms solvable + gets a uniqueness signal) → `LevelCanonicalizer` (reject duplicates) → save as `SingleLevelDataSO` assets. Ran it end-to-end: **50/50 World 1 levels generated and saved** to `Assets/Resources/Levels/`, `UIController.totalLevelCount` set to 50, zero generation failures, zero validator errors.
- **Scope as explicitly requested: World 1 only.** Worlds 2–11 (Blocked cells through combined mechanics, levels 51–500+) are **not generated** — the generator's mechanic-layering, per-world difficulty tables, and the teach→force→combine→master authoring pattern (Phase 6) don't exist yet. This phase is not "done," only its first slice is.
- **Difficulty proxy, not the real thing (by design, not oversight):** World 1 has no mechanics at all, so difficulty comes entirely from path *shape* — a "straightness bias" knob (0–1) that ramps from mostly-straight (easy, early levels) to constantly-turning (hard, late levels) across the 50 levels, plus colour count per the spec's own World 1 table (2→5) and a periodic "breather" easier level per §13. This is a deliberately simple stand-in; **Phase 5's real multi-factor difficulty scorer does not exist yet**, so this ordering is a reasonable guess, not a validated difficulty curve.
- **Solution-uniqueness handling:** levels 46–50 require a uniquely-solvable candidate (retrying up to 250 times), with a graceful fallback to the best non-unique candidate found rather than leaving a gap if uniqueness can't be hit in time. In practice, the run found unique solutions for every level that required one — no fallback was needed.
- **Verification performed:** full 60-test suite re-run clean after generation (no regressions from the new Editor-only file). Entered Play Mode, loaded the generated Level 1 directly, and confirmed via direct state inspection — not a screenshot (see below) — that: `gameplayScreen` was active and `mainMenuScreen` inactive, the live board was 5×5 with exactly the two generated pairs at the expected cells, and the console had zero errors/warnings through the whole load.
- **Tooling limitation hit, not a code bug:** attempted a Game View screenshot for visual confirmation; it consistently returned a stale/blank frame (still showing the main menu) even though direct state queries proved gameplay had loaded correctly. This looks like an artifact of driving the Editor without a genuinely focused/rendering Game View window in this automated session, not a problem with the generated level or the game code. Flagging this honestly rather than presenting an unreliable screenshot as proof.
- **Issues found:** None in the generation logic itself — the dry run and full run both succeeded without needing a single fallback path. The only friction was operational: a test-runner job briefly got stuck mid-domain-reload right after the 50-asset save (cleared and re-ran successfully) and the screenshot limitation above.

**Phase 5 — Difficulty analyzer** ✅ Done
Multi-factor scoring (§13) built on solver search statistics (branching factor, forced-move count, dead ends, backtracking depth) plus board metrics.
- **Done:** `PuzzleSolver.SolveResult` gained three instrumentation fields gathered during the same search that already runs — `StepsTaken`, `DecisionPointCount` (cells where more than one direction was legal, a real branch point), `DeadEndCount` (path-building ran out of legal directions and had to backtrack) — by restructuring the search's direction loop to collect all legal moves before trying them, rather than short-circuiting on the first one that worked. `Assets/Script/GamePlay/DifficultyAnalyzer.cs` (new) combines these with purely-structural board metrics — constrained-cell ratio (walls/mechanics as a fraction of usable cells), path-winding ratio (solved length vs. direct Manhattan distance between dots), and path-competition ratio (fraction of cells more than one pair could structurally reach, independent of any particular solve) — into a documented, weighted 0-100 score and a 6-tier classification (`VeryEasy`…`Expert`) matching spec §13's thresholds exactly. 13 new tests — including hand-derived exact expected values (e.g. a 3x3 competition-ratio case computed by hand as 5/9 and confirmed to the third decimal) — 73/73 total passing.
- **Deliberate design decision:** `Analyze` takes an already-computed `PuzzleSolver.SolveResult` as a required input rather than solving internally — callers like `LevelGenerator` have typically already solved the board via `ValidateSolvability`, and solving twice would double that cost for nothing.
- **Explicitly flagged as a first pass, not a validated curve:** the 10 factor weights (documented in the class doc) are a reasoned but untuned starting point — the spec itself doesn't prescribe exact weights, and getting them right needs real playtesting data this project doesn't have yet.
- **Issues found:** None — every hand-derived test value matched on the first run.

**Follow-up — wiring DifficultyAnalyzer into LevelGenerator, and re-running World 1:**
- **Done:** `LevelGenerator` no longer treats `straightnessBias` as the difficulty signal itself. Each candidate is now analyzed with `DifficultyAnalyzer.Analyze` (reusing the same `SolveResult` `ValidateSolvability` already produced — no second solve) and only accepted once its score lands in the level's target band; otherwise the generator retries with a fresh random snake/colour count, keeping the closest-scoring valid candidate as a fallback (same graceful-degradation pattern as the uniqueness requirement) so no level is ever left ungenerated. `LevelData` gained one new optional field, `difficultyScore` (float, defaults to 0), so the computed score is actually persisted per level rather than only appearing in a console log. `straightnessBias` is kept as a coarse generation-time nudge to the snake's shape, not as the acceptance criterion.
- **A real finding this surfaced, calibrated before committing to it:** before wiring the gate, I empirically probed what score range a mechanic-free 5×5 board can actually reach (40 sampled boards across colour counts 2–5 and a spread of straightness values). Result: **roughly 36–50, clustering in the high 30s to mid 40s, regardless of colour count or snake shape** — never higher. This is because `mechanicFactor` and `constrainedCellRatio` are 25 of the score formula's 100 points and are structurally always zero with no mechanics at all, which World 1 by design never has. My first instinct (aim World 1's levels at a 0→100 spread ending near "World Challenge" hard) was simply unreachable and would have made every level 46–50 generation attempt fail outright. Recalibrated `SpecForWorld1Level`'s target bands to ramp within the real, measured envelope (~36.5 → ~48 center, ±2.5 band) instead of a hoped-for range — an honest reflection of the fact that a mechanic-free world's difficulty ceiling is genuinely lower than a mechanic-loaded one, not a bug to work around.
- **Re-ran `GenerateWorld1()` end to end: 50/50 levels regenerated and saved**, each now carrying a real computed `difficultyScore`. Observed a clear, mostly-monotonic upward trend across the 50 levels (score ≈37.6 at level 1 → ≈44–46 through levels 40–50), tiers ranging Easy→Medium (matching the calibrated envelope, not Hard/VeryHard/Expert — those require mechanics, which is correct for World 1). Levels 46–50 all landed on uniquely-solvable boards as required.
- **Issue found and handled, not hidden:** levels 46–50 combine two constraints at once (tight score band *and* required uniqueness) right at the top of the achievable range, which is a hard combination to hit exactly — all 5 of those levels used the fallback path (best candidate found within 300 attempts, logged via `Debug.LogWarning`), landing within about 0.2–1.2 points of their target band's lower edge rather than inside it. Every one still met the uniqueness requirement exactly; only the score band was missed, and only slightly. This is the fallback mechanism working as designed, not a failure — flagging it because "5 levels used the documented fallback" is a fact worth knowing, not because anything is broken.
- Full 73-test suite re-run clean after both the `LevelData` schema change and the generator rewrite.

**Phase 6 — Required-mechanic validator** 🟡 Partial — validator built, not yet wired to content
Generate-with-mechanic / strip / re-solve check (§10), and the teach→force→combine→master authoring pattern (§11) as generator presets per world.
- **Done:** `Assets/Script/GamePlay/RequiredMechanicValidator.cs` (new) — given a board and a specific mechanic (a cell's BlockType, or a specific wall edge), clones the board, strips just that one mechanic, re-solves, and classifies `Required` / `NotRequired` / `Inconclusive`. 10 new tests, every scenario worked out from first principles and then empirically confirmed (not guessed-and-adjusted) before being locked in — 83/83 total passing.
- **A real correction to the spec's own worked example, derived from first principles before writing any code:** the spec's §10 example ("with Arrow: solvable; without Arrow: unsolvable; therefore required") is not how a pure movement-restriction mechanic (Blocked's-neighbours aside, One-Way, Arrow, Forbidden, Allowed, a Wall) can ever actually behave. Removing a restriction can only ever *grow* the set of legal moves, so any solution valid *with* the restriction remains valid *without* it — meaning solvability literally cannot flip from "yes" to "no" by deleting one of these six mechanics. What removing them *can* do is turn a puzzle with exactly one solution into one with several, by reopening a route the mechanic used to rule out. So the real, checkable signal implemented here is **solution-count change (unique → non-unique), not solvability**, for those six mechanics. Bridge (grants extra capacity, a cell holding two pairs instead of one) and Blocked (removes a cell from the coverage requirement entirely) are genuinely different — for those two, "does it stay solvable" *is* the right question, and both are handled by the same classifier. This is documented at length in the class's own doc comment, not just here, since it's exactly the kind of non-obvious reasoning a future reader would otherwise have to rediscover.
- **A second finding, from extensive empirical probing before locking in test cases:** constructing a hand-verified "Blocked → NotRequired" board turned out to be extremely difficult, and eventually provably impossible for the simple cases tried. Reason: for a single pair covering 100% of a board's cells, blocking one cell changes the required-coverage count's parity (odd↔even), which — per the standard checkerboard-colouring argument for Hamiltonian paths — flips whether the two dots' colours are even allowed to admit a full-coverage path *at all*. A fixed pair of dots can only ever satisfy one of the two parity requirements, never both, so blocking a cell in this setup is structurally always either `Required` or breaks solvability outright (`Inconclusive` by this validator's own honest "can't test necessity against an unsolvable baseline" rule) — never neutral. Documented in the test file rather than papered over with a contrived multi-pair board that might not generalize.
- **Explicitly out of scope for this phase, by design:** shared-destination identities (`secondPairId` etc. on a dot cell) are not covered — a dot names which pairs it belongs to, not a strippable rule, so "remove it and re-solve" doesn't apply the same way; testing whether a *sharing* is meaningful would need a different technique. Also out of scope: the teach→force→combine→master authoring *pattern* itself (spec §11) and any actual generator presets using this validator — those need a mechanic world to apply to, and only World 1 (no mechanics) exists so far. This phase built and proved the checking mechanism; wiring it into `LevelGenerator` for Worlds 2+ is Phase 7 work.

### 6.5 Full-coverage generation "hard rules" compliance audit

The user supplied a detailed, 30-point "Hard Rules for Full-Coverage Level Generation" spec addendum, focused entirely on making full coverage a *generation-time* constraint rather than a post-hoc check. Rather than assume the existing generator already complied, it was audited point by point against the real code (`LevelGenerator`, `PuzzleSolver`, `GamePlayController`).

**Already compliant, no changes needed:** the core "snake-then-cut" architecture (Phase 4) already matches the spec's central demand almost exactly — generate a complete board-spanning solution first (rules 2, 3, 29), partition it into continuous per-colour paths with no branches/self-intersections (rules 4, 5), select endpoints from the partition rather than randomly (rule 9), never produce "artificial" forced-fill coverage (rule 6), and verify 100% coverage as part of the solve itself, not after (rule 11) — a candidate solution is never even recorded unless `PuzzleSolver` confirms full coverage. Connectivity/reachability is maintained as an invariant during generation (rules 14–17) by construction: the Hamiltonian-path backtracking search cannot produce a trapped/unreachable region without the search itself detecting the dead end and backtracking immediately — the same mechanism the rules ask for, arrived at via a single-path-then-partition algorithm rather than the multiple-simultaneous-paths algorithm the rules seem to assume.

**Real gaps found and fixed:**
- **Rule 12 ("one source of truth" for full coverage across runtime/solver/generator/validator).** Found a genuine violation: `GamePlayController.IsBoardFullyCovered` (Phase 1) and `PuzzleSolver`'s internal `SolverState.IsFullyCovered` (Phase 2) were two independently-written implementations of the same rule, on two different occupancy representations (the live board's real `Block` state vs. the solver's own scratch state during search — which must stay separate; see Phase 2's notes on why the solver never touches Block's real fields). Fixed by extracting the *structural* rule — "every cell except Blocked needs an occupant" — into one canonical `BoardTopology.IsFullyCovered(grid, rows, cols, hasOccupant)`, parameterized by a caller-supplied occupancy query. Both call sites now delegate to it; only *what counts as occupied* still differs per caller (correctly), not *which cells need checking*. All 83 tests still pass after the refactor.
- **Rule 13 (graduated solution-uniqueness preference: Easy allows several, Medium/Hard prefer one, Expert requires one).** The generator previously had only a binary `RequireUniqueSolution` switch (on for levels 46–50, completely off — no preference at all — for 1–45). Replaced with a 3-value `UniquenessPolicy` (`Ignore` / `Prefer` / `Require`): `Prefer` adds a small tie-breaking penalty (1 point, well under the ±2.5 score-band half-width) that favours a unique candidate without rejecting a good non-unique one, `Require` keeps the old hard, large penalty. World 1's levels 16–45 now use `Prefer`. Re-ran `GenerateWorld1()` again: **35 of 50 levels are now uniquely-solvable** (up from ~24 before this change) — the soft preference turned out to be very effective in practice, not just in theory. Fallback-path usage on the hard-required levels 46–50 also dropped (3 warnings this run vs. 5 before), incidentally, since the wider pool of unique candidates from levels 16–45's change made the whole search space easier to satisfy. 50/50 levels saved, full 83-test suite still clean.

**Gaps found and deliberately deferred (flagged, not fixed):**
- **Rules 7–8 (balanced path lengths; avoid large monochrome blocks/visually trivial solutions).** `DistributeLengths` scatters "extra" cells across segments via independent random increments — this has no explicit balance control and no check against a segment landing as a long straight monochrome run. Not fixed now: doing this well needs either a configurable balance parameter or a post-hoc "does this look trivial" check, and tuning it without real playtest/visual feedback risks the same "hoped-for, not measured" mistake already made once this session (§ Phase 4/5's difficulty-band recalibration). Worth doing before Worlds 2+ content generation, not blocking now.
- **Rule 20 (explicit decision-point-count targets per difficulty tier, e.g. "Easy 5–10, Hard 15–25, Expert 25+").** `PuzzleSolver` already exposes `DecisionPointCount`/`DeadEndCount` (Phase 5) and `DifficultyAnalyzer` folds decision *density* into its score, but nothing gates generation on an explicit numeric decision-point target the way rule 20 describes. Related to the rules 7–8 deferral above — same reasoning, same "needs real tuning data" caveat.
- **Rule 22's mechanic-requirement framing repeats the same imprecise claim already corrected in Phase 6** ("remove Arrow → no valid 100% solution therefore required") — no new action needed here since `RequiredMechanicValidator` already implements the mathematically-correct version (solution-count change, not solvability, for pure-restriction mechanics); noting the repetition here only so it doesn't get re-litigated as a fresh gap later.
- **Rules 23–26 (Blocked/Bridge/Checkpoint/Shared-Destination must be woven into the solution structure from the start, not bolted on afterward).** Not yet applicable — World 1 has zero mechanics — but stated here as a design constraint to hold to when Worlds 2+ generation (Phase 7) actually starts adding mechanics to the snake-then-cut pipeline.

**Issues found:** none beyond the two gaps above — everything else in the 30-point spec was either already satisfied by the existing solution-first architecture or is explicitly out of scope until mechanic worlds exist.

### 6.6 Campaign rescoped to 200 levels; Levels 1–10 built (Basic Flow + Blocked Cell)

*(See §6.7 immediately below for Levels 11–15 / Wall, built in a follow-up session.)*

The user replaced the original 500+/11-world campaign spec with a 200-level design: a 50-level Learning Phase introducing the 9 mechanics one at a time with a strict teach→reinforce→force→combine pattern per mechanic, then a 150-level Mastery Phase (levels 51–200) that introduces nothing new and scales difficulty purely through combination, grid size, and constraint density. This supersedes Phase 7's old "Worlds 1–11" framing entirely — that row in the Progress Log now tracks the new 200-level structure instead.

**Scope decision, stated up front:** building generation support for all 9 mechanics in one pass was not attempted — each mechanic needs its own new mechanic-placement engineering (Arrow/One-Way need direction assignment onto the path, Bridge needs an actual crossing constructed between two paths, Checkpoint needs on-path placement, Shared Destination needs multi-pair endpoint merging), not just a config flag. This session built and shipped the first concrete slice — Levels 1–10 — the same incremental, test-as-you-go approach used for every prior phase, rather than generating 190 more levels' worth of unbuilt mechanics.

**Done:**
- **Levels 1–5 (Basic Flow, spec §4):** regenerated from the existing mechanic-free snake-then-cut pipeline — 2 colours, 5×5, no mechanics, `UniquenessPolicy.Ignore` (spec explicitly allows several solutions at this stage).
- **Levels 6–10 (Blocked Cell, spec §5):** required genuinely new generator capability, since Blocked cells must be excluded from the board *before* the solution is generated, not stamped on afterward (rule 2's "don't generate endpoints/obstacles first" applies just as much to Blocked cells as to endpoints). Built:
  - `LevelGenerator.PlaceBlockedCells` — randomly excludes N cells, retrying until the *remaining* usable region is a single connected component (a disconnected or island remainder could never admit one Hamiltonian path covering it — spec §15–17).
  - The Hamiltonian snake search (`TryGenerateHamiltonianSnake`/`ExtendSnake`) now walks only the usable-cell mask, targeting usable-cell count rather than the full board; it returns `null` on failure instead of throwing, since a specific *connected* placement can still admit no Hamiltonian path at all (a parity issue, not a connectivity one) — that's a failed attempt to retry with a fresh placement, not a fatal error.
  - `RequiredMechanicValidator.CheckBlockTypeMechanicRequired` is now actually **wired into `LevelGenerator`** (`AllBlockedCellsAreNecessary`, gated by a new `RequireBlockedCellsNecessary` spec flag) — every Blocked-cell level's acceptance is gated on the cell being load-bearing, not decorative, closing out Phase 6's "built but not wired" status.
  - `BoardTopology.IsFullyCovered`'s single-source-of-truth fix (§6.5) meant this new path automatically inherited the correct coverage rule with no separate implementation needed.
- **Old Levels 11–50 deleted.** They were pure Basic-Flow boards generated under the superseded 50-level-World-1 design and no longer match the new spec (which wants Walls at 11–15, One-Way at 16–20, etc.) — shipping them would have been actively misleading content, not just incomplete. `UIController.totalLevelCount` set to 10, reflecting only what's actually built.
- **4 new tests** for the new connectivity/placement logic specifically (`LevelGeneratorBlockedCellTests` — the rest of a candidate's correctness rides on the already-tested solve/validate/canonicalize/mechanic-necessity pipeline, which needed no new tests to cover this). **87/87 total passing.**
- Ran the real generator end-to-end: **10/10 levels saved**, every Blocked-cell level (6–10) passed the necessity gate on the first generation pass (no fallback warnings).

**A calibration finding, consistent with the pattern already established twice this session:** a single Blocked cell barely moves `DifficultyAnalyzer`'s score at all on a 24-25-usable-cell board (its mechanic-weight and constrained-cell-ratio contributions are a few tenths of a point each) — so Levels 6–10's target difficulty band is a small, honest drift (36→40) rather than an invented ramp, the same "calibrate to the measured ceiling" lesson from Phase 4/5's World 1 work and §6.5's deferred rules 7/8/20.

**Explicitly not done at this point, and why:** Levels 11–200 (Wall through Shared Destination, all combination/mastery levels) were not yet built. Each remaining mechanic needs its own generator-side placement logic before any of those levels can be generated the "solution-first" way the hard rules demand — this is substantial, multi-session engineering, not a config change.

### 6.7 Levels 11–15 built (Wall)

**Done:**
- **`LevelGenerator.PlaceWalls`** (new) — walls off `WallCount` single edges, restricted to edges the intended snake solution never crosses (placing a wall the solution itself needs would break the very thing generation just built). Each undirected edge is considered exactly once (probing only Right/Down from each cell) so it can't be double-picked from either side; a normalized `(row,col,row,col)` edge key is checked against the snake's own consecutive-cell pairs to exclude path edges. Returns fewer than requested (rather than throwing) if the board doesn't offer enough non-path edges — the caller treats that as a failed attempt and retries with a fresh snake, same pattern as the Blocked-cell placement's parity failure handling.
- **`RequiredMechanicValidator.CheckWallRequired` wired into `LevelGenerator`** (`AllWallsAreNecessary`, gated by a new `RequireWallsNecessary` spec flag) — scans the built `Block[,]` for `HasWall` directly rather than threading the placement list through, since a wall is authored one-sided (matching existing hand-authored convention) and a scan finds each one exactly once.
- **`LevelData.GridRow.wallMask` now actually populated by the generator** (previously always null/unused, since only Blocked levels existed) and read by `BuildBlockGrid`.
- **Cross-generation-run duplicate detection, fixed for real this time:** added `SeedExistingCanonicalKeys`, which loads every already-generated level and seeds `LevelCanonicalizer` keys from it before a new range starts generating — Levels 11–15 now correctly reject duplicates against Levels 1–10 too, not just against each other. This is the right foundation for every future mechanic batch, not just Walls.
- **Level 14 built as Wall + Blocked combined**, per spec §6's "Wall interacts with Blocked Cell" — both spec flags set simultaneously, no special-case code needed since the two placement/necessity systems already compose independently.
- **4 new tests** (`LevelGeneratorBlockedCellTests` renamed in spirit but kept as-is; wall-specific coverage added inline) for the new edge-selection/connectivity logic. **87/87 total passing.**
- Ran the real generator end-to-end: **5/5 levels saved (Levels 11–15)**, every one landed on a walled, necessity-verified, uniquely-solvable board on the first pass (no fallback warnings) — stronger than expected, similar to the Blocked-cell batch.
- **Verified in live gameplay, not just via the offline pipeline:** entered Play Mode, loaded Level 14 directly, and confirmed via direct state inspection that the board carries exactly 1 Blocked cell and the wall shows on **2** cells rather than 1 — correct, since `BoardGenerator.NormalizeWalls` mirrors a one-sided authored wall onto its neighbour at real load time, exactly as designed. Zero console errors/warnings through the load.

**Issues found:** none — every hand-designed piece (edge selection, necessity gating, cross-run dedup) worked as intended on the first full run, unusual for how much new logic landed at once.

### 6.8 Levels 16–20 built (One-Way)

**Done:**
- **`LevelGenerator.PlaceOneWayCells`** (new) — chooses interior (non-dot) path cells and locks each one's `requiredEntryDirection` to whatever direction the intended snake solution actually travels when it steps into that cell. This is a stronger constraint-placement technique than Blocked/Wall: rather than merely *avoiding* the solution (don't block/wall a cell or edge the path needs), it *derives the mechanic's parameter from* the solution (the entry direction isn't chosen freely, it's read off the snake). Never chosen from a dot cell or the snake's very first cell, since a One-Way constraint on an endpoint isn't meaningful (a pair's path touches its own dot exactly once, at whichever end it happens to be).
- **Pipeline reordered inside `TryBuildCandidate`:** segments are now cut *before* Wall/One-Way placement, not after — dot/endpoint positions have to be known before a mechanic can correctly avoid landing on one, and that wasn't true when only Blocked+Wall existed (Wall doesn't touch cells, only edges, so it never needed this; One-Way does).
- **`GenerationSpec` simplified:** replaced the two growing per-mechanic booleans (`RequireBlockedCellsNecessary`, `RequireWallsNecessary`) with one `RequireMechanicsNecessary` flag covering every mechanic uniformly, and generalized `AllBlockedCellsAreNecessary` into `AllCellsOfTypeAreNecessary(grid, rows, cols, BlockType)` (Blocked and One-Way now share one scan; Wall keeps its own, since a wall is an edge property with no `BlockType` to scan for). Done now rather than after a fourth mechanic added a third near-identical bool, since the shape was already clearly repeating.
- **5 new tests** for the interior-cell selection and direction-of-travel logic specifically. **92/92 total passing.**
- Ran the real generator end-to-end: **5/5 levels saved (Levels 16–20)**, every one landed on a necessity-verified, uniquely-solvable board on the first pass. Levels 19–20 combine One-Way + Wall + Blocked simultaneously (per spec's "One-Way + Blocked + Wall") with zero special-case code — the three placement/necessity systems compose independently, exactly as designed when Wall+Blocked were first combined in Level 14.
- **Verified in live Play Mode:** loaded Level 20 directly, confirmed the board carries all three mechanics with the One-Way cell's `RequiredEntryDirection` correctly readable off the live `Block`, wall correctly mirrored per `BoardGenerator.NormalizeWalls`, zero console errors.

**Issues found:** none — every piece worked as intended on the first full run, including the pipeline reorder (verified via the full 92-test suite staying green through the change).

**Explicitly not done, and why (updated):** Levels 21–200 (Arrow through Shared Destination, all combination/mastery levels) are still not built. Remaining scope: 6 more mechanics' worth of generator work plus 180 more levels of content. Arrow (next, levels 21–25) is expected to reuse most of One-Way's placement shape (same "derive the parameter from the solution's own direction of travel" technique, just recording the *exit* direction instead of the entry direction) — worth checking that assumption before writing it, not just assuming it.

### 6.9 Bug found and fixed: mirrored walls rendered all 4 sides instead of 1

**Reported by the user, playing Level 14:** "there is wall mechanics, then why all 4 corners have the wall activated, and only one is restricting." The level's *data* was correct (only one edge authored, at cell (3,1)); this was a pre-existing visual bug in `Block.cs`, not a generator/data bug — it just took a generated level authoring a wall to surface it, since no hand-authored level had exercised this exact path before.

**Root cause:** `BoardGenerator.NormalizeWalls` mirrors a one-sided authored wall onto its neighbour by calling `Block.AddWall` on the neighbour. For a neighbour cell whose own level data named *no* wall at all, this is the first time that cell's wall-bar prefab group gets instantiated (`EnsureWallGroup`/`ShowWallBar`, called lazily from inside `AddWall`). Every other cell gets its wall-bar group created earlier, during `SetBlock`, which — right after creating the group — explicitly deactivates the 3 unused sides. `AddWall`'s lazy first-time instantiation never ran that same cleanup, so the neighbour's wall bar came up showing all 4 sides exactly as the source prefab authored them, instead of just the one mirrored side.

**Fix:** [`Block.AddWall`](Assets/Script/GamePlay/Block.cs) now captures whether the wall group is being created for the first time (`wallGroup == null` before `ShowWallBar` runs); if so, it runs the same "deactivate the 3 sides not in use" loop that `SetBlock` normally does, right after the group is created.

**Verified:**
- Compiles clean, full suite still **92/92 passing** (this only touches `AddWall`'s one-time cleanup path, not any rule logic the tests exercise).
- Live in Play Mode: reloaded Level 14, reflection-inspected every cell's actual wall-bar `activeSelf` state against `HasWall()` truth — cell (3,2) (the mirrored neighbour) now shows only `Left` active, matching (3,1)'s `Right`; zero mismatches board-wide.
- Swept **all 7 wall-bearing levels** (11, 12, 13, 14, 15, 19, 20) the same way in one pass — zero mismatches on any of them. No level assets needed regeneration; this was purely a rendering bug, solvability/data was never affected.
- No regression test added: `BlockTestHarness` (used by all existing `BlockRulesTests`) deliberately bypasses `SetBlock`/`AddWall`'s visual GameObject path via reflection on the pure rule predicates, so it structurally cannot reproduce or guard this bug. A real regression test would need to instantiate the actual `Block.prefab` via `AssetDatabase` and drive `SetBlock`+`AddWall` for real — left as a follow-up, not done here, to keep this fix scoped to the reported bug.

**Issues found:** the bug above (now fixed). No other issues surfaced.

### 6.10 Levels 21–25 built (Arrow)

**Done:**
- **`LevelGenerator.PlaceArrowCells`** (new) — chooses interior (non-dot) path cells and locks each one's `forcedExitDirection` to whatever direction the intended solution actually travels when it steps *out of* that cell — the mirror image of `PlaceOneWayCells`'s entry-direction technique. Never chosen from a dot cell, the snake's very last cell, or a cell One-Way already claimed (Arrow and One-Way are mutually exclusive `BlockType`s on the same cell).
- **`GenerationSpec.ArrowCount`**, `TryBuildCandidate` wiring (`forcedExitGrid` alongside the existing `requiredEntryGrid`), `BuildBlockGrid` reading `forcedExitDirection` back out, and `AllCellsOfTypeAreNecessary(grid, rows, cols, BlockType.Arrow)` reusing the same generalized necessity scan One-Way already shares with Blocked — no new per-mechanic scan needed.
- **6 new tests** for the interior-cell selection and exit-direction logic. **97/97 passing** before the bug below was found, **100/100** after its fix added 3 more (2 regression tests below, 1 for the new `IsRowMajorBefore` helper).

**A real bug found while dry-running the first candidate, not just a slow generator:** the very first Level 21 attempt failed `ValidateSolvability` outright (`Unsolvable`), and a manual replay of the intended solution against the actual built `Block` grid showed every individual rule check (`CanExitFrom`, `CanEnterFrom`, walls) passing cleanly — so the intended path itself wasn't the problem. Root cause: `PuzzleSolver.Solve` always starts a pair's search from `dots[pairId][0]`, and `BoardTopology.CollectDots` fills that list in board **row-major scan order** — not from "whichever end the generator's own snake happened to list first." So the solver sometimes walks a segment in the exact reverse of the direction `PlaceArrowCells`/`PlaceOneWayCells` derived their forced direction from, and a genuinely directional mechanic (unlike Blocked/Wall, which don't care about direction) then rejects the *only* solution that exists — Arrow's own head-on rule even blocks entry from the "right" side outright in that case. This silently doubled the average attempts needed for *any* directional-mechanic level (One-Way included) — Levels 16–20 already shipped are unaffected in correctness (only a *solvable* candidate is ever saved, so a "wrong-direction" attempt is just discarded and retried), but generation for both mechanics was roughly 2× slower than necessary and logged a lot of spurious `LevelValidator` errors along the way.
- **Fix:** `TryBuildCandidate` now computes a `reversedByCell` lookup per segment — comparing each segment's two endpoints via a new `IsRowMajorBefore` helper (the exact comparison `CollectDots`'s scan uses) to determine whether the solver will walk that segment in the snake's own array order or the reverse. `PlaceOneWayCells`/`PlaceArrowCells` both take this lookup and flip to `BoardTopology.Opposite(...)` of the other direction when a segment is reversed.
- **Verified:** a controlled single-level dry run went from **14.4s (systematically failing almost every attempt)** to **~1.2s** after the fix; a full 5-level dry run (with fresh seeds) completed in **~23s total** with a normal, non-systematic failure rate. 2 new regression tests added to each of `LevelGeneratorOneWayTests` and `LevelGeneratorArrowTests` (`..._IsReversed_WhenSegmentIsReversed`), plus a test for `IsRowMajorBefore` itself.
- Ran the real generator end-to-end: **5/5 levels saved (Levels 21–25)**, every one landed on a necessity-verified, uniquely-solvable board (`solutions=1 (unique)` on all five). Levels 24–25 combine Arrow + Blocked + Wall (not One-Way, matching how One-Way's own combination levels didn't reach back further than the two mechanics immediately before it) with zero special-case code.
- **Verified in live Play Mode:** loaded all 5 levels directly, confirmed each board's `BlockType.Arrow` cell and `ForcedExitDirection` are correctly readable off the live `Block`, Levels 24–25 correctly carry Blocked + Wall alongside it, zero console errors (aside from expected `DOTween` teardown warnings from switching levels programmatically five times in a row faster than any real player would, not a production issue).

**Issues found:** the row-major-scan-direction bug above (now fixed, and made retroactively more efficient for One-Way's own generation too — no effect on the already-shipped Levels 16–20 themselves).

**Explicitly not done, and why:** Levels 26–200 (Forbidden through Shared Destination, all combination/mastery levels) are still not built. Remaining scope: 5 more mechanics' worth of generator work plus 175 more levels of content.

### 6.11 Real player feedback: Level 1 left the board mostly empty after connecting both pairs — Levels 1–10 recalibrated

**Reported by the user, after actually playing Level 1:** "our first rule says create a level in such way that while pairing all cells should occupied. but that did not happen." Tested live rather than assumed: `PuzzleSolver.Solve` on the shipped Level 1 confirmed it **was** genuinely solvable with full coverage (2 valid solutions, both covering all 25 cells) — so this wasn't a generation/solvability bug. The real cause: Level 1's 2 colours split the 25-cell board very unevenly (a 6-cell pair and a 19-cell pair), and a pair only needs its two dots *connected* to register as "complete" (chime, pair counter) — full-board coverage is a separate, additional win condition the game never communicates. A player who drags the 19-cell pair the "obvious" direct way (distance 4) leaves 14 cells silently empty with zero feedback that anything is missing.

**The user's explicit steer, when asked whether to add UI feedback (an empty-cell counter, highlighting, a tutorial popup) instead:** no — *design the levels themselves* so that connecting the pairs naturally fills the board, rather than teaching the player a hidden rule.

**A geometric reality had to be surfaced before that could be done honestly:** a colour's *direct* path can only ever equal its full solution when that colour's path fits inside a single straight run of the grid — a general multi-colour full-coverage board cannot make every "obvious drag" the right answer, since a segment longer than the grid's width must reverse direction at least once. This is measured, not asserted: **`MaxSlackAcrossSolution`** (new) computes, per colour, `(actual path length) − (Manhattan distance between that colour's two dots)` off the real solved board — the size of the gap between "what a player would naively guess" and "what's actually required." Empirically, holding `StraightnessBias` at 0.95:
- 2 colours on 5×5 (original Level 1's shape): floors around **slack=10**, regardless of straightness — a ~12-cell segment cannot fit in one 5-cell run.
- 2 colours on 4×4: floors around **slack=4**.
- **4 colours on 4×4: floors around slack=0–2**, most segments short enough to fit one run.

The user's own follow-up mid-fix — "we can increase the color" — confirmed more colours (not fewer, despite the original spec's "2-3 colours" framing for early levels) was the right lever, and matched what the data already showed.

**Fix, all in `LevelGenerator.cs`:**
- New `GenerationSpec.MaxSlackPerColor` + `MaxSlackAcrossSolution`, wired into `TryGenerateLevel`'s existing penalty-based acceptance (same graceful-fallback shape as `UniquenessPolicy`/`RequireMechanicsNecessary` — never leaves a gap, just tries hard to satisfy it).
- **Levels 1–5 now generate on a 4×4 grid** (not the campaign's usual 5×5) **with 3–4 colours** (not 2–3), `StraightnessBias` raised to 0.95 at Level 1 (was 0.8), and `MaxSlackPerColor` capped at 4. Levels 6–10 (Blocked Cell introduced) are unchanged at 5×5.
- `SpecForLevel1To10` signature simplified (`gridSize` is now decided internally per level, not passed in uniformly) and its own doc comment carries the full empirical reasoning above.

**Verified (first pass):**
- 100/100 tests still passing (no rule-logic touched — this is entirely a generation-time scoring/spec change).
- Regenerated Levels 1–10: new **Level 1** — 3 colours, unique solution, `maxSlack=2` (down from 14) — a live solve shows two of its three colours at **slack=0** (the direct drag between dots literally *is* the answer) and the third only a 2-cell detour. Levels 2–5 landed `maxSlack` between 0–4; Levels 6–10 (5×5, unaffected by this fix) stayed at their prior ~6–10 range.
- Verified live in Play Mode: reloaded Level 1, re-ran the solver against the actual board, confirmed the same low-slack result on the shipped asset (not just the generator's own dry run).

**Round two: still visibly wrong in actual play.** The user played the recalibrated Level 1 and sent a screenshot — all 3 pairs connected (Pair 3/3, Moves 6), and 2 cells still sat black/empty (the remaining slack=2 on the orange pair). Their instruction: keep working on Levels 1–5 specifically, and confirmed increasing colour count further was fine. A larger empirical sample (40 candidates each) showed **3 colours on 4x4 never reaches slack=0** (floors at 2 every time), while **4 colours reaches it ~28% of the time**. Since anything short of zero is still a cell the player has no way to expect, `MaxSlackPerColor` was tightened to a **hard 0** for Levels 1–5 (not "close to the floor"), with colour count fixed at exactly 4 (not a 3–4 range, since 3 can't satisfy the new cap at all) and `MaxAttempts` raised to 600 for this range to give the now-two-simultaneous constraints (slack=0 AND the score band) more room before falling back.

**Verified (round two):** regenerated Levels 1–10 again — **all of Levels 1–5 landed `maxSlack=0`, each with a unique solution**, no fallback warnings logged (the target was actually met, not settled-for). Levels 6–10 unchanged at this point. Re-verified live in Play Mode across all 5 levels directly against the shipped assets: every colour on every one of Levels 1–5 has slack=0 — the direct drag between any pair's two dots is now, literally, the complete and only solution for that colour.

**Round three: extended the same hard-zero standard to Levels 6–10, on request.** The user asked to continue with 6–10 rather than leave them at the original, merely-reduced 5x5/2-3-colour calibration. A fresh empirical sample (21–40 candidates each, `StraightnessBias` 0.95, 1 Blocked cell) found **4 colours on 5x5+Blocked never hits slack=0** (floors at 2, 24 usable cells / 4 colours still averages 6 cells each — too long to reliably fit one straight run) — but **4 colours on 4x4+Blocked hits it 40% of the time** (16/40), actually *better* than 4x4 without a Blocked cell at all (~28%), since removing one cell trims just enough off some segments to land inside a run. Grid size, not colour count alone, is what makes zero slack reachable at this colour count — so **Levels 6–10 now generate on the same 4x4 board as 1–5** (not the original 5x5) rather than escalating grid size at the same time as introducing the Blocked Cell mechanic; the new mechanic is the difficulty step for this range, not a bigger board on top of it. `SpecForLevel1To10` was simplified accordingly: grid size and colour count are now fixed constants (4 and 4) across the whole 1–10 range rather than branching per level, with `MaxSlackPerColor=0` and `MaxAttempts=600` applied uniformly too. Grid size still ramps up starting at Level 11 (Wall), already on 5x5 and untouched by this change.

**Verified (round three):** regenerated Levels 1–10 a third time — **all 10 levels landed `maxSlack=0`, each with a unique solution**, no fallback warnings. Re-verified live in Play Mode across all 10 levels directly against the shipped assets: zero slack on every level, Blocked cell confirmed present (and necessity-gated, per the existing `RequireMechanicsNecessary` check) on Levels 6–10 specifically.

**Issues found:** the lack of full-coverage feedback itself is still not fixed (by explicit user direction — level design is the fix, not UI). This zero-slack standard now covers the entire Learning Phase's first ten levels; Levels 11–25 (Wall/One-Way/Arrow, all 5x5) were not part of this request and haven't been re-examined against it.

**Round four: correcting course after round three, per a direct design critique.** The user, asking to be treated as a senior design engineer, called Levels 1–10 "too basic, there is no challenge at all" — accurately. Round three's flat `MaxSlackPerColor=0` across all ten levels fixed the confusion completely but deleted the puzzle along with it: if a colour's direct drag between its own dots is *always* the entire solution, there is nothing left to discover, at any of the ten levels. The deeper bug: a slack CEILING alone only ever *permits* an easy candidate, it never *requires* a harder one — raising it doesn't guarantee more challenge, since the scoring loop is still free to prefer whatever's easiest to reach.

**The fix:** `MinSlackPerColor` (new) turns the single ceiling into a two-sided TARGET BAND, penalised the same way as `TargetScoreMin/Max` via `BandPenalty`. `SpecForLevel1To10` was rewritten from a flat spec into an explicit per-level ramp (grid size, colour count, `StraightnessBias`, and slack band all chosen per level, each band empirically sampled at ≥11 built candidates first): Level 1 stays at slack [0,0] (still risk-free, still teaches the concept cleanly) but every level after ramps a genuinely harder-to-find detour — [0,2] → [2,4] → [2,6] → [6,10] (Basic Flow's capstone, landing in DifficultyAnalyzer's *Medium* tier) — then Level 6 deliberately RESETS to [0,2] when Blocked Cell is introduced (never compound "brand new rule" with "hardest puzzle yet" — teach one thing at a time), before ramping again through [4,6] → [6,8] → [8,12] → [10,16] to a real capstone at Level 10. Grid size now also grows (4×4 for Levels 1–3 and 6, 5×5 from Level 4 and again from 7 onward) and colour count varies (4 → 3 → 2) rather than being pinned at a flat 4×4/4-colours throughout — both were part of why every level scored in the same narrow "Easy" band in round three regardless of slack. `TargetScoreMin/Max` was deliberately loosened to fully permissive ([0,100]) for this whole range: slack plus grid/colour/mechanic changes are now the deliberate difficulty control, and fighting the score band on top of a slack band risked exhausting `MaxAttempts` chasing two targets at once for no real benefit.

**Verified:** regenerated Levels 1–10 a fourth time, no fallback warnings on any level. The resulting per-level report shows a genuine escalation: `maxSlack` 0→2→2→4→6 (Levels 1–5, tier Easy→Easy→Easy→Easy→**Medium**), reset to 2 at Level 6, then 4→6→8→10 (Levels 7–10, tier Medium throughout, Level 10 scoring highest of the whole range at 49.6). Re-verified live in Play Mode across all 10 levels directly against the shipped assets — the live-solved `maxSlack` matches the generator's own figures exactly at every level, confirming the ramp is real on the actual shipped content, not just in the generator's own bookkeeping.

**Issues found:** none new — this is a direct, requested correction to round three's overcorrection, not a bug. Worth remembering for every future mechanic range: a slack (or any difficulty) constraint expressed as a bare ceiling silently caps challenge at zero if nothing else pushes back; express it as a band from the start.

**Round five: slack itself declared a permanently rejected difficulty axis, after a Level 7 screenshot.** The user hit exactly the "redraw needed" case round four was designed to produce — all 3 pairs connected, several cells still empty — and pushed back a final time: *"make it a hard rule... while connecting colors, it should cover all the cell. i have seen this mistake in multiple levels from 1-10."* This overrides round four outright: slack — any amount, not just a large one — is now a permanently rejected axis for this range, not a dial to revisit next time challenge feels low.

**Fix:** `MinSlackPerColor`/`MaxSlackPerColor` both pinned to a hard `[0,0]` for every level, permanently. With slack no longer available, colour count (4→5→6, empirically the one lever that stays *reliable* at zero slack — sampled ≥15 times per configuration, 4x4 hits zero 28–93% of the time depending on colour count and whether a Blocked cell is present, while 5x5 at the same colour counts is fragile-to-impossible, e.g. 0/11 samples for 5 colours+Blocked) and `UniquenessPolicy` (Ignore→Prefer→Require) became the only remaining ramp, with grid size held at a constant, reliable 4x4 throughout — abandoning round four's grid-size growth and slack band entirely, not just tuning them.

**A second, more fundamental finding surfaced while verifying this:** the regenerated levels' `DifficultyAnalyzer` scores did not rise with colour count — **they fell** (Level 1: 21.3 "Easy" → Level 10: 13.3 "**VeryEasy**", despite Level 10 having the most colours, a Blocked cell, and a required-unique solution). Root cause, read directly out of `DifficultyAnalyzer.cs`: **6 of its 10 weighted factors are structurally pinned near zero the instant slack=0** — path-winding (15% weight) is *exactly* 1.0 by definition with zero detour, decision-density (15%) collapses because a zero-slack path is almost entirely forced moves, dead-ends (10%) barely occur, and grid-size (10%) is flat at a held-constant 4x4. That is half the score formula permanently capped low under this hard rule; colour count only carries 10% weight, not enough to outweigh the other factors actually shrinking as segments got shorter. **Zero slack and a rising difficulty score are close to mutually exclusive on this board, by the game's own difficulty model** — more colours add real busy-ness (more pairs to track, tighter clicking) but neither the model nor the underlying puzzle rates that as harder, since there is no search or discovery left once every path is forced.

**Put to the user directly rather than silently shipped:** given the hard rule's cost is now measured, asked whether to (a) accept Levels 1–10 as the genuine beginner tier — VeryEasy/Easy by design, with real difficulty deferred to Level 11+ where stronger mechanics (Wall/One-Way/Arrow, mechanic weights 2–2.5 vs Blocked's 1) carry far more of the score formula's weight, (b) extend zero-slack campaign-wide and accept the same ceiling everywhere, or (c) reconsider the hard rule now that its cost is known. **Chosen: (a).** The zero-slack hard rule stays scoped to Levels 1–10 only; Levels 11+ are not required to hold to it, and their own difficulty is expected to come from mechanic complexity rather than needing this same treatment.

**Verified:** the already-regenerated Levels 1–10 satisfy this design as shipped — no further regeneration needed. Live in Play Mode against the actual assets: all 10 levels solve at `maxSlack=0` (4, 4, 5, 5, 6, 4, 5, 5, 6, 6 colours respectively), matching the generator's own log exactly.

**Issues found:** none — Levels 1–10's low `DifficultyAnalyzer` score is now a known, accepted, and understood property of this range (the true beginner tier), not a defect. Standing note for every future mechanic range (11+): do not import the zero-slack hard rule by default — it was scoped to 1–10 specifically because that range has no mechanic strong enough to carry difficulty any other way; a range with a heavier mechanic (Wall, One-Way, Arrow, Bridge, ...) has real room to use slack as a difficulty axis if it wants to, without repeating this range's ceiling.

### 6.12 The full-coverage rule, finally expressed correctly — and difficulty restored with it

Round five closed by accepting a hard difficulty ceiling as the price of the coverage rule. The user rejected that trade-off outright ("the measure difficulty should go up… we need to make game challenging"), and they were right to: **the ceiling was an artefact of enforcing the rule the wrong way, not a real constraint.**

**The mistake, stated plainly.** Every pass from §6.11 constrained *slack* — how far a colour's real path exceeds the direct route between its own two dots. But slack is a property of the **intended** solution, while the actual bug is about **other** arrangements. The Level 7 screenshot showed a player who had connected all three pairs via a *different* pairing than the intended one, and that pairing left cells empty. Clamping slack to zero does prevent this, but only by making every path rigid — which is why every attempt kept trading the rule against the puzzle. The two were never genuinely in conflict; the lever was simply wrong.

**The correct rule, now implemented:** *there must be no way to connect every pair and still leave a cell empty.* Checked directly by `LevelGenerator.EveryPairingCoversTheBoard`: run the solver with the full-coverage win condition switched **off**, enumerate every ordinary connect-the-pairs arrangement, and require that all of them fill the board anyway. Slack is left completely unconstrained, so paths are free to wind.

**Supporting changes:**
- **`PuzzleSolver.SolverOptions.AllowPartialCoverage`** (new) — relaxes the coverage gate at `Search`'s terminal branch. Stored *inverted* deliberately: `SolverOptions` is a struct, so `default(...)` and both existing constructors must keep meaning "full coverage required". Gameplay and `LevelValidator` are untouched.
- **`PuzzleSolver.SolveResult.AllSolutions`** (new) — the full solution set, not just the first, since this rule must reason about *all* arrangements.
- **Enforced as a HARD REJECT, not a penalty.** Penalties only *rank* candidates and the best-ranked one still ships as a fallback, which would let a rule-breaking board through. It is also placed *after* the `penalty >= bestPenalty` ranking check purely for cost (it is by far the most expensive gate), but before anything is recorded — so every candidate that can still become `best`, fallbacks included, has passed it. Conservative about uncertainty: a search that hits its step budget, or its 200-arrangement enumeration cap, returns *reject* rather than assuming the unexplored branches are fine.
- **3 new tests** (`PartialCoverageSolveTests`) pinning the invariant on a 2×2 board that is the Level 7 bug at minimum size — one pair on the top row, where the direct connection uses 2 of 4 cells. Verifies the default still rejects the shortcut, that `AllowPartialCoverage` *does* surface it, and that `AllSolutions` is populated consistently. **103/103 passing.**

**With slack free, board size and colour count became real levers again.** Measured (25 candidates per configuration, scoring only those that pass the rule): 4×4/4c ≈ 21 · 4×4/3c ≈ 31 · 5×5/5–6c ≈ 40 · 6×6/7–8c ≈ 43–44. Two findings worth keeping: on a **fixed** board, *more* colours means shorter, more forced segments and an **easier** puzzle — which is exactly why round five got easier as it added colours; colour count only helps when the board grows with it. And 5×5 at only 4 colours never passed the rule once in 40 samples — long paths on an open board leave too much room for some other pairing to short-cut.

**Capped at 5×5 despite 6×6 scoring higher**, on cost grounds honestly stated: the rule enumerates the whole pairing space, and a single 6×6 check runs on the order of seconds, which at this rule's ~4–16% hit rate turns one level into an unbounded multi-hour generation. 6×6 is real, measured headroom for Levels 11+.

**One more correction inside this round.** The first regenerated run left target scores fully permissive, so the loop accepted the *first* candidate clearing the coverage rule (penalty already 0 → immediate break) and difficulty became whatever luck dealt — a visibly jagged curve (20.6, 37.0, 22.4, 26.5, 39.9, **12.2**, …). Adding per-level `TargetScoreMin/Max` bands makes the search keep hunting until the level is as hard as it should be.

**Verified:**
- **Final ramp, no fallback warnings on any level** (every band genuinely met, not settled for): **20.6 → 26.5 → 30.1 → 35.6 → 39.5**, deliberate reset to **24.3** at Level 6 where Blocked Cell is introduced on a smaller board, then **35.9 → 40.2 → 36.1 → 39.9**. Top end roughly **doubles** round five's (which peaked at 27 and *fell* to 13.3), and Level 8 reaches the Medium tier.
- **The hard rule holds on all 10 shipped assets**, checked by enumerating every pairing of each saved level: all 10 report `partialPairings=0` with the search exhausted. The Level 7 state is now structurally unreachable, not merely unlikely.
- Live in Play Mode: all 10 load cleanly (4×4 and 5×5, 3–6 pairs, Blocked cell present on 6–10, pair goals correct), zero console errors.

**Issues found:** none outstanding. Two standing notes: (1) the earlier `MinSlackPerColor`/`MaxSlackPerColor` band machinery is retained but now unused by this range — it is a legitimate difficulty lever for a *later* range and was left in place rather than deleted; (2) do not assume more colours means a harder level — on a fixed board the relationship is inverted, and this cost two full regeneration rounds to notice.

### 6.13 Blocked Cell made a mechanic the player actually learns

Two related notes from the user, the second the more important: *"why there is only one blocked block, we can even use multiple too"*, and *"for the first time use introduced blocked block it does not look challenging — player should know while playing it's mechanics."*

**The real defect was placement, not count.** `PlaceBlockedCells` drew coordinates with `rng.Next(size)` over the whole board, so on a 4×4 there was a 12-in-16 chance the single blocked cell landed on the outer ring. In that position, routing around it is indistinguishable from the board simply being smaller there — nothing obliges the player to notice it, so a level "introducing" the mechanic could be completed without the mechanic ever registering. A cell is only legible as a mechanic when paths have to bend around it *on both sides*, which requires it to be interior.

**Fixes:**
- **`PlaceBlockedCells` gained an `interiorOnly` mode**, confining placement to cells off the outer ring; `GenerationSpec.BlockedCellsInteriorOnly` turns it on, and Levels 6–10 all set it. It fails loudly if asked for more interior cells than the board has (an authoring error, e.g. 5 on a 4×4 whose interior is only 2×2) rather than silently placing fewer.
- **Blocked count now ramps 2, 2, 2, 3, 3** across Levels 6–10 instead of a flat 1.
- **2 new tests** pinning both guarantees (no blocked cell on the outer ring; over-request throws). **105/105 passing.** Two existing tests were updated for the new signature.

**A finding that reshaped the ramp: blocked cells make a level EASIER by the difficulty model.** `DifficultyAnalyzer` scores only usable cells, so excluding cells shortens paths and removes decisions — and blocked cells are skipped entirely by the constrained-cell ratio, so they add almost nothing back. Measured on a fixed 5×5: 0 blocked ≈ 40, 3 blocked ≈ 21, 4 blocked ≈ 15–33. Simply adding blocked cells would therefore have made Levels 6–10 *easier* exactly where they are supposed to get harder. They have to be paid for with board size, so Levels 8–10 moved to 6×6.

**This also corrected §6.12's "capped at 5×5" decision, which was wrong.** That call was made on a 0-out-of-9 sample and generalised too confidently. Counting pairings directly showed both why the rule is so restrictive and why 6×6 is in fact viable: **the rule effectively requires the board to have exactly one possible pairing** — once more than one exists, nearly all the extras are the partial-coverage kind (one 6×6 sample: 104 pairings, 102 partial; another: 20 with 18 partial). 6×6 boards with a single pairing do occur, so the earlier failure was seed luck plus the enumeration cap, not a structural limit. `PairingEnumerationCap` doubles as the cost guard: a board with more pairings than the cap is rejected cheaply as unproven, which is almost always the right answer anyway.

**Verified:**
- **Final ramp, no fallback warnings on any level:** 20.6 → 26.5 → 30.1 → 35.6 → 39.5, deliberate reset to **24.4** at Level 6 (mechanic introduction), then 35.4 → 37.8 → **42.8** → **43.4**, the last two reaching the **Medium** tier — the highest this range has scored, with the mechanic present rather than at its expense.
- **Both guarantees hold on all 10 shipped assets**, checked against the saved levels: every level reports `pairings=1, partial=0` (hard rule intact), and every blocked cell is interior — L6 (1,1)(1,3), L7 (2,2)(3,1), L8 (2,1)(2,4), L9 (3,3)(4,1)(4,3), L10 (2,2)(3,1)(4,3), with `edgeBlocked=0` throughout.
- Live in Play Mode: Levels 6–10 load cleanly at 5×5 and 6×6, blocked cells render (2,2,2,3,3), 5–8 pairs with correct goals, zero console errors.

**Issues found:** none outstanding. Standing note: when a mechanic is introduced, check it is *legible in play*, not merely present in the data — "the validator says it is load-bearing" and "the player can see it doing something" are different claims, and only the first was being tested before this round.

### 6.14 Path length: the metric that actually matches how the game feels

*"While playing I did not even feel challenged. Looks like the path we are drawing [is] too short."* Measuring the shipped levels confirmed it immediately: mean path length was **3.8–5.3 cells**, and several levels contained **2-cell paths** — two adjacent dots, one drag, nothing to work out (L10 was `[2,3,4,4,4,4,5,7]`).

**Root cause: the optimisation target itself was wrong, and had been for several rounds.** The coverage rule (§6.12) needs the board to have exactly one possible pairing. Every previous round bought that uniqueness by *adding colours* — which is precisely what shortens paths. Worse, `DifficultyAnalyzer`'s score **rewards** grid size and colour count, both of which rise when a board is packed with more, shorter paths, so optimising the score actively drove levels toward feeling thinner. The divergence is measurable and stark: a 5×5 whose three colours each ran 7 cells scored **20.9**, while the 6×6 of eight paths averaging 4 (including a 2 and a 3) scored **43.4**. The score claimed the second was twice as hard; play said the opposite.

**The fix — buy uniqueness with blocked cells rather than colours.** Blocked cells close off the alternative pairings a sparse, few-colour board would otherwise have, which lets colour count *drop* while path length climbs. This finally gives the Blocked Cell mechanic a reason to exist beyond being present: it is what *makes* long paths possible. Measured: 6×6 with 4 colours + 6 blocked yields `[6,6,7,11]`.

**Changes:**
- **`GenerationSpec.MinPathCells`** — hard reject if *any* colour's path is shorter than the floor. Guards the floor rather than the mean, precisely because a mean hides a 2-cell pair. Checked immediately after the solve, before anything expensive.
- **`GenerationSpec.TargetAvgPathMin/Max`** — a band on mean path length, now the **ranking criterion in place of the difficulty score**. `TargetScoreMin/Max` is deliberately left permissive `[0,100]` for this range; the score is still computed and logged, just not steered toward.
- **`MeasurePathLengths`** helper; shortest/average recorded on `GeneratedLevel` and printed in the generation report.
- Levels 6–10 re-specced to **3–5 colours with 3–6 blocked cells** (was 5–8 colours with 2–3), inverting the previous direction.

**Verified — path lengths roughly doubled at the top, with no trivial pairs anywhere:**

| Level | Board | Colours | Blocked | Path lengths | mean |
|---|---|---|---|---|---|
| 1 | 4×4 | 4 | 0 | `[3,3,5,5]` | 4.0 |
| 2 | 4×4 | 4 | 0 | `[4,4,4,4]` | 4.0 |
| 3 | 4×4 | 3 | 0 | `[4,4,8]` | 5.3 |
| 4 | 5×5 | 5 | 0 | `[4,4,4,5,8]` | 5.0 |
| 5 | 4×4 | 3 | 0 | `[5,5,6]` | 5.3 |
| 6 | 5×5 | 4 | 3 | `[5,5,6,6]` | 5.5 |
| 7 | 5×5 | 3 | 4 | `[5,7,9]` | 7.0 |
| 8 | 6×6 | 5 | 5 | `[5,5,6,7,8]` | 6.2 |
| 9 | 6×6 | 4 | 6 | `[6,6,7,11]` | 7.5 |
| 10 | 6×6 | 4 | 6 | `[6,7,8,9]` | 7.5 |

- No fallback warnings on any level — every path-length band was genuinely met.
- **All three guarantees hold on the shipped assets**: every level reports `pairings=1, partial=0` (coverage rule intact), `edgeBlocked=0` (placement rule intact), and no path ≤ 2 cells anywhere (the shortest on the whole range is a single 3 on Level 1).
- Live in Play Mode: all 10 load cleanly at 4×4/5×5/6×6, blocked cells render 0,0,0,0,0,3,4,5,6,6, pair goals correct, zero console errors. **105/105 tests passing.**

### 6.15 Researched the genre, then replaced the solution constructor

Asked to stop tuning blind and research how these puzzles are actually built. Flow Free is an implementation of **Numberlink**, a Nikoli puzzle type with established conventions ([Nikoli](https://www.nikoli.co.jp/en/puzzles/numberlink/), [Wikipedia](https://en.wikipedia.org/wiki/Numberlink), [thomasahle/numberlink](https://github.com/thomasahle/numberlink), [UKPA discussion](https://forum.ukpuzzles.org/viewtopic.php?t=41)):
- **No path may touch itself** — two same-colour cells that are orthogonally adjacent must be consecutive along the path. Ahle's generator states its solver "assumes the solution uses 100% of the paper and no link touches itself."
- **Well-designed = unique solution + every cell filled.** The user's hard rule is the genuine Numberlink standard, independently arrived at.
- **Pair count ≈ √(width × height)** is the established default.

**A hypothesis I held confidently and was wrong about.** I expected the snake-then-cut construction to produce self-touching paths everywhere, and that this explained the "mushy" feel. Audited all 10 shipped levels: **zero self-touching across 223 path cells**. The coverage rule already implies the Numberlink property — a self-touching path can generally be short-cut, which creates exactly the hole-leaving pairing the rule rejects. Worth recording because the fix that followed came from measuring, not from the theory.

**The real ceiling was the constructor.** Real Flow Free ships 5×5–6×6 as tutorial packs; its difficulty lives at 8×8+. The generator could not go there: building one Hamiltonian path over the whole board is exponential, and 7×7 did not merely run slowly, it **hung the editor**.

**`TryGeneratePathPartition`** replaces it. Asking for *k* paths that partition the board is strictly weaker than one Hamiltonian path and needs no exponential search: every colour's path grows a cell at a time, choosing next by **Warnsdorff's rule** (always take the free cell with the fewest free neighbours — the knight's-tour heuristic, which exists precisely to avoid stranding cells), with **shortest-path-first** as a tie-break to keep lengths even. Failure is cheap and expected — a stranded cell returns null and the caller retries.

| Board | old (Hamiltonian) | new (partition) |
|---|---|---|
| 6×6 | works | 0.0ms, 39/40 |
| 7×7 | **hung the editor** | 0.0ms, 39/40 |
| 10×10 | impossible | 0.1ms, 38/40 |

**Wall / One-Way / Arrow migrated, and the old constructor deleted** (~121 lines: `TryGenerateHamiltonianSnake`, `ExtendSnake`, `OrderedSteps`, `CutIntoSegments`, `DistributeLengths`, plus the temporary `UsePathPartition` flag and its guard). All three previously derived a direction from `index-1`/`index+1` in one contiguous list, which silently reads across a path boundary once the solution is several paths — a direction between two unconnected cells. They now take the per-path lists:
- One-Way/Arrow share a new **`InteriorPathCells`** helper returning `(path, index)` for cells strictly interior to a path. This *removed* a check rather than adding one: interior means "not an endpoint", and endpoints are exactly the dots, so dot exclusion is now structural. Arrow keeps an exclusion set only for cells One-Way already claimed.
- **`PlaceWalls`** builds its protected-edge set per path. It had been passed a concatenation, which invents a phantom edge at each junction — harmless there (it only over-protects) but wrong, and unsafe for the directional mechanics. All three now share one representation.

The existing One-Way/Arrow tests carried over with only an `AsPaths` wrapper and **no change to their assertions** — the candidate sets are identical for a single path, evidence the semantics were preserved rather than redefined.

**A correction to an earlier claim in this document.** §6.12 recorded 6×6 as viable and later notes implied 7×7 would follow. Levels 9–10 were specced at 7×7 and **failed outright**: the generator builds those boards in microseconds, but the coverage rule admits only ~11% of 7×7 candidates and 1500 attempts found none that also met a path-length band. They were moved back to 6×6 with more blocked cells. **The ceiling is the rule, not the board size** — and the solver is the next wall after that (full-coverage multi-pair solving is NP-hard; 6×6 settles inside 300k steps, 7×7 wants millions, 8×8 needs ~8M ≈ 1–3s). `SolverBudgetFor` now scales the budget by board size, because a flat budget was silently reporting Inconclusive and making large boards look impossible when they were merely unfinished.

**Verified — all 25 levels regenerated on the new constructor:**

| Range | Result | Tier |
|---|---|---|
| 1–10 (Basic + Blocked) | 10/10, avgPath 4.0→7.5, minPath 3→7 | Medium from L3 |
| 11–15 (Wall) | 5/5, wall present + necessity-gated | all Medium |
| 16–20 (One-Way) | 5/5, 19–20 combine One-Way+Wall+Blocked | all Medium |
| 21–25 (Arrow) | 5/5, 24–25 combine Arrow+Wall+Blocked | all Medium |

Path lengths on 1–10 now reach `[7,8,8,9]` (L8) and `[6,6,7,12]` (L9), against `[2,3,4,4,4,4,5,7]` before this work. All four guarantees re-checked on the shipped assets: **coverage rule holds** (`pairings=1, partial=0` on all 10), **blocked cells interior-only** (`edgeBlocked=0`), **no path ≤2 cells**, **zero self-touching**. Live in Play Mode, levels 11–25 carry their mechanics with directions correctly readable (e.g. L16 One-Way entry=Left, L21 Arrow exit=Up), zero console errors. **105/105 tests passing.**

### 6.16 The coverage rule was never applied to Levels 11–25 (found in play)

*"In level 11, connected all the pairs, still some cells are empty."* Checked, and it was not one level: **all 15 levels from 11–25 violated the rule**, because `RequireEveryPairingCoversBoard` was set in exactly one place — `SpecForLevel1To10`. The mechanic ranges never had it.

| Level | Pairings | Partial | Cells that could be left empty |
|---|---|---|---|
| 11 | 21 | 20 | 8 |
| 13 | 72 | 71 | 10 |
| 22 | 152 | 151 | 12 |
| 25 | 55 | 54 | 12 |

**How it was missed.** The rule was introduced (§6.12) to fix the same complaint on Level 7, and Levels 1–10 were then verified thoroughly. When the constructor migration (§6.15) later touched every range, 11–25 were re-verified for *mechanics only* — walls present, arrow directions readable — and the coverage check was never re-run on them. The thing that had just been changed got verified; the thing originally asked for did not.

**Fixed** by bringing all three mechanic specs up to the same standard as 1–10 rather than only setting the flag: 6×6 boards (was 5×5), 4 colours, interior-only blocked cells, `MinPathCells`, a path-length target band in place of the misleading difficulty score, and the coverage rule.

**Two structural findings surfaced while getting Arrow to generate:**

1. **The coverage rule and mechanic-necessity are in direct tension.** The rule forces a board to have exactly one pairing; necessity asks that *removing* the mechanic create a second solution. On a board geometry has already pinned to one answer, deleting a restriction rarely opens a new one, so the mechanic reads as decorative and the candidate is rejected. Instrumented rejection counts for Level 21 (1500 attempts): `minPath` 697, **arrow-necessity 722**, coverage 15, passes **0**. Relaxing path constraints did not help (minPath=4: still 0). An alternative definition — "removing the mechanic lets a partial-coverage pairing appear" — was implemented and measured, and also scored 0. The two rules together admit roughly **1 candidate in 2000**, which is why 21–22 failed at 12000 attempts while 23–25 happened to succeed.
2. **Board density is what makes the rule satisfiable.** Measured on the Arrow spec: blocked=1..4 produced zero candidates clearing the rule in 400 attempts; blocked=5 produced 2. An open board simply has more pairings, and the rule demands exactly one. Levels 1–10 pass routinely because they run blocked=4–6.

Resolved by raising the Arrow range to blocked 4–5 and `MaxAttempts` 60000 rather than dropping either rule. **A wrong first diagnosis worth recording:** the two failures were initially attributed to `StraightnessBias` (21 and 22 were the two straightest specs — a clean-looking correlation). Lowering it changed nothing; only per-gate instrumentation found the real cause. Measure before tuning.

**Verified — all 25 levels regenerated, 105/105 tests passing:**
- **Coverage rule holds on all 25** (the reported bug, fixed).
- Blocked cells interior-only: OK. No path ≤2 cells: OK.
- `UIController.totalLevelCount` set to 25, scene saved.

**Issue found — self-touching returned on 7 levels** (11, 13, 14, 17, 18, 20, 25; one or two instances each). This **corrects §6.15's claim** that the coverage rule implies the Numberlink non-self-touching property. It holds only on mechanic-free boards: there, a self-touching path can be short-cut, creating the hole-leaving pairing the rule rejects. Once a wall, one-way or arrow *blocks* that short-cut, the self-touching path stays uniquely forced and passes. The property was never implied by the rule — it was implied by the absence of mechanics, and levels 1–10 were the only evidence.

**Decision: not enforced, deliberately.** Inspecting an actual case (Level 11, pair 2: `(3,2)(3,3)(2,3)(1,3)(1,4)(2,4)(2,5)(3,5)` — steps 2 and 5 adjacent) shows the usual argument does not apply here. That argument is "if a line runs beside itself you could cut the corner, so a second solution exists" — but cutting from `(2,3)` to `(2,4)` would skip `(1,3)` and `(1,4)`, which full coverage forbids. The detour is genuinely forced, and the level is still uniquely solvable and fully covered. The convention comes from **pencil Numberlink, where filling every cell is not required**; in a 100%-coverage game paths are obliged to wind, and running alongside yourself is normal (real Flow Free levels do it constantly). Treated as a purity criterion, not a defect.

### 6.17 Walls as connected barriers, and the gate-ordering mistake that made generation slow

Two changes driven by the user, both measured on the shipped assets afterward.

**Walls now form connected barriers** (user's sketch: a horizontal run meeting a vertical one). `PlaceWalls` previously shuffled the legal non-path edges and took the first N, giving scattered one-cell stubs that each block a single step and read as noise. It now grows a connected run: two walls join when they share a lattice corner (`WallCorners` — cell (r,c) spans corners (r,c)..(r+1,c+1), so its Right edge is the segment (r,c+1)-(r+1,c+1) and its Down edge (r+1,c)-(r+1,c+1)), and after the first pick each subsequent one prefers a candidate touching a corner already used. Falls back to a random edge when the barrier cannot extend, so a board with few legal edges still gets its walls rather than failing. Levels 11–15 also raised from 1 wall to **2–4** — a single edge cannot form a barrier at all.

Unexpected benefit: connected barriers block more alternative routes, and the coverage rule needs the board to have exactly one pairing — so the hardest constraint in the pipeline got *easier*, not harder. Levels 11–15 generated 5/5 first try at the higher wall counts.

**The gate-ordering mistake.** Generation had grown to 28–40 minutes per range. Cause: `RequireMechanicsNecessary` ran *before* the coverage rule. Necessity is the most expensive check in the pipeline — `AllCellsOfTypeAreNecessary`/`AllWallsAreNecessary` clone the board and run **two extra solves per mechanic instance**, so a level with 2 walls + 2 blocked + 1 One-Way pays ~10 extra solves. The coverage rule costs ~0.5ms and rejects the large majority of candidates. Every candidate was paying the expensive test before the cheap one discarded it.

That ordering was correct when written — with `PairingEnumerationCap` at 200, coverage really was the expensive gate, and the code comment said exactly that. Cutting the cap to 2 (§6.16) inverted the economics, and the ordering was never revisited; the comment stayed plausible and wrong. **Stale optimisations do not announce themselves — when the cost of one stage changes by two orders of magnitude, re-check every decision that was justified by the old cost.**

Coverage now runs immediately after the duplicate check, before the difficulty analysis and before any necessity check.

| Range | before reorder | after |
|---|---|---|
| 16–20 | ~28 min, **4/5** (L19 failed twice) | 11 min, **5/5** |
| 21–25 | ~40 min, **3/5** | **~1 min, 5/5** |

Same 60000-attempt budget. It did not only speed things up: by not burning the budget on redundant solves, the search reached candidates it had never got to before, which is why Level 19 — which had failed at 60000 attempts twice — was found.

**Verified on all 25 shipped assets:**
- Coverage rule (no pairing can leave a cell empty): **OK, all 25**
- Blocked cells interior-only: OK · No path ≤2 cells: OK
- **Walls form a fully connected barrier: 9/9** multi-wall levels
- Levels 3–24 all Medium tier (25 is Easy at 36.5). 105/105 tests passing; `totalLevelCount` = 25, scene saved.

**Issues found (§6.15):** none outstanding. Standing note: the generator now scales far past what the *verification* pipeline can afford — board size is limited by the coverage rule's hit rate and the solver's NP-hard cost, not by construction. Reaching 8×8+ means either relaxing the coverage rule or investing in solver pruning (constraint propagation / forced-move deduction), not further generator work.

**Issues found (earlier rounds):** two standing notes: (1) `DifficultyAnalyzer.Score` should not be used as a generation target for any range where board size or colour count is being varied — it rewards packing in short paths and will silently drive levels the wrong way; path length is the honest proxy, and the analyzer's own weights are flagged in its class doc as an untuned first pass. (2) Level 8's mean (6.2) sits slightly below Level 7's (7.0) because its band bottomed out; its floor is still 5, so no trivial pairs — worth tightening if the range is ever revisited.

### 6.18 Relaxing the coverage rule on Levels 11–30: the change that finally made the puzzles work

The strict rule from §6.16 fixed the confusion but had a cost that only became visible once it was measured: **it forced every board to have exactly ONE possible pairing.** With no wrong routes, the player traces the only line that exists rather than searching — and mechanics cannot be load-bearing, because "necessary" means *removing it creates a second solution* and there is no second solution to create. That is why only 13 of 41 mechanic instances were doing anything, with every Arrow in the game decorative.

**Prototyped before committing** (levels 31–33, written outside the campaign so nothing shipped changed). One setting differed: `RequireEveryPairingCoversBoard = false`, with `Uniqueness = Require` so the FULL-COVERAGE solution stays unique. Result:

| | Board | Pairings | Wrong routes | Walls load-bearing |
|---|---|---|---|---|
| Shipped L13 | 6×6 | 1 | **0** | 1 / 3 |
| Shipped L20 | 6×6 | 1 | **0** | **0 / 2** |
| Prototype L31 | 6×6 | 408 | 407 | **2 / 2** |
| Prototype L33 | 7×7 | 500+ | 499 | **2 / 2** |

Generation also went from 8–40 minutes per range to **3 seconds**, and 7×7 — which the strict rule could not produce at all — worked first try. The user played them and confirmed they feel more challenging, so it was rolled out.

**What shipped:**
- **Levels 11–30 relaxed.** Each level still has exactly one winning solution, and `IsBoardFullyCovered` still gates completion, so wrong routes lose — they are attempts, not alternative wins.
- **Levels 1–10 deliberately keep the strict rule.** They are the tutorial; a new player who connects everything and faces empty cells has no idea what the game wants. Over ten easy levels that guarantee is worth more than the challenge it costs.
- **Necessity became a HARD REJECT**, which only became possible because of the relaxation — with wrong routes to rule out, a wall that eliminates one is genuinely load-bearing.
- **Board ramp:** Wall/One-Way at 6×6, Arrow/Forbidden at **7×7**.
- **The HUD counter now reads `Cells : 28/36` instead of `Pair : 3/4`.** Completion needs every cell covered, so a pair counter could read "4/4" — the game announcing the level is done — while refusing to end. That was the actual defect behind the Level 7 and Level 11 reports. Wired to the same occupancy test `IsBoardFullyCovered` uses, so the two cannot drift.

**Result across levels 11–30:** mechanics load-bearing **41/41** (was 13/41), every level has exactly one winning solution, no path under 3 cells, blocked cells all interior, wrong routes ranging 3–299 per level. Scores rose from 43–46 to **47–54**. 110/110 tests, `totalLevelCount` = 30, prototypes 31–33 deleted.

**Four mistakes worth recording, because three are the same mistake:**
1. **Tuning carried across configurations.** 4 colours worked at 6×6, so I reused it at 7×7 — where it cost **127 ms/candidate and produced zero unique solutions**, leaving Level 21 grinding past five minutes. Six colours cost **9 ms**. Fewer colours on a bigger board means very long paths, which makes proving uniqueness both rare and slow. Measure per configuration; do not carry numbers forward.
2. **A cheap gate removed without re-checking what followed it.** Turning the coverage rule off deleted the ~0.5 ms filter that had been rejecting ~99% of candidates ahead of the necessity checks (two extra solves *per mechanic instance*). This is the second instance of the same class of bug as §6.17's gate ordering. **Standing rule: when a gate is disabled or its cost changes, re-check the ordering of everything after it.**
3. **Necessity checks were ordered worst-first** — Blocked (3–5 instances, nearly always passes) before the headline mechanic (one instance, the one at risk). Reordered.
4. **Scripted bulk edits silently dropped a line.** `RequireMechanicsNecessary` vanished from the Arrow spec during a pattern substitution, so levels 21–25 shipped with decorative Arrows — the gate was never asked to run. An earlier bulk edit had also stripped the strict rule from levels 1–10 (caught and reverted). For a handful of lines, use targeted edits that can be read back, not file-wide substitutions.

**Open, and it gates the remaining four mechanics:** `EditorUtility.DisplayCancelableProgressBar`'s cancel flag is sticky — a cancelled run leaves the next run aborting on its first poll, reported as "CANCELLED by user" when the user did nothing. All six generate methods now call `ClearProgressBar()` on entry as well as exit. Self-touching (§6.16) remains a deliberate non-issue.

### 6.19 Levels 31–35 built (Permitted)

Fifth mechanic, and the first one built entirely from the §0 runbook — which is the point of having written it. No new machinery was needed: `PlacePermittedCells` reuses `InteriorPathCells`, the necessity gate already handled any `BlockType`, and the menu item is a clone of the Forbidden one.

**Permitted is Forbidden's exact inverse, and that is the whole risk.** They read the same two id columns, so the two are trivially easy to confuse:

| | Names | Effect |
|---|---|---|
| `ForbiddenForPairs` | a colour that must stay away | refuses the colour it names |
| `AllowedForPairs` | the colour whose path runs through it | refuses every colour it does **not** name |

So `PlacePermittedCells` names the cell's **owner** path, where `PlaceForbiddenCells` names any **other** path. Get it backwards and the intended solution is barred from a cell it needs: every candidate becomes unsolvable and the generator silently produces nothing, with no clue why. `LevelGeneratorPermittedTests.AlwaysPermitsTheColourThatOwnsTheCell` pins that invariant, deliberately mirroring the first Forbidden test. A second test pins that a permit cell must name *someone* — one naming nobody is `Blocked` under another name.

**Spec:** 7×7, 6 colours, 5 interior blocked, 1 permitted, walls on 34–35 only, `MinPathCells = 5`, `Uniqueness = Require`, `RequireEveryPairingCoversBoard = false`, `RequireMechanicsNecessary = true`.

**Measured before running** (600 attempts, seed 4321), per the runbook's "do not reuse another range's numbers": 8.4 ms/attempt, 1 pass in 600 (~0.17%) — so 40 000 attempts had ample headroom. Rejections were dominated by `minPath` (346) and non-uniqueness (96); necessity rejected only 17, meaning a permit cell that lands on a contested route is usually load-bearing already.

**Verified from the saved assets, not the generator's log:** permits 5/5 load-bearing, every level exactly one winning solution (search exhausted, not capped), 5/5 blocked cells interior on all five, shortest path 5 cells, wrong routes 7–399, scores 47.5–53.0. Walls on 34–35 form connected barriers (L34's two edges meet at lattice corner (6,1); L35's at (3,2)). 115/115 tests, `totalLevelCount` = 35.

**Two things worth recording:**
1. **A round-trip check caught nothing this time, but was still right to run.** Forbidden had a real bug where the pair id never survived into the built grid. So before generating, I confirmed on three candidates that each permit cell admits its own colour and refuses others through `Block.CanEnter` — the method gameplay actually calls. Cheap, and it is the failure this mechanic is most prone to.
2. **`totalLevelCount` was 30, not the 33 I set during the prototype.** The prototype cleanup reverted it. It is a serialized field in `MainScene`, so it does not travel with the generator and is easy to leave stale — check it when a range ships.

### 6.20 Levels 36–40 built (Bridge) — the mechanic that could not be a placement pass

Every mechanic from Wall through Permitted is a decoration applied to a finished partition: build the solution, then read a rule off it. Bridge is not a restriction on a cell, it is **extra capacity** — two paths crossing at right angles. A partition gives every cell to exactly one path, so there is no finished solution to read a crossing off. The second path has to exist from the start, which meant changing the constructor rather than adding to it.

**Node splitting.** A bridge cell enters the search as **two independent nodes**: a horizontal lane adjacent only to the cells left and right of it, and a vertical lane adjacent only to those above and below. Everything else is unchanged — the same Warnsdorff growth covers every node exactly once, and because the lanes are separate nodes, both get covered. Two properties fall out for free rather than needing enforcement: a lane has exactly two neighbours, so a path through it is **straight** (which `Block.CanExitFrom` demands), and it can never switch axes mid-cell.

Two things the graph cannot express are checked after growth instead: a lane must be **interior** to its path (a path ending on a bridge would make it a pair dot, which `LevelValidator` rejects), and the two lanes must belong to **different paths**.

**The refactor was proved non-destructive rather than assumed to be.** Five shipped ranges run through this constructor, so a behaviour change would have been invisible until levels stopped reproducing. The original grid-based algorithm was transcribed as a reference implementation and diffed against the new node-based one across **1000 boards** (five configurations × 200 seeds, including the 85 that legitimately fail): **byte-identical, failure cases included.** A test comparing `null` against an empty bridge set does *not* establish this — both run the new code — which is exactly the gap that made the reference diff worth doing.

**The bug the checklist did not catch.** Verification showed L40 with `coloursCrossing=1`: its winning solution had **one colour running through both lanes**, self-crossing at (4,1). The constructor was not at fault — 78/78 partitions produced genuine two-colour crossings. The gap is subtler: *the constructor's guarantee covers the arrangement it built, not the one the player must find.* The dots it derives admit other solutions, and a board can turn out to be uniquely solvable by a different one. A self-crossing bridge is legal (`Block.CanAcceptEntry` waves through a pair that already owns the other axis) **and passes necessity** — stripping it removes the straight-through rule and opens a second solution, so it is genuinely load-bearing while still being the wrong picture. Necessity and "is this actually a crossing" are two different questions.

Fixed with a new gate, `EveryBridgeCarriesTwoColours`, run against the winning solution. It is free — `solveResult` is already in hand — so it sits ahead of the necessity checks that re-solve the board.

**Generalisable lesson:** when a mechanic's guarantee is established at construction time, verify it against the *solved* board, not the constructed one. Every mechanic so far was safe from this because its rule was read off the solution; Bridge is the first whose property could survive construction and then evaporate.

**Measured before running** (600 attempts, seed 8642): 3.8 ms/attempt, 4 passes in 600 (~0.67%). Construction failure dominates rejections (486/600, 81%) — a board that cannot seat a crossing fails during construction instead of being filtered afterwards — but it fails fast, which is why the per-attempt cost is *lower* than Permitted's 8.4 ms. `bridgeNotNecessary` was 0: a capacity mechanic is essentially always load-bearing, unlike the restriction mechanics.

Blocked drops to 4 (from 5) because a bridge needs all four neighbours usable, and five holes on a 7×7 leave few qualifying interior cells.

**A second gate, for a problem that was never measured before.** Verification also showed L36 and L39 with **1 wrong route** each — the player draws the only line that exists, with nothing to search. Levels 11–35 had all landed at 3 or more without being asked to, so nothing had ever checked. `MinWrongRoutes` now states it: one partial-coverage solve capped at floor+1, rejecting ~7.5% of candidates at 4.43 ms, and only on candidates that already passed uniqueness (~1 attempt in 150), so roughly 0.03 ms/attempt amortised. Set to 3 for this range only — the earlier ranges already satisfy it, and regenerating verified levels to enforce a rule they already meet would be churn.

**Result across levels 36–40:** bridges 5/5 load-bearing and 5/5 carrying two colours, every level uniquely solvable with search exhausted, blocked cells all interior, shortest path 5–6, wrong routes **7–127** (was 1 on two of them), scores 47.9–49.4. 123/123 tests, `totalLevelCount` = 40.

### 6.21 Levels 41–45 built (Checkpoint)

Back to the ordinary recipe after Bridge broke it. Checkpoint is a per-cell rule again, so no constructor work: `PlaceCheckpointCells`, a spec count, a necessity check, tests, measure, generate.

**Checkpoint is the first rule the player can violate without ever making an illegal move.** Every other mechanic polices entry — `Block.CanEnter` refuses the move. Checkpoint does not appear in `CanEnter` at all; `PuzzleSolver.CheckpointsSatisfied` tests it only when the pair is complete. The board simply refuses to finish, which is why it deserves its own introduction range.

**Shared body with Permitted, deliberately.** Both answer the same placement question — "which colour owns this cell" — so `PlaceOwnerNamedCells` holds the body and both call it, rather than a same-shaped copy per mechanic (the pattern `AllCellsOfTypeAreNecessary` already established). The rules do opposite things with the answer, and even the *reason* the owner is correct differs in kind:

| | Names | Why the owner |
|---|---|---|
| Permitted | the colour allowed through | naming anyone else **bars** the intended solution |
| Checkpoint | the colour required through | the cell belongs to one path, so requiring another colour demands a second visit full coverage forbids — **unsolvable**, not merely worse |

Because one body now serves two mechanics, a bug there would break both at once silently, so Checkpoint got its own test suite rather than leaning on Permitted's.

**Measured before running** (600 attempts, seed 5150): 7.5 ms/attempt, 1 pass in 600 (~0.17%) — same profile as Permitted, ample headroom at 40 000.

The notable number is **necessity rejecting 16 of the 17 candidates that reached it (94%)**. Checkpoints are usually decorative: the named colour would have crossed that cell anyway, so the rule constrains nothing. This is the mechanic where the hard-reject gate earns its cost most clearly — as a penalty rather than a reject, essentially every shipped checkpoint would have been ornamental. `MinWrongRoutes` carried over from §6.20 and rejected 0 here, but it is cheap and there is no reason a later range should be allowed to ship a board with nothing to search.

**Round-trip checked before generating,** since Checkpoint reads the `pairId` column that Forbidden once failed to round-trip: on 50 built candidates, every checkpoint named a real pair, none was a dot, and every one lay on its named pair's path **in the solved board** — the §6.20 lesson applied by default now, not after the fact.

**Result across levels 41–45:** checkpoints 5/5 load-bearing, 5/5 on their named pair's path in the winning solution, every level uniquely solvable with search exhausted, blocked cells all interior, shortest path 5, wrong routes 15–87, scores 49.7–52.1. 129/129 tests, `totalLevelCount` = 45. Nothing needed a second run — the first Checkpoint generation shipped as-is, which is what the accumulated gates are for.

### 6.22 Levels 46–50 built (Shared Destination) — the ninth and last mechanic

Two colours whose paths both end on the same cell. Like Bridge, this had to be built into the partition rather than laid over one — and for a sharper reason. Bridge needed a second path *through* a cell; here it is the **dots themselves** that change. A path's two ends become its colour's dots, so sharing a destination means two paths ending on one cell, and no rule applied to a finished partition can produce that.

**Same node splitting as Bridge, aimed the opposite way.** A bridge's two nodes must be *interior* to their paths; a shared goal's must be *endpoints*. Rather than generating partitions and discarding the ones that miss — a bridge lane is naturally interior, but nothing makes a four-neighbour node naturally terminal — the two nodes are used as path **seeds**, and those paths are **anchored**: an anchored path may grow only from its tail, so the seed stays at index 0 and becomes a dot by construction. One extra line in the growth loop (`side = anchored[p] ? 1 : 0`) rather than a rejection loop.

Two properties then come free. The two nodes are in different paths because each seeds its own. And the two colours arrive on **different edges**, because the cell each steps back through belongs to exactly one path — which matters because `LevelData` caps sharing at four colours for exactly that reason: a path ending in a cell claims the edge it arrived through, and a cell has four.

**A real bug, found by the checklist before a line of generator code was written.** Runbook step 4 says to confirm any new `LevelData` column is read by `BoardGenerator` *and* by the generator's own `BuildBlockGrid`. `BoardGenerator` read `secondPairId`/`thirdPairId`/`fourthPairId`; **`BuildBlockGrid` did not.** A shared dot would have looked like an ordinary one-colour dot during offline validation, so the second colour would have appeared to have a single dot, and every candidate would have been validated against a board the game would never build. This is the third instance of that exact class (Forbidden's `pairId`, Bridge's construction-vs-solution gap, now this) — the checklist step exists because of the first, and it earned its keep here.

**The one mechanic with no necessity check, correctly.** `RequiredMechanicValidator` asks "is the board different without this rule", but a shared destination is not a rule on a cell — it is the identity of a dot, and there is no board without it to compare against. Nor can it be decorative: both colours must reach that cell or the level is simply unfinished. The gate still runs for Blocked and Wall on this range.

**Measured before running** (600 attempts, seed 7311): 10.2 ms/attempt, 5 passes in 600 (~0.83%). Round-trip checked on 50 built candidates first: every shared cell carried the second colour's id, and in the **solved** board both named colours ended there.

**Result across levels 46–50:** shared goals 5/5 with exactly two paths ending on them and distinct arrival edges, every level uniquely solvable with search exhausted, blocked cells all interior, shortest path 5–6, wrong routes 47–399, scores 47.7–55.5. 137/137 tests, `totalLevelCount` = 50. No regeneration needed.

### 6.23 Levels 51–55 built (Mastery begins, 8×8) — and why stacking mechanics was the wrong lever

First range past 7x7, and the first where the plan's own assumption turned out to be wrong.

**K-of-M was built, then deliberately not used here.** `MinNecessaryMechanics` implements "at least K of the M mechanics must be individually load-bearing", with a collective guard so the failures still have to matter as a group. It is the right tool for dense boards — but measurement said 8×8 is not one.

**The tension that decides it:** *uniqueness needs short paths; mechanic necessity needs long ones.* A mechanic is only load-bearing if there is an alternative route for it to rule out. Twelve colours buys uniqueness at 8×8 by driving the average path down to **4.8 cells**, and a mechanic on a 4-cell path has almost nothing to forbid. On this exact board:

| Mechanics on the board | Unique boards sampled | All load-bearing |
|---|---|---|
| 3 | 136 | **0** (5 had two) |
| 2 | 144 | **0** |
| 1 | — | **~18%** |

So the range carries **one mechanic under the strict rule**, cycling across the five levels, and takes its difficulty from the board: twelve simultaneous pairs on 58 usable cells is a different problem from six on 44, not a harder version of the same one. K-of-M stays for dense 7×7 ranges, where paths run to 7.3 cells.

**A bug in the new gate, worth recording because it disguised itself as an impossible spec.** With three mechanics and K=2 exactly one lands in the "unnecessary" set, and stripping a *single* type is bit-for-bit the individual test that just failed — so the collective guard could never pass and rejected every board. It read as "K=2 is unsatisfiable" rather than "the gate contradicts itself". It now runs only when two or more fail, the only case where mechanics can mask each other.

**Tuning that did not transfer, again.** `MinPathCells = 5` (the 7×7 value) rejected 61% of candidates before any other gate and produced nothing. At 3 the range generates at 0.48% and 7.3 ms/attempt. The floor exists to kill trivial 2-cell pairs and still does.

**Result:** mechanics 5/5 load-bearing, walls necessary where present, every level uniquely solvable with search exhausted, 6/6 blocked cells interior, wrong routes 7–383, scores **52.8–55.8** against 47–54 for levels 11–50. `totalLevelCount` = 55.

**Playtested, and the first build was wrong.** Reported as *"too many colours, path is shortest, too easy to solve"* — the 4.8-cell average had predicted it and the level shipped anyway. More pairs on a bigger board is not a harder level; it is a **flatter** one. Rebuilt at **eight colours with three mechanics**, average path back to **7.3 cells**.

**What the rebuild taught, and it generalises past this range.** Eight colours had been ruled out earlier because it produced 0 uniquely solvable boards in 60 — long paths mean many solutions. That measurement had no mechanics on the board. With three, uniqueness returns (6 of 149), because ruling out alternatives is exactly what a mechanic does:

| 8×8 configuration | Avg path | Unique | Mechanics load-bearing |
|---|---|---|---|
| 12 colours, 3 mechanics | 4.8 | 136 sampled | **0 of 3, ever** |
| 12 colours, 2 mechanics | 4.8 | 144 sampled | **0 of 2** |
| 8 colours, no mechanics | — | **0 of 60** | — |
| **8 colours, 3 mechanics** | **7.3** | 6 of 149 | **2 of 3 reached** |

**Constraint has to come from somewhere, and the choice of source is the design decision.** Take it from pairs and paths shorten, difficulty flattens, and mechanics have nothing left to forbid — they cannot be load-bearing on a board that is already over-determined. Take it from mechanics and path length survives, the board stays searchable, and the mechanics matter *because* they are what makes the solution unique. The twelve-colour build failed both tests at once, and the same trade governs every remaining Mastery range.

**Also:** `MinPathCells` is a floor on the SHORTEST pair, not the average, and at 4 it rejected half of all candidates before uniqueness was tested. Lowered to 3; the average is held at 7.3 by the path band, and one short pair among eight does not make a board feel small.

**Redundancy caught walls too.** Level 54 — the only board with three mechanics *and* two walls — exhausted 8000 attempts and produced nothing, while its four neighbours succeeded. The wall gate was rejecting 7 of every 8 candidates that reached it: walls are judged strictly, and on a board already constrained by three mechanics, removing one rarely opens a second solution, so it reads as decorative and the board is thrown out. Walls had been carried over from the 7×7 template without rechecking that they still had work to do at this density. Level 55 proved the combination *is* satisfiable, so the budget was raised to 25000 rather than the design changed — the rng is seeded per range, so re-running at the same budget reproduces the identical miss.

**Result across levels 51–55:** all uniquely solvable with search exhausted, average path **7.3 cells** on every board, 6/6 blocked cells interior, walls necessary where present, wrong routes 13–399, scores **51.6–55.8**. Mechanics load-bearing 2 of 3 on four levels and 3 of 3 on level 55 — which is what K=2 was chosen to permit, with the collective guard refusing any board whose failures matter to nothing.

**Phase 8 — Hint system** *(researched, not started — see §6.24)*
Three-tier hints (§16). The original premise — "built directly on the stored per-level solution, no new solving at gameplay time" — does not survive contact with the code: **nothing stores a solution.** Measurement says it does not need to. See §6.24.

### 6.24 Hint system: researched against the levels we actually generate

Asked before building: will Phase 8 work on levels generated this way? Short answer — yes, and more cheaply than planned, but the plan's design is wrong in two places and there is one genuine problem it never anticipated.

**1. The premise is false: no solution is stored.** Phase 8 says hints replay "the stored per-level solution". `LevelData` carries `gridSize`, `pairCount`, `gridRows` and `difficultyScore` — and nothing else. The generator builds a solution, uses it to place every mechanic, and discards it.

**2. It does not need to be stored.** Deriving a solution on-device is cheap, because a hint needs *one* solution rather than proof that it is the only one — a much shallower search. Measured across all 55 shipped levels:

| | Find one solution | Prove uniqueness |
|---|---|---|
| Average | **2.6 ms** | — |
| Worst | **34 ms** (L54) | 54 ms |
| Levels over one 60 fps frame | **1 of 55** | — |

Solved once on level load and cached, that is free. So Phase 8 needs **no schema change and no regeneration** of levels already playtested — which matters, because regenerating to add a field would throw away boards that have been played.

**3. Uniqueness is what makes hints well-defined, and we already have it.** Because every level ships with `Uniqueness = Require`, "the correct next move" is unambiguous. On a multi-solution board a hint can contradict a different valid line the player is pursuing, and either has to track which one they are on or risk telling them to undo correct work. The generation policy solved the hard half of hint design as a side effect.

**4. The real problem, which the plan does not mention: a hint often has to say "undo", not "draw".** `PuzzleSolver.Solve` builds a fresh `SolverState` and **ignores whatever the player has drawn**, so it cannot continue from a partial board — it can only produce the whole answer for comparison. That is fine arithmetically, but our levels admit **13–399 wrong routes each** by deliberate design (§6.18 — that is what makes them puzzles rather than traces). So a player asking for help is frequently in a state that is legal, looks reasonable, and is inconsistent with the only winning solution. Three tiers that only ever reveal more of the answer would point at cells the player has already filled wrongly, and say nothing about why.

This is the same shape as the checkpoint dead end (§6.23's sibling finding): the game knowing something is wrong and not saying so. **A usable hint system needs a "this is where you went wrong" tier**, and it is cheap to build — compare drawn cells against the cached solution and name the first disagreement.

**5. Hint payload must carry direction, not just colour.** Three mechanics need it: One-Way and Arrow are directional rules, and a Bridge appears in **two** paths at once, so "put colour X here" is ambiguous on a crossing. `PairSolution.Cells` is an ordered per-pair list, which supplies direction for free — but the hint UI has to use it. Shared destinations (two paths ending on one cell) are handled by the same per-pair structure.

**Verdict:** Phase 8 works on the current levels, needs no data migration, and gets its hardest guarantee free from uniqueness. Budget the effort for the wrong-state tier rather than for solver work.

**Phase 9 — Player skill system + save-data expansion**
Extend `SavingSystem`'s single flat struct into a versioned save format carrying skill (overall + per-mechanic), stats, hint usage, and — new — a schema version with migration, since the current "overwrite the whole JSON" approach has no versioning at all today.

**Phase 10 — Daily challenge**
Deterministic date+version seeding (§20), skill-based pool selection (§21–22), on-device bounded generation-or-cache (§39).

**Phase 11 — Campaign/world UI + stars/rewards + statistics screen**
Replace the flat paginated level list with real world structure; add star rating, timer, completion animations, statistics screen, daily-streak UI.

**Phase 12 — Mobile UX polish + performance pass**
Responsive layout across aspect ratios (grid/board scaling already exists and should mostly carry over), touch-tolerance tuning, pooling/allocation audit for path drawing at 12×12.

**Phase 13 — Test suite hardening + final QA gate**
Bulk-generate-and-validate regression tests (spec §42), no-level-ships-without-passing-pipeline gate (spec §37/45).

## 7. Where the project actually stands

**Levels 1–50 are built and verified; all nine mechanics are done.** `totalLevelCount` = 50. Every level from 11 up has exactly one winning solution with wrong routes to search; levels 1–10 keep the strict coverage rule as the tutorial tier (§6.18). 137 tests pass.

| Mechanic | Levels | Built in |
|---|---|---|
| Basic Flow + Blocked | 1–10 | §6.6, §6.11–6.13 |
| Wall | 11–15 | §6.7 |
| One-Way | 16–20 | §6.8 |
| Arrow | 21–25 | §6.10 |
| Forbidden | 26–30 | §6.18 |
| Permitted | 31–35 | §6.19 |
| Bridge | 36–40 | §6.20 |
| Checkpoint | 41–45 | §6.21 |
| Shared Destination | 46–50 | §6.22 |

### Known defect: an unmet Checkpoint is a silent dead end

**Found by reasoning, not by play** — "can the level end if the checkpoint is occupied by another colour?"

The answer is no, and the enforcement is correct: `IsPairSatisfied` ends with `PairSatisfiesCheckpoints`, so a pair whose checkpoint is not on its path never counts toward `GetPairCompleteCount`, the count stays under `CurrentLevelGoal`, and the win branch does not fire.

The defect is what the player sees while that is happening: every cell filled, the HUD reading **`Cells : 49/49`**, every pair visibly joining its dots — and nothing happens, with no indication of why. It is §6.18's defect through a different door: the UI announcing the level is complete while the game refuses to end.

**Every level from 41–45 contains at least one such state, by construction.** Necessity requires that removing the checkpoint opens a second solution, and that second solution must route the checkpoint's owner around it (otherwise it would satisfy the rule too, contradicting uniqueness). Measured on Level 41: strip the checkpoint and 3 full-coverage solutions appear, **2 of which are dead ends** — board full, every pair connected, the checkpoint at (3,5) held by pair 9 or pair 5 rather than its owner, pair 3.

**Audited across levels 36–50, and Checkpoint is the only offender.** The test: count arrangements the player can physically reach that fill the board and join every pair — move-time rules bind them, completion-time rules do not. Anything above 1 is a dead end.

| Levels | Mechanic | Reachable complete-looking states | Dead ends |
|---|---|---|---|
| 36–40 | Bridge | 1 each | **0** |
| 41–45 | Checkpoint | 3, 4, 2, 2, 2 | **8 total** |
| 46–50 | Shared Destination | 1 each | **0** |

The split is structural, not luck. Bridge's rules live in `CanExitFrom` (no turning on a crossing) and `CanAcceptEntry` (one lane per axis), both consulted as the player moves, so an illegal bridge route cannot be drawn in the first place. Shared Destination has no completion-time rule at all — it is a dot with two identities. **Checkpoint is the only mechanic whose rule is checked at completion time**, and therefore the only one that can be broken silently.

**Fixed** in `GamePlayController.RefreshUnmetCheckpointFeedback`, on the sharper of two possible triggers.

The first attempt waited until the board was full, reasoning that a checkpoint missing its colour mid-solve is unfinished rather than wrong. That is true of an EMPTY checkpoint but not of one **held by another colour** — the cell takes a single occupant, so the rule is already unsatisfiable the instant a wrong colour lands, and no waiting is justified. The sharper trigger also subsumes the original: a full board means every cell has an occupant, so "wrong occupant" catches everything "board full" caught, only earlier. The empty case stays silent.

It reads **live occupancy** rather than the committed segments the win condition uses, so the warning arrives during the drag that causes it rather than after release. The HUD counter deliberately does the opposite — `SatisfiedCheckpointCount` asks the same question `PairSatisfiesCheckpoints` asks, so it can never read `2/2` while the level refuses to finish, which is precisely the defect the cells counter was introduced to fix.

Hooked into `OnPointerMoved` (live, covers drawing and retreating) and the win check; cleared on `ResetGameplay`.

**HUD:** the cells line gains `Checkpoints : 1/2` on levels that have them, hidden entirely on levels that do not rather than shown as `0/0`, which would read as a goal already failed. Checkpoints need their own count because they are the one rule the board cannot show as satisfied on its own — a filled cell looks identical whether the colour crossing it is the named one or not.

**Multiple checkpoints per level** work throughout — verified end to end, not assumed: `PairSatisfiesCheckpoints`, `CollectCheckpoints`, the feedback pass and the counter all iterate. A spec with `CheckpointCount = 2` produces uniquely-solvable boards with both honoured. Levels 41–45 ship one each; that is a content choice, not a limit.

### 6.25 The second playtest correction: mechanics are seasoning, not difficulty

Levels 51–55 were rebuilt twice, and both rebuilds were the same mistake in different clothes.

| Build | Lever pulled | Playtest verdict |
|---|---|---|
| 12 colours, 1 mechanic | more pairs | *"too many colours, path is shortest, too easy"* |
| 8 colours, 3 mechanics | more rules | *"too much of mechanics, annoying to play"* |
| **8 colours, 1 mechanic, 10 blocked** | **board shape** | — |

The first two both came from reaching for a number to raise instead of asking what makes this genre hard. **Numberlink has no mechanics at all** (§6.15) — its difficulty is routing. Stacking three rule types is not depth, it is bookkeeping, and the player feels the difference immediately.

**Constraint has to come from somewhere, but the sources are not equivalent to the player.** A board needs constraint to have a unique solution. A rule cell is something to remember and check on every move; a **blocked cell is a hole you route around**, costing nothing to hold in mind. Both constrain the search equally as far as the solver is concerned. Measured at 8×8 / 8 colours:

| Configuration | Unique yield | Avg path | Avg wrong routes |
|---|---|---|---|
| 3 mechanics, 6 blocked | ~1 per 1000 | 7.3 | 13–399 |
| **1 mechanic, 10 blocked** | **95 per 908** | **6.8** | **~131** |
| 1 mechanic, 14 blocked | 452 per 3105 | 6.3 | ~40 (over-constrained) |

One mechanic with more holes holds path length, produces **more** search than the three-mechanic build, and generates a hundred times more easily. Fourteen holes over-constrains — wrong routes collapse and paths shorten — so ten is the pick.

**Walls dropped from the range.** With ten holes already shaping the board, removing a wall almost never opens a second solution, so walls failed the necessity gate as decoration: 63 rejections out of the 65 candidates that reached it, which is also what made the wall levels ungeneratable. A wall the player cannot tell is doing anything is precisely the clutter being removed.

**The rule this leaves for the rest of Mastery:** scale difficulty with **board size and board shape**, and keep mechanics to one per level as flavour that rotates. The generator makes it easy to add rules and hard to notice they are not fun — every gate here measures whether a mechanic is *load-bearing*, and none of them measures whether it is *welcome*. Only playtest does.

### Open questions, current

- **Levels 51–200 (Mastery) is blocked on BOTH of its difficulty axes.** Measured, not estimated — and the answer to "can we just generate the next 150?" is no, because with the current pipeline they would come out structurally identical to levels 41–50.
  - **Mechanic density — and the reason is redundancy, not rarity.** 7×7, 6 colours, three stacked mechanics (Checkpoint + Forbidden + Arrow), across 175 uniquely solvable boards:

| Mechanics load-bearing | Boards | Share |
|---|---|---|
| 0 of 3 | 97 | 55.4% |
| 1 of 3 | 65 | 37.1% |
| 2 of 3 | 13 | 7.4% |
| 3 of 3 | 0 | **0.0%** |

    A steep decay, not a cliff — each additional "must be load-bearing" costs roughly 5× in yield. **The cause is that our necessity test asks a MARGINAL question**: "does removing this one mechanic, alone, open a second solution?" As density rises the mechanics start ruling out the *same* alternative routes, so removing either one alone leaves that route still blocked by the other, and both then measure as unnecessary. Redundancy between mechanics reads as uselessness of each. Uniqueness is not the constraint (175 unique boards were easy to find); marginal necessity is.

    **The fix is to stop asking a marginal question.** A "**at least K of M**" rule is immediately viable off these numbers — 2-of-3 accepts 7.4% of unique boards, 1-of-3 accepts 44.6% — ideally paired with a joint check (removing *all* the mechanics together must open alternatives) so a board cannot pass with two decorative mechanics riding along. Choosing K is a level-quality decision, not tuning.
  - **Board size — 8×8 is NOT blocked; the ceiling is the colour palette.** An earlier note here claimed 8×8 needed solver pruning. That was wrong, and wrong in the exact way §0's first gotcha warns about: it tested one colour count and generalised. Re-measured across colour counts:

| Board | Colours | Usable cells | Avg solve | Worst | Over budget | Unique | Avg path |
|---|---|---|---|---|---|---|---|
| 7×7 | 6 | 44 | 9 ms | 98 ms | 0 | 4/50 | 7.3 |
| 8×8 | 8 | 58 | 105 ms | 687 ms | 0 | **0/60** | — |
| 8×8 | **12** | 58 | **6 ms** | 54 ms | 0 | **6/50** | 4.8 |
| 9×9 | 12 | 74 | 1243 ms | 3653 ms | 1 | 0/9 | — |
| 10×10 | 12 | 92 | 2388 ms | 3397 ms | 3 | 0/5 | — |

    **8×8 at 12 colours is cheaper than the 7×7 we already ship.** The causal chain is: proving uniqueness needs short paths → short paths need many pairs per cell → `PairColorType` holds exactly **12** colours → boards above ~60 usable cells cannot be constrained enough. 9×9 needs about 15 pairs for the same density and cannot have them. So the ceiling is a **content** limit (add colours and their art), not an algorithmic one — the solver never even ran out of budget at 8×8.

    **Caveat before treating 8×8 as free difficulty:** its average path is **4.8 cells against 7×7's 7.3**. By §6.14's measure that is *easier*, not harder — more colours to track but shorter routes each. Bigger board does not automatically mean harder level, and this needs playtesting rather than assuming.
- **The four newest mechanics have never been played.** Bridge, Checkpoint, Shared Destination and Permitted (levels 31–50) are verified by the solver but not by a human. Every previous playtest found something the gates did not — Level 1's empty cells, Level 7, Level 11, Level 14's walls — so this is worth doing before generating 150 more.
- **Board size: 8×8 is available now at 12 colours; 9×9+ needs more colours, not a better solver.** See the table above. The open question is whether 8×8 actually plays harder, given its shorter average path, and whether adding colours past 12 is worth the art cost.
- **`DifficultyAnalyzer.Score` still is not a difficulty target** and all 50 levels score in a narrow 43–56 band regardless of how they actually play. Something better is needed before difficulty can be *ordered* rather than merely bounded — the level-select UI and daily-challenge selection both want a number that means something.
- **Phases 8–13 are untouched** (hints, save-data versioning, daily challenge, campaign/world UI, mobile polish, QA gate). Hints are the natural next one: they read the stored per-level solution and need no new solving.
- **Art/UI scope** remains a separate parallel content task; nothing in this plan covers world themes, statistics screens, or daily-challenge screens.
