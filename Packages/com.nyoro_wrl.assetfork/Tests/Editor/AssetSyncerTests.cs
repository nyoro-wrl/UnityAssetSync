using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Nyorowrl.Assetfork;
using Nyorowrl.Assetfork.Editor;

namespace Nyorowrl.Assetfork.Editor.Tests
{
    public class AssetSyncerTests
    {
        private string _testRoot;
        private string _srcAssetPath;
        private string _dstAssetPath;
        private string _srcFullPath;
        private string _dstFullPath;

        [SetUp]
        public void SetUp()
        {
            string uid = Guid.NewGuid().ToString("N").Substring(0, 8);
            _testRoot = "Assets/AssetForkTest_" + uid;
            _srcAssetPath = _testRoot + "/Src";
            _dstAssetPath = _testRoot + "/Dst";

            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            _srcFullPath = Path.GetFullPath(Path.Combine(projectRoot, _srcAssetPath));
            _dstFullPath = Path.GetFullPath(Path.Combine(projectRoot, _dstAssetPath));

            Directory.CreateDirectory(_srcFullPath);
            Directory.CreateDirectory(_dstFullPath);
        }

        [TearDown]
        public void TearDown()
        {
            string manifestPath = ManifestFilePath(_dstAssetPath);
            if (File.Exists(manifestPath))
                File.Delete(manifestPath);

            // AssetDatabase.DeleteAsset はフォルダが未登録だと失敗するため、
            // FileUtil で直接削除してから Refresh する
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string fullTestRoot = Path.GetFullPath(Path.Combine(projectRoot, _testRoot));
            FileUtil.DeleteFileOrDirectory(fullTestRoot);
            FileUtil.DeleteFileOrDirectory(fullTestRoot + ".meta");
            AssetDatabase.Refresh();
        }

        // ─── Helpers ──────────────────────────────────────────────────

        private void WriteSrc(string relPath, string content = "test")
        {
            string full = Path.Combine(_srcFullPath, relPath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllText(full, content);
        }

        private void WriteDst(string relPath, string content = "test")
        {
            string full = Path.Combine(_dstFullPath, relPath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllText(full, content);
        }

        private void DeleteSrc(string relPath)
        {
            string full = Path.Combine(_srcFullPath, relPath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(full)) File.Delete(full);
            // SyncConfig の Refresh でインポート済みの .meta も削除しないと孤立 .meta 警告が出る
            string meta = full + ".meta";
            if (File.Exists(meta)) File.Delete(meta);
        }

        private bool DstExists(string relPath)
        {
            return File.Exists(Path.Combine(_dstFullPath, relPath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private string ReadDst(string relPath)
        {
            return File.ReadAllText(Path.Combine(_dstFullPath, relPath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private DateTime DstWriteTime(string relPath)
        {
            return File.GetLastWriteTimeUtc(Path.Combine(_dstFullPath, relPath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private SyncConfig MakeConfig(List<FilterCondition> filters = null, bool includeSubdirectories = true)
        {
            return new SyncConfig
            {
                configName = "TestConfig",
                enabled = true,
                includeSubdirectories = includeSubdirectories,
                sourcePath = _srcAssetPath,
                destinationPath = _dstAssetPath,
                filters = filters ?? new List<FilterCondition>()
            };
        }

        private string ManifestFilePath(string dstAssetRoot)
        {
            using var md5 = MD5.Create();
            string hash = BitConverter.ToString(md5.ComputeHash(Encoding.UTF8.GetBytes(dstAssetRoot))).Replace("-", "");
            string dir = Path.Combine(Application.dataPath, "..", "Library", "AssetFork");
            return Path.Combine(dir, hash + ".json");
        }

        private bool ManifestContains(string relPath)
        {
            string path = ManifestFilePath(_dstAssetPath);
            if (!File.Exists(path)) return false;
            return File.ReadAllText(path).Contains("\"" + relPath.Replace('\\', '/') + "\"");
        }

        // ─── ShouldCopy (#15–18) ──────────────────────────────────────

        // #15
        [Test]
        public void ShouldCopy_DstNotExists_ReturnsTrue()
        {
            string tmp = Path.Combine(Path.GetTempPath(), "aftest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try
            {
                string src = Path.Combine(tmp, "a.txt");
                File.WriteAllText(src, "x");
                Assert.IsTrue(AssetSyncer.ShouldCopy(src, Path.Combine(tmp, "b.txt")));
            }
            finally { Directory.Delete(tmp, true); }
        }

        // #16
        [Test]
        public void ShouldCopy_IdenticalContent_ReturnsFalse()
        {
            string tmp = Path.Combine(Path.GetTempPath(), "aftest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try
            {
                string src = Path.Combine(tmp, "a.txt");
                string dst = Path.Combine(tmp, "b.txt");
                File.WriteAllText(src, "hello");
                File.WriteAllText(dst, "hello");
                Assert.IsFalse(AssetSyncer.ShouldCopy(src, dst));
            }
            finally { Directory.Delete(tmp, true); }
        }

        // #17
        [Test]
        public void ShouldCopy_DifferentSize_ReturnsTrue()
        {
            string tmp = Path.Combine(Path.GetTempPath(), "aftest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try
            {
                string src = Path.Combine(tmp, "a.txt");
                string dst = Path.Combine(tmp, "b.txt");
                File.WriteAllText(src, "hello world");
                File.WriteAllText(dst, "hi");
                Assert.IsTrue(AssetSyncer.ShouldCopy(src, dst));
            }
            finally { Directory.Delete(tmp, true); }
        }

        // #18
        [Test]
        public void ShouldCopy_SameSizeDifferentContent_ReturnsTrue()
        {
            string tmp = Path.Combine(Path.GetTempPath(), "aftest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try
            {
                string src = Path.Combine(tmp, "a.txt");
                string dst = Path.Combine(tmp, "b.txt");
                File.WriteAllBytes(src, new byte[] { 0x41, 0x42 }); // "AB"
                File.WriteAllBytes(dst, new byte[] { 0x43, 0x44 }); // "CD"
                Assert.IsTrue(AssetSyncer.ShouldCopy(src, dst));
            }
            finally { Directory.Delete(tmp, true); }
        }

        // ─── SyncConfig バリデーション (#19–23) ───────────────────────

        // #19
        [Test]
        public void SyncConfig_Disabled_DoesNotSync()
        {
            WriteSrc("file.txt");
            var config = MakeConfig();
            config.enabled = false;
            AssetSyncer.SyncConfig(config);
            Assert.IsFalse(DstExists("file.txt"));
        }

        // #20
        [Test]
        public void SyncConfig_EmptySourcePath_LogsWarning()
        {
            LogAssert.Expect(LogType.Warning, new Regex(".*is not set.*"));
            AssetSyncer.SyncConfig(new SyncConfig { destinationPath = _dstAssetPath, enabled = true });
        }

        // #21
        [Test]
        public void SyncConfig_EmptyDestinationPath_LogsWarning()
        {
            LogAssert.Expect(LogType.Warning, new Regex(".*is not set.*"));
            AssetSyncer.SyncConfig(new SyncConfig { sourcePath = _srcAssetPath, enabled = true });
        }

        // #22
        [Test]
        public void SyncConfig_SourceDirNotExist_LogsWarning()
        {
            LogAssert.Expect(LogType.Warning, new Regex(".*does not exist.*"));
            AssetSyncer.SyncConfig(new SyncConfig
            {
                sourcePath = _testRoot + "/NoSuchDir",
                destinationPath = _dstAssetPath,
                enabled = true
            });
        }

        // #23
        [Test]
        public void SyncConfig_SrcEqualsDst_LogsWarning()
        {
            LogAssert.Expect(LogType.Warning, new Regex(".*same directory.*"));
            AssetSyncer.SyncConfig(new SyncConfig
            {
                sourcePath = _srcAssetPath,
                destinationPath = _srcAssetPath,
                enabled = true
            });
        }

        [Test]
        public void SyncConfig_DstNestedUnderSrc_LogsWarningAndSkips()
        {
            WriteSrc("file.txt", "data");

            string nestedDst = _srcAssetPath + "/NestedDst";
            LogAssert.Expect(LogType.Warning, new Regex(".*must not be nested.*"));

            int copied = AssetSyncer.SyncConfig(new SyncConfig
            {
                sourcePath = _srcAssetPath,
                destinationPath = nestedDst,
                enabled = true
            });

            Assert.AreEqual(0, copied);
            string nestedDstFull = Path.Combine(_srcFullPath, "NestedDst", "file.txt");
            Assert.IsFalse(File.Exists(nestedDstFull));
        }

        [Test]
        public void SyncConfig_SrcNestedUnderDst_LogsWarningAndSkips()
        {
            WriteSrc("file.txt", "data");

            LogAssert.Expect(LogType.Warning, new Regex(".*must not be nested.*"));

            int copied = AssetSyncer.SyncConfig(new SyncConfig
            {
                sourcePath = _srcAssetPath,
                destinationPath = _testRoot,
                enabled = true
            });

            Assert.AreEqual(0, copied);
            Assert.IsFalse(File.Exists(Path.Combine(Path.GetDirectoryName(Application.dataPath), _testRoot, "file.txt")));
        }

        // ─── Phase 1: コピー (#24–32) ─────────────────────────────────

        // #24
        [Test]
        public void Phase1_NewFile_CopiedToDst()
        {
            WriteSrc("file.txt", "abc");
            AssetSyncer.SyncConfig(MakeConfig());
            Assert.IsTrue(DstExists("file.txt"));
            Assert.AreEqual("abc", ReadDst("file.txt"));
        }

        // #25
        [Test]
        public void Phase1_UpdatedFile_CopiedToDst()
        {
            WriteSrc("file.txt", "v1");
            AssetSyncer.SyncConfig(MakeConfig());
            Assert.AreEqual("v1", ReadDst("file.txt"));

            WriteSrc("file.txt", "v2");
            AssetSyncer.SyncConfig(MakeConfig());
            Assert.AreEqual("v2", ReadDst("file.txt"));
        }

        // #26
        [Test]
        public void Phase1_UnchangedFile_NotOverwritten()
        {
            WriteSrc("file.txt", "same");
            AssetSyncer.SyncConfig(MakeConfig());

            DateTime before = DstWriteTime("file.txt");
            System.Threading.Thread.Sleep(50);

            AssetSyncer.SyncConfig(MakeConfig());
            DateTime after = DstWriteTime("file.txt");

            Assert.AreEqual(before, after, "Content unchanged → dst must not be overwritten");
        }

        // #27
        [Test]
        public void Phase1_FileFailsFilter_NotCopied()
        {
            WriteSrc("file.txt", "data");
            AssetDatabase.Refresh();

            // Texture2D 型のみ許可 → TextAsset は除外
            var filter = new FilterCondition { singleTypeName = typeof(Texture2D).AssemblyQualifiedName };
            AssetSyncer.SyncConfig(MakeConfig(new List<FilterCondition> { filter }));
            Assert.IsFalse(DstExists("file.txt"));
        }

        // #28
        [Test]
        public void Phase1_FilePassesFilter_Copied()
        {
            WriteSrc("file.txt", "data");
            AssetDatabase.Refresh();

            // TextAsset 型のみ許可 → .txt はコピーされる
            var filter = new FilterCondition { singleTypeName = typeof(TextAsset).AssemblyQualifiedName };
            AssetSyncer.SyncConfig(MakeConfig(new List<FilterCondition> { filter }));
            Assert.IsTrue(DstExists("file.txt"));
        }

        // #29
        [Test]
        public void Phase1_NestedSubdir_CopiedWithStructure()
        {
            WriteSrc("sub/deep/file.txt", "nested");
            AssetSyncer.SyncConfig(MakeConfig());
            Assert.IsTrue(DstExists("sub/deep/file.txt"));
            Assert.AreEqual("nested", ReadDst("sub/deep/file.txt"));
        }

        [Test]
        public void Phase1_NestedSubdir_NotCopiedWhenIncludeSubdirectoriesIsFalse()
        {
            WriteSrc("root.txt", "root");
            WriteSrc("sub/deep/file.txt", "nested");

            AssetSyncer.SyncConfig(MakeConfig(includeSubdirectories: false));

            Assert.IsTrue(DstExists("root.txt"));
            Assert.IsFalse(DstExists("sub/deep/file.txt"));
        }

        // #30
        // meta ファイルがコピーされていないことの確認:
        // コピーされていれば src と dst の GUID が一致するが、Unity が独立して生成した場合は異なる。
        [Test]
        public void Phase1_MetaFile_NotCopied()
        {
            WriteSrc("file.txt", "data");
            AssetDatabase.Refresh(); // src/.meta が生成される

            AssetSyncer.SyncConfig(MakeConfig()); // 内部で Refresh → dst/.meta が Unity により生成される

            string srcMeta = Path.Combine(_srcFullPath, "file.txt.meta");
            string dstMeta = Path.Combine(_dstFullPath, "file.txt.meta");

            Assert.IsTrue(File.Exists(srcMeta), "src meta must exist");
            Assert.IsTrue(File.Exists(dstMeta), "dst meta must exist (generated by Unity importer)");
            Assert.AreNotEqual(
                File.ReadAllText(srcMeta),
                File.ReadAllText(dstMeta),
                "dst meta must have a different GUID than src meta (i.e. not copied from src)");
        }

        // #31
        [Test]
        public void Phase1_CopiedFile_AddedToManifest()
        {
            WriteSrc("file.txt");
            AssetSyncer.SyncConfig(MakeConfig());
            Assert.IsTrue(ManifestContains("file.txt"));
        }

        // #32
        [Test]
        public void Phase1_AlreadySynced_StillInManifest()
        {
            WriteSrc("file.txt");
            AssetSyncer.SyncConfig(MakeConfig());
            // 変更なしで再同期
            AssetSyncer.SyncConfig(MakeConfig());
            Assert.IsTrue(ManifestContains("file.txt"));
        }

        // ─── Phase 2: 削除 (#33–41) ───────────────────────────────────

        // #33
        [Test]
        public void Phase2_ManifestFile_SrcDeleted_DeletedFromDst()
        {
            WriteSrc("file.txt");
            AssetSyncer.SyncConfig(MakeConfig());
            Assert.IsTrue(DstExists("file.txt"));

            DeleteSrc("file.txt");
            AssetSyncer.SyncConfig(MakeConfig());
            Assert.IsFalse(DstExists("file.txt"));
        }

        // #34
        [Test]
        public void Phase2_ManifestFile_SrcDeleted_MetaAlsoDeleted()
        {
            WriteSrc("file.txt");
            AssetSyncer.SyncConfig(MakeConfig());
            AssetDatabase.Refresh(); // dst/.meta が生成される

            string dstMeta = Path.Combine(_dstFullPath, "file.txt.meta");
            Assume.That(File.Exists(dstMeta), "meta must exist after Refresh for this test to be meaningful");

            DeleteSrc("file.txt");
            AssetSyncer.SyncConfig(MakeConfig());
            Assert.IsFalse(File.Exists(dstMeta), "meta file must be deleted along with the asset");
        }

        // #35
        [Test]
        public void Phase2_ManifestFile_SrcExists_PassesFilter_Kept()
        {
            WriteSrc("file.txt");
            AssetSyncer.SyncConfig(MakeConfig());
            Assert.IsTrue(DstExists("file.txt"));

            // 再同期しても src が存在してフィルタ通過 → 保持
            AssetSyncer.SyncConfig(MakeConfig());
            Assert.IsTrue(DstExists("file.txt"));
        }

        // #36
        [Test]
        public void Phase2_NonManifestFile_SrcNotExist_Kept()
        {
            // dst に手動配置（マニフェストなし）
            WriteDst("manual.txt", "manual");
            AssetSyncer.SyncConfig(MakeConfig());
            Assert.IsTrue(DstExists("manual.txt"), "manually placed file must not be deleted");
        }

        // #37
        [Test]
        public void Phase2_NonManifestFile_SrcExists_Kept()
        {
            // src と dst に同名ファイルがあっても、dst がマニフェスト外なら触らない
            WriteSrc("file.txt", "src-content");
            WriteDst("file.txt", "different-manual-content");

            // src のファイルをフィルタで除外して同期（dst の file.txt はマニフェストに入らない）
            var exclude = new FilterCondition
            {
                singleTypeName = typeof(TextAsset).AssemblyQualifiedName,
                invert = true
            };
            AssetDatabase.Refresh();
            AssetSyncer.SyncConfig(MakeConfig(new List<FilterCondition> { exclude }));

            // マニフェスト外なので内容が違っても保持
            Assert.IsTrue(DstExists("file.txt"));
            Assert.AreEqual("different-manual-content", ReadDst("file.txt"));
        }

        // #38
        [Test]
        public void Phase2_ManifestFile_FilterChanged_ContentMatches_Deleted()
        {
            WriteSrc("file.txt", "synced");
            AssetDatabase.Refresh();

            // フィルタなしで同期 → マニフェストに登録
            AssetSyncer.SyncConfig(MakeConfig());
            Assert.IsTrue(DstExists("file.txt"));
            Assert.IsTrue(ManifestContains("file.txt"));

            // TextAsset を除外するフィルタを追加して再同期
            // dst の内容は src と同一（同期済み） → 削除
            var exclude = new FilterCondition
            {
                singleTypeName = typeof(TextAsset).AssemblyQualifiedName,
                invert = true
            };
            AssetSyncer.SyncConfig(MakeConfig(new List<FilterCondition> { exclude }));
            Assert.IsFalse(DstExists("file.txt"));
        }

        // #39
        [Test]
        public void Phase2_ManifestFile_FilterChanged_ContentDiffers_Kept()
        {
            WriteSrc("file.txt", "original");
            AssetDatabase.Refresh();

            // フィルタなしで同期 → マニフェストに登録
            AssetSyncer.SyncConfig(MakeConfig());
            Assert.IsTrue(DstExists("file.txt"));

            // dst を手動で変更
            WriteDst("file.txt", "manually-modified");

            // TextAsset を除外するフィルタで再同期
            // dst 内容が src と異なる → 手動変更と見なし保持
            var exclude = new FilterCondition
            {
                singleTypeName = typeof(TextAsset).AssemblyQualifiedName,
                invert = true
            };
            AssetSyncer.SyncConfig(MakeConfig(new List<FilterCondition> { exclude }));
            Assert.IsTrue(DstExists("file.txt"), "manually modified file must not be deleted");
        }

        // #40
        [Test]
        public void Phase2_EmptySubdirAfterDelete_Remains()
        {
            WriteSrc("sub/file.txt");
            AssetSyncer.SyncConfig(MakeConfig());
            Assert.IsTrue(DstExists("sub/file.txt"));

            DeleteSrc("sub/file.txt");
            AssetSyncer.SyncConfig(MakeConfig());

            Assert.IsFalse(DstExists("sub/file.txt"), "file should be deleted");
            string subDir = Path.Combine(_dstFullPath, "sub");
            Assert.IsTrue(Directory.Exists(subDir), "empty subdirectory should remain");
        }

        // #41
        [Test]
        public void Phase2_DeletedFile_RemovedFromManifest()
        {
            WriteSrc("file.txt");
            AssetSyncer.SyncConfig(MakeConfig());
            Assert.IsTrue(ManifestContains("file.txt"));

            DeleteSrc("file.txt");
            AssetSyncer.SyncConfig(MakeConfig());
            Assert.IsFalse(ManifestContains("file.txt"));
        }

        // ─── マニフェスト (#42–44) ────────────────────────────────────

        // #42
        [Test]
        public void Manifest_FirstSync_ManifestCreated()
        {
            WriteSrc("file.txt");
            string manifestPath = ManifestFilePath(_dstAssetPath);
            if (File.Exists(manifestPath)) File.Delete(manifestPath);

            AssetSyncer.SyncConfig(MakeConfig());
            Assert.IsTrue(File.Exists(manifestPath));
        }

        // #43
        [Test]
        public void Manifest_DifferentDstPaths_DifferentFiles()
        {
            string dst2AssetPath = _testRoot + "/Dst2";
            string dst2FullPath = Path.GetFullPath(
                Path.Combine(Path.GetDirectoryName(Application.dataPath), dst2AssetPath));
            Directory.CreateDirectory(dst2FullPath);

            string manifest2 = ManifestFilePath(dst2AssetPath);
            if (File.Exists(manifest2)) File.Delete(manifest2);

            WriteSrc("file.txt");
            AssetSyncer.SyncConfig(MakeConfig());

            var config2 = new SyncConfig
            {
                configName = "TestConfig2",
                enabled = true,
                sourcePath = _srcAssetPath,
                destinationPath = dst2AssetPath,
                filters = new List<FilterCondition>()
            };
            AssetSyncer.SyncConfig(config2);

            string manifest1 = ManifestFilePath(_dstAssetPath);
            Assert.AreNotEqual(manifest1, manifest2, "different dst paths must produce different manifest files");
            Assert.IsTrue(File.Exists(manifest1));
            Assert.IsTrue(File.Exists(manifest2));

            // Dst2 は _testRoot 配下のため TearDown で削除される
            // manifest2 だけ Library に残るので明示的に削除
            if (File.Exists(manifest2)) File.Delete(manifest2);
        }

        // #44
        [Test]
        public void Manifest_NoChanges_NotUpdated()
        {
            WriteSrc("file.txt");
            AssetSyncer.SyncConfig(MakeConfig()); // 初回同期

            string manifestPath = ManifestFilePath(_dstAssetPath);
            DateTime before = File.GetLastWriteTimeUtc(manifestPath);
            System.Threading.Thread.Sleep(50);

            AssetSyncer.SyncConfig(MakeConfig()); // 変更なし
            DateTime after = File.GetLastWriteTimeUtc(manifestPath);

            Assert.AreEqual(before, after, "manifest must not be rewritten when nothing changed");
        }

        // ─── Integration (#45–50) ─────────────────────────────────────

        // #45
        [Test]
        public void Integration_AddUpdateDelete()
        {
            // Add
            WriteSrc("a.txt", "v1");
            AssetSyncer.SyncConfig(MakeConfig());
            Assert.IsTrue(DstExists("a.txt"));

            // Update
            WriteSrc("a.txt", "v2");
            AssetSyncer.SyncConfig(MakeConfig());
            Assert.AreEqual("v2", ReadDst("a.txt"));

            // Delete
            DeleteSrc("a.txt");
            AssetSyncer.SyncConfig(MakeConfig());
            Assert.IsFalse(DstExists("a.txt"));
        }

        // #46
        [Test]
        public void Integration_ManualFileCoexists()
        {
            WriteSrc("synced.txt", "from-src");
            WriteDst("manual.txt", "hand-placed");

            AssetSyncer.SyncConfig(MakeConfig());

            Assert.IsTrue(DstExists("synced.txt"));
            Assert.IsTrue(DstExists("manual.txt"), "manually placed file must survive sync");
            Assert.AreEqual("hand-placed", ReadDst("manual.txt"));
        }

        // #47 — 「Preset型を含む」フィルタ + dst に別名Preset → dst の Preset 保持、src の Preset も到着
        [Test]
        public void Integration_FilterIncludesType_OtherDstFileKept()
        {
            WriteSrc("src-preset.txt", "src");
            WriteDst("dst-preset.txt", "manual");
            AssetDatabase.Refresh();

            // TextAsset 型のみ許可
            var include = new FilterCondition { singleTypeName = typeof(TextAsset).AssemblyQualifiedName };
            AssetSyncer.SyncConfig(MakeConfig(new List<FilterCondition> { include }));

            Assert.IsTrue(DstExists("src-preset.txt"), "src file that passes filter must be copied");
            Assert.IsTrue(DstExists("dst-preset.txt"), "manually placed file must not be touched");
        }

        // #48 — 「Preset型を除外」フィルタ + dst に別名Preset → dst の Preset 保持
        [Test]
        public void Integration_FilterExcludesType_ManualDstFileKept()
        {
            WriteSrc("src-file.txt", "src");
            WriteDst("manual.txt", "manual");
            AssetDatabase.Refresh();

            // TextAsset 除外 → src のファイルはコピーされない
            var exclude = new FilterCondition
            {
                singleTypeName = typeof(TextAsset).AssemblyQualifiedName,
                invert = true
            };
            AssetSyncer.SyncConfig(MakeConfig(new List<FilterCondition> { exclude }));

            Assert.IsFalse(DstExists("src-file.txt"), "filtered-out src file must not be copied");
            Assert.IsTrue(DstExists("manual.txt"), "manually placed dst file must not be deleted");
        }

        [Test]
        public void PostprocessorPathMatch_ExactRootAndChildOnly()
        {
            const string root = "Assets/Foo";

            Assert.IsTrue(AssetForkPostprocessor.IsAssetPathWithinRoot("Assets/Foo", root));
            Assert.IsTrue(AssetForkPostprocessor.IsAssetPathWithinRoot("Assets/Foo/Bar.asset", root));
            Assert.IsFalse(AssetForkPostprocessor.IsAssetPathWithinRoot("Assets/FooBar/Bar.asset", root));
        }

        [Test]
        public void PostprocessorScopeMatch_IncludeSubdirectoriesFlagIsRespected()
        {
            const string root = "Assets/Foo";

            Assert.IsTrue(AssetForkPostprocessor.IsAssetPathWithinScope("Assets/Foo", root, includeSubdirectories: false));
            Assert.IsTrue(AssetForkPostprocessor.IsAssetPathWithinScope("Assets/Foo/Bar.asset", root, includeSubdirectories: false));
            Assert.IsFalse(AssetForkPostprocessor.IsAssetPathWithinScope("Assets/Foo/Sub/Bar.asset", root, includeSubdirectories: false));

            Assert.IsTrue(AssetForkPostprocessor.IsAssetPathWithinScope("Assets/Foo/Sub/Bar.asset", root, includeSubdirectories: true));
        }

    }
}
