using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FreeFlow.Enums;
using FreeFlow.GamePlay;
using FreeFlow.Util;

namespace FreeFlow.UI
{
    /// <summary>
    /// Manages the UI elements and controls the flow of the game
    /// </summary>
    public class UIController : Singleton<UIController>
    {
        [Header("Hints")]
        // What a player starts with, granted once per save (see EnsureHintBalance). One balance
        // for the whole game -- not per level, per pack or per run -- so a hint saved on a Classic
        // 6x6 level is a hint still available in tomorrow's daily challenge.
        [SerializeField] private int startingHintCount = 3;

        [Header("Packs")]
        // Board sizes that have a generated pack, and how many levels each holds. A pack is a
        // self-contained run the player CHOOSES -- 5x5 through 9x9 for Classic -- rather than a
        // stage of one long campaign, so each ramps from the easiest board that size can produce to
        // the hardest and there is no ordering between them.
        [SerializeField] private int[] classicPackSizes = { 5, 6, 7, 8, 9 };
        [SerializeField] private int[] advancedPackSizes = { 6 };
        [SerializeField] private int packLevelCount = 100;

        // Which pack the level screen is showing. 0 meant the legacy linear campaign (the original
        // Classic 1-100 and Advanced 1-45); those level assets are now deleted, so 0 is a dead
        // setting, not a working fallback -- LevelResourcePath still builds a path for it, but
        // Resources.Load finds nothing there. Nothing in the shipped UI sets this to 0.
        [SerializeField] private int currentPackSize = 6;

        // Which campaign to open on. Serialised because nothing calls SetMode yet: without it the
        // mode is fixed at Classic in code and the Advanced packs cannot be reached at all, which
        // reads in play as "the mechanics are missing" when they are simply in the other folder.
        [SerializeField] private GameMode startingMode = GameMode.Classic;

        [Header("Daily Challenge")]
        // How many levels one calendar day of daily challenges holds. Serialised rather than a
        // constant so the day's length is a design dial, not a code change -- the hub's reference
        // art shows five, which is the default here.
        //
        // Changing this DOES re-pick the current day (see EnsureTodayDailyPicks): the cache exists
        // so the boards cannot move under a player mid-session, not so a deliberate config change
        // is ignored until tomorrow.
        [SerializeField, Range(1, 7)] private int dailyChallengeCount = 5;

        private LevelData currentLevelData;
        private SingleLevelDataSO currentLevelDataAsset;
        private int currentLevel;

        // Which route the level in play was opened by, and therefore what prev/next step through
        // and what the header says -- see LevelSource. Set by the two public entry points
        // (LoadLevel for a pack level, LoadDailyChallenge for a daily one) and by nothing else, so
        // a reload that is neither -- Retry -- simply leaves it alone and cannot demote a daily
        // challenge to a pack level the way the bool this replaced once did.
        private LevelSource currentSource = LevelSource.Pack;

        // Today's daily challenges and which of them is in play, mirrored from SaveData whenever
        // EnsureTodayDailyPicks runs. Cached here only so the pages can ask for the day's shape
        // (how many, which one, what the next one is) without each doing its own file read; the
        // SOLVED flags in it go stale the moment one is completed, so anything that needs those
        // re-reads SaveData -- see IsTodayDailyChallengeSolved.
        private DailyPick[] dailyPicks = new DailyPick[0];
        private int dailyIndex;

        // Which calendar day dailyPicks belongs to. A daily run belongs to a DAY, and the day can
        // end while a level from it is still on screen -- a player who starts a challenge at
        // 23:59 finishes it on the next day. Without this, dailyIndex is just an offset with no
        // day attached, and once anything re-selects for the new day it silently indexes into a
        // different day's challenges. -1 until a daily run has been opened.
        private int dailyPicksDay = -1;

        public int CurrentLevel { get { return currentLevel; } }

        /// <summary>Which route the level in play was opened by. Drives the header text and what
        /// prev/next walk through, on both the gameplay screen and the level-complete overlay.</summary>
        public LevelSource CurrentSource { get { return currentSource; } }

        /// <summary>Whether the level in play is one of today's daily challenges. Read by
        /// GamePlayController.SaveLevelData to credit the day, and by LevelCompletePage to show
        /// the streak banner.</summary>
        public bool IsDailyChallenge { get { return currentSource == LevelSource.Daily; } }

        /// <summary>Which of today's daily challenges is in play (0-based). Meaningless unless
        /// <see cref="IsDailyChallenge"/>.</summary>
        public int DailyIndex { get { return dailyIndex; } }

        /// <summary>How many daily challenges today holds, as last read from the save.</summary>
        public int DailyCount { get { return dailyPicks.Length; } }

        /// <summary>The calendar day the daily challenge in play was drawn for. Compared against
        /// SaveData.dailyChallengeCachedDay before any daily bookkeeping is written, so a level
        /// belonging to a day that has since been replaced cannot credit the new day's slots.</summary>
        public int DailyDayIndex { get { return dailyPicksDay; } }

        /// <summary>Whether the daily run in play belongs to a day that has since ended -- true
        /// only in the narrow window where the player was mid-challenge as the reset passed.
        /// Today's challenges are a different set, so stepping to "the next one" is meaningless
        /// and the player is sent to the hub to see the new day instead.</summary>
        public bool DailyDayHasEnded
        {
            get
            {
                return currentSource == LevelSource.Daily
                    && dailyPicksDay >= 0
                    && dailyPicksDay != DailyChallengeSelector.DayIndex(System.DateTime.UtcNow);
            }
        }

        /// <summary>How many challenges a day is configured to hold. Unlike <see cref="DailyCount"/>
        /// this needs no day to have been selected yet, so a screen can describe today's challenges
        /// without committing a pick just by being looked at -- what the main menu's daily card
        /// needs before the player has ever opened the hub.</summary>
        public int ConfiguredDailyChallengeCount
        {
            get { return Mathf.Max(1, dailyChallengeCount); }
        }

        /// <summary>Which campaign is being played. Classic is the default and the front door;
        /// see <see cref="GameMode"/> for why the two are separate level sets rather than a
        /// difficulty toggle.</summary>
        public GameMode CurrentMode { get; private set; } = GameMode.Classic;

        /// <summary>How many levels the CURRENT mode has. Every caller that used to ask for a
        /// single campaign total wants this.</summary>
        /// <summary>Board size of the pack on screen, or 0 for the legacy linear campaign.</summary>
        public int CurrentPackSize { get { return currentPackSize; } }

        /// <summary>Pack sizes available in <paramref name="mode"/>, for a pack-select screen.</summary>
        public int[] PackSizesFor(GameMode mode)
        {
            return mode == GameMode.Advanced ? advancedPackSizes : classicPackSizes;
        }

        /// <summary>Levels in a pack of this size. Currently uniform across every size (see
        /// packLevelCount), but exposed per-size rather than as a bare constant so a pack-select
        /// screen never needs its own copy of that assumption.</summary>
        public int PackLevelCountFor(int packSize)
        {
            return packLevelCount;
        }

        /// <summary>Same key format as <see cref="ProgressKey"/>, for any pack size/mode
        /// combination rather than just the one currently on screen -- what a pack-select screen
        /// needs to read every pack's progress at once. Callers must use this rather than
        /// building the string themselves -- see SaveData's own warning about that.</summary>
        public string KeyFor(GameMode mode, int packSize)
        {
            return packSize > 0 ? mode.ToString() + packSize + "x" + packSize : mode.ToString();
        }

        /// <summary>Levels in the run currently on screen. Every run is a pack, and every pack
        /// holds <see cref="packLevelCount"/> levels -- the legacy linear campaigns, which had
        /// their own per-mode totals, no longer have level assets to load.</summary>
        public int TotalLevelCount
        {
            get { return packLevelCount; }
        }

        /// <summary>
        /// Identifies whose progress this is. Packs are keyed by mode AND size, because finishing
        /// 5x5 level 20 must not mark 7x7 level 20 complete; the legacy campaigns keep the bare mode
        /// name and, through <see cref="SaveData"/>, their original fields -- so a returning player
        /// mid-way through the old run keeps their place.
        ///
        /// A daily challenge keys to the pack it was drawn FROM, not to a daily-only bucket: it is
        /// that pack's level, and finishing it really does finish that pack level.
        /// </summary>
        public string ProgressKey
        {
            get
            {
                return currentPackSize > 0
                    ? CurrentMode.ToString() + currentPackSize + "x" + currentPackSize
                    : CurrentMode.ToString();
            }
        }

        /// <summary>
        /// Where the current mode's level assets live. The two campaigns are numbered
        /// independently -- Classic 1 and Advanced 1 are different boards -- so the mode is part
        /// of the path rather than an offset into one shared range.
        /// </summary>
        private string LevelResourcePath(int levelNumber)
        {
            if (currentPackSize > 0)
            {
                return "Levels/" + CurrentMode + "/" + currentPackSize + "x" + currentPackSize
                     + "/Level_" + levelNumber;
            }
            return "Levels/" + CurrentMode + "/Level_" + levelNumber;
        }

        /// <summary>
        /// Switches campaign and rebuilds the level list. Progress is stored per mode, so this
        /// does not disturb the other campaign's completion.
        /// </summary>
        public void SetMode(GameMode mode)
        {
            if (CurrentMode == mode) { return; }
            CurrentMode = mode;

            // A pack size that exists in one mode may not in the other -- Classic has 5x5 through
            // 9x9, Advanced only what has been generated so far -- so fall back rather than leave
            // the level screen pointing at a folder with nothing in it.
            int[] available = PackSizesFor(mode);
            if (currentPackSize > 0 && System.Array.IndexOf(available, currentPackSize) < 0)
            {
                currentPackSize = available.Length > 0 ? available[0] : 0;
            }

            LevelsScreen.SpawnLevelButtons(TotalLevelCount);
        }

        /// <summary>
        /// Switches the level screen to another pack. <paramref name="packSize"/> is a board size,
        /// or 0 for the legacy linear campaign.
        /// </summary>
        public void SetPack(int packSize)
        {
            if (currentPackSize == packSize) { return; }
            currentPackSize = packSize;
            LevelsScreen.SpawnLevelButtons(TotalLevelCount);
        }

        public int CurrentLevelGoal { get { return currentLevelData.pairCount; } }

        // Pages are reached through PageManager, which is the one place their references live --
        // a second serialized field here is a second thing to keep wired, and a second thing that
        // can quietly end up pointing somewhere else. See PageManager.Get.
        private LevelsPage LevelsScreen
        {
            get { return PageManager.Instance.Get<LevelsPage>(PageType.Levels); }
        }

        private GameplayPage GameplayScreen
        {
            get { return PageManager.Instance.Get<GameplayPage>(PageType.Gameplay); }
        }

        private void Start()
        {
            CurrentMode = startingMode;

            int[] available = PackSizesFor(CurrentMode);
            if (currentPackSize > 0 && System.Array.IndexOf(available, currentPackSize) < 0)
            {
                Debug.LogWarning("UIController: " + CurrentMode + " has no " + currentPackSize + "x"
                    + currentPackSize + " pack; falling back to "
                    + (available.Length > 0 ? available[0] + "x" + available[0] : "the linear campaign") + ".");
                currentPackSize = available.Length > 0 ? available[0] : 0;
            }

            LevelsScreen.SpawnLevelButtons(TotalLevelCount);

            // Before any screen can read the balance, so a save that has never held one is never
            // seen as a save with no hints left.
            EnsureHintBalance();
        }

        // ---- hints -----------------------------------------------------------------------
        //
        // One balance for the whole game, held in the save file rather than here so it survives
        // the session; this class owns only the opening grant. Spending is GamePlayController's
        // (see RecordHintUsed) -- it is the thing that knows a hint actually committed to a pair.

        /// <summary>How many hints the player has left, across every mode, pack and run.</summary>
        public int HintsRemaining
        {
            get { return SavingSystem.Instance.Load().hintsRemaining; }
        }

        /// <summary>Grants <see cref="startingHintCount"/> hints to a save that has never been
        /// granted any, and does nothing to one that has. Keyed on SaveData.hintsInitialized
        /// rather than on the balance being 0, so a player who has spent every hint is not handed
        /// a fresh set on the next level load.</summary>
        private void EnsureHintBalance()
        {
            SaveData data = SavingSystem.Instance.Load();
            if (data.hintsInitialized) { return; }

            data.hintsRemaining = Mathf.Max(0, startingHintCount);
            data.hintsInitialized = true;
            SavingSystem.Instance.Save(data);
        }

        /// <summary>Puts a short message on screen -- why a tap did nothing, typically -- and lets
        /// it take itself away again (see WarningNotifier). Composed here, the same way this class
        /// owns every other piece of wording the screens show, so the same situation cannot be
        /// described two different ways on two different screens.</summary>
        public void ShowWarning(string message)
        {
            WarningNotifier notifier = PageManager.Instance.Get<WarningNotifier>(PageType.Warning);
            if (notifier == null) { return; }

            notifier.SetMessage(message);
            PageManager.Instance.OpenAsOverlay(PageType.Warning);
        }

        /// <summary>What the hint button says when it is tapped with nothing left to spend.</summary>
        public string NoHintsMessage { get { return "No More Hints"; } }

        // ---- header text -------------------------------------------------------------------
        //
        // Owned here rather than by each screen so the gameplay header and the level-complete
        // overlay can never disagree about what the player is playing -- they were separately
        // composed strings before, and only the gameplay one knew the daily challenge existed.

        /// <summary>The same thing <see cref="LevelHeaderTitle"/> names, written as prose rather
        /// than as a UI label: "Level 24", "today's Daily Challenge". For text a person READS --
        /// a shared message, where the header's all-caps reads as shouting rather than as a
        /// heading.</summary>
        public string LevelPhrase
        {
            get
            {
                return currentSource == LevelSource.Daily
                    ? "today's Daily Challenge"
                    : "Level " + currentLevel;
            }
        }

        /// <summary>The big line of the header: which level, or that this is the daily
        /// challenge -- a daily challenge is a whole different run, not "level 37 of a pack",
        /// even though it is drawn from one.</summary>
        public string LevelHeaderTitle
        {
            get
            {
                return currentSource == LevelSource.Daily ? "DAILY CHALLENGE" : "LEVEL " + currentLevel;
            }
        }

        /// <summary>The small line under it: for a pack level the campaign and board size
        /// ("CLASSIC 6 x 6"), for a daily challenge where in the day it sits ("2 OF 5 - 6 x 6").
        /// A day holding exactly one challenge drops the position, which would only ever read
        /// "1 OF 1".</summary>
        public string LevelHeaderSubtitle
        {
            get
            {
                string size = currentPackSize > 0 ? currentPackSize + " × " + currentPackSize : "";

                if (currentSource == LevelSource.Daily)
                {
                    if (DailyCount <= 1) { return size; }
                    string position = (dailyIndex + 1) + " OF " + DailyCount;
                    return size.Length == 0 ? position : position + "  ·  " + size;
                }

                string mode = CurrentMode.ToString().ToUpperInvariant();
                return size.Length == 0 ? mode : mode + " " + size;
            }
        }

        /// <summary>What the level-complete overlay's NEXT button is offering, or an empty string
        /// when there is nothing after this one. Built here so that label can never promise a
        /// level <see cref="GoToNextLevel"/> would not actually load.</summary>
        /// <summary>The heading on the level-complete overlay's Prev button -- the mirror of
        /// <see cref="NextActionTitle"/>. Prev never leaves the run (there is no "before" to exit
        /// to), so unlike Next it stays a plain step-back and simply disables at the start.</summary>
        public string PrevActionTitle
        {
            get { return currentSource == LevelSource.Daily ? "PREV CHALLENGE" : "PREV LEVEL"; }
        }

        /// <summary>What stepping back would open, or an empty string at the start of a run.</summary>
        public string PrevActionSubtitle
        {
            get
            {
                if (!HasPrevLevel) { return ""; }

                if (currentSource == LevelSource.Daily)
                {
                    DailyPick prev = dailyPicks[dailyIndex - 1];
                    return "DAILY " + dailyIndex + " OF " + DailyCount
                         + "  ·  " + prev.packSize + "×" + prev.packSize;
                }

                return "LEVEL " + (currentLevel - 1)
                     + (currentPackSize > 0 ? "  ·  " + currentPackSize + "×" + currentPackSize : "");
            }
        }

        // ---- gameplay footer step labels -----------------------------------------------------
        //
        // The footer's two stepping buttons, which say the same things as the level-complete
        // overlay's pair. The subtitle uses a tighter separator than the overlay's: 320px of
        // button with a 100px chevron in it leaves about 215px of text, where the overlay's
        // primary button has 480, and the wider spacing pushes the longest daily subtitle onto
        // a second line.
        //
        // Next is deliberately not NextActionTitle/NextActionSubtitle: that pair turns into a way
        // OUT of the run at the end of a pack or a day, which the footer's button does not do (it
        // fades instead), so borrowing it would have the footer promise "CHOOSE A PACK" on a
        // button that refuses the tap.
        //
        // A subtitle names the level that EXISTS either side of this one whether or not it can be
        // opened yet: a next level still behind the unlock frontier is named and the button faded
        // (see HasNextLevel, and GameplayPage.Refresh), so the player is told what is waiting
        // rather than shown a button that silently does nothing. Only a genuine end of the run --
        // the pack's first/last level, the day's first/last challenge -- empties it, because then
        // there is no such level to name.

        public string PrevStepTitle
        {
            get { return currentSource == LevelSource.Daily ? "PREV CHALLENGE" : "PREV LEVEL"; }
        }

        public string NextStepTitle
        {
            get { return currentSource == LevelSource.Daily ? "NEXT CHALLENGE" : "NEXT LEVEL"; }
        }

        public string PrevStepSubtitle
        {
            get
            {
                if (currentSource == LevelSource.Daily)
                {
                    if (dailyIndex <= 0) { return ""; }
                    DailyPick prev = dailyPicks[dailyIndex - 1];
                    return "DAILY " + dailyIndex + " OF " + DailyCount
                         + " · " + prev.packSize + "×" + prev.packSize;
                }

                if (currentLevel <= 1) { return ""; }

                return "LEVEL " + (currentLevel - 1) + PackSizeSuffix;
            }
        }

        public string NextStepSubtitle
        {
            get
            {
                if (currentSource == LevelSource.Daily)
                {
                    if (dailyIndex >= DailyCount - 1) { return ""; }
                    DailyPick next = dailyPicks[dailyIndex + 1];
                    return "DAILY " + (dailyIndex + 2) + " OF " + DailyCount
                         + " · " + next.packSize + "×" + next.packSize;
                }

                if (currentLevel >= TotalLevelCount) { return ""; }

                return "LEVEL " + (currentLevel + 1) + PackSizeSuffix;
            }
        }

        /// <summary>" · 6×6", or nothing at all for a pack whose size is unknown -- a bare
        /// trailing separator would read as a truncated label.</summary>
        private string PackSizeSuffix
        {
            get { return currentPackSize > 0 ? " · " + currentPackSize + "×" + currentPackSize : ""; }
        }

        /// <summary>The heading on the level-complete overlay's primary button. It is the same
        /// button throughout: while the run has something after this level it advances, and at the
        /// end of a run it becomes the way out to wherever that run was chosen from -- a finished
        /// run should hand the player somewhere to go, not a greyed-out button.</summary>
        public string NextActionTitle
        {
            get
            {
                if (HasNextLevel)
                {
                    return currentSource == LevelSource.Daily ? "NEXT CHALLENGE" : "NEXT LEVEL";
                }

                return currentSource == LevelSource.Daily ? "BACK TO DAILY" : "CHOOSE A PACK";
            }
        }

        /// <summary>The line under <see cref="NextActionTitle"/>: what is being offered next, or
        /// why there is nothing after this one. Only claims a run is finished when it actually is
        /// -- the end of a run is normally the last level, but a locked next level would reach
        /// here too and must not be reported as a completed pack.</summary>
        public string NextActionSubtitle
        {
            get
            {
                if (HasNextLevel)
                {
                    if (currentSource == LevelSource.Daily)
                    {
                        DailyPick next = dailyPicks[dailyIndex + 1];
                        return "DAILY " + (dailyIndex + 2) + " OF " + DailyCount
                             + "  ·  " + next.packSize + "×" + next.packSize;
                    }

                    return "LEVEL " + (currentLevel + 1)
                         + (currentPackSize > 0 ? "  ·  " + currentPackSize + "×" + currentPackSize : "");
                }

                if (currentSource == LevelSource.Daily)
                {
                    return dailyIndex >= DailyCount - 1 ? "DAY COMPLETE" : "";
                }

                return currentLevel >= TotalLevelCount ? "PACK COMPLETE" : "";
            }
        }

        // ---- navigation --------------------------------------------------------------------
        //
        // One definition of "is there a level before/after this one", used by the gameplay HUD's
        // arrows AND the level-complete overlay's, so a button can never be tappable on one screen
        // and refuse on the other. What "before/after" MEANS depends on how the level was opened:
        // within a pack it is the level numbering, within a daily challenge it is today's own list
        // (whose entries sit in different packs and board sizes).

        public bool HasPrevLevel
        {
            get
            {
                if (currentSource == LevelSource.Daily) { return dailyIndex > 0; }
                return currentLevel > 1;
            }
        }

        public bool HasNextLevel
        {
            get
            {
                // A day is played in order, same as a pack: the next challenge opens only once
                // this one is solved. Read from the save rather than the cached dailyPicks --
                // those solved flags are stale the moment a level is completed, which is exactly
                // when this is asked (the level-complete overlay).
                if (currentSource == LevelSource.Daily)
                {
                    if (dailyIndex >= DailyCount - 1) { return false; }
                    return dailyIndex + 1 <= SavingSystem.Instance.Load().UnlockedDailyChallengeThrough();
                }

                if (currentLevel >= TotalLevelCount) { return false; }

                // Same unlock frontier the level grid enforces: Next cannot jump past a level the
                // player has not reached, exactly as a locked LevelButton refuses a tap.
                int unlockedUpTo = SavingSystem.Instance.Load().CompletedLevelForKey(ProgressKey) + 1;
                return currentLevel < unlockedUpTo;
            }
        }

        public void GoToPrevLevel()
        {
            if (!HasPrevLevel) { return; }

            if (currentSource == LevelSource.Daily)
            {
                if (DailyDayHasEnded) { ExitToRunHome(); return; }
                LoadDailyChallenge(dailyIndex - 1);
                return;
            }

            LoadLevel(currentLevel - 1);
        }

        /// <summary>Advances to whatever comes after the level in play -- what the gameplay HUD's
        /// Next arrow and the level-complete overlay's NEXT button both do. No-ops when there is
        /// nothing after it (the pack's last level, or the day's last challenge); both callers
        /// also disable their button in that state, so this is the backstop rather than the
        /// gate.</summary>
        public void GoToNextLevel()
        {
            if (!HasNextLevel) { return; }

            if (currentSource == LevelSource.Daily)
            {
                // The reset passed while this challenge was being played. "The next one" belongs
                // to a day that no longer exists -- LoadDailyChallenge would re-select for today
                // and drop the player on its FIRST challenge, while the button said something
                // like "DAILY 4 OF 5". Show them the new day instead of quietly substituting it.
                if (DailyDayHasEnded) { ExitToRunHome(); return; }

                LoadDailyChallenge(dailyIndex + 1);
                return;
            }

            LoadLevel(currentLevel + 1);
        }

        /// <summary>
        /// Loads a level from the pack currently on screen -- what a LevelButton tap does. Opening
        /// a level this way means the player is browsing a pack, so prev/next walk the pack from
        /// here on and the header shows the level number, even if a daily challenge was in play a
        /// moment ago.
        /// </summary>
        /// <param name="levelNumber">The number of the level to load.</param>
        public void LoadLevel(int levelNumber)
        {
            currentSource = LevelSource.Pack;
            LoadCurrentModeLevel(levelNumber);
        }

        /// <summary>
        /// Loads a level of the mode/pack already selected, WITHOUT deciding what kind of run this
        /// is -- that is <see cref="currentSource"/>'s owner's job (LoadLevel and
        /// LoadDailyChallenge each set it before calling here). Retry deliberately calls this
        /// rather than LoadLevel: reloading the same board is a continuation of the same attempt,
        /// so it must not change which run the player is in.
        /// </summary>
        private void LoadCurrentModeLevel(int levelNumber)
        {
            if (levelNumber <= TotalLevelCount)
            {
                // Set BEFORE ResetGameplay -- it calls BeginAttempt, which records the new
                // attempt against UIController.Instance.CurrentLevel. Reading it after ResetGameplay
                // used to record every level-select jump (as opposed to a retry or "next", which
                // both already point currentLevel at the right level before calling LoadLevel)
                // against whatever level had been playing before, not the one being opened.
                currentLevel = levelNumber;

                GamePlayController.Instance.ResetGameplay();

                if (currentLevelDataAsset != null)
                {
                    Resources.UnloadAsset(currentLevelDataAsset);
                }
                string path = LevelResourcePath(levelNumber);
                currentLevelDataAsset = Resources.Load<SingleLevelDataSO>(path);

                // A missing asset used to surface as a NullReferenceException one line down, which
                // says nothing about the cause. A mis-set pack size or a pack that was never
                // generated is the likely reason, so name the path.
                if (currentLevelDataAsset == null)
                {
                    Debug.LogError("No level asset at Resources/" + path
                        + " -- check the pack exists for " + CurrentMode + " at this board size.");
                    return;
                }

                currentLevelData = currentLevelDataAsset.levelData;

                PageManager.Instance.CloseOverlay(PageType.LevelComplete);
                PageManager.Instance.OpenPage(PageType.Gameplay);

                GamePlayController.Instance.GenerateBoard(currentLevelData);

                // GameplayPage's own OnEnable refreshes the whole screen -- header, progress
                // card, hint button, prev/next buttons -- and OpenPage above already triggered it
                // (it cycles the page even when Gameplay was already current, see
                // PageManager.OpenPage). That ran BEFORE GenerateBoard though, so the hint button
                // was set against the previous level's answer and the progress card against the
                // previous board; refresh once more now this level's board is actually in place.
                GameplayPage page = GameplayScreen;
                if (page != null) { page.Refresh(); }
            }
        }

        // ---- daily challenge ---------------------------------------------------------------

        /// <summary>
        /// Today's daily challenges, selecting and caching them if that has not happened yet, and
        /// mirroring the result into <see cref="dailyPicks"/>. Every caller goes through here
        /// rather than reading SaveData directly, so the hub can never show a day the gameplay
        /// screen would then load something different for.
        ///
        /// The picks are real levels from existing packs, picked once per calendar day and cached
        /// so they do not change under the player mid-session (see DailyChallengeSelector).
        /// Classic only for now: it is the default mode and the one whose packs vary by board
        /// size, which is what the selector rotates through slot to slot; Advanced ships one pack
        /// size so far, which would make that rotation a no-op for it.
        ///
        /// Deliberately reuses each pack's own LevelData rather than daily-only assets --
        /// completing one also completes that pack level, which is correct (it IS that level), and
        /// avoids a second content pipeline for a handful of levels a day.
        /// </summary>
        public DailyPick[] EnsureTodayDailyPicks()
        {
            SaveData data = SavingSystem.Instance.Load();
            bool dataChanged = false;

            // Assigned once, ever, on whichever device first opens the daily challenge -- see
            // SaveData.playerSalt and EnsurePlayerSalt. Range's upper bound is exclusive, so this
            // can never generate the 0 that means "unset".
            if (data.playerSalt == 0)
            {
                data.EnsurePlayerSalt(Random.Range(1, int.MaxValue));
                dataChanged = true;
            }

            int today = DailyChallengeSelector.DayIndex(System.DateTime.UtcNow);
            int wanted = Mathf.Max(1, dailyChallengeCount);

            // A day whose streak credit has already been banked is finished, and re-picking it
            // would leave the hub reading "0 of 5 solved" next to a week chain and streak card
            // that already counted the day -- which reads as the streak having moved without the
            // day being completed, the one thing the all-solved rule exists to prevent. A longer
            // day starts tomorrow instead. This is what a save written before a day could hold
            // several challenges hits on its first launch, and what a mid-day count change hits
            // once the day is done.
            bool dayAlreadyCredited = data.dailyChallengeCachedDay == today
                && data.dailyChallengeLastCompletedDay == today;

            // Re-pick on a new day, or when dailyChallengeCount has been changed since the cache
            // was written. The second case is a deliberate config change, not drift -- see the
            // field's own comment.
            bool wrongLength = data.DailyChallengeCount != wanted && !dayAlreadyCredited;

            if (data.dailyChallengeCachedDay != today || wrongLength)
            {
                DailyChallengeSelector.Pick[] picks = DailyChallengeSelector.SelectDay(
                    today, GameMode.Classic, PackSizesFor(GameMode.Classic), packLevelCount,
                    data.OverallSkillRating(), data.playerSalt, wanted);

                DailyPick[] stored = new DailyPick[picks.Length];
                for (int i = 0; i < picks.Length; i++)
                {
                    stored[i] = new DailyPick
                    {
                        mode = picks[i].mode,
                        packSize = picks[i].packSize,
                        levelNumber = picks[i].levelNumber,
                        solved = false,
                    };
                }

                data.SetDailyChallenges(today, stored);
                dataChanged = true;
            }

            if (dataChanged) { SavingSystem.Instance.Save(data); }

            dailyPicks = data.dailyChallengePicks ?? new DailyPick[0];
            // Either branch above leaves the cache on today, so the picks now in hand are today's.
            dailyPicksDay = today;
            return dailyPicks;
        }

        /// <summary>Records that the player has now seen today's challenges, clearing the main
        /// menu's "NEW" badge until the next daily reset. Does its own load/save rather than
        /// taking a SaveData: its caller (DailyChallengePage.Refresh) runs
        /// <see cref="EnsureTodayDailyPicks"/> first, which may itself write, and saving a copy
        /// read before that would put the day's freshly selected picks straight back.</summary>
        public void MarkDailyChallengeSeen()
        {
            int today = DailyChallengeSelector.DayIndex(System.DateTime.UtcNow);

            SaveData data = SavingSystem.Instance.Load();
            if (data.dailyChallengeLastSeenDay == today) { return; }

            data.dailyChallengeLastSeenDay = today;
            SavingSystem.Instance.Save(data);
        }

        /// <summary>Opens one of today's daily challenges by its position in the day (0-based).
        /// Switches mode/pack to wherever that level actually lives, since the day's challenges
        /// are drawn from different packs.
        ///
        /// Clamped to the day's unlock frontier (see SaveData.UnlockedDailyChallengeThrough), so a
        /// slot the player has not reached opens the frontier challenge instead of the locked one.
        /// The hub's locked tiles and the disabled Next button already prevent asking for one --
        /// this is the backstop that keeps the rule true regardless of the caller.</summary>
        public void LoadDailyChallenge(int slot)
        {
            DailyPick[] picks = EnsureTodayDailyPicks();
            if (picks.Length == 0)
            {
                Debug.LogError("UIController: no daily challenge could be selected for today -- "
                    + "check that " + GameMode.Classic + " has at least one pack size configured.");
                return;
            }

            int unlockedThrough = SavingSystem.Instance.Load().UnlockedDailyChallengeThrough();
            slot = Mathf.Clamp(slot, 0, Mathf.Min(picks.Length - 1, unlockedThrough));

            currentSource = LevelSource.Daily;
            dailyIndex = slot;

            SetMode(picks[slot].mode);
            SetPack(picks[slot].packSize);
            LoadCurrentModeLevel(picks[slot].levelNumber);
        }

        /// <summary>
        /// Activates the level complete screen and hands it the attempt's real stats (moves,
        /// hints, time, and the pack-progress before this completion -- all read from
        /// GamePlayController, none fabricated). LevelCompletePage owns every field this content
        /// needs and reads everything else (mode, pack size, current level, which run this is)
        /// itself from this controller's own public properties -- see
        /// LevelCompletePage.SetLevelCompleteData.
        ///
        /// Called AFTER GamePlayController.SaveLevelData (see CheckForLevelComplete) specifically
        /// so that on a daily-challenge completion, the streak SetLevelCompleteData reads
        /// reflects the count SaveLevelData just persisted rather than the value from before this
        /// completion.
        /// </summary>
        /// <param name="movesCount">Moves made this attempt.</param>
        /// <param name="hintsUsedThisAttempt">Hints used since this attempt began (a diff against
        /// the lifetime hint total -- see GamePlayController.hintsAtAttemptStart).</param>
        /// <param name="secondsTaken">Wall-clock time this attempt took to solve the level (see
        /// GamePlayController.lastCompletionSeconds).</param>
        /// <param name="oldCompletedLevel">CompletedLevelForKey for this pack BEFORE this
        /// completion, so the progress bar can show where the player was, not just where they are.</param>
        public void ActivateLevelCompleteScreen(int movesCount, int hintsUsedThisAttempt, float secondsTaken, int oldCompletedLevel)
        {
            LevelCompletePage page = PageManager.Instance.Get<LevelCompletePage>(PageType.LevelComplete);
            if (page != null)
            {
                page.SetLevelCompleteData(movesCount, hintsUsedThisAttempt, secondsTaken, oldCompletedLevel);
            }
            PageManager.Instance.OpenAsOverlay(PageType.LevelComplete);
        }

        /// <summary>
        /// Resets gameplay state and reloads the level currently in progress -- what every
        /// "Retry" button (the Level Complete overlay's) does. Goes through
        /// LoadCurrentModeLevel rather than LoadLevel precisely so it does NOT touch
        /// <see cref="currentSource"/>: retrying a daily challenge is still that daily challenge.
        /// </summary>
        public void RetryCurrentLevel()
        {
            GamePlayController.Instance.ResetGameplay();
            // LoadCurrentModeLevel closes the LevelComplete overlay itself before opening Gameplay.
            LoadCurrentModeLevel(currentLevel);
        }

        /// <summary>
        /// Resets gameplay state and returns to the main menu -- what every "Home" button
        /// (the Level Complete overlay's, and the in-HUD Home) does. Unconditionally closes the
        /// Level Complete overlay -- safe even when it was never open -- so this one method works
        /// from either caller.
        /// </summary>
        public void GoToMainMenu()
        {
            GamePlayController.Instance.ResetGameplay();

            PageManager.Instance.CloseOverlay(PageType.LevelComplete);
            PageManager.Instance.OpenPage(PageType.MainMenu);
        }

        /// <summary>
        /// Puts the level-complete overlay away and hands the finished board back, WITHOUT
        /// leaving the level -- what tapping the backdrop behind the sheet does. The solved board
        /// is worth looking at: players screenshot it, and some want to redraw a route on it. The
        /// overlay covering it permanently, with no way past except forward or out, made that
        /// impossible.
        ///
        /// Three things have to happen together. The overlay closes; the board goes back to
        /// <see cref="GameState.Playing"/> so it accepts touches again (input is gated on that
        /// state, so without it the board would only be visible, not usable); and the HUD is
        /// refreshed, because completing the level may have just moved the unlock frontier and the
        /// gameplay Next arrow was last set before that happened.
        ///
        /// Re-completing the board from here is allowed and simply reopens this overlay. It does
        /// not record a second completion -- see GamePlayController.completionRecordedThisAttempt.
        /// </summary>
        public void DismissLevelCompleteOverlay()
        {
            PageManager.Instance.CloseOverlay(PageType.LevelComplete);
            GamePlayController.Instance.GameState = GameState.Playing;

            GameplayPage page = GameplayScreen;
            if (page != null) { page.Refresh(); }
        }

        /// <summary>
        /// Leaves gameplay for the screen the CURRENT RUN was chosen from -- the Daily Challenge
        /// hub for a daily, the pack grid's own chooser for a pack level. What the level-complete
        /// overlay's primary button does once there is nothing after this level: finishing the
        /// last challenge of the day should offer the day, and finishing a pack should offer the
        /// other packs, rather than dropping the player on the main menu or at a dead button.
        ///
        /// Both destinations are already below Gameplay on the back-stack in the normal flow, so
        /// PageManager.OpenPage pops back to them rather than stacking a second copy -- which also
        /// means a later Back behaves as if the player had simply walked out of the run.
        /// </summary>
        public void ExitToRunHome()
        {
            GamePlayController.Instance.ResetGameplay();

            PageManager.Instance.CloseOverlay(PageType.LevelComplete);
            PageManager.Instance.OpenPage(currentSource == LevelSource.Daily
                ? PageType.DailyChallenge
                : PageType.PackSelect);
        }

    }
}
