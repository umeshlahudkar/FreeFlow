namespace FreeFlow.Enums
{
    public enum BlockType
    {
        Normal = 0,
        Blocked = 1,

        // A plain cell that repurposes its PairId to mean "which pair this applies to"
        // rather than "which pair owns this dot" (it is never a pair dot itself):
        //   Checkpoint      - that pair's path must pass through this cell (checked at
        //                     level-completion time)
        //   ForbiddenForPair - that pair may not enter this cell at all (other pairs can)
        Checkpoint = 2,
        ForbiddenForPair = 3,

        // Entry is only allowed while moving in the cell's requiredEntryDirection.
        // Arrow / turn-only / straight-only cells are deliberately NOT modeled here: they
        // constrain the relationship between a cell's incoming AND outgoing direction
        // (a two-direction check spanning the move before and after), not just entry into
        // one cell -- a materially different shape that deserves its own design pass
        // rather than being forced into this enum. Tracked as a follow-up.
        OneWay = 4,

        // Repurposes PairId as "which pair must be solved to open this gate" (again, never
        // a pair dot itself). Blocks entry for ANY pair until that dependency pair is fully
        // complete, then opens for everyone -- re-evaluated live every selection check, so
        // it opens/re-locks immediately as the dependency pair's solved state changes.
        // Key/breakable/timer/pressure-plate/dynamic obstacles are deliberately NOT modeled
        // yet: they're variations on this same dependency-tracking idea, but each needs its
        // own trigger condition designed against real levels rather than guessed now.
        Gate = 5,

        // A plain junction that more than one pair may occupy at once -- breaks the
        // otherwise-universal "one Block = one path" invariant. Entry/completion logic
        // needs no special-casing (each pair already tracks its own List<Block> path, and
        // list membership doesn't conflict); only cell-stealing (must not steal a Mixed
        // cell from another pair) and per-cell highlight state (must not let one pair's
        // reset wipe the other pair's direction images) need to know about it. See
        // GamePlayController.ProcessBlockStep and Block.ResetAllHighlightDirection(int).
        Mixed = 6
    }
}
