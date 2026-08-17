# Known Issues — Drag/Select Algorithm (`GamePlayController`)

Findings from a manual code review of the drag-to-connect logic in
[`Assets/Script/GamePlay/GamePlayController.cs`](../Assets/Script/GamePlay/GamePlayController.cs),
[`Assets/Script/GamePlay/Block.cs`](../Assets/Script/GamePlay/Block.cs) and related files.
Ordered by practical impact.

---

## 1. Fast swipes can silently skip cells

**Where:** `OnPointerMoved`, `GetDirection` — `GamePlayController.cs:165-288`, `:460-482`

`GetDirection` only recognizes a move between two **exactly-adjacent** cells (row/col
delta of 1). There is no interpolation between the previously-processed block and the
current one. If the pointer moves faster than the raycast samples every intermediate
cell in a frame (fast flick, low frame rate, small cells on an 8×8 grid), `GetDirection`
returns `Direction.None` and that step is dropped silently — no highlight, no addition,
no backfill. The drawn line visibly fails to keep up with the finger.

**Suggested fix:** when a non-adjacent cell is detected, walk the straight-line path
between the last selected block and the new one and add the intermediate cells instead
of dropping the step.

---

## 2. Pair identity is keyed by color — hard cap of 9 simultaneous pairs

**Where:** `completedPairs` dictionary, `HighlightSelectedColorTypeBlock` —
`GamePlayController.cs:360-400`, `Enums/PairColorType.cs`

`completedPairs` is a `Dictionary<PairColorType, List<Block>>`, and "find the other end
of this pair" works by scanning the grid for another dot with the **same**
`PairColorType`. `PairColorType` only defines 9 non-`None` values. An 8×8 board can fit
far more than 9 pairs, but the algorithm has no independent per-pair ID — color *is*
identity. A level with two unrelated pairs sharing a color would connect/highlight the
wrong endpoints, and nothing validates that a level's colors are unique per pair.

**Suggested fix:** either cap level design to ≤9 pairs, or give each pair a unique ID
(e.g. an index) separate from its display color.

---

## 3. Tap-without-drag on a dot flashes the cell black (confirmed cosmetic bug)

**Where:** `OnPointerUp` → `AddSelectedBlocksToCompletedPairs` → `Block.HighlightBlockBg`
— `GamePlayController.cs:290-330`, `:490-502`; `Block.cs:107-114`

Tapping a dot and releasing immediately (no drag) still stores a 1-element list and
calls `HighlightBlockBg()` on it. That block's `highlightedColorType` was never set
(only `HighlightBlockDirection`, which never ran, sets it), so it's still
`PairColorType.None`. `GetColor(PairColorType.None)` falls through to the default
`Color.black`, so the tapped dot's background flashes black. It self-heals the next
time that same dot is tapped (`ResetAllHighlightDirection` clears it), but the glitch is
visible every time.

**Suggested fix:** skip `HighlightBlockBg()` when `selectedBlocks.Count == 1`, or set
`highlightedColorType` to the block's own `PairColorType` at selection time instead of
only during drag.

---

## 4. Dead "merge" branch in `AddSelectedBlocksToCompletedPairs`

**Where:** `GamePlayController.cs:565-582`

The `RemoveAt(0)` + `AddRange` branch (commented `// added two times`) is guarded by
`completedPairs.ContainsKey(selectedBlocks[0].HighlightedColorType)`. `OnPointerDown`
always removes that exact key before `OnPointerUp` runs, for every entry path (fresh
dot, completed-pair dot, or reconnect). So the key is never present here and the branch
is unreachable — every completion falls through to the `else` and does a full overwrite
instead. Harmless in outcome, but it's vestigial code from an earlier version of the
algorithm and should be removed rather than trusted.

---

## 5. Misleading unused parameter on `IsPairComplete` overload

**Where:** `GamePlayController.cs:617-620`

```csharp
private bool IsPairComplete(Block b1, Block b2, PairColorType type)
{
    return (!IsEqual(b1, b2) && b1.HighlightedColorType == b2.PairColorType);
}
```

`type` is never read. The signature implies the check validates against a specific
color, but it doesn't. A future change that assumes `type` is actually checked would
introduce a real bug.

**Suggested fix:** drop the parameter, or rename to make clear it's unused/for future
use.

---

## 6. No validation that level data pairs colors correctly

**Where:** `HighlightSelectedColorTypeBlock` — `GamePlayController.cs:360-400`

The "find the matching dot" scan assumes exactly two dots share a `PairColorType`. If a
level's data ever has a color appear only once (bad content — not currently possible via
existing tooling, but nothing enforces it), `highlightedBlock[1]` keeps its value from
the *previous* selection (or `null` on the first click of a session), leading to either a
wrong block being highlighted or a `NullReferenceException`.

**Suggested fix:** validate level data on load (each non-`None` `PairColorType` appears
exactly twice), or guard `highlightedBlock[1]` before use.

---

## 7. Needs confirmation in the Unity Inspector (not verifiable from code)

**Where:** `touchPointer` — `GamePlayController.cs:36`, `:652-655`

The `touchPointer` `Image` is activated and moved to the exact pointer position every
frame during a drag. If its `Raycast Target` is enabled, it would sit directly over the
grid on the next frame and the raycast would hit the pointer graphic instead of the
`Block` underneath, stalling the drag entirely. The game clearly works today (per the
README screenshots), so this is presumably already disabled on the prefab — but it's
exactly the kind of setting that silently breaks the interaction if the prefab is edited
later, and it can't be confirmed from script alone.

**Suggested fix:** confirm `Raycast Target` is unchecked on the `touchPointer` Image, or
disable it explicitly in code (`touchPointer.raycastTarget = false`) so it can't
regress.

---

## Verified correct (no action needed)

- The "steal another path's cell" logic (`GamePlayController.cs:183-195`) and the
  reconnect-from-last-vs-middle logic (`:119-150`, `ResetBlockToRemove` at `:528-549`)
  hold up under all traced edge cases (empty lists, index -1, stealing an endpoint dot).
  They rely on the invariant "index 0 of every stored path is always a dot," which is
  never actually violated given how paths are only ever created starting from a dot
  click.
