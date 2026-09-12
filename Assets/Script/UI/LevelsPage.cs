using FreeFlow.Util;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using TMPro;

namespace FreeFlow.UI
{
    /// <summary>
    /// Handles the spawning and management of level buttons on the level grid screen (07_levels) --
    /// one specific pack's 100 levels, paged 30-at-a-time. Populating and paging the grid for
    /// whichever pack UIController.CurrentPackSize points at; picking WHICH pack is
    /// PackSelectScreenController's job (02_level_select).
    ///
    /// Exactly TWO physical stage containers ever exist (currentStageGO/incomingStageGO), never
    /// one per page -- a page's stage container and its LevelButtons only exist while that page
    /// is actually the current page or is being dragged/tweened into that role. Paging (swipe or
    /// tab jump) reassigns which page each of the two GameObjects represents and repositions them
    /// to fake the slide, rather than creating a stage per page and shuffling many of them at
    /// once. Worst case is therefore fixed at 2 stage containers and 2 pages worth of LevelButtons
    /// (60), regardless of how many pages a pack has.
    /// </summary>
    public class LevelsPage : Page
    {
        [SerializeField] private LevelButton levelButtonPrefab;
        [SerializeField] private GameObject levelStagePrefab;
        [SerializeField] private TopPanel topPanel;

        [Header("Stage Tabs")]
        [SerializeField] private Button[] stageTabButtons;
        [SerializeField] private Image[] stageTabImages;
        [SerializeField] private TextMeshProUGUI[] stageTabTexts;
        [SerializeField] private Sprite stageTabOnSprite;
        [SerializeField] private Sprite stageTabOffSprite;
        [SerializeField] private Color stageTabOnTextColor = new Color(0.0588f, 0.6196f, 0.5333f, 1f);
        [SerializeField] private Color stageTabOffTextColor = new Color(0.4039f, 0.4706f, 0.5529f, 1f);
        private bool tabsWired;

        private int levelButtonPerScreen = 30;
        private int totalPages;

        private ObjectPool<LevelButton> objectPool;

        // The only two stage containers that ever exist. currentStageGO always holds
        // currentstageOnScreen; incomingStageGO holds whichever neighbor page is being dragged/
        // tweened into view, or is simply inactive with no page assigned when at rest.
        private GameObject currentStageGO;
        private GameObject incomingStageGO;
        private int incomingStagePage = -1;

        // Keyed by GameObject rather than page number -- there are only ever two keys (the two
        // stage containers above), so this is just "this stage's live buttons", independent of
        // which page it currently represents.
        private Dictionary<GameObject, List<LevelButton>> activeStageButtons = new Dictionary<GameObject, List<LevelButton>>();

        private int totalLevels;
        private float buttonWidth;
        private float horizontalSpacing;
        private float verticalSpacing;
        private float startX;
        private float startY;

        public int currentstageOnScreen;

        private Vector3 clickPosition;
        private Vector3 endPosition;
        private Vector3 prePosition;
        private bool dragDirectionDecided;
        private int committedDragDirection;
        private float lastFrameVelocityX;

        // Fraction of screen width rather than a fixed pixel count, so the same gesture feels
        // consistent across resolutions/aspect ratios -- 0.15 means a drag past 15% of the
        // screen's width commits the page change.
        public float swipeThresholdFraction = 0.15f;

        // A fast flick commits the page change even short of swipeThresholdFraction, same as most
        // native pagers -- in pixels/second of pointer movement measured at release.
        public float swipeSpeed = 1500f;

        private Vector3 stageScreenPosition;


        private void OnEnable()
        {
            SetButtons();
            RefreshHeaderAndProgress();
        }

        /// <summary>
        /// Prepares the level selection screen for <paramref name="totalLevels"/> levels. Every
        /// real pack is 100 levels (4 pages) regardless of mode or board size, so a pack switch
        /// never needs new stage containers -- only the LEVEL BUTTONS are pack-specific (lock/done
        /// state, level numbers), so those are always cleared and refreshed, while the two stage
        /// containers themselves are created once (lazily, on first need) and reused for the life
        /// of this screen.
        /// </summary>
        /// <param name="totalLevels">The total number of levels to create buttons for.</param>
        public void SpawnLevelButtons(int totalLevels)
        {
            this.totalLevels = totalLevels;
            currentstageOnScreen = 0;

            // Pack-specific content always needs a fresh pass, independent of the stage
            // containers themselves, which are kept.
            ReleaseStageButtons(currentStageGO);
            ReleaseStageButtons(incomingStageGO);
            incomingStagePage = -1;
            if (incomingStageGO != null) { incomingStageGO.SetActive(false); }

            totalPages = Mathf.CeilToInt((float) totalLevels / levelButtonPerScreen);

            Rect levelStageRect = levelStagePrefab.GetComponent<RectTransform>().rect;
            float totalButtonSize = levelStageRect.width * 0.7f;
            buttonWidth = totalButtonSize / 5;

            horizontalSpacing = (levelStageRect.width - totalButtonSize) / 6;
            verticalSpacing = (levelStageRect.height - (buttonWidth * 6)) / 7;

            startX = -((levelStageRect.width / 2) - (buttonWidth / 2) - horizontalSpacing);
            startY = (levelStageRect.height / 2) - (buttonWidth / 2) - verticalSpacing;

            stageScreenPosition = levelStagePrefab.transform.localPosition;

            SetupStageTabs();
        }

        private void EnsureCurrentStageExists()
        {
            if (currentStageGO != null) { return; }
            currentStageGO = Instantiate(levelStagePrefab, transform);
            currentStageGO.SetActive(true);
        }

        private void EnsureIncomingStageExists()
        {
            if (incomingStageGO != null) { return; }
            incomingStageGO = Instantiate(levelStagePrefab, transform);
            incomingStageGO.SetActive(true);
        }

        /// <summary>Populates <paramref name="stageGO"/> with LevelButtons for <paramref name="page"/>,
        /// returning whichever buttons it held before to the pool first. This is the only place
        /// LevelButtons are created/reused from the pool -- at most two stages ever call this at
        /// once, so live LevelButton count is bounded at 2 pages (60) regardless of pack size.</summary>
        private void PopulateStagePage(GameObject stageGO, int page)
        {
            if (objectPool == null)
            {
                objectPool = new ObjectPool<LevelButton>(levelButtonPrefab, levelButtonPerScreen, transform);
            }

            ReleaseStageButtons(stageGO);

            SaveData data = SavingSystem.Instance.Load();
            // The pack on screen, not Classic's raw field. Reading `completedLevel` directly
            // meant the Advanced level list showed Classic's progress, and would have shown 5x5's
            // on every pack.
            int completedLevels = data.CompletedLevelForKey(UIController.Instance.ProgressKey);
            int nextLevel = completedLevels + 1;

            List<LevelButton> pageButtons = new List<LevelButton>();
            float currentX = startX;
            float currentY = startY;
            int firstLevelOnPage = page * levelButtonPerScreen + 1;

            for (int j = 0; j < 6; j++)
            {
                for (int k = 0; k < 5; k++)
                {
                    int level = firstLevelOnPage + (j * 5 + k);
                    if (level > totalLevels) { break; }

                    LevelButton button = objectPool.GetObject();
                    button.ThisTransform.SetParent(stageGO.transform, false);
                    button.ThisTransform.localScale = Vector3.one;
                    button.ThisTransform.sizeDelta = new Vector2(buttonWidth, buttonWidth);
                    button.ThisTransform.localPosition = new Vector3(currentX, currentY);

                    LevelTileState state = level <= completedLevels ? LevelTileState.Done
                        : level == nextLevel ? LevelTileState.Current
                        : LevelTileState.Locked;

                    button.SetDetails(level, state);
                    pageButtons.Add(button);

                    currentX += buttonWidth + horizontalSpacing;
                }

                currentX = startX;
                currentY -= buttonWidth + verticalSpacing;
            }

            activeStageButtons[stageGO] = pageButtons;
        }

        private void ReleaseStageButtons(GameObject stageGO)
        {
            if (stageGO == null) { return; }
            if (activeStageButtons.TryGetValue(stageGO, out List<LevelButton> buttons))
            {
                foreach (LevelButton button in buttons) { objectPool?.ReturnObject(button); }
                activeStageButtons.Remove(stageGO);
            }
        }

        private void SetButtons()
        {
            SaveData data = SavingSystem.Instance.Load();
            int nextLevel = data.CompletedLevelForKey(UIController.Instance.ProgressKey) + 1;

            currentstageOnScreen = Mathf.CeilToInt((float)nextLevel / levelButtonPerScreen);
            currentstageOnScreen--;
            currentstageOnScreen = Mathf.Clamp(currentstageOnScreen, 0, Mathf.Max(0, totalPages - 1));

            EnsureCurrentStageExists();
            PopulateStagePage(currentStageGO, currentstageOnScreen);
            currentStageGO.transform.localPosition = stageScreenPosition;

            if (incomingStageGO != null) { incomingStageGO.SetActive(false); }
            incomingStagePage = -1;

            RefreshTabs();
        }

        /// <summary>Header title/subtitle -- everything on this screen that depends on which pack
        /// is showing rather than which stage page is showing. (The progress card and CONTINUE
        /// button this screen used to have were removed -- no longer part of the design.)</summary>
        private void RefreshHeaderAndProgress()
        {
            var ui = UIController.Instance;
            int size = ui.CurrentPackSize;
            SaveData data = SavingSystem.Instance.Load();
            int completed = data.CompletedLevelForKey(ui.ProgressKey);
            int total = ui.TotalLevelCount;

            if (topPanel != null)
            {
                string title = size > 0
                    ? ui.CurrentMode.ToString().ToUpperInvariant() + " " + size + "×" + size
                    : ui.CurrentMode.ToString().ToUpperInvariant();
                topPanel.SetTopPanel(title, completed + "/" + total + " COMPLETE");
            }
        }

        private void Update()
        {
            if (UnityEngine.Input.GetMouseButtonDown(0))
            {
                // Stop any in-flight commit/cancel retire animation from a previous gesture so a
                // quick second swipe doesn't fight leftover tweens on these transforms.
                if (currentStageGO != null) { currentStageGO.transform.DOKill(); }
                if (incomingStageGO != null) { incomingStageGO.transform.DOKill(); }

                clickPosition = UnityEngine.Input.mousePosition;
                prePosition = clickPosition;
                endPosition = clickPosition;
                dragDirectionDecided = false;
                committedDragDirection = 0;
                lastFrameVelocityX = 0f;
            }
            else if (UnityEngine.Input.GetMouseButton(0))
            {
                endPosition = UnityEngine.Input.mousePosition;
                Vector3 totalDelta = endPosition - clickPosition;
                Vector3 frameDelta = endPosition - prePosition;
                float deltaTime = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
                lastFrameVelocityX = frameDelta.x / deltaTime;

                // Re-decided every frame (not just once) so reversing mid-drag swaps which
                // neighbor page is being dragged in, instead of staying committed to whatever
                // direction was first detected even after the finger doubles back past the start.
                if (Mathf.Abs(totalDelta.x) > 0.1f)
                {
                    int desiredDirection = totalDelta.x < 0 ? 1 : -1;
                    if (!dragDirectionDecided || desiredDirection != committedDragDirection)
                    {
                        SetIncomingStageDirection(desiredDirection);
                        committedDragDirection = desiredDirection;
                        dragDirectionDecided = true;
                    }
                }

                // 1:1 with the pointer now (previously halved, which made the drag visibly lag
                // behind the finger/mouse).
                if (Mathf.Abs(frameDelta.x) > 0.01f)
                {
                    Vector3 delta = new Vector3(frameDelta.x, 0, 0);
                    if (currentStageGO != null) { currentStageGO.transform.localPosition += delta; }
                    if (incomingStagePage >= 0) { incomingStageGO.transform.localPosition += delta; }
                }
                prePosition = endPosition;
            }
            else if (UnityEngine.Input.GetMouseButtonUp(0))
            {
                endPosition = UnityEngine.Input.mousePosition;
                float dragDistanceX = Mathf.Abs(endPosition.x - clickPosition.x);
                float distanceThreshold = Screen.width * swipeThresholdFraction;

                // Commits on either a long-enough drag or a fast-enough flick that fell short of
                // the distance threshold -- the same "distance OR velocity" rule most native
                // pagers use, so a quick short swipe still pages instead of always snapping back.
                bool committed = incomingStagePage >= 0
                    && (dragDistanceX > distanceThreshold || Mathf.Abs(lastFrameVelocityX) > swipeSpeed);

                if (committed)
                {
                    CommitIncomingStage();
                }
                else
                {
                    CancelIncomingStage();
                }
                RefreshTabs();
            }
        }

        /// <summary>Sets up (or swaps) the incoming stage for <paramref name="direction"/> (+1 =
        /// next page, -1 = previous page). Tears down whatever incoming stage was set up for a
        /// different direction first, so reversing mid-drag re-targets the other neighbor instead
        /// of leaving a stale one in place.</summary>
        private void SetIncomingStageDirection(int direction)
        {
            if (incomingStagePage >= 0)
            {
                ReleaseStageButtons(incomingStageGO);
                incomingStageGO.SetActive(false);
                incomingStagePage = -1;
            }

            int candidatePage = currentstageOnScreen + direction;
            if (candidatePage < 0 || candidatePage >= totalPages) { return; }

            EnsureIncomingStageExists();
            PopulateStagePage(incomingStageGO, candidatePage);
            incomingStagePage = candidatePage;
            incomingStageGO.SetActive(true);
            incomingStageGO.transform.localPosition = new Vector3(
                stageScreenPosition.x + (Screen.width * direction), stageScreenPosition.y, stageScreenPosition.z);
        }

        /// <summary>The drag went far enough -- the incoming stage becomes the new current stage,
        /// and the old current stage slides the rest of the way off-screen before being released
        /// back to the pool and hidden, rather than popping away instantly (visible whenever a
        /// drag crossed the threshold without reaching the edge of the screen).</summary>
        private void CommitIncomingStage()
        {
            int direction = incomingStagePage > currentstageOnScreen ? 1 : -1;
            GameObject retiring = currentStageGO;

            currentStageGO = incomingStageGO;
            currentstageOnScreen = incomingStagePage;
            incomingStageGO = retiring;
            incomingStagePage = -1;

            currentStageGO.transform.DOKill();
            currentStageGO.transform.DOLocalMove(stageScreenPosition, 0.2f);

            if (retiring != null)
            {
                Vector3 offscreenPosition = new Vector3(
                    stageScreenPosition.x - (Screen.width * direction), stageScreenPosition.y, stageScreenPosition.z);
                retiring.transform.DOKill();
                retiring.transform.DOLocalMove(offscreenPosition, 0.2f).OnComplete(() =>
                {
                    if (retiring == null) { return; }
                    ReleaseStageButtons(retiring);
                    retiring.SetActive(false);
                });
            }
        }

        /// <summary>Drag released without crossing the threshold (or there was no valid neighbor
        /// to drag toward) -- the current stage slides back to center and the incoming stage
        /// slides back to the same off-screen position it started the drag from before being
        /// released and hidden, rather than popping away instantly -- the pop was most visible on
        /// a short drag, where the incoming stage had only peeked a little onto screen.</summary>
        private void CancelIncomingStage()
        {
            if (currentStageGO != null)
            {
                currentStageGO.transform.DOKill();
                currentStageGO.transform.DOLocalMove(stageScreenPosition, 0.2f);
            }

            if (incomingStagePage >= 0)
            {
                int direction = incomingStagePage > currentstageOnScreen ? 1 : -1;
                GameObject retiring = incomingStageGO;
                incomingStagePage = -1;

                Vector3 restPosition = new Vector3(
                    stageScreenPosition.x + (Screen.width * direction), stageScreenPosition.y, stageScreenPosition.z);
                retiring.transform.DOKill();
                retiring.transform.DOLocalMove(restPosition, 0.2f).OnComplete(() =>
                {
                    if (retiring == null) { return; }
                    ReleaseStageButtons(retiring);
                    retiring.SetActive(false);
                });
            }
        }

        /// <summary>Jumps directly to <paramref name="stageIndex"/> -- what the stage tabs (and
        /// OnLeftButtonClick/OnRightButtonClick) call. Slides the old current page out and the
        /// target page in from the appropriate side, the same as a completed swipe, regardless of
        /// how many pages away the target is -- intermediate pages are never instantiated.</summary>
        public void JumpToStage(int stageIndex)
        {
            if (stageIndex < 0 || stageIndex >= totalPages || stageIndex == currentstageOnScreen) { return; }

            int direction = stageIndex > currentstageOnScreen ? 1 : -1;

            EnsureIncomingStageExists();
            PopulateStagePage(incomingStageGO, stageIndex);
            incomingStageGO.SetActive(true);
            incomingStageGO.transform.localPosition = new Vector3(
                stageScreenPosition.x + (Screen.width * direction), stageScreenPosition.y, stageScreenPosition.z);
            incomingStagePage = stageIndex;

            // CommitIncomingStage itself now animates the outgoing stage off-screen and releases
            // it once that finishes, so no separate exit tween is needed here.
            CommitIncomingStage();

            RefreshTabs();
        }

        /// <summary>Wires each stage tab button to jump straight to its page, once per
        /// SpawnLevelButtons call (tab COUNT can change if the pack's level count ever does, so
        /// listeners are re-registered rather than assumed stable).</summary>
        private void SetupStageTabs()
        {
            if (stageTabButtons == null) { return; }
            for (int i = 0; i < stageTabButtons.Length; i++)
            {
                if (stageTabButtons[i] == null) { continue; }
                int captured = i;
                stageTabButtons[i].onClick.RemoveAllListeners();
                stageTabButtons[i].onClick.AddListener(() => JumpToStage(captured));
                stageTabButtons[i].gameObject.SetActive(i < totalPages);
            }
            tabsWired = true;
            RefreshTabs();
        }

        private void RefreshTabs()
        {
            if (!tabsWired || stageTabImages == null) { return; }
            for (int i = 0; i < stageTabImages.Length; i++)
            {
                bool isOn = i == currentstageOnScreen;
                if (stageTabImages[i] != null)
                {
                    stageTabImages[i].sprite = isOn ? stageTabOnSprite : stageTabOffSprite;
                }
                if (stageTabTexts != null && i < stageTabTexts.Length && stageTabTexts[i] != null)
                {
                    stageTabTexts[i].color = isOn ? stageTabOnTextColor : stageTabOffTextColor;
                    stageTabTexts[i].fontStyle = isOn ? FontStyles.Bold : FontStyles.Normal;
                }
            }
        }

        public void OnRightButtonClick()
        {
            JumpToStage(currentstageOnScreen + 1);
        }

        public void OnLeftButtonClick()
        {
            JumpToStage(currentstageOnScreen - 1);
        }
    }
}
