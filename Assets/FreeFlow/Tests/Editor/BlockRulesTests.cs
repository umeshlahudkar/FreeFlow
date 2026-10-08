using FreeFlow.Enums;
using FreeFlow.GamePlay;
using NUnit.Framework;

namespace FreeFlow.Tests
{
    /// <summary>
    /// Covers Block's movement-legality predicates -- CanEnter, CanEnterFrom, CanExitFrom,
    /// CanAcceptEntry -- across every mechanic that constrains them. These are pure functions of
    /// a cell's own state, so each test pokes exactly the state it needs via
    /// <see cref="BlockTestHarness"/> and asserts directly, with no board or scene involved.
    /// </summary>
    public class BlockRulesTests
    {
        private Block block;

        [TearDown]
        public void TearDown()
        {
            BlockTestHarness.Destroy(block);
        }

        // -- CanEnter: whole-cell admission, ignoring direction ---------------------------

        [Test]
        public void Blocked_RefusesEveryPair()
        {
            block = BlockTestHarness.CreateBlock();
            BlockTestHarness.SetBlocked(block);

            Assert.IsFalse(block.CanEnter(1));
            Assert.IsFalse(block.CanEnter(2));
        }

        [Test]
        public void Normal_AcceptsEveryPair()
        {
            block = BlockTestHarness.CreateBlock();

            Assert.IsTrue(block.CanEnter(1));
            Assert.IsTrue(block.CanEnter(2));
        }

        [Test]
        public void ForbiddenForPair_RefusesOnlyTheNamedPair()
        {
            block = BlockTestHarness.CreateBlock();
            BlockTestHarness.SetRuleCell(block, BlockType.ForbiddenForPair, pairId: 1);

            Assert.IsFalse(block.CanEnter(1));
            Assert.IsTrue(block.CanEnter(2));
        }

        [Test]
        public void ForbiddenForPair_RefusesBothNamedPairs()
        {
            block = BlockTestHarness.CreateBlock();
            BlockTestHarness.SetRuleCell(block, BlockType.ForbiddenForPair, pairId: 1, secondPairId: 2);

            Assert.IsFalse(block.CanEnter(1));
            Assert.IsFalse(block.CanEnter(2));
            Assert.IsTrue(block.CanEnter(3));
        }

        [Test]
        public void AllowedForPairs_AcceptsOnlyTheNamedPairs()
        {
            block = BlockTestHarness.CreateBlock();
            BlockTestHarness.SetRuleCell(block, BlockType.AllowedForPairs, pairId: 1, secondPairId: 2);

            Assert.IsTrue(block.CanEnter(1));
            Assert.IsTrue(block.CanEnter(2));
            Assert.IsFalse(block.CanEnter(3));
        }

        // -- CanEnterFrom: entry direction (One-Way, Arrow head-on refusal) ---------------

        [Test]
        public void OneWay_AcceptsOnlyTheRequiredEntryDirection()
        {
            block = BlockTestHarness.CreateBlock();
            BlockTestHarness.SetOneWay(block, Direction.Down);

            Assert.IsTrue(block.CanEnterFrom(Direction.Down));
            Assert.IsFalse(block.CanEnterFrom(Direction.Up));
            Assert.IsFalse(block.CanEnterFrom(Direction.Left));
        }

        [Test]
        public void Normal_CanEnterFromAnyDirection()
        {
            block = BlockTestHarness.CreateBlock();

            Assert.IsTrue(block.CanEnterFrom(Direction.Left));
            Assert.IsTrue(block.CanEnterFrom(Direction.Right));
        }

        [Test]
        public void Arrow_RefusesHeadOnEntryThatWouldBounceStraightBack()
        {
            block = BlockTestHarness.CreateBlock();
            BlockTestHarness.SetArrow(block, Direction.Up);

            // Forced to exit Up; entering while moving Down means it arrived through the Up edge,
            // so the forced exit would walk it straight back into the cell it just left.
            Assert.IsFalse(block.CanEnterFrom(Direction.Down));
            Assert.IsTrue(block.CanEnterFrom(Direction.Up));
            Assert.IsTrue(block.CanEnterFrom(Direction.Left));
        }

        // -- CanExitFrom: the pure two-direction rule (Arrow, Bridge) ---------------------

        [Test]
        public void Arrow_ForcesTheExitDirectionRegardlessOfEntry()
        {
            block = BlockTestHarness.CreateBlock();
            BlockTestHarness.SetArrow(block, Direction.Right);

            Assert.IsTrue(block.CanExitFrom(Direction.Up, Direction.Right));
            Assert.IsFalse(block.CanExitFrom(Direction.Up, Direction.Left));
        }

        [Test]
        public void Bridge_OnlyAllowsStraightThroughExit()
        {
            block = BlockTestHarness.CreateBlock();
            BlockTestHarness.SetBridge(block);

            Assert.IsTrue(block.CanExitFrom(Direction.Left, Direction.Left));
            Assert.IsFalse(block.CanExitFrom(Direction.Left, Direction.Up));
        }

        [Test]
        public void Bridge_WithUnknownEntryAllowsAnyExit()
        {
            block = BlockTestHarness.CreateBlock();
            BlockTestHarness.SetBridge(block);

            Assert.IsTrue(block.CanExitFrom(Direction.None, Direction.Up));
        }

        [Test]
        public void Normal_AllowsAnyExit()
        {
            block = BlockTestHarness.CreateBlock();

            Assert.IsTrue(block.CanExitFrom(Direction.Left, Direction.Up));
        }

        // -- CanAcceptEntry: Bridge per-axis occupancy ------------------------------------

        [Test]
        public void Bridge_RefusesASecondPairOnTheSameAxis()
        {
            block = BlockTestHarness.CreateBlock();
            BlockTestHarness.SetBridge(block);
            BlockTestHarness.ClaimDirection(block, Direction.Left, pairId: 1);

            Assert.IsFalse(block.CanAcceptEntry(Direction.Right, enteringPairId: 2));
        }

        [Test]
        public void Bridge_AcceptsASecondPairOnTheOtherAxis()
        {
            block = BlockTestHarness.CreateBlock();
            BlockTestHarness.SetBridge(block);
            BlockTestHarness.ClaimDirection(block, Direction.Left, pairId: 1);

            Assert.IsTrue(block.CanAcceptEntry(Direction.Up, enteringPairId: 2));
        }

        [Test]
        public void Bridge_AcceptsTheSamePairReenteringItsOwnAxis()
        {
            block = BlockTestHarness.CreateBlock();
            BlockTestHarness.SetBridge(block);
            BlockTestHarness.ClaimDirection(block, Direction.Left, pairId: 1);

            Assert.IsTrue(block.CanAcceptEntry(Direction.Right, enteringPairId: 1));
        }

        [Test]
        public void NonBridge_AlwaysAcceptsEntry()
        {
            block = BlockTestHarness.CreateBlock();

            Assert.IsTrue(block.CanAcceptEntry(Direction.Left, enteringPairId: 1));
        }
    }
}
