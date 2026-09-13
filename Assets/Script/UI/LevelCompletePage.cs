using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FreeFlow.Enums;
using FreeFlow.Input;
using FreeFlow.Share;

namespace FreeFlow.UI
{
    /// <summary>The level-complete (game over) overlay. Opened with PageManager.OpenAsOverlay,
    /// closed with CloseOverlay -- never part of the back-stack. Owns every field this screen's
    /// content needs -- title/subtitle, next-level subtitle, stat cards, progress bar and
    /// streak banner -- rather than UIController holding any of it directly;
    /// <see cref="SetLevelCompleteData"/> is the one entry point UIController calls to fill it
    /// all in.
    ///
    /// Its header and its prev/next buttons read the same UIController properties the gameplay
    /// HUD does, so finishing a daily challenge lands on a screen that still says DAILY CHALLENGE
    /// and still offers the same neighbours the HUD offered a moment ago.</summary>
    public class LevelCompletePage : Page
    {
        [SerializeField] private TextMeshProUGUI titleText;
        [SerializeField] private TextMeshProUGUI subtitleText;
        // The big button's own two lines. Its title is worded per run -- the thing after a daily
        // challenge is another challenge, not "the next level" of anything -- and its subtitle
        // names what that thing actually is.
        [SerializeField] private TextMeshProUGUI nextLevelTitleText;
        [SerializeField] private TextMeshProUGUI nextLevelSubtitleText;

        [Header("Level navigation")]
        // Next is the big primary button; Prev is its smaller sibling, for stepping BACK into a
        // level already finished (an earlier pack level, or an earlier daily challenge of the same
        // day). Both are disabled rather than hidden at the ends of a run, matching the gameplay
        // HUD's own arrows.
        [SerializeField] private Button prevButton;
        [SerializeField] private Button nextButton;

        // Prev carries the same two lines Next does -- heading plus what it would open -- so the
        // two read as a matched pair rather than one labelled button beside a bare chevron.
        [SerializeField] private TextMeshProUGUI prevLevelTitleText;
        [SerializeField] private TextMeshProUGUI prevLevelSubtitleText;

        [Header("Stat cards")]
        [SerializeField] private TextMeshProUGUI movesStatText;
        [SerializeField] private TextMeshProUGUI timeStatText;
        [SerializeField] private TextMeshProUGUI hintsStatText;

        [Header("Progress")]
        // Shows the progress of whatever run the player is actually IN -- a pack's completion for
        // a pack level, the day's challenges for a daily one. The caption is part of that, not
        // fixed scenery: a daily challenge is drawn from anywhere inside a pack, so reporting that
        // pack's frontier after one would say the player had finished fifty levels they never saw.
        [SerializeField] private TextMeshProUGUI progressCaptionText;
        [SerializeField] private Slider progressSlider;
        [SerializeField] private TextMeshProUGUI progressLabelText;

        [Header("Daily streak")]
        [SerializeField] private GameObject streakBanner;
        [SerializeField] private TextMeshProUGUI streakText;

        // The attempt this screen is currently reporting. Held because Share happens later, on a
        // tap, and these numbers exist nowhere else by then: moves, time and hints-this-attempt
        // are never written to the save (see GamePlayController.lastCompletion*), so once
        // SetLevelCompleteData has drawn them into its labels there is nothing left to read them
        // back from.
        private ShareResultData lastResult;

        /// <summary>Fills in this screen's content for the attempt that was just completed --
        /// called by UIController.ActivateLevelCompleteScreen right before it opens this overlay.
        /// Only the truly one-shot attempt stats are passed in (moves/hints/time/old progress --
        /// never stored anywhere, so nothing else can supply them); everything else (mode, pack
        /// size, current level, total levels, which run this is) is read directly from
        /// UIController.Instance's own public properties, same as every other page's header.
        /// </summary>
        /// <param name="movesCount">Moves made this attempt.</param>
        /// <param name="hintsUsedThisAttempt">Hints used since this attempt began.</param>
        /// <param name="secondsTaken">Wall-clock time this attempt took to solve the level.</param>
        /// <param name="oldCompletedLevel">CompletedLevelForKey for this pack BEFORE this
        /// completion, so the progress bar can show where the player was, not just where they are.</param>
        public void SetLevelCompleteData(int movesCount, int hintsUsedThisAttempt, float secondsTaken, int oldCompletedLevel)
        {
            UIController ui = UIController.Instance;
            if (ui == null) { return; }

            if (titleText != null)
            {
                // Named after the run, not the board: finishing one of the day's challenges is not
                // "the daily challenge complete" while others are still open.
                titleText.text = ui.CurrentSource == LevelSource.Daily
                    ? "CHALLENGE COMPLETE"
                    : "LEVEL COMPLETE";
            }

            if (subtitleText != null)
            {
                subtitleText.text = ui.LevelHeaderTitle
                    + "  ·  " + ui.LevelHeaderSubtitle
                    + "  ·  SOLVED IN " + movesCount + " MOVES";
            }

            if (movesStatText != null) { movesStatText.text = movesCount.ToString(); }
            if (timeStatText != null) { timeStatText.text = FormatTime(secondsTaken); }
            if (hintsStatText != null) { hintsStatText.text = hintsUsedThisAttempt.ToString(); }

            SetProgress(ui, oldCompletedLevel);

            SetStreakBanner(ui);
            SetNavigation(ui);

            lastResult = new ShareResultData
            {
                Headline = ui.LevelHeaderTitle,
                Subheadline = ui.LevelHeaderSubtitle,
                Phrase = ui.LevelPhrase,
                Moves = movesCount,
                Hints = hintsUsedThisAttempt,
                Seconds = secondsTaken,
            };
        }

        /// <summary>Shown only on a daily challenge, and worded for whether the DAY is finished --
        /// the streak only moves when every one of the day's challenges is done (see
        /// SaveData.RecordDailyChallengeCompletion), so claiming a streak after the first of five
        /// would be claiming something that has not happened yet.</summary>
        private void SetStreakBanner(UIController ui)
        {
            if (streakBanner == null) { return; }

            streakBanner.SetActive(ui.IsDailyChallenge);
            if (!ui.IsDailyChallenge || streakText == null) { return; }

            SaveData data = SavingSystem.Instance.Load();

            if (data.AllDailyChallengesSolved())
            {
                streakText.text = data.dailyChallengeStreak + "-day streak!";
                return;
            }

            int remaining = data.DailyChallengeCount - data.SolvedDailyChallengeCount();
            streakText.text = remaining + (remaining == 1 ? " more to keep the streak" : " more today to keep the streak");
        }

        private void SetNavigation(UIController ui)
        {
            if (prevButton != null) { prevButton.interactable = ui.HasPrevLevel; }
            if (prevLevelTitleText != null) { prevLevelTitleText.text = ui.PrevActionTitle; }
            if (prevLevelSubtitleText != null) { prevLevelSubtitleText.text = ui.PrevActionSubtitle; }

            // Deliberately always interactable, unlike Prev and unlike the gameplay HUD's arrows.
            // At the end of a run this button stops being "next" and becomes the way back to the
            // Daily Challenge hub or the pack chooser -- see OnNextButtonClick -- so there is
            // never a state where the screen's primary action is dead.
            if (nextButton != null) { nextButton.interactable = true; }

            // Both lines come from UIController so they describe whatever the button is actually
            // about to do, and can never promise a level it would refuse to load.
            if (nextLevelTitleText != null) { nextLevelTitleText.text = ui.NextActionTitle; }
            if (nextLevelSubtitleText != null) { nextLevelSubtitleText.text = ui.NextActionSubtitle; }
        }

        /// <summary>Fills the progress card for the run the player is in.
        ///
        /// A daily challenge reports the DAY (2/5 of today's challenges), not the pack its level
        /// happened to be drawn from. Pack progress is meaningless there and was actively
        /// misleading: a daily drawn at level 59 of a pack the player has barely started would
        /// report "59/100" as if fifty-eight levels behind it had been finished.
        /// </summary>
        /// <param name="oldCompletedLevel">Pack frontier before this completion. Unused on a daily
        /// challenge, which does not move a pack frontier at all -- see
        /// GamePlayController.SaveLevelData.</param>
        private void SetProgress(UIController ui, int oldCompletedLevel)
        {
            string caption;
            int done;
            int total;

            if (ui.CurrentSource == LevelSource.Daily)
            {
                // Read fresh: SaveLevelData has already marked this challenge solved by the time
                // this screen is filled in (see ActivateLevelCompleteScreen), and UIController's
                // cached picks still say otherwise.
                SaveData data = SavingSystem.Instance.Load();
                caption = "TODAY'S CHALLENGES";
                done = data.SolvedDailyChallengeCount();
                total = data.DailyChallengeCount;
            }
            else
            {
                caption = "PACK PROGRESS";
                done = Mathf.Max(oldCompletedLevel, ui.CurrentLevel);
                total = ui.TotalLevelCount;
            }

            if (progressCaptionText != null) { progressCaptionText.text = caption; }
            if (progressLabelText != null) { progressLabelText.text = done + "/" + total; }
            if (progressSlider != null)
            {
                progressSlider.value = total > 0 ? (float)done / total : 0f;
            }
        }

        /// <summary>"1:24" for anything a minute or over, "42s" under a minute -- matches how
        /// short a level attempt actually runs, so the common case doesn't carry a redundant
        /// "0:" prefix.</summary>
        private static string FormatTime(float seconds)
        {
            int totalSeconds = Mathf.Max(0, Mathf.RoundToInt(seconds));
            int minutes = totalSeconds / 60;
            int remainder = totalSeconds % 60;
            return minutes > 0 ? minutes + ":" + remainder.ToString("00") : remainder + "s";
        }

        public void OnRetryButtonClick()
        {
            if (InputManager.Instance.CanInput())
            {
                AudioManager.Instance.PlaySFX(SoundType.ButtonClick);
                UIController.Instance.RetryCurrentLevel();
            }
        }

        /// <summary>Tapping the backdrop behind the sheet puts this overlay away and returns the
        /// player to the solved board -- to screenshot it, to redraw a route on it, or to use the
        /// gameplay HUD's own Next arrow instead of this screen's button. Wired to a full-screen
        /// transparent catcher sitting behind the sheet, so only taps that MISS the sheet reach
        /// it; the sheet's own buttons are unaffected.</summary>
        public void OnBackdropClick()
        {
            if (InputManager.Instance.CanInput())
            {
                AudioManager.Instance.PlaySFX(SoundType.ButtonClick);
                UIController.Instance.DismissLevelCompleteOverlay();
            }
        }

        public void OnHomeButtonClick()
        {
            if (InputManager.Instance.CanInput())
            {
                AudioManager.Instance.PlaySFX(SoundType.ButtonClick);
                UIController.Instance.GoToMainMenu();
            }
        }

        /// <summary>Shares what the player just did -- the level and the attempt's stats, as a
        /// rendered card plus text (see ShareService.ShareResult). Not the same share as the
        /// Settings screen's, which promotes the game and has no result to show.
        ///
        /// The overlay is deliberately left open behind the share sheet: the player came back from
        /// sharing to the screen they shared from, and closing it would drop them somewhere they
        /// did not ask to be.</summary>
        public void OnShareButtonClick()
        {
            if (InputManager.Instance.CanInput())
            {
                AudioManager.Instance.PlaySFX(SoundType.ButtonClick);
                ShareService.Instance.ShareResult(lastResult);
            }
        }

        /// <summary>Steps back to the level before this one -- an earlier level of the same pack,
        /// or an earlier challenge of the same day. Loading it closes this overlay (see
        /// UIController.LoadCurrentModeLevel), so there is nothing to close here.</summary>
        public void OnPrevButtonClick()
        {
            if (InputManager.Instance.CanInput() && UIController.Instance.HasPrevLevel)
            {
                AudioManager.Instance.PlaySFX(SoundType.ButtonClick);
                UIController.Instance.GoToPrevLevel();
            }
        }

        /// <summary>The screen's primary action. Advances within the run while there is something
        /// after this level; once there is not, it leaves for wherever the run was chosen from --
        /// the Daily Challenge hub after the day's last challenge, the pack chooser after a pack's
        /// last level. The button's own label says which of the two it is about to do (see
        /// UIController.NextActionTitle), so this is never a surprise.</summary>
        public void OnNextButtonClick()
        {
            if (InputManager.Instance.CanInput())
            {
                AudioManager.Instance.PlaySFX(SoundType.ButtonClick);

                if (UIController.Instance.HasNextLevel) { UIController.Instance.GoToNextLevel(); }
                else { UIController.Instance.ExitToRunHome(); }
            }
        }
    }
}
