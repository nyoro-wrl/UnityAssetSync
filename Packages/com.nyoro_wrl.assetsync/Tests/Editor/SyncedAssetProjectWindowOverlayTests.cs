using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Nyorowrl.AssetSync;
using Nyorowrl.AssetSync.Editor;

namespace Nyorowrl.AssetSync.Editor.Tests
{
    public class SyncedAssetProjectWindowOverlayTests
    {
        private string _testRoot;
        private string _srcAssetPath;
        private string _dstAssetPath;

        [SetUp]
        public void SetUp()
        {
            string uid = Guid.NewGuid().ToString("N").Substring(0, 8);
            _testRoot = "Assets/AssetSyncOverlayTest_" + uid;
            _srcAssetPath = _testRoot + "/Src";
            _dstAssetPath = _testRoot + "/Dst";

            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            Directory.CreateDirectory(Path.Combine(projectRoot, _srcAssetPath));
            Directory.CreateDirectory(Path.Combine(projectRoot, _dstAssetPath));
            AssetDatabase.Refresh();
        }

        [TearDown]
        public void TearDown()
        {
            AssetSyncSettingsDeletionProcessor.DisplayDialogOverride = (_, _, _, _) => true;
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string fullTestRoot = Path.GetFullPath(Path.Combine(projectRoot, _testRoot));
            FileUtil.DeleteFileOrDirectory(fullTestRoot);
            FileUtil.DeleteFileOrDirectory(fullTestRoot + ".meta");
            AssetDatabase.Refresh();
            AssetSyncSettingsDeletionProcessor.DisplayDialogOverride = null;
        }

        [Test]
        public void RebuildSyncedPathCache_IgnoreSyncFile_DoesNotShowBadge()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string ignoreRelativePath = "ignore.txt";
            string normalRelativePath = "normal.txt";

            File.WriteAllText(Path.Combine(projectRoot, _dstAssetPath, ignoreRelativePath), "ignore");
            File.WriteAllText(Path.Combine(projectRoot, _srcAssetPath, normalRelativePath), "normal");
            AssetDatabase.Refresh();

            string ignoreDestinationAssetPath = _dstAssetPath + "/" + ignoreRelativePath;
            string ignoreGuid = AssetDatabase.AssetPathToGUID(ignoreDestinationAssetPath);
            Assume.That(!string.IsNullOrEmpty(ignoreGuid), "destination ignored asset guid must exist");

            var settings = ScriptableObject.CreateInstance<AssetSyncSettings>();
            settings.syncConfigs = new List<SyncConfig>
            {
                new SyncConfig
                {
                    configName = "OverlayTest",
                    enabled = true,
                    sourcePath = _srcAssetPath,
                    destinationPath = _dstAssetPath,
                    syncRelativePaths = new List<string> { ignoreRelativePath, normalRelativePath },
                    ignoreGuids = new List<string> { ignoreGuid }
                }
            };

            string settingsAssetPath = _testRoot + "/AssetSyncOverlaySettings.asset";
            AssetDatabase.CreateAsset(settings, settingsAssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            MethodInfo rebuildMethod = typeof(SyncedAssetProjectWindowOverlay)
                .GetMethod("RebuildSyncedPathCache", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(rebuildMethod, "RebuildSyncedPathCache method not found");
            rebuildMethod.Invoke(null, null);

            FieldInfo pathsField = typeof(SyncedAssetProjectWindowOverlay)
                .GetField("SyncedAssetPaths", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(pathsField, "SyncedAssetPaths field not found");

            var syncedPaths = pathsField.GetValue(null) as HashSet<string>;
            Assert.IsNotNull(syncedPaths, "SyncedAssetPaths must be a HashSet<string>");

            string destinationRoot = _dstAssetPath;
            string ignoreFullPath = destinationRoot + "/" + ignoreRelativePath;
            string normalFullPath = destinationRoot + "/" + normalRelativePath;

            Assert.IsFalse(syncedPaths.Contains(ignoreFullPath), "ignored synced path should not show synced badge");
            Assert.IsTrue(syncedPaths.Contains(normalFullPath), "non-ignored synced path should still show synced badge");
        }

        [Test]
        public void RebuildSyncedPathCache_DisabledConfig_DoesNotShowBadge()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string relativePath = "file.txt";

            File.WriteAllText(Path.Combine(projectRoot, _dstAssetPath, relativePath), "dst");
            AssetDatabase.Refresh();

            var settings = ScriptableObject.CreateInstance<AssetSyncSettings>();
            settings.syncConfigs = new List<SyncConfig>
            {
                new SyncConfig
                {
                    configName = "OverlayDisabledTest",
                    enabled = false,
                    sourcePath = _srcAssetPath,
                    destinationPath = _dstAssetPath,
                    syncRelativePaths = new List<string> { relativePath },
                    ignoreGuids = new List<string>()
                }
            };

            string settingsAssetPath = _testRoot + "/AssetSyncOverlayDisabledSettings.asset";
            AssetDatabase.CreateAsset(settings, settingsAssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            MethodInfo rebuildMethod = typeof(SyncedAssetProjectWindowOverlay)
                .GetMethod("RebuildSyncedPathCache", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(rebuildMethod, "RebuildSyncedPathCache method not found");
            rebuildMethod.Invoke(null, null);

            FieldInfo pathsField = typeof(SyncedAssetProjectWindowOverlay)
                .GetField("SyncedAssetPaths", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(pathsField, "SyncedAssetPaths field not found");

            var syncedPaths = pathsField.GetValue(null) as HashSet<string>;
            Assert.IsNotNull(syncedPaths, "SyncedAssetPaths must be a HashSet<string>");

            string fullPath = _dstAssetPath + "/" + relativePath;
            Assert.IsFalse(syncedPaths.Contains(fullPath), "disabled config should not show synced badge");
        }

        [Test]
        public void RebuildSyncedPathCache_SyncedNestedPath_AddsDestinationSubfolderBadges()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string relativePath = "sub/inner/file.txt";

            string destinationFilePath = Path.Combine(projectRoot, _dstAssetPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFilePath));
            File.WriteAllText(destinationFilePath, "dst");
            AssetDatabase.Refresh();

            var settings = ScriptableObject.CreateInstance<AssetSyncSettings>();
            settings.syncConfigs = new List<SyncConfig>
            {
                new SyncConfig
                {
                    configName = "OverlayNestedFolderTest",
                    enabled = true,
                    sourcePath = _srcAssetPath,
                    destinationPath = _dstAssetPath,
                    syncRelativePaths = new List<string> { relativePath },
                    ignoreGuids = new List<string>()
                }
            };

            string settingsAssetPath = _testRoot + "/AssetSyncOverlayNestedSettings.asset";
            AssetDatabase.CreateAsset(settings, settingsAssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            MethodInfo rebuildMethod = typeof(SyncedAssetProjectWindowOverlay)
                .GetMethod("RebuildSyncedPathCache", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(rebuildMethod, "RebuildSyncedPathCache method not found");
            rebuildMethod.Invoke(null, null);

            FieldInfo pathsField = typeof(SyncedAssetProjectWindowOverlay)
                .GetField("SyncedAssetPaths", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(pathsField, "SyncedAssetPaths field not found");

            var syncedPaths = pathsField.GetValue(null) as HashSet<string>;
            Assert.IsNotNull(syncedPaths, "SyncedAssetPaths must be a HashSet<string>");

            string destinationRoot = _dstAssetPath;
            Assert.IsTrue(syncedPaths.Contains(destinationRoot + "/sub"), "managed destination subfolder should show synced badge");
            Assert.IsTrue(syncedPaths.Contains(destinationRoot + "/sub/inner"), "managed nested destination subfolder should show synced badge");
            Assert.IsTrue(syncedPaths.Contains(destinationRoot + "/" + relativePath), "managed destination file should show synced badge");
        }
    }
}


