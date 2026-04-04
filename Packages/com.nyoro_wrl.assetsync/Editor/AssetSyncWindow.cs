using System.Collections.Generic;
using System;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditorInternal;
using UnityEngine;
using Nyorowrl.AssetSync;

namespace Nyorowrl.AssetSync.Editor
{
    public class AssetSyncWindow : EditorWindow
    {
        private const string SettingsPathPrefKey = "AssetSync.SettingsPath";
        private const string SelectedConfigIndexPrefKeyPrefix = "AssetSync.SelectedConfigIndex.";
        internal static Func<string, string, string, string, bool> DisplayDialogOverride;

        private AssetSyncSettings _settings;
        private Vector2 _detailScrollPosition;
        private float _listPanelWidth = 150f;
        private bool _isResizing;
        private readonly Queue<Action> _deferredSyncActions = new Queue<Action>();
        private bool _deferredSyncScheduled;

        [SerializeField] private TreeViewState<int> _treeViewState;
        private ConfigTreeView _configTreeView;

        private readonly Dictionary<FilterCondition, ReorderableList> _typesLists =
            new Dictionary<FilterCondition, ReorderableList>();
        private readonly Dictionary<SyncConfig, ReorderableList> _ignoreLists =
            new Dictionary<SyncConfig, ReorderableList>();

        private int SelectedConfigIndex => _configTreeView?.SelectedIndex ?? -1;

        [MenuItem("Window/AssetSync")]
        public static void Open()
        {
            GetWindow<AssetSyncWindow>("AssetSync");
        }

        private void OnEnable()
        {
            minSize = new Vector2(480, 320);

            if (_treeViewState == null)
                _treeViewState = new TreeViewState<int>();

            string savedPath = EditorPrefs.GetString(SettingsPathPrefKey, "");
            if (!string.IsNullOrEmpty(savedPath))
                _settings = AssetDatabase.LoadAssetAtPath<AssetSyncSettings>(savedPath);

            RebuildTreeView();
            RestoreOrInitializeConfigSelection();
        }

        private void RebuildTreeView()
        {
            _configTreeView = new ConfigTreeView(
                _treeViewState,
                _settings,
                onRenamed: (config, newName) =>
                {
                    Undo.RecordObject(_settings, "Rename Config");
                    config.configName = newName;
                    EditorUtility.SetDirty(_settings);
                },
                onDeleted: DeleteConfig,
                onAddRequested: AddConfig,
                onSelectionChanged: SaveSelectedConfigIndex
            );
        }

        private void OnGUI()
        {
            DrawSettingsField();

            if (_settings == null)
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox("Assign settings or create a new settings file with the New button.", MessageType.Info);
                return;
            }

            if (_settings.syncConfigs == null)
            {
                _settings.syncConfigs = new List<SyncConfig>();
                EditorUtility.SetDirty(_settings);
            }

            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawConfigList();
                DrawResizeHandle();
                DrawConfigDetail();
            }
        }

        private void DrawSettingsField()
        {
            using (new EditorGUILayout.HorizontalScope())
            {

                EditorGUI.BeginChangeCheck();
                _settings = (AssetSyncSettings)EditorGUILayout.ObjectField("Settings", _settings, typeof(AssetSyncSettings), false);
                if (EditorGUI.EndChangeCheck())
                {
                    _typesLists.Clear();
                    _ignoreLists.Clear();
                    string path = _settings != null ? AssetDatabase.GetAssetPath(_settings) : "";
                    EditorPrefs.SetString(SettingsPathPrefKey, path);
                    RebuildTreeView();
                    RestoreOrInitializeConfigSelection();
                }

                if (GUILayout.Button("New", GUILayout.Width(50)))
                {
                    string path = EditorUtility.SaveFilePanelInProject(
                        "Create AssetSync Settings",
                        "AssetSyncSettings",
                        "asset",
                        "Create a new AssetSync settings file");

                    if (!string.IsNullOrEmpty(path))
                    {
                        var newSettings = CreateInstance<AssetSyncSettings>();
                        AssetDatabase.CreateAsset(newSettings, path);
                        AssetDatabase.SaveAssets();
                        _settings = newSettings;
                        _typesLists.Clear();
                        _ignoreLists.Clear();
                        EditorPrefs.SetString(SettingsPathPrefKey, path);
                        RebuildTreeView();
                        RestoreOrInitializeConfigSelection();
                    }
                }
            }
        }

        private void DrawConfigList()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(_listPanelWidth)))
            {

                Rect treeRect = GUILayoutUtility.GetRect(
                    _listPanelWidth, _listPanelWidth,
                    0, float.MaxValue,
                    GUILayout.ExpandHeight(true));
                _configTreeView.OnGUI(treeRect);

            }
        }

        private void AddConfig()
        {
            Undo.RecordObject(_settings, "Add Config");
            _settings.syncConfigs.Add(new SyncConfig
            {
                configName = $"Config {_settings.syncConfigs.Count + 1}",
                enabled = false,
                isSyncActivated = false
            });
            EditorUtility.SetDirty(_settings);
            int newIndex = _settings.syncConfigs.Count - 1;
            _configTreeView.Reload();
            _configTreeView.SelectAndBeginRename(newIndex);
            SaveSelectedConfigIndex(newIndex);
        }

        private void DrawResizeHandle()
        {
            Rect handleRect = GUILayoutUtility.GetRect(5f, 1f, GUILayout.Width(5f), GUILayout.ExpandHeight(true));
            EditorGUIUtility.AddCursorRect(handleRect, MouseCursor.ResizeHorizontal);

            EditorGUI.DrawRect(new Rect(handleRect.x + 2, handleRect.y, 1, handleRect.height),
                new Color(0.5f, 0.5f, 0.5f, 0.4f));

            if (Event.current.type == EventType.MouseDown && handleRect.Contains(Event.current.mousePosition))
            {
                _isResizing = true;
                Event.current.Use();
            }

            if (_isResizing)
            {
                if (Event.current.type == EventType.MouseDrag)
                {
                    _listPanelWidth = Mathf.Clamp(_listPanelWidth + Event.current.delta.x, 100f, position.width - 200f);
                    Repaint();
                    Event.current.Use();
                }
                if (Event.current.type == EventType.MouseUp)
                {
                    _isResizing = false;
                    Event.current.Use();
                }
            }
        }

        private void DrawConfigDetail()
        {
            using (new EditorGUILayout.VerticalScope())
            {
                int idx = SelectedConfigIndex;
                if (idx < 0 || idx >= _settings.syncConfigs.Count)
                {
                    GUILayout.FlexibleSpace();
                    GUILayout.Label("Select a config from the list.", EditorStyles.centeredGreyMiniLabel);
                    GUILayout.FlexibleSpace();
                    return;
                }

                var config = _settings.syncConfigs[idx];
                EnsureConfigCollections(config);
                EnsureActivationState(config);

                using (var scrollView = new EditorGUILayout.ScrollViewScope(_detailScrollPosition, GUILayout.ExpandHeight(true)))
                {
                    _detailScrollPosition = scrollView.scrollPosition;

                    if (config.isSyncActivated)
                    {
                        using (new EditorGUI.DisabledScope(!CanInteractWithEnableToggle(config)))
                        {
                            EditorGUI.BeginChangeCheck();
                            bool newEnabled = EditorGUILayout.Toggle("Enable", config.enabled);
                            if (EditorGUI.EndChangeCheck())
                            {
                                if (newEnabled && !CanActivateWithSyncButton(config))
                                    newEnabled = false;

                                if (newEnabled != config.enabled)
                                {
                                    Undo.RecordObject(_settings, "Toggle Config");
                                    config.enabled = newEnabled;
                                    ApplyEnableStateChange(config);
                                }
                            }
                        }
                    }

                    bool sourceDestinationReadOnly = IsSourceAndDestinationReadOnly(config);
                    using (new EditorGUI.DisabledScope(sourceDestinationReadOnly))
                    {
                        bool sourceIsValid = IsFolderSelectionValid(config.sourcePath);
                        bool destinationIsValid = IsFolderSelectionValid(config.destinationPath);

                        var srcObj = string.IsNullOrEmpty(config.sourcePath)
                            ? null : AssetDatabase.LoadAssetAtPath<DefaultAsset>(config.sourcePath);
                        Color previousBackgroundColor = GUI.backgroundColor;
                        if (!sourceIsValid)
                            GUI.backgroundColor = Color.red;
                        var newSrcObj = (DefaultAsset)EditorGUILayout.ObjectField("Source", srcObj, typeof(DefaultAsset), false);
                        GUI.backgroundColor = previousBackgroundColor;
                        if (newSrcObj != srcObj)
                        {
                            string selectedPath = AssetDatabase.GetAssetPath(newSrcObj);
                            if (!string.IsNullOrEmpty(selectedPath) && !AssetDatabase.IsValidFolder(selectedPath))
                            {
                                Debug.LogWarning("[AssetSync] Source must be a folder.");
                            }
                            else
                            {
                                Undo.RecordObject(_settings, "Set Source");
                                config.sourcePath = selectedPath;
                                ApplyConfigChange(config);
                            }
                        }

                        var dstObj = string.IsNullOrEmpty(config.destinationPath)
                            ? null : AssetDatabase.LoadAssetAtPath<DefaultAsset>(config.destinationPath);
                        previousBackgroundColor = GUI.backgroundColor;
                        if (!destinationIsValid)
                            GUI.backgroundColor = Color.red;
                        var newDstObj = (DefaultAsset)EditorGUILayout.ObjectField("Destination", dstObj, typeof(DefaultAsset), false);
                        GUI.backgroundColor = previousBackgroundColor;
                        if (newDstObj != dstObj)
                        {
                            string selectedPath = AssetDatabase.GetAssetPath(newDstObj);
                            if (!string.IsNullOrEmpty(selectedPath) && !AssetDatabase.IsValidFolder(selectedPath))
                            {
                                Debug.LogWarning("[AssetSync] Destination must be a folder.");
                            }
                            else
                            {
                                Undo.RecordObject(_settings, "Set Destination");
                                config.destinationPath = selectedPath;
                                ApplyConfigChange(config);
                            }
                        }
                    }

                    EditorGUI.BeginChangeCheck();
                    bool newIncludeSubdirectories = EditorGUILayout.Toggle("Include Subdirectories", config.includeSubdirectories);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(_settings, "Toggle Include Subdirectories");
                        config.includeSubdirectories = newIncludeSubdirectories;
                        ApplyConfigChange(config);
                    }

                    if (TryGetConfigWarningForDisplay(config, out string warning))
                    {
                        EditorGUILayout.HelpBox(warning, MessageType.Warning);
                    }

                    EditorGUILayout.Space();
                    DrawFilterList(config);
                    EditorGUILayout.Space();
                    DrawIgnoreList(config);
                }

                if (!config.isSyncActivated)
                {
                    DrawSyncActivationButton(config);
                }
            }
        }

        private void DrawFilterList(SyncConfig config)
        {
            EnsureConfigCollections(config);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Filters", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                var addIcon = EditorGUIUtility.IconContent("d_Toolbar Plus");
                addIcon.tooltip = "Add Filter";
                if (GUILayout.Button(addIcon, EditorStyles.iconButton))
                {
                    Undo.RecordObject(_settings, "Add Filter");
                    config.filters.Add(new FilterCondition());
                    ApplyConfigChange(config);
                }
            }

            int deleteIndex = -1;
            for (int i = 0; i < config.filters.Count; i++)
            {
                config.filters[i] ??= new FilterCondition();
                DrawFilterCondition(config.filters[i], i, ref deleteIndex, config);
            }

            if (deleteIndex >= 0)
            {
                Undo.RecordObject(_settings, "Delete Filter");
                _typesLists.Remove(config.filters[deleteIndex]);
                config.filters.RemoveAt(deleteIndex);
                ApplyConfigChange(config);
            }
        }

        private void DrawFilterCondition(FilterCondition filter, int index, ref int deleteIndex, SyncConfig config)
        {
            filter.multipleTypeNames ??= new List<string>();

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    bool newInvert = EditorGUILayout.Toggle("Exclude", filter.invert);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(_settings, "Toggle Filter Mode");
                        filter.invert = newInvert;
                        ApplyConfigChange(config);
                    }
                    GUILayout.FlexibleSpace();
                    var deleteFilterIcon = EditorGUIUtility.IconContent("CrossIcon");
                    deleteFilterIcon.tooltip = "Delete Filter";
                    if (GUILayout.Button(deleteFilterIcon, EditorStyles.iconButton))
                        deleteIndex = index;
                }

                EditorGUI.BeginChangeCheck();
                bool newUseMultipleTypes = EditorGUILayout.Toggle("Multiple Types", filter.useMultipleTypes);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(_settings, "Change Filter Mode");
                    if (newUseMultipleTypes)
                    {
                        filter.multipleTypeNames.Clear();
                        if (!string.IsNullOrEmpty(filter.singleTypeName))
                            filter.multipleTypeNames.Add(filter.singleTypeName);
                    }
                    else
                    {
                        filter.singleTypeName = filter.multipleTypeNames.Count > 0
                            ? filter.multipleTypeNames[0] : "";
                    }
                    filter.useMultipleTypes = newUseMultipleTypes;
                    _typesLists.Remove(filter);
                    ApplyConfigChange(config);
                }

                EditorGUILayout.LabelField("Type", EditorStyles.miniLabel);
                if (filter.useMultipleTypes)
                {
                    GetOrCreateTypesList(filter, config).DoLayoutList();
                }
                else
                {
                    string typeDisplay = string.IsNullOrEmpty(filter.singleTypeName)
                        ? "Select Type..."
                        : NicifyTypeName(filter.singleTypeName);
                    if (GUILayout.Button(typeDisplay, EditorStyles.popup))
                    {
                        var dropdown = new TypeSelectorDropdown(new AdvancedDropdownState(),
                            n =>
                            {
                                Undo.RecordObject(_settings, "Change Type");
                                filter.singleTypeName = n;
                                ApplyConfigChange(config);
                            });
                        dropdown.Show(GUILayoutUtility.GetLastRect());
                    }
                }
            }
        }

        private void DrawIgnoreList(SyncConfig config)
        {
            config.ignoreGuids ??= new List<string>();
            GetOrCreateIgnoreList(config).DoLayoutList();
        }

        private ReorderableList GetOrCreateTypesList(FilterCondition filter, SyncConfig config)
        {
            if (_typesLists.TryGetValue(filter, out var existing))
                return existing;

            var list = new ReorderableList(filter.multipleTypeNames, typeof(string),
                draggable: true, displayHeader: true, displayAddButton: true, displayRemoveButton: true);

            list.drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Types");
            list.elementHeight = EditorGUIUtility.singleLineHeight + 2;

            list.drawElementCallback = (rect, i, isActive, isFocused) =>
            {
                if (i >= filter.multipleTypeNames.Count) return;
                string display = string.IsNullOrEmpty(filter.multipleTypeNames[i])
                    ? "Select Type..." : NicifyTypeName(filter.multipleTypeNames[i]);
                var btnRect = new Rect(rect.x, rect.y + 1, rect.width, rect.height - 2);
                int captured = i;
                if (GUI.Button(btnRect, display, EditorStyles.popup))
                {
                    var dropdown = new TypeSelectorDropdown(new AdvancedDropdownState(),
                        n =>
                        {
                            Undo.RecordObject(_settings, "Change Type");
                            filter.multipleTypeNames[captured] = n;
                            ApplyConfigChange(config);
                        });
                    dropdown.Show(btnRect);
                }
            };

            list.onAddCallback = _ =>
            {
                Undo.RecordObject(_settings, "Add Type");
                filter.multipleTypeNames.Add("");
                ApplyConfigChange(config);
            };

            list.onRemoveCallback = _ =>
            {
                if (list.index >= 0 && list.index < filter.multipleTypeNames.Count)
                {
                    Undo.RecordObject(_settings, "Remove Type");
                    filter.multipleTypeNames.RemoveAt(list.index);
                    ApplyConfigChange(config);
                }
            };

            _typesLists[filter] = list;
            return list;
        }

        private ReorderableList GetOrCreateIgnoreList(SyncConfig config)
        {
            if (_ignoreLists.TryGetValue(config, out var existing))
                return existing;

            config.ignoreGuids ??= new List<string>();

            var list = new ReorderableList(config.ignoreGuids, typeof(string),
                draggable: true, displayHeader: true, displayAddButton: true, displayRemoveButton: true);

            list.drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Ignore Assets");
            list.elementHeight = EditorGUIUtility.singleLineHeight + 2;

            list.drawElementCallback = (rect, index, isActive, isFocused) =>
            {
                if (index < 0 || index >= config.ignoreGuids.Count)
                    return;

                string guid = config.ignoreGuids[index];
                string path = AssetDatabase.GUIDToAssetPath(guid);
                UnityEngine.Object current = string.IsNullOrEmpty(path)
                    ? null
                    : AssetDatabase.LoadMainAssetAtPath(path);

                var fieldRect = new Rect(rect.x, rect.y + 1, rect.width, EditorGUIUtility.singleLineHeight);
                EditorGUI.BeginChangeCheck();
                var next = EditorGUI.ObjectField(fieldRect, $"Element {index}", current, typeof(UnityEngine.Object), false);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(_settings, "Change Ignore Asset");
                    string nextPath = AssetDatabase.GetAssetPath(next);
                    config.ignoreGuids[index] = string.IsNullOrEmpty(nextPath)
                        ? string.Empty
                        : AssetDatabase.AssetPathToGUID(nextPath);
                    ApplyConfigChange(config);
                }
            };

            list.onAddCallback = _ =>
            {
                Undo.RecordObject(_settings, "Add Ignore Asset");
                config.ignoreGuids.Add(string.Empty);
                ApplyConfigChange(config);
            };

            list.onRemoveCallback = _ =>
            {
                if (list.index < 0 || list.index >= config.ignoreGuids.Count)
                    return;

                Undo.RecordObject(_settings, "Delete Ignore Asset");
                config.ignoreGuids.RemoveAt(list.index);
                ApplyConfigChange(config);
            };

            _ignoreLists[config] = list;
            return list;
        }

        private void DeleteConfig(int idx)
        {
            if (_settings == null || idx < 0 || idx >= _settings.syncConfigs.Count) return;

            var config = _settings.syncConfigs[idx];
            HashSet<string> filesToDelete = AssetSyncer.CollectExistingSyncedDestinationFilesForDeletedConfig(config);
            if (filesToDelete.Count > 0)
            {
                string fileLabel = filesToDelete.Count == 1 ? "file" : "files";
                bool approved = ShowDeleteWarningDialog(
                    "Remove Config",
                    $"Removing this config will also delete {filesToDelete.Count} synced destination {fileLabel}.{Environment.NewLine}{Environment.NewLine}Continue?");
                if (!approved)
                    return;
            }

            AssetSyncer.RemoveSyncedFilesForDeletedConfig(config);

            Undo.RecordObject(_settings, "Delete Config");
            _settings.syncConfigs.RemoveAt(idx);
            EditorUtility.SetDirty(_settings);
            _typesLists.Clear();
            _ignoreLists.Clear();

            _configTreeView.Reload();
            int next = Mathf.Clamp(idx - 1, 0, _settings.syncConfigs.Count - 1);
            if (_settings.syncConfigs.Count > 0)
            {
                _configTreeView.SetSelection(new[] { next }, TreeViewSelectionOptions.RevealAndFrame);
                SaveSelectedConfigIndex(next);
            }
            else
            {
                _configTreeView.SetSelection(new List<int>());
                SaveSelectedConfigIndex(-1);
            }
        }

        private void RestoreOrInitializeConfigSelection()
        {
            if (_settings == null || _configTreeView == null || _settings.syncConfigs == null || _settings.syncConfigs.Count == 0)
                return;

            int rememberedIndex = GetRememberedConfigIndex();
            _configTreeView.SetSelection(new[] { rememberedIndex }, TreeViewSelectionOptions.RevealAndFrame);
        }

        private int GetRememberedConfigIndex()
        {
            if (_settings?.syncConfigs == null || _settings.syncConfigs.Count == 0)
                return -1;

            string selectionKey = GetSelectedConfigIndexPrefKey();
            int rememberedIndex = string.IsNullOrEmpty(selectionKey)
                ? 0
                : EditorPrefs.GetInt(selectionKey, 0);

            return Mathf.Clamp(rememberedIndex, 0, _settings.syncConfigs.Count - 1);
        }

        private string GetSelectedConfigIndexPrefKey()
        {
            if (_settings == null)
                return null;

            string settingsPath = AssetDatabase.GetAssetPath(_settings);
            if (string.IsNullOrEmpty(settingsPath))
                return null;

            return SelectedConfigIndexPrefKeyPrefix + settingsPath;
        }

        private void SaveSelectedConfigIndex(int selectedIndex)
        {
            string selectionKey = GetSelectedConfigIndexPrefKey();
            if (string.IsNullOrEmpty(selectionKey))
                return;

            if (_settings?.syncConfigs == null
                || _settings.syncConfigs.Count == 0
                || selectedIndex < 0
                || selectedIndex >= _settings.syncConfigs.Count)
            {
                EditorPrefs.DeleteKey(selectionKey);
                return;
            }

            EditorPrefs.SetInt(selectionKey, selectedIndex);
        }

        private static bool ShowDeleteWarningDialog(string title, string message)
        {
            if (DisplayDialogOverride != null)
                return DisplayDialogOverride(title, message, "Remove", "Cancel");

            return EditorUtility.DisplayDialog(title, message, "Remove", "Cancel");
        }

        private void DrawSyncActivationButton(SyncConfig config)
        {
            GUILayout.Space(6f);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(!CanActivateWithSyncButton(config)))
                {
                    if (GUILayout.Button("Sync", GUILayout.Width(96f), GUILayout.Height(28f)))
                        ActivateConfigWithSyncButton(config);
                }
            }
            GUILayout.Space(2f);
        }

        private void ActivateConfigWithSyncButton(SyncConfig config)
        {
            if (_settings == null || config == null || !CanActivateWithSyncButton(config))
                return;

            Undo.RecordObject(_settings, "Activate Config Sync");
            config.isSyncActivated = true;
            config.enabled = true;
            ApplyEnableStateChange(config);
        }

        private void EnsureActivationState(SyncConfig config)
        {
            if (_settings == null || config == null)
                return;

            bool shouldActivate = config.isSyncActivated
                || config.enabled
                || (config.syncRelativePaths != null && config.syncRelativePaths.Count > 0);
            if (shouldActivate == config.isSyncActivated)
                return;

            config.isSyncActivated = shouldActivate;
            EditorUtility.SetDirty(_settings);
        }

        private static bool CanActivateWithSyncButton(SyncConfig config)
        {
            if (config == null)
                return false;
            if (string.IsNullOrEmpty(config.sourcePath) || string.IsNullOrEmpty(config.destinationPath))
                return false;
            if (!AssetDatabase.IsValidFolder(config.sourcePath) || !AssetDatabase.IsValidFolder(config.destinationPath))
                return false;

            return !TryGetConfigWarningForActivation(config, out _);
        }

        private static bool IsSourceAndDestinationReadOnly(SyncConfig config)
        {
            return config != null && config.isSyncActivated && config.enabled;
        }

        private static bool CanInteractWithEnableToggle(SyncConfig config)
        {
            if (config == null)
                return false;

            return config.enabled || CanActivateWithSyncButton(config);
        }

        private static bool IsFolderSelectionValid(string assetPath)
        {
            return !string.IsNullOrEmpty(assetPath) && AssetDatabase.IsValidFolder(assetPath);
        }

        private static bool TryGetConfigWarningForDisplay(SyncConfig config, out string warning)
        {
            if (config == null)
            {
                warning = null;
                return false;
            }

            if (config.enabled)
                return AssetSyncer.TryGetConfigWarning(config, out warning);

            return TryGetConfigWarningForActivation(config, out warning);
        }

        private static bool TryGetConfigWarningForActivation(SyncConfig config, out string warning)
        {
            var probe = new SyncConfig
            {
                configName = config?.configName,
                enabled = true,
                includeSubdirectories = config?.includeSubdirectories ?? false,
                sourcePath = config?.sourcePath,
                destinationPath = config?.destinationPath
            };
            return AssetSyncer.TryGetConfigWarning(probe, out warning);
        }

        private void ApplyConfigChange(SyncConfig config)
        {
            EditorUtility.SetDirty(_settings);
            if (!config.enabled)
            {
                AssetSyncer.PruneSyncPathsForDisabledConfig(config);
                return;
            }
            if (string.IsNullOrEmpty(config.sourcePath) || string.IsNullOrEmpty(config.destinationPath))
                return;
            EnqueueDeferredSync(() =>
            {
                if (_settings == null || config == null)
                    return;

                AssetSyncer.SyncConfig(config, out bool stateChanged);
                if (stateChanged)
                    EditorUtility.SetDirty(_settings);
            });
        }

        private void ApplyEnableStateChange(SyncConfig config)
        {
            EditorUtility.SetDirty(_settings);
            if (string.IsNullOrEmpty(config.sourcePath) || string.IsNullOrEmpty(config.destinationPath))
                return;

            // Explicit enable toggle is the only time disabled-state sync cleanup should run.
            EnqueueDeferredSync(() =>
            {
                if (_settings == null || config == null)
                    return;

                AssetSyncer.SyncConfig(config, out bool stateChanged);
                if (stateChanged)
                    EditorUtility.SetDirty(_settings);
            });
        }

        private static string NicifyTypeName(string assemblyQualifiedName)
        {
            if (string.IsNullOrEmpty(assemblyQualifiedName)) return "(None)";
            int comma = assemblyQualifiedName.IndexOf(',');
            string fullName = comma >= 0 ? assemblyQualifiedName.Substring(0, comma).Trim() : assemblyQualifiedName;
            int dot = fullName.LastIndexOf('.');
            return ObjectNames.NicifyVariableName(dot >= 0 ? fullName.Substring(dot + 1) : fullName);
        }

        private static void EnsureConfigCollections(SyncConfig config)
        {
            config.filters ??= new List<FilterCondition>();
            config.ignoreGuids ??= new List<string>();
            config.syncRelativePaths ??= new List<string>();
        }

        private void EnqueueDeferredSync(Action syncAction)
        {
            if (syncAction == null)
                return;

            _deferredSyncActions.Enqueue(syncAction);
            if (_deferredSyncScheduled)
                return;

            _deferredSyncScheduled = true;
            EditorApplication.delayCall += FlushDeferredSyncActions;
        }

        private void FlushDeferredSyncActions()
        {
            _deferredSyncScheduled = false;

            while (_deferredSyncActions.Count > 0)
            {
                var action = _deferredSyncActions.Dequeue();
                action?.Invoke();
            }
        }

        private void OnDisable()
        {
            EditorApplication.delayCall -= FlushDeferredSyncActions;
            _deferredSyncActions.Clear();
            _deferredSyncScheduled = false;
        }
    }
}
