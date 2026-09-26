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

        private LevelData currentLevelData;
        private SingleLevelDataSO currentLevelDataAsset;
        private int currentLevel;

        // Which route the level in play was opened by, and therefore what prev/next step through
        // and what the header says -- see LevelSource. Set by the two public entry points
        // (LoadLevel for a pack level, LoadDailyChallenge for a daily one) and by nothing else, so
        // a reload that is neither -- Retry -- simply leaves it alone and cannot demote a daily
        // challenge to a pack level the way the bool this replaced once did.
        private LevelSource currentSource = LevelSource.Pack;

        // The board size of the daily level currently open, alongside dailyPicksDay below --
        // needed for header/subtitle text since a daily run deliberately does NOT touch
        // currentPackSize/CurrentMode any more (see LoadDailyChallengeForDay's own comment on
        // why: those drive real pack progress, and a daily level must never write to one).
        private int dailyPackSize;

        // Which calendar day (DailyChallengeSelector.DayIndex/EpochUtc numbering) the daily level
        // currently open belongs to. -1 until a daily run has been opened. A daily run belongs to
        // a DAY, and the day can end while a level from it is still on screen -- a player who
        // starts a challenge at 23:59 finishes it after midnight; nothing here needs to notice
        // that any more (see GamePlayController.SaveLevelData, which credits whatever day this
        // actually is, not "today").
        private int dailyPicksDay = -1;

        public int CurrentLevel { get { return currentLevel; } }

        /// <summary>Which route the level in play was opened by. Drives the header text and what
        /// prev/next walk through, on both the gameplay screen and the level-complete overlay.</summary>
        public LevelSource CurrentSource { get { return currentSource; } }

        /// <summary>Whether the level in play is one of today's daily challenges. Read by
        /// GamePlayController.SaveLevelData to credit the day, and by LevelCompletePage to show
        /// the streak banner.</summary>
        public bool IsDailyChallenge { get { return currentSource == LevelSource.Daily; } }

        /// <summary>The calendar day (absolute day index) the daily level in play was drawn for.
        /// -1 unless <see cref="IsDailyChallenge"/>.</summary>
        public int DailyDayIndex { get { return dailyPicksDay; } }

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
        /// granted any, and does nothing to one that has. Keyed on the balance still sitting at
        /// -1 (see SaveData.hintsRemaining) rather than on it being 0, so a player who has spent
        /// every hint is not handed a fresh set on the next level load.</summary>
        private void EnsureHintBalance()
        {
            SaveData data = SavingSystem.Instance.Load();
            if (data.hintsRemaining >= 0) { return; }

            data.hintsRemaining = Mathf.Max(0, startingHintCount);
            SavingSystem.Instance.Save(data);
        }

        /// <summary>Adds to the player's hint balance -- the rewarded-ad payout, as opposed to
        /// <see cref="EnsureHintBalance"/>'s one-time opening grant. Whoever awards the hint (the
        /// ad flow) still leaves refreshing the on-screen pill to its own caller.</summary>
        public void GrantHint(int amount = 1)
        {
            if (amount <= 0) { return; }

            SaveData data = SavingSystem.Instance.Load();
            data.hintsRemaining += amount;
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

        /// <summary>Puts the first-encounter card for one mechanic on screen -- see
        /// MechanicIntroPage. Composed here beside ShowWarning for the same reason: this class
        /// owns what the screens say, and a page is reached through PageManager rather than
        /// through a second serialized reference.</summary>
        public void ShowMechanicIntro(string mechanicKey)
        {
            MechanicIntroPage card = PageManager.Instance.Get<MechanicIntroPage>(PageType.MechanicIntro);
            if (card == null) { return; }

            card.SetMechanic(mechanicKey);
            PageManager.Instance.OpenAsOverlay(PageType.MechanicIntro);
        }

        /// <summary>Opens the guide on every mechanic the board being played has a card for --
        /// the header's info button. A different page from the first-encounter card on purpose:
        /// see MechanicGuidePage. Silently does nothing on a board with nothing to list.</summary>
        public void ShowMechanicGuideForCurrentBoard()
        {
            string[] keys = GamePlayController.Instance == null
                ? null
                : GamePlayController.Instance.MechanicsWithIntro;
            if (keys == null || keys.Length == 0) { return; }

            MechanicGuidePage guide = PageManager.Instance.Get<MechanicGuidePage>(PageType.MechanicGuide);
            if (guide == null || !guide.HasAnythingFor(keys)) { return; }

            guide.SetMechanics(keys);
            PageManager.Instance.OpenAsOverlay(PageType.MechanicGuide);
        }

        /// <summary>What the hint button says when it is tapped with nothing left to spend.</summary>
        public string NoHintsMessage { get { return "No More Hints"; } }

        /// <summary>What the hint button says when the rewarded ad it offered instead of a
        /// balance could not be shown.</summary>
        public string AdNotAvailableMessage { get { return "Ad Not Available. Try Again Later."; } }

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
        /// ("CLASSIC 6 x 6"), for a daily challenge the calendar date and board size
        /// ("SEP 18  ·  7 x 7") -- there is only ever one challenge a day now, so there is no
        /// position within a list left to report.</summary>
        public string LevelHeaderSubtitle
        {
            get
            {
                if (currentSource == LevelSource.Daily)
                {
                    string date = DailyDateLabel(dailyPicksDay);
                    return dailyPackSize > 0 ? date + "  ·  " + dailyPackSize + " × " + dailyPackSize : date;
                }

                string size = currentPackSize > 0 ? currentPackSize + " × " + currentPackSize : "";
                string mode = CurrentMode.ToString().ToUpperInvariant();
                return size.Length == 0 ? mode : mode + " " + size;
            }
        }

        /// <summary>"SEP 18" for the calendar day <paramref name="absoluteDayIndex"/> days after
        /// DailyChallengeSelector.EpochUtc -- the short date form every daily header/step label
        /// uses in place of the old "N OF total" position, now that a day holds one challenge.</summary>
        private static string DailyDateLabel(int absoluteDayIndex)
        {
            return DailyChallengeSelector.EpochUtc.AddDays(absoluteDayIndex)
                .ToString("MMM d", System.Globalization.CultureInfo.InvariantCulture).ToUpperInvariant();
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
                    DailyChallengeCalendar.DailyLevelPick prev =
                        DailyChallengeCalendar.LevelForAbsoluteDay(dailyPicksDay - 1);
                    return DailyDateLabel(dailyPicksDay - 1) + "  ·  " + prev.packSize + "×" + prev.packSize;
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
                    return HasPrevLevel ? DailyDateLabel(dailyPicksDay - 1) : "";
                }

                if (currentLevel <= 1) { return ""; }

                return "LEVEL " + (currentLevel - 1);
            }
        }

        public string NextStepSubtitle
        {
            get
            {
                if (currentSource == LevelSource.Daily)
                {
                    return HasNextLevel ? DailyDateLabel(dailyPicksDay + 1) : "";
                }

                if (currentLevel >= TotalLevelCount) { return ""; }

                return "LEVEL " + (currentLevel + 1);
            }
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
                        DailyChallengeCalendar.DailyLevelPick next =
                            DailyChallengeCalendar.LevelForAbsoluteDay(dailyPicksDay + 1);
                        return DailyDateLabel(dailyPicksDay + 1) + "  ·  " + next.packSize + "×" + next.packSize;
                    }

                    return "LEVEL " + (currentLevel + 1)
                         + (currentPackSize > 0 ? "  ·  " + currentPackSize + "×" + currentPackSize : "");
                }

                if (currentSource == LevelSource.Daily) { return "DAY COMPLETE"; }

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
                // Any earlier calendar day can always be revisited -- a daily run is never
                // "played in order" the way a pack is, so there is no frontier to check here. But a
                // day already completed is a closed record (same rule the calendar screen itself
                // enforces), so it is never offered as somewhere Prev can step to -- Pack/Advanced
                // levels are untouched by this, they keep working exactly as before.
                if (currentSource == LevelSource.Daily)
                {
                    if (dailyPicksDay <= 0) { return false; }
                    return !DailyChallengeSystem.Instance.Load().IsDayCompleted(dailyPicksDay - 1);
                }
                return currentLevel > 1;
            }
        }

        public bool HasNextLevel
        {
            get
            {
                // Stepping forward through the calendar only ever reaches as far as TODAY -- a
                // future day's board is not unlocked yet, whether or not the day currently on
                // screen has been solved. A day already completed is likewise never offered, same
                // rule as HasPrevLevel above.
                if (currentSource == LevelSource.Daily)
                {
                    if (dailyPicksDay < 0 || dailyPicksDay >= DailyChallengeSelector.DayIndex(System.DateTime.UtcNow)) { return false; }
                    return !DailyChallengeSystem.Instance.Load().IsDayCompleted(dailyPicksDay + 1);
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
                LoadDailyChallengeForDay(dailyPicksDay - 1);
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
                LoadDailyChallengeForDay(dailyPicksDay + 1);
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
        //
        // A curated calendar (see DailyChallengeCalendar), not a pick drawn from the Classic
        // packs: every player sees the same board on the same date, and there is nothing left to
        // "cache" per day the way the older per-install/skill-banded design needed to (see
        // EnsureTodayDailyPicks/LoadDailyChallenge(slot) in an earlier revision of this file).

        /// <summary>Opens today's daily challenge -- the main menu's Daily Challenge button, and
        /// the calendar hub's default selection when it first opens.</summary>
        public void LoadTodaysDailyChallenge()
        {
            LoadDailyChallengeForDay(DailyChallengeSelector.DayIndex(System.DateTime.UtcNow));
        }

        /// <summary>Opens the single Classic level the calendar assigns to
        /// <paramref name="absoluteDayIndex"/> (days since DailyChallengeSelector.EpochUtc) --
        /// see DailyChallengeCalendar.LevelForAbsoluteDay for how a day maps to a board.
        ///
        /// Deliberately does NOT call SetMode/SetPack: those exist to switch which PACK the level
        /// grid/pack progress point at, and a daily level is never a pack level -- it lives in its
        /// own Resources/Levels/Daily folder, under a level NUMBER that may collide with an
        /// unrelated Classic pack level of the same size (see LoadDailyLevelAsset). Touching
        /// currentPackSize/ProgressKey here would risk writing a daily's level number into the
        /// wrong pack's progress the next time a level completes.</summary>
        public void LoadDailyChallengeForDay(int absoluteDayIndex)
        {
            DailyChallengeCalendar.DailyLevelPick pick = DailyChallengeCalendar.LevelForAbsoluteDay(absoluteDayIndex);

            currentSource = LevelSource.Daily;
            dailyPicksDay = absoluteDayIndex;
            dailyPackSize = pick.packSize;

            // GameplayPage reads CurrentMode to decide whether to show the mechanics-info button
            // (Advanced only) -- every daily level is Classic (see DailyChallengeCalendar), so
            // this has to say so even though SetMode itself is not called. Bypasses SetMode's own
            // pack-grid rebuild/validation, which a daily run has no use for.
            CurrentMode = GameMode.Classic;

            LoadDailyLevelAsset(pick.packSize, pick.levelNumber);
        }

        /// <summary>The daily-challenge counterpart to LoadCurrentModeLevel: loads a level from
        /// Resources/Levels/Daily/{packSize}x{packSize}/Level_{levelNumber} instead of the current
        /// pack's own folder, and -- unlike LoadCurrentModeLevel -- never touches currentLevel's
        /// meaning as a pack position, since a daily level is not one.</summary>
        private void LoadDailyLevelAsset(int packSize, int levelNumber)
        {
            currentLevel = levelNumber;

            GamePlayController.Instance.ResetGameplay();

            if (currentLevelDataAsset != null) { Resources.UnloadAsset(currentLevelDataAsset); }

            string path = "Levels/Daily/" + packSize + "x" + packSize + "/Level_" + levelNumber;
            currentLevelDataAsset = Resources.Load<SingleLevelDataSO>(path);

            if (currentLevelDataAsset == null)
            {
                Debug.LogError("UIController: No daily level asset at Resources/" + path + ".");
                return;
            }

            currentLevelData = currentLevelDataAsset.levelData;

            PageManager.Instance.CloseOverlay(PageType.LevelComplete);
            PageManager.Instance.OpenPage(PageType.Gameplay);

            GamePlayController.Instance.GenerateBoard(currentLevelData);

            GameplayPage page = GameplayScreen;
            if (page != null) { page.Refresh(); }
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
        /// <param name="hintsUsedThisAttempt">Hints used since this attempt began -- see
        /// GamePlayController.hintsUsedThisAttempt.</param>
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

            // With the sheet, not with the last pair joined: PathComplete already sounds for that,
            // and the two would land on top of each other.
            AudioManager.Instance.PlaySFX(SoundType.LevelComplete);
            Haptics.Play(HapticType.Success);
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
