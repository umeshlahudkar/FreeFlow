using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FreeFlow.Enums;
using FreeFlow.GamePlay;
using FreeFlow.Input;
using FreeFlow.Util;

namespace FreeFlow.UI
{
    /// <summary>
    /// Manages the UI elements and controls the flow of the game
    /// </summary>
    public class UIController : Singleton<UIController>
    {
        [Header("Menu Screen")]
        [SerializeField] private LevelScreenController levelScreenController;
        [SerializeField] private GameObject mainMenuScreen;
        [SerializeField] private BoardGenerator boardGenerator;

        [Header("Game over Screen")]
        [SerializeField] private GameObject gameOverScreen;
        [SerializeField] private TextMeshProUGUI gameOverMsgText;
        [SerializeField] private TextMeshProUGUI gameOverLevelText;

        [Header("Gameplay")]
        [SerializeField] private TextMeshProUGUI gameplaylevelText;
        [SerializeField] private TextMeshProUGUI gameplayPairText;
        [SerializeField] private TextMeshProUGUI gameplayMoveText;

        // Names the mechanic(s) the current level actually contains. Derived from the level
        // data every load rather than authored per level, so it cannot drift out of step with
        // the board -- see DescribeMechanics.
        [SerializeField] private TextMeshProUGUI gameplayMechanicText;

        [SerializeField] private GameObject gameplayScreen;

        // Turned off on any level with no stored answer -- the legacy Advanced 1-45 boards predate
        // the column and carry none. Left visible but not interactable rather than hidden, so the
        // header does not reshuffle itself between levels.
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

        // Which pack the level screen is showing. 0 means the LEGACY linear campaign -- the older
        // Classic 1-100 and Advanced 1-45 -- which still exists and still has its own progress.
        [SerializeField] private int currentPackSize = 6;

        // Which campaign to open on. Serialised because nothing calls SetMode yet: without it the
        // mode is fixed at Classic in code and the Advanced packs cannot be reached at all, which
        // reads in play as "the mechanics are missing" when they are simply in the other folder.
        [SerializeField] private GameMode startingMode = GameMode.Classic;

        [Header("Pause screen")]
        [SerializeField] GameObject pauseScreen;

        [Header("Setting screen")]
        [SerializeField] GameObject settingScreen;

        private LevelData currentLevelData;
        private SingleLevelDataSO currentLevelDataAsset;
        private int currentLevel;

        public int CurrentLevel { get { return currentLevel; } }

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
                GamePlayController.Instance.ResetGameplay();
                boardGenerator.ResetBoard();

                currentLevel = levelNumber;

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

                levelScreenController.gameObject.SetActive(false);
                gameplayScreen.SetActive(false);
                mainMenuScreen.SetActive(false);

                gameOverScreen.SetActive(false);
                gameplayScreen.SetActive(true);

                boardGenerator.GenerateBoard(currentLevelData);

                gameplaylevelText.text = "Level : " + levelNumber;
                UpdateMechanicLabel(currentLevelData);
                UpdateFilledCells();
                UpdateMovesCount(0);

                // After GenerateBoard, which is what hands the level's answer over.
                if (hintButton != null)
                {
                    hintButton.interactable = GamePlayController.Instance.HintAvailable;
                }
            }
        }

        private void UpdateMechanicLabel(LevelData data)
        {
            if (gameplayMechanicText == null) { return; }
            gameplayMechanicText.text = "Mechanic : " + DescribeMechanics(data);
        }

        /// <summary>
        /// Names the mechanics present in <paramref name="data"/>, read straight off the level's
        /// own cells and constraints. Derived rather than authored on purpose: a hand-written
        /// label would be one more field to forget when a board changes, and this cannot
        /// disagree with what the player is looking at. Levels with no mechanic read "Basic",
        /// and a board carrying more than one lists them all.
        /// </summary>
        private static string DescribeMechanics(LevelData data)
        {
            bool blocked = false, walls = false, checkpoint = false, forbidden = false;
            bool oneWay = false;
            bool arrow = false, bridge = false;
            bool sharedGoal = false, permitted = false;

            if (data.gridRows != null)
            {
                for (int i = 0; i < data.gridRows.Length; i++)
                {
                    GridRow row = data.gridRows[i];

                    if (row.blockType != null)
                    {
                        for (int j = 0; j < row.blockType.Length; j++)
                        {
                            switch (row.blockType[j])
                            {
                                case BlockType.Blocked: blocked = true; break;
                                case BlockType.Checkpoint: checkpoint = true; break;
                                case BlockType.ForbiddenForPair: forbidden = true; break;
                                case BlockType.AllowedForPairs: permitted = true; break;
                                case BlockType.OneWay: oneWay = true; break;
                                case BlockType.Arrow: arrow = true; break;
                                case BlockType.Bridge: bridge = true; break;
                            }
                        }
                    }

                    // secondPairId is the only one of the three a permission rule also reads, so
                    // it is the only one that needs the guard -- see Block.SecondIdNamesAPair.
                    if (row.secondPairId != null)
                    {
                        for (int j = 0; j < row.secondPairId.Length; j++)
                        {
                            if (row.secondPairId[j] == 0) { continue; }

                            bool namesAPair = row.blockType != null
                                           && j < row.blockType.Length
                                           && Block.SecondIdNamesAPair(row.blockType[j]);
                            if (!namesAPair) { sharedGoal = true; }
                        }
                    }

                    if (HasAnyNonZero(row.thirdPairId) || HasAnyNonZero(row.fourthPairId))
                    {
                        sharedGoal = true;
                    }

                    if (row.wallMask != null)
                    {
                        for (int j = 0; j < row.wallMask.Length; j++)
                        {
                            if (row.wallMask[j] != 0) { walls = true; }
                        }
                    }
                }
            }

            string description = string.Empty;
            AppendMechanic(ref description, blocked, "Blocked cell");
            AppendMechanic(ref description, walls, "Wall");
            AppendMechanic(ref description, oneWay, "One-way");
            AppendMechanic(ref description, arrow, "Arrow");
            AppendMechanic(ref description, forbidden, "Forbidden cell");
            AppendMechanic(ref description, permitted, "Permitted colours");
            AppendMechanic(ref description, bridge, "Bridge");
            AppendMechanic(ref description, sharedGoal, "Shared destination");
            AppendMechanic(ref description, checkpoint, "Checkpoint");

            return description.Length > 0 ? description : "Basic";
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

        private static void AppendMechanic(ref string description, bool present, string name)
        {
            if (!present) { return; }
            description = description.Length > 0 ? description + " + " + name : name;
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

        /// <summary>
        /// Gets called when Play button click from main menu,
        /// activates level screen
        /// </summary>
        public void OnPlayButtonClick()
        {
            if (InputManager.Instance.CanInput())
            {
                AudioManager.Instance.PlayButtonClickSound();
                mainMenuScreen.SetActive(false);
                //levelScreenController.LoadLevelScreen(levelDataSO.levels.Length);

                levelScreenController.gameObject.Activate();
            }
        }

        public void OnLevelScreenBackButtonClick()
        {
            if (InputManager.Instance.CanInput())
            {
                AudioManager.Instance.PlayButtonClickSound();
                levelScreenController.gameObject.Deactivate(0.25f, () => mainMenuScreen.SetActive(true));
            }
        }

        public void OnPauseButtonClick()
        {
            if (InputManager.Instance.CanInput())
            {
                AudioManager.Instance.PlayButtonClickSound();
                GamePlayController.Instance.GameState = Enums.GameState.Paused;
                pauseScreen.Activate();
            }
        }

        public void OnResumeButtonClick()
        {
            if (InputManager.Instance.CanInput())
            {
                AudioManager.Instance.PlayButtonClickSound();
                pauseScreen.Deactivate(0.25f, () => GamePlayController.Instance.GameState = Enums.GameState.Playing);
            }
        }

        public void OnPauseScreenRetryButtonClick()
        {
            if (InputManager.Instance.CanInput())
            {
                AudioManager.Instance.PlayButtonClickSound();
                GamePlayController.Instance.ResetGameplay();
                boardGenerator.ResetBoard();
                pauseScreen.Deactivate(0.25f, () => LoadLevel(currentLevel));
            }
        }

        public void OnPauseScreenHomeButtonClick()
        {
            if (InputManager.Instance.CanInput())
            {
                AudioManager.Instance.PlayButtonClickSound();
                GamePlayController.Instance.ResetGameplay();
                boardGenerator.ResetBoard();

                gameOverScreen.SetActive(false);
                gameplayScreen.SetActive(false);

                pauseScreen.Deactivate(0.25f, () => mainMenuScreen.SetActive(true));
            }
        }

        /// <summary>
        ///  Gets called when Quit button click from the Main menu screen,
        ///  closes the game
        /// </summary>
        public void OnQuitButtonClick()
        {
            if (InputManager.Instance.CanInput())
            {
                AudioManager.Instance.PlayButtonClickSound();
                Application.Quit();
            }
        }

        /// <summary>
        /// Activates level complete screen,
        /// Updates move count on level screen
        /// </summary>
        /// <param name="movesCount"></param>
        public void ActivateLevelCompleteScreen(int movesCount)
        {
            gameOverScreen.SetActive(true);
            gameOverMsgText.text = "Congrats!, You Completed the level in " + movesCount + " moves.";
            gameOverLevelText.text = "Level " + currentLevel;

            gameOverScreen.Activate();
        }

        public void OnGameOverScreenRetryButtonClick()
        {
            if (InputManager.Instance.CanInput())
            {
                AudioManager.Instance.PlayButtonClickSound();
                GamePlayController.Instance.ResetGameplay();
                boardGenerator.ResetBoard();

                gameOverScreen.Deactivate(0.25f, () => LoadLevel(currentLevel));
            }
        }

        public void OnGameOverScreenHomeButtonClick()
        {
            if (InputManager.Instance.CanInput())
            {
                AudioManager.Instance.PlayButtonClickSound();
                GamePlayController.Instance.ResetGameplay();
                boardGenerator.ResetBoard();

                pauseScreen.SetActive(false);
                gameplayScreen.SetActive(false);

                gameOverScreen.Deactivate(0.25f, () => mainMenuScreen.SetActive(true));
            }
        }

        public void OnGameOverScreenNextButtonClick()
        {
            if (InputManager.Instance.CanInput())
            {
                AudioManager.Instance.PlayButtonClickSound();
                GamePlayController.Instance.ResetGameplay();
                boardGenerator.ResetBoard();

                gameOverScreen.Deactivate(0.25f, ()=>LoadNextLevel());
            }
        }

        /// <summary>
        /// Joins one pair along the level's own answer. One hint, one pair, no limit on how many
        /// times it can be used -- the only cost is the move it adds, and a player who taps it for
        /// every pair has asked to be shown the board rather than to play it.
        ///
        /// A hint that finds nothing to do is silent by design: the board is either already correct
        /// or has no stored answer, and in the second case the button is not interactable anyway.
        /// </summary>
        public void OnHintButtonClick()
        {
            if (InputManager.Instance.CanInput())
            {
                AudioManager.Instance.PlayButtonClickSound();
                GamePlayController.Instance.TryApplyHint();
            }
        }

        public void OnSeetingButtonClick()
        {
            if (InputManager.Instance.CanInput())
            {
                AudioManager.Instance.PlayButtonClickSound();
                settingScreen.Activate();
            }
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

            string label = "Cells : " + controller.FilledCellCount + "/" + controller.UsableCellCount;

            // Checkpoints get their own count because they are the one rule the board does not
            // show as satisfied on its own: a filled cell looks identical whether the colour
            // crossing it is the one the checkpoint named or not. Hidden entirely on levels
            // without the mechanic rather than shown as "0/0", which would read as a goal the
            // player has failed to start.
            int checkpoints = controller.CheckpointCellCount;
            if (checkpoints > 0)
            {
                label += "   Checkpoints : " + controller.SatisfiedCheckpointCount + "/" + checkpoints;
            }

            gameplayPairText.text = label;
        }

        /// <summary>
        /// Update and shows the completed moves count, basically on game screen
        /// </summary>
        /// <param name="moves">Number of moves</param>
        public void UpdateMovesCount(int moves)
        {
            gameplayMoveText.text = "Moves : " + moves;
        }
    }
}
