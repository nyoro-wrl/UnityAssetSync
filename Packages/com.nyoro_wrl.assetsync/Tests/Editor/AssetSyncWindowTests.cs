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
        private const string SettingsPathPrefKey = "AssetSync.SettingsPath";
        private const string SelectedConfigIndexPrefKeyPrefix = "AssetSync.SelectedConfigIndex.";

        private string _testRoot;
        private string _srcAssetPath;
        private string _dstAssetPath;
        private string _srcFullPath;
        private string _dstFullPath;
        private bool _hadOriginalSettingsPathPref;
        private string _originalSettingsPathPrefValue;

        [SetUp]
        public void SetUp()
        {
            _hadOriginalSettingsPathPref = EditorPrefs.HasKey(SettingsPathPrefKey);
            _originalSettingsPathPrefValue = _hadOriginalSettingsPathPref
                ? EditorPrefs.GetString(SettingsPathPrefKey, string.Empty)
                : string.Empty;

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
            AssetSyncSettingsDeletionProcessor.DisplayDialogOverride = (_, _, _, _) => true;
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string fullTestRoot = Path.GetFullPath(Path.Combine(projectRoot, _testRoot));
            FileUtil.DeleteFileOrDirectory(fullTestRoot);
            FileUtil.DeleteFileOrDirectory(fullTestRoot + ".meta");
            AssetDatabase.Refresh();
            if (_hadOriginalSettingsPathPref)
                EditorPrefs.SetString(SettingsPathPrefKey, _originalSettingsPathPrefValue ?? string.Empty);
            else
                EditorPrefs.DeleteKey(SettingsPathPrefKey);
            AssetSyncWindow.DisplayDialogOverride = null;
            AssetSyncSettingsDeletionProcessor.DisplayDialogOverride = null;
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
            Assert.IsTrue(File.Exists(dstFilePath), "ignored destination file should remain after disabling");

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

        [Test]
        public void DeleteConfig_RemovesSyncedDestinationFiles()
        {
            const string relativePath = "file.txt";
            File.WriteAllText(Path.Combine(_srcFullPath, relativePath), "v1");
            AssetDatabase.Refresh();

            var config = new SyncConfig
            {
                configName = "DeleteConfigTest",
                enabled = true,
                includeSubdirectories = true,
                sourcePath = _srcAssetPath,
                destinationPath = _dstAssetPath,
                filters = new List<FilterCondition>()
            };
            AssetSyncer.SyncConfig(config);

            string dstFilePath = Path.Combine(_dstFullPath, relativePath);
            Assert.IsTrue(File.Exists(dstFilePath), "destination file should exist after sync");
            Assert.IsTrue(config.syncRelativePaths.Contains(relativePath), "synced state should track the file");

            string settingsAssetPath = CreateSettingsAssetPath("DeleteConfigSettings.asset", new List<SyncConfig> { config });

            var window = CreateAndEnableWindow(settingsAssetPath);
            try
            {
                string confirmationMessage = null;
                AssetSyncWindow.DisplayDialogOverride = (_, message, _, _) =>
                {
                    confirmationMessage = message;
                    return true;
                };

                MethodInfo deleteConfigMethod = typeof(AssetSyncWindow).GetMethod("DeleteConfig", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.IsNotNull(deleteConfigMethod, "DeleteConfig method not found");
                deleteConfigMethod.Invoke(window, new object[] { 0 });

                StringAssert.Contains("1", confirmationMessage, "delete warning should include file count");
                Assert.IsFalse(File.Exists(dstFilePath), "deleting config should remove synced destination files");

                var settings = AssetDatabase.LoadAssetAtPath<AssetSyncSettings>(settingsAssetPath);
                Assert.IsNotNull(settings);
                Assert.AreEqual(0, settings.syncConfigs.Count, "config should be removed from settings");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void DeleteConfig_WhenWarningCanceled_DoesNotDeleteConfigOrSyncedDestinationFiles()
        {
            const string relativePath = "cancel_file.txt";
            File.WriteAllText(Path.Combine(_srcFullPath, relativePath), "v1");
            AssetDatabase.Refresh();

            var config = new SyncConfig
            {
                configName = "DeleteConfigCancelTest",
                enabled = true,
                includeSubdirectories = true,
                sourcePath = _srcAssetPath,
                destinationPath = _dstAssetPath,
                filters = new List<FilterCondition>()
            };
            AssetSyncer.SyncConfig(config);
            string dstFilePath = Path.Combine(_dstFullPath, relativePath);
            Assert.IsTrue(File.Exists(dstFilePath));

            string settingsAssetPath = CreateSettingsAssetPath("DeleteConfigCancelSettings.asset", new List<SyncConfig> { config });

            var window = CreateAndEnableWindow(settingsAssetPath);
            try
            {
                string confirmationMessage = null;
                AssetSyncWindow.DisplayDialogOverride = (_, message, _, _) =>
                {
                    confirmationMessage = message;
                    return false;
                };

                MethodInfo deleteConfigMethod = typeof(AssetSyncWindow).GetMethod("DeleteConfig", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.IsNotNull(deleteConfigMethod, "DeleteConfig method not found");
                deleteConfigMethod.Invoke(window, new object[] { 0 });

                StringAssert.Contains("1", confirmationMessage, "delete warning should include file count");
                Assert.IsTrue(File.Exists(dstFilePath), "synced destination file must remain when delete is canceled");

                var settings = AssetDatabase.LoadAssetAtPath<AssetSyncSettings>(settingsAssetPath);
                Assert.IsNotNull(settings);
                Assert.AreEqual(1, settings.syncConfigs.Count, "config should remain when delete is canceled");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void OnEnable_SelectsFirstConfigAndRestoresRememberedSelection()
        {
            string settingsAssetPath = CreateSettingsAssetPath("SelectionSettings.asset", new List<SyncConfig>
            {
                new SyncConfig { configName = "Config 1" },
                new SyncConfig { configName = "Config 2" }
            });

            string selectionKey = SelectedConfigIndexPrefKeyPrefix + settingsAssetPath;
            EditorPrefs.DeleteKey(selectionKey);

            var firstWindow = CreateAndEnableWindow(settingsAssetPath);
            try
            {
                Assert.AreEqual(0, GetSelectedConfigIndex(firstWindow), "first config should be auto-selected when no previous selection exists");

                MethodInfo saveSelectedConfigIndexMethod = typeof(AssetSyncWindow).GetMethod("SaveSelectedConfigIndex", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.IsNotNull(saveSelectedConfigIndexMethod, "SaveSelectedConfigIndex method not found");
                saveSelectedConfigIndexMethod.Invoke(firstWindow, new object[] { 1 });
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(firstWindow);
            }

            var secondWindow = CreateAndEnableWindow(settingsAssetPath);
            try
            {
                Assert.AreEqual(1, GetSelectedConfigIndex(secondWindow), "remembered config selection should be restored");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(secondWindow);
                EditorPrefs.DeleteKey(selectionKey);
            }
        }

        [Test]
        public void AddConfig_DefaultsToDisabledAndNotActivated()
        {
            string settingsAssetPath = CreateSettingsAssetPath("AddConfigDefaults.asset", new List<SyncConfig>());
            var window = CreateAndEnableWindow(settingsAssetPath);
            try
            {
                MethodInfo addConfigMethod = typeof(AssetSyncWindow).GetMethod("AddConfig", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.IsNotNull(addConfigMethod, "AddConfig method not found");
                addConfigMethod.Invoke(window, null);

                var settings = AssetDatabase.LoadAssetAtPath<AssetSyncSettings>(settingsAssetPath);
                Assert.IsNotNull(settings);
                Assert.AreEqual(1, settings.syncConfigs.Count);
                Assert.IsFalse(settings.syncConfigs[0].enabled, "new config should default to disabled");
                Assert.IsFalse(settings.syncConfigs[0].isSyncActivated, "new config should require explicit sync activation");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void ActivateConfigWithSyncButton_ValidPaths_EnablesAndActivatesConfig()
        {
            var config = new SyncConfig
            {
                configName = "ActivationTest",
                enabled = false,
                isSyncActivated = false,
                includeSubdirectories = true,
                sourcePath = _srcAssetPath,
                destinationPath = _dstAssetPath,
                filters = new List<FilterCondition>()
            };
            var settings = ScriptableObject.CreateInstance<AssetSyncSettings>();
            settings.syncConfigs = new List<SyncConfig> { config };

            var window = ScriptableObject.CreateInstance<AssetSyncWindow>();
            try
            {
                FieldInfo settingsField = typeof(AssetSyncWindow).GetField("_settings", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.IsNotNull(settingsField, "_settings field not found");
                settingsField.SetValue(window, settings);

                MethodInfo canActivateMethod = typeof(AssetSyncWindow).GetMethod("CanActivateWithSyncButton", BindingFlags.NonPublic | BindingFlags.Static);
                Assert.IsNotNull(canActivateMethod, "CanActivateWithSyncButton method not found");
                bool canActivate = (bool)canActivateMethod.Invoke(null, new object[] { config });
                Assert.IsTrue(canActivate, "valid Source/Destination should enable Sync button interaction");

                MethodInfo activateMethod = typeof(AssetSyncWindow).GetMethod("ActivateConfigWithSyncButton", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.IsNotNull(activateMethod, "ActivateConfigWithSyncButton method not found");
                activateMethod.Invoke(window, new object[] { config });

                Assert.IsTrue(config.enabled, "sync activation should enable config");
                Assert.IsTrue(config.isSyncActivated, "sync activation should switch UI to Enable toggle mode");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
                UnityEngine.Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void IsSourceAndDestinationReadOnly_AfterSyncActivation_ReadOnlyOnlyWhenEnabled()
        {
            MethodInfo readOnlyMethod = typeof(AssetSyncWindow).GetMethod("IsSourceAndDestinationReadOnly", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(readOnlyMethod, "IsSourceAndDestinationReadOnly method not found");

            var activatedEnabled = new SyncConfig { isSyncActivated = true, enabled = true };
            var activatedDisabled = new SyncConfig { isSyncActivated = true, enabled = false };
            var notActivatedEnabled = new SyncConfig { isSyncActivated = false, enabled = true };

            Assert.IsTrue((bool)readOnlyMethod.Invoke(null, new object[] { activatedEnabled }));
            Assert.IsFalse((bool)readOnlyMethod.Invoke(null, new object[] { activatedDisabled }));
            Assert.IsFalse((bool)readOnlyMethod.Invoke(null, new object[] { notActivatedEnabled }));
        }

        [Test]
        public void IsFolderSelectionValid_EmptyOrInvalidPath_ReturnsFalse()
        {
            MethodInfo folderValidMethod = typeof(AssetSyncWindow).GetMethod("IsFolderSelectionValid", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(folderValidMethod, "IsFolderSelectionValid method not found");

            string externalRoot = Path.Combine(Path.GetTempPath(), "AssetSyncWindowExternal_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(externalRoot);
            try
            {
                Assert.IsTrue((bool)folderValidMethod.Invoke(null, new object[] { _srcAssetPath }), "existing folder path should be valid");
                Assert.IsTrue((bool)folderValidMethod.Invoke(null, new object[] { externalRoot }), "existing external directory should be valid");
                Assert.IsFalse((bool)folderValidMethod.Invoke(null, new object[] { "" }), "empty path should be invalid");
                Assert.IsFalse((bool)folderValidMethod.Invoke(null, new object[] { _testRoot + "/NoSuchFolder" }), "missing folder path should be invalid");
            }
            finally
            {
                if (Directory.Exists(externalRoot))
                    Directory.Delete(externalRoot, true);
            }
        }

        [Test]
        public void CanActivateWithSyncButton_ExternalSourceAndProjectDestination_ReturnsTrue()
        {
            string externalRoot = Path.Combine(Path.GetTempPath(), "AssetSyncWindowExternal_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(externalRoot);
            try
            {
                MethodInfo canActivateMethod = typeof(AssetSyncWindow).GetMethod("CanActivateWithSyncButton", BindingFlags.NonPublic | BindingFlags.Static);
                Assert.IsNotNull(canActivateMethod, "CanActivateWithSyncButton method not found");

                var config = new SyncConfig
                {
                    isSyncActivated = false,
                    enabled = false,
                    sourcePath = externalRoot,
                    destinationPath = _dstAssetPath
                };

                bool canActivate = (bool)canActivateMethod.Invoke(null, new object[] { config });
                Assert.IsTrue(canActivate);
            }
            finally
            {
                if (Directory.Exists(externalRoot))
                    Directory.Delete(externalRoot, true);
            }
        }

        [Test]
        public void SwitchSourceMode_EmptySourcePath_CanStayExternal()
        {
            var window = ScriptableObject.CreateInstance<AssetSyncWindow>();
            try
            {
                var config = new SyncConfig
                {
                    sourcePath = string.Empty,
                    destinationPath = _dstAssetPath
                };

                Type sourceInputModeType = typeof(AssetSyncWindow).GetNestedType("SourceInputMode", BindingFlags.NonPublic);
                Assert.IsNotNull(sourceInputModeType, "SourceInputMode enum not found");
                object externalMode = Enum.Parse(sourceInputModeType, "External");

                MethodInfo switchSourceModeMethod = typeof(AssetSyncWindow).GetMethod("SwitchSourceMode", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.IsNotNull(switchSourceModeMethod, "SwitchSourceMode method not found");
                switchSourceModeMethod.Invoke(window, new[] { config, externalMode });

                MethodInfo getSourceInputModeMethod = typeof(AssetSyncWindow).GetMethod("GetSourceInputMode", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.IsNotNull(getSourceInputModeMethod, "GetSourceInputMode method not found");
                object currentMode = getSourceInputModeMethod.Invoke(window, new object[] { config });

                Assert.AreEqual("External", currentMode?.ToString(), "empty source path should keep External mode after toggle");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void CanInteractWithEnableToggle_DisabledConfig_RequiresSyncActivationCondition()
        {
            MethodInfo canInteractMethod = typeof(AssetSyncWindow).GetMethod("CanInteractWithEnableToggle", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(canInteractMethod, "CanInteractWithEnableToggle method not found");

            var disabledInvalid = new SyncConfig
            {
                isSyncActivated = true,
                enabled = false,
                sourcePath = string.Empty,
                destinationPath = string.Empty
            };
            var disabledValid = new SyncConfig
            {
                isSyncActivated = true,
                enabled = false,
                sourcePath = _srcAssetPath,
                destinationPath = _dstAssetPath
            };
            var enabledInvalid = new SyncConfig
            {
                isSyncActivated = true,
                enabled = true,
                sourcePath = string.Empty,
                destinationPath = string.Empty
            };

            Assert.IsFalse((bool)canInteractMethod.Invoke(null, new object[] { disabledInvalid }), "disabled config with invalid setup must not be enable-toggle interactive");
            Assert.IsTrue((bool)canInteractMethod.Invoke(null, new object[] { disabledValid }), "disabled config with valid setup should be enable-toggle interactive");
            Assert.IsTrue((bool)canInteractMethod.Invoke(null, new object[] { enabledInvalid }), "enabled config should allow toggling off regardless of setup");
        }

        [Test]
        public void IsFilterAssetGuidValid_OutsideSource_ReturnsFalse()
        {
            string sourceFilePath = _srcAssetPath + "/in-source.txt";
            string destinationFilePath = _dstAssetPath + "/in-destination.txt";
            File.WriteAllText(Path.Combine(_srcFullPath, "in-source.txt"), "source");
            File.WriteAllText(Path.Combine(_dstFullPath, "in-destination.txt"), "destination");
            AssetDatabase.Refresh();

            string sourceGuid = AssetDatabase.AssetPathToGUID(sourceFilePath);
            string destinationGuid = AssetDatabase.AssetPathToGUID(destinationFilePath);
            Assume.That(!string.IsNullOrEmpty(sourceGuid), "source guid must exist");
            Assume.That(!string.IsNullOrEmpty(destinationGuid), "destination guid must exist");

            MethodInfo isFilterAssetGuidValidMethod = typeof(AssetSyncWindow).GetMethod("IsFilterAssetGuidValid", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(isFilterAssetGuidValidMethod, "IsFilterAssetGuidValid method not found");

            var config = new SyncConfig
            {
                sourcePath = _srcAssetPath,
                destinationPath = _dstAssetPath
            };

            Assert.IsTrue((bool)isFilterAssetGuidValidMethod.Invoke(null, new object[] { config, sourceGuid }));
            Assert.IsFalse((bool)isFilterAssetGuidValidMethod.Invoke(null, new object[] { config, destinationGuid }));
        }

        [Test]
        public void IsIgnoreGuidValid_OutsideDestination_ReturnsFalse()
        {
            string sourceFilePath = _srcAssetPath + "/in-source.txt";
            string destinationFilePath = _dstAssetPath + "/in-destination.txt";
            File.WriteAllText(Path.Combine(_srcFullPath, "in-source.txt"), "source");
            File.WriteAllText(Path.Combine(_dstFullPath, "in-destination.txt"), "destination");
            AssetDatabase.Refresh();

            string sourceGuid = AssetDatabase.AssetPathToGUID(sourceFilePath);
            string destinationGuid = AssetDatabase.AssetPathToGUID(destinationFilePath);
            Assume.That(!string.IsNullOrEmpty(sourceGuid), "source guid must exist");
            Assume.That(!string.IsNullOrEmpty(destinationGuid), "destination guid must exist");

            MethodInfo isIgnoreGuidValidMethod = typeof(AssetSyncWindow).GetMethod("IsIgnoreGuidValid", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(isIgnoreGuidValidMethod, "IsIgnoreGuidValid method not found");

            var config = new SyncConfig
            {
                sourcePath = _srcAssetPath,
                destinationPath = _dstAssetPath
            };

            Assert.IsTrue((bool)isIgnoreGuidValidMethod.Invoke(null, new object[] { config, destinationGuid }));
            Assert.IsFalse((bool)isIgnoreGuidValidMethod.Invoke(null, new object[] { config, sourceGuid }));
        }

        [Test]
        public void GetPreviewIcon_ExternalCsPath_UsesScriptIcon()
        {
            MethodInfo getPreviewIconMethod = typeof(AssetSyncWindow).GetMethod("GetPreviewIcon", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(getPreviewIconMethod, "GetPreviewIcon method not found");

            string externalScriptPath = Path.Combine(Path.GetTempPath(), "AssetSyncWindowPreview_" + Guid.NewGuid().ToString("N") + ".cs");
            var icon = (Texture)getPreviewIconMethod.Invoke(null, new object[] { externalScriptPath, string.Empty });

            Texture expectedScriptIcon = EditorGUIUtility.IconContent("cs Script Icon")?.image
                ?? EditorGUIUtility.IconContent("Script Icon")?.image
                ?? EditorGUIUtility.IconContent("TextAsset Icon")?.image;

            Assert.IsNotNull(icon, "preview icon should not be null for .cs path");
            if (expectedScriptIcon != null)
                Assert.AreEqual(expectedScriptIcon, icon, ".cs preview should prefer script icon");
        }

        [Test]
        public void GetPreviewIcon_ExternalDirectoryPath_UsesFolderIcon()
        {
            MethodInfo getPreviewIconMethod = typeof(AssetSyncWindow).GetMethod("GetPreviewIcon", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(getPreviewIconMethod, "GetPreviewIcon method not found");

            string externalDirectory = Path.Combine(Path.GetTempPath(), "AssetSyncWindowPreviewDir_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(externalDirectory);
            try
            {
                var icon = (Texture)getPreviewIconMethod.Invoke(null, new object[] { externalDirectory, string.Empty });

                Texture expectedFolderIcon = EditorGUIUtility.IconContent("Folder Icon")?.image
                    ?? EditorGUIUtility.IconContent("FolderEmpty Icon")?.image
                    ?? EditorGUIUtility.IconContent("DefaultAsset Icon")?.image;

                Assert.IsNotNull(icon, "preview icon should not be null for directory path");
                if (expectedFolderIcon != null)
                    Assert.AreEqual(expectedFolderIcon, icon, "directory preview should use folder icon");
            }
            finally
            {
                if (Directory.Exists(externalDirectory))
                    Directory.Delete(externalDirectory, true);
            }
        }

        private string CreateSettingsAssetPath(string assetName, List<SyncConfig> configs)
        {
            var settings = ScriptableObject.CreateInstance<AssetSyncSettings>();
            settings.syncConfigs = configs;
            string settingsAssetPath = _testRoot + "/" + assetName;
            AssetDatabase.CreateAsset(settings, settingsAssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return settingsAssetPath;
        }

        private static AssetSyncWindow CreateAndEnableWindow(string settingsAssetPath)
        {
            EditorPrefs.SetString(SettingsPathPrefKey, settingsAssetPath);

            var window = ScriptableObject.CreateInstance<AssetSyncWindow>();
            MethodInfo onEnableMethod = typeof(AssetSyncWindow).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(onEnableMethod, "OnEnable method not found");
            onEnableMethod.Invoke(window, null);
            return window;
        }

        private static int GetSelectedConfigIndex(AssetSyncWindow window)
        {
            PropertyInfo selectedConfigIndexProperty = typeof(AssetSyncWindow).GetProperty("SelectedConfigIndex", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(selectedConfigIndexProperty, "SelectedConfigIndex property not found");
            return (int)selectedConfigIndexProperty.GetValue(window);
        }
    }
}
