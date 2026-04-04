using System;
using System.Collections.Generic;
using System.IO;
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

            AssetSyncer.ConflictResolverOverride = (SyncConfig _, IReadOnlyList<AssetSyncer.SyncConflict> conflicts, out Dictionary<string, AssetSyncer.ConflictResolution> decisions) =>
            {
                decisions = new Dictionary<string, AssetSyncer.ConflictResolution>();
                foreach (var conflict in conflicts)
                    decisions[conflict.NormalizedRelativePath] = AssetSyncer.ConflictResolution.Protected;
                return true;
            };
        }

        [TearDown]
        public void TearDown()
        {

            // AssetDatabase.DeleteAsset 縺ｯ繝輔か繝ｫ繝縺梧悴逋ｻ骭ｲ縺縺ｨ螟ｱ謨励☆繧九◆繧√・
            // FileUtil 縺ｧ逶ｴ謗･蜑企勁縺励※縺九ｉ Refresh 縺吶ｋ
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string fullTestRoot = Path.GetFullPath(Path.Combine(projectRoot, _testRoot));
            FileUtil.DeleteFileOrDirectory(fullTestRoot);
            FileUtil.DeleteFileOrDirectory(fullTestRoot + ".meta");
            AssetDatabase.Refresh();
            AssetSyncer.ConflictResolverOverride = null;
        }

        // 笏笏笏 Helpers 笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏

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
            // SyncConfig 縺ｮ Refresh 縺ｧ繧､繝ｳ繝昴・繝域ｸ医∩縺ｮ .meta 繧ょ炎髯､縺励↑縺・→蟄､遶・.meta 隴ｦ蜻翫′蜃ｺ繧・
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

        private bool OwnedContains(SyncConfig config, string relPath)
        {
            return config.ownedRelativePaths.Contains(AssetSyncer.NormalizeRelativePath(relPath));
        }

        // 笏笏笏 ShouldCopy (#15窶・8) 笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏

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

        // 笏笏笏 SyncConfig 繝舌Μ繝・・繧ｷ繝ｧ繝ｳ (#19窶・3) 笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏

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

        [Test]
        public void SyncConfig_Disabled_RemovesOwnedFileFromDst()
        {
            WriteSrc("file.txt", "synced");
            var config = MakeConfig();

            AssetSyncer.SyncConfig(config);
            Assert.IsTrue(DstExists("file.txt"));
            Assert.IsTrue(OwnedContains(config, "file.txt"));

            config.enabled = false;
            AssetSyncer.SyncConfig(config);

            Assert.IsFalse(DstExists("file.txt"));
            Assert.IsFalse(OwnedContains(config, "file.txt"));
        }

        [Test]
        public void SyncConfig_Disabled_KeepsManualDstFile()
        {
            WriteSrc("synced.txt", "from-src");
            WriteDst("manual.txt", "manual");
            var config = MakeConfig();

            AssetSyncer.SyncConfig(config);
            Assert.IsTrue(DstExists("synced.txt"));
            Assert.IsTrue(DstExists("manual.txt"));

            config.enabled = false;
            AssetSyncer.SyncConfig(config);

            Assert.IsFalse(DstExists("synced.txt"), "owned file should be removed when config is disabled");
            Assert.IsTrue(DstExists("manual.txt"), "manual destination file must remain");
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
        public void TryGetConfigWarning_NestedWithIncludeSubdirectories_ReturnsWarning()
        {
            bool hasWarning = AssetSyncer.TryGetConfigWarning(new SyncConfig
            {
                sourcePath = _srcAssetPath,
                destinationPath = _srcAssetPath + "/NestedDst",
                enabled = true,
                includeSubdirectories = true
            }, out string warning);

            Assert.IsTrue(hasWarning);
            StringAssert.Contains("must not be nested", warning);
        }

        [Test]
        public void TryGetConfigWarning_NestedWithoutIncludeSubdirectories_ReturnsNoWarning()
        {
            bool hasWarning = AssetSyncer.TryGetConfigWarning(new SyncConfig
            {
                sourcePath = _srcAssetPath,
                destinationPath = _srcAssetPath + "/NestedDst",
                enabled = true,
                includeSubdirectories = false
            }, out string warning);

            Assert.IsFalse(hasWarning);
            Assert.IsNull(warning);
        }

        [Test]
        public void SyncConfig_DstNestedUnderSrc_WithIncludeSubdirectories_LogsWarningAndSkips()
        {
            WriteSrc("file.txt", "data");

            string nestedDst = _srcAssetPath + "/NestedDst";
            LogAssert.Expect(LogType.Warning, new Regex(".*must not be nested.*"));

            int copied = AssetSyncer.SyncConfig(new SyncConfig
            {
                sourcePath = _srcAssetPath,
                destinationPath = nestedDst,
                enabled = true,
                includeSubdirectories = true
            });

            Assert.AreEqual(0, copied);
            string nestedDstFull = Path.Combine(_srcFullPath, "NestedDst", "file.txt");
            Assert.IsFalse(File.Exists(nestedDstFull));
        }

        [Test]
        public void SyncConfig_SrcNestedUnderDst_WithIncludeSubdirectories_LogsWarningAndSkips()
        {
            WriteSrc("file.txt", "data");

            LogAssert.Expect(LogType.Warning, new Regex(".*must not be nested.*"));

            int copied = AssetSyncer.SyncConfig(new SyncConfig
            {
                sourcePath = _srcAssetPath,
                destinationPath = _testRoot,
                enabled = true,
                includeSubdirectories = true
            });

            Assert.AreEqual(0, copied);
            Assert.IsFalse(File.Exists(Path.Combine(Path.GetDirectoryName(Application.dataPath), _testRoot, "file.txt")));
        }

        [Test]
        public void SyncConfig_DstNestedUnderSrc_WithoutIncludeSubdirectories_AllowsSync()
        {
            WriteSrc("root.txt", "root");
            WriteSrc("sub/deep.txt", "deep");

            string nestedDst = _srcAssetPath + "/NestedDst";
            int copied = AssetSyncer.SyncConfig(new SyncConfig
            {
                sourcePath = _srcAssetPath,
                destinationPath = nestedDst,
                enabled = true,
                includeSubdirectories = false
            });

            Assert.AreEqual(1, copied);
            Assert.IsTrue(File.Exists(Path.Combine(_srcFullPath, "NestedDst", "root.txt")));
            Assert.IsFalse(File.Exists(Path.Combine(_srcFullPath, "NestedDst", "sub", "deep.txt")));
        }

        [Test]
        public void SyncConfig_SrcNestedUnderDst_WithoutIncludeSubdirectories_AllowsSync()
        {
            WriteSrc("root.txt", "root");
            WriteSrc("child/file.txt", "child");

            string dstRootFull = Path.Combine(Path.GetDirectoryName(Application.dataPath), _testRoot);
            int copied = AssetSyncer.SyncConfig(new SyncConfig
            {
                sourcePath = _srcAssetPath,
                destinationPath = _testRoot,
                enabled = true,
                includeSubdirectories = false
            });

            Assert.AreEqual(1, copied);
            Assert.IsTrue(File.Exists(Path.Combine(dstRootFull, "root.txt")));
            Assert.IsFalse(File.Exists(Path.Combine(dstRootFull, "child", "file.txt")));
        }

        [Test]
        public void SyncConfig_NestedPaths_WhenIncludeSubdirectoriesToggledOn_LogsWarningAndSkips()
        {
            WriteSrc("root.txt", "v1");
            string nestedDst = _srcAssetPath + "/NestedDst";
            string nestedDstFile = Path.Combine(_srcFullPath, "NestedDst", "root.txt");

            int firstCopied = AssetSyncer.SyncConfig(new SyncConfig
            {
                sourcePath = _srcAssetPath,
                destinationPath = nestedDst,
                enabled = true,
                includeSubdirectories = false
            });
            Assert.AreEqual(1, firstCopied);
            Assert.AreEqual("v1", File.ReadAllText(nestedDstFile));

            WriteSrc("root.txt", "v2");
            LogAssert.Expect(LogType.Warning, new Regex(".*must not be nested.*"));
            int secondCopied = AssetSyncer.SyncConfig(new SyncConfig
            {
                sourcePath = _srcAssetPath,
                destinationPath = nestedDst,
                enabled = true,
                includeSubdirectories = true
            });

            Assert.AreEqual(0, secondCopied);
            Assert.AreEqual("v1", File.ReadAllText(nestedDstFile));
        }

        // 笏笏笏 Phase 1: 繧ｳ繝斐・ (#24窶・2) 笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏

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
            var config = MakeConfig();
            WriteSrc("file.txt", "v1");
            AssetSyncer.SyncConfig(config);
            Assert.AreEqual("v1", ReadDst("file.txt"));

            WriteSrc("file.txt", "v2");
            AssetSyncer.SyncConfig(config);
            Assert.AreEqual("v2", ReadDst("file.txt"));
        }

        // #26
        [Test]
        public void Phase1_UnchangedFile_NotOverwritten()
        {
            var config = MakeConfig();
            WriteSrc("file.txt", "same");
            AssetSyncer.SyncConfig(config);

            DateTime before = DstWriteTime("file.txt");
            System.Threading.Thread.Sleep(50);

            AssetSyncer.SyncConfig(config);
            DateTime after = DstWriteTime("file.txt");

            Assert.AreEqual(before, after, "Content unchanged 竊・dst must not be overwritten");
        }

        // #27
        [Test]
        public void Phase1_FileFailsFilter_NotCopied()
        {
            WriteSrc("file.txt", "data");
            AssetDatabase.Refresh();

            // Texture2D 蝙九・縺ｿ險ｱ蜿ｯ 竊・TextAsset 縺ｯ髯､螟・
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

            // TextAsset 蝙九・縺ｿ險ｱ蜿ｯ 竊・.txt 縺ｯ繧ｳ繝斐・縺輔ｌ繧・
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
        // meta 繝輔ぃ繧､繝ｫ縺後さ繝斐・縺輔ｌ縺ｦ縺・↑縺・％縺ｨ縺ｮ遒ｺ隱・
        // 繧ｳ繝斐・縺輔ｌ縺ｦ縺・ｌ縺ｰ src 縺ｨ dst 縺ｮ GUID 縺御ｸ閾ｴ縺吶ｋ縺後ゞnity 縺檎峡遶九＠縺ｦ逕滓・縺励◆蝣ｴ蜷医・逡ｰ縺ｪ繧九・
        [Test]
        public void Phase1_MetaFile_NotCopied()
        {
            WriteSrc("file.txt", "data");
            AssetDatabase.Refresh(); // src/.meta 縺檎函謌舌＆繧後ｋ

            AssetSyncer.SyncConfig(MakeConfig()); // 蜀・Κ縺ｧ Refresh 竊・dst/.meta 縺・Unity 縺ｫ繧医ｊ逕滓・縺輔ｌ繧・

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
        public void Phase1_CopiedFile_AddedToOwned()
        {
            WriteSrc("file.txt");
            var config = MakeConfig();
            AssetSyncer.SyncConfig(config);
            Assert.IsTrue(OwnedContains(config, "file.txt"));
        }

        // #32
        [Test]
        public void Phase1_AlreadySynced_StillInOwned()
        {
            WriteSrc("file.txt");
            var config = MakeConfig();
            AssetSyncer.SyncConfig(config);
            AssetSyncer.SyncConfig(config);
            Assert.IsTrue(OwnedContains(config, "file.txt"));
        }

        // 笏笏笏 Phase 2: 蜑企勁 (#33窶・1) 笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏

        // #33
        [Test]
        public void Phase2_ManifestFile_SrcDeleted_DeletedFromDst()
        {
            var config = MakeConfig();
            WriteSrc("file.txt");
            AssetSyncer.SyncConfig(config);
            Assert.IsTrue(DstExists("file.txt"));

            DeleteSrc("file.txt");
            AssetSyncer.SyncConfig(config);
            Assert.IsFalse(DstExists("file.txt"));
        }

        // #34
        [Test]
        public void Phase2_ManifestFile_SrcDeleted_MetaAlsoDeleted()
        {
            var config = MakeConfig();
            WriteSrc("file.txt");
            AssetSyncer.SyncConfig(config);
            AssetDatabase.Refresh(); // dst/.meta 縺檎函謌舌＆繧後ｋ

            string dstMeta = Path.Combine(_dstFullPath, "file.txt.meta");
            Assume.That(File.Exists(dstMeta), "meta must exist after Refresh for this test to be meaningful");

            DeleteSrc("file.txt");
            AssetSyncer.SyncConfig(config);
            Assert.IsFalse(File.Exists(dstMeta), "meta file must be deleted along with the asset");
        }

        // #35
        [Test]
        public void Phase2_ManifestFile_SrcExists_PassesFilter_Kept()
        {
            var config = MakeConfig();
            WriteSrc("file.txt");
            AssetSyncer.SyncConfig(config);
            Assert.IsTrue(DstExists("file.txt"));

            // 蜀榊酔譛溘＠縺ｦ繧・src 縺悟ｭ伜惠縺励※繝輔ぅ繝ｫ繧ｿ騾夐℃ 竊・菫晄戟
            AssetSyncer.SyncConfig(config);
            Assert.IsTrue(DstExists("file.txt"));
        }

        // #36
        [Test]
        public void Phase2_NonManifestFile_SrcNotExist_Kept()
        {
            // dst 縺ｫ謇句虚驟咲ｽｮ・医・繝九ヵ繧ｧ繧ｹ繝医↑縺暦ｼ・
            WriteDst("manual.txt", "manual");
            AssetSyncer.SyncConfig(MakeConfig());
            Assert.IsTrue(DstExists("manual.txt"), "manually placed file must not be deleted");
        }

        // #37
        [Test]
        public void Phase2_NonManifestFile_SrcExists_Kept()
        {
            // src 縺ｨ dst 縺ｫ蜷悟錐繝輔ぃ繧､繝ｫ縺後≠縺｣縺ｦ繧ゅ‥st 縺後・繝九ヵ繧ｧ繧ｹ繝亥､悶↑繧芽ｧｦ繧峨↑縺・
            WriteSrc("file.txt", "src-content");
            WriteDst("file.txt", "different-manual-content");

            // src 縺ｮ繝輔ぃ繧､繝ｫ繧偵ヵ繧｣繝ｫ繧ｿ縺ｧ髯､螟悶＠縺ｦ蜷梧悄・・st 縺ｮ file.txt 縺ｯ繝槭ル繝輔ぉ繧ｹ繝医↓蜈･繧峨↑縺・ｼ・
            var exclude = new FilterCondition
            {
                singleTypeName = typeof(TextAsset).AssemblyQualifiedName,
                invert = true
            };
            AssetDatabase.Refresh();
            AssetSyncer.SyncConfig(MakeConfig(new List<FilterCondition> { exclude }));

            // 繝槭ル繝輔ぉ繧ｹ繝亥､悶↑縺ｮ縺ｧ蜀・ｮｹ縺碁＆縺｣縺ｦ繧ゆｿ晄戟
            Assert.IsTrue(DstExists("file.txt"));
            Assert.AreEqual("different-manual-content", ReadDst("file.txt"));
        }

        // #38
        [Test]
        public void Phase2_ManifestFile_FilterChanged_ContentMatches_Deleted()
        {
            WriteSrc("file.txt", "synced");
            AssetDatabase.Refresh();

            var config = MakeConfig();
            AssetSyncer.SyncConfig(config);
            Assert.IsTrue(DstExists("file.txt"));
            Assert.IsTrue(OwnedContains(config, "file.txt"));

            var exclude = new FilterCondition
            {
                singleTypeName = typeof(TextAsset).AssemblyQualifiedName,
                invert = true
            };
            config.filters = new List<FilterCondition> { exclude };
            AssetSyncer.SyncConfig(config);
            Assert.IsFalse(DstExists("file.txt"));
            Assert.IsFalse(OwnedContains(config, "file.txt"));
        }

        // #39
        [Test]
        public void Phase2_ManifestFile_FilterChanged_ContentDiffers_Kept()
        {
            WriteSrc("file.txt", "original");
            AssetDatabase.Refresh();

            // 繝輔ぅ繝ｫ繧ｿ縺ｪ縺励〒蜷梧悄 竊・繝槭ル繝輔ぉ繧ｹ繝医↓逋ｻ骭ｲ
            var config = MakeConfig();
            AssetSyncer.SyncConfig(config);
            Assert.IsTrue(DstExists("file.txt"));

            // dst 繧呈焔蜍輔〒螟画峩
            WriteDst("file.txt", "manually-modified");

            // TextAsset 繧帝勁螟悶☆繧九ヵ繧｣繝ｫ繧ｿ縺ｧ蜀榊酔譛・
            // dst 蜀・ｮｹ縺・src 縺ｨ逡ｰ縺ｪ繧・竊・謇句虚螟画峩縺ｨ隕九↑縺嶺ｿ晄戟
            var exclude = new FilterCondition
            {
                singleTypeName = typeof(TextAsset).AssemblyQualifiedName,
                invert = true
            };
            config.filters = new List<FilterCondition> { exclude };
            AssetSyncer.SyncConfig(config);
            Assert.IsTrue(DstExists("file.txt"), "manually modified file must not be deleted");
        }

        // #40
        [Test]
        public void Phase2_EmptySubdirAfterDelete_Remains()
        {
            var config = MakeConfig();
            WriteSrc("sub/file.txt");
            AssetSyncer.SyncConfig(config);
            Assert.IsTrue(DstExists("sub/file.txt"));

            DeleteSrc("sub/file.txt");
            AssetSyncer.SyncConfig(config);

            Assert.IsFalse(DstExists("sub/file.txt"), "file should be deleted");
            string subDir = Path.Combine(_dstFullPath, "sub");
            Assert.IsTrue(Directory.Exists(subDir), "empty subdirectory should remain");
        }

        // #41
        [Test]
        public void Phase2_DeletedFile_RemovedFromOwned()
        {
            WriteSrc("file.txt");
            var config = MakeConfig();
            AssetSyncer.SyncConfig(config);
            Assert.IsTrue(OwnedContains(config, "file.txt"));

            DeleteSrc("file.txt");
            AssetSyncer.SyncConfig(config);
            Assert.IsFalse(OwnedContains(config, "file.txt"));
        }

        // 笏笏笏 繝槭ル繝輔ぉ繧ｹ繝・(#42窶・4) 笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏

        // #42
        [Test]
        public void State_FirstSync_OwnedPathCreated()
        {
            WriteSrc("file.txt");
            var config = MakeConfig();

            AssetSyncer.SyncConfig(config);
            Assert.IsTrue(OwnedContains(config, "file.txt"));
        }

        // #43
        [Test]
        public void State_DifferentConfigs_KeepIndependentOwnedLists()
        {
            string dst2AssetPath = _testRoot + "/Dst2";
            Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(Application.dataPath), dst2AssetPath));

            WriteSrc("file.txt");

            var config1 = MakeConfig();
            var config2 = new SyncConfig
            {
                configName = "TestConfig2",
                enabled = true,
                sourcePath = _srcAssetPath,
                destinationPath = dst2AssetPath,
                filters = new List<FilterCondition>()
            };

            AssetSyncer.SyncConfig(config1);
            AssetSyncer.SyncConfig(config2);

            Assert.IsTrue(OwnedContains(config1, "file.txt"));
            Assert.IsTrue(OwnedContains(config2, "file.txt"));
        }

        // #44
        [Test]
        public void State_Normalize_NormalizesOwnedOnly()
        {
            var config = MakeConfig();
            config.ownedRelativePaths = new List<string> { "a.txt", "a.txt", "A.txt", "sub\\b.txt" };
            config.protectedGuids = new List<string> { "", "g1", "g1", "g2" };

            bool changed = AssetSyncer.NormalizeState(config);

            Assert.IsTrue(changed);
            Assert.AreEqual(2, config.ownedRelativePaths.Count);
            Assert.AreEqual("a.txt", config.ownedRelativePaths[0]);
            Assert.AreEqual("sub/b.txt", config.ownedRelativePaths[1]);
            Assert.AreEqual(4, config.protectedGuids.Count);
            Assert.AreEqual("", config.protectedGuids[0]);
            Assert.AreEqual("g1", config.protectedGuids[1]);
            Assert.AreEqual("g1", config.protectedGuids[2]);
            Assert.AreEqual("g2", config.protectedGuids[3]);
        }

        // 笏笏笏 Integration (#45窶・0) 笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏

        // #45
        [Test]
        public void Integration_AddUpdateDelete()
        {
            var config = MakeConfig();
            // Add
            WriteSrc("a.txt", "v1");
            AssetSyncer.SyncConfig(config);
            Assert.IsTrue(DstExists("a.txt"));

            // Update
            WriteSrc("a.txt", "v2");
            AssetSyncer.SyncConfig(config);
            Assert.AreEqual("v2", ReadDst("a.txt"));

            // Delete
            DeleteSrc("a.txt");
            AssetSyncer.SyncConfig(config);
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

        // #47 窶・縲訓reset蝙九ｒ蜷ｫ繧縲阪ヵ繧｣繝ｫ繧ｿ + dst 縺ｫ蛻･蜷恒reset 竊・dst 縺ｮ Preset 菫晄戟縲《rc 縺ｮ Preset 繧ょ芦逹
        [Test]
        public void Integration_FilterIncludesType_OtherDstFileKept()
        {
            WriteSrc("src-preset.txt", "src");
            WriteDst("dst-preset.txt", "manual");
            AssetDatabase.Refresh();

            // TextAsset 蝙九・縺ｿ險ｱ蜿ｯ
            var include = new FilterCondition { singleTypeName = typeof(TextAsset).AssemblyQualifiedName };
            AssetSyncer.SyncConfig(MakeConfig(new List<FilterCondition> { include }));

            Assert.IsTrue(DstExists("src-preset.txt"), "src file that passes filter must be copied");
            Assert.IsTrue(DstExists("dst-preset.txt"), "manually placed file must not be touched");
        }

        // #48 窶・縲訓reset蝙九ｒ髯､螟悶阪ヵ繧｣繝ｫ繧ｿ + dst 縺ｫ蛻･蜷恒reset 竊・dst 縺ｮ Preset 菫晄戟
        [Test]
        public void Integration_FilterExcludesType_ManualDstFileKept()
        {
            WriteSrc("src-file.txt", "src");
            WriteDst("manual.txt", "manual");
            AssetDatabase.Refresh();

            // TextAsset 髯､螟・竊・src 縺ｮ繝輔ぃ繧､繝ｫ縺ｯ繧ｳ繝斐・縺輔ｌ縺ｪ縺・
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
        public void AreAssetPathsNested_NestedPair_ReturnsTrue()
        {
            Assert.IsTrue(AssetSyncer.AreAssetPathsNested(_srcAssetPath, _srcAssetPath + "/Nested"));
            Assert.IsTrue(AssetSyncer.AreAssetPathsNested(_srcAssetPath + "/Nested", _srcAssetPath));
        }

        [Test]
        public void AreAssetPathsNested_SameOrSeparatePaths_ReturnsFalse()
        {
            Assert.IsFalse(AssetSyncer.AreAssetPathsNested(_srcAssetPath, _srcAssetPath));
            Assert.IsFalse(AssetSyncer.AreAssetPathsNested(_srcAssetPath, _dstAssetPath));
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

        [Test]
        public void PostprocessorTryRemapPathByMoves_ExactMatch_ReturnsRemappedPath()
        {
            string movedTo = _testRoot + "/Moved";
            Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(Application.dataPath), movedTo));
            AssetDatabase.Refresh();

            bool changed = AssetForkPostprocessor.TryRemapPathByMoves(
                _testRoot + "/BeforeMove",
                new[] { movedTo },
                new[] { _testRoot + "/BeforeMove" },
                out string remappedPath);

            Assert.IsTrue(changed);
            Assert.AreEqual(movedTo, remappedPath);
        }

        [Test]
        public void PostprocessorTryRemapPathByMoves_ChildPathUnderMovedParent_IsRemapped()
        {
            string movedTo = _testRoot + "/MovedParent";
            Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(Application.dataPath), movedTo));
            AssetDatabase.Refresh();

            bool changed = AssetForkPostprocessor.TryRemapPathByMoves(
                _testRoot + "/BeforeParent/SubDir",
                new[] { movedTo },
                new[] { _testRoot + "/BeforeParent" },
                out string remappedPath);

            Assert.IsTrue(changed);
            Assert.AreEqual(movedTo + "/SubDir", remappedPath);
        }

        [Test]
        public void PostprocessorTryRemapPathByMoves_NoMatch_ReturnsFalse()
        {
            string movedTo = _testRoot + "/Moved";
            Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(Application.dataPath), movedTo));
            AssetDatabase.Refresh();

            bool changed = AssetForkPostprocessor.TryRemapPathByMoves(
                _testRoot + "/Unchanged",
                new[] { movedTo },
                new[] { _testRoot + "/BeforeMove" },
                out string remappedPath);

            Assert.IsFalse(changed);
            Assert.AreEqual(_testRoot + "/Unchanged", remappedPath);
        }

        [Test]
        public void PostprocessorTryRemapPathByMoves_PrefersLongestMatchingPrefix()
        {
            string movedParent = _testRoot + "/MovedParent";
            string movedChild = _testRoot + "/MovedChild";
            Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(Application.dataPath), movedParent));
            Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(Application.dataPath), movedChild));
            AssetDatabase.Refresh();

            bool changed = AssetForkPostprocessor.TryRemapPathByMoves(
                _testRoot + "/Before/Sub/Target",
                new[] { movedParent, movedChild },
                new[] { _testRoot + "/Before", _testRoot + "/Before/Sub" },
                out string remappedPath);

            Assert.IsTrue(changed);
            Assert.AreEqual(movedChild + "/Target", remappedPath);
        }

    }
}

