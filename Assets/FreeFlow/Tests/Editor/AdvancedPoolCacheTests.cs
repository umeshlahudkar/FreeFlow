using System.Collections.Generic;
using FreeFlow.GamePlay;
using NUnit.Framework;
using UnityEditor;

namespace FreeFlow.Tests
{
    /// <summary>
    /// The Advanced gather checkpoint must give back exactly the boards it saved: a resumed
    /// 16-hour build that silently lost a column (a wall, an arrow's direction, a bridge's second
    /// colour) would ship levels that no longer match their stored answers.
    ///
    /// Uses shipped Advanced levels as material because they carry every per-cell column the
    /// generator writes, and a fake board size (99) so the real Library/AdvancedPoolCache files of
    /// any build in progress are never touched.
    /// </summary>
    public class AdvancedPoolCacheTests
    {
        private const int FakeSize = 99;

        private static readonly string[] Samples =
        {
            "Assets/FreeFlow/Resources/Levels/Advanced/7x7/Level_12.asset", // Bridge: secondPairId
            "Assets/FreeFlow/Resources/Levels/Advanced/7x7/Level_9.asset",  // Arrow: forcedExitDirection
            "Assets/FreeFlow/Resources/Levels/Advanced/6x6/Level_9.asset",  // Wall: wallMask
            "Assets/FreeFlow/Resources/Levels/Advanced/6x6/Level_6.asset",  // OneWay + blocked cells
        };

        [TearDown]
        public void RemoveFakeCache()
        {
            LevelGenerator.DeleteAdvancedCache(FakeSize);
        }

        [Test]
        public void SavedCheckpointLoadsBackIdentically()
        {
            LevelGenerator.AdvancedPoolCache cache = new LevelGenerator.AdvancedPoolCache
            {
                size = FakeSize, count = 100, poolTarget = 900, cellsPerColour = 7,
                deficitShift = 0, seedRound = 2
            };

            List<LevelData> originals = new List<LevelData>();
            for (int i = 0; i < Samples.Length; i++)
            {
                SingleLevelDataSO so = AssetDatabase.LoadAssetAtPath<SingleLevelDataSO>(Samples[i]);
                Assert.IsNotNull(so, "sample level missing: " + Samples[i]);
                originals.Add(so.levelData);
                cache.pool.Add(new LevelGenerator.AdvancedPoolEntry
                {
                    data = so.levelData, mechanic = "Rule" + i, instances = i + 2, deficit = 1 + (i % 3),
                    bucket = i == 0 ? string.Empty : "Rule" + i + "@1", recipeTally = "Rule" + i + "x" + (i + 2)
                });
            }

            LevelGenerator.SaveAdvancedCheckpoint(cache, 12345, 300, 4, 70, 1, 28, 7200.5);
            LevelGenerator.AdvancedPoolCache loaded = LevelGenerator.LoadAdvancedCache(FakeSize);

            Assert.AreEqual(FakeSize, loaded.size);
            Assert.AreEqual(100, loaded.count);
            Assert.AreEqual(900, loaded.poolTarget);
            Assert.AreEqual(7, loaded.cellsPerColour);
            Assert.AreEqual(2, loaded.seedRound);
            Assert.AreEqual(12345, loaded.attempts);
            Assert.AreEqual(300, loaded.generated);
            Assert.AreEqual(4, loaded.duplicates);
            Assert.AreEqual(70, loaded.unsound);
            Assert.AreEqual(1, loaded.notUnique);
            Assert.AreEqual(28, loaded.decorative);
            Assert.AreEqual(7200.5, loaded.gatherSeconds, 1e-6);
            Assert.AreEqual(originals.Count, loaded.pool.Count);

            for (int i = 0; i < originals.Count; i++)
            {
                LevelGenerator.AdvancedPoolEntry e = loaded.pool[i];
                Assert.AreEqual("Rule" + i, e.mechanic);
                Assert.AreEqual(i + 2, e.instances);
                Assert.AreEqual(1 + (i % 3), e.deficit);
                Assert.AreEqual(i == 0 ? string.Empty : "Rule" + i + "@1", e.bucket);
                Assert.AreEqual("Rule" + i + "x" + (i + 2), e.recipeTally);
                AssertSameLevel(originals[i], e.data, Samples[i]);
            }
        }

        [Test]
        public void MissingCheckpointLoadsEmpty()
        {
            LevelGenerator.DeleteAdvancedCache(FakeSize);
            LevelGenerator.AdvancedPoolCache loaded = LevelGenerator.LoadAdvancedCache(FakeSize);
            Assert.IsNotNull(loaded.pool);
            Assert.AreEqual(0, loaded.pool.Count);
            Assert.AreEqual(0, loaded.seedRound, "a fresh run must use the original seed");
            Assert.AreEqual(0, loaded.attempts, "a fresh run must start its attempt counter at 0");
        }

        private static void AssertSameLevel(LevelData a, LevelData b, string label)
        {
            Assert.AreEqual(a.gridSize, b.gridSize, label);
            Assert.AreEqual(a.pairCount, b.pairCount, label);
            Assert.AreEqual(a.difficultyScore, b.difficultyScore, 1e-4f, label);
            Assert.AreEqual(a.gridRows.Length, b.gridRows.Length, label);
            for (int r = 0; r < a.gridRows.Length; r++)
            {
                GridRow x = a.gridRows[r], y = b.gridRows[r];
                string at = label + " row " + r;
                CollectionAssert.AreEqual(x.coloum, y.coloum, at + " coloum");
                CollectionAssert.AreEqual(x.pairId, y.pairId, at + " pairId");
                CollectionAssert.AreEqual(x.blockType, y.blockType, at + " blockType");
                CollectionAssert.AreEqual(x.wallMask, y.wallMask, at + " wallMask");
                CollectionAssert.AreEqual(x.requiredEntryDirection, y.requiredEntryDirection, at + " entry");
                CollectionAssert.AreEqual(x.forcedExitDirection, y.forcedExitDirection, at + " exit");
                CollectionAssert.AreEqual(x.secondPairId, y.secondPairId, at + " secondPairId");
                CollectionAssert.AreEqual(Norm(x.thirdPairId), Norm(y.thirdPairId), at + " thirdPairId");
                CollectionAssert.AreEqual(Norm(x.fourthPairId), Norm(y.fourthPairId), at + " fourthPairId");
                CollectionAssert.AreEqual(x.solutionPairId, y.solutionPairId, at + " solutionPairId");
            }
        }

        // JsonUtility writes a null array as an empty one; both mean "no third/fourth identity".
        private static int[] Norm(int[] values) { return values ?? new int[0]; }
    }
}
