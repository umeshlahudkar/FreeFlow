# Feature Roadmap — New Puzzle Mechanics

Candidate mechanics for making levels more challenging, grouped by category, plus a
feasibility read against the current codebase. See also
[`KNOWN_ISSUES.md`](KNOWN_ISSUES.md) (drag/select algorithm bugs) and
[`SCALING_NOTES.md`](SCALING_NOTES.md) (why several of these need a prerequisite
refactor before they can be added safely).

---

## 1. Board obstacles

| Mechanic | Description |
|---|---|
| Wall between cells | Player cannot draw across that specific edge |
| Blocked / non-walkable cell | Cell is completely unusable |
| One-way passage | Flow may only enter from one direction |
| Gate | Initially blocked; opens once another color/pair meets a condition |
| Teleport cell | Entering one cell moves the flow to a linked cell |
| Ice cell | Flow keeps moving in the same direction until it hits an obstacle |
| Bridge cell | Allows one path to cross over another |
| Portal pair | Two non-adjacent cells act as if adjacent |

## 2. Multi-color / shared cells

| Version | Description |
|---|---|
| Fixed mixed cell | A single cell that both Red and Blue must pass through |
| Split cell | One path enters, a different path exits from the same cell |
| Color switch | Special cell that changes the "current color" of the flow passing through |
| Two-layer cell | Two colors occupy the same cell on separate layers (cleanest for readability) |

## 3. Crossing mechanics

- Normal crossing — two flows may cross freely
- No crossing — standard Flow Free behavior (current default)
- Bridge crossing — one color may cross another only at a bridge cell
- Tunnel — a path passes underneath another
- Intersection — multiple colors share a junction cell
- Rotating intersection — player chooses which directions connect at a junction

## 4. Special cells

| Cell | Function |
|---|---|
| Locked | Cannot be entered initially |
| Key | Unlocks a specific locked cell |
| Portal | Teleports flow to a linked cell |
| Ice | Forces straight-line movement until blocked |
| One-way | Only one valid entry direction |
| Rotator | Changes the path's direction |
| Mixer | Combines two colors |
| Splitter | Splits one flow into two |
| Bridge | Allows crossing another path |
| Breakable | Becomes blocked/available after being used once |
| Timer | Must be reached before a turn/move limit expires |
| Number | Must be visited in a specific order |

## 5. Path restrictions

- **Exact length** — a color's path must use exactly N cells
- **Required checkpoint** — path must pass through a specific cell
- **Forbidden cell (per color)** — a cell is off-limits to one color but may be usable by another

## 6. Direction mechanics

- **Arrow cell** — entering forces the flow to exit in the indicated direction
- **Turn-only cell** — flow cannot continue straight through
- **Straight-only cell** — flow cannot turn

## 7. Dynamic obstacles

Board state changes mid-solve, creating solve-order dependencies (e.g. "solve Blue
first so it opens the gate Red needs"):

- Disappearing / moving walls
- Cells that become blocked or become free
- Switches, gates, pressure plates
- Rotating barriers

---

## Feasibility against the current codebase

Checked against `Block.cs`, `BoardGenerator.cs`, `GamePlayController.cs`, and the
level-data schema (`LevelDataSO` / `GridRow`, one `PairColorType` per cell).

**Current constraints that matter here:**
- `Block` is a flat set of fields (`isPairBlock`, `pairColorType`, `highlightedColorType`)
  — no cell-type concept, no per-edge data.
- The grid is `Block[,]` — exactly one `Block` occupies one cell. There is no notion of
  an edge *between* two cells, and no notion of two things sharing one cell.
- Level data stores a single `PairColorType` per cell — nothing else.
- Pair identity is the display color itself (`KNOWN_ISSUES.md` #2 / `SCALING_NOTES.md`
  #1), so anything needing a durable per-pair ID (gates reacting to "did Blue finish",
  checkpoints, mixed cells) is blocked on that fix regardless of order chosen below.

| Tier | Mechanics | Why this tier |
|---|---|---|
| **Prerequisite (shared by almost everything below)** | Pair-ID-not-color fix; `BlockType` enum or `IBlockBehaviour` | Nothing else here can be added cleanly while color *is* pair identity and `Block` has no type concept. Already flagged as a must-fix in `SCALING_NOTES.md`. |
| **Cheap — data/validation only** | Blocked cell, required checkpoint, exact length, forbidden-cell-per-color | Checked at cell-selection or path-completion time; no change to the drag algorithm itself. |
| **Moderate — extends `GetDirection` / `CanSelectToAdd`** | Wall between cells, one-way passage, arrow / turn-only / straight-only cells | Wall specifically needs new **edge** data — not a cell property — since the schema currently has none. The others are a per-cell "which directions are legal" predicate. |
| **Needs a new subsystem** | Gate, key, breakable, timer, pressure plate, dynamic/disappearing obstacles | Requires tracking cross-pair/cross-cell state ("has pair X completed", "has this cell been used") that nothing in `GamePlayController` currently tracks. |
| **Biggest architectural lift** | Mixed/shared cells, splitter, color switch, crossing, bridge, portal, tunnel, rotating intersection | All break the core invariant "one `Block` = one cell = owned by at most one path," which `grid[,]`, `completedPairs`, and the cell-stealing logic in `KNOWN_ISSUES.md` are built around. This is a data-model change (multi-occupancy or layers), not a new branch. |

---

## Suggested build order

1. **Prerequisite refactor** — pair IDs independent of color, `BlockType` abstraction.
   Everything else depends on this.
2. **Cheap tier** — blocked cells, checkpoints, length/forbidden-cell restrictions.
   Validates the new schema without touching drag mechanics.
3. **Moderate tier** — walls, one-way, arrow/turn/straight-only cells.
4. **Subsystem tier** — gates, keys, breakables, timers, dynamic obstacles.
5. **Biggest-lift tier** — mixed cells, crossing, bridges, portals, splitters. Treat as
   a separate stretch decision once 1–4 are stable, since it changes the cell-occupancy
   model everywhere rather than adding to it.

Difficulty should come from **dependencies between mechanics**, not just density of
obstacles — e.g. "Blue must be solved first because it opens the gate Red needs."
