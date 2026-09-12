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
        [Header("Menu Screen")]
        [SerializeField] private LevelsPage levelScreenController;
        [SerializeField] private BoardGenerator boardGenerator;

        [Header("Game over Screen")]
        [SerializeField] private LevelCompletePage levelCompletePage;

        [Header("Gameplay")]
        // Filled-cells fraction ("16/36 CELLS") and percent ("44%") on the HUD progress card.
        // gameplayMoveText no longer shows a move count -- the new HUD has no moves readout, see
        // UpdateMovesCount -- it is repurposed to show the percent instead, so no field/reference
        // needed to go dead.
        [SerializeField] private TextMeshProUGUI gameplayPairText;
        [SerializeField] private TextMeshProUGUI gameplayMoveText;
        [SerializeField] private Slider gameplaySlider;

        // Turned off on any level with no stored answer (nothing shipped today lacks one, but the
        // column has always been optional -- see LevelData.solutionPairId). Left visible but not
        // interactable rather than hidden, so the header does not reshuffle itself between levels.
        [SerializeField] private Button hintButton;

        [Header("Level Data")]
        // Counts are authored metadata, not derived from a loaded array -- each level's grid data
        // lives in its own SingleLevelDataSO under Resources/Levels/<Mode>/, loaded on demand so
        // memory scales with levels visited, not levels that exist. Keep these in sync when adding
        // level assets.
        [SerializeField] private int classicLevelCount;
        [SerializeField] private int advancedLevelCount;

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

        // Whether the level currently loaded is today's daily challenge rather than an ordinary
        // pack level, even though it is the SAME LevelData asset either way -- see LoadDailyChallenge.
        // Reset to false at the top of every LoadLevel call, then set true again immediately after
        // by LoadDailyChallenge, so a plain LoadLevel (picking a level from the pack grid) can never
        // leave a stale daily-challenge flag set. Retry deliberately re-arms it (see
        // OnGameOverScreenRetryButtonClick/OnPauseScreenRetryButtonClick) since a retry is a
        // continuation of the same attempt, not a return to normal browsing.
        private bool isDailyChallenge;

        public int CurrentLevel { get { return currentLevel; } }

        /// <summary>Whether the level in play is today's daily challenge. Read by
        /// GamePlayController.SaveLevelData to credit a completion to the streak, and by
        /// ActivateLevelCompleteScreen to show it.</summary>
        public bool IsDailyChallenge { get { return isDailyChallenge; } }

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

        public int TotalLevelCount
        {
            get
            {
                if (currentPackSize > 0) { return packLevelCount; }
                return CurrentMode == GameMode.Advanced ? advancedLevelCount : classicLevelCount;
            }
        }

        /// <summary>
        /// Identifies whose progress this is. Packs are keyed by mode AND size, because finishing
        /// 5x5 level 20 must not mark 7x7 level 20 complete; the legacy campaigns keep the bare mode
        /// name and, through <see cref="SaveData"/>, their original fields -- so a returning player
        /// mid-way through the old run keeps their place.
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

        public int LevelCountFor(GameMode mode)
        {
            return mode == GameMode.Advanced ? advancedLevelCount : classicLevelCount;
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

            levelScreenController.SpawnLevelButtons(TotalLevelCount);
        }

        /// <summary>
        /// Switches the level screen to another pack. <paramref name="packSize"/> is a board size,
        /// or 0 for the legacy linear campaign.
        /// </summary>
        public void SetPack(int packSize)
        {
            if (currentPackSize == packSize) { return; }
            currentPackSize = packSize;
            levelScreenController.SpawnLevelButtons(TotalLevelCount);
        }

        public int CurrentLevelGoal { get { return currentLevelData.pairCount; } }

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

            levelScreenController.SpawnLevelButtons(TotalLevelCount);
        }

        /// <summary>
        /// Loads the specified game level and initializes relevant UI elements.
        /// </summary>
        /// <param name="levelNumber">The number of the level to load.</param>
        public void LoadLevel(int levelNumber)
        {
            if (levelNumber <= TotalLevelCount)
            {
                // Any DIRECT call defaults to "not the daily challenge" -- LoadDailyChallenge
                // re-arms this immediately after calling here, once the level it picked has
                // actually loaded. See the field's own doc comment for why retry has to restore it.
                isDailyChallenge = false;

                // Set BEFORE ResetGameplay -- it calls BeginAttempt, which records the new
                // attempt against UIController.Instance.CurrentLevel. Reading it after ResetGameplay
                // used to record every level-select jump (as opposed to a retry or "next", which
                // both already point currentLevel at the right level before calling LoadLevel)
                // against whatever level had been playing before, not the one being opened.
                currentLevel = levelNumber;

                GamePlayController.Instance.ResetGameplay();
                boardGenerator.ResetBoard();

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

                boardGenerator.GenerateBoard(currentLevelData);

                // GameplayPage's own OnEnable refreshes its TopPanel title/subtitle -- OpenPage
                // above already triggered it (or will, if Gameplay wasn't already the current
                // page) -- so no header text is set directly here anymore.
                UpdateFilledCells();
                UpdateMovesCount(0);

                // After GenerateBoard, which is what hands the level's answer over.
                if (hintButton != null)
                {
                    // Show/hide is the player's own Settings-screen preference; interactable is
                    // still purely GamePlayController.HintAvailable so a shown-but-unusable button
                    // still refuses a level with no stored answer.
                    hintButton.gameObject.SetActive(SavingSystem.Instance.Load().showHintButton);
                    hintButton.interactable = GamePlayController.Instance.HintAvailable;
                }
            }
        }

        /// <summary>
        /// Loads today's daily challenge -- a real level from an existing pack, picked once per
        /// calendar day and cached so it does not change under the player mid-session (see
        /// DailyChallengeSelector). Classic only for now: it is the default mode and the one whose
        /// packs vary by board size, which is what DailyChallengeSelector rotates through day to
        /// day; Advanced ships one pack size so far, which would make "rotate through pack sizes"
        /// a no-op for it.
        ///
        /// Deliberately reuses the pack's own LevelData rather than a separate daily-only asset --
        /// completing it also completes that pack level, which is correct (it IS that level), and
        /// avoids a second content pipeline for one level a day.
        /// </summary>
        public void LoadDailyChallenge()
        {
            SaveData data = SavingSystem.Instance.Load();
            bool dataChanged = false;

            // Assigned once, ever, on whichever device first opens the daily challenge -- see
            // SaveData.playerSalt and EnsurePlayerSalt. Range's upper bound is exclusive, so this
            // can never generate the 0 that means "unset".
            if (data.playerSalt == 0)
            {
                data.EnsurePlayerSalt(UnityEngine.Random.Range(1, int.MaxValue));
                dataChanged = true;
            }

            int today = DailyChallengeSelector.DayIndex(System.DateTime.UtcNow);

            DailyChallengeSelector.Pick pick;
            if (data.dailyChallengeCachedDay == today)
            {
                pick = new DailyChallengeSelector.Pick
                {
                    mode = data.dailyChallengeMode,
                    packSize = data.dailyChallengePackSize,
                    levelNumber = data.dailyChallengeLevel,
                };
            }
            else
            {
                pick = DailyChallengeSelector.Select(today, GameMode.Classic, PackSizesFor(GameMode.Classic),
                    packLevelCount, data.OverallSkillRating(), data.playerSalt);

                data.dailyChallengeCachedDay = today;
                data.dailyChallengeMode = pick.mode;
                data.dailyChallengePackSize = pick.packSize;
                data.dailyChallengeLevel = pick.levelNumber;
                dataChanged = true;
            }

            if (dataChanged) { SavingSystem.Instance.Save(data); }

            SetMode(pick.mode);
            SetPack(pick.packSize);
            LoadLevel(pick.levelNumber);
            isDailyChallenge = true; // after LoadLevel, which resets this at its own top
        }

        /// <summary>
        /// Gets called when next level button click from the lwvwl win screen,
        /// Handles the next level loading
        /// </summary>
        private void LoadNextLevel()
        {
            currentLevel++;
            if (currentLevel > TotalLevelCount) { currentLevel = 1; }
            LoadLevel(currentLevel);
        }

        /// <summary>Today's daily-challenge pick, WITHOUT persisting anything -- lets the hub
        /// screen preview mode/size/level before the player taps Play. Mirrors LoadDailyChallenge's
        /// own cache-or-select branch exactly, but never writes SaveData (no playerSalt assignment,
        /// no cache write); LoadDailyChallenge remains the only place that actually commits a pick.
        /// </summary>
        public DailyChallengeSelector.Pick PeekTodayDailyChallenge()
        {
            SaveData data = SavingSystem.Instance.Load();
            int today = DailyChallengeSelector.DayIndex(System.DateTime.UtcNow);

            if (data.dailyChallengeCachedDay == today)
            {
                return new DailyChallengeSelector.Pick
                {
                    mode = data.dailyChallengeMode,
                    packSize = data.dailyChallengePackSize,
                    levelNumber = data.dailyChallengeLevel,
                };
            }

            // playerSalt may still be 0 (unassigned) here -- fine for a preview, since it only
            // changes WHICH level within the skill band gets picked; LoadDailyChallenge assigns
            // the real salt before this same Select() call actually commits one.
            return DailyChallengeSelector.Select(today, GameMode.Classic, PackSizesFor(GameMode.Classic),
                packLevelCount, data.OverallSkillRating(), data.playerSalt);
        }

        /// <summary>Whether today's daily challenge has already been completed.</summary>
        public bool IsTodayDailyChallengeSolved()
        {
            SaveData data = SavingSystem.Instance.Load();
            int today = DailyChallengeSelector.DayIndex(System.DateTime.UtcNow);
            return data.dailyChallengeLastCompletedDay == today;
        }

        /// <summary>
        /// Activates the level complete screen and hands it the attempt's real stats (moves,
        /// hints, time, and the pack-progress before this completion -- all read from
        /// GamePlayController, none fabricated). LevelCompletePage owns every field this content
        /// needs and reads everything else (mode, pack size, current level) itself from this
        /// controller's own public properties -- see LevelCompletePage.SetLevelCompleteData.
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
            if (levelCompletePage != null)
            {
                levelCompletePage.SetLevelCompleteData(movesCount, hintsUsedThisAttempt, secondsTaken, oldCompletedLevel);
            }
            PageManager.Instance.OpenAsOverlay(PageType.LevelComplete);
        }

        /// <summary>
        /// Resets gameplay state and reloads the level currently in progress -- what every
        /// "Retry" button (Pause overlay, Level Complete overlay) does. Preserves the
        /// daily-challenge flag across the reload (LoadLevel itself always clears it first) so
        /// retrying a daily challenge still counts as one. Unconditionally closes the Pause
        /// overlay -- safe even when it was never open (see PageManager.CloseOverlay) -- so this
        /// one method works for both callers without either needing to know about the other's
        /// overlay.
        /// </summary>
        public void RetryCurrentLevel()
        {
            bool wasDailyChallenge = isDailyChallenge; // LoadLevel below resets this to false
            GamePlayController.Instance.ResetGameplay();
            boardGenerator.ResetBoard();
            PageManager.Instance.CloseOverlay(PageType.Pause);
            // LoadLevel closes the LevelComplete overlay itself before opening Gameplay.
            LoadLevel(currentLevel);
            if (wasDailyChallenge) { isDailyChallenge = true; }
        }

        /// <summary>
        /// Resets gameplay state and returns to the main menu -- what every "Home" button
        /// (Pause overlay, Level Complete overlay, in-HUD Home) does. Unconditionally closes both
        /// overlays -- safe even when neither was open -- so this one method works from any of
        /// those callers.
        /// </summary>
        public void GoToMainMenu()
        {
            GamePlayController.Instance.ResetGameplay();
            boardGenerator.ResetBoard();

            PageManager.Instance.CloseOverlay(PageType.Pause);
            PageManager.Instance.CloseOverlay(PageType.LevelComplete);
            PageManager.Instance.OpenPage(PageType.MainMenu);
        }

        /// <summary>
        /// Resets gameplay state and advances to the next level -- what the Level Complete
        /// overlay's "Next" button does. LoadNextLevel -> LoadLevel closes the LevelComplete
        /// overlay itself.
        /// </summary>
        public void GoToNextLevel()
        {
            GamePlayController.Instance.ResetGameplay();
            boardGenerator.ResetBoard();
            LoadNextLevel();
        }

        /// <summary>
        /// Shows how much of the board is filled, on the game screen.
        ///
        /// Deliberately cells rather than pairs. Completing a level needs every usable cell
        /// covered, not just every pair joined, so a pair counter reads "4/4" -- the game
        /// announcing the level is done -- while the level refuses to end. Players hit exactly
        /// that and reported it as the game being broken. Cells are the real win condition, so
        /// showing them means the readout can never claim completion the game will not honour.
        /// </summary>
        public void UpdateFilledCells()
        {
            GamePlayController controller = GamePlayController.Instance;
            if (controller == null) { return; }

            int filled = controller.FilledCellCount;
            int usable = controller.UsableCellCount;

            if (gameplayPairText != null) { gameplayPairText.text = filled + "/" + usable + " CELLS"; }
            if (gameplayMoveText != null)
            {
                int percent = usable > 0 ? Mathf.RoundToInt(100f * filled / usable) : 0;
                gameplayMoveText.text = percent + "%";
            }
            if (gameplaySlider != null)
            {
                gameplaySlider.value = usable > 0 ? (float)filled / usable : 0f;
            }
        }

        /// <summary>
        /// The new HUD has no moves readout (see gameplayMoveText, repurposed for the cells
        /// percent) -- kept as a no-op rather than removed so GamePlayController's per-move call
        /// site needs no change if a moves display ever comes back.
        /// </summary>
        public void UpdateMovesCount(int moves)
        {
        }

    }
}
