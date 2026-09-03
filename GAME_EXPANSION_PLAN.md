# FreeFlow → Full Offline Flow Puzzle Game: Feasibility & Plan

Status: **Living document — updated after every phase.** Originally a feasibility audit with no code changed; now also the running record of what's actually been built, what's still pending, and any issues found along the way. See the Progress Log immediately below for the at-a-glance status, and §6 for the detailed per-phase notes.

**Campaign rescoped (superseding §12 of the original spec and Phase 7's "Worlds 1–11, 500+ levels" framing):** the user replaced the original 500+/11-world campaign design with a detailed **200-level** campaign spec — a 50-level Learning Phase teaching all 9 mechanics one at a time (Basic Flow → Blocked → Wall → One-Way → Arrow → Forbidden → Permitted → Bridge → Checkpoint → Shared Destination, levels 47–50 combining them), followed by a 150-level Mastery Phase that only recombines what's already been taught, scaling grid size (7×7→12×12), mechanic count per level (2–3 up to 5–7), and difficulty — never introducing anything new after level 50. See §6.6–6.22 for what's actually built against this new structure (**50/200 — the whole Learning Phase, all nine mechanics**) and §7 for where it stands and what is still open.

> **Coming back to this after a break? Read [§0 — How levels are generated](#0-how-levels-are-generated--start-here-when-picking-this-up-again) first.** It is the runbook: how to run generation, the two design regimes, how to verify a range, and how to add the next mechanic. Everything else is history and reasoning.

> **Work in progress, interrupted mid-task: read [§6.44](#644-advanced-7x7-why-the-wired-call-fails-the-fix-and-where-this-stands--read-this-first-if-picking-up-the-7x7-pack) before touching Advanced 7x7.** The generator had a real bug (now fixed, 227/227 tests pass) and a good `cellsPerColour` has been measured (7, not the originally-wired 10) — but the actual 100-level pack has **not been generated yet**. §6.44 has the exact next command to run and why.

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
| 8 — Hint system | ✅ Done | One hint, one pair: `HintPath` turns the level's stored answer back into an ordered route and `GamePlayController.TryApplyHint` draws it. Verified across all 600 shipped levels (§6.41) |
| 9 — Player skill system + save-data expansion | 🟡 Partial | `SaveData` gained a schema version + migration seam, per-mechanic attempt/completion tracking (`RecordMechanicAttempt`/`RecordMechanicCompletion`, pooled into `OverallSkillRating`/`MechanicSkillRating`), and per-level hint-usage counts. Classic's mechanic-free levels key skill by board size, not one shared bucket, since size is Classic's real difficulty lever (§6.25/§6.31). Wired into real play, not just the schema — see §6.42. No consumer reads the ratings yet; that is level-select/daily-challenge work (Phases 10–11) |
| 10 — Daily challenge | 🟡 Partial | One level a day, deterministic per calendar day, drawn from an existing Classic pack (not generated) and cached so it can't change under the player mid-session. Skill (Phase 9's `OverallSkillRating`) picks which third of the pack; the day's hash picks the level inside that third. Streak tracked and shown on completion. Reachable from a new main-menu button. See §6.43 |
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

**Classic ships as five packs of 100, one per board size — 5×5 through 9×9, 500 levels; Advanced has its 6×6 pack of 100 with all nine mechanics.** The player picks a pack; each ramps on its own from the easiest board that size can produce to the hardest. Every level is uniquely solvable, structurally well-formed, has at least one deduction available from the opening position, and carries its own stored solution for the hint system. 160 tests pass.

~~The older linear Classic 1–50 still exists and is still what the game loads, because none of the packs are reachable in play yet.~~ **Retired.** The linear Classic 1–100 and Advanced 1–45 level assets (`Resources/Levels/{Mode}/Level_N.asset`) have been deleted outright — see the "old campaigns retired" note in Open Questions below for what that leaves dangling. Difficulty, the problem that survived six rounds of playtesting, was closed in §6.35; §6.37–6.38 cover the pack structure and the two colour-count corrections that came out of play.

The table below describes the ADVANCED campaign's mechanics, which is where levels 1–50 of that mode live. Classic is the default mode and carries no mechanics at all (§6.27); its 50 levels were rebuilt from scratch in §6.34–6.35 and every one of them is uniquely solvable, structurally well-formed, and has at least one deduction available from the opening position. Difficulty, the problem that survived six rounds of playtesting, was closed in §6.35 — the calibration that did it is recorded there.

Two things this section does not yet cover: Advanced mode is unreachable in play, because `SetMode()` exists but nothing calls it; and the 5×5 and 6×6 Classic blocks have not had the §6.35 treatment, so the campaign dips in the middle.

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

### 6.26 A real Flow Free board, measured on our own solver

A screenshot of **Flow Free "8x8 Mania, level 1"** was encoded by hand (9 pairs, full 64-cell grid, no holes) and run through `PuzzleSolver`. Four findings, one of which contradicts an earlier decision.

**1. It has exactly one full-coverage solution, search exhausted.** Our `Uniqueness = Require` policy matches what the market leader actually ships. That was an assumption until now.

**2. It has ZERO wrong routes.** There is only one way to connect the nine pairs *even ignoring the coverage rule*. That is precisely the property §6.18 removed from levels 11+ on the grounds that a board with no wrong routes is "a trace, not a puzzle" — and here it is in a shipped commercial level.

  Two caveats before over-correcting. This is **level 1 of the pack**, the easiest board in it, so few alternatives is expected. And the metric may simply be a poor proxy: it counts *complete alternative pairings*, but a human never sees pairings — they explore partial paths and make local mistakes the metric never counts. A board can have one valid pairing and still be hard to find. **"Wrong routes" measures search space for a solver, not for a person**, and it should not be treated as a difficulty guarantee on its own.

**3. Their board shape differs from ours.** Flow Free uses a **full grid with no blocked cells** — 9 colours over 64 cells, average path 7.1. Levels 51–55 use 10 blocked cells and 8 colours over 54 cells, average path 6.8. Comparable density, different silhouette: they get constraint from pair count, we get it from holes.

**4. The reason we get it from holes is cost, and it is severe.** On that full-grid board our solver takes:

| | Time | Steps |
|---|---|---|
| Find one solution | 155 ms | 308,539 |
| **Prove uniqueness** | **2951 ms** | **4,973,792** |

Against ~7 ms per candidate on our 8×8-with-10-holes spec — roughly **400× more expensive**. Generation needs ~1000 candidates per level, so Flow-Free-shaped boards would take hours each with this solver. **Blocked cells are not only a design choice for us; they are what makes generation tractable.** Big Duck Games either has a far better solver, authors by hand, or accepts very long offline runs.

**Consequence for the two-mode plan:** Classic mode is cheap at 8×8 *with holes* (8.1% unique, ~100 wrong routes, 6.8 paths) and impractical at full-grid without solver work. If matching Flow Free's clean full-board look matters, that is a solver-pruning project, not a spec change.

### 6.27 Two modes, and the Classic progression

**Decided:** two campaigns of 100 levels each, expanding later. **Classic is the default and the front door.**

The existing levels split along the line already, so this was a re-label rather than a rebuild: levels 1–10 carry no rule cells and no walls (Classic 1–10); levels 11–55 each carry a mechanic (Advanced 1–45). Assets moved to `Resources/Levels/<Mode>/`, and the generator writes there with mode-local numbering via an `outputOffset` so the specs and this document keep the campaign numbering they were written against.

**Save data is per mode, and Classic keeps the ORIGINAL field names deliberately.** `JsonUtility` fills any field missing from an existing save with its default, so renaming would silently reset whichever campaign lost its name. Classic is the default and the likelier campaign to be mid-way through, so it inherits the old progress; Advanced starts empty, which is correct because it did not exist before.

#### The Classic progression

Removing mechanics removes every difficulty dial except **board size, colour count and hole count** — which is the point of the mode. Two findings shaped the curve:

**Difficulty rises by REMOVING holes.** Counter-intuitive, but fewer holes means longer paths, and path length is the metric that tracks how a board actually feels (§6.14). Each block therefore starts hole-rich and thins out; board size steps up between blocks, which resets path length but raises the number of simultaneous pairs. That is the shape Flow Free's own packs use.

| Block | Board | Colours | Holes | Path | Unique yield |
|---|---|---|---|---|---|
| 1–10 | 4×4→6×6 | 3–6 | — | — | *existing, strict rule* |
| 11–35 | 6×6 | 5 | 6 → 3 | 6.0 → 6.6 | 36% → 14% |
| 36–65 | 7×7 | 6 | 9 → 4 | 6.7 → 7.5 | 20% → 4.3% |
| 66–100 | 8×8 | 8 | 12 → 8 | ~6.5 → 7.0 | ~13% → 3.7% |

**The strict coverage rule stops at 6×6, and that is measured rather than chosen.** Levels 1–10 keep `RequireEveryPairingCoversBoard` so a beginner can never connect every pair and be left staring at empty cells. Above 6×6 it cannot be satisfied: at 6×6 it produced 10 clean boards out of 477 unique ones, and at 7×7 and 8×8 it produced **zero**. This is the same ceiling §6.18 hit from the other direction.

**Blocked cells still face the necessity gate.** A hole that changes nothing is as much noise as a decorative rule, and in Classic it is the only mechanic there is.

**Built: Classic 100/100.** Verified across the whole campaign, not level by level: **0 missing, 0 non-unique, 0 rule cells, 0 wall edges** — genuinely mechanic-free. Average path by block: 6.47 (1–10, mixed sizes) → 6.30 (6×6) → **7.08** (7×7) → 6.75 (8×8), with no pair shorter than 3 cells anywhere. `classicLevelCount` = 100.

The path dip at the 8×8 block is the board-size step doing its work: paths shorten when the board grows because the colour count rises with it (6 → 8 pairs), so the level gets harder along a different axis than the one path length measures. That is the same trade Flow Free's packs make, and it is the one thing in this progression that measurement cannot settle — whether 8 pairs on 8×8 at path 6.75 actually plays harder than 6 pairs on 7×7 at 7.08 is a playtest question.

### 6.28 Making Classic work on full grids: solver work, and where the ceiling actually is

Classic must carry no blocked cells (a hole is a mechanic, §6.27's correction), and hints need a unique solution. Both together mean generating **full-grid, mechanic-free, uniquely-solvable boards** — which the generator could not do above 6×6. Four changes fixed that, and one measurement says where it stops.

**1. Filtering does not work on a full grid.** Generating random boards and keeping the unique ones found **0 in ~230 attempts** at 8×8. Holes had been doing the constraining; without them a board has too many ways to re-route.

**2. Refinement replaced it.** When a solve returns two solutions they must disagree about some cell; split the intended path there and one colour becomes two, pinning the routing. Ambiguity falls monotonically. Each split also makes the next proof cheaper, so the expensive exhaustive check only runs on an already-constrained board.

**3. Merge-down is what makes the puzzles good.** Splitting only ADDS colours and stops at the first unique board it stumbles into — on a 9×9 that was 18+ colours and sub-4.5-cell paths, which is the "too many colours, too easy" failure §6.25 already recorded. Merging rejoins paths whose ends touch and keeps any join that preserves uniqueness, walking to a locally minimal colour count — the same thing as locally maximal path length.

**4. Two solver prunes and per-step MRV.** A stranded-cell check (an empty cell needs two ways out, or one if it is a dot), a per-move connectivity flood fill (every unrouted pair's dots must stay in one free component), and choosing the next pair by fewest remaining options rather than a fixed order. The connectivity prune is the decisive one: a full 9×9 went from *Inconclusive after 8M steps and 5.2 s* to *Solved in 2.4 s*. All three verified against the 145 shipped levels — 0 became unsolvable, 0 lost uniqueness — and they made those levels **1.9× faster** as a side effect.

**Measured result, full grid, no mechanics, guaranteed unique:**

| Board | Colours | Avg path | Time/level | 100 levels |
|---|---|---|---|---|
| 5×5 | 3.7 | 6.8 | 0.1 s | seconds |
| 6×6 | 4.0 | **9.0** | 0.3 s | seconds |
| 7×7 | 6.7 | 7.4 | 0.7 s | ~1 min |
| 8×8 | 8.7 | 7.4 | 7.9 s | ~13 min |
| 9×9 | 11.3 | 7.1 | 204 s | ~5.7 h |
| 10×10 | — | — | **0 of 105 attempts** | — |

8×8 at 8.7 colours matches Flow Free's own 8×8 (9 colours, path 7.1); 6×6 reaches **one pair per nine cells**, better than the commercial benchmark.

**Cost grows 10–25× per board size.** That, not any single wall, is the real constraint.

**Why 10×10 produced nothing, and it is probably not a wall.** A 10×10 at 14 colours was measured proving out in **1,974,169 steps against a 2,000,000 budget** — clearing by 1.3%. At that size most proofs land just past the cap, come back `Inconclusive`, and are rejected as "not proven unique". The generator is discarding boards that are likely fine. Raising the budget for 10×10 is the obvious next experiment; the cost is roughly linear in the budget, so ~4× the already-slow per-attempt time.

**A claim to retract.** Earlier notes in this document said full-grid uniqueness needs roughly one pair per 4–5 cells. That was a property of greedy splitting, not of the problem — with merge-down, 6×6 reaches one pair per nine. Do not treat the old figure as a constraint.

**Two process lessons that cost real time:**
- `Get-Process Unity | Select-Object -First 1` picks an arbitrary one of several Unity processes. Sampling an idle leftover made healthy runs look dead and prompted an unnecessary rewrite of the job architecture. Sort by CPU.
- A long `[MenuItem]` run dies to any domain reload — editing a script or the editor regaining focus is enough. `EditorApplication.update` is not the fix: Unity barely ticks it while unfocused. Blocking plus a frequently-updated `DisplayCancelableProgressBar` is what actually works, and is what the shipping generators already did.

### 6.29 The difficulty metric was wrong, and it took three playtests to see it

Three separate level sets came back as *"too easy"* — the 12-colour 8×8 build, the three-mechanic rebuild, and finally a Classic campaign generated specifically to be hard. The cause was not any tuning value. **It was the acceptance test: the generator kept the FIRST uniquely solvable board it produced.**

Uniqueness guarantees a puzzle has one answer. It says nothing about whether finding that answer takes any thought. Most uniquely-solvable boards fall out to pure local deduction with almost no branching — and those are exactly what "first valid board" selects for.

**Measured against a real Flow Free board** (8×8 Mania level 1, hand-encoded from a screenshot):

| | steps | decisions | dead ends | wrong routes |
|---|---|---|---|---|
| Our L100, the "hardest" | 754 | **188** | 6 | 199 |
| Flow Free's FIRST level | 20004 | **4600** | 340 | **0** |

Twenty-four times less thinking than the level they ship first.

**Both metrics this document had been ramping are wrong:**

- **Path length.** Our L100 had LONGER paths than L88 (8.2 vs 7.0) and demanded seven times FEWER decisions. §6.14 adopted path length as the honest proxy; it is not one. In places the "harder" ramp was making levels easier.
- **Wrong routes.** §6.18 introduced these as the sign of a real puzzle, and §6.20 added a floor for them. The Flow Free board has **zero** alternative pairings and is hard anyway. Difficulty is not how many wrong answers exist; it is how much search finding the right one costs.

**The right measure was already in `SolveResult`** — `DecisionPointCount`, `DeadEndCount`, `StepsTaken` — and had been since the solver was built. It was never selected on. The campaign now generates 12 valid boards per level, solves each, and keeps the one with the most decision points.

**Result across the Classic 100:**

| Block | Avg decisions |
|---|---|
| 5×5 (1–25) | 134 |
| 6×6 (26–60) | 668 |
| 7×7 (61–100) | **3722** |

Range 24 to 20,447, average 1,756. Level 100 reaches **4,806 decisions — past Flow Free's 4,600, on a smaller board.** Generation cost rose from 57 s to 500 s for the campaign, which is the price of scoring twelve candidates instead of taking the first.

**The lesson worth keeping:** every gate in this generator verifies that a level is *valid* — solvable, unique, fully covered, mechanics load-bearing. None of them measured whether it was *interesting*, and validity turns out to be nearly uncorrelated with difficulty. A generator will happily produce a thousand correct, trivial puzzles unless something explicitly selects for effort.

### 6.30 Tangle: the metric that finally matched how the game plays

Five difficulty metrics were tried across four rebuilds. Four measured how hard the SOLVER works, and every one of them rated boards as hard that played easy. The fifth measures the SHAPE of the solution, and it matched play on the first try.

**The four that failed**, and why each looked convincing at the time:

| Metric | Why it was adopted | Why it was wrong |
|---|---|---|
| Path length | longer routes feel harder to draw | our longest-path level needed 7× FEWER decisions than a shorter one |
| Wrong routes | a board with one pairing is a trace | Flow Free's board has **zero** alternatives and is hard anyway |
| Solver decision points | branching is where a player must think | a 6×6 reached 7192, above Flow Free's 4600, still played easy |
| Forced-move collapse | boards that deduce themselves are trivial | ours were LESS deducible than Flow Free's, 25% against 33% |

**What separated them** was measuring the solution's geometry against a real Flow Free board:

| | turns/cell | bounding-box fill | cross-colour adjacency |
|---|---|---|---|
| Ours | 0.44–0.61 | 0.73–0.87 | 22–41% |
| Flow Free 8×8 | **0.17** | **0.63** | **51%** |

Their paths **turn less, sprawl more, and run alongside other colours far more**. Ours wiggled inside a small area, each colour keeping to a compact blob — *scribbly*, not *tangled*. A player never had to route around anyone else, so nothing felt constrained however much the solver thrashed.

That is a direct consequence of the Warnsdorff partition builder: most-constrained-first fills corners and edges, growing compact regions. Excellent at not stranding cells, which is why it was chosen (§6.15), and the wrong geometry entirely.

`TangleScore` = cross-colour adjacency ÷ bounding-box fill. Flow Free scores **81**.

**The two criteria actively oppose each other**, which is why four rounds of "make it harder" made it worse. On the same 6×6 board size: the most decision-heavy board scored **16598 decisions / 25 tangle**; the most tangled scored **86 tangle / 728 decisions**. Optimising solver effort was selecting *against* the property that was wanted.

**Levels 1–50 rebuilt on tangle** — pools of 260–400 per block, keep the most tangled, order ascending so each block ramps:

| Levels | Board | Tangle kept | Pool spanned |
|---|---|---|---|
| 1–15 | 5×5 | 76–88 | 27–88 |
| 16–32 | 6×6 | 78–99 | 30–99 |
| 33–50 | 7×7 | 78–90 | 43–90 |

Verified: 0 missing, 0 non-unique, 0 duplicates, 0 mechanic cells or walls, 0 ramp breaks within blocks. Average tangle **82** against roughly 40 for the previous build. Confirmed as feeling tangled in play.

**The lesson**, and it is the largest one in this document: every gate here verifies a level is *valid* — solvable, unique, covered, deduplicated. Validity is nearly uncorrelated with fun, and four plausible difficulty proxies were not merely weak but **anti-correlated** with the real thing. The only reliable instrument was a person playing it and saying "tangled".

### 6.31 What other puzzle games actually do about difficulty

Five metrics failed against play. Rather than invent a sixth, here is how the wider field solves this.

**Sudoku is the closest solved problem, and its answer is the opposite of ours.** Pelánek's computational model reaches **r = 0.95 against human solving times** across 2000+ puzzles and thousands of hours of play. The metric is *"the number of high-level strategies required to solve the problem **without brute force**"*. Generators rate a puzzle by running a **human-technique solver** in order of sophistication -- singles, pairs, locked candidates, fish, wings, chains -- and score it by the hardest technique it is forced to use.

**Crucially, puzzles that require backtracking are reported as "brute-force needed" and treated as BADLY FORMED, not hard.** Search effort is the field's signal for a *defect*. Every metric we tried — decision points, dead ends, nodes explored — measures precisely that.

Pelánek identifies two sources of difficulty, neither of which is search volume:
1. **complexity of the individual deduction steps**, and
2. **the dependency structure between them** — how far one deduction must be carried before it unlocks the next.

**Sokoban research agrees.** Comparing computational scores against user-study ratings: human-solver metrics correlate 0.66, box changes 0.74, and **problem decomposition 0.82**. The structural measure beats the search-effort measure.

**Numberlink's own well-formedness rule** (from thomasahle/numberlink, a generator for this exact puzzle): *"the solution uses 100% of the paper and no link touches itself."* Checked against our boards — we already satisfy it: 0, 1 and 0 self-touches on levels 20, 40 and 50, against Flow Free's 0. Not the gap.

**The one caveat the literature is explicit about:** constraint-propagation models are easy to formalise for Sudoku because its rules are simple, and *"for similar problems like Nurikabe it can be difficult to formulate suitable constraint propagation rules."* Flow is closer to Nurikabe than to Sudoku, so the technique hierarchy has to be built rather than borrowed.

**What this means for us.** The measurable target is not "how much does a DFS thrash" but **"how far can a human-style deduction engine get before it must guess, and how many guesses does it need."** That requires a second solver — propagation-only, with named techniques:

- a path head with exactly one legal continuation
- an empty cell only one colour can still reach
- a free region reachable by only one colour
- endpoint parity and corner forcing

Difficulty then rates as: **hardest technique required**, plus **number of times propagation stalls and an assumption is needed**. Easy = solvable by rule 1 alone. Hard = repeated stalls requiring deep look-ahead.

This is a real piece of work, not a scoring tweak, and it is the only approach in the literature with a validated correlation to human experience.

### 6.32 Deeper research: the four things the field does that we do not

§6.31 established that search effort is the wrong signal. This is the follow-up — reading the primary sources properly rather than the summaries — and it turns up four concrete, implementable things, one of which is a Numberlink-specific deduction rule we have simply never used.

**Pelánek's full correlation table, with our own metrics placed in it.** Every metric he evaluated, against human solving time on two independent portals:

| Family | Metric | Fed-Sudoku | Sudoku.org | Ours? |
|---|---|---|---|---|
| Algorithm | Backtracking | 0.16 | 0.25 | **this is what §6.29's metrics were** |
| Algorithm | Harmony search | 0.18 | 0.22 | — |
| Static | Number of givens | 0.25 | 0.27 | ~ colour count |
| Algorithm | Simulated annealing | 0.38 | 0.39 | — |
| Relaxation | Solution count | 0.40 | 0.46 | not measured |
| Relaxation | **Fixedness** | 0.56 | 0.61 | **not measured** |
| Model | **Dependency** | 0.67 | 0.69 | **not measured** |
| Model | Refutation sum | 0.68 | 0.83 | ~ `HumanSolver.Assumptions` |
| Model | Serate LM | 0.78 | 0.86 | — |
| Combined | RD (refutation + dependency) | 0.74 | 0.88 | — |
| Combined | SFRD (4-metric linear) | 0.84 | **0.95** | — |

Two things fall out. First, backtracking metrics score **0.16–0.25** — our five failed metrics were not merely imperfect, they were the worst-performing family in the literature, and play was right to reject them all. Second, **no single metric reaches 0.95; the four-metric linear combination does.** We have been looking for one number. The field's answer is a weighted blend of four, drawn from *different* families — which is also why our tangle score plateaued: it is one static structural measure doing a job that needs several.

**Dependency is the biggest gap, and it is cheap.** Pelánek's definition: at each solving step, count *how many different places the technique could be applied*, then average over the first 20–30 steps. Ten applicable cells means the solver can start anywhere and the puzzle carries itself; two means every step must be found. **On its own it hits r = 0.67–0.69** — better than everything except refutation.

This is about ten lines on top of `HumanSolver`, which already enumerates every firing site each round and currently just takes the first. It also explains the failed metrics directly: a board with many simultaneous forced moves has *high* solver-decision counts (lots of branching later) and *low* dependency (trivial to start), and we were ranking exactly the wrong way round.

**Constraint relaxation is the one that generalises — and it may matter more here than the technique model.** The paper is explicit that its propagation model is easy for Sudoku because Sudoku's rules are simple, and that formulating techniques for other puzzles is the hard part. Their portable alternative: **relax constraints and watch what happens.**

- **Solution count under relaxation** — drop a constraint, count how many solutions appear.
- **Fixedness** — across the relaxed variants, how many cells keep the same value. Cells that stay fixed were over-determined; a board of mostly-fixed cells is easy.

The headline result: **on Nurikabe, relaxation reaches r = 0.9 and beats their own constraint-propagation model at r = 0.8.** Nurikabe is a region/connectivity puzzle, structurally far closer to Flow than Sudoku is. That is a direct argument that relaxation may outperform `HumanSolver` on our puzzle, and it costs no new solver: **remove one dot pair, re-solve, count solutions and count cells whose colour did not change.** Our uniqueness solver already does all of that.

**The corner dual heuristic — a real Numberlink deduction we have never implemented.** From thomasahle/numberlink, the reference generator/solver for this exact puzzle:

> if a square is filled out with a corner, the square inside the turn will either have to be a source or be a corner of the same orientation as well.

Anything else forces the link to touch itself. Take the inductive closure and **every corner must lie on a "spike" rooted at a dot** — the solver uses this to represent any solution as one signed integer pair per dot, and it is why it solves 40×40 boards casually.

The human form of the same rule, from the Numberlink primer: you may keep drawing 90° turns from a corner until you hit a number, and a corner chain that runs into nothing is a proven contradiction — the primer says a path that causes it is 100% certainly wrong.

**This applies to us because we enforce no-self-touching** (§6.31 verified our boards satisfy it). It is a propagation rule of a strength `HumanSolver` currently has nothing like — its three rules are all local degree-counting, whereas this one propagates diagonally across the whole board from a single placed turn.

**The rest of the human technique list**, from the Numberlink primer and Puzzolve's strategy guide — these are what an experienced player actually does, and the tier order is roughly the difficulty scale we want:

1. **Corner doctrine** — a dot in a corner has two directions, so start there. Edge dots next.
2. **Corner chains / corner dual** — as above.
3. **Pinch points** — cells every route to a dot must pass through; a single route is a forced move.
4. **Orphan test** — zero free neighbours is dead, one free neighbour will orphan if taken. (`HumanSolver` has this; it is our `ForcedByDegree`.)
5. **Parity** — detours cost cells in pairs, so a path's length has fixed parity and routes of the wrong parity are eliminated without tracing them.
6. **Cell budget** — cells ÷ pairs is the average path length; a path that overspends starves the others.
7. **Corridor priority** — when two paths want one narrow passage, the more constrained one takes it.
8. **Assumption** — only after all of the above.

We implement 4, partially 3, and none of 1, 2, 5, 6, 7. That is why `HumanSolver` reports `Hardest = Assumption` on every board including Flow Free's: with three weak rules, everything looks like it needs a guess.

**This also resolves the contradiction in the last measurement.** Nikoli and Pelánek both hold that a puzzle needing backtracking is *badly formed*, yet our solver says the Flow Free board needs 13 assumptions. Both cannot describe the same thing. The resolution is that our technique library is too poor to be evidence about the board — a real player solves that board by deduction, using rules 1, 2, 5 and 7 that we have not written. The 13 is a measure of our solver's ignorance as much as of the board.

**What a commercial Numberlink publisher actually ships.** PuzzleMadness generate by laying random links (up to six turns each), filling isolated regions, then merging adjacent links — recognisably our refinement-and-merge-down pipeline. What is different is that they **do not rate difficulty at all**; they gate on structure:

- unique solution, verified by their own solver;
- **every link at least 3 cells — a single 2-cell link restarts the whole board;**
- a deliberate spread of link lengths, both short and long;
- **total link length between 85% and 115% of the grid's cell count.**

They also say the rules were arrived at by building, playing, and adding a constraint for each thing they disliked. Our current pipeline has no minimum path length and no length-distribution requirement, and short 2-cell links are exactly the "free" pairs that make a board collapse.

**Nikoli names the failure mode of computer generation, and it is not the one we assumed.** Their objection is specific: computer-generated puzzles often have no straightforward starting point and demand advanced deductions immediately. Ours has the opposite defect and the same root cause — **8 of our 50 Classic levels need zero assumptions**, i.e. they are all starting point and no middle. Neither is a difficulty-tuning problem; both are the generator being indifferent to the *shape* of the solve.

**How the industry settles it in the end: telemetry.** King rate Candy Crush levels with bots trained to **imitate** human play rather than to play well, specifically to predict difficulty before release at a cadence of ~15 levels a week. Lichess do not model difficulty at all — each attempt is scored as a **Glicko2 game between the player and the puzzle**, and a rating stabilises after 20–30 attempts. Mobile puzzle benchmarks put a tuned difficulty curve at roughly **3.2 attempts per completion** after the onboarding stretch.

The lesson is not that offline metrics are useless — King build one precisely so they can ship weekly — but that **every shipped system treats the offline metric as a pre-filter and lets play data set the final order.** We have no attempt telemetry at all, and adding move-count and time-to-solve per level is a small change that would let us re-rank the campaign from real play instead of arguing about proxies.

**Concrete conclusions.**

1. **Add dependency to `HumanSolver`** — count applicable firing sites per round, mean over the first 20–30. Cheapest change, second-best single metric in the literature.
2. **Implement the corner dual rule.** It is the strongest Numberlink-specific deduction known, we satisfy its precondition, and it is likely to be what collapses Flow Free's 13 assumptions and exposes the real gap.
3. **Build the relaxation metrics** — drop-one-pair solution count and fixedness. Needs no new solver, and on the puzzle most like ours it *beat* the technique model.
4. **Adopt PuzzleMadness's structural gates** — minimum 3-cell paths, length spread, total-length band. Cheap, and it kills the degenerate boards outright.
5. **Combine, do not choose.** 0.95 came from four metrics blended, not one; our search for a single number was the wrong shape of answer.
6. **Add per-level attempt telemetry** so the final ordering comes from play, as it does everywhere else in the industry.

**Sources.** [Pelánek, *Difficulty Rating of Sudoku Puzzles: An Overview and Evaluation*](https://arxiv.org/abs/1403.7373) · [thomasahle/numberlink](https://github.com/thomasahle/numberlink) · [A Numberlink Solving Primer, Melon's Puzzles](https://mellowmelon.wordpress.com/2010/07/24/numberlink-primer/) · [Puzzolve, Connect strategies](https://puzzolve.com/intel/connect-strategies) · [PuzzleMadness, How we make Numberlink puzzles](https://puzzlemadness.co.uk/howwemakenumberlink/) · [Nikoli, Why hand made](https://www.nikoli.co.jp/en/puzzles/sudoku/why_hand_made/) · [How TensorFlow makes Candy Crush virtual players](https://www.computerweekly.com/news/252456896/How-TensorFlow-makes-Candy-Crush-virtual-players) · [lichess.org open database](https://database.lichess.org/) · [Kristensen et al., *Difficulty Modelling in Mobile Puzzle Games*](https://arxiv.org/pdf/2401.17436)

### 6.33 The research, implemented — and the defect it immediately found

All six conclusions from §6.32 are built. The headline is not any one of them: it is that the first thing the new instruments did was **fail more than two thirds of the levels we have already shipped**.

| Classic 1–50, audited | Count |
|---|---|
| Fully well-formed | **13 / 50** |
| A link touching itself | **26 / 50** |
| Path lengths too uniform | 8 / 50 |
| No opening deduction at all | 21 / 50 |

#### The corner dual law, and the generator bug it exposed

`HumanSolver` gained the corner dual rule (§6.32): where a path turns using edges *u* and *v*, the square diagonally inside the turn must be a dot or turn the same way. The proof is short — that square is adjacent to both of the turn's neighbours, each of which already spends both its connections, so linking to either would make the path touch itself; only *u* and *v* remain, and an interior cell needs exactly two.

**The law depends entirely on no link touching itself, and our generator never enforced that.** §6.31 recorded that we already satisfied the convention. That was wrong, and wrong in the way §0's first gotcha warns about: it was checked on three levels, read 0, 1 and 0, and generalised from a sample that *contained a violation*.

The rule found this by itself. Turning it on made five of ten sampled levels report UNSOLVED, and the correlation with self-touching was exact:

| | self-touches | corner-dual solver |
|---|---|---|
| L5, L10, L20, L30, L50 | 0 | solved |
| L25, L35, L40, L43, L45 | 1, 2, 1, 3, 1 | **UNSOLVED** |

Ten for ten. Across all 50, **26 levels self-touch.** So the corner dual is two things at once: our strongest deduction technique, and a defect detector we did not have. It is now gated on `StructuralGates.Report.SelfTouches == 0` — a self-touching board is rated without it rather than declared impossible.

Two soundness bugs were fixed getting there. The first version banned a colour from a *cell*; the law only forbids one *link*, and the over-restriction cut real moves out of the search — bans are now keyed by edge. And the relaxation metric read `LevelData.pairId`, which `BuildBlockGrid` does not use for the primary identity (it derives it from the colour), so it found no colours, removed none, and reported a confident zero for every board.

#### Dependency, and Nikoli's failure mode showing up in our own levels

`Rating.Dependency` counts how many places a technique could fire per round, averaged over the opening rounds — low is hard. Measured:

| | assumptions | dependency | deduction rounds |
|---|---|---|---|
| Flow Free 8×8 | 13 | **1.33** | 4 |
| Our L5 (5×5) | 0 | 3.00 | 23 |
| Our L30 (6×6) | 0 | 2.40 | 31 |
| Our L20 (6×6) | 4 | 2.75 | 17 |
| Our L43 (7×7) | 17 | **0.00** | 1 |

Flow Free's opening is narrow but real: 1.33 openings per round for four rounds before it needs a guess. Our easy boards are wide open — three simultaneous deductions, every round, for twenty-three rounds.

But **21 of our 50 levels measure dependency 0 over a single round**, which is not the hard end of the scale — it is Nikoli's specific complaint about generated puzzles, that they *"have no straightforward starting point, requiring advanced logical deductions immediately"*. Not one deduction is available from the opening position, so the player's first move can only be a guess. The blend would have *rewarded* that, since zero openings is the maximum of the dependency term. It is now a **well-formedness gate, not a score**.

**Caveat, stated plainly:** "no opening move" is measured with a three-technique solver. A human has parity, cell budget and corridor priority, none of which are implemented, so some of those 21 boards do have an opening a person would find. The 26 self-touches are objective; this 21 is an upper bound.

#### What was built

- **`HumanSolver`** — the corner dual rule (with edge-keyed bans and the self-touch precondition), and `Dependency` sampled at depth 0 before any rule fires.
- **`RelaxationMetrics`** — deletes one colour, re-solves, reports **fixedness** (cells keeping their colour) and **solution growth**. Six of nine Flow Free colours cannot be removed at all without making coverage impossible.
- **`StructuralGates`** — PuzzleMadness's rules (min 3-cell links, length spread) plus the genre's own no-self-touch requirement. The 85–115% total-length rule is satisfied by construction, since we demand exactly 100% coverage.
- **`DifficultyModel`** — the four-metric blend, weighted by each measure's reported correlation.
- **`SaveData`** — per-level `completedLevelAttempts` and `completedLevelSeconds`, per mode. Attempts are written on every level start, not on completion, because the attempt that matters most is the one the player abandons.
- **`Rebuild levels 1-50 on the difficulty model`** — gates as a filter, blend as the ranking, in two passes so the expensive model is only paid for on a shortlist.

#### Calibration, and the two blend bugs the data found

| | score | assumptions | dependency | fixedness | tangle | structure |
|---|---|---|---|---|---|---|
| **Flow Free 8×8** | **67** | 13 | 1.33 | 0.99 | 81 | pass |
| L50 (7×7) | 46 | 1 | 0.00 | 0.98 | 90 | pass |
| L20 (6×6) | 65 | 4 | 2.75 | — | 79 | pass |
| L45 (7×7) | 94 → 62 | 15 | 1.00 | — | 84 | **1 self-touch** |

L45 originally scored **94, well above the Flow Free reference, while being malformed.** Two real bugs behind it. Removing *any* colour from L45 makes coverage impossible, so every relaxed variant was unsolvable and fixedness came back 0 — meaning "not measured", which the blend read as "nothing stayed fixed" and scored as maximally hard. The term is now dropped and the weights renormalised when there is no measurement. And its self-touch was invisible to a model that only ranked.

**The general lesson, and it is the one that cost five playtests: ranking cannot fix a malformed board, it can only prefer one malformed board over another.** `WellFormed` filters; `Score` ranks; the filter runs first.

#### Honest limits

- **The weights are not fitted.** Pelánek fitted his on thousands of hours of solving times. Ours are the reported correlations used as a prior. That is why `completedLevelSeconds` now exists — until there is enough of it, the score ranks within a board size and should not be compared across sizes.
- **The technique library is still three rules deep.** Parity, cell budget and corridor priority are all missing, and every one of them missing inflates the assumption count. Flow Free's 13 is partly a measure of our own ignorance.
- **Levels 1–50 have not been rebuilt.** The menu item exists and the audit says they need it; the rebuild is a long run and has not been made yet.

### 6.34 Classic 1-50 rebuilt — and the fix that made it possible

| Classic 1–50 | Before | After |
|---|---|---|
| Well-formed | 13 / 50 | **50 / 50** |
| Uniquely solvable | 50 / 50 | 50 / 50 |
| A link touching itself | 26 / 50 | **0 / 50** |
| No opening deduction | 21 / 50 | **0 / 50** |

Stored scores: 5×5 45–68, 6×6 47–65, 7×7 53–78, against the Flow Free reference at 67.

**The first attempt failed, and the failure was informative.** Run as one job, it spent 40 minutes on the 5×5 and 6×6 blocks and was cancelled an hour into 7×7 without finishing it. The report said why:

```
5x5  levels 1-15:  generated 3829, rejected 3379 on structure
6x6  levels 16-32: generated 3372, rejected 3239 on structure
```

**88% of 5×5s and 96% of 6×6s were thrown away for self-touching.** The gates were working; the generator was producing almost nothing that could pass them. The instinct to raise the pool budget and retry would have been wrong — the cost was not the budget, it was generating garbage and filtering it.

**The fix is one rule in the growth loop.** `TryGeneratePathPartition` grows by Warnsdorff, always stepping into the most enclosed free cell — which is exactly the rule that makes a path curl back alongside itself. It now refuses a step into any cell adjacent to a cell of its own path other than the one it is growing from. `MergeDownWhileUnique` needed the same guard for a different reason: two paths that each avoided touching themselves can be joined into one that does, and that check is free next to the uniqueness proof it precedes.

Measured immediately after, on 120 attempts per size:

| | acceptance before | after |
|---|---|---|
| 6×6 | 4% | **75%** |
| 7×7 | (never finished) | **100%** |

The 7×7 block then rebuilt in **122 seconds**, having generated 203 boards and rejected 53. The same work that could not finish in an hour.

**Two process notes worth keeping.**

The rebuild was split into per-block menu items *before* re-running, so the 7×7 block could be redone without discarding levels 1–32, which had already been built and had already passed every gate. Re-running everything to fix the last block would have thrown away work that was known good.

And a scripted edit intended for the new rebuild landed on the older tangle rebuild instead, because it matched the first occurrence in the file and the older method appears earlier. It failed to compile, which is the good case; the general hazard is a text replacement that matches in more than one place and silently picks the wrong one.

**Still outstanding.** The 5×5 and 6×6 blocks were built with the OLD generator, from pools that were 88–96% rejected — so the shortlist had very little to choose between, and it shows: the 6×6 block tops out at 65, below the 5×5's 68, so the campaign dips in the middle. Rebuilding those two blocks now costs a few minutes and should give a cleaner ramp.

### 6.35 Turning the difficulty up: selection pressure and colour count

Play on the rebuilt 7x7 block came back "looks fine, can we make it more challenging". The block's own numbers said where the problem was, and it was not the ceiling:

| levels | colours | mean path | assumptions |
|---|---|---|---|
| 33-42 | 10 -> 6 | 4.9-8.2 | **7-8** |
| 43-48 | 7 -> 6 | 7.0-8.2 | 11-12 |
| 49-50 | 5 | 9.8 | 13-14 |

L50 already matched the Flow Free reference at 13 assumptions, on a smaller board. **Ten of the eighteen levels sat at 7-8.** Most of what was played was the easy half, so the floor was the problem.

**Two changes, both pointed at that.**

1. **Selection pressure, 3x -> 20x.** The shortlist was `needed * 3`, so a third of everything looked at was kept, tail included. That ratio was chosen when a candidate was expensive; refusing self-touching growth (§6.34) made candidates cheap, so the ratio can buy selectivity instead of throughput.
2. **Colour target `cells/9` -> `cells/12`, sweep width 6 -> 3.** The block's own data was unambiguous: colours 10 -> 5 tracked score 53 -> 78, monotonic, and nothing else in the table behaved as tidily. The old sweep asked for 5-10 at 7x7, so most candidates were born easy. Probed first rather than assumed -- 4 colours turned out to be reachable at 7x7 (2 of 26 sound boards), at a mean path of 12.3.

**Result:**

| | before | after | Flow Free 8x8 |
|---|---|---|---|
| Assumptions (mean) | 9.6 | **11.8** | 13 |
| Levels needing >= 13 | 2/18 | **9/18** | -- |
| Mean path | 7.2 | **10.2** | 7.1 |
| Dependency (lower = harder) | 1.41 | **1.11** | 1.33 |
| Colours | 5-10 | **4-6** | 9 on 64 cells |
| Score | 53-78 | **74-87** | 67 |

All 18 still well-formed and uniquely solvable.

**A ceiling nobody intended, found in the after-measurement.** `DifficultyModel.Measure` defaults to `maxAssumptions = 14`. A board needing 15 comes back unsolved, fails `WellFormed`, and is thrown away -- so the selection actively rejects the hardest boards the generator produces. Nine of the eighteen shipped levels sit at exactly 14, pressed against that wall. Raising the cap costs only solve time and is the obvious next lever.

**And a repeated estimation error worth naming.** The run was predicted at ~10 minutes and took 39. Both times the pool was sized from generation cost while the scoring pass -- 360 full model evaluations, each costing roughly one solve per colour plus a deduction pass -- is the expensive half. Size the run by the shortlist, not the pool.

#### Confirmed in play — the difficulty problem is closed for 7×7

Play on the rebuilt block came back **"now it is working"**. That is the first time in seven rounds of playtesting that difficulty has been confirmed right, and it retires the longest-running open problem in this document.

**Every earlier attempt was rejected by the same instrument that finally accepted this one — play.** For the record, what failed and why:

| Round | Lever tried | Verdict |
|---|---|---|
| §6.14 | Path length | Retracted — longer paths had *fewer* decisions |
| §6.23 | Stacking mechanics | "too annoying while playing" |
| §6.29 | Solver decision points | Beat Flow Free's 4600 and still played easy |
| §6.29 | Alternative pairings, forced-move collapse | Flow Free's board has zero of the first, and is *less* deducible on the second |
| §6.30 | Tangle | "feels tangled" then "still feel easy" |
| §6.34 | Structural gates | Fixed *validity*, not difficulty — 50/50 well-formed, still "looks fine" |
| **§6.35** | **Selection pressure + colour count** | **Confirmed** |

The pattern across the failures is consistent and worth keeping: **each one optimised a single number, and five of the six were measuring search effort** — the family that Pelánek's evaluation puts at the bottom of the table, r = 0.16–0.25 against human solving time. What finally worked was not a better single metric. It was a filter for well-formedness, a four-family blend for ranking, and then pressure applied to the two levers the blend said actually mattered.

#### The calibration that worked — reuse these numbers

This is the point of recording it. These settings produced a block that a person confirmed as challenging, so they are the baseline for the 5×5 and 6×6 blocks, for Advanced, and for any move to 8×8:

| Generator setting | Value |
|---|---|
| `ShortlistPerLevel` | **20** (score 20 candidates per level kept) |
| `CellsPerColourTarget` | **12** (aim at cells ÷ 12 colours) |
| `ColourSweepWidth` | **3** (so 4–6 colours at 7×7) |
| Gates | `StructuralGates.Passed` **and** dependency > 0 **and** uniquely solvable |
| Ranking | `DifficultyModel.Score`, kept descending then re-sorted ascending for the ramp |

And the measured profile of a block that plays right, on 49 cells:

| Measure | Confirmed-good value | Flow Free 8×8 |
|---|---|---|
| Assumptions, mean | **11.8** | 13 |
| Levels needing ≥ 13 | **9 / 18** | — |
| Mean path length | **10.2** | 7.1 |
| Dependency (lower = harder) | **1.11** | 1.33 |
| Colours | **4–6** | 9 on 64 cells |
| Score | **74–87** | 67 |

Note that the confirmed-good board is *longer-pathed and narrower-opening than the Flow Free reference*, on a smaller grid — it reaches comparable depth by stretching fewer colours further rather than by having more cells. That is the shape to preserve when moving to 8×8: more cells should buy **more colours at the same path length**, not shorter paths.

### 6.36 The whole campaign on one calibration

The 5×5 and 6×6 blocks rebuilt with the settings play confirmed on 7×7 (§6.35), unchanged. **109 seconds** for both, against 39 minutes for 7×7 alone — and this time the estimate was right, because the run was sized from a measurement of the scoring pass rather than from generation cost:

| | measured per board | shortlist | projected | actual |
|---|---|---|---|---|
| 5×5 | 16 ms generate, 19 ms score | 300 | 0.2 min | — |
| 6×6 | 120 ms generate, 209 ms score | 340 | 1.9 min | — |
| both | | | **~2 min** | **1.8 min** |

**The campaign, end to end:**

| block | levels | score | assumptions | mean path | dependency |
|---|---|---|---|---|---|
| 5×5 | 1–15 | 56–65 | 4.3 | 5.8 | 1.04 |
| 6×6 | 16–32 | 62–75 | 7.4 | 8.1 | 1.16 |
| 7×7 | 33–50 | 74–87 | 11.8 | 10.2 | 1.11 |
| *Flow Free 8×8* | | *67* | *13* | *7.1* | *1.33* |

50/50 well-formed, 50/50 uniquely solvable, zero self-touches, zero levels without an opening deduction.

**Every measure now rises monotonically across the blocks** — assumptions 4.3 → 7.4 → 11.8, path 5.8 → 8.1 → 10.2 — which is the first time this campaign has had a ramp rather than a scatter. The old middle dip is gone: the 6×6 block used to top out at 65, BELOW the 5×5's 68. Each block's range now overlaps the previous one by only a few points at the boundary, which plays as a short breather when the board grows rather than a step backwards.

Worth noting what did NOT change to achieve this: not the gates, not the blend, not the weights, and not one line of the difficulty model. The same calibration that worked at 7×7 produced a coherent ramp at 5×5 and 6×6 on the first attempt, because `CellsPerColourTarget` scales with the board — cells ÷ 12 asks for 3–5 colours at 5×5 and 4–6 at 7×7 on its own. That is the argument for keeping difficulty settings expressed as ratios rather than per-block constants.

### 6.37 Packs: 300 levels, chosen by board size

The campaign structure changed. Instead of one linear run of levels that grows a board size at a time, Classic is now **three packs of 100 — 5×5, 6×6 and 7×7 — and the player picks which to play**. Each pack ramps on its own from the easiest board that size can produce to the hardest; there is no cross-pack ordering to preserve.

| pack | build | generated | duplicates | ramp (score) | assumptions | mean path |
|---|---|---|---|---|---|---|
| 5×5 | 269 s | 36,964 | 93% | 25 → 69 | 2.6 | 5.4 |
| 6×6 | 174 s | 1,982 | 24% | 29 → 75 | 4.0 | 7.4 |
| 7×7 | 61 min | 2,793 | 3% | **31 → 95** | 7.1 | 8.7 |

Audited independently afterwards, sampling each pack: **every level unique, well-formed, and carrying a stored solution that matches the solver.** Across every board the three builds evaluated, `not unique` and `bad solution` were both **0**.

#### Stratified selection — the change that makes a pack a pack

Selection took the **top N by score**. That is right for eighteen levels appended to a campaign and wrong for a hundred-level pack the player enters at level 1: the hardest hundred of two thousand are bunched at the top of the range, so the pack opens hard and barely climbs.

`SelectStratified` walks the score **range** rather than the population — for each slot it asks for a target difficulty and takes the nearest board not yet used. Taking every *n*th board by rank would instead follow the distribution, and since scores cluster in the middle that yields a pack where most levels feel alike and the ends are sparse.

The result is visible in the 5×5 pack's score walk, every fifth level:

```
25 27 30 32 34 36 38 40 43 45 47 49 51 53 54 56 57 60 62 64
```

It is used twice per pack: once to choose the ~300 finalists for stage two, so the expensive model sees the whole range and not just the top, and again to pick the final 100.

#### Two-stage scoring — 98% of the cost, 23% of the weight

`DifficultyModel.Measure` was too expensive to run on thousands of candidates. Measured on 7×7:

| | per board |
|---|---|
| Everything except relaxation | **55 ms** |
| Full model | **2243 ms** |

`RelaxationMetrics` deletes each colour in turn and re-solves, and a board missing a colour is *less* constrained, so each of those solves is dearer than the original. Meanwhile fixedness carries 0.61 of the blend's 2.63 total — about 23%. The three cheap terms carry the other 77%, including refutation, the heaviest single term and the one play responded to.

So `Measure(..., includeRelaxation: false)` ranks every candidate, and the full model runs only on a stratified slice of finalists. `Blend` already dropped an unmeasurable fixedness term and renormalised, so stage one needed no special case.

#### Storing the solution — and the §6.24 conclusion that expired

Asked before generating, because a hint system needs to know the answer. §6.24 had measured deriving a solution on-device at 2.6 ms average and concluded storage was unnecessary. **That is no longer true, and the reason is our own doing:**

| Find ONE solution | §6.24 | after §6.35 |
|---|---|---|
| Average | 2.6 ms | **49.5 ms** |
| Worst | 34 ms | **771 ms** |
| Over one 60 fps frame | 1 / 55 | **10 / 50** |

Fewer colours and longer paths mean fewer constraints and a far bigger search. **The property that makes these levels good to play is the same one that makes them expensive to solve** — and that is desktop; a phone is several times slower again, so a hint tap could freeze the UI for seconds.

`GridRow.solutionPairId` now records the pair covering each cell. `BuildPlainLevelData` fills it, because the partition IS the solution and that is the last place still holding it — the generator was computing the answer, using it for the gates and tangle, and discarding it.

Two things this buys beyond speed, and both depend on uniqueness:

- **A hint can never be wrong.** One solution means one correct colour per cell, so a hint cannot contradict a valid line the player is pursuing. With two solutions there is no "the" answer to reveal.
- **A wrong move is provably wrong.** Any path departing from `solutionPairId` cannot be part of the answer, so the player can be told immediately rather than filling the board and failing.

The pack builder verifies both at build time — it re-proves uniqueness with `maxSolutions: 2`, and checks the stored answer against the solver. Both are redundant on paper. §6.20 is why they are there anyway: the Bridge constructor guaranteed two colours crossed, and the dots *derived* from it admitted a different unique solution.

#### 8×8 measured, and set aside

| ask | yield | mean path |
|---|---|---|
| 5–7 colours | **0 / 162** | — |
| 10 colours | 7 / 150 | 6.4 |
| 12 colours | 9 / 150 | 5.3 |

8×8 is reachable only at 10–12 colours, giving paths of 5.3–6.4 against 7×7's 10.2. More cells forces more colours to keep uniqueness provable, which *shortens* paths. An 8×8 pack would not be a harder 7×7; it would be a different, shorter-pathed puzzle. Consistent with stopping at 7×7.

#### A negative result worth keeping: 5×5 cannot be pushed

The first 5×5 pack duplicated 70% of the time, so the pool was raised from 600 to 1500 and the shortlist from 5× to 12×:

| | before | after |
|---|---|---|
| Build time | 38 s | 269 s |
| Generated | 3,531 | **36,964** |
| Duplicate rate | 70% | **93%** |
| Ramp | 25 → 66 | **25 → 69** |

**Seven times the compute bought three points on the ceiling and nothing on the floor**, and it still fell short of the 1500 target, exiting on the attempt cap.

**The higher duplicate rate is the point, not a regression.** Reading 70% → 93% as "the second run was worse" gets it backwards: the rate is not a property of the generator, it is a property of how full the set already is. Every board found makes the next one more likely to be a repeat. The second run contains the first run's cheap early boards *and* then keeps going into the tail, so its average is dragged up by the part the first run never attempted.

The marginal cost is the honest number:

| | generated | distinct | generated per new board |
|---|---|---|---|
| First run | 3,531 | 600 | **5.9** |
| Second run | 36,964 | 1,466 | **25.2** |

Roughly 4× dearer per board, for boards 601 through 1466. A fresh 2000-attempt probe duplicates at only ~50%, which confirms the shape: it is a coupon-collector curve against a finite reachable set, not a defect that appeared between runs. And widening the colour sweep, the obvious diversity lever, measured **worse**: asking 3–7 colours yielded 92 distinct sound boards per 2000 attempts against 3–5's 122, because high-colour attempts mostly fail to generate at all.

**The 5×5 ceiling is board size, not selection pressure.** Twenty-five cells holding three to five paths have no room for more interaction, and searching harder does not change that. Average assumptions is 2.6, a fifth of what 7×7 reaches. A harder small-board experience needs a mechanic, not more search.

#### Three time estimates, three misses

Predicted 10 minutes for a 7×7 rebuild that took 39; predicted 2 minutes for the small blocks and got 1.8; predicted 13 minutes for the 7×7 pack that took 61. The pattern in the misses is the same each time: **whichever stage was not front of mind got left out of the arithmetic.** The 39-minute miss omitted the scoring pass; the 61-minute miss omitted gathering, which for 2,100 canonically distinct boards is the dominant cost and gets slower as the pool fills. The one estimate that landed was the one taken from a direct measurement of every stage.

### 6.38 Five packs, and the colour-count error that had to be found twice

| pack | ramp | assumptions | mean path | colours (cells per colour) | build |
|---|---|---|---|---|---|
| 5×5 | 25 → 69 | 2.6 | 5.4 | 3–5 (8.3 → 5.0) | 4.5 min |
| 6×6 | 29 → 75 | 4.0 | 7.4 | 4–7 (9.0 → 5.1) | 2.9 min |
| 7×7 | 31 → 95 | 7.1 | 8.7 | 4–7 (12.3 → 7.0) | 61 min |
| 8×8 | 30 → 88 | 7.8 | 7.4 | 7–10 (9.1 → 6.4) | 2.5 h |
| 9×9 | 25 → 93 | 14 at L100 | 7.4–8.1 | 9–12 (9.0 → 6.8) | **23 h** |

500 levels. Every one sampled came back unique, well-formed, and carrying a stored solution that matched the solver; across every board the five builds evaluated, `not unique` and `bad solution` were both 0.

#### The 8×8 colour error, and how it was made

The first 8×8 pack shipped at 10–12 colours on the strength of a recorded claim that nothing lower would generate. **That claim came from samples of ten and twelve attempts at a yield rate near 5%** — where zero results is the *expected* outcome even when the colour count works perfectly well. It was then quoted as "0 / 162", which was not a measurement of anything.

Play feedback ("we have too many colors for 8×8") prompted a proper probe, 1500 attempts per count:

| ask | sound / 1500 | mean path | ms per sound board |
|---|---|---|---|
| 7 | 51 | 8.9 | 6772 |
| 8 | 71 | 7.9 | 1916 |
| 9 | 54 | 7.1 | 877 |
| 10 | 83 | 6.4 | 251 |

Seven, eight and nine all yield perfectly well. They are simply **far dearer per board** — which is exactly what a ten-attempt sample cannot see, because it observes only the instant partition failures and never reaches the informative case. Nine colours also reproduces Flow Free's own 8×8 exactly: 9 colours, mean path 7.1.

Rebuilt at 7–9:

| 8×8 pack | 10–12 colours | **7–9 colours** |
|---|---|---|
| Assumptions | 4.9 | **7.8** |
| Dependency (lower = harder) | 2.83 | **1.93** |
| Mean path | 5.6 | **7.4** |
| Ramp | 20 → 71 | **30 → 88** |

Fewer colours *and* longer paths *and* harder, moving the pack from below the 7×7 one to above it. The original configuration was the worst of both worlds.

**The general lesson: a bigger board does not mean a harder pack — density does.** Measured across the shipped packs, cells-per-colour lands in the same band every time (7×7: 12.3→7.0, 8×8: 9.1→6.4, 9×9: 9.0→6.8). Board size sets how much *material* there is; the colour ratio sets how hard it plays. This intuition has now been wrong twice in this document, which is why the ratio is expressed as `cellsPerColour` per pack rather than as a colour count.

#### The CPU throttle, and a bug that defeated it silently

Long runs were pinning a core flat out for hours, risking thermal throttling — which slows the run down anyway, on top of what else it risks. `CpuThrottle` holds a target duty cycle by sleeping in proportion to work actually done, rather than sleeping a fixed amount per iteration; that distinction matters because attempt costs vary enormously, from a sub-millisecond partition failure to a seven-second uniqueness proof.

The first version capped each sleep at 250 ms to keep Cancel responsive — and thereby **truncated the rest debt instead of paying it.** A 6.8 s attempt owes ~4.5 s of rest and got 250 ms, so a run intended to hold 60% measured at **92% of a core**: the mechanism failed precisely on the expensive work it existed for. The cap belongs on each *sleep*, not on the *debt*; paying it off in 250 ms slices preserves both properties. Measured over the 9×9 run: **60.5% against a 60% target.**

#### 9×9, and what it cost

Probed first, 400 attempts per count:

| ask | sound / 400 | mean path | ms per sound board |
|---|---|---|---|
| 9 | 8 | 8.7 | 19,491 |
| 10 | 2 | 7.7 | 62,011 *(2 samples — noise, not a measurement)* |
| 11 | 13 | 7.3 | 11,622 |
| 12 | 9 | 6.7 | 2,595 |

**The affordable configurations are the ones that make a worse puzzle.** At 12 colours a board costs 2.6 s and gives a 6.7-cell path — shorter than the 8×8 pack already manages. Only 9–11 beat it, at 12–20 s per board. The full pool at 9–11 was chosen deliberately and took **23 hours**.

It ramps through every measure at once:

| level | assumptions | dependency | score |
|---|---|---|---|
| 1 | 1 | 6.20 | 25 |
| 50 | 10 | 2.13 | 59 |
| 100 | **14** | **1.00** | 93 |

Level 100 needs 14 assumptions with a single deduction available per round — above the Flow Free reference board's 13.

#### Two things left open, and one is the same error again

**The 9×9 probe never tested 7 or 8 colours.** The range `{9,10,11,12}` was chosen by analogy with 8×8's density rather than measured — *the identical mistake that put the first 8×8 pack at 10–12 colours*. At 81 cells, 7 colours is 11.6 cells each and 8 is 10.1, which would be longer paths than anything currently shipped. Cost is the reason to expect difficulty rather than a reason not to look: per-board cost climbed 2.6 s → 11.6 s → 19.5 s as colours fell from 12 to 9, so 8 colours could be several days for a full pack. Probing it is cheap even if building it is not.

**`maxAssumptions = 14` is now hard against the ceiling.** 9×9 level 100 measures exactly 14, which means anything harder was discarded during selection rather than ranked. On the hardest pack we own, that cap is actively throwing away the best material.

#### A note on reading long runs

Mid-way through the 9×9 build I read two monitor events that arrived hours apart as though they were minutes apart, declared the run "far too fast", and asserted it had hit its attempt cap. It had not — it ran for 11 hours of gathering exactly as the probe predicted. Elapsed time on a long job has to be read off the clock, not inferred from the spacing of notifications.

### 6.39 Advanced pack design: teach to 50, interleave and escalate to 100

#### Two hard constraints, both measured

**Two rules cannot share a board.** Probed at 400 attempts across four mechanic pairs, then again across three colour deficits: **4 boards in 1600, then 1, 0 and 2 out of 300 each.** 103 combinations did pin their board, but the second rule almost always *subsumes* the first — B alone would have sufficed, leaving A decorative. Adding rules in sequence does not make both necessary, and more ambiguity does not help: it just gives the first rule more to do.

**Instance count is governed by the colour deficit**, and that lever works well:

| deficit | cells the rule ends up needing |
|---|---|
| 1 | **1×11**, 2×8, 3×3, 4×5, 5×2 |
| 2 | 1×6, 2×3, **3×8**, 4×6, 5×2, 6×3 |
| 3 | 1×1, 2×4, 3×3, **4×7**, 5×1, 6×1 |

At one colour down the mode is a single cell; at three it is four, and single-cell boards nearly vanish. So *multiple* checkpoints or one-ways are available on demand — and earned, because the board genuinely needs them to have one solution.

#### What the research says to do with that

Two findings, and they point the same way.

**The contextual interference effect.** Interleaved practice — items from different categories mixed so the same one never repeats consecutively — gives *worse* initial acquisition but better long-term retention and transfer than blocked practice. It is one of the "desirable difficulties": the learner must retrieve afresh each time rather than running a rote response from short-term memory. The corrective literature is equally clear that **initial blocked practice still matters for acquisition**, so the answer is block first, interleave second.

**Puzzle-design pacing.** Scale density back when introducing a mechanic; follow the introduction with simple reinforcing levels; aim for a saw curve where each new mechanic is a valley.

**Together these resolve the mixing problem.** "Mix the mechanics" is achievable *between* levels even though it is impossible *within* one — and interleaving across levels is the form that actually produces retention. The constraint and the good design agree.

#### The plan

**Levels 1–50 — blocked acquisition, deficit 1.**

| slots | content |
|---|---|
| warm-up | Blocked, ramping. Needs no introduction; holes are self-evident. |
| runs | **Three practice levels per rule**, one run each, drawn at deficit 1–2. |
| consolidation | Interleaved across every rule, still gentle — the remainder of the first half. |

**There is no separate teaching level, and that was a deliberate removal.** A run is stratified from its own rule's difficulty range, so its opening level is already the easiest board carrying that rule — and at deficit 1 that board usually holds a *single* cell of it, which is the clearest form the rule takes. The dedicated teaching slot was adding a level that differed from the one after it only in name, so the introduction now falls out of the ordering rather than costing a slot.

That keeps Blow's "extreme clarity around the first layer" and the pacing literature's density valley, without spending two slots per rule to get them.

**Bridge and Shared Destination are included here.** `HumanSolver.CanRate` refuses both, so they cannot be *ranked* — but a teaching slot does not rank, it only needs the board to be correct and simple. They are gated on uniqueness and the structural gates alone, and placed at fixed positions.

**Levels 51–100 — interleaved escalation, deficit 2→3.**

Three escalations at once, each measured to be available:

1. **Interleaved rules** — no two consecutive levels share a mechanic. This is the "mixing" that is actually possible, and the one the retention research endorses.
2. **Rising instance counts** — the deficit climbs from 2 to 3 across the block, so levels move from ~3 load-bearing cells to ~4. Four checkpoints that all matter is a genuinely different puzzle from one.
3. **Rising difficulty score**, stratified as elsewhere.

Bridge and Shared Destination appear at a few fixed slots here too, unranked, so they are not taught and then abandoned.

#### What this deliberately does not do

- **No two-rule boards.** Measured three separate ways; the yield is a rounding error and the survivors carry a decorative rule.
- **No raising the instance ceiling by fiat.** Counts rise because the deficit makes the board need them, never because a quota asked for them.
- **No difficulty score on Bridge or Shared Destination levels.** They sit at authored positions rather than being ranked by a model that cannot read them — which is honest about a known limit rather than inventing a number.

#### As built

All nine mechanics generate. They fall into three groups, and the differences are structural rather than cosmetic:

| group | mechanics | how | ranked |
|---|---|---|---|
| Partition shape | Blocked | holes placed before partitioning | yes |
| Edges | Wall | `PlaceWalls` on edges the solution never crosses | yes |
| Overlay rules | One-Way, Arrow, Checkpoint, Forbidden, Permitted | laid on an under-constrained partition, climbing until pinned | yes |
| Structural | Bridge, Shared Destination | the older spec pipeline | **no** |

**Mechanic identity had to stop being a `BlockType`.** There is no `BlockType.Wall` — a wall is a blocked *edge* in `wallMask`, not a cell — so keying the schedule on cell type made walls unrepresentable without inventing a fake enum member. Runs are keyed by name instead.

**Both permission rules already supported two colours and we were not using it.** `Block.NamesPair` checks `secondPairId`, and the border art draws a slice per named colour; the generator only ever set `pairId`. Both forms are now generated, and they are worth having because **they pull in opposite directions**: a second *forbidden* colour refuses one more path and tightens the board, a second *permitted* colour admits one more and loosens it. So the pair covers boards the one-colour form over-constrains as well as ones it under-constrains. Two is the model's ceiling rather than a choice — past that the cell stops being readable and the honest form would be a bitmask.

The two-colour form is a *variant*, not a rule: it keeps its own pool cap so both get generated, but shares the player-facing run. Giving it a run of its own split six rules into eight groups and starved each.

**Checkpoint and One-Way start their climb at two cells**, because one of either reads as a quirk rather than a rule. This does not pad the board: `AllCellsOfTypeAreNecessary` requires every cell of the type to be individually load-bearing, so a board where one would have sufficed is rejected as decorative. Raising the floor selects for boards that genuinely need two; it cannot invent them. Measured across a 260-board pool, `Checkpointx1` and `OneWayx1` vanished entirely and the decorative rejection count rose from 891 to 1522 — which is precisely those boards being thrown out.

**Bridge and Shared Destination reuse the spec pipeline** that built the shipped levels 36–40 and 46–50, including the `EveryBridgeCarriesTwoColours` check that caught the L40 self-crossing bug in §6.20. They change the partition's *shape* — one cell split into two lanes, or one cell as the endpoint of two paths — so the deficit-and-climb construction cannot express them. They arrive unranked and are spaced evenly at authored positions, which is honest about a known limit rather than inventing a difficulty for them. Note that `TryGenerateLevel` *reports* uniqueness rather than guaranteeing it, so the call site checks `SolutionsFound == 1 && SearchExhausted` — asking a spec for uniqueness is not the same as getting it.

**No teaching levels.** They were designed in and then removed: a run is stratified from its own rule's range, so its opening level is already the easiest board carrying that rule, and at deficit 1 that board usually holds a single cell of it. The dedicated teaching slot was producing a level that differed from the next one mainly in name.

#### Two bugs the small probe caught that the real pack never would have

The probe runs at 24 levels; the pack runs at 100. Both of these are invisible at 100 and fatal at 24, which is the argument for keeping the probe small.

- **`Stratify(entries, 0)` returned one entry instead of none** — the `count <= 1` branch did not separate "none" from "one". Consolidation showed a single level at score 80 when its budget was zero, making it *harder* than the escalation block beneath it.
- **The run block could overflow the slot budget.** At 24 levels, 8 rules × 3 exceeded the 12-slot first half, so the ordering exceeded `count` and the write truncated the tail — silently dropping the escalation half. At 100 the arithmetic happens to come out exact, so it would have shipped unnoticed and bitten the first smaller pack. Runs now shrink before they overflow, and never below one level per rule.

#### The open door

Extending `HumanSolver` to model Bridge (multi-occupant cells) and Shared Destination (one cell, two endpoints) would let both be ranked and take full part in the escalation half. It is real work — `State.Owner` holds one pair id per cell — and it is the single change that would most widen what Advanced can be.

### 6.40 Instance floors, wall pairing, and where the Advanced generator actually stands

#### One cell of a rule reads as a quirk, not a rule

Play on the first full 6×6 pack: *"some levels I played which has only one arrow."* The data agreed — `Arrow: 1 cell ×6`, and `Wall: 1 cell ×6` too. A single arrow is a peculiarity of that one board; it does not teach the player that arrows exist.

Checkpoint and One-Way had already been given a floor of two for exactly this reason, because those were the two named at the time. The argument was never carried across to the rest, and only playing them surfaced it.

Every rule now starts its climb at **two** cells, walls at **three**, and Bridge and Shared Destination ask their spec for **two**. Walls get three rather than two because `PlaceWalls` grows a *connected* barrier (§6.17) — two edges can only ever make an L, and three is where a T or a longer run becomes possible.

**This is not padding, and the distinction matters.** `MinInstances` sets where the climb starts, not what survives: `AllCellsOfTypeAreNecessary` still requires every cell of the type to be individually load-bearing, so a board where one arrow would have sufficed is rejected as decorative rather than shipped with a spare. Raising the floor *selects for* boards that genuinely need two; it cannot invent them. The evidence is the rejection count — decorative rose from 891 to 1522 on a comparable run, which is precisely those boards being thrown out.

#### Both permission rules always supported two colours; we were using one

`Block.NamesPair` checks `secondPairId`, and the border art draws a slice per named colour. The generator only ever set `pairId`. Both forms are now generated, and both are worth having because **they pull in opposite directions**: a second *forbidden* colour refuses one more path and tightens the board, a second *permitted* colour admits one more and loosens it. So the pair covers boards the one-colour form over-constrains as well as ones it under-constrains.

The two-colour form is a variant, not a rule: it keeps its own pool cap so both get generated, but shares the player-facing run. Giving it a run of its own split six rules into eight groups and starved each.

#### Wall + rule pairing: the same answer, and a measurement bug in my favour

"Two rules cannot share a board" had been measured only on pairs of CELL rules — One-Way+Checkpoint, Arrow+Forbidden, Checkpoint+Forbidden, One-Way+Arrow. Every one of those restricts entry to cells, so one subsuming the other is close to predictable. A wall blocks an EDGE regardless of colour or direction, which is a different kind of constraint entirely, and it had never been tested. Prompted by *"in some other level some have arrow some will have Walls."*

| pair | both load-bearing |
|---|---|
| Arrow + Wall | 0 / 400 |
| One-Way + Wall | 0 / 400 |
| Checkpoint + Wall | 5 / 400 — *shapes `2+0w`* |
| Forbidden + Wall | 1 / 400 — *shape `2+2w`* |

**The 5 are not pairings at all.** Their shape is `2+0w`: two checkpoints and *zero walls*. `AllWallsAreNecessary` passes vacuously on a board with no walls, so the check counted checkpoint-only boards as successful pairings. Corrected, the real figure is **1 genuine wall+rule board in 1600** — statistically identical to the 4-in-1600 for cell pairs.

So the conclusion extends to walls, now tested rather than assumed. My reason for expecting walls to differ was plausible and wrong: the binding constraint is not *what kind* of thing a rule restricts, but that once a board is pinned, whatever pinned it leaves nothing for anything else to do.

**The general lesson is about the measurement, not the mechanic.** A predicate that quantifies over a set is vacuously true on the empty set, and the number it produces is silently wrong in the direction you were hoping for. Any "all X are necessary" check needs a companion assertion that there *were* some X.

#### Blocked cells alongside a rule: a yield problem, not a design limit

Only **4 of 100** shipped levels carried blocked cells with a rule, though half the recipes asked for it. Holes remove cells before the partition is built, so those boards are markedly harder to pin and lost the pool race at equal weight.

Fixed with two changes, and one alone would not have worked: blocked-plus-rule recipes are entered **twice** against the bare form's once, **and** capped separately. Weighting alone would have been undone by the bare form filling the shared cap — the fix would have looked applied and done nothing.

#### Where the generator actually stands

| pack | state |
|---|---|
| 6×6 | **Ready and proven** — 100 levels built, verified, committed |
| 7×7 | **Investigated, not yet built — see §6.44.** The wired call (`cellsPerColour=10`) fails outright: 6.75h to gather 46 of a 260 probe target, hard-capped at 200,000 attempts. Root cause found and fixed (a length-equalising bias in the shared partition builder), `cellsPerColour=7` measured as the real pick (~3.3h estimated to a 900-pool, small sample). The 100-level pack itself has not been generated yet -- that is the next action. |
| 5×5, 8×8, 9×9 | **No entry point at all** |

Two things block the rest:

**The instance ceiling is hard-coded at 6** on every recipe. Harmless at 6×6, where boards settle on two to four cells. At 9×9 a board may legitimately need eight to become unique, and it is discarded as a generation failure — which would read as "9×9 will not generate" rather than "the ceiling was too low". The same shape of error as the 8×8 colour-count claim.

**The colour ratio is not guessable per size.** `cellsPerColour` is 9 at 6×6 and 10 at 7×7, both from measurement. Classic had to measure 8×8 and 9×9 the hard way, and got 8×8 wrong the first time by probing too small a sample.

**5×5 is genuinely doubtful.** Classic already hit a material ceiling there — 226 distinct boards in 4000 attempts, 93% duplicates when pushed. Advanced demands strictly more: ambiguous first, then pinned by a mechanic whose every cell is load-bearing, on 25 cells carrying 2 holes and a 2-cell rule floor. It may not have the room.

#### Proposed, not implemented: scale the window with the board

Floor `cells / 18`, ceiling `cells / 6` — so 6×6 keeps 2 and 6, while 9×9 gets 4 and 13. This sets the *window*, not the count: the number is still whatever the board needs, and every cell still faces the necessity gate.

Two exceptions:

- **Permitted must NOT scale up.** It refuses everyone *not* named, so its strength grows with colour count — on a 9×9 with ten colours a single permitted cell is roughly three times the constraint it is on a 6×6. Raising its floor there would push boards into unsolvability, and the failures would show up as poor yield rather than as anything obviously wrong.
- **Shared Destination is bounded by arithmetic**, not judgement: each cell is the endpoint for two to four colours, so a board with C colours holds at most C/2 of them.

### 6.41 The hint system, and why reading the stored answer is a search

One hint, one pair: the button joins the lowest-numbered pair that is not already drawn correctly, along the route the level's own answer gives it. No limit on taps, no economy, no second kind of hint.

**The stored column is a colouring, not a route, and that is the whole difficulty.** `solutionPairId` says which pair covers each cell and nothing about the order they are visited in. The obvious reconstruction — from a dot, step to whichever neighbour is also mine — is wrong on ordinary boards, not exotic ones: a Flow path routinely runs alongside itself, and then two cells are neighbours on the grid while being many steps apart along the path. The failure is silent and it is the worst shape a hint bug can have — a route that joins the pair while skipping its own cells, on a board that only completes when every cell is covered, so the player is told the answer and the answer does not work.

`HintPath` backtracks instead, and requires the route to use **every** cell the answer gave the pair. That requirement is what makes the reconstruction unambiguous rather than merely lucky; the two dots being adjacent is then just another branch to reject rather than a special case. Each step is checked against the board's own predicates — walls, one-way entry, an arrow's forced exit, a bridge's straight-through-only — minus `CanAcceptEntry`, which asks who is drawn there *now*: the route is a property of the level, not of the board mid-play.

**Bridges needed one extra rule, and only measurement showed it was needed.** The column holds one pair id per cell, so at a crossing only one of the two paths is recorded there — the other pair's cells are left split in two by a cell it does in fact pass straight through. Its route has to cross a cell the answer gave to someone else, which is the single exception to "only my own cells". This was nearly written off as a legacy-only concern: `HumanSolver` refuses Bridge, so the assumption was that no pack level carries one. **Four of the 100 shipped Advanced levels do** (12, 23, 34, 45), and without the exception the hint would have failed on exactly those four.

**The route traces itself rather than appearing.** Drawn instantly, a whole route arrives at once and the player has to work out afterwards which pair moved and where it went — play feedback on the first build was exactly that. It now draws a cell at a time, the leaving cell's bar growing out to the shared edge and then the entered cell's bar in from it, with the touch pointer riding the head of the line: the same two calls per step a drag commits, only spaced over frames. About 0.7 s for a route, clamped per step so a two-cell pair does not finish before the eye finds it and a twenty-cell one does not make the player wait.

Two things that follow from it rather than being decoration. The board is closed to input while a route draws (`GameState.Waiting`), because a drag over cells the route has not reached yet would be editing a line still being laid down — which also makes a second tap a no-op for free. And nothing is registered in `pairSegments` until the last cell lands, so a route interrupted half-way counts as nothing rather than as a wrongly-drawn pair; `ResetGameplay` stops the trace, since its next frame would otherwise draw onto cells that are being destroyed.

**Taking cells back is a trim, not a wipe.** Cells on the route that another pair is holding are trimmed to the cell before the one taken — the same thing a drag stealing a cell already does — rather than clearing that pair's whole line. It runs as its own pass before anything is drawn, because trimming resets a cell's bars by direction regardless of who owns them, so interleaving the two passes would let a later trim erase a bar the hint had just drawn.

**Two design choices worth stating.** The hint targets pairs that are not already *correct*, not pairs that are not already *joined*: a pair joined by the wrong route still blocks the level, and skipping it would make the hint look broken to the player who most needs it. And it counts as a move, because it changes the board exactly as a drag would and the move count is the record of what finishing took.

**Verified two ways.** Offline, every one of the **600 shipped levels — 3,776 pairs — reconstructs a route, and each level's routes together cover every usable cell**, which is the property that makes hinting every pair finish the board. In play, seven levels including all four bridge levels were hinted to completion (36/36 cells, win screen fired), one of them after a deliberately wrong partial route had been drawn first, so the trimming path was exercised rather than assumed; the animated version was then re-run on a bridge level and caught mid-trace to confirm it draws progressively rather than snapping. 181 tests pass, 12 of them new.

**One thing to know before verifying this in the Editor again:** an unfocused Editor does not tick play mode unless Run In Background is on, so the trace simply does not advance and reads as a hang. `Application.runInBackground = true` at runtime is enough; `Time.timeScale` slows the trace for a screenshot. Neither is a code change — the first build of this was verified by calling `TryApplyHint` in a loop, which worked precisely because nothing needed frames.

**Where it did not work, by design, before the campaign was retired:** the legacy Advanced 1-45 campaign carried no stored answer at all (0 of 45), so the button turned itself off there rather than solving on device — which §6.24's own measurement rules out at 49.5 ms average and 771 ms worst on desktop. Backfilling would have needed the generator's `FillStoredSolution`, but those were the Bridge and Shared Destination boards, where the column is inherently partial — one more reason those levels were retired rather than backfilled (see Open Questions).

### 6.42 Phase 9 started: schema versioning, per-mechanic skill, hint telemetry

Picked as the next slice for a reason the doc's own open questions already stated: of the four untouched phases (9–13), only 9 has no product decision gating it — the pack-select UI (Phase 11) needs an answer on whether packs unlock or are all open, daily challenge (10) needs a difficulty number that does not exist yet (see the open `DifficultyAnalyzer.Score` question below). Save-data work needed neither.

**`SaveData` gained a real version number.** `schemaVersion` (`SaveData.CurrentSchemaVersion = 1`) plus a `Migrate` method, called once by `SavingSystem.Load` whenever an existing save reads below the current version. The struct has taken five rounds of additive fields since it shipped (packs, attempts, seconds) with no version at all, relying entirely on JsonUtility defaulting anything missing to zero/null/false — which has worked so far, but was luck rather than a mechanism: the first field that ever needs a real conversion (a rename, a units change, a value that has to be recomputed) would have had nowhere to put that logic. Migrate is a no-op today, honestly, because nothing added this round needs converting either — the point was building the seam before it was forced, not solving a problem that exists yet. Verified on the developer's own real save (schemaVersion absent, i.e. 0): loading it in Play mode rewrote it to `"schemaVersion":1` with every existing field — 100 Classic levels, 16 Advanced levels, per-level attempts/seconds — untouched.

**Per-mechanic skill: a completion-ratio proxy, not the Glicko2 idea §6.32 floated.** `SaveData.mechanicSkills` is a flat array of `{mechanic, attempts, completions}`, one entry per mechanic key ever seen (`LevelMechanics.BasicFlowKey` for Classic's pure routing, `"Bridge"`/`"Checkpoint"`/etc. for Advanced). `MechanicSkillRating`/`OverallSkillRating` report `100 * completions / attempts`. This is deliberately the simple thing rather than the ambitious one: a Glicko2-style rating needs a difficulty number to play the puzzle against, and `DifficultyAnalyzer.Score` is explicitly not that yet (all 50 pre-pack levels landed in a 43-56 band regardless of how they played — see Open Questions). Completion ratio needs nothing further built first, and is the same honesty-over-ambition call Phase 5 made about its own untuned weights.

**One source of truth for "what mechanics does this level have", extracted rather than duplicated.** The detection logic already existed once, inline in `UIController.DescribeMechanics` (for the HUD's "Mechanic : Bridge" label). Recording skill needed the same nine-way classification a second time, and writing it again inline would have been the exact bug class §6.5's rule 12 already named once (`GamePlayController.IsBoardFullyCovered` and the solver's internal coverage check drifting apart) — this time between what the player reads on screen and what stats actually get credited. Pulled out into `LevelMechanics.Identify(LevelData) -> MechanicFlags` (new file), with `UIController` and `GamePlayController` both calling it. `UIController.DescribeMechanics` keeps its own display strings ("Blocked cell", "One-way", ...) — those are presentation, not data, and the doc's "one source of truth" rule is about the underlying classification, not its two different renderings (a human label vs. a save-data key).

**Attempt and completion are credited at the two moments that already exist for the per-level counters, not new hooks.** `GamePlayController.SetSolution(LevelData)` — already called exactly once per attempt, by `BoardGenerator.GenerateBoard`, right after `ResetGameplay`/`BeginAttempt` — now also stores the level's `MechanicFlags` and credits an attempt to each key `LevelMechanics.Keys` returns. `SaveLevelData` (already called once per completion) credits a completion to the same keys, read back off the field `SetSolution` stored — never recomputed, since a level's mechanics cannot change mid-attempt. A board combining several mechanics counts as an attempt/completion of EACH — the same "each instance is its own question" treatment `RequiredMechanicValidator` already gives necessity checks, not an arbitrary choice.

**A real ordering bug found while wiring this in, not introduced by it.** `UIController.LoadLevel` called `GamePlayController.Instance.ResetGameplay()` — which calls `BeginAttempt`, reading `UIController.Instance.CurrentLevel` — BEFORE `currentLevel = levelNumber` ran. Retrying a level or advancing via "next" both already point `currentLevel` at the right value before calling `LoadLevel`, so those paths were fine; jumping to an arbitrary level from the level-select grid was not; the attempt landed against whatever level had been playing before. This has shipped since the attempts counter was added — silent, because nothing before Phase 9 read `currentLevel` from inside that specific window in a way anyone checked. Fixed by moving the assignment above `ResetGameplay()`.

**Hint usage: a fourth per-level array, same shape as attempts/seconds.** `PackProgress.hints` plus `SaveData.HintsForKey`/`SetHintsForKey`, incremented once per successful `TryApplyHint()` call — counted the moment the hint commits to a pair, not when its coroutine finishes drawing, so tearing the board down mid-trace still counts (the player still spent the move). The legacy campaigns' key-based accessors return null/no-op rather than gaining the column: they never had a stored answer for the hint button to use in the first place, so there is nothing to record there and never will be.

**Verified two ways.** 20 new tests (`LevelMechanicsTests`, `SaveDataSkillTests`) — pure-logic, no scene needed, since `SaveData` and `LevelMechanics` are plain C# with no Unity dependency — 201/201 total passing. In Play mode, against the developer's own real save: loaded Advanced Level 1 (which carries a Blocked cell) and confirmed `mechanicSkills` gained `{"Blocked","attempts":1}`; called `TryApplyHint` and confirmed `Advanced6x6`'s `hints` array set index 0 to 1; invoked the private `SaveLevelData` directly (reflection, to test completion without solving the board) and confirmed `Blocked` moved to `completions:1`. The real save was backed up before this and restored after — none of the developer's actual progress was left altered.

**A gap found immediately after, by re-reading the doc's own prior findings, not by play:** the first pass pooled every mechanic-free level under one flat `"BasicFlow"` bucket — every Classic pack, 5x5 through 9x9, sharing one skill number. That directly contradicts §6.25/§6.31: Classic carries no mechanic at all, and board size/shape (not mechanic count) is its actual difficulty lever, confirmed across two rounds of playtest correction. Rating a 5x5 win and a 9x9 win as the same skill was exactly backwards. Fixed with `LevelMechanics.SkillKeys(flags, mode, packSize)`: a mechanic-free Classic board now keys off `"BasicFlow" + size + "x" + size` (`"BasicFlow7x7"`, etc.), while Advanced -- which varies mechanics, not board size, and ships only one pack size so far -- keeps the flat key `Keys` already returned. A real mechanic always wins regardless of mode, since necessity, not board size, is what makes those levels hard. 5 more tests (206/206 total), and live-verified the same way as above: switching `SetPack` from 7 to 5 mid-session produced two separate `mechanicSkills` entries (`BasicFlow7x7`, `BasicFlow5x5`) rather than one shared bucket.

**Explicitly not done, and why:** nothing consumes `OverallSkillRating`/`MechanicSkillRating` yet — no UI shows it, no selection logic reads it. That is what Phases 10 (daily challenge's skill-based pool selection) and 11 (a statistics screen) actually need it for, and building a consumer before those phases exist would be speculative UI work with no real caller. This phase built the recording; using what it records is later work, named rather than skipped silently.

### 6.43 Phase 10 started: daily challenge

The spec's ask has three parts — date+version seeding, skill-based pool selection, on-device bounded generation-or-cache. Two scope calls made up front, both because of constraints this project already established rather than convenience:

**"Generation" is a pick, not a search.** Every earlier phase (§4.3, §5.1, and the whole of §6) established that generation is an offline Editor pipeline expensive enough to need its own per-configuration tuning pass — nothing a phone can do in a frame. `DailyChallengeSelector.Select` does not generate anything: it picks an index into a pack that already shipped, which is arithmetic. The "cache" half is what actually does the work the spec asks for — once picked for a day, `SaveData.dailyChallengeCachedDay`/`Mode`/`PackSize`/`Level` keep showing the same level even if the player's skill rating moves from playing other levels in between.

**Pool selection did not wait on a real cross-pack difficulty score.** `DifficultyAnalyzer.Score` is explicitly not one yet (all 50 pre-pack levels land in a 43-56 band regardless of how they played — see Open Questions) — but a level's own NUMBER inside a pack already is a meaningful difficulty ordinal, since every pack "ramps from the easiest board that size can produce to the hardest" by construction (§7). So `Select` needs no cross-pack score: skill (Phase 9's `OverallSkillRating`, 0-100) picks a third of the day's pack — under 30 the bottom third, 30-70 the middle, 70+ the top — and a deterministic hash of (day, seed version, pack size) picks the exact level inside that third, so the pick still varies day to day without leaving the player's comfort band. The pack itself rotates through every size the mode has, one per day (`dayIndex % packSizesForMode.Length`), so a week of daily challenges samples every board size.

**Classic only, for now.** Daily challenge always draws from Classic — the default mode, and the one whose packs vary by board size, which is the axis `Select` rotates through. Advanced ships one pack size so far, which would make the rotation a no-op; extending to Advanced is a config change (pass its pack sizes in) once it has more than one, not a design change.

**The hash is a hand-rolled mix, not `System.Random(seed)`, on purpose.** .NET does not guarantee `System.Random`'s algorithm stays fixed across runtime versions, and this only ever needs to be deterministic for the same (day, version, pack size) on THIS device — nothing here is compared against another player's pick, since the game has no backend. A small Murmur3-style finalizer over the three inputs is enough, and is trivially testable without touching `System.Random`'s internals.

**Completion is credited to the day the challenge was PICKED for, not "now".** `SaveData.RecordDailyChallengeCompletion(dayIndex)` takes `dailyChallengeCachedDay`, not a freshly-read date — a session that happens to run past midnight still credits the day it was opened on. It is also idempotent per day, so retrying an already-completed daily challenge cannot inflate the streak or the lifetime count; a gap of more than one day resets the streak to 1 rather than to 0, matching "you broke the streak, but you did complete one today."

**A second ordering fix, the same shape as Phase 9's.** `GamePlayController.CheckForLevelComplete` used to show the game-over screen BEFORE calling `SaveLevelData`, which is harmless for the existing move-count message but would have shown the STREAK COUNT FROM BEFORE this completion, since `ActivateLevelCompleteScreen` reads `SaveData` fresh off disk. Reordered so persistence happens first. Same lesson as `UIController.LoadLevel`'s `currentLevel` bug in §6.42: a value read from storage has to be read AFTER whatever updates that storage, not before, however natural the original order looks.

**A retry-preserves-the-flag fix, found by reasoning about the flag's own lifetime before shipping it.** `isDailyChallenge` is reset to false at the top of every `LoadLevel` call, since a direct call (picking a level from the pack grid) is never the daily challenge. But `OnGameOverScreenRetryButtonClick`/`OnPauseScreenRetryButtonClick` also call `LoadLevel` internally (to reload the same level) — unpatched, retrying a daily challenge after a mistake would have silently turned it into an ordinary pack level for streak purposes, the exact "found by reasoning, not by play" shape §6.23's checkpoint defect had. Both retry handlers now capture `isDailyChallenge` before the reload and restore it after. Live-verified: invoked the button, confirmed the flag survives across the reload's DOTween-deferred callback, not just synchronously.

**The one piece of scene work this phase needed.** Nothing existed to actually trigger a daily challenge, so a `Button_dailyChallenge` was added to the main menu (reusing `Button_play`'s sprite for visual consistency, wired via `UnityEventTools.AddVoidPersistentListener` rather than hand-editing the scene YAML) calling a new `UIController.OnDailyChallengeButtonClick` → `LoadDailyChallenge`. It jumps straight to the gameplay screen, skipping the pack grid entirely — there is exactly one level to play today.

**Verified two ways.** 15 new tests (`DailyChallengeSelectorTests`, `DailyChallengeStreakTests`) — pure logic, no scene needed — 221/221 total passing, including a 200-day sweep confirming the top skill band actually reaches a pack's final level (not just "somewhere in the top third"). In Play mode, against the developer's own real save (backed up, restored after): clicked the new button and confirmed via direct state inspection (a Game View screenshot came back blank again — the same unfocused-window artifact §6 Phase 4 already hit and documented, not a new problem) that it loaded Classic 6x6 level 20, cached that pick to `SaveData`, hid the main menu and pack grid, and showed gameplay; invoked completion and confirmed the streak went 0→1 and the game-over message read "Daily Challenge complete! 1-day streak."; retried and confirmed the flag survived the reload.

**Explicitly not done, and why:** no UI shows the streak anywhere except the completion message — a persistent "N-day streak" readout (on the daily-challenge button itself, say) is presentation work with no functional gap behind it, closer to Phase 11's statistics screen than to this phase's job of making the mechanism correct. Advanced mode's daily challenge is not wired (see above). No push notification or any "come back today" prompt exists — out of scope for an offline single-player game with no notification infrastructure elsewhere in the project.

### 6.44 Advanced 7x7: why the wired call fails, the fix, and where this stands — READ THIS FIRST if picking up the 7x7 pack

**Session handoff note.** This work was interrupted mid-investigation to continue on a different machine. Everything needed to resume is in this section. The short version: `BuildAdvancedPack(7, 100, 10, 900, 10)` — the call §6.40 called "wired, never run" — **does not work as configured**, a real generator bug was found and fixed, and a good `cellsPerColour` was measured rather than guessed. **The fix is committed. The real 900/100 pack has NOT been generated yet** — that is the next action, spelled out at the end of this section.

#### The original call fails, and not slowly

Ran `BuildAdvancedPack(7, 24, 4, 260, 10)` (the existing small-probe pattern, `ProbeAdvancedYield()`'s 7x7 sibling, added as `ProbeAdvancedYield7x7()`). It took **24,284.7 seconds (6.75 hours)** to gather only **46 of a 260 pool target** — it hit the pipeline's hard 200,000-attempt cap, not the pool target, at a raw yield of 0.023%. The full call (`poolTarget=900`) hits the identical attempt cap at roughly the identical cost, then fails outright (`scored.Count < count`) since 46 « 100. This is a wrong configuration, not a patience problem — the doc's own §6.38 already made this exact mistake (extrapolating a colour ratio from a different pack rather than measuring) for Classic's 8×8, twice.

#### Root cause: the growth heuristic actively fights StructuralGates

Built `ProbeColourRatioSweep(size, cellsPerColourValues[], attemptsPerValue)` — a cheap, bounded sampler (no 200,000-attempt cap, no disk writes) that reports exactly where attempts die: generated/duplicate/unsound (broken down by `StructuralGates`' own failure reason)/decorative/kept, plus ms-per-kept. Menu item `FreeFlow/Level Generator/Advanced/PROBE 7x7 colour ratio sweep`. At `cellsPerColour ∈ {6,7,8,9,10,12}`, 3,000 attempts each (~15-20 min total, run headless via `unity run . -- -executeMethod FreeFlow.GamePlay.LevelGenerator.ProbeColourRatioSweep7x7` since the Unity Editor GUI was closed for this session — see "Running probes without the Editor open" below):

**"Spread too uniform" (`StructuralGates.LengthSpread < 0.75`) was the dominant rejection reason at EVERY colour ratio tried**, not just the bad ones. Traced to the actual cause in `BuildPathPartitionCore`'s (formerly `TryGeneratePathPartition`'s) growth loop:
```csharp
bool better = freeNeighbours < bestFreeNeighbours
    || (freeNeighbours == bestFreeNeighbours && path.Count < bestPathLength);
```
Warnsdorff's rule (`freeNeighbours`) is correct and untouched. The second clause is not: whenever two candidate moves are equally constrained, it **actively prefers extending whichever path is currently shortest** — a deliberate length-equalising bias baked into the one growth loop every generator call site shares (Classic's packs, the proven Advanced 6x6 pack, and Advanced's mechanic-dependent construction alike). `DistributeLengths` (the old snake-then-cut length-variation step) was fully deleted with the Hamiltonian-snake constructor it belonged to (§6.15) and never replaced — nothing in the current pipeline tries to vary path lengths; the growth heuristic actively resists it.

#### The fix, and why it needed a second pass

Added `shortPathProtectionFloor` to the shared partition builder: the tie-break now compares `min(path.Count, floor)` instead of raw `path.Count`. Two paths already at or past the floor both clamp to exactly the floor and read as equal, so ties between two SAFE paths fall through to pure randomness (no bias); a path still short of the floor keeps winning ties (protecting whichever is most at risk first). `int.MaxValue` recovers the original always-prefer-shortest behaviour exactly (every `TryGeneratePathPartition` overload passes this — **zero behaviour change for Classic or the shipped Advanced 6x6 pack**). A new, distinctly-named `TryGeneratePathPartitionUnbalanced` (default floor `StructuralGates.MinPathCells + 2` = 5) is the only thing that passes anything else, and only `TryBuildMechanicDependentBoard` (Advanced's mechanic construction) calls it.

**First attempt used floor 0 (no protection at all) and made things worse in a different way**: "spread" dropped as predicted, but "too short" (`<3`-cell link) rose sharply at low colour counts, since nothing stopped one path starving while others ran long. Comparing all three configurations, same 3,000-attempt sample each:

| cellsPerColour | Balanced (orig) kept / ms-per-kept | Unbalanced (floor 0) kept / ms-per-kept | **Hybrid (floor 5) kept / ms-per-kept** |
|---|---|---|---|
| 6 | 3 / 9,454 | 0 / n/a | **4 / 7,876** |
| 7 | 1 / 65,959 | 2 / 24,296 | **5 / 13,220** |
| 8 | 2 / 82,897 | 0 / n/a | 2 / 48,986 |
| 9 | 3 / 107,279 | 3 / 86,353 | 1 / 346,265 |
| 10 | 2 / 209,815 | 3 / 122,260 | **4 / 94,375** |
| 12 | 0 / n/a | 1 / 363,174 | 0 / n/a |
| **total kept, all 6 values** | **11** | 9 | **16** |

The hybrid floor is the one actually shipped (it's the default). It isn't just "faster somewhere" — it has the highest total yield of the three, and by a real margin at cellsPerColour=7 (1→5 kept).

**Reason found by reasoning about the numbers, not just picking the biggest table cell**: `cellsPerColour=6` is cheapest (ms/kept=7,876 → ~2h to a 900-pool) but its mean path (5.6) is shorter than Classic's own measured 7x7 reference (8.7, §6.38) — a materially easier board underneath whatever mechanic sits on it. **`cellsPerColour=7` is the better pick**: best raw yield (5/3000, also the most statistically reliable sample of the three fast options), ms/kept=13,220 → **≈3.3 hours extrapolated to a 900-board pool**, and mean path 7.6, close to the Classic reference. `cellsPerColour=10` (the original guess) is now viable too at ≈23.6 hours, but no longer the best option once actually measured.

All sample sizes here are small (1-5 kept per 3,000 attempts) — real sampling noise, not a precise measurement. Treat the ≈3.3h estimate as a planning number, not a guarantee.

#### A real regression caught before it shipped

The first attempt at this fix added `shortPathProtectionFloor`/`balanceLengths` as an optional parameter directly on the existing `TryGeneratePathPartition` overloads. **This broke 10 of 227 tests** (`LevelGeneratorBridgeTests`, `LevelGeneratorSharedGoalTests`) — both resolve those overloads via reflection matched on an *exact* parameter list/count (`GetMethod(..., new[]{ typeof(int), typeof(bool[,]), ... }, null)` and `m.GetParameters().Length == 7`), and an appended parameter either breaks that lookup outright (`NullReferenceException` on a null `MethodInfo`) or, worse, silently resolves to the *wrong* overload. Fixed by keeping all three `TryGeneratePathPartition` overloads at their exact original signatures (delegating to a new, differently-named `BuildPathPartitionCore`), and giving the new behaviour its own method name (`TryGeneratePathPartitionUnbalanced`) instead of a bolted-on parameter. **227/227 tests pass** with the final version. Lesson for next time this file's shared methods change shape: grep the test folder for `GetMethod`/`GetParameters` against the method's name before assuming an added optional parameter is harmless.

#### Running probes without the Editor open

The Unity Editor GUI got closed partway through this session (crashed or closed independently — not something this session did), which also disconnects UnityMCP. Rather than reopen the GUI (which blocks on every generation call — see §0's own runbook note), the rest of this investigation ran via the **Unity CLI** (`unity` on PATH, installed, beta channel) in headless batch mode, which needs no live Editor and doesn't fight a GUI instance for the project lock:
```bash
# Compile-check + full test suite (fast, ~1s once warm):
unity test . --mode EditMode --editor-version 6000.3.8f1 --output <path>.xml --format json

# Run a generator menu-item method headlessly (no GUI, no lock conflict):
unity run . --editor-version 6000.3.8f1 --timeout 5400 --format json -- \
  -executeMethod FreeFlow.GamePlay.LevelGenerator.ProbeColourRatioSweep7x7 \
  -logFile <path-to-a-log-file>

# The actual next-action command -- the real 100-level pack, cellsPerColour=7 already wired in.
# No --timeout (or a very large one): this is expected to run for hours, and that is fine.
unity run . --editor-version 6000.3.8f1 --format json -- \
  -executeMethod FreeFlow.GamePlay.LevelGenerator.BuildAdvancedPack7x7 \
  -logFile <path-to-a-log-file>
```
`unity run`'s own stdout is mostly noise; the actual `Debug.Log`/`Debug.LogError` output — including a probe's final summary line — lands in whatever `-logFile` was given, not in the CLI's own JSON envelope. Grep that log file for the method's own log output. **Do not run `unity run`/`unity test` at the same time the Editor GUI has this project open** — same project-lock conflict as two GUI instances; check `tasklist` for `Unity.exe` and `Temp/UnityLockfile` first if unsure.

#### The exact next action

1. Run the real pack build: `FreeFlow/Level Generator/Advanced/Build 7x7 pack (100)` (the menu item is already updated to `BuildAdvancedPack(7, 100, 10, 900, 7)` — `cellsPerColour=7`, everything else unchanged from the original wired call). Expect on the order of **3-4 hours**, possibly more (small-sample estimate, see above) — the user has explicitly said timing is not a constraint, correctness is what matters, so let it run to completion rather than capping it.
2. This can run headless via the CLI exactly as above, with a longer `--timeout` (5400s was enough for the 3,000-attempt sweeps; the real run needs its own budget — 6+ hours, so pass e.g. `--timeout 43200` or omit `--timeout` and just wait).
3. Verify the output the same way every prior pack was verified (§0's runbook): load each `Level_N.asset`, confirm `ValidateSolvability` reports a unique solution, confirm every mechanic instance is load-bearing (`RequiredMechanicValidator`), confirm no path ≤ 2 cells and no blocked cell on the outer ring. Do NOT trust the generation log alone.
4. Only after that verification, treat the pack as shippable, `UIController`'s `advancedPackSizes`/relevant fields get 7 added, and this section's status moves from "next action" to "done" in a follow-up doc update.
5. **Not yet decided**: whether `cellsPerColour=7`'s shorter-than-original mean path needs a second look once real levels exist to play, the same way §6.25's mechanics-are-seasoning finding only came from actually playing generated boards, not from any generation-time metric.

### Open questions, current

- **Difficulty is SOLVED for 7×7 and confirmed in play (§6.35).** The note below was written when it was not, and its two "blocked" axes have both moved: board size is no longer the constraint (8×8 is cheaper to generate than 7×7, §6.31), and mechanic density is an Advanced-mode question rather than a difficulty one. What remains genuinely open is carried forward below.
- ~~The 5×5 and 6×6 blocks have not had the §6.35 treatment.~~ **Done — the whole campaign now ramps (§6.36).**
- **`DifficultyModel.Measure` caps at `maxAssumptions = 14`, and that cap is hard against the ceiling.** A board needing 15 reports unsolved, fails `WellFormed`, and is discarded — so selection actively rejects the hardest boards the generator makes. 9×9's level 100 measures exactly 14. On the hardest pack we own, the cap is throwing away the best material. Raising it costs only solve time.
- **9×9 was never probed below 9 colours**, and the range was chosen by analogy rather than measured — the same error that put the first 8×8 pack at 10–12 colours until play caught it. 7 and 8 colours would give 11.6 and 10.1 cells each, longer paths than anything shipped. Probing is cheap; building might be days.
- **Advanced generates for 6×6 and 7×7 only (§6.40).** 5×5, 8×8 and 9×9 have no entry point, the instance ceiling is hard-coded at 6 and will silently discard boards a 9×9 legitimately needs, and each size's colour ratio has to be measured rather than guessed. 5×5 may not have the room at all.
- **Classic packs ARE reachable now; Advanced 6×6 is the one on screen.** `LevelResourcePath` takes a pack dimension and progress is keyed per pack. What is still missing is a pack-select and mode-select UI: `SetPack` and `SetMode` exist and nothing calls them, so switching packs means editing `currentPackSize` / `startingMode` in the Inspector. `startingMode` is committed as Advanced, which is right for testing and wrong for release.
- ~~The old linear Classic 1–100 and Advanced 1–45 still exist, keep their own progress under the legacy keys, and are reachable by setting the pack size to 0.~~ **Retired — the level assets are deleted.** One of the three design decisions below (whether they survive alongside the packs) is answered: they do not. This opened a real gap rather than closing one cleanly: `UIController.LevelResourcePath` still returns `Levels/{Mode}/Level_{n}` for `currentPackSize == 0`, and that path now points at nothing — `Resources.Load` returns null, `LoadLevel` logs the "No level asset" error and refuses to load rather than crashing, but pack size 0 is now a dead setting, not a working fallback. `classicLevelCount`/`advancedLevelCount` (100/45) are dead fields for the same reason. None of this is reachable from the shipped UI today (nothing sets `currentPackSize` to 0), so it costs nothing in practice, but the dead code and dead fields are worth removing rather than leaving as a trap for whoever next touches `LevelResourcePath`.
- **Per-level move counts were dropped as a feature in the same pass**, not merely orphaned by the level deletion: `completedlevelMoves`/`advancedCompletedLevelMoves` and `PackProgress.moves` are removed from `SaveData`, and the level-select button no longer has a moves badge (the prefab's `MoveObject` child was deleted). `completedLevel`/`advancedCompletedLevel` (the legacy keys `CompletedLevelForKey("Classic")`/`"Advanced"` resolve to) and the per-pack attempts/seconds telemetry are untouched — the legacy fields are now dead weight rather than live-but-pointless, for the same reason `LevelResourcePath`'s pack-0 branch is: no UI sets `currentPackSize` to 0, so nothing reads them, but they still round-trip through every save load/save. A real, pre-existing bug surfaced while removing the moves badge: `LevelScreenController.RefreshVisibleButtons` read `data.completedlevelMoves` directly instead of going through a pack-aware accessor, which threw `NullReferenceException` on the Advanced6x6 pack (whose legacy array was null) and **aborted the entire button-population loop**, leaving the level-select screen showing nothing but the stage prefab's static placeholder button. Reproduced by isolating the change against the prior commit and confirming the exact exception and stack trace; fixed by removing the read (and the feature) rather than correcting it to the right key, since the field it would have pointed to no longer exists either.
- **Two of the three design decisions below are still open**, now that the first is settled: how mode and pack compose, given Advanced's mechanic levels are not organised by board size (Advanced currently ships only a 6×6 pack, so this hasn't bitten yet); and whether packs unlock or are all open from the start.
- **Sizing a generation run: measure every stage, do not extrapolate from one.** Three estimates, two badly wrong, and the same cause each time — the stage not front of mind was left out. Gathering N *canonically distinct* boards is often the dominant cost and gets slower as the pool fills; the scoring pass is dominated by relaxation. The estimate that landed was measured end to end first.
- **Levels 51–200 (Mastery), for the record as it stood before the above.** Measured, not estimated.
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
- ~~Phases 8–13 are untouched.~~ **Phase 8 (hints) is built (§6.41). Phase 9 (save-data versioning, per-mechanic skill, hint telemetry) is started (§6.42) — the recording exists, nothing consumes it yet, except Phase 10 which now does. Phase 10 (daily challenge) is started (§6.43): one level a day, deterministic, skill-banded, streak-tracked, reachable from a real button.** Phases 11–13 remain: campaign/world UI, mobile polish, QA gate. The pack-select and mode-select UI (`SetPack`/`SetMode` exist and nothing calls them) is still the one blocking the PACKS (as opposed to the daily challenge, which now bypasses the pack grid entirely) from being reachable in play at all — that is the natural next one.
- ~~The legacy Advanced 1–45 levels carry no stored answer, so the hint button is off there — worth deciding whether to backfill with `FillStoredSolution` or retire them.~~ **Decided: retired**, not backfilled — see the retirement note earlier in this list.
- **Art/UI scope** remains a separate parallel content task; nothing in this plan covers world themes, statistics screens, or daily-challenge screens.
