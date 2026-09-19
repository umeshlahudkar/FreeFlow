using UnityEngine;
using FreeFlow.Enums;

/// <summary>
/// Authored, per-pack facts that the pack-select screen shows but cannot afford to derive at
/// runtime.
///
/// The colour range in particular is the reason this exists. It is a real property of a pack --
/// the min and max pairCount across its hundred levels -- but computing it means loading a
/// hundred ScriptableObjects, which would stall the screen every time it opens. Baking it here
/// costs one small asset and makes the detail free.
///
/// The difficulty tier is authored rather than measured, deliberately. LevelData.difficultyScore
/// exists and DifficultyAnalyzer can bucket it, but the measured medians across the shipped packs
/// come out nearly flat (Medium/Medium/Hard/Medium/Medium for Classic 5x5-9x9) and are not even
/// monotonic -- 7x7 scores harder than 9x9. A tag built on that would tell the player nothing and
/// actively mislead about the ramp. The authored ladder says what the packs are FOR.
/// </summary>
[CreateAssetMenu(fileName = "PackMetadata", menuName = "FreeFlow/Pack Metadata")]
public class PackMetadataSO : ScriptableObject
{
    public enum PackTier { Easy, Medium, Hard, VeryHard, Expert }

    [System.Serializable]
    public class Entry
    {
        public GameMode mode;
        public int packSize;
        public PackTier tier;

        // Fewest and most pairs any level in this pack uses. Shown as "4-7 colours".
        public int minColors;
        public int maxColors;
    }

    [System.Serializable]
    public class TierStyle
    {
        public PackTier tier;
        public string label;
        public Color cardBackground;
        public Color badgeBackground;
        public Color badgeText;
    }

    [SerializeField] private Entry[] entries;
    [SerializeField] private TierStyle[] tierStyles;

    /// <summary>The entry for one pack, or null when a size is not authored here -- callers must
    /// handle that rather than assume, so a newly added board size degrades to "no tag" instead
    /// of throwing on a screen the player is looking at.</summary>
    public Entry For(GameMode mode, int packSize)
    {
        if (entries == null) { return null; }
        for (int i = 0; i < entries.Length; i++)
        {
            if (entries[i] != null && entries[i].mode == mode && entries[i].packSize == packSize)
            {
                return entries[i];
            }
        }
        return null;
    }

    public TierStyle StyleFor(PackTier tier)
    {
        if (tierStyles == null) { return null; }
        for (int i = 0; i < tierStyles.Length; i++)
        {
            if (tierStyles[i] != null && tierStyles[i].tier == tier) { return tierStyles[i]; }
        }
        return null;
    }
}
