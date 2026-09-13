using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FreeFlow.UI
{
    /// <summary>
    /// Handles the spawning and management of level buttons on the level grid screen (07_levels) --
    /// one specific pack's levels, for whichever pack UIController.CurrentPackSize points at;
    /// picking WHICH pack is PackSelectPage's job (02_level_select).
    ///
    /// Layout and paging are Unity's own -- a ScrollRect over a GridLayoutGroup -- so the whole
    /// pack is one scrollable grid. This only spawns a tile per level and sets its state; where
    /// each tile lands is the grid's business. Opening the screen scrolls to the level the player
    /// is actually up to rather than starting at level 1.
    /// </summary>
    public class LevelsPage : Page
    {
        [SerializeField] private LevelButton levelButtonPrefab;
        [SerializeField] private ScrollRect levelsScroll;
        [SerializeField] private TopPanel topPanel;

        // Spawned once and reused for the life of the screen -- a pack switch changes only each
        // tile's state (level number, lock/done), never how many tiles there are, so surplus is
        // parked inactive rather than destroyed in case a later pack is longer.
        private readonly List<LevelButton> levelButtons = new List<LevelButton>();

        private int totalLevels;

        private void OnEnable()
        {
            RefreshButtons();
            RefreshHeaderAndProgress();
            StartCoroutine(ScrollToUnlockedLevelWhenLaidOut());
        }

        /// <summary>Waits one frame before scrolling, because this page's own OnEnable runs before
        /// the grid and size fitter underneath it have laid out -- the content still measures zero
        /// height at that point. Forcing a rebuild that early bakes the zero in AND clears the
        /// dirty flag, so the real layout pass never runs afterwards and the list stays stuck at
        /// the top no matter how far into the pack the player is (exactly what it did).</summary>
        private IEnumerator ScrollToUnlockedLevelWhenLaidOut()
        {
            yield return null;

            if (levelsScroll == null || levelsScroll.content == null) { yield break; }

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(levelsScroll.content);
            ScrollToUnlockedLevel();
        }

        /// <summary>
        /// Prepares the level selection screen for <paramref name="totalLevels"/> levels. Only the
        /// LEVEL BUTTONS are pack-specific (lock/done state, level numbers), so a pack switch
        /// refreshes those in place rather than rebuilding the grid.
        /// </summary>
        /// <param name="totalLevels">The total number of levels to create buttons for.</param>
        public void SpawnLevelButtons(int totalLevels)
        {
            this.totalLevels = totalLevels;
            RefreshButtons();
        }

        /// <summary>One tile per level in the pack, in order, each carrying its own lock/current/
        /// done state for the pack currently on screen.</summary>
        private void RefreshButtons()
        {
            if (levelButtonPrefab == null || levelsScroll == null || levelsScroll.content == null) { return; }

            SaveData data = SavingSystem.Instance.Load();
            // The pack on screen, not Classic's raw field. Reading `completedLevel` directly
            // meant the Advanced level list showed Classic's progress, and would have shown 5x5's
            // on every pack.
            int completedLevels = data.CompletedLevelForKey(UIController.Instance.ProgressKey);
            int nextLevel = completedLevels + 1;

            for (int level = 1; level <= totalLevels; level++)
            {
                LevelButton button = ButtonAt(level - 1);

                LevelTileState state = level <= completedLevels ? LevelTileState.Done
                    : level == nextLevel ? LevelTileState.Current
                    : LevelTileState.Locked;

                button.SetDetails(level, state);
            }

            for (int i = totalLevels; i < levelButtons.Count; i++)
            {
                levelButtons[i].gameObject.SetActive(false);
            }
        }

        private LevelButton ButtonAt(int slot)
        {
            while (levelButtons.Count <= slot)
            {
                levelButtons.Add(Instantiate(levelButtonPrefab, levelsScroll.content));
            }

            levelButtons[slot].gameObject.SetActive(true);
            return levelButtons[slot];
        }

        /// <summary>Opens the list at the level the player has unlocked instead of at level 1 --
        /// the grid screen used to do this by opening on the page that held the next level, and a
        /// player deep into a pack should not have to scroll to reach where they left off.</summary>
        private void ScrollToUnlockedLevel()
        {
            if (levelsScroll == null || levelsScroll.content == null || levelsScroll.viewport == null) { return; }
            if (levelButtons.Count == 0) { return; }

            SaveData data = SavingSystem.Instance.Load();
            int nextLevel = data.CompletedLevelForKey(UIController.Instance.ProgressKey) + 1;
            int slot = Mathf.Clamp(nextLevel - 1, 0, levelButtons.Count - 1);

            float viewportHeight = levelsScroll.viewport.rect.height;
            float scrollable = levelsScroll.content.rect.height - viewportHeight;
            if (scrollable <= 0f)
            {
                levelsScroll.verticalNormalizedPosition = 1f;
                return;
            }

            // Tiles are anchored to the content's top-left by the layout group, so anchoredPosition
            // runs negative downward; the pivot term keeps this honest if the tile art is ever
            // re-pivoted away from its centre.
            RectTransform tile = levelButtons[slot].ThisTransform;
            float tileCentreFromTop = -tile.anchoredPosition.y + ((tile.pivot.y - 0.5f) * tile.rect.height);

            float offsetFromTop = Mathf.Clamp(tileCentreFromTop - (viewportHeight * 0.5f), 0f, scrollable);
            levelsScroll.verticalNormalizedPosition = 1f - (offsetFromTop / scrollable);
        }

        /// <summary>Header title/subtitle -- everything on this screen that depends on which pack
        /// is showing. (The progress card and CONTINUE button this screen used to have were
        /// removed -- no longer part of the design.)</summary>
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
    }
}
