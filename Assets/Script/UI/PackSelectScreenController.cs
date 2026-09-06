using System.Collections.Generic;
using UnityEngine;
using TMPro;
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
    public class PackSelectScreenController : MonoBehaviour
    {
        [SerializeField] private PackCard packCardPrefab;
        [SerializeField] private Transform packCardParent;

        [Header("Header")]
        [SerializeField] private TextMeshProUGUI headerTitleText;
        [SerializeField] private TextMeshProUGUI headerSubtitleText;

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

            if (headerTitleText != null) { headerTitleText.text = mode.ToString().ToUpperInvariant(); }

            // Summed across every pack size in the mode, not just whichever one is currently
            // selected -- matches the Menu screen's own mode-card progress (also a sum, not a
            // single representative size), per the user's own explicit clarification there.
            if (headerSubtitleText != null) { headerSubtitleText.text = totalCompleted + "/" + totalLevels + " COMPLETE"; }
        }
    }
}
