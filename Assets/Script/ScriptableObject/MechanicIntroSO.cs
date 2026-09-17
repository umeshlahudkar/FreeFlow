using UnityEngine;

namespace FreeFlow.GamePlay
{
    /// <summary>
    /// How one mechanic is taught the first time a player meets it: a name, a one-line rule, a
    /// small board to show it on, and the runs traced across that board.
    ///
    /// The board is a real <see cref="LevelData"/> rather than a picture, so the demo is drawn by
    /// the same <see cref="Block"/> prefab, the same markers and the same bars as the game -- a
    /// hand-drawn illustration of a mechanic is a second thing to keep in step with the art, and
    /// the one place it falls behind is the place it teaches the wrong thing. It is never played:
    /// nothing solves it, so it does not need a solution and does not need to be solvable.
    ///
    /// One asset per mechanic, keyed by the same string <see cref="LevelMechanics.Keys"/> mints,
    /// so adding a mechanic's intro is authoring an asset rather than writing code.
    /// </summary>
    [CreateAssetMenu(fileName = "MechanicIntro", menuName = "FreeFlow/Mechanic Intro")]
    public class MechanicIntroSO : ScriptableObject
    {
        /// <summary>Matches one of <see cref="LevelMechanics.Keys"/> -- "OneWay", "Arrow",
        /// "Bridge" and so on. This is the join between a board containing a mechanic and the
        /// card that explains it; a typo here means the intro silently never shows.</summary>
        public string mechanicKey;

        /// <summary>Heading, in the player's words rather than the enum's: "One-Way Cell".</summary>
        public string title;

        /// <summary>The rule, in one sentence. Long enough to be complete, short enough that it is
        /// read rather than skipped -- the demo below is what actually does the teaching.</summary>
        [TextArea(2, 4)] public string rule;

        /// <summary>Shown INSTEAD of the demo board on a card that has no runs -- a mechanic
        /// whose rule is the whole of it (a blocked cell, a checkpoint) and which an animation
        /// would only restate. Optional even then.</summary>
        public Sprite icon;

        /// <summary>Whether this card animates or just describes. A card with no runs is a
        /// description: the page hides the board and shows <see cref="icon"/> in its place.
        /// Deliberately derived rather than authored as a flag, so the two can never
        /// disagree.</summary>
        public bool HasDemo
        {
            get { return runs != null && runs.Length > 0 && demoBoard.gridRows != null && demoBoard.gridRows.Length > 0; }
        }

        /// <summary>The demo board. 4x4 by convention: big enough for a path to turn on and to
        /// approach a cell from two sides, small enough that every cell is genuinely large inside
        /// a card -- the same box that gives 154px cells at 5x5 gives 194px at 4x4, and the demo
        /// is looked at for a few seconds rather than studied.</summary>
        public LevelData demoBoard;

        /// <summary>The runs traced across it, in order, looping forever while the card is up.
        ///
        /// The pattern is refusals FIRST and the legal run LAST: the rule is learned from the
        /// moves that do not work and confirmed by the one that does, and putting the payoff last
        /// means a player who dismisses the card early still leaves having seen it.
        ///
        /// Two refusals beat one, because one reads as a single special case rather than as a
        /// rule -- but they should differ in KIND, not merely in position. For a one-way cell that
        /// is head-on (against the arrow) and sideways (across it); a second sideways approach
        /// from the opposite edge is the mirror of the first and teaches nothing the first did
        /// not, while making the loop longer than the card is looked at.</summary>
        public DemoRun[] runs;
    }

    /// <summary>One traced path across a <see cref="MechanicIntroSO.demoBoard"/>.</summary>
    [System.Serializable]
    public struct DemoRun
    {
        /// <summary>The cells the path visits, in order, as (row, column) -- x is the row, y the
        /// column, matching the level data's own indexing rather than screen axes. The first must
        /// be one of the board's dots: its colour and pair id are what the run is drawn in.</summary>
        public Vector2Int[] cells;

        /// <summary>When true the LAST cell is never entered: the path stops one short and that
        /// cell plays the same refusal flash the board gives a player who tries an illegal move.
        /// This is what makes a demo show the rule rather than merely a route.</summary>
        public bool endsRefused;

        /// <summary>Draw this run ON TOP of the one before it instead of clearing the board first.
        ///
        /// For a mechanic whose rule is about one path, clearing between runs is what keeps each
        /// one readable. For a Bridge it would hide the mechanic entirely: "two paths may be here
        /// at once" cannot be shown one path at a time, so its second crossing keeps the first on
        /// screen and the two are seen sharing the cell.
        ///
        /// Only meaningful on a run that FOLLOWS another; the first run of a loop always starts
        /// from a clear board.</summary>
        public bool keepPrevious;
    }
}
