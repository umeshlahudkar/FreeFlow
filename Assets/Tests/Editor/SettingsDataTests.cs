using NUnit.Framework;

namespace FreeFlow.Tests
{
    /// <summary>
    /// SettingsData's mechanic-teaching gate. SettingsData is a plain serialisable struct with no
    /// Unity dependencies, so these run against it directly rather than through SettingsSystem's
    /// file I/O.
    /// </summary>
    public class SettingsDataTests
    {
        // -- mechanic teaching -----------------------------------------------------------------

        [Test]
        public void UnseenMechanic_HasNotBeenMet()
        {
            SettingsData data = new SettingsData();
            Assert.IsFalse(data.HasMetMechanic("Bridge"));
        }

        [Test]
        public void MetMechanics_MarksExactlyThatMechanicAsMet()
        {
            SettingsData data = new SettingsData { metMechanics = FreeFlow.GamePlay.MechanicFlags.Bridge };

            Assert.IsTrue(data.HasMetMechanic("Bridge"));
            Assert.IsFalse(data.HasMetMechanic("Checkpoint"));
        }

        [Test]
        public void MetMechanics_AccumulatesAcrossSeveralOrs()
        {
            SettingsData data = new SettingsData();
            data.metMechanics |= FreeFlow.GamePlay.MechanicFlags.Bridge;
            data.metMechanics |= FreeFlow.GamePlay.MechanicFlags.Checkpoint;

            Assert.IsTrue(data.HasMetMechanic("Bridge"));
            Assert.IsTrue(data.HasMetMechanic("Checkpoint"));
            Assert.IsFalse(data.HasMetMechanic("Wall"));
        }

        [Test]
        public void BasicFlowKey_IsNeverMet()
        {
            // BasicFlowKey names a mechanic-free board, not a mechanic -- it has no flag, so it can
            // never gate the teaching card (nothing is authored for it either -- see
            // GamePlayController.FirstMechanicWithIntro).
            SettingsData data = new SettingsData { metMechanics = (FreeFlow.GamePlay.MechanicFlags)(-1) };
            Assert.IsFalse(data.HasMetMechanic(FreeFlow.GamePlay.LevelMechanics.BasicFlowKey));
        }
    }
}
