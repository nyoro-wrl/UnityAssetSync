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
    public class AssetSyncWindowTests
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
            _testRoot = "Assets/AssetSyncWindowTest_" + uid;
            _srcAssetPath = _testRoot + "/Src";
            _dstAssetPath = _testRoot + "/Dst";

            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            _srcFullPath = Path.GetFullPath(Path.Combine(projectRoot, _srcAssetPath));
            _dstFullPath = Path.GetFullPath(Path.Combine(projectRoot, _dstAssetPath));

            Directory.CreateDirectory(_srcFullPath);
            Directory.CreateDirectory(_dstFullPath);
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
        public void ApplyConfigChange_DisabledConfig_RemoveDestinationProtection_DoesNotDeleteFile_AndEnableTriggersConflict()
        {
            string relativePath = "file.txt";
            File.WriteAllText(Path.Combine(_srcFullPath, relativePath), "v1");
            AssetDatabase.Refresh();

            var config = new SyncConfig
            {
                configName = "WindowTestConfig",
                enabled = true,
                includeSubdirectories = true,
                sourcePath = _srcAssetPath,
                destinationPath = _dstAssetPath,
                filters = new List<FilterCondition>()
            };

            AssetSyncer.SyncConfig(config);
            string dstFilePath = Path.Combine(_dstFullPath, relativePath);
            Assert.IsTrue(File.Exists(dstFilePath), "file should exist after initial sync");
            Assert.Contains("file.txt", config.syncRelativePaths);

            string dstGuid = AssetDatabase.AssetPathToGUID(_dstAssetPath + "/" + relativePath);
            Assume.That(!string.IsNullOrEmpty(dstGuid), "destination guid must exist");
            config.ignoreGuids.Add(dstGuid);

            config.enabled = false;
            AssetSyncer.SyncConfig(config);
            Assert.IsTrue(File.Exists(dstFilePath), "destination ignore file should remain after disabling");

            config.ignoreGuids.Remove(dstGuid);
            Assert.IsTrue(config.syncRelativePaths.Contains("file.txt"), "synced path should still exist before apply");

            var settings = ScriptableObject.CreateInstance<AssetSyncSettings>();
            settings.syncConfigs = new List<SyncConfig> { config };

            var window = ScriptableObject.CreateInstance<AssetSyncWindow>();
            var settingsField = typeof(AssetSyncWindow).GetField("_settings", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(settingsField, "_settings field not found");
            settingsField.SetValue(window, settings);

            MethodInfo applyConfigChangeMethod = typeof(AssetSyncWindow).GetMethod("ApplyConfigChange", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(applyConfigChangeMethod, "ApplyConfigChange method not found");
            applyConfigChangeMethod.Invoke(window, new object[] { config });

            Assert.IsTrue(File.Exists(dstFilePath), "disabled config change must not delete destination file");
            Assert.IsFalse(config.syncRelativePaths.Contains("file.txt"), "removing protection while disabled should drop ownership");

            int conflictDialogCalls = 0;
            AssetSyncer.ConflictResolverOverride = (SyncConfig _, IReadOnlyList<AssetSyncer.SyncConflict> conflicts, out Dictionary<string, AssetSyncer.ConflictResolution> decisions) =>
            {
                conflictDialogCalls++;
                decisions = new Dictionary<string, AssetSyncer.ConflictResolution>();
                foreach (var conflict in conflicts)
                    decisions[conflict.NormalizedRelativePath] = AssetSyncer.ConflictResolution.Ignore;
                return true;
            };

            try
            {
                config.enabled = true;
                MethodInfo applyEnableStateChangeMethod = typeof(AssetSyncWindow).GetMethod("ApplyEnableStateChange", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.IsNotNull(applyEnableStateChangeMethod, "ApplyEnableStateChange method not found");
                applyEnableStateChangeMethod.Invoke(window, new object[] { config });
                MethodInfo flushDeferredSyncActionsMethod = typeof(AssetSyncWindow).GetMethod("FlushDeferredSyncActions", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.IsNotNull(flushDeferredSyncActionsMethod, "FlushDeferredSyncActions method not found");
                flushDeferredSyncActionsMethod.Invoke(window, null);

                Assert.Greater(conflictDialogCalls, 0, "enabling after unprotecting while disabled should trigger conflict");
                Assert.IsTrue(File.Exists(dstFilePath), "conflict resolution should not delete destination file");
            }
            finally
            {
                AssetSyncer.ConflictResolverOverride = null;
            }

            UnityEngine.Object.DestroyImmediate(window);
            UnityEngine.Object.DestroyImmediate(settings);
        }
    }
}
