using FreeFlow.Enums;

namespace FreeFlow.GamePlay
{
    /// <summary>
    /// Which of the nine mechanics a level's cells actually use, read straight off its
    /// <see cref="LevelData"/> rather than authored per level. This is the single source of
    /// truth for "what does this board contain" -- both the gameplay HUD label
    /// (<c>UIController.DescribeMechanics</c>) and the mechanic-teaching gate
    /// (<c>SettingsData.metMechanics</c>/<c>HasMetMechanic</c>) read it, rather than each
    /// re-deriving the same nine booleans and risking the two disagreeing -- the same class of
    /// bug Forbidden's missing pairId caused once already (GAME_EXPANSION_PLAN §6.5).
    /// </summary>
    [System.Flags]
    public enum MechanicFlags
    {
        None = 0,
        Blocked = 1 << 0,
        Wall = 1 << 1,
        OneWay = 1 << 2,
        Arrow = 1 << 3,
        Forbidden = 1 << 4,
        Permitted = 1 << 5,
        Bridge = 1 << 6,
        Checkpoint = 1 << 7,
        SharedDestination = 1 << 8,
    }

    public static class LevelMechanics
    {
        /// <summary>Stable key for a mechanic-free board with no board-size context to fold in --
        /// an Advanced board that somehow ships without a mechanic, or Classic accessed through
        /// <see cref="Keys"/> directly rather than <see cref="SkillKeys"/>. Classic's actual skill
        /// tracking uses this as a PREFIX (see SkillKeys), not the bare key. Never a display
        /// string; see UIController.DescribeMechanics for the human-readable label.</summary>
        public const string BasicFlowKey = "BasicFlow";

        public static MechanicFlags Identify(LevelData data)
        {
            MechanicFlags flags = MechanicFlags.None;
            if (data.gridRows == null) { return flags; }

            for (int i = 0; i < data.gridRows.Length; i++)
            {
                GridRow row = data.gridRows[i];

                if (row.blockType != null)
                {
                    for (int j = 0; j < row.blockType.Length; j++)
                    {
                        switch (row.blockType[j])
                        {
                            case BlockType.Blocked: flags |= MechanicFlags.Blocked; break;
                            case BlockType.Checkpoint: flags |= MechanicFlags.Checkpoint; break;
                            case BlockType.ForbiddenForPair: flags |= MechanicFlags.Forbidden; break;
                            case BlockType.AllowedForPairs: flags |= MechanicFlags.Permitted; break;
                            case BlockType.OneWay: flags |= MechanicFlags.OneWay; break;
                            case BlockType.Arrow: flags |= MechanicFlags.Arrow; break;
                            case BlockType.Bridge: flags |= MechanicFlags.Bridge; break;
                        }
                    }
                }

                // secondPairId is the only one of the three shared-destination columns a
                // permission rule also reads, so it is the only one that needs the guard --
                // see Block.SecondIdNamesAPair.
                if (row.secondPairId != null)
                {
                    for (int j = 0; j < row.secondPairId.Length; j++)
                    {
                        if (row.secondPairId[j] == 0) { continue; }

                        bool namesAPair = row.blockType != null
                                       && j < row.blockType.Length
                                       && Block.SecondIdNamesAPair(row.blockType[j]);
                        if (!namesAPair) { flags |= MechanicFlags.SharedDestination; }
                    }
                }

                if (HasAnyNonZero(row.thirdPairId) || HasAnyNonZero(row.fourthPairId))
                {
                    flags |= MechanicFlags.SharedDestination;
                }

                if (row.wallMask != null)
                {
                    for (int j = 0; j < row.wallMask.Length; j++)
                    {
                        if (row.wallMask[j] != 0) { flags |= MechanicFlags.Wall; break; }
                    }
                }
            }

            return flags;
        }

        private static bool HasAnyNonZero(int[] values)
        {
            if (values == null) { return false; }
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] != 0) { return true; }
            }
            return false;
        }

        /// <summary>Stable save-data keys for every mechanic set in <paramref name="flags"/>, or
        /// just <see cref="BasicFlowKey"/> when none are. Order matches the enum's declaration,
        /// not any display order -- callers that need a specific reading order (the HUD label)
        /// check their own flags directly rather than relying on this.</summary>
        public static string[] Keys(MechanicFlags flags)
        {
            if (flags == MechanicFlags.None) { return new[] { BasicFlowKey }; }

            var keys = new System.Collections.Generic.List<string>(9);
            if ((flags & MechanicFlags.Blocked) != 0) { keys.Add("Blocked"); }
            if ((flags & MechanicFlags.Wall) != 0) { keys.Add("Wall"); }
            if ((flags & MechanicFlags.OneWay) != 0) { keys.Add("OneWay"); }
            if ((flags & MechanicFlags.Arrow) != 0) { keys.Add("Arrow"); }
            if ((flags & MechanicFlags.Forbidden) != 0) { keys.Add("Forbidden"); }
            if ((flags & MechanicFlags.Permitted) != 0) { keys.Add("Permitted"); }
            if ((flags & MechanicFlags.Bridge) != 0) { keys.Add("Bridge"); }
            if ((flags & MechanicFlags.Checkpoint) != 0) { keys.Add("Checkpoint"); }
            if ((flags & MechanicFlags.SharedDestination) != 0) { keys.Add("SharedDestination"); }
            return keys.ToArray();
        }

        /// <summary>The single flag <see cref="Keys"/> would render as <paramref name="key"/>, or
        /// <see cref="MechanicFlags.None"/> for a key with no corresponding flag (e.g.
        /// <see cref="BasicFlowKey"/>, which names a mechanic-free board rather than a mechanic).
        /// The reverse of <see cref="Keys"/>, for callers that only have the string back (saved
        /// data, authored content) and need the flag to test against a bitmask.</summary>
        public static MechanicFlags FlagFor(string key)
        {
            switch (key)
            {
                case "Blocked": return MechanicFlags.Blocked;
                case "Wall": return MechanicFlags.Wall;
                case "OneWay": return MechanicFlags.OneWay;
                case "Arrow": return MechanicFlags.Arrow;
                case "Forbidden": return MechanicFlags.Forbidden;
                case "Permitted": return MechanicFlags.Permitted;
                case "Bridge": return MechanicFlags.Bridge;
                case "Checkpoint": return MechanicFlags.Checkpoint;
                case "SharedDestination": return MechanicFlags.SharedDestination;
                default: return MechanicFlags.None;
            }
        }
    }
}
