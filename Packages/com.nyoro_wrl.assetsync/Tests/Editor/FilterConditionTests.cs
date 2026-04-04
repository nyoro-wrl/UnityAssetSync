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
        private string _txtAssetPath;

        [SetUp]
        public void SetUp()
        {
            Directory.CreateDirectory(TestDir);
            _txtAssetPath = TestDir + "/test.txt";
            File.WriteAllText(_txtAssetPath, "hello");
            AssetDatabase.Refresh();
        }

        [TearDown]
        public void TearDown()
        {
            string fullPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Application.dataPath), TestDir));
            FileUtil.DeleteFileOrDirectory(fullPath);
            FileUtil.DeleteFileOrDirectory(fullPath + ".meta");
            AssetDatabase.Refresh();
        }

        // ─── PassesFilters ────────────────────────────────────────────

        // #1
        [Test]
        public void EmptyFilterList_ReturnsTrue()
        {
            Assert.IsTrue(AssetSyncer.PassesFilters(_txtAssetPath, new List<FilterCondition>()));
        }

        // #9
        [Test]
        public void MultipleFilters_And_BothPass_ReturnsTrue()
        {
            // TextAsset matches, Texture2D inverted-matches (i.e. NOT Texture2D)
            var filters = new List<FilterCondition>
            {
                new FilterCondition { singleTypeName = typeof(TextAsset).AssemblyQualifiedName },
                new FilterCondition { singleTypeName = typeof(Texture2D).AssemblyQualifiedName, invert = true }
            };
            Assert.IsTrue(AssetSyncer.PassesFilters(_txtAssetPath, filters));
        }

        // #10
        [Test]
        public void MultipleFilters_And_OneFails_ReturnsFalse()
        {
            // Both conditions reference TextAsset but one is inverted ↁEcontradicts itself
            var filters = new List<FilterCondition>
            {
                new FilterCondition { singleTypeName = typeof(TextAsset).AssemblyQualifiedName },
                new FilterCondition { singleTypeName = typeof(TextAsset).AssemblyQualifiedName, invert = true }
            };
            Assert.IsFalse(AssetSyncer.PassesFilters(_txtAssetPath, filters));
        }

        // ─── EvaluateCondition  Esingle type ─────────────────────────

        // #2
        [Test]
        public void SingleType_Matching_ReturnsTrue()
        {
            var f = new FilterCondition { singleTypeName = typeof(TextAsset).AssemblyQualifiedName };
            Assert.IsTrue(AssetSyncer.EvaluateCondition(f, _txtAssetPath));
        }

        // #3
        [Test]
        public void SingleType_NonMatching_ReturnsFalse()
        {
            var f = new FilterCondition { singleTypeName = typeof(Texture2D).AssemblyQualifiedName };
            Assert.IsFalse(AssetSyncer.EvaluateCondition(f, _txtAssetPath));
        }

        // #4
        [Test]
        public void SingleType_Invert_Matching_ReturnsFalse()
        {
            var f = new FilterCondition { singleTypeName = typeof(TextAsset).AssemblyQualifiedName, invert = true };
            Assert.IsFalse(AssetSyncer.EvaluateCondition(f, _txtAssetPath));
        }

        // #5
        [Test]
        public void SingleType_Invert_NonMatching_ReturnsTrue()
        {
            var f = new FilterCondition { singleTypeName = typeof(Texture2D).AssemblyQualifiedName, invert = true };
            Assert.IsTrue(AssetSyncer.EvaluateCondition(f, _txtAssetPath));
        }

        // #11
        [Test]
        public void EmptySingleTypeName_ReturnsTrue()
        {
            var f = new FilterCondition { singleTypeName = "" };
            Assert.IsTrue(AssetSyncer.EvaluateCondition(f, _txtAssetPath));
        }

        // #13  Einvert + 空垁E= 制限なし（修正後�E仕様！E        [Test]
        public void Invert_EmptySingleTypeName_ReturnsTrue()
        {
            var f = new FilterCondition { singleTypeName = "", invert = true };
            Assert.IsTrue(AssetSyncer.EvaluateCondition(f, _txtAssetPath));
        }

        // ─── EvaluateCondition  Emultiple types ──────────────────────

        // #6
        [Test]
        public void MultipleTypes_MatchesOne_ReturnsTrue()
        {
            var f = new FilterCondition
            {
                useMultipleTypes = true,
                multipleTypeNames = new List<string>
                {
                    typeof(TextAsset).AssemblyQualifiedName,
                    typeof(Texture2D).AssemblyQualifiedName
                }
            };
            Assert.IsTrue(AssetSyncer.EvaluateCondition(f, _txtAssetPath));
        }

        // #7
        [Test]
        public void MultipleTypes_MatchesNone_ReturnsFalse()
        {
            var f = new FilterCondition
            {
                useMultipleTypes = true,
                multipleTypeNames = new List<string>
                {
                    typeof(Texture2D).AssemblyQualifiedName,
                    typeof(Material).AssemblyQualifiedName
                }
            };
            Assert.IsFalse(AssetSyncer.EvaluateCondition(f, _txtAssetPath));
        }

        // #8
        [Test]
        public void MultipleTypes_Invert_MatchesOne_ReturnsFalse()
        {
            var f = new FilterCondition
            {
                useMultipleTypes = true,
                invert = true,
                multipleTypeNames = new List<string> { typeof(TextAsset).AssemblyQualifiedName }
            };
            Assert.IsFalse(AssetSyncer.EvaluateCondition(f, _txtAssetPath));
        }

        // #12
        [Test]
        public void EmptyMultipleTypeNames_ReturnsTrue()
        {
            var f = new FilterCondition
            {
                useMultipleTypes = true,
                multipleTypeNames = new List<string>()
            };
            Assert.IsTrue(AssetSyncer.EvaluateCondition(f, _txtAssetPath));
        }

        // #14  Einvert + 空リスチE= 制限なし（修正後�E仕様！E        [Test]
        public void Invert_EmptyMultipleTypeNames_ReturnsTrue()
        {
            var f = new FilterCondition
            {
                useMultipleTypes = true,
                invert = true,
                multipleTypeNames = new List<string>()
            };
            Assert.IsTrue(AssetSyncer.EvaluateCondition(f, _txtAssetPath));
        }
    }
}
