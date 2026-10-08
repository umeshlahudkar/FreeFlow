using System.Reflection;
using FreeFlow.Enums;
using FreeFlow.GamePlay;
using NUnit.Framework;
using UnityEngine;

namespace FreeFlow.Tests
{
    /// <summary>
    /// Builds and pokes <see cref="Block"/> instances for tests without going through
    /// <see cref="Block.SetBlock"/>. SetBlock drives visuals (Image/DOTween, plus
    /// GamePlayController.Instance.GetColor lookups) that need a fully wired prefab and a live
    /// GamePlayController singleton -- neither of which the rule predicates or LevelValidator
    /// actually read. Reflection sets exactly the private state those two care about instead, so
    /// tests can run on a bare GameObject with no scene, prefab, or singleton required.
    /// </summary>
    internal static class BlockTestHarness
    {
        public static Block CreateBlock(int row = 0, int col = 0)
        {
            GameObject go = new GameObject("TestBlock_" + row + "_" + col);
            Block block = go.AddComponent<Block>();
            SetField(block, "row_ID", row);
            SetField(block, "coloum_ID", col);
            return block;
        }

        public static void Destroy(Block block)
        {
            if (block != null) { Object.DestroyImmediate(block.gameObject); }
        }

        public static void SetDot(Block block, int pairId, int secondPairId = 0, int thirdPairId = 0,
            int fourthPairId = 0, PairColorType color = PairColorType.Red)
        {
            SetField(block, "isPairBlock", true);
            SetField(block, "pairColorType", color);
            SetField(block, "pairId", pairId);
            SetField(block, "secondPairId", secondPairId);
            SetField(block, "thirdPairId", thirdPairId);
            SetField(block, "fourthPairId", fourthPairId);
        }

        public static void SetRuleCell(Block block, BlockType type, int pairId, int secondPairId = 0)
        {
            SetField(block, "blockType", type);
            SetField(block, "pairId", pairId);
            SetField(block, "secondPairId", secondPairId);
        }

        public static void SetBlocked(Block block)
        {
            SetField(block, "blockType", BlockType.Blocked);
        }

        public static void SetOneWay(Block block, Direction requiredEntryDirection)
        {
            SetField(block, "blockType", BlockType.OneWay);
            SetField(block, "requiredEntryDirection", requiredEntryDirection);
        }

        public static void SetArrow(Block block, Direction forcedExitDirection)
        {
            SetField(block, "blockType", BlockType.Arrow);
            SetField(block, "forcedExitDirection", forcedExitDirection);
        }

        public static void SetBridge(Block block)
        {
            SetField(block, "blockType", BlockType.Bridge);
        }

        public static void SetWall(Block block, Direction edge)
        {
            int bit;
            switch (edge)
            {
                case Direction.Left: bit = 1; break;
                case Direction.Right: bit = 2; break;
                case Direction.Up: bit = 4; break;
                case Direction.Down: bit = 8; break;
                default: return;
            }

            int current = (int)GetField(block, "wallMask");
            SetField(block, "wallMask", current | bit);
        }

        /// <summary>
        /// Claims direction slot <paramref name="dir"/> on <paramref name="block"/> for
        /// <paramref name="pairId"/>, the way a real crossing path would after
        /// <see cref="Block.HighlightBlockDirection"/> -- but without touching any visuals, so it
        /// works on a bare test Block with no direction-bar images wired up. Used to set up
        /// Bridge axis-occupancy scenarios for <see cref="Block.CanAcceptEntry"/>.
        /// </summary>
        public static void ClaimDirection(Block block, Direction dir, int pairId)
        {
            int[] owners = (int[])GetField(block, "directionOwnerPairId");
            owners[(int)dir - 1] = pairId;

            int[] occupantPairId = (int[])GetField(block, "occupantPairId");
            int occupantCount = (int)GetField(block, "occupantCount");
            for (int i = 0; i < occupantCount; i++)
            {
                if (occupantPairId[i] == pairId) { return; }
            }

            occupantPairId[occupantCount] = pairId;
            SetField(block, "occupantCount", occupantCount + 1);
        }

        private static void SetField(Block block, string fieldName, object value)
        {
            FieldInfo field = typeof(Block).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "Block has no private field named '" + fieldName +
                "' -- BlockTestHarness needs updating to match Block.cs.");
            field.SetValue(block, value);
        }

        private static object GetField(Block block, string fieldName)
        {
            FieldInfo field = typeof(Block).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "Block has no private field named '" + fieldName +
                "' -- BlockTestHarness needs updating to match Block.cs.");
            return field.GetValue(block);
        }
    }
}
