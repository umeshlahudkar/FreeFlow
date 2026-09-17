using System.Collections.Generic;
using UnityEngine;
using FreeFlow.Enums;
using FreeFlow.GamePlay;

namespace FreeFlow.UI
{
    /// <summary>
    /// Every mechanic on the board being played, listed -- what the header's info button opens.
    ///
    /// Separate from <see cref="MechanicIntroPage"/> on purpose, and the two differ in what they
    /// are FOR rather than only in how they look. The intro card interrupts a level load because
    /// one mechanic is new, so it shows that one; this is asked for, by a player who wants to know
    /// what is on their board, so it shows all of them and nothing is selected or hidden behind a
    /// tab. Both draw their demos with the same <see cref="MechanicDemoView"/>, so there is one
    /// copy of the board-building and the playback rather than one per screen.
    ///
    /// Rows are spawned from a prefab rather than authored per mechanic: which ones appear depends
    /// on the level, and no level is guaranteed to carry the same pair twice.
    /// </summary>
    public class MechanicGuidePage : Page
    {
        // Every card there is. The same asset the intro page reads, so a newly authored mechanic
        // appears on both screens without being wired into either.
        [SerializeField] private MechanicIntroCatalog catalog;

        [Header("Rows")]
        [SerializeField] private MechanicGuideRow rowPrefab;

        // What rows are parented to -- a scroll view's content, or a plain vertical layout when
        // the list is short enough not to need scrolling.
        [SerializeField] private RectTransform rowsParent;

        // Spawned rows, kept so they can be cleared when the page is next opened for a different
        // board. Not pooled: this opens on a button press, a handful of times a session at most.
        private readonly List<MechanicGuideRow> rows = new List<MechanicGuideRow>();

        /// <summary>
        /// Builds the list for <paramref name="mechanicKeys"/> -- one row each, in the order given,
        /// which is <see cref="LevelMechanics.Keys"/> order and therefore stable from level to
        /// level. Called BEFORE PageManager opens the page.
        ///
        /// Keys with no card authored are skipped rather than given an empty row, so this can be
        /// handed a board's whole mechanic list without filtering it first.
        /// </summary>
        public void SetMechanics(string[] mechanicKeys)
        {
            ClearRows();

            if (catalog == null || rowPrefab == null || rowsParent == null || mechanicKeys == null) { return; }

            for (int i = 0; i < mechanicKeys.Length; i++)
            {
                MechanicIntroSO intro = catalog.Find(mechanicKeys[i]);
                if (intro == null) { continue; }

                MechanicGuideRow row = Instantiate(rowPrefab, rowsParent);
                row.gameObject.SetActive(true);
                rows.Add(row);
            }

            // Filled in a second pass, after every row exists: a row measures the space it was
            // given to size its demo board, and a vertical layout only hands out final sizes once
            // it knows how many children it has.
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(rowsParent);

            int at = 0;
            for (int i = 0; i < mechanicKeys.Length && at < rows.Count; i++)
            {
                MechanicIntroSO intro = catalog.Find(mechanicKeys[i]);
                if (intro == null) { continue; }

                rows[at].Show(intro);
                at++;
            }
        }

        /// <summary>Whether anything would be listed for <paramref name="mechanicKeys"/> -- asked
        /// before opening, so a board whose mechanics all lack cards is not answered with an empty
        /// page.</summary>
        public bool HasAnythingFor(string[] mechanicKeys)
        {
            if (catalog == null || mechanicKeys == null) { return false; }

            for (int i = 0; i < mechanicKeys.Length; i++)
            {
                if (catalog.Find(mechanicKeys[i]) != null) { return true; }
            }
            return false;
        }

        public override void Close()
        {
            // Stopped before the page goes inactive: Unity kills a coroutine on deactivation and
            // would leave each demo holding a handle that still reads as a running trace.
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i] != null) { rows[i].StopDemo(); }
            }

            base.Close();
        }

        /// <summary>The close button and the backdrop. Through PageManager rather than
        /// deactivating itself -- a page must not put itself away behind the manager's back.</summary>
        public void OnDismissClick()
        {
            AudioManager.Instance.PlaySFX(SoundType.ButtonClick);
            PageManager.Instance.CloseOverlay(PageType.MechanicGuide);
        }

        private void ClearRows()
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i] == null) { continue; }

                rows[i].StopDemo();

                // Hidden before it is destroyed: Destroy is deferred to the end of the frame, so
                // the outgoing rows would otherwise be laid out alongside the incoming ones.
                rows[i].gameObject.SetActive(false);
                Destroy(rows[i].gameObject);
            }

            rows.Clear();
        }
    }
}
