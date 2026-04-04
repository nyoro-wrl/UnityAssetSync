using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Nyorowrl.Assetfork;
using Nyorowrl.Assetfork.Editor;

namespace Nyorowrl.Assetfork.Editor.Tests
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
            _testRoot = "Assets/AssetForkOverlayTest_" + uid;
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
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string fullTestRoot = Path.GetFullPath(Path.Combine(projectRoot, _testRoot));
            FileUtil.DeleteFileOrDirectory(fullTestRoot);
            FileUtil.DeleteFileOrDirectory(fullTestRoot + ".meta");
            AssetDatabase.Refresh();
        }

        [Test]
        public void RebuildSyncedPathCache_DestinationProtectedOwnedFile_DoesNotShowBadge()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string protectedRelativePath = "protected.txt";
            string normalRelativePath = "normal.txt";

            File.WriteAllText(Path.Combine(projectRoot, _dstAssetPath, protectedRelativePath), "protected");
            File.WriteAllText(Path.Combine(projectRoot, _srcAssetPath, normalRelativePath), "normal");
            AssetDatabase.Refresh();

            string protectedDestinationAssetPath = _dstAssetPath + "/" + protectedRelativePath;
            string protectedGuid = AssetDatabase.AssetPathToGUID(protectedDestinationAssetPath);
            Assume.That(!string.IsNullOrEmpty(protectedGuid), "destination protected asset guid must exist");

            var settings = ScriptableObject.CreateInstance<AssetForkSettings>();
            settings.syncConfigs = new List<SyncConfig>
            {
                new SyncConfig
                {
                    configName = "OverlayTest",
                    enabled = true,
                    sourcePath = _srcAssetPath,
                    destinationPath = _dstAssetPath,
                    ownedRelativePaths = new List<string> { protectedRelativePath, normalRelativePath },
                    protectedGuids = new List<string> { protectedGuid }
                }
            };

            string settingsAssetPath = _testRoot + "/AssetForkOverlaySettings.asset";
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
            string protectedFullPath = destinationRoot + "/" + protectedRelativePath;
            string normalFullPath = destinationRoot + "/" + normalRelativePath;

            Assert.IsFalse(syncedPaths.Contains(protectedFullPath), "destination-protected owned path should not show synced badge");
            Assert.IsTrue(syncedPaths.Contains(normalFullPath), "non-protected owned path should still show synced badge");
        }
    }
}
