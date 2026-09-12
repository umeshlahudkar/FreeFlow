using System.Collections.Generic;
using UnityEngine;
using FreeFlow.Enums;

namespace FreeFlow.UI
{
    /// <summary>
    /// Populates the pack-select screen (02_level_select) -- one PackCard row per board size in
    /// the current mode (UIController.PackSizesFor), plus the header. Every pack is always open
    /// (no pack-level locking exists here -- level-within-a-pack locking on the Levels screen is
    /// a separate, unrelated concept and is untouched). (The "all packs" footer summary this
    /// screen used to have was removed -- no longer part of the design.)
    /// </summary>
    public class PackSelectPage : Page
    {
        [SerializeField] private PackCard packCardPrefab;
        [SerializeField] private Transform packCardParent;
        [SerializeField] private TopPanel topPanel;

        private readonly List<PackCard> spawnedCards = new List<PackCard>();

        private void OnEnable()
        {
            Refresh();
        }

        public void Refresh()
        {
            var ui = UIController.Instance;
            GameMode mode = ui.CurrentMode;
            int[] sizes = ui.PackSizesFor(mode);
            SaveData data = SavingSystem.Instance.Load();

            foreach (PackCard card in spawnedCards)
            {
                if (card != null) { Destroy(card.gameObject); }
            }
            spawnedCards.Clear();

            int totalCompleted = 0;
            int totalLevels = 0;

            for (int i = 0; i < sizes.Length; i++)
            {
                int size = sizes[i];
                string key = ui.KeyFor(mode, size);
                int completed = data.CompletedLevelForKey(key);
                int total = ui.PackLevelCountFor(size);

                totalCompleted += completed;
                totalLevels += total;

                PackCard card = Instantiate(packCardPrefab, packCardParent);
                card.gameObject.SetActive(true);
                card.SetDetails(size, completed, total);
                spawnedCards.Add(card);
            }

            // Summed across every pack size in the mode, not just whichever one is currently
            // selected -- matches the Menu screen's own mode-card progress (also a sum, not a
            // single representative size), per the user's own explicit clarification there.
            if (topPanel != null)
            {
                topPanel.SetTopPanel(mode.ToString().ToUpperInvariant(), totalCompleted + "/" + totalLevels + " COMPLETE");
            }
        }

        /// <summary>Called by a PackCard when it is tapped. Switches to that pack and hands off
        /// to the level grid screen.
        ///
        /// Deliberately has NO CanInput() gate of its own (unlike most On*Click methods) -- its
        /// only caller, PackCard.OnCardClick, already gates on CanInput() before calling this, the
        /// same division LevelButton.OnButtonClick/UIController.LoadLevel already uses. A second
        /// gate here would consume CanInput()'s own one-shot debounce a second time in the same
        /// call stack (CanInput() disables input for the next 0.25s as a side effect of returning
        /// true), so this method would ALWAYS silently no-op -- CanInput() was already spent by
        /// OnCardClick's own check by the time this runs. That exact bug shipped here until
        /// 2026-09-06: tapping any pack card appeared to do nothing.</summary>
        public void OnPackSelected(int packSize)
        {
            UIController.Instance.SetPack(packSize);
            PageManager.Instance.OpenPage(PageType.Levels);
        }
    }
}
