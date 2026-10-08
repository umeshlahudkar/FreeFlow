using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FreeFlow.GamePlay;

namespace FreeFlow.UI
{
    /// <summary>
    /// One mechanic's entry in <see cref="MechanicGuidePage"/>: its picture on the left, its name
    /// and rule filling the space beside it.
    ///
    /// The picture is one of two things and never both. A mechanic with runs authored shows the
    /// same animated 4x4 demo the first-encounter card uses (<see cref="MechanicDemoView"/>); one
    /// without shows its icon, because a board with nothing to trace on it is an empty box that
    /// says less than the sentence next to it.
    /// </summary>
    public class MechanicGuideRow : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI headerText;
        [SerializeField] private TextMeshProUGUI descriptionText;

        // The two things that can fill the square on the left. Exactly one is on at a time.
        [SerializeField] private MechanicDemoView demoView;
        [SerializeField] private Image iconImage;

        /// <summary>Fills this row in for <paramref name="intro"/> and starts its demo if it has
        /// one. Every row on the page runs its own demo at once -- they are short loops on small
        /// boards, and a guide where only one animates reads as the others being broken.</summary>
        public void Show(MechanicIntroSO intro)
        {
            if (intro == null) { return; }

            gameObject.name = "Row_" + intro.mechanicKey;

            if (headerText != null) { headerText.text = intro.title; }
            if (descriptionText != null) { descriptionText.text = intro.rule; }

            bool animated = intro.HasDemo;

            if (demoView != null)
            {
                demoView.gameObject.SetActive(animated);

                // After SetActive: the demo measures the space it was given, and an inactive
                // RectTransform has no size to measure.
                if (animated) { demoView.Play(intro); }
            }

            if (iconImage != null)
            {
                iconImage.sprite = intro.icon;

                // A description-only mechanic with no icon authored yet leaves the square empty
                // rather than showing a blank white box where a picture should be.
                iconImage.gameObject.SetActive(!animated && intro.icon != null);
            }
        }

        /// <summary>Stops the demo without tearing the row down -- the page closing, or this row
        /// being recycled onto another mechanic.</summary>
        public void StopDemo()
        {
            if (demoView != null) { demoView.Stop(); }
        }
    }
}
