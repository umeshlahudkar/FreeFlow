using UnityEngine;
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

        /// <summary>Called by the owning Page (from its own OnEnable/Refresh, same as every other
        /// page-specific header field) to configure this shared header for that page: title/
        /// subtitle text and which of the three buttons should be visible. Defaults match what
        /// every page has used so far -- Back and Setting shown, Option hidden (it currently
        /// duplicates Setting's action and has no distinct use yet).</summary>
        public void SetTopPanel(string title, string subtitle,
            bool showBack = true, bool showSetting = true, bool showOption = false)
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
    }
}
