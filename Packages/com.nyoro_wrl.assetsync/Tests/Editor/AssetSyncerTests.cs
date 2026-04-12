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

            // AssetDatabase.DeleteAsset 邵ｺ・ｽE・ｽ郢晁ｼ斐°郢晢ｽｫ郢敖邵ｺ譴ｧ謔ｴ騾具ｽｻ鬪ｭ・ｽE・ｽ邵ｺ・ｽE・ｽ邵ｺ・ｽE・ｽ陞滂ｽｱ隰ｨ蜉ｱ笘・・ｽ・ｽ荵昶螺郢ｧ竏堋繝ｻ
            // FileUtil 邵ｺ・ｽE・ｽ騾ｶ・ｽE・ｽ隰暦ｽ･陷台ｼ∝求邵ｺ蜉ｱ窶ｻ邵ｺ荵晢ｽ・Refresh 邵ｺ蜷ｶ・ｽE・ｽE
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string fullTestRoot = Path.GetFullPath(Path.Combine(projectRoot, _testRoot));
            FileUtil.DeleteFileOrDirectory(fullTestRoot);
            FileUtil.DeleteFileOrDirectory(fullTestRoot + ".meta");
            AssetDatabase.Refresh();
            AssetSyncer.ConflictResolverOverride = null;
        }

        // 隨渉隨渉隨渉 Helpers 隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉

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
            // SyncConfig 邵ｺ・ｽE・ｽ Refresh 邵ｺ・ｽE・ｽ郢ｧ・ｽE・ｽ郢晢ｽｳ郢晄亢繝ｻ郢晏沺・ｽE・ｽ蛹ｻ竏ｩ邵ｺ・ｽE・ｽ .meta 郢ｧ繧・・ｽ・ｽ鬮ｯ・ｽE・ｽ邵ｺ蜉ｱ竊醍ｸｺ繝ｻ竊定氛・ｽE・ｽ驕ｶ繝ｻ.meta 髫ｴ・ｽE・ｽ陷ｻ鄙ｫ窶ｲ陷・・ｽ・ｽ郢ｧ繝ｻ
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

        private SyncConfig MakeConfig(
            List<FilterCondition> filters = null,
            bool includeSubdirectories = true,
            bool keepEmptyDirectories = false)
        {
            return new SyncConfig
            {
                configName = "TestConfig",
                enabled = true,
                includeSubdirectories = includeSubdirectories,
                keepEmptyDirectories = keepEmptyDirectories,
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

        // 隨渉隨渉隨渉 ShouldCopy (#15遯ｶ繝ｻ8) 隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉

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

        // 隨渉隨渉隨渉 SyncConfig 郢晢ｿｽEﾎ懃ｹ晢ｿｽE繝ｻ郢ｧ・ｽE・ｽ郢晢ｽｧ郢晢ｽｳ (#19遯ｶ繝ｻ3) 隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉

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

        [Test]
        public void CollectCopyPreviewDestinationAssetPaths_EmptySubdirectory_DoesNotIncludeDirectoryPath()
        {
            Directory.CreateDirectory(Path.Combine(_srcFullPath, "preview", "empty"));
            var config = MakeConfig();

            IReadOnlyList<string> preview = AssetSyncer.CollectCopyPreviewDestinationAssetPaths(config);

            CollectionAssert.DoesNotContain(preview, _dstAssetPath + "/preview/empty");
        }

        [Test]
        public void CollectCopyPreviewDestinationAssetPaths_NonEmptySubdirectory_DoesNotIncludeDirectoryPath()
        {
            WriteSrc("preview/non-empty/file.txt", "v1");
            var config = MakeConfig();

            IReadOnlyList<string> preview = AssetSyncer.CollectCopyPreviewDestinationAssetPaths(config);

            CollectionAssert.Contains(preview, _dstAssetPath + "/preview/non-empty/file.txt");
            CollectionAssert.DoesNotContain(preview, _dstAssetPath + "/preview/non-empty");
        }

        [Test]
        public void CollectCopyPreviewDestinationAssetPaths_KeepEmptyDirectories_IncludesNestedDirectoryPaths()
        {
            WriteSrc("content/with-file/data.txt", "v1");
            Directory.CreateDirectory(Path.Combine(_srcFullPath, "content", "empty", "leaf"));

            var config = MakeConfig(includeSubdirectories: true, keepEmptyDirectories: true);

            IReadOnlyList<string> preview = AssetSyncer.CollectCopyPreviewDestinationAssetPaths(config);

            CollectionAssert.Contains(preview, _dstAssetPath + "/content");
            CollectionAssert.Contains(preview, _dstAssetPath + "/content/with-file");
            CollectionAssert.Contains(preview, _dstAssetPath + "/content/empty");
            CollectionAssert.Contains(preview, _dstAssetPath + "/content/empty/leaf");
            CollectionAssert.Contains(preview, _dstAssetPath + "/content/with-file/data.txt");
        }

        [Test]
        public void CollectCopyPreviewDestinationAssetPaths_KeepEmptyDirectories_WithFilters_IncludesDirectoriesThatBecomeEmpty()
        {
            WriteSrc("filtered/by-filter/only.bytes", "x");
            var includeByExtension = new FilterCondition
            {
                targetKind = FilterConditionTargetKind.Extension,
                multipleExtensions = new List<string> { ".txt" }
            };
            var config = MakeConfig(
                new List<FilterCondition> { includeByExtension },
                includeSubdirectories: true,
                keepEmptyDirectories: true);

            IReadOnlyList<string> preview = AssetSyncer.CollectCopyPreviewDestinationAssetPaths(config);

            CollectionAssert.Contains(preview, _dstAssetPath + "/filtered");
            CollectionAssert.Contains(preview, _dstAssetPath + "/filtered/by-filter");
            CollectionAssert.DoesNotContain(preview, _dstAssetPath + "/filtered/by-filter/only.bytes");
        }

        [Test]
        public void CollectCopyPreviewDestinationAssetPaths_KeepEmptyDirectories_SortsDirectoriesFirstWithinSameParent()
        {
            WriteSrc("sameParent/alpha.txt", "a");
            WriteSrc("sameParent/beta.txt", "b");
            Directory.CreateDirectory(Path.Combine(_srcFullPath, "sameParent", "nestedDir"));
            var config = MakeConfig(includeSubdirectories: true, keepEmptyDirectories: true);

            IReadOnlyList<string> preview = AssetSyncer.CollectCopyPreviewDestinationAssetPaths(config);
            List<string> previewList = preview.ToList();

            int nestedDirIndex = previewList.IndexOf(_dstAssetPath + "/sameParent/nestedDir");
            int alphaIndex = previewList.IndexOf(_dstAssetPath + "/sameParent/alpha.txt");
            int betaIndex = previewList.IndexOf(_dstAssetPath + "/sameParent/beta.txt");

            Assert.GreaterOrEqual(nestedDirIndex, 0, "directory entry should exist in preview");
            Assert.GreaterOrEqual(alphaIndex, 0, "file entry should exist in preview");
            Assert.GreaterOrEqual(betaIndex, 0, "file entry should exist in preview");
            Assert.Less(nestedDirIndex, alphaIndex, "directory should be listed before sibling files");
            Assert.Less(nestedDirIndex, betaIndex, "directory should be listed before sibling files");
        }

        [Test]
        public void CollectCopyPreviewEntries_KeepEmptyDirectories_IncludeUnchangedShowsAlreadySyncedDirectories()
        {
            WriteSrc("nested/a/file.txt", "v1");
            Directory.CreateDirectory(Path.Combine(_srcFullPath, "nested", "empty", "leaf"));
            var config = MakeConfig(includeSubdirectories: true, keepEmptyDirectories: true);

            AssetSyncer.SyncConfig(config);

            IReadOnlyList<AssetSyncer.PreviewCopyEntry> preview = AssetSyncer.CollectCopyPreviewEntries(
                config,
                includeUnchanged: true);
            IReadOnlyList<string> destinationPaths = preview.Select(entry => entry.DestinationAssetPath).ToList();

            CollectionAssert.Contains(destinationPaths, _dstAssetPath + "/nested");
            CollectionAssert.Contains(destinationPaths, _dstAssetPath + "/nested/a");
            CollectionAssert.Contains(destinationPaths, _dstAssetPath + "/nested/empty");
            CollectionAssert.Contains(destinationPaths, _dstAssetPath + "/nested/empty/leaf");
            CollectionAssert.Contains(destinationPaths, _dstAssetPath + "/nested/a/file.txt");
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
        public void SyncConfig_EmptySubdirectory_IsNotCreatedAndNotTracked()
        {
            Directory.CreateDirectory(Path.Combine(_srcFullPath, "sub", "empty"));
            var config = MakeConfig();

            AssetSyncer.SyncConfig(config);
            Assert.IsFalse(Directory.Exists(Path.Combine(_dstFullPath, "sub", "empty")));
            Assert.IsFalse(SyncDirectoryContains(config, "sub/empty"));

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

        [Test]
        public void SyncConfig_ExternalSourceDirectory_CopiesFiles()
        {
            string externalRoot = Path.Combine(Path.GetTempPath(), "AssetSyncExternal_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(externalRoot);
            try
            {
                File.WriteAllText(Path.Combine(externalRoot, "external.txt"), "external");

                var config = MakeConfig();
                config.sourcePath = externalRoot;

                int copied = AssetSyncer.SyncConfig(config);

                Assert.AreEqual(1, copied);
                Assert.IsTrue(DstExists("external.txt"));
                Assert.IsTrue(SyncContains(config, "external.txt"));
            }
            finally
            {
                if (Directory.Exists(externalRoot))
                    Directory.Delete(externalRoot, true);
            }
        }

        [Test]
        public void SyncConfig_ExternalSourceDirectory_ExtensionFilter_Works()
        {
            string externalRoot = Path.Combine(Path.GetTempPath(), "AssetSyncExternal_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(externalRoot);
            try
            {
                File.WriteAllText(Path.Combine(externalRoot, "a.txt"), "A");
                File.WriteAllText(Path.Combine(externalRoot, "b.bytes"), "B");

                var config = MakeConfig(new List<FilterCondition>
                {
                    new FilterCondition
                    {
                        targetKind = FilterConditionTargetKind.Extension,
                        multipleExtensions = new List<string> { ".txt" }
                    }
                });
                config.sourcePath = externalRoot;

                AssetSyncer.SyncConfig(config);

                Assert.IsTrue(DstExists("a.txt"));
                Assert.IsFalse(DstExists("b.bytes"));
            }
            finally
            {
                if (Directory.Exists(externalRoot))
                    Directory.Delete(externalRoot, true);
            }
        }

        [Test]
        public void TryGetConfigWarning_ExternalSourceDirectory_WithTypeFilter_ReturnsWarning()
        {
            string externalRoot = Path.Combine(Path.GetTempPath(), "AssetSyncExternal_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(externalRoot);
            try
            {
                bool hasWarning = AssetSyncer.TryGetConfigWarning(new SyncConfig
                {
                    sourcePath = externalRoot,
                    destinationPath = _dstAssetPath,
                    enabled = true,
                    filters = new List<FilterCondition>
                    {
                        new FilterCondition
                        {
                            targetKind = FilterConditionTargetKind.Type,
                            multipleTypeNames = new List<string> { typeof(TextAsset).AssemblyQualifiedName }
                        }
                    }
                }, out string warning);

                Assert.IsTrue(hasWarning);
                StringAssert.Contains("only Extension filters are supported", warning);
            }
            finally
            {
                if (Directory.Exists(externalRoot))
                    Directory.Delete(externalRoot, true);
            }
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

        // 隨渉隨渉隨渉 Phase 1: 郢ｧ・ｽE・ｽ郢晄鱒繝ｻ (#24遯ｶ繝ｻ2) 隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉

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

            Assert.AreEqual(before, after, "Content unchanged 遶奇ｿｽEdst must not be overwritten");
        }

        // #27
        [Test]
        public void Phase1_FileFailsFilter_NotCopied()
        {
            WriteSrc("file.txt", "data");
            AssetDatabase.Refresh();

            // Texture2D 陜吩ｹ晢ｿｽE邵ｺ・ｽE・ｽ髫ｪ・ｽE・ｽ陷ｿ・ｽE・ｽ 遶奇ｿｽETextAsset 邵ｺ・ｽE・ｽ鬮ｯ・ｽE・ｽ陞滂ｿｽE
            var filter = new FilterCondition { multipleTypeNames = new List<string> { typeof(Texture2D).AssemblyQualifiedName } };
            AssetSyncer.SyncConfig(MakeConfig(new List<FilterCondition> { filter }));
            Assert.IsFalse(DstExists("file.txt"));
        }

        // #28
        [Test]
        public void Phase1_FilePassesFilter_Copied()
        {
            WriteSrc("file.txt", "data");
            AssetDatabase.Refresh();

            // TextAsset 陜吩ｹ晢ｿｽE邵ｺ・ｽE・ｽ髫ｪ・ｽE・ｽ陷ｿ・ｽE・ｽ 遶奇ｿｽE.txt 邵ｺ・ｽE・ｽ郢ｧ・ｽE・ｽ郢晄鱒繝ｻ邵ｺ霈費ｽ檎ｹｧ繝ｻ
            var filter = new FilterCondition { multipleTypeNames = new List<string> { typeof(TextAsset).AssemblyQualifiedName } };
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
        public void Phase1_EmptySubdir_NotCopiedWhenIncludeSubdirectoriesIsTrue()
        {
            Directory.CreateDirectory(Path.Combine(_srcFullPath, "sub", "empty"));

            var config = MakeConfig(includeSubdirectories: true);
            AssetSyncer.SyncConfig(config);

            Assert.IsFalse(Directory.Exists(Path.Combine(_dstFullPath, "sub", "empty")));
            Assert.IsFalse(SyncDirectoryContains(config, "sub"));
            Assert.IsFalse(SyncDirectoryContains(config, "sub/empty"));
        }

        [Test]
        public void Phase1_EmptySubdir_CopiedWhenKeepEmptyDirectoriesIsTrue()
        {
            Directory.CreateDirectory(Path.Combine(_srcFullPath, "sub", "empty"));

            var config = MakeConfig(includeSubdirectories: true, keepEmptyDirectories: true);
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

        [Test]
        public void Phase1_NestedEmptyDirectories_NotCreatedInDestination_WithoutFilters()
        {
            WriteSrc("root.txt", "root");
            Directory.CreateDirectory(Path.Combine(_srcFullPath, "nested", "inner", "leafEmpty"));

            var config = MakeConfig(includeSubdirectories: true);
            AssetSyncer.SyncConfig(config);

            Assert.IsTrue(DstExists("root.txt"));
            Assert.IsFalse(Directory.Exists(Path.Combine(_dstFullPath, "nested", "inner", "leafEmpty")),
                "empty leaf directory should not be created in destination");
            Assert.IsFalse(Directory.Exists(Path.Combine(_dstFullPath, "nested", "inner")),
                "directory containing only empty directories should not be created in destination");
            Assert.IsFalse(Directory.Exists(Path.Combine(_dstFullPath, "nested")),
                "ancestor directory containing only empty directories should not be created in destination");

            Assert.IsFalse(SyncDirectoryContains(config, "nested"));
            Assert.IsFalse(SyncDirectoryContains(config, "nested/inner"));
            Assert.IsFalse(SyncDirectoryContains(config, "nested/inner/leafEmpty"));
        }

        [Test]
        public void Phase1_NestedEmptyDirectories_NotCreatedInDestination_WhenBecomingEmptyByFilters()
        {
            WriteSrc("root.txt", "root");
            WriteSrc("filteredOut/sub/only.bytes", "X");
            WriteSrc("filteredOut/sub/deep/only2.bytes", "Y");
            Directory.CreateDirectory(Path.Combine(_srcFullPath, "alreadyEmpty", "leafEmpty"));

            var includeByExtension = new FilterCondition
            {
                targetKind = FilterConditionTargetKind.Extension,
                multipleExtensions = new List<string> { ".txt" }
            };
            var config = MakeConfig(new List<FilterCondition> { includeByExtension }, includeSubdirectories: true);
            AssetSyncer.SyncConfig(config);

            Assert.IsTrue(DstExists("root.txt"));
            Assert.IsFalse(DstExists("filteredOut/sub/only.bytes"));
            Assert.IsFalse(DstExists("filteredOut/sub/deep/only2.bytes"));
            Assert.IsFalse(Directory.Exists(Path.Combine(_dstFullPath, "filteredOut", "sub", "deep")),
                "directory should not be created when all files under it are filtered out");
            Assert.IsFalse(Directory.Exists(Path.Combine(_dstFullPath, "filteredOut", "sub")),
                "directory containing only filtered-out descendants should not be created in destination");
            Assert.IsFalse(Directory.Exists(Path.Combine(_dstFullPath, "filteredOut")),
                "ancestor directory containing only filtered-out descendants should not be created in destination");

            Assert.IsFalse(Directory.Exists(Path.Combine(_dstFullPath, "alreadyEmpty", "leafEmpty")),
                "empty leaf directory should not be created in destination");
            Assert.IsFalse(Directory.Exists(Path.Combine(_dstFullPath, "alreadyEmpty")),
                "directory containing only empty directories should not be created in destination");

            Assert.IsFalse(SyncDirectoryContains(config, "filteredOut"));
            Assert.IsFalse(SyncDirectoryContains(config, "filteredOut/sub"));
            Assert.IsFalse(SyncDirectoryContains(config, "filteredOut/sub/deep"));
            Assert.IsFalse(SyncDirectoryContains(config, "alreadyEmpty"));
            Assert.IsFalse(SyncDirectoryContains(config, "alreadyEmpty/leafEmpty"));
        }

        [Test]
        public void Phase1_NestedEmptyDirectories_CreatedInDestination_WhenKeepEmptyDirectoriesIsTrue()
        {
            WriteSrc("root.txt", "root");
            WriteSrc("filteredOut/sub/only.bytes", "X");
            WriteSrc("filteredOut/sub/deep/only2.bytes", "Y");
            Directory.CreateDirectory(Path.Combine(_srcFullPath, "alreadyEmpty", "leafEmpty"));

            var includeByExtension = new FilterCondition
            {
                targetKind = FilterConditionTargetKind.Extension,
                multipleExtensions = new List<string> { ".txt" }
            };
            var config = MakeConfig(
                new List<FilterCondition> { includeByExtension },
                includeSubdirectories: true,
                keepEmptyDirectories: true);
            AssetSyncer.SyncConfig(config);

            Assert.IsTrue(DstExists("root.txt"));
            Assert.IsFalse(DstExists("filteredOut/sub/only.bytes"));
            Assert.IsFalse(DstExists("filteredOut/sub/deep/only2.bytes"));

            Assert.IsTrue(Directory.Exists(Path.Combine(_dstFullPath, "filteredOut", "sub", "deep")),
                "directory should be created when Keep Empty Directories is enabled");
            Assert.IsTrue(Directory.Exists(Path.Combine(_dstFullPath, "alreadyEmpty", "leafEmpty")),
                "empty leaf directory should be created when Keep Empty Directories is enabled");

            Assert.IsTrue(SyncDirectoryContains(config, "filteredOut"));
            Assert.IsTrue(SyncDirectoryContains(config, "filteredOut/sub"));
            Assert.IsTrue(SyncDirectoryContains(config, "filteredOut/sub/deep"));
            Assert.IsTrue(SyncDirectoryContains(config, "alreadyEmpty"));
            Assert.IsTrue(SyncDirectoryContains(config, "alreadyEmpty/leafEmpty"));
        }

        // #30
        // meta 郢晁ｼ斐＜郢ｧ・ｽE・ｽ郢晢ｽｫ邵ｺ蠕後＆郢晄鱒繝ｻ邵ｺ霈費ｽ檎ｸｺ・ｽE・ｽ邵ｺ繝ｻ竊醍ｸｺ繝ｻ・ｽE・ｽE・ｽ・ｽ・ｽE・ｽ邵ｺ・ｽE・ｽ驕抵ｽｺ髫ｱ繝ｻ
        // 郢ｧ・ｽE・ｽ郢晄鱒繝ｻ邵ｺ霈費ｽ檎ｸｺ・ｽE・ｽ邵ｺ繝ｻ・ｽE・ｽ邵ｺ・ｽE・ｽ src 邵ｺ・ｽE・ｽ dst 邵ｺ・ｽE・ｽ GUID 邵ｺ蠕｡・ｽE・ｽﾂ髢ｾ・ｽE・ｽ邵ｺ蜷ｶ・ｽE・ｽ邵ｺ蠕個繧柤ity 邵ｺ讙主ｳ｡驕ｶ荵晢ｼ邵ｺ・ｽE・ｽ騾墓ｻ難ｿｽE邵ｺ蜉ｱ笳・・ｽ・ｽ・ｽE・ｽ陷ｷ蛹ｻ繝ｻ騾｡・ｽE・ｽ邵ｺ・ｽE・ｽ郢ｧ荵敖繝ｻ
        [Test]
        public void Phase1_MetaFile_NotCopied()
        {
            WriteSrc("file.txt", "data");
            AssetDatabase.Refresh(); // src/.meta 邵ｺ讙趣ｿｽE隰鯉ｿｽE・ｽE・ｽE・ｽ・ｽ蠕鯉ｽ・

            AssetSyncer.SyncConfig(MakeConfig()); // 陷繝ｻﾎ夂ｸｺ・ｽE・ｽ Refresh 遶奇ｿｽEdst/.meta 邵ｺ繝ｻUnity 邵ｺ・ｽE・ｽ郢ｧ蛹ｻ・ｽE・ｽ騾墓ｻ難ｿｽE邵ｺ霈費ｽ檎ｹｧ繝ｻ

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

        // 隨渉隨渉隨渉 Phase 2: 陷台ｼ∝求 (#33遯ｶ繝ｻ1) 隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉

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
            AssetDatabase.Refresh(); // dst/.meta 邵ｺ讙趣ｿｽE隰鯉ｿｽE・ｽE・ｽE・ｽ・ｽ蠕鯉ｽ・
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

            // 陷讎奇ｿｽE隴帶ｺ假ｼ邵ｺ・ｽE・ｽ郢ｧ繝ｻsrc 邵ｺ謔滂ｽｭ莨懈Β邵ｺ蜉ｱ窶ｻ郢晁ｼ斐≦郢晢ｽｫ郢ｧ・ｽE・ｽ鬨ｾ螟絶с 遶奇ｿｽE闖ｫ譎・・ｽ・ｽ
            AssetSyncer.SyncConfig(config);
            Assert.IsTrue(DstExists("file.txt"));
        }

        // #36
        [Test]
        public void Phase2_NonManifestFile_SrcNotExist_Kept()
        {
            // dst 邵ｺ・ｽE・ｽ隰・・ｽ・ｽ陌夐ｩ溷調・ｽE・ｽ・ｽE・ｽ繝ｻ蛹ｻ繝ｻ郢昜ｹ昴Ψ郢ｧ・ｽE・ｽ郢ｧ・ｽE・ｽ郢晏現竊醍ｸｺ證ｦ・ｽE・ｽ繝ｻ
            WriteDst("manual.txt", "manual");
            AssetSyncer.SyncConfig(MakeConfig());
            Assert.IsTrue(DstExists("manual.txt"), "manually placed file must not be deleted");
        }

        // #37
        [Test]
        public void Phase2_NonManifestFile_SrcExists_Kept()
        {
            // src 邵ｺ・ｽE・ｽ dst 邵ｺ・ｽE・ｽ陷ｷ謔滄倹郢晁ｼ斐＜郢ｧ・ｽE・ｽ郢晢ｽｫ邵ｺ蠕娯旺邵ｺ・ｽE・ｽ邵ｺ・ｽE・ｽ郢ｧ繧・・ｽ窶･st 邵ｺ蠕鯉ｿｽE郢昜ｹ昴Ψ郢ｧ・ｽE・ｽ郢ｧ・ｽE・ｽ郢昜ｺ･・ｽE・ｽ謔ｶ竊醍ｹｧ闃ｽ・ｽE・ｽ・ｽE・ｽ郢ｧ蟲ｨ竊醍ｸｺ繝ｻ
            WriteSrc("file.txt", "src-content");
            WriteDst("file.txt", "different-manual-content");

            // src 邵ｺ・ｽE・ｽ郢晁ｼ斐＜郢ｧ・ｽE・ｽ郢晢ｽｫ郢ｧ蛛ｵ繝ｵ郢ｧ・ｽE・ｽ郢晢ｽｫ郢ｧ・ｽE・ｽ邵ｺ・ｽE・ｽ鬮ｯ・ｽE・ｽ陞滓じ・ｽE・ｽ邵ｺ・ｽE・ｽ陷ｷ譴ｧ謔・・ｽE繝ｻst 邵ｺ・ｽE・ｽ file.txt 邵ｺ・ｽE・ｽ郢晄ｧｭ繝ｫ郢晁ｼ斐♂郢ｧ・ｽE・ｽ郢晏現竊楢怦・ｽE・ｽ郢ｧ蟲ｨ竊醍ｸｺ繝ｻ・ｽE・ｽ繝ｻ
            var exclude = new FilterCondition
            {
                multipleTypeNames = new List<string> { typeof(TextAsset).AssemblyQualifiedName },
                invert = true
            };
            AssetDatabase.Refresh();
            AssetSyncer.SyncConfig(MakeConfig(new List<FilterCondition> { exclude }));

            // 郢晄ｧｭ繝ｫ郢晁ｼ斐♂郢ｧ・ｽE・ｽ郢昜ｺ･・ｽE・ｽ謔ｶ竊醍ｸｺ・ｽE・ｽ邵ｺ・ｽE・ｽ陷繝ｻ・ｽE・ｽ・ｽE・ｽ邵ｺ遒・・ｽ・ｽE・ｽ・ｽ・ｽE・ｽ邵ｺ・ｽE・ｽ郢ｧ繧・・ｽ・ｽ譎・・ｽ・ｽ
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
                multipleTypeNames = new List<string> { typeof(TextAsset).AssemblyQualifiedName },
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

            // 郢晁ｼ斐≦郢晢ｽｫ郢ｧ・ｽE・ｽ邵ｺ・ｽE・ｽ邵ｺ蜉ｱ縲定惺譴ｧ謔・遶奇ｿｽE郢晄ｧｭ繝ｫ郢晁ｼ斐♂郢ｧ・ｽE・ｽ郢晏現竊馴具ｽｻ鬪ｭ・ｽE・ｽ
            var config = MakeConfig();
            AssetSyncer.SyncConfig(config);
            Assert.IsTrue(DstExists("file.txt"));

            // dst 郢ｧ蜻茨ｿｽE陷崎ｼ斐定棔逕ｻ蟲ｩ
            WriteDst("file.txt", "manually-modified");

            // TextAsset 郢ｧ蟶晏求陞滓じ笘・・ｽ・ｽ荵昴Ψ郢ｧ・ｽE・ｽ郢晢ｽｫ郢ｧ・ｽE・ｽ邵ｺ・ｽE・ｽ陷讎奇ｿｽE隴幢ｿｽE
            // dst 陷繝ｻ・ｽE・ｽ・ｽE・ｽ邵ｺ繝ｻsrc 邵ｺ・ｽE・ｽ騾｡・ｽE・ｽ邵ｺ・ｽE・ｽ郢ｧ繝ｻ遶奇ｿｽE隰・・ｽ・ｽ陌夊棔逕ｻ蟲ｩ邵ｺ・ｽE・ｽ髫穂ｹ晢ｿｽE邵ｺ蠍ｺ・ｽE・ｽ譎・・ｽ・ｽ
            var exclude = new FilterCondition
            {
                multipleTypeNames = new List<string> { typeof(TextAsset).AssemblyQualifiedName },
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
                multipleTypeNames = new List<string> { typeof(TextAsset).AssemblyQualifiedName },
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
                multipleTypeNames = new List<string> { typeof(TextAsset).AssemblyQualifiedName },
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

            Assert.IsFalse(Directory.Exists(Path.Combine(_dstFullPath, "sub", "empty")));
            Assert.IsFalse(SyncDirectoryContains(config, "sub/empty"));

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

        // 隨渉隨渉隨渉 郢晄ｧｭ繝ｫ郢晁ｼ斐♂郢ｧ・ｽE・ｽ郢晢ｿｽE(#42遯ｶ繝ｻ4) 隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉

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

        // 隨渉隨渉隨渉 Integration (#45遯ｶ繝ｻ0) 隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉隨渉

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

        // #47 遯ｶ繝ｻ邵ｲ險途eset陜吩ｹ晢ｽ定惺・ｽE・ｽ郢ｧﾂ邵ｲ髦ｪ繝ｵ郢ｧ・ｽE・ｽ郢晢ｽｫ郢ｧ・ｽE・ｽ + dst 邵ｺ・ｽE・ｽ陋ｻ・ｽE・ｽ陷ｷ諱池eset 遶奇ｿｽEdst 邵ｺ・ｽE・ｽ Preset 闖ｫ譎・・ｽ・ｽ邵ｲ縲較c 邵ｺ・ｽE・ｽ Preset 郢ｧ繧・・ｽ・ｽ騾ｹﾂ
        [Test]
        public void Integration_FilterIncludesType_OtherDstFileKept()
        {
            WriteSrc("src-preset.txt", "src");
            WriteDst("dst-preset.txt", "manual");
            AssetDatabase.Refresh();

            // TextAsset 陜吩ｹ晢ｿｽE邵ｺ・ｽE・ｽ髫ｪ・ｽE・ｽ陷ｿ・ｽE・ｽ
            var include = new FilterCondition { multipleTypeNames = new List<string> { typeof(TextAsset).AssemblyQualifiedName } };
            AssetSyncer.SyncConfig(MakeConfig(new List<FilterCondition> { include }));

            Assert.IsTrue(DstExists("src-preset.txt"), "src file that passes filter must be copied");
            Assert.IsTrue(DstExists("dst-preset.txt"), "manually placed file must not be touched");
        }

        // #48 遯ｶ繝ｻ邵ｲ險途eset陜吩ｹ晢ｽ帝ｫｯ・ｽE・ｽ陞滓じﾂ髦ｪ繝ｵ郢ｧ・ｽE・ｽ郢晢ｽｫ郢ｧ・ｽE・ｽ + dst 邵ｺ・ｽE・ｽ陋ｻ・ｽE・ｽ陷ｷ諱池eset 遶奇ｿｽEdst 邵ｺ・ｽE・ｽ Preset 闖ｫ譎・・ｽ・ｽ
        [Test]
        public void Integration_FilterExcludesType_ManualDstFileKept()
        {
            WriteSrc("src-file.txt", "src");
            WriteDst("manual.txt", "manual");
            AssetDatabase.Refresh();

            // TextAsset 鬮ｯ・ｽE・ｽ陞滂ｿｽE遶奇ｿｽEsrc 邵ｺ・ｽE・ｽ郢晁ｼ斐＜郢ｧ・ｽE・ｽ郢晢ｽｫ邵ｺ・ｽE・ｽ郢ｧ・ｽE・ｽ郢晄鱒繝ｻ邵ｺ霈費ｽ檎ｸｺ・ｽE・ｽ邵ｺ繝ｻ
            var exclude = new FilterCondition
            {
                multipleTypeNames = new List<string> { typeof(TextAsset).AssemblyQualifiedName },
                invert = true
            };
            AssetSyncer.SyncConfig(MakeConfig(new List<FilterCondition> { exclude }));

            Assert.IsFalse(DstExists("src-file.txt"), "filtered-out src file must not be copied");
            Assert.IsTrue(DstExists("manual.txt"), "manually placed dst file must not be deleted");
        }

        [Test]
        public void Integration_FilterIncludeExtension_OnlyMatchingExtensionCopied()
        {
            WriteSrc("a.txt", "A");
            WriteSrc("b.bytes", "B");
            AssetDatabase.Refresh();

            var includeByExtension = new FilterCondition
            {
                targetKind = FilterConditionTargetKind.Extension,
                multipleExtensions = new List<string> { "txt" }
            };

            AssetSyncer.SyncConfig(MakeConfig(new List<FilterCondition> { includeByExtension }));

            Assert.IsTrue(DstExists("a.txt"));
            Assert.IsFalse(DstExists("b.bytes"));
        }

        [Test]
        public void Integration_FilterExcludeExtension_MatchingExtensionExcluded()
        {
            WriteSrc("a.txt", "A");
            WriteSrc("b.bytes", "B");
            AssetDatabase.Refresh();

            var excludeByExtension = new FilterCondition
            {
                targetKind = FilterConditionTargetKind.Extension,
                invert = true,
                multipleExtensions = new List<string> { ".txt" }
            };

            AssetSyncer.SyncConfig(MakeConfig(new List<FilterCondition> { excludeByExtension }));

            Assert.IsFalse(DstExists("a.txt"));
            Assert.IsTrue(DstExists("b.bytes"));
        }

        [Test]
        public void Integration_FilterIncludeAsset_OnlySelectedAssetCopied()
        {
            WriteSrc("a.txt", "A");
            WriteSrc("b.txt", "B");
            AssetDatabase.Refresh();

            string selectedGuid = AssetDatabase.AssetPathToGUID(_srcAssetPath + "/a.txt");
            Assume.That(!string.IsNullOrEmpty(selectedGuid), "source guid must exist");

            var includeSelectedAsset = new FilterCondition
            {
                targetKind = FilterConditionTargetKind.Asset,
                multipleAssetGuids = new List<string> { selectedGuid }
            };

            AssetSyncer.SyncConfig(MakeConfig(new List<FilterCondition> { includeSelectedAsset }));

            Assert.IsTrue(DstExists("a.txt"));
            Assert.IsFalse(DstExists("b.txt"));
        }

        [Test]
        public void Integration_FilterExcludeAsset_SelectedAssetExcluded()
        {
            WriteSrc("a.txt", "A");
            WriteSrc("b.txt", "B");
            AssetDatabase.Refresh();

            string excludedGuid = AssetDatabase.AssetPathToGUID(_srcAssetPath + "/a.txt");
            Assume.That(!string.IsNullOrEmpty(excludedGuid), "source guid must exist");

            var excludeSelectedAsset = new FilterCondition
            {
                targetKind = FilterConditionTargetKind.Asset,
                multipleAssetGuids = new List<string> { excludedGuid },
                invert = true
            };

            AssetSyncer.SyncConfig(MakeConfig(new List<FilterCondition> { excludeSelectedAsset }));

            Assert.IsFalse(DstExists("a.txt"));
            Assert.IsTrue(DstExists("b.txt"));
        }

        [Test]
        public void Integration_FilterAsset_InvalidOutsideSource_NoEffect()
        {
            WriteSrc("a.txt", "A");
            WriteSrc("b.txt", "B");
            WriteDst("manual.txt", "manual");
            AssetDatabase.Refresh();

            string outsideGuid = AssetDatabase.AssetPathToGUID(_dstAssetPath + "/manual.txt");
            Assume.That(!string.IsNullOrEmpty(outsideGuid), "outside guid must exist");

            var includeOutsideAsset = new FilterCondition
            {
                targetKind = FilterConditionTargetKind.Asset,
                multipleAssetGuids = new List<string> { outsideGuid }
            };

            AssetSyncer.SyncConfig(MakeConfig(new List<FilterCondition> { includeOutsideAsset }));

            Assert.IsTrue(DstExists("a.txt"));
            Assert.IsTrue(DstExists("b.txt"));
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
            Assert.AreEqual("v1", ReadDst("file.txt"), "ignored destination file should not update");
            Assert.IsTrue(SyncContains(config, "file.txt"), "synced state should be retained while destination is ignored");

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
            Assert.AreEqual("v1", ReadDst("file.txt"), "ignored destination file should not update");
            Assert.IsTrue(SyncContains(config, "file.txt"), "synced state should be retained while destination is ignored");

            config.enabled = false;
            AssetSyncer.SyncConfig(config);
            Assert.IsTrue(DstExists("file.txt"), "ignored destination file should remain when sync is disabled");
            Assert.IsTrue(SyncContains(config, "file.txt"), "synced state should be retained for ignored files while disabled");

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
            Assert.IsTrue(DstExists("file.txt"), "ignored destination file should remain when disabled");
            Assert.IsTrue(SyncContains(config, "file.txt"), "synced should remain while destination is ignored");

            config.ignoreGuids.Remove(dstGuid);
            bool changed = AssetSyncer.PruneSyncPathsForDisabledConfig(config);

            Assert.IsTrue(changed, "removing destination protection while disabled should drop synced state");
            Assert.IsTrue(DstExists("file.txt"), "pruning ownership must not delete destination file");
            Assert.IsFalse(SyncContains(config, "file.txt"), "destination file should become unsynced");
        }

        [Test]
        public void CollectSyncedDestinationSyncRelativePaths_ExcludesIgnoreDestination()
        {
            var config = MakeConfig();
            WriteSrc("ignore.txt", "p1");
            WriteSrc("normal.txt", "n1");
            AssetSyncer.SyncConfig(config);

            AssetDatabase.Refresh();
            string ignoreGuid = AssetDatabase.AssetPathToGUID(_dstAssetPath + "/ignore.txt");
            Assume.That(!string.IsNullOrEmpty(ignoreGuid), "destination ignored asset guid must exist");
            config.ignoreGuids.Add(ignoreGuid);

            HashSet<string> syncedSync = AssetSyncer.CollectSyncedDestinationSyncRelativePaths(config);
            Assert.IsFalse(syncedSync.Contains("ignore.txt"));
            Assert.IsTrue(syncedSync.Contains("normal.txt"));
        }

        [Test]
        public void IgnoreFolder_DescendantFile_IsNotUpdated()
        {
            var config = MakeConfig();
            WriteSrc("IgnoreSub/file.txt", "v1");
            AssetSyncer.SyncConfig(config);
            Assert.AreEqual("v1", ReadDst("IgnoreSub/file.txt"));

            AssetDatabase.Refresh();
            string ignoreFolderGuid = AssetDatabase.AssetPathToGUID(_dstAssetPath + "/IgnoreSub");
            Assume.That(!string.IsNullOrEmpty(ignoreFolderGuid), "destination ignored folder guid must exist");
            config.ignoreGuids.Add(ignoreFolderGuid);

            WriteSrc("IgnoreSub/file.txt", "v2");
            AssetSyncer.SyncConfig(config);

            Assert.AreEqual("v1", ReadDst("IgnoreSub/file.txt"), "file under ignored folder should not be updated");
        }

        [Test]
        public void IgnoreEntry_OutsideDestination_IsIgnored()
        {
            var config = MakeConfig();
            WriteSrc("file.txt", "v1");
            AssetSyncer.SyncConfig(config);
            Assert.AreEqual("v1", ReadDst("file.txt"));

            AssetDatabase.Refresh();
            string sourceGuid = AssetDatabase.AssetPathToGUID(_srcAssetPath + "/file.txt");
            Assume.That(!string.IsNullOrEmpty(sourceGuid), "source guid must exist");
            config.ignoreGuids.Add(sourceGuid);

            WriteSrc("file.txt", "v2");
            AssetSyncer.SyncConfig(config);

            Assert.AreEqual("v2", ReadDst("file.txt"), "outside-destination ignored entry must be ignored");
        }

        [Test]
        public void ConflictKeep_AddsIgnoreGuid()
        {
            var config = MakeConfig();
            WriteSrc("conflict.txt", "src-v1");
            WriteDst("conflict.txt", "manual-v1");
            AssetDatabase.Refresh();

            AssetSyncer.SyncConfig(config);
            Assert.AreEqual("manual-v1", ReadDst("conflict.txt"), "keep decision should preserve destination content");

            AssetDatabase.Refresh();
            string destinationGuid = AssetDatabase.AssetPathToGUID(_dstAssetPath + "/conflict.txt");
            Assume.That(!string.IsNullOrEmpty(destinationGuid), "destination guid must exist");
            CollectionAssert.Contains(config.ignoreGuids, destinationGuid, "keep decision should register ignored destination asset");
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


