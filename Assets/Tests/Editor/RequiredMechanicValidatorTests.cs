using FreeFlow.Enums;
using FreeFlow.GamePlay;
using NUnit.Framework;

namespace FreeFlow.Tests
{
    /// <summary>
    /// Covers RequiredMechanicValidator against boards where the expected classification was
    /// worked out from the underlying math first (see the class's own doc comment on why a pure
    /// restriction mechanic can only ever break UNIQUENESS, never solvability, while Bridge and
    /// Blocked are the exception), then confirmed empirically before being locked in here -- not
    /// guessed at and adjusted until green.
    /// </summary>
    public class RequiredMechanicValidatorTests
    {
        private Block[,] grid;

        [TearDown]
        public void TearDown()
        {
            if (grid == null) { return; }
            foreach (Block block in grid) { BlockTestHarness.Destroy(block); }
            grid = null;
        }

        private Block[,] CreateGrid(int rows, int cols)
        {
            grid = new Block[rows, cols];
            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < cols; j++)
                {
                    grid[i, j] = BlockTestHarness.CreateBlock(i, j);
                }
            }
            return grid;
        }

        // -- Arrow ---------------------------------------------------------------------------
        // The 3x3 opposite-corner board has exactly two full-coverage solutions (see
        // PuzzleSolverTests): one going right-then-down-then-left-then-down-then-right, the other
        // its transpose. At (1,1) the first exits Left, the second exits Up.

        [Test]
        public void Arrow_EliminatingTheOtherOfTwoSolutions_IsRequired()
        {
            CreateGrid(3, 3);
            BlockTestHarness.SetDot(grid[0, 0], pairId: 1);
            BlockTestHarness.SetDot(grid[2, 2], pairId: 1);
            BlockTestHarness.SetArrow(grid[1, 1], Direction.Left); // only one of the two solutions exits this way

            RequiredMechanicValidator.RequirementResult result =
                RequiredMechanicValidator.CheckBlockTypeMechanicRequired(grid, 3, 3, 1, 1);

            Assert.AreEqual(RequiredMechanicValidator.RequirementStatus.Required, result.Status);
            Assert.AreEqual(1, result.WithMechanic.SolutionsFound);
            Assert.AreEqual(2, result.WithoutMechanic.SolutionsFound);
        }

        [Test]
        public void Arrow_RedundantWithTheOnlyPossibleDirection_IsNotRequired()
        {
            CreateGrid(1, 3);
            BlockTestHarness.SetDot(grid[0, 0], pairId: 1);
            BlockTestHarness.SetDot(grid[0, 2], pairId: 1);
            BlockTestHarness.SetArrow(grid[0, 1], Direction.Right); // the only route already goes this way

            RequiredMechanicValidator.RequirementResult result =
                RequiredMechanicValidator.CheckBlockTypeMechanicRequired(grid, 1, 3, 0, 1);

            Assert.AreEqual(RequiredMechanicValidator.RequirementStatus.NotRequired, result.Status);
        }

        // -- One-Way ---------------------------------------------------------------------------

        [Test]
        public void OneWay_EliminatingTheOtherOfTwoSolutions_IsRequired()
        {
            CreateGrid(3, 3);
            BlockTestHarness.SetDot(grid[0, 0], pairId: 1);
            BlockTestHarness.SetDot(grid[2, 2], pairId: 1);
            BlockTestHarness.SetOneWay(grid[1, 1], Direction.Left); // only one of the two solutions enters this way

            RequiredMechanicValidator.RequirementResult result =
                RequiredMechanicValidator.CheckBlockTypeMechanicRequired(grid, 3, 3, 1, 1);

            Assert.AreEqual(RequiredMechanicValidator.RequirementStatus.Required, result.Status);
        }

        [Test]
        public void OneWay_RedundantWithTheOnlyPossibleDirection_IsNotRequired()
        {
            CreateGrid(1, 3);
            BlockTestHarness.SetDot(grid[0, 0], pairId: 1);
            BlockTestHarness.SetDot(grid[0, 2], pairId: 1);
            BlockTestHarness.SetOneWay(grid[0, 1], Direction.Right);

            RequiredMechanicValidator.RequirementResult result =
                RequiredMechanicValidator.CheckBlockTypeMechanicRequired(grid, 1, 3, 0, 1);

            Assert.AreEqual(RequiredMechanicValidator.RequirementStatus.NotRequired, result.Status);
        }

        // -- Wall ------------------------------------------------------------------------------

        [Test]
        public void Wall_EliminatingTheOtherOfTwoSolutions_IsRequired()
        {
            // Of the two solutions, only the first crosses the (1,1)-(1,2) edge; the second
            // (the transpose) never touches it.
            CreateGrid(3, 3);
            BlockTestHarness.SetDot(grid[0, 0], pairId: 1);
            BlockTestHarness.SetDot(grid[2, 2], pairId: 1);
            BlockTestHarness.SetWall(grid[1, 1], Direction.Right);

            RequiredMechanicValidator.RequirementResult result =
                RequiredMechanicValidator.CheckWallRequired(grid, 3, 3, 1, 1, Direction.Right);

            Assert.AreEqual(RequiredMechanicValidator.RequirementStatus.Required, result.Status);
            Assert.AreEqual(1, result.WithMechanic.SolutionsFound);
            Assert.AreEqual(2, result.WithoutMechanic.SolutionsFound);
        }

        [Test]
        public void Wall_OnABoardBoundary_IsNotRequired()
        {
            // No neighbour exists across a boundary edge, so walling it changes nothing at all.
            CreateGrid(1, 3);
            BlockTestHarness.SetDot(grid[0, 0], pairId: 1);
            BlockTestHarness.SetDot(grid[0, 2], pairId: 1);
            BlockTestHarness.SetWall(grid[0, 0], Direction.Left);

            RequiredMechanicValidator.RequirementResult result =
                RequiredMechanicValidator.CheckWallRequired(grid, 1, 3, 0, 0, Direction.Left);

            Assert.AreEqual(RequiredMechanicValidator.RequirementStatus.NotRequired, result.Status);
        }

        // -- Blocked -----------------------------------------------------------------------------
        // Blocked is one of the two mechanics (with Bridge) that can flip solvability itself,
        // rather than only solution count -- see the class doc. Note there is no NotRequired case
        // here: for a single pair covering 100% of the board, blocking one cell always changes the
        // required-coverage count's parity, so (per the checkerboard argument the class doc
        // describes) it can only ever be Required or break solvability outright, never neutral.

        [Test]
        public void Blocked_ExcludingAnOtherwiseUnreachableCell_IsRequired()
        {
            // (0,3)'s only neighbour is the target dot (0,2) -- reachable only as a dead end, so
            // once it must be covered (unblocked) there is no way to visit it at all.
            CreateGrid(1, 4);
            BlockTestHarness.SetDot(grid[0, 0], pairId: 1);
            BlockTestHarness.SetDot(grid[0, 2], pairId: 1);
            BlockTestHarness.SetBlocked(grid[0, 3]);

            RequiredMechanicValidator.RequirementResult result =
                RequiredMechanicValidator.CheckBlockTypeMechanicRequired(grid, 1, 4, 0, 3);

            Assert.AreEqual(RequiredMechanicValidator.RequirementStatus.Required, result.Status);
            Assert.AreEqual(PuzzleSolver.SolveStatus.Unsolvable, result.WithoutMechanic.Status);
        }

        // -- Bridge ------------------------------------------------------------------------------

        [Test]
        public void Bridge_EnablingATwoPairCrossing_IsRequired()
        {
            // Same plus-shaped board as PuzzleSolverTests' bridge test: without the bridge, the
            // horizontal and vertical pairs simply cannot both cross the centre cell.
            CreateGrid(3, 3);
            BlockTestHarness.SetBlocked(grid[0, 0]);
            BlockTestHarness.SetBlocked(grid[0, 2]);
            BlockTestHarness.SetBlocked(grid[2, 0]);
            BlockTestHarness.SetBlocked(grid[2, 2]);
            BlockTestHarness.SetBridge(grid[1, 1]);
            BlockTestHarness.SetDot(grid[1, 0], pairId: 1);
            BlockTestHarness.SetDot(grid[1, 2], pairId: 1);
            BlockTestHarness.SetDot(grid[0, 1], pairId: 2);
            BlockTestHarness.SetDot(grid[2, 1], pairId: 2);

            RequiredMechanicValidator.RequirementResult result =
                RequiredMechanicValidator.CheckBlockTypeMechanicRequired(grid, 3, 3, 1, 1);

            Assert.AreEqual(RequiredMechanicValidator.RequirementStatus.Required, result.Status);
            Assert.AreEqual(PuzzleSolver.SolveStatus.Unsolvable, result.WithoutMechanic.Status);
        }

        // -- Checkpoint --------------------------------------------------------------------------

        [Test]
        public void Checkpoint_WhenTheBoardAlreadyHasMultipleSolutions_IsNotRequired()
        {
            // With the checkpoint present the board already has 2 solutions (not unique), so
            // pinning pair 1 through (1,1) isn't narrowing anything down to begin with.
            CreateGrid(2, 4);
            BlockTestHarness.SetDot(grid[0, 0], pairId: 1);
            BlockTestHarness.SetDot(grid[1, 0], pairId: 1);
            BlockTestHarness.SetDot(grid[0, 3], pairId: 2);
            BlockTestHarness.SetDot(grid[1, 3], pairId: 2);
            BlockTestHarness.SetRuleCell(grid[1, 1], BlockType.Checkpoint, pairId: 1);

            RequiredMechanicValidator.RequirementResult result = RequiredMechanicValidator.CheckBlockTypeMechanicRequired(
                grid, 2, 4, 1, 1, new PuzzleSolver.SolverOptions(300000, 3));

            Assert.AreEqual(RequiredMechanicValidator.RequirementStatus.NotRequired, result.Status);
            Assert.AreEqual(2, result.WithMechanic.SolutionsFound);
        }

        // -- Inconclusive --------------------------------------------------------------------------

        [Test]
        public void InsufficientBudget_IsInconclusive()
        {
            CreateGrid(2, 3);
            BlockTestHarness.SetDot(grid[0, 0], pairId: 1);
            BlockTestHarness.SetDot(grid[1, 0], pairId: 1);

            RequiredMechanicValidator.RequirementResult result = RequiredMechanicValidator.CheckBlockTypeMechanicRequired(
                grid, 2, 3, 0, 0, new PuzzleSolver.SolverOptions(1));

            Assert.AreEqual(RequiredMechanicValidator.RequirementStatus.Inconclusive, result.Status);
        }
    }
}
