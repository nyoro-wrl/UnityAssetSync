using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Nyorowrl.AssetSync;
using Nyorowrl.AssetSync.Editor;

namespace Nyorowrl.AssetSync.Editor.Tests
{
    public class FilterConditionTests
    {
        private const string TestDir = "Assets/AssetSyncFilterTest";
        private const string OtherDir = "Assets/AssetSyncFilterTest_Other";

        private string _txtAssetPath;
        private string _nestedAssetPath;
        private string _outsideAssetPath;

        [SetUp]
        public void SetUp()
        {
            Directory.CreateDirectory(TestDir);
            Directory.CreateDirectory(TestDir + "/Sub");
            Directory.CreateDirectory(OtherDir);

            _txtAssetPath = TestDir + "/test.txt";
            _nestedAssetPath = TestDir + "/Sub/nested.txt";
            _outsideAssetPath = OtherDir + "/outside.txt";

            File.WriteAllText(_txtAssetPath, "hello");
            File.WriteAllText(_nestedAssetPath, "hello nested");
            File.WriteAllText(_outsideAssetPath, "outside");
            AssetDatabase.Refresh();
        }

        [TearDown]
        public void TearDown()
        {
            DeleteAssetDirectory(TestDir);
            DeleteAssetDirectory(OtherDir);
            AssetDatabase.Refresh();
        }

        [Test]
        public void EmptyFilterList_ReturnsTrue()
        {
            Assert.IsTrue(AssetSyncer.PassesFilters(_txtAssetPath, new List<FilterCondition>()));
        }

        [Test]
        public void MultipleFilters_And_BothPass_ReturnsTrue()
        {
            var filters = new List<FilterCondition>
            {
                new FilterCondition { multipleTypeNames = new List<string> { typeof(TextAsset).AssemblyQualifiedName } },
                new FilterCondition { multipleTypeNames = new List<string> { typeof(Texture2D).AssemblyQualifiedName }, invert = true }
            };

            Assert.IsTrue(AssetSyncer.PassesFilters(_txtAssetPath, filters));
        }

        [Test]
        public void MultipleFilters_And_OneFails_ReturnsFalse()
        {
            var filters = new List<FilterCondition>
            {
                new FilterCondition { multipleTypeNames = new List<string> { typeof(TextAsset).AssemblyQualifiedName } },
                new FilterCondition { multipleTypeNames = new List<string> { typeof(TextAsset).AssemblyQualifiedName }, invert = true }
            };

            Assert.IsFalse(AssetSyncer.PassesFilters(_txtAssetPath, filters));
        }

        [Test]
        public void PassesFilters_AssetIncludes_AreEvaluatedAsOr()
        {
            var filters = new List<FilterCondition>
            {
                new FilterCondition
                {
                    targetKind = FilterConditionTargetKind.Asset,
                    multipleAssetGuids = new List<string> { AssetDatabase.AssetPathToGUID(_txtAssetPath) }
                },
                new FilterCondition
                {
                    targetKind = FilterConditionTargetKind.Asset,
                    multipleAssetGuids = new List<string> { AssetDatabase.AssetPathToGUID(_nestedAssetPath) }
                }
            };

            Assert.IsTrue(AssetSyncer.PassesFilters(_txtAssetPath, filters, TestDir));
            Assert.IsTrue(AssetSyncer.PassesFilters(_nestedAssetPath, filters, TestDir));
        }

        [Test]
        public void PassesFilters_AssetIncludeOutsideSource_DoesNotSatisfyOr()
        {
            var filters = new List<FilterCondition>
            {
                new FilterCondition
                {
                    targetKind = FilterConditionTargetKind.Asset,
                    multipleAssetGuids = new List<string> { AssetDatabase.AssetPathToGUID(_outsideAssetPath) }
                },
                new FilterCondition
                {
                    targetKind = FilterConditionTargetKind.Asset,
                    multipleAssetGuids = new List<string> { AssetDatabase.AssetPathToGUID(_txtAssetPath) }
                }
            };

            Assert.IsTrue(AssetSyncer.PassesFilters(_txtAssetPath, filters, TestDir));
            Assert.IsFalse(AssetSyncer.PassesFilters(_nestedAssetPath, filters, TestDir));
        }

        [Test]
        public void PassesFilters_TypeIncludeAndAssetInclude_AreCombinedWithOr()
        {
            var filters = new List<FilterCondition>
            {
                new FilterCondition
                {
                    multipleTypeNames = new List<string> { typeof(Texture2D).AssemblyQualifiedName }
                },
                new FilterCondition
                {
                    targetKind = FilterConditionTargetKind.Asset,
                    multipleAssetGuids = new List<string> { AssetDatabase.AssetPathToGUID(_txtAssetPath) }
                }
            };

            Assert.IsTrue(AssetSyncer.PassesFilters(_txtAssetPath, filters, TestDir));
            Assert.IsFalse(AssetSyncer.PassesFilters(_nestedAssetPath, filters, TestDir));
        }

        [Test]
        public void TypeList_SingleEntry_Matching_ReturnsTrue()
        {
            var f = new FilterCondition { multipleTypeNames = new List<string> { typeof(TextAsset).AssemblyQualifiedName } };
            Assert.IsTrue(AssetSyncer.EvaluateCondition(f, _txtAssetPath));
        }

        [Test]
        public void TypeList_SingleEntry_NonMatching_ReturnsFalse()
        {
            var f = new FilterCondition { multipleTypeNames = new List<string> { typeof(Texture2D).AssemblyQualifiedName } };
            Assert.IsFalse(AssetSyncer.EvaluateCondition(f, _txtAssetPath));
        }

        [Test]
        public void TypeList_SingleEntry_InvertNonMatching_ReturnsTrue()
        {
            var f = new FilterCondition { multipleTypeNames = new List<string> { typeof(Texture2D).AssemblyQualifiedName }, invert = true };
            Assert.IsTrue(AssetSyncer.EvaluateCondition(f, _txtAssetPath));
        }

        [Test]
        public void EmptyTypeEntry_ReturnsTrue()
        {
            var f = new FilterCondition { multipleTypeNames = new List<string> { string.Empty } };
            Assert.IsTrue(AssetSyncer.EvaluateCondition(f, _txtAssetPath));
        }

        [Test]
        public void Invert_EmptyTypeEntry_ReturnsTrue()
        {
            var f = new FilterCondition { multipleTypeNames = new List<string> { string.Empty }, invert = true };
            Assert.IsTrue(AssetSyncer.EvaluateCondition(f, _txtAssetPath));
        }

        [Test]
        public void MultipleTypes_MatchesOne_ReturnsTrue()
        {
            var f = new FilterCondition
            {
                multipleTypeNames = new List<string>
                {
                    typeof(TextAsset).AssemblyQualifiedName,
                    typeof(Texture2D).AssemblyQualifiedName
                }
            };

            Assert.IsTrue(AssetSyncer.EvaluateCondition(f, _txtAssetPath));
        }

        [Test]
        public void MultipleTypes_MatchesNone_ReturnsFalse()
        {
            var f = new FilterCondition
            {
                multipleTypeNames = new List<string>
                {
                    typeof(Texture2D).AssemblyQualifiedName,
                    typeof(Material).AssemblyQualifiedName
                }
            };

            Assert.IsFalse(AssetSyncer.EvaluateCondition(f, _txtAssetPath));
        }

        [Test]
        public void MultipleTypes_Invert_MatchesOne_ReturnsFalse()
        {
            var f = new FilterCondition
            {
                invert = true,
                multipleTypeNames = new List<string> { typeof(TextAsset).AssemblyQualifiedName }
            };

            Assert.IsFalse(AssetSyncer.EvaluateCondition(f, _txtAssetPath));
        }

        [Test]
        public void AssetList_SingleEntry_FileMatch_ReturnsTrue()
        {
            var f = new FilterCondition
            {
                targetKind = FilterConditionTargetKind.Asset,
                multipleAssetGuids = new List<string> { AssetDatabase.AssetPathToGUID(_txtAssetPath) }
            };

            Assert.IsTrue(AssetSyncer.EvaluateCondition(f, _txtAssetPath, TestDir));
        }

        [Test]
        public void AssetList_SingleEntry_FileMismatch_ReturnsFalse()
        {
            var f = new FilterCondition
            {
                targetKind = FilterConditionTargetKind.Asset,
                multipleAssetGuids = new List<string> { AssetDatabase.AssetPathToGUID(_txtAssetPath) }
            };

            Assert.IsFalse(AssetSyncer.EvaluateCondition(f, _nestedAssetPath, TestDir));
        }

        [Test]
        public void AssetList_SingleEntry_InvertFileMismatch_ReturnsTrue()
        {
            var f = new FilterCondition
            {
                targetKind = FilterConditionTargetKind.Asset,
                invert = true,
                multipleAssetGuids = new List<string> { AssetDatabase.AssetPathToGUID(_txtAssetPath) }
            };

            Assert.IsTrue(AssetSyncer.EvaluateCondition(f, _nestedAssetPath, TestDir));
        }

        [Test]
        public void AssetList_SingleEntry_FolderMatchesDescendant_ReturnsTrue()
        {
            var f = new FilterCondition
            {
                targetKind = FilterConditionTargetKind.Asset,
                multipleAssetGuids = new List<string> { AssetDatabase.AssetPathToGUID(TestDir + "/Sub") }
            };

            Assert.IsTrue(AssetSyncer.EvaluateCondition(f, _nestedAssetPath, TestDir));
        }

        [Test]
        public void AssetMultiple_MatchWithInvalidEntries_ReturnsTrue()
        {
            var f = new FilterCondition
            {
                targetKind = FilterConditionTargetKind.Asset,
                multipleAssetGuids = new List<string>
                {
                    AssetDatabase.AssetPathToGUID(_outsideAssetPath),
                    "00000000000000000000000000000000",
                    AssetDatabase.AssetPathToGUID(_txtAssetPath)
                }
            };

            Assert.IsTrue(AssetSyncer.EvaluateCondition(f, _txtAssetPath, TestDir));
        }

        [Test]
        public void AssetList_SingleEntry_OutsideSource_NoOp_ReturnsTrue()
        {
            var f = new FilterCondition
            {
                targetKind = FilterConditionTargetKind.Asset,
                multipleAssetGuids = new List<string> { AssetDatabase.AssetPathToGUID(_outsideAssetPath) }
            };

            Assert.IsTrue(AssetSyncer.EvaluateCondition(f, _txtAssetPath, TestDir));
        }

        [Test]
        public void AssetList_SingleEntry_OutsideSource_InvertNoOp_ReturnsTrue()
        {
            var f = new FilterCondition
            {
                targetKind = FilterConditionTargetKind.Asset,
                invert = true,
                multipleAssetGuids = new List<string> { AssetDatabase.AssetPathToGUID(_outsideAssetPath) }
            };

            Assert.IsTrue(AssetSyncer.EvaluateCondition(f, _txtAssetPath, TestDir));
        }

        [Test]
        public void AssetMultiple_AllInvalid_NoOp_ReturnsTrue()
        {
            var f = new FilterCondition
            {
                targetKind = FilterConditionTargetKind.Asset,
                multipleAssetGuids = new List<string>
                {
                    AssetDatabase.AssetPathToGUID(_outsideAssetPath),
                    "00000000000000000000000000000000"
                }
            };

            Assert.IsTrue(AssetSyncer.EvaluateCondition(f, _txtAssetPath, TestDir));
        }

        private static void DeleteAssetDirectory(string path)
        {
            string fullPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Application.dataPath), path));
            FileUtil.DeleteFileOrDirectory(fullPath);
            FileUtil.DeleteFileOrDirectory(fullPath + ".meta");
        }
    }
}
