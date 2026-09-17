using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FreeFlow.Enums;
using FreeFlow.Input;

namespace FreeFlow.UI
{
    /// <summary>Shared header bar (title, subtitle, back/settings/option buttons) reused across
    /// pages. Not itself a Page -- just routes its buttons through PageManager instead of
    /// UIController. Content and per-button visibility are entirely driven by whichever Page owns
    /// this instance, via <see cref="SetTopPanel"/> -- this component has no opinion of its own
    /// about what a given page's header should show.</summary>
    public class TopPanel : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI titleText;
        [SerializeField] private TextMeshProUGUI subtitleText;
        [SerializeField] private GameObject backButton;
        [SerializeField] private GameObject settingButton;
        [SerializeField] private GameObject optionButton;

        // The "i". A Button rather than a GameObject like the three above, because this one has
        // two states and not one: WHETHER it is on the header is a property of the page (only
        // gameplay shows it, and only in Advanced), while whether it can be TAPPED is a property
        // of the board behind it -- a mechanic with no intro card authored yet has nothing to
        // open. See SetInfoInteractable.
        [SerializeField] private Button infoButton;

        /// <summary>Called by the owning Page (from its own OnEnable/Refresh, same as every other
        /// page-specific header field) to configure this shared header for that page: title/
        /// subtitle text and which of the three buttons should be visible. Defaults match what
        /// every page has used so far -- Back and Setting shown, Option hidden (it currently
        /// duplicates Setting's action and has no distinct use yet).</summary>
        public void SetTopPanel(string title, string subtitle,
            bool showBack = true, bool showSetting = true, bool showOption = false,
            bool showInfo = false)
        {
            if (titleText != null) { titleText.text = title; }
            if (subtitleText != null)
            {
                bool hasSubtitle = !string.IsNullOrEmpty(subtitle);
                subtitleText.gameObject.SetActive(hasSubtitle);
                if (hasSubtitle) { subtitleText.text = subtitle; }
            }
            if (backButton != null) { backButton.SetActive(showBack); }
            if (settingButton != null) { settingButton.SetActive(showSetting); }
            if (optionButton != null) { optionButton.SetActive(showOption); }
            if (infoButton != null) { infoButton.gameObject.SetActive(showInfo); }
        }

        /// <summary>Whether the info button can be tapped, separate from whether it is shown --
        /// see the field. Left interactable is the default so a page that shows the button
        /// without saying anything about it gets a live one rather than a dead one.</summary>
        public void SetInfoInteractable(bool interactable)
        {
            if (infoButton != null) { infoButton.interactable = interactable; }
        }

        public void OnBackButtonClick()
        {
            if (InputManager.Instance.CanInput())
            {
                AudioManager.Instance.PlaySFX(SoundType.ButtonClick);
                PageManager.Instance.ClosePage();
            }
        }

        public void OnSettingButtonClick()
        {
            if (InputManager.Instance.CanInput())
            {
                AudioManager.Instance.PlaySFX(SoundType.ButtonClick);
                PageManager.Instance.OpenAsOverlay(PageType.Setting);
            }
        }

        /// <summary>Opens the guide to every mechanic on the board being played -- for a player
        /// who has forgotten what a marker means, or who never saw its card because they met that
        /// mechanic before the cards existed.
        ///
        /// One CanInput() for the whole tap, like every other button here: it is a one-shot
        /// debounce, so a second check further down this call stack would always fail and swallow
        /// the tap.</summary>
        public void OnInfoButtonClick()
        {
            if (InputManager.Instance.CanInput())
            {
                AudioManager.Instance.PlaySFX(SoundType.ButtonClick);
                UIController.Instance.ShowMechanicGuideForCurrentBoard();
            }
        }
    }
}
