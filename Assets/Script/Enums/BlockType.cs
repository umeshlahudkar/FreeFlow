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

        // A crossing: two pairs may occupy the cell at once, but on strict terms -- one
        // horizontally, one vertically, and neither may turn on it. The axis is not authored; it
        // is whichever way each occupant happens to cross. This is the only cell that breaks the
        // otherwise-universal "one Block = one path" invariant, which is what per-pair occupancy
        // exists for; Arrow already brought the exit predicate. A bridge is those two plus "one
        // lane per axis". See Block.CanAcceptEntry and Block.CanExitFrom.
        Bridge = 8,

        // The inverse of ForbiddenForPair: instead of naming the one pair that may NOT enter, the
        // cell names the one or two that may, and refuses everyone else. PairId is the first
        // permitted pair and SecondPairId the optional second, reusing the column the shared
        // destination already added rather than inventing a list.
        //
        // Worth knowing when authoring: on a two-colour board this is indistinguishable from
        // ForbiddenForPair -- "only pair 2 may pass" IS "forbidden for pair 1". The two diverge
        // only from three colours up, where a denylist stops one pair and an allowlist stops
        // every other. Levels with two pairs should keep using the forbidden cell; it is the
        // simpler rule to read.
        //
        // A third permitted colour is deliberately not modeled. Two fits the existing columns and
        // the border can show two mitred halves (see PermissionBorderView); beyond that the cell
        // stops being readable at a glance and the honest form would be a bitmask naming every
        // colour's status, which no other mechanic needs yet.
        AllowedForPairs = 11
    }
}
