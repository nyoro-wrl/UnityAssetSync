using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Nyorowrl.AssetSync;
using Nyorowrl.AssetSync.Editor;

namespace Nyorowrl.AssetSync.Editor.Tests
{
    public class AssetSyncSettingsDeletionProcessorTests
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
            _testRoot = "Assets/AssetSyncSettingsDeleteTest_" + uid;
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
            AssetSyncSettingsDeletionProcessor.DisplayDialogOverride = (_, _, _, _) => true;
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string fullTestRoot = Path.GetFullPath(Path.Combine(projectRoot, _testRoot));
            FileUtil.DeleteFileOrDirectory(fullTestRoot);
            FileUtil.DeleteFileOrDirectory(fullTestRoot + ".meta");
            AssetDatabase.Refresh();
            AssetSyncSettingsDeletionProcessor.DisplayDialogOverride = null;
        }

        [Test]
        public void TryCleanupSyncedFilesFromDeletedSettingsAsset_SettingsAssetPath_RemovesSyncedFiles()
        {
            WriteSrc("synced.txt", "src");
            WriteDst("manual.txt", "manual");
            AssetDatabase.Refresh();

            var config = CreateConfig();
            AssetSyncer.SyncConfig(config);
            Assert.IsTrue(DstExists("synced.txt"));
            Assert.IsTrue(DstExists("manual.txt"));

            var settings = ScriptableObject.CreateInstance<AssetSyncSettings>();
            settings.syncConfigs = new List<SyncConfig> { config };
            string settingsAssetPath = _testRoot + "/Settings.asset";
            AssetDatabase.CreateAsset(settings, settingsAssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            bool changed = AssetSyncSettingsDeletionProcessor.TryCleanupSyncedFilesFromDeletedSettingsAsset(settingsAssetPath);

            Assert.IsTrue(changed);
            Assert.IsFalse(DstExists("synced.txt"), "synced file should be removed when settings is deleted");
            Assert.IsTrue(DstExists("manual.txt"), "manual destination file must remain");
        }

        [Test]
        public void TryCleanupSyncedFilesFromDeletedSettingsAsset_RemovesManagedEmptySubdirectory()
        {
            WriteSrc("sub/synced.txt", "src");
            AssetDatabase.Refresh();

            var config = CreateConfig();
            AssetSyncer.SyncConfig(config);
            Assert.IsTrue(DstExists("sub/synced.txt"));

            var settings = ScriptableObject.CreateInstance<AssetSyncSettings>();
            settings.syncConfigs = new List<SyncConfig> { config };
            string settingsAssetPath = _testRoot + "/SettingsWithSubdir.asset";
            AssetDatabase.CreateAsset(settings, settingsAssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            bool changed = AssetSyncSettingsDeletionProcessor.TryCleanupSyncedFilesFromDeletedSettingsAsset(settingsAssetPath);

            Assert.IsTrue(changed);
            Assert.IsFalse(DstExists("sub/synced.txt"), "synced file should be removed when settings is deleted");
            Assert.IsFalse(Directory.Exists(Path.Combine(_dstFullPath, "sub")), "empty managed subdirectory should be removed");
        }

        [Test]
        public void TryCleanupSyncedFilesFromDeletedSettingsAsset_KeepsSubdirectoryWithManualContent()
        {
            WriteSrc("sub/synced.txt", "src");
            AssetDatabase.Refresh();

            var config = CreateConfig();
            AssetSyncer.SyncConfig(config);
            WriteDst("sub/manual.txt", "manual");

            var settings = ScriptableObject.CreateInstance<AssetSyncSettings>();
            settings.syncConfigs = new List<SyncConfig> { config };
            string settingsAssetPath = _testRoot + "/SettingsWithManualSubdir.asset";
            AssetDatabase.CreateAsset(settings, settingsAssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            bool changed = AssetSyncSettingsDeletionProcessor.TryCleanupSyncedFilesFromDeletedSettingsAsset(settingsAssetPath);

            Assert.IsTrue(changed);
            Assert.IsFalse(DstExists("sub/synced.txt"), "synced file should be removed when settings is deleted");
            Assert.IsTrue(DstExists("sub/manual.txt"), "manual destination file must remain");
            Assert.IsTrue(Directory.Exists(Path.Combine(_dstFullPath, "sub")), "subdirectory with manual content must remain");
        }

        [Test]
        public void TryCleanupSyncedFilesFromDeletedSettingsAsset_OnlyEmptySourceSubdirectory_ReturnsFalse()
        {
            Directory.CreateDirectory(Path.Combine(_srcFullPath, "sub", "empty"));
            AssetDatabase.Refresh();

            var config = CreateConfig();
            AssetSyncer.SyncConfig(config);
            Assert.IsFalse(Directory.Exists(Path.Combine(_dstFullPath, "sub", "empty")));

            var settings = ScriptableObject.CreateInstance<AssetSyncSettings>();
            settings.syncConfigs = new List<SyncConfig> { config };
            string settingsAssetPath = _testRoot + "/SettingsWithOnlyEmptyDir.asset";
            AssetDatabase.CreateAsset(settings, settingsAssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            bool changed = AssetSyncSettingsDeletionProcessor.TryCleanupSyncedFilesFromDeletedSettingsAsset(settingsAssetPath);

            Assert.IsFalse(changed);
            Assert.IsFalse(Directory.Exists(Path.Combine(_dstFullPath, "sub", "empty")));
            Assert.IsFalse(Directory.Exists(Path.Combine(_dstFullPath, "sub")));
        }

        [Test]
        public void TryCleanupSyncedFilesFromDeletedSettingsAsset_FolderPath_RemovesSyncedFiles()
        {
            WriteSrc("folder_synced.txt", "src");
            AssetDatabase.Refresh();

            var config = CreateConfig();
            AssetSyncer.SyncConfig(config);
            Assert.IsTrue(DstExists("folder_synced.txt"));

            string settingsFolderAssetPath = _testRoot + "/SettingsFolder";
            string settingsFolderFullPath = Path.Combine(Path.GetDirectoryName(Application.dataPath), settingsFolderAssetPath);
            Directory.CreateDirectory(settingsFolderFullPath);

            var settings = ScriptableObject.CreateInstance<AssetSyncSettings>();
            settings.syncConfigs = new List<SyncConfig> { config };
            string settingsAssetPath = settingsFolderAssetPath + "/Settings.asset";
            AssetDatabase.CreateAsset(settings, settingsAssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            bool changed = AssetSyncSettingsDeletionProcessor.TryCleanupSyncedFilesFromDeletedSettingsAsset(settingsFolderAssetPath);

            Assert.IsTrue(changed);
            Assert.IsFalse(DstExists("folder_synced.txt"), "synced file should be removed when settings folder is deleted");
        }

        [Test]
        public void HandleWillDeleteAsset_WhenWarningCanceled_CancelsDeleteAndKeepsSyncedFiles()
        {
            WriteSrc("cancel_synced.txt", "src");
            AssetDatabase.Refresh();

            var config = CreateConfig();
            AssetSyncer.SyncConfig(config);
            Assert.IsTrue(DstExists("cancel_synced.txt"));

            var settings = ScriptableObject.CreateInstance<AssetSyncSettings>();
            settings.syncConfigs = new List<SyncConfig> { config };
            string settingsAssetPath = _testRoot + "/CancelSettings.asset";
            AssetDatabase.CreateAsset(settings, settingsAssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            string warningMessage = null;
            AssetSyncSettingsDeletionProcessor.DisplayDialogOverride = (_, message, _, _) =>
            {
                warningMessage = message;
                return false;
            };

            AssetDeleteResult result = AssetSyncSettingsDeletionProcessor.HandleWillDeleteAsset(settingsAssetPath);

            Assert.AreEqual(AssetDeleteResult.FailedDelete, result, "settings deletion should be canceled when warning is rejected");
            StringAssert.Contains("1", warningMessage, "settings delete warning should include file count");
            Assert.IsTrue(DstExists("cancel_synced.txt"), "synced destination file must remain when delete is canceled");
        }

        [Test]
        public void HandleWillDeleteAsset_WhenWarningAccepted_RemovesSyncedFiles()
        {
            WriteSrc("confirm_synced.txt", "src");
            AssetDatabase.Refresh();

            var config = CreateConfig();
            AssetSyncer.SyncConfig(config);
            Assert.IsTrue(DstExists("confirm_synced.txt"));

            var settings = ScriptableObject.CreateInstance<AssetSyncSettings>();
            settings.syncConfigs = new List<SyncConfig> { config };
            string settingsAssetPath = _testRoot + "/ConfirmSettings.asset";
            AssetDatabase.CreateAsset(settings, settingsAssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            AssetSyncSettingsDeletionProcessor.DisplayDialogOverride = (_, _, _, _) => true;

            AssetDeleteResult result = AssetSyncSettingsDeletionProcessor.HandleWillDeleteAsset(settingsAssetPath);

            Assert.AreEqual(AssetDeleteResult.DidNotDelete, result);
            Assert.IsFalse(DstExists("confirm_synced.txt"), "synced destination file should be removed when delete is accepted");
        }

        private SyncConfig CreateConfig()
        {
            return new SyncConfig
            {
                configName = "DeleteSettingsTestConfig",
                enabled = true,
                includeSubdirectories = true,
                sourcePath = _srcAssetPath,
                destinationPath = _dstAssetPath,
                filters = new List<FilterCondition>()
            };
        }

        private void WriteSrc(string relPath, string content)
        {
            string fullPath = Path.Combine(_srcFullPath, relPath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(fullPath, content);
        }

        private void WriteDst(string relPath, string content)
        {
            string fullPath = Path.Combine(_dstFullPath, relPath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(fullPath, content);
        }

        private bool DstExists(string relPath)
        {
            string fullPath = Path.Combine(_dstFullPath, relPath.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(fullPath);
        }
    }
}
