using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FreeFlow.Enums;
using FreeFlow.GamePlay;
using FreeFlow.Input;

namespace FreeFlow.UI
{
    /// <summary>The gameplay screen, and the owner of every widget on it: the header, the
    /// progress card, the hint button and the footer's prev/next buttons. What those widgets SAY
    /// is still UIController's to decide -- this page reads the wording and the run state straight
    /// off it rather than composing either itself, so the header and the buttons say the same
    /// thing here as on the level-complete overlay, whether the player came in from a pack or
    /// from the daily challenge.</summary>
    public class GameplayPage : Page
    {
        [SerializeField] private TopPanel topPanel;

        [Header("Progress card")]
        // Cells rather than pairs, deliberately -- see UpdateFilledCells.
        [SerializeField] private TextMeshProUGUI cellsText;
        [SerializeField] private TextMeshProUGUI percentText;
        [SerializeField] private Slider progressSlider;

        [Header("Hint")]
        // Left interactable with an empty balance on purpose -- see OnHintButtonClick.
        [SerializeField] private Button hintButton;
        // The "x3" pill: how many hints are left to spend.
        [SerializeField] private TextMeshProUGUI hintCountText;

        // The halo behind the button. A SIBLING drawn before it rather than a child -- a child
        // would draw on top of the icon -- so hiding the button leaves this glowing on its own
        // unless it is switched off with it.
        [SerializeField] private GameObject hintGlow;

        [Header("Level navigation")]
        // The footer's two stepping buttons. Faded and inert rather than hidden when a step is
        // unavailable (level 1, a next level still behind the unlock frontier, the first/last of
        // today's challenges) so the footer does not reshuffle itself as the player moves along it.
        [SerializeField] private Button prevButton;
        [SerializeField] private Button nextButton;

        // Each button's two lines: the heading says what the button DOES ("NEXT DAILY"), the
        // subtitle what it would open ("LEVEL 74 · 6×6"). The same two steps the level-complete
        // overlay offers, in the shorter wording these narrower buttons can hold -- see
        // UIController's footer step labels.
        [SerializeField] private TextMeshProUGUI prevTitleText;
        [SerializeField] private TextMeshProUGUI prevSubtitleText;
        [SerializeField] private TextMeshProUGUI nextTitleText;
        [SerializeField] private TextMeshProUGUI nextSubtitleText;

        // The fade. A CanvasGroup rather than the Button's own disabled tint because that only
        // tints the button's target graphic -- the chevron and both labels are separate graphics
        // and would stay at full strength, so a locked button would read as merely unresponsive.
        [SerializeField] private CanvasGroup prevGroup;
        [SerializeField] private CanvasGroup nextGroup;

        // Faded enough to read as unavailable at a glance, not so faint the level number it names
        // stops being legible -- the labels are the point of the fade, not a casualty of it.
        [SerializeField, Range(0f, 1f)] private float unavailableAlpha = 0.35f;

        // Every load re-opens this page through PageManager, which cycles it (Close then Open)
        // even when Gameplay was already current -- so OnEnable really does run on every
        // prev/next/retry, not only on the first entry into gameplay.
        private void OnEnable()
        {
            Refresh();
        }

        public void Refresh()
        {
            UIController ui = UIController.Instance;
            if (ui == null) { return; }

            if (topPanel != null)
            {
                topPanel.SetTopPanel(ui.LevelHeaderTitle, ui.LevelHeaderSubtitle);
            }

            SetStepButton(prevButton, prevGroup, prevTitleText, prevSubtitleText,
                ui.HasPrevLevel, ui.PrevStepTitle, ui.PrevStepSubtitle);
            SetStepButton(nextButton, nextGroup, nextTitleText, nextSubtitleText,
                ui.HasNextLevel, ui.NextStepTitle, ui.NextStepSubtitle);

            RefreshHintButton();
            UpdateFilledCells();
        }

        /// <summary>
        /// Shows how much of the board is filled, on the progress card.
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

            int filled = controller.FilledCellCount;
            int usable = controller.UsableCellCount;

            if (cellsText != null) { cellsText.text = filled + "/" + usable + " CELLS"; }
            if (percentText != null)
            {
                int percent = usable > 0 ? Mathf.RoundToInt(100f * filled / usable) : 0;
                percentText.text = percent + "%";
            }
            if (progressSlider != null)
            {
                progressSlider.value = usable > 0 ? (float)filled / usable : 0f;
            }
        }

        /// <summary>
        /// Puts the hint button in the state the save says it should be in: the count on its pill,
        /// whether it is shown at all, and whether it can be tapped. Called on every level load
        /// (through <see cref="Refresh"/>) and after every hint spent, so the pill and the button
        /// can never disagree with the balance behind them.
        ///
        /// The balance itself belongs to UIController (one for the whole game, held in the save);
        /// this only draws it.
        /// </summary>
        public void RefreshHintButton()
        {
            SaveData data = SavingSystem.Instance.Load();

            if (hintCountText != null) { hintCountText.text = "×" + data.hintsRemaining; }
            if (hintGlow != null) { hintGlow.SetActive(data.showHintButton); }

            if (hintButton == null) { return; }

            // Show/hide is the player's own Settings-screen preference. Interactable is only about
            // whether the board can be hinted at all (GamePlayController.HintAvailable -- a level
            // with no stored answer has nothing to show); an empty balance deliberately leaves the
            // button live, because a tap on it is what raises the "No More Hints" notice. A dead
            // button would answer the same tap with nothing at all.
            hintButton.gameObject.SetActive(data.showHintButton);
            hintButton.interactable = GamePlayController.Instance != null
                && GamePlayController.Instance.HintAvailable;
        }

        /// <summary>One stepping button: what it leads to, and whether it can be taken. A step
        /// that names a level but cannot be taken (a next level still locked behind the unlock
        /// frontier) keeps its labels and fades -- the player is told which level is waiting and
        /// that it is not open yet, rather than being shown a button that silently does
        /// nothing.</summary>
        private void SetStepButton(Button button, CanvasGroup group, TextMeshProUGUI title,
            TextMeshProUGUI subtitle, bool available, string doesWhat, string leadsTo)
        {
            if (title != null) { title.text = doesWhat; }
            if (subtitle != null) { subtitle.text = leadsTo; }
            if (button != null) { button.interactable = available; }
            if (group != null) { group.alpha = available ? 1f : unavailableAlpha; }
        }

        /// <summary>
        /// Joins one pair along the level's own answer, at the cost of one hint from the player's
        /// balance and the move it adds to this attempt.
        ///
        /// With the balance empty the tap is answered with the "No More Hints" notice instead of
        /// being swallowed: the button stays interactable in that state on purpose (see
        /// <see cref="RefreshHintButton"/>), because a player who has just watched the pill count
        /// down to zero will tap it again, and silence is the one response that explains nothing.
        ///
        /// A hint that finds nothing to do IS silent by design: the board is either already
        /// correct or has no stored answer, and in the second case the button is not interactable
        /// anyway.
        /// </summary>
        public void OnHintButtonClick()
        {
            // One CanInput() for the whole tap -- it is a one-shot debounce, so checking it again
            // further down this call stack would always fail and lose the tap.
            if (InputManager.Instance.CanInput())
            {
                AudioManager.Instance.PlaySFX(SoundType.ButtonClick);

                if (UIController.Instance.HintsRemaining <= 0)
                {
                    UIController.Instance.ShowWarning(UIController.Instance.NoHintsMessage);
                    return;
                }


                GamePlayController.Instance.TryApplyHint();
            }
        }

        /// <summary>Steps to the previous/next level without leaving gameplay -- the footer's two
        /// arrow buttons. What "previous" and "next" mean is UIController's call, not this page's:
        /// within a pack they are level numbers (Next respecting the same unlock frontier the
        /// level grid enforces), within a daily challenge they are the other challenges of the
        /// same day.</summary>
        public void OnPrevLevelClick()
        {
            if (InputManager.Instance.CanInput() && UIController.Instance.HasPrevLevel)
            {
                AudioManager.Instance.PlaySFX(SoundType.ButtonClick);
                UIController.Instance.GoToPrevLevel();
            }
        }

        public void OnNextLevelClick()
        {
            if (InputManager.Instance.CanInput() && UIController.Instance.HasNextLevel)
            {
                AudioManager.Instance.PlaySFX(SoundType.ButtonClick);
                UIController.Instance.GoToNextLevel();
            }
        }
    }
}
