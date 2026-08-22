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

        // Entry is only allowed while moving in the cell's requiredEntryDirection. Constrains
        // entry only: a path may still leave a OneWay cell in any direction, including back
        // out the way it came. For "in one side, out the other" see Arrow.
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
        Mixed = 6,

        // However a path enters, it must leave in the cell's forcedExitDirection. Unlike every
        // type above, this constrains the relationship between the incoming AND outgoing
        // direction, which needs a predicate the other two cannot express: Block.CanExit, given
        // the direction the path is about to take. Entering against the arrow is refused
        // outright, since the forced exit would send the path straight back into the cell it
        // just came from.
        //
        // Turn-only / straight-only cells remain deliberately unmodeled. They want the same
        // two-direction check but do not name the exit, so they are ambiguous to read on a board
        // and awkward to teach; the arrow covers the same design need legibly.
        Arrow = 7,

        // A crossing: two pairs may occupy the cell at once, like Mixed, but on strict terms --
        // one horizontally, one vertically, and neither may turn on it. The axis is not authored;
        // it is whichever way each occupant happens to cross. Mixed is the permissive sibling
        // (share freely), this is the strict one (share on terms), and level design picks.
        //
        // Needs nothing new: Mixed already broke the one-cell-one-path invariant and brought
        // per-pair occupancy, and Arrow already brought the exit predicate. A bridge is those two
        // plus "one lane per axis". See Block.CanAcceptEntry and Block.CanExitFrom.
        Bridge = 8,

        // The junction of a splitter pair: a pair with THREE dots instead of two, complete only
        // when all three reach this cell. Nothing on the cell enforces that -- the rule lives in
        // how completion is measured, which is why this was the deepest of the mechanics to add:
        // a pair holds a set of drawn segments and is solved when all its dots sit in one
        // connected component of them. See GamePlayController.IsPairSatisfied.
        //
        // The cell itself is permissive, like Mixed: it has to hold three segments of one pair,
        // and refusing anything here would only get in the way.
        Splitter = 9,

        // An elbow joining exactly two of the cell's four edges, which the player rotates by
        // tapping. The only mechanic where the player changes the BOARD rather than the path, and
        // the only one with state that is neither level data nor path data: the initial rotation
        // is authored, the current rotation is runtime and resets with the level.
        //
        // Always an elbow, never a straight -- the four rotations are Up+Right, Right+Down,
        // Down+Left, Left+Up -- so a rotator always turns a path 90 degrees. A level built around
        // one has to want the turn.
        Rotator = 10
    }
}
