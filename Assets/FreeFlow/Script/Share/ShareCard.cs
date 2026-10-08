using UnityEngine;
using TMPro;

namespace FreeFlow.Share
{
    /// <summary>
    /// The square image a shared result carries: game name, which level, and the attempt's stats.
    /// Lives off-screen on its own camera and canvas and is only switched on for the frame it
    /// takes to render itself.
    ///
    /// Deliberately NOT a screenshot of the level-complete sheet. That sheet is phone-shaped and
    /// covered in buttons, neither of which belongs in something posted to a feed; this is built
    /// square, at a fixed size, so it looks the same whatever device it was made on.
    ///
    /// Every other canvas in the scene is ScreenSpaceOverlay, and an overlay canvas is drawn
    /// straight to the display rather than through any camera -- so a camera rendering to a
    /// texture cannot pick them up, and this card is guaranteed to be alone in its own image.
    /// </summary>
    public class ShareCard : MonoBehaviour
    {
        [Header("Rig")]
        [SerializeField] private Camera cardCamera;
        [SerializeField] private Canvas cardCanvas;

        // Square, and big enough that the platforms which re-compress a shared image still have
        // something to work with. 1080 is the same width the rest of the UI is authored at.
        [SerializeField] private int size = 1080;

        [Header("Content")]
        [SerializeField] private TextMeshProUGUI gameNameText;
        [SerializeField] private TextMeshProUGUI headlineText;
        [SerializeField] private TextMeshProUGUI subheadlineText;
        [SerializeField] private TextMeshProUGUI movesValueText;
        [SerializeField] private TextMeshProUGUI timeValueText;
        [SerializeField] private TextMeshProUGUI hintsValueText;
        [SerializeField] private TextMeshProUGUI footerText;

        /// <summary>Fills the card in and renders it to a texture the caller owns -- it must
        /// Destroy the result when it is done with it.
        ///
        /// The rig is switched on only for the duration of this call. Left on, its canvas would
        /// keep laying itself out and its camera would keep rendering every frame, for an image
        /// nobody is looking at.</summary>
        public Texture2D Render(ShareResultData data, string gameName, string footer)
        {
            if (cardCamera == null || cardCanvas == null)
            {
                Debug.LogError("ShareCard: rig is not wired -- no card can be rendered.");
                return null;
            }

            bool wasActive = gameObject.activeSelf;
            gameObject.SetActive(true);

            Fill(data, gameName, footer);

            // The text set above only reaches the mesh on the next layout pass, and Camera.Render
            // below does not wait for one -- without this the card renders a frame stale, showing
            // the PREVIOUS level's numbers.
            Canvas.ForceUpdateCanvases();

            Texture2D texture = Capture();

            gameObject.SetActive(wasActive);
            return texture;
        }

        private void Fill(ShareResultData data, string gameName, string footer)
        {
            if (gameNameText != null) { gameNameText.text = gameName; }
            if (headlineText != null) { headlineText.text = data.Headline; }
            if (subheadlineText != null) { subheadlineText.text = data.Subheadline; }
            if (movesValueText != null) { movesValueText.text = data.Moves.ToString(); }
            if (timeValueText != null) { timeValueText.text = FormatTime(data.Seconds); }
            if (hintsValueText != null) { hintsValueText.text = data.Hints.ToString(); }
            if (footerText != null) { footerText.text = footer; }
        }

        /// <summary>Renders the rig's camera into a texture and reads it back on the CPU, which is
        /// what encoding a PNG needs. A one-off cost on a button press, not a per-frame one.</summary>
        private Texture2D Capture()
        {
            RenderTexture target = RenderTexture.GetTemporary(size, size, 24, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;

            cardCamera.targetTexture = target;
            cardCamera.Render();

            RenderTexture.active = target;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            texture.Apply();

            // Restore before releasing: a temporary RenderTexture released while still active
            // leaves the active target dangling for whatever renders next.
            RenderTexture.active = previous;
            cardCamera.targetTexture = null;
            RenderTexture.ReleaseTemporary(target);

            return texture;
        }

        /// <summary>"1:24" for anything a minute or over, "42s" under one -- the same formatting
        /// the level-complete sheet's own time card uses, so the card cannot disagree with the
        /// screen it was shared from.</summary>
        public static string FormatTime(float seconds)
        {
            int totalSeconds = Mathf.Max(0, Mathf.RoundToInt(seconds));
            int minutes = totalSeconds / 60;
            int remainder = totalSeconds % 60;
            return minutes > 0 ? minutes + ":" + remainder.ToString("00") : remainder + "s";
        }
    }
}
