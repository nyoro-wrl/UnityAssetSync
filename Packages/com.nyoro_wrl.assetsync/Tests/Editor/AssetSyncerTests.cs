using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Nyorowrl.AssetSync;
using Nyorowrl.AssetSync.Editor;

namespace Nyorowrl.AssetSync.Editor.Tests
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
            _testRoot = "Assets/AssetSyncTest_" + uid;
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
                    decisions[conflict.NormalizedRelativePath] = AssetSyncer.ConflictResolution.Ignore;
                return true;
            };
        }

        [TearDown]
        public void TearDown()
        {

            // AssetDatabase.DeleteAsset 縺�E�繝輔か繝ｫ繝縺梧悴逋ｻ骭�E�縺�E�縺�E�螟ｱ謨励☁E��九◆繧√・
            // FileUtil 縺�E�逶�E�謗･蜑企勁縺励※縺九ａERefresh 縺吶�E�E
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
            // SyncConfig 縺�E� Refresh 縺�E�繧�E�繝ｳ繝昴・繝域�E�医∩縺�E� .meta 繧めE��髯�E�縺励↑縺・→蟄�E�遶・.meta 隴�E�蜻翫′蜁E��繧・
            string meta = full + ".meta";
            if (File.Exists(meta)) File.Delete(meta);
        }

        private void DeleteSrcDirectory(string relPath)
        {
            string full = Path.Combine(_srcFullPath, relPath.Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(full))
                Directory.Delete(full, recursive: true);

            string meta = full + ".meta";
            if (File.Exists(meta))
                File.Delete(meta);
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

        private bool SyncContains(SyncConfig config, string relPath)
        {
            return config.syncRelativePaths.Contains(AssetSyncer.NormalizeRelativePath(relPath));
        }

        private bool SyncDirectoryContains(SyncConfig config, string relPath)
        {
            return config.syncRelativeDirectoryPaths.Contains(AssetSyncer.NormalizeRelativePath(relPath));
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

        // 笏笏笏 SyncConfig 繝�EΜ繝�E・繧�E�繝ｧ繝ｳ (#19窶・3) 笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏

        [Test]
        public void CollectCopyPreviewDestinationAssetPaths_NewFile_ReturnsDestinationAssetPath()
        {
            WriteSrc("preview/new.txt", "v1");
            var config = MakeConfig();

            IReadOnlyList<string> preview = AssetSyncer.CollectCopyPreviewDestinationAssetPaths(config);

            CollectionAssert.Contains(preview, _dstAssetPath + "/preview/new.txt");
        }

        [Test]
        public void CollectCopyPreviewDestinationAssetPaths_ExcludesUnchangedAndConflicts_IncludesChangedSynced()
        {
            var config = MakeConfig();
            WriteSrc("same.txt", "same-v1");
            WriteSrc("changed.txt", "changed-v1");
            AssetSyncer.SyncConfig(config);

            WriteSrc("changed.txt", "changed-v2");
            WriteSrc("conflict.txt", "src-v1");
            WriteDst("conflict.txt", "dst-manual");

            IReadOnlyList<string> preview = AssetSyncer.CollectCopyPreviewDestinationAssetPaths(config);

            CollectionAssert.Contains(preview, _dstAssetPath + "/changed.txt");
            CollectionAssert.DoesNotContain(preview, _dstAssetPath + "/same.txt");
            CollectionAssert.DoesNotContain(preview, _dstAssetPath + "/conflict.txt");
        }
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
        public void SyncConfig_Disabled_RemovesSyncFileFromDst()
        {
            WriteSrc("file.txt", "synced");
            var config = MakeConfig();

            AssetSyncer.SyncConfig(config);
            Assert.IsTrue(DstExists("file.txt"));
            Assert.IsTrue(SyncContains(config, "file.txt"));

            config.enabled = false;
            AssetSyncer.SyncConfig(config);

            Assert.IsFalse(DstExists("file.txt"));
            Assert.IsFalse(SyncContains(config, "file.txt"));
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

            Assert.IsFalse(DstExists("synced.txt"), "synced file should be removed when config is disabled");
            Assert.IsTrue(DstExists("manual.txt"), "manual destination file must remain");
        }

        [Test]
        public void SyncConfig_Disabled_RemovesManagedEmptySubdirectory()
        {
            WriteSrc("sub/synced.txt", "from-src");
            var config = MakeConfig();

            AssetSyncer.SyncConfig(config);
            Assert.IsTrue(DstExists("sub/synced.txt"));

            config.enabled = false;
            AssetSyncer.SyncConfig(config);

            Assert.IsFalse(DstExists("sub/synced.txt"), "synced file should be removed when config is disabled");
            Assert.IsFalse(Directory.Exists(Path.Combine(_dstFullPath, "sub")), "empty managed subdirectory should be removed");
        }

        [Test]
        public void SyncConfig_Disabled_KeepsSubdirectoryWithManualContent()
        {
            WriteSrc("sub/synced.txt", "from-src");
            var config = MakeConfig();

            AssetSyncer.SyncConfig(config);
            WriteDst("sub/manual.txt", "manual");

            config.enabled = false;
            AssetSyncer.SyncConfig(config);

            Assert.IsFalse(DstExists("sub/synced.txt"), "synced file should be removed when config is disabled");
            Assert.IsTrue(DstExists("sub/manual.txt"), "manual file must remain");
            Assert.IsTrue(Directory.Exists(Path.Combine(_dstFullPath, "sub")), "subdirectory with manual content must remain");
        }

        [Test]
        public void SyncConfig_Disabled_KeepsSubdirectoryWithManualDirectory()
        {
            WriteSrc("sub/synced.txt", "from-src");
            var config = MakeConfig();

            AssetSyncer.SyncConfig(config);
            Directory.CreateDirectory(Path.Combine(_dstFullPath, "sub", "manualDir"));

            config.enabled = false;
            AssetSyncer.SyncConfig(config);

            Assert.IsFalse(DstExists("sub/synced.txt"), "synced file should be removed when config is disabled");
            Assert.IsTrue(Directory.Exists(Path.Combine(_dstFullPath, "sub")), "subdirectory with manual directory must remain");
            Assert.IsTrue(Directory.Exists(Path.Combine(_dstFullPath, "sub", "manualDir")), "manual directory must remain");
        }

        [Test]
        public void SyncConfig_Disabled_RemovesManagedEmptySubdirectoryWithoutFiles()
        {
            Directory.CreateDirectory(Path.Combine(_srcFullPath, "sub", "empty"));
            var config = MakeConfig();

            AssetSyncer.SyncConfig(config);
            Assert.IsTrue(Directory.Exists(Path.Combine(_dstFullPath, "sub", "empty")));
            Assert.IsTrue(SyncDirectoryContains(config, "sub/empty"));

            config.enabled = false;
            AssetSyncer.SyncConfig(config);

            Assert.IsFalse(Directory.Exists(Path.Combine(_dstFullPath, "sub", "empty")));
            Assert.IsFalse(Directory.Exists(Path.Combine(_dstFullPath, "sub")));
            Assert.IsFalse(SyncDirectoryContains(config, "sub/empty"));
            Assert.IsFalse(SyncDirectoryContains(config, "sub"));
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

        // 笏笏笏 Phase 1: 繧�E�繝斐・ (#24窶・2) 笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏

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

            Assert.AreEqual(before, after, "Content unchanged 竊�Edst must not be overwritten");
        }

        // #27
        [Test]
        public void Phase1_FileFailsFilter_NotCopied()
        {
            WriteSrc("file.txt", "data");
            AssetDatabase.Refresh();

            // Texture2D 蝙九�E縺�E�險�E�蜿�E� 竊�ETextAsset 縺�E�髯�E�螟�E
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

            // TextAsset 蝙九�E縺�E�險�E�蜿�E� 竊�E.txt 縺�E�繧�E�繝斐・縺輔ｌ繧・
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

        [Test]
        public void Phase1_EmptySubdir_CopiedWhenIncludeSubdirectoriesIsTrue()
        {
            Directory.CreateDirectory(Path.Combine(_srcFullPath, "sub", "empty"));

            var config = MakeConfig(includeSubdirectories: true);
            AssetSyncer.SyncConfig(config);

            Assert.IsTrue(Directory.Exists(Path.Combine(_dstFullPath, "sub", "empty")));
            Assert.IsTrue(SyncDirectoryContains(config, "sub"));
            Assert.IsTrue(SyncDirectoryContains(config, "sub/empty"));
        }

        [Test]
        public void Phase1_EmptySubdir_NotCopiedWhenIncludeSubdirectoriesIsFalse()
        {
            Directory.CreateDirectory(Path.Combine(_srcFullPath, "sub", "empty"));
            WriteSrc("root.txt", "root");

            var config = MakeConfig(includeSubdirectories: false);
            AssetSyncer.SyncConfig(config);

            Assert.IsTrue(DstExists("root.txt"));
            Assert.IsFalse(Directory.Exists(Path.Combine(_dstFullPath, "sub")));
            Assert.IsFalse(SyncDirectoryContains(config, "sub"));
            Assert.IsFalse(SyncDirectoryContains(config, "sub/empty"));
        }

        // #30
        // meta 繝輔ぃ繧�E�繝ｫ縺後さ繝斐・縺輔ｌ縺�E�縺・↑縺・�E�E���E�縺�E�遒ｺ隱・
        // 繧�E�繝斐・縺輔ｌ縺�E�縺・�E�縺�E� src 縺�E� dst 縺�E� GUID 縺御�E�閾�E�縺吶�E�縺後ゞnity 縺檎峡遶九＠縺�E�逕滓�E縺励◁E���E�蜷医・逡�E�縺�E�繧九・
        [Test]
        public void Phase1_MetaFile_NotCopied()
        {
            WriteSrc("file.txt", "data");
            AssetDatabase.Refresh(); // src/.meta 縺檎�E謌�E�E�E��後ａE

            AssetSyncer.SyncConfig(MakeConfig()); // 蜀・Κ縺�E� Refresh 竊�Edst/.meta 縺・Unity 縺�E�繧医�E�逕滓�E縺輔ｌ繧・

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
        public void Phase1_CopiedFile_AddedToSync()
        {
            WriteSrc("file.txt");
            var config = MakeConfig();
            AssetSyncer.SyncConfig(config);
            Assert.IsTrue(SyncContains(config, "file.txt"));
        }

        // #32
        [Test]
        public void Phase1_AlreadySynced_StillInSync()
        {
            WriteSrc("file.txt");
            var config = MakeConfig();
            AssetSyncer.SyncConfig(config);
            AssetSyncer.SyncConfig(config);
            Assert.IsTrue(SyncContains(config, "file.txt"));
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
            AssetDatabase.Refresh(); // dst/.meta 縺檎�E謌�E�E�E��後ａE
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

            // 蜀榊�E譛溘＠縺�E�繧・src 縺悟ｭ伜惠縺励※繝輔ぅ繝ｫ繧�E�騾夐℃ 竊�E菫晁E��
            AssetSyncer.SyncConfig(config);
            Assert.IsTrue(DstExists("file.txt"));
        }

        // #36
        [Test]
        public void Phase2_NonManifestFile_SrcNotExist_Kept()
        {
            // dst 縺�E�謁E��虚驟咲�E��E�・医・繝九ヵ繧�E�繧�E�繝医↑縺暦�E�・
            WriteDst("manual.txt", "manual");
            AssetSyncer.SyncConfig(MakeConfig());
            Assert.IsTrue(DstExists("manual.txt"), "manually placed file must not be deleted");
        }

        // #37
        [Test]
        public void Phase2_NonManifestFile_SrcExists_Kept()
        {
            // src 縺�E� dst 縺�E�蜷悟錐繝輔ぃ繧�E�繝ｫ縺後≠縺�E�縺�E�繧めE�‥st 縺後�E繝九ヵ繧�E�繧�E�繝亥�E�悶↑繧芽�E��E�繧峨↑縺・
            WriteSrc("file.txt", "src-content");
            WriteDst("file.txt", "different-manual-content");

            // src 縺�E�繝輔ぃ繧�E�繝ｫ繧偵ヵ繧�E�繝ｫ繧�E�縺�E�髯�E�螟悶�E�縺�E�蜷梧悁E�E・st 縺�E� file.txt 縺�E�繝槭ル繝輔ぉ繧�E�繝医↓蜈�E�繧峨↑縺・�E�・
            var exclude = new FilterCondition
            {
                singleTypeName = typeof(TextAsset).AssemblyQualifiedName,
                invert = true
            };
            AssetDatabase.Refresh();
            AssetSyncer.SyncConfig(MakeConfig(new List<FilterCondition> { exclude }));

            // 繝槭ル繝輔ぉ繧�E�繝亥�E�悶↑縺�E�縺�E�蜀・�E��E�縺碁E��E���E�縺�E�繧めE��晁E��
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
            Assert.IsTrue(SyncContains(config, "file.txt"));

            var exclude = new FilterCondition
            {
                singleTypeName = typeof(TextAsset).AssemblyQualifiedName,
                invert = true
            };
            config.filters = new List<FilterCondition> { exclude };
            AssetSyncer.SyncConfig(config);
            Assert.IsFalse(DstExists("file.txt"));
            Assert.IsFalse(SyncContains(config, "file.txt"));
        }

        // #39
        [Test]
        public void Phase2_ManifestFile_FilterChanged_ContentDiffers_Kept()
        {
            WriteSrc("file.txt", "original");
            AssetDatabase.Refresh();

            // 繝輔ぅ繝ｫ繧�E�縺�E�縺励〒蜷梧悁E竊�E繝槭ル繝輔ぉ繧�E�繝医↓逋ｻ骭�E�
            var config = MakeConfig();
            AssetSyncer.SyncConfig(config);
            Assert.IsTrue(DstExists("file.txt"));

            // dst 繧呈�E蜍輔〒螟画峩
            WriteDst("file.txt", "manually-modified");

            // TextAsset 繧帝勁螟悶☁E��九ヵ繧�E�繝ｫ繧�E�縺�E�蜀榊�E譛�E
            // dst 蜀・�E��E�縺・src 縺�E�逡�E�縺�E�繧・竊�E謁E��虚螟画峩縺�E�隕九�E縺嶺�E�晁E��
            var exclude = new FilterCondition
            {
                singleTypeName = typeof(TextAsset).AssemblyQualifiedName,
                invert = true
            };
            config.filters = new List<FilterCondition> { exclude };
            AssetSyncer.SyncConfig(config);
            Assert.IsTrue(DstExists("file.txt"), "manually modified file must not be deleted");
        }

        [Test]
        public void Phase2_FilterChanged_EmptyManagedSubdir_IsDeleted()
        {
            WriteSrc("sub/file.txt", "synced");
            AssetDatabase.Refresh();

            var config = MakeConfig();
            AssetSyncer.SyncConfig(config);
            Assert.IsTrue(DstExists("sub/file.txt"));

            var exclude = new FilterCondition
            {
                singleTypeName = typeof(TextAsset).AssemblyQualifiedName,
                invert = true
            };
            config.filters = new List<FilterCondition> { exclude };
            AssetSyncer.SyncConfig(config);

            Assert.IsFalse(DstExists("sub/file.txt"), "filtered-out managed file should be deleted");
            Assert.IsFalse(Directory.Exists(Path.Combine(_dstFullPath, "sub")), "empty managed subdirectory should be removed");
        }

        [Test]
        public void Phase2_FilterChanged_SubdirWithManualFile_Remains()
        {
            WriteSrc("sub/file.txt", "synced");
            AssetDatabase.Refresh();

            var config = MakeConfig();
            AssetSyncer.SyncConfig(config);
            WriteDst("sub/manual.txt", "manual");

            var exclude = new FilterCondition
            {
                singleTypeName = typeof(TextAsset).AssemblyQualifiedName,
                invert = true
            };
            config.filters = new List<FilterCondition> { exclude };
            AssetSyncer.SyncConfig(config);

            Assert.IsFalse(DstExists("sub/file.txt"), "filtered-out managed file should be deleted");
            Assert.IsTrue(DstExists("sub/manual.txt"), "manual file should remain");
            Assert.IsTrue(Directory.Exists(Path.Combine(_dstFullPath, "sub")), "subdirectory with manual content should remain");
        }

        [Test]
        public void Phase2_EmptySubdir_SourceDeleted_IsDeleted()
        {
            Directory.CreateDirectory(Path.Combine(_srcFullPath, "sub", "empty"));
            var config = MakeConfig();
            AssetSyncer.SyncConfig(config);

            Assert.IsTrue(Directory.Exists(Path.Combine(_dstFullPath, "sub", "empty")));
            Assert.IsTrue(SyncDirectoryContains(config, "sub/empty"));

            DeleteSrcDirectory("sub/empty");
            if (!Directory.EnumerateFileSystemEntries(Path.Combine(_srcFullPath, "sub")).Any())
                DeleteSrcDirectory("sub");

            AssetSyncer.SyncConfig(config);

            Assert.IsFalse(Directory.Exists(Path.Combine(_dstFullPath, "sub", "empty")));
            Assert.IsFalse(Directory.Exists(Path.Combine(_dstFullPath, "sub")));
            Assert.IsFalse(SyncDirectoryContains(config, "sub/empty"));
            Assert.IsFalse(SyncDirectoryContains(config, "sub"));
        }

        [Test]
        public void Phase2_EmptySubdir_SourceDeleted_WithManualContent_Remains()
        {
            Directory.CreateDirectory(Path.Combine(_srcFullPath, "sub", "empty"));
            var config = MakeConfig();
            AssetSyncer.SyncConfig(config);
            WriteDst("sub/empty/manual.txt", "manual");

            DeleteSrcDirectory("sub/empty");
            if (!Directory.EnumerateFileSystemEntries(Path.Combine(_srcFullPath, "sub")).Any())
                DeleteSrcDirectory("sub");

            AssetSyncer.SyncConfig(config);

            Assert.IsTrue(DstExists("sub/empty/manual.txt"), "manual file should remain");
            Assert.IsTrue(Directory.Exists(Path.Combine(_dstFullPath, "sub", "empty")), "subdirectory with manual file should remain");
            Assert.IsFalse(SyncDirectoryContains(config, "sub/empty"), "directory ownership should be released");
        }

        // #40
        [Test]
        public void Phase2_EmptySubdirAfterDelete_IsDeleted()
        {
            var config = MakeConfig();
            WriteSrc("sub/file.txt");
            AssetSyncer.SyncConfig(config);
            Assert.IsTrue(DstExists("sub/file.txt"));

            DeleteSrc("sub/file.txt");
            AssetSyncer.SyncConfig(config);

            Assert.IsFalse(DstExists("sub/file.txt"), "file should be deleted");
            string subDir = Path.Combine(_dstFullPath, "sub");
            Assert.IsFalse(Directory.Exists(subDir), "empty managed subdirectory should be removed");
        }

        [Test]
        public void Phase2_ManagedDelete_SubdirectoryWithManualFile_Remains()
        {
            var config = MakeConfig();
            WriteSrc("sub/file.txt");
            AssetSyncer.SyncConfig(config);
            WriteDst("sub/manual.txt", "manual");

            DeleteSrc("sub/file.txt");
            AssetSyncer.SyncConfig(config);

            Assert.IsFalse(DstExists("sub/file.txt"), "synced file should be deleted");
            Assert.IsTrue(DstExists("sub/manual.txt"), "manual file should remain");
            Assert.IsTrue(Directory.Exists(Path.Combine(_dstFullPath, "sub")), "subdirectory with manual content should remain");
        }

        // #41
        [Test]
        public void Phase2_DeletedFile_RemovedFromSync()
        {
            WriteSrc("file.txt");
            var config = MakeConfig();
            AssetSyncer.SyncConfig(config);
            Assert.IsTrue(SyncContains(config, "file.txt"));

            DeleteSrc("file.txt");
            AssetSyncer.SyncConfig(config);
            Assert.IsFalse(SyncContains(config, "file.txt"));
        }

        // 笏笏笏 繝槭ル繝輔ぉ繧�E�繝�E(#42窶・4) 笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏笏

        // #42
        [Test]
        public void State_FirstSync_SyncPathCreated()
        {
            WriteSrc("file.txt");
            var config = MakeConfig();

            AssetSyncer.SyncConfig(config);
            Assert.IsTrue(SyncContains(config, "file.txt"));
        }

        // #43
        [Test]
        public void State_DifferentConfigs_KeepIndependentSyncLists()
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

            Assert.IsTrue(SyncContains(config1, "file.txt"));
            Assert.IsTrue(SyncContains(config2, "file.txt"));
        }

        // #44
        [Test]
        public void State_Normalize_NormalizesSyncFilesAndDirectories()
        {
            var config = MakeConfig();
            config.syncRelativePaths = new List<string> { "a.txt", "a.txt", "A.txt", "sub\\b.txt" };
            config.syncRelativeDirectoryPaths = new List<string> { "sub", "sub/", "SUB", "x\\y" };
            config.ignoreGuids = new List<string> { "", "g1", "g1", "g2" };

            bool changed = AssetSyncer.NormalizeState(config);

            Assert.IsTrue(changed);
            Assert.AreEqual(2, config.syncRelativePaths.Count);
            Assert.AreEqual("a.txt", config.syncRelativePaths[0]);
            Assert.AreEqual("sub/b.txt", config.syncRelativePaths[1]);
            Assert.AreEqual(2, config.syncRelativeDirectoryPaths.Count);
            Assert.AreEqual("sub", config.syncRelativeDirectoryPaths[0]);
            Assert.AreEqual("x/y", config.syncRelativeDirectoryPaths[1]);
            Assert.AreEqual(4, config.ignoreGuids.Count);
            Assert.AreEqual("", config.ignoreGuids[0]);
            Assert.AreEqual("g1", config.ignoreGuids[1]);
            Assert.AreEqual("g1", config.ignoreGuids[2]);
            Assert.AreEqual("g2", config.ignoreGuids[3]);
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

        // #47 窶・縲訓reset蝙九ｒ蜷�E�繧縲阪ヵ繧�E�繝ｫ繧�E� + dst 縺�E�蛻�E�蜷恒reset 竊�Edst 縺�E� Preset 菫晁E��縲《rc 縺�E� Preset 繧めE��逹
        [Test]
        public void Integration_FilterIncludesType_OtherDstFileKept()
        {
            WriteSrc("src-preset.txt", "src");
            WriteDst("dst-preset.txt", "manual");
            AssetDatabase.Refresh();

            // TextAsset 蝙九�E縺�E�險�E�蜿�E�
            var include = new FilterCondition { singleTypeName = typeof(TextAsset).AssemblyQualifiedName };
            AssetSyncer.SyncConfig(MakeConfig(new List<FilterCondition> { include }));

            Assert.IsTrue(DstExists("src-preset.txt"), "src file that passes filter must be copied");
            Assert.IsTrue(DstExists("dst-preset.txt"), "manually placed file must not be touched");
        }

        // #48 窶・縲訓reset蝙九ｒ髯�E�螟悶阪ヵ繧�E�繝ｫ繧�E� + dst 縺�E�蛻�E�蜷恒reset 竊�Edst 縺�E� Preset 菫晁E��
        [Test]
        public void Integration_FilterExcludesType_ManualDstFileKept()
        {
            WriteSrc("src-file.txt", "src");
            WriteDst("manual.txt", "manual");
            AssetDatabase.Refresh();

            // TextAsset 髯�E�螟�E竊�Esrc 縺�E�繝輔ぃ繧�E�繝ｫ縺�E�繧�E�繝斐・縺輔ｌ縺�E�縺・
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
        public void IgnoreDestination_RemoveProtection_ResumesSyncWithoutConflict()
        {
            var config = MakeConfig();
            WriteSrc("file.txt", "v1");
            AssetSyncer.SyncConfig(config);
            Assert.AreEqual("v1", ReadDst("file.txt"));
            Assert.IsTrue(SyncContains(config, "file.txt"));

            AssetDatabase.Refresh();
            string dstAssetPath = _dstAssetPath + "/file.txt";
            string dstGuid = AssetDatabase.AssetPathToGUID(dstAssetPath);
            Assume.That(!string.IsNullOrEmpty(dstGuid), "destination GUID must be available");

            config.ignoreGuids.Add(dstGuid);
            WriteSrc("file.txt", "v2");
            AssetSyncer.SyncConfig(config);
            Assert.AreEqual("v1", ReadDst("file.txt"), "destination ignore file should not update");
            Assert.IsTrue(SyncContains(config, "file.txt"), "synced state should be retained while destination is ignore");

            config.ignoreGuids.Remove(dstGuid);
            AssetSyncer.SyncConfig(config);
            Assert.AreEqual("v2", ReadDst("file.txt"), "sync should resume after removing destination protection");
        }

        [Test]
        public void IgnoreDestination_RemoveProtection_AfterDisableEnable_ResumesSyncWithoutConflict()
        {
            var config = MakeConfig();
            WriteSrc("file.txt", "v1");
            AssetSyncer.SyncConfig(config);
            Assert.AreEqual("v1", ReadDst("file.txt"));
            Assert.IsTrue(SyncContains(config, "file.txt"));

            AssetDatabase.Refresh();
            string dstAssetPath = _dstAssetPath + "/file.txt";
            string dstGuid = AssetDatabase.AssetPathToGUID(dstAssetPath);
            Assume.That(!string.IsNullOrEmpty(dstGuid), "destination GUID must be available");

            config.ignoreGuids.Add(dstGuid);
            WriteSrc("file.txt", "v2");
            AssetSyncer.SyncConfig(config);
            Assert.AreEqual("v1", ReadDst("file.txt"), "destination ignore file should not update");
            Assert.IsTrue(SyncContains(config, "file.txt"), "synced state should be retained while destination is ignore");

            config.enabled = false;
            AssetSyncer.SyncConfig(config);
            Assert.IsTrue(DstExists("file.txt"), "destination ignore file should remain when sync is disabled");
            Assert.IsTrue(SyncContains(config, "file.txt"), "synced state should be retained for destination ignore files while disabled");

            config.enabled = true;
            AssetSyncer.SyncConfig(config);

            int conflictDialogCalls = 0;
            AssetSyncer.ConflictResolverOverride = (SyncConfig _, IReadOnlyList<AssetSyncer.SyncConflict> conflicts, out Dictionary<string, AssetSyncer.ConflictResolution> decisions) =>
            {
                conflictDialogCalls++;
                decisions = new Dictionary<string, AssetSyncer.ConflictResolution>();
                foreach (var conflict in conflicts)
                    decisions[conflict.NormalizedRelativePath] = AssetSyncer.ConflictResolution.Ignore;
                return true;
            };

            config.ignoreGuids.Remove(dstGuid);
            AssetSyncer.SyncConfig(config);

            Assert.AreEqual(0, conflictDialogCalls, "removing destination protection should not open conflicts dialog");
            Assert.AreEqual("v2", ReadDst("file.txt"), "sync should resume after removing destination protection");
            Assert.IsTrue(SyncContains(config, "file.txt"), "file should remain synced after protection is removed");
        }

        [Test]
        public void PruneSyncPathsForDisabledConfig_RemoveDestinationProtection_RemovesSyncWithoutDeleting()
        {
            var config = MakeConfig();
            WriteSrc("file.txt", "v1");
            AssetSyncer.SyncConfig(config);
            Assert.IsTrue(DstExists("file.txt"));
            Assert.IsTrue(SyncContains(config, "file.txt"));

            AssetDatabase.Refresh();
            string dstGuid = AssetDatabase.AssetPathToGUID(_dstAssetPath + "/file.txt");
            Assume.That(!string.IsNullOrEmpty(dstGuid), "destination guid must be available");
            config.ignoreGuids.Add(dstGuid);

            config.enabled = false;
            AssetSyncer.SyncConfig(config);
            Assert.IsTrue(DstExists("file.txt"), "destination ignore file should remain when disabled");
            Assert.IsTrue(SyncContains(config, "file.txt"), "synced should remain while destination is ignore");

            config.ignoreGuids.Remove(dstGuid);
            bool changed = AssetSyncer.PruneSyncPathsForDisabledConfig(config);

            Assert.IsTrue(changed, "removing destination protection while disabled should drop synced state");
            Assert.IsTrue(DstExists("file.txt"), "pruning ownership must not delete destination file");
            Assert.IsFalse(SyncContains(config, "file.txt"), "destination file should become unsynced");
        }

        [Test]
        public void CollectSyncedDestinationSyncRelativePaths_ExcludesDestinationIgnore()
        {
            var config = MakeConfig();
            WriteSrc("ignore.txt", "p1");
            WriteSrc("normal.txt", "n1");
            AssetSyncer.SyncConfig(config);

            AssetDatabase.Refresh();
            string ignoreGuid = AssetDatabase.AssetPathToGUID(_dstAssetPath + "/ignore.txt");
            Assume.That(!string.IsNullOrEmpty(ignoreGuid), "destination ignore asset guid must exist");
            config.ignoreGuids.Add(ignoreGuid);

            HashSet<string> syncedSync = AssetSyncer.CollectSyncedDestinationSyncRelativePaths(config);
            Assert.IsFalse(syncedSync.Contains("ignore.txt"));
            Assert.IsTrue(syncedSync.Contains("normal.txt"));
        }

        [Test]
        public void PostprocessorPathMatch_ExactRootAndChildOnly()
        {
            const string root = "Assets/Foo";

            Assert.IsTrue(AssetSyncPostprocessor.IsAssetPathWithinRoot("Assets/Foo", root));
            Assert.IsTrue(AssetSyncPostprocessor.IsAssetPathWithinRoot("Assets/Foo/Bar.asset", root));
            Assert.IsFalse(AssetSyncPostprocessor.IsAssetPathWithinRoot("Assets/FooBar/Bar.asset", root));
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

            Assert.IsTrue(AssetSyncPostprocessor.IsAssetPathWithinScope("Assets/Foo", root, includeSubdirectories: false));
            Assert.IsTrue(AssetSyncPostprocessor.IsAssetPathWithinScope("Assets/Foo/Bar.asset", root, includeSubdirectories: false));
            Assert.IsFalse(AssetSyncPostprocessor.IsAssetPathWithinScope("Assets/Foo/Sub/Bar.asset", root, includeSubdirectories: false));

            Assert.IsTrue(AssetSyncPostprocessor.IsAssetPathWithinScope("Assets/Foo/Sub/Bar.asset", root, includeSubdirectories: true));
        }

        [Test]
        public void PostprocessorTryRemapPathByMoves_ExactMatch_ReturnsRemappedPath()
        {
            string movedTo = _testRoot + "/Moved";
            Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(Application.dataPath), movedTo));
            AssetDatabase.Refresh();

            bool changed = AssetSyncPostprocessor.TryRemapPathByMoves(
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

            bool changed = AssetSyncPostprocessor.TryRemapPathByMoves(
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

            bool changed = AssetSyncPostprocessor.TryRemapPathByMoves(
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

            bool changed = AssetSyncPostprocessor.TryRemapPathByMoves(
                _testRoot + "/Before/Sub/Target",
                new[] { movedParent, movedChild },
                new[] { _testRoot + "/Before", _testRoot + "/Before/Sub" },
                out string remappedPath);

            Assert.IsTrue(changed);
            Assert.AreEqual(movedChild + "/Target", remappedPath);
        }

    }
}
