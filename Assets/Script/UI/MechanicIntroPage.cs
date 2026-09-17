using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FreeFlow.Enums;
using FreeFlow.GamePlay;

namespace FreeFlow.UI
{
    /// <summary>
    /// The card that teaches ONE mechanic the first time a player meets it: its name, its rule, and
    /// a small board tracing the mechanic being used -- and, first, being refused.
    ///
    /// Deliberately one mechanic and not a list. This opens by itself, in the middle of a level
    /// load, because something on the board is new; answering that with a catalogue of everything
    /// the board contains buries the one thing the player is being interrupted for. The guide
    /// behind the info button is the list (see <see cref="MechanicGuidePage"/>); this is the
    /// interruption, and it stays about the one mechanic that caused it.
    ///
    /// An overlay rather than something drawn on the real board, for three reasons. The real
    /// board's demonstration would BE part of its solution, which is what the hint is for and what
    /// the hint charges for. A card controls its own framing, so the demo is legible with big cells
    /// rather than lost in the corner of a 9x9 under a thumb. And a card can be re-opened later; a
    /// one-shot animation on the board cannot.
    ///
    /// Unlike WarningNotifier it BLOCKS input -- its backdrop is a raycast target, so the board
    /// underneath cannot be drawn on while the card is up -- and it has no timer. This one is
    /// asking for attention, not reporting something in passing, so it waits to be dismissed.
    /// </summary>
    public class MechanicIntroPage : Page
    {
        [Header("Content")]
        [SerializeField] private TextMeshProUGUI titleText;
        [SerializeField] private TextMeshProUGUI ruleText;

        // Every card there is. Shared with the guide page rather than duplicated -- see the asset.
        [SerializeField] private MechanicIntroCatalog catalog;

        [Header("Demo")]
        // The animated board. Switched off entirely for a card that only describes.
        [SerializeField] private MechanicDemoView demoView;

        // Stands in for the board on a description-only card -- see MechanicIntroSO.HasDemo.
        [SerializeField] private Image iconImage;

        private MechanicIntroSO current;

        /// <summary>Whether a card exists for <paramref name="mechanicKey"/>. Asked before a level
        /// load decides to interrupt itself: a mechanic with no card authored yet must not stop the
        /// player with an empty one, and must not be treated as taught either.</summary>
        public bool HasIntroFor(string mechanicKey)
        {
            return catalog != null && catalog.Find(mechanicKey) != null;
        }

        /// <summary>Points the card at one mechanic. Called BEFORE PageManager opens it -- the same
        /// order WarningNotifier's SetMessage uses, because only PageManager puts a page on
        /// screen.</summary>
        public void SetMechanic(string mechanicKey)
        {
            if (catalog == null) { return; }

            MechanicIntroSO intro = catalog.Find(mechanicKey);
            if (intro == null) { return; }

            current = intro;
        }

        public override void Open()
        {
            base.Open();

            if (current == null) { return; }

            if (titleText != null) { titleText.text = current.title; }
            if (ruleText != null) { ruleText.text = current.rule; }

            // After base.Open(): the demo measures itself to lay out, and an inactive RectTransform
            // has no size to measure.
            bool animated = current.HasDemo;

            if (demoView != null)
            {
                demoView.gameObject.SetActive(animated);
                if (animated) { demoView.Play(current); }
            }

            if (iconImage != null)
            {
                iconImage.sprite = current.icon;
                iconImage.gameObject.SetActive(!animated && current.icon != null);
            }
        }

        public override void Close()
        {
            if (demoView != null) { demoView.Stop(); }
            base.Close();
        }

        /// <summary>The GOT IT button, and the backdrop. Closes through PageManager rather than
        /// deactivating itself -- a page must not put itself away behind the manager's back.</summary>
        public void OnDismissClick()
        {
            AudioManager.Instance.PlaySFX(SoundType.ButtonClick);
            PageManager.Instance.CloseOverlay(PageType.MechanicIntro);
        }
    }
}
