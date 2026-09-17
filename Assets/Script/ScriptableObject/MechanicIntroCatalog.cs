using UnityEngine;

namespace FreeFlow.GamePlay
{
    /// <summary>
    /// Every mechanic card there is, in one asset.
    ///
    /// Two screens need this list -- the first-encounter card and the guide the info button opens
    /// -- and a serialized array on each would be two places to remember when a mechanic gains a
    /// card, with nothing to say the two disagree. The same reason PageManager owns the one list of
    /// pages rather than letting each caller keep its own reference.
    /// </summary>
    [CreateAssetMenu(fileName = "MechanicIntroCatalog", menuName = "FreeFlow/Mechanic Intro Catalog")]
    public class MechanicIntroCatalog : ScriptableObject
    {
        [SerializeField] private MechanicIntroSO[] intros;

        /// <summary>The card for <paramref name="mechanicKey"/> -- one of
        /// <see cref="LevelMechanics.Keys"/> -- or null when that mechanic has none authored yet.
        /// A missing card is an ordinary state, not an error: it means that mechanic is explained
        /// nowhere and simply does not appear.</summary>
        public MechanicIntroSO Find(string mechanicKey)
        {
            if (intros == null || string.IsNullOrEmpty(mechanicKey)) { return null; }

            for (int i = 0; i < intros.Length; i++)
            {
                if (intros[i] != null && intros[i].mechanicKey == mechanicKey) { return intros[i]; }
            }
            return null;
        }
    }
}
