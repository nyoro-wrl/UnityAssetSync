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
        private static readonly string[] FilterActionLabels = { "Include", "Exclude" };
        private const float FilterTypeModeToggleWidth = 30f;
        private const float ListSectionHorizontalMargin = 6f;
        internal static Func<string, string, string, string, bool> DisplayDialogOverride;

        private AssetSyncSettings _settings;
        private Vector2 _detailScrollPosition;
        private float _listPanelWidth = 150f;
        private bool _isResizing;
        private readonly Queue<Action> _deferredSyncActions = new Queue<Action>();
        private bool _deferredSyncScheduled;

        [SerializeField] private TreeViewState<int> _treeViewState;
        private ConfigTreeView _configTreeView;

        private readonly Dictionary<FilterCondition, bool> _filterTypeFoldouts =
            new Dictionary<FilterCondition, bool>();
        private readonly Dictionary<SyncConfig, ReorderableList> _filterLists =
            new Dictionary<SyncConfig, ReorderableList>();
        private readonly Dictionary<SyncConfig, ReorderableList> _ignoreLists =
            new Dictionary<SyncConfig, ReorderableList>();
        private GUIContent _filterTypeModeToggleContent;

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
                    _filterTypeFoldouts.Clear();
                    _filterLists.Clear();
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
                        _filterTypeFoldouts.Clear();
                        _filterLists.Clear();
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
                    DrawListSectionWithHorizontalMargin(() => DrawFilterList(config));
                    EditorGUILayout.Space();
                    DrawListSectionWithHorizontalMargin(() => DrawIgnoreList(config));
                }

                if (!config.isSyncActivated)
                {
                    DrawSyncActivationButton(config);
                }
            }
        }

        private void DrawFilterList(SyncConfig config)
        {
            config.filters ??= new List<FilterCondition>();
            GetOrCreateFilterList(config).DoLayoutList();
        }

        private static void DrawListSectionWithHorizontalMargin(Action drawAction)
        {
            if (drawAction == null)
                return;

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(ListSectionHorizontalMargin);
                using (new EditorGUILayout.VerticalScope())
                {
                    drawAction.Invoke();
                }
                GUILayout.Space(ListSectionHorizontalMargin);
            }
        }

        private ReorderableList GetOrCreateFilterList(SyncConfig config)
        {
            if (_filterLists.TryGetValue(config, out var existing))
            {
                existing.list = config.filters;
                return existing;
            }

            var list = new ReorderableList(config.filters, typeof(FilterCondition),
                draggable: true, displayHeader: true, displayAddButton: true, displayRemoveButton: true);

            list.drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Filters");
            list.elementHeightCallback = index => GetFilterElementHeight(config, index);

            list.drawElementCallback = (rect, index, isActive, isFocused) =>
            {
                if (index < 0 || index >= config.filters.Count)
                    return;

                var filter = config.filters[index] ??= new FilterCondition();
                DrawFilterElement(rect, filter, config);
            };

            list.onAddCallback = _ =>
            {
                Undo.RecordObject(_settings, "Add Filter");
                config.filters.Add(new FilterCondition());
                ApplyConfigChange(config);
            };

            list.onRemoveCallback = _ =>
            {
                if (list.index < 0 || list.index >= config.filters.Count)
                    return;

                Undo.RecordObject(_settings, "Delete Filter");
                FilterCondition removedFilter = config.filters[list.index];
                _filterTypeFoldouts.Remove(removedFilter);
                config.filters.RemoveAt(list.index);
                ApplyConfigChange(config);
            };

            _filterLists[config] = list;
            return list;
        }

        private float GetFilterElementHeight(SyncConfig config, int index)
        {
            const float itemMargin = 8f;
            const float outerPadding = 6f;
            float lineHeight = EditorGUIUtility.singleLineHeight;
            float rowSpacing = EditorGUIUtility.standardVerticalSpacing;

            if (index < 0 || index >= config.filters.Count)
                return lineHeight + (outerPadding * 2f) + (itemMargin * 2f);

            FilterCondition filter = config.filters[index] ??= new FilterCondition();
            filter.multipleTypeNames ??= new List<string>();

            float contentHeight = lineHeight + rowSpacing; // action
            if (filter.useMultipleTypes)
            {
                contentHeight += lineHeight;
                if (GetFilterTypeFoldout(filter))
                {
                    int count = filter.multipleTypeNames.Count;
                    if (count > 0)
                    {
                        contentHeight += rowSpacing;
                        contentHeight += count * lineHeight;
                        contentHeight += (count - 1) * rowSpacing;
                    }
                }
            }
            else
            {
                contentHeight += lineHeight;
            }

            return contentHeight + (outerPadding * 2f) + (itemMargin * 2f);
        }

        private void DrawFilterElement(Rect rect, FilterCondition filter, SyncConfig config)
        {
            filter.multipleTypeNames ??= new List<string>();
            float lineHeight = EditorGUIUtility.singleLineHeight;
            float rowSpacing = EditorGUIUtility.standardVerticalSpacing;
            const float itemMargin = 8f;
            const float outerPadding = 6f;
            const float innerPadding = 8f;
            const float labelWidth = 120f;
            const float actionToolbarWidth = 180f;

            var containerRect = new Rect(
                rect.x,
                rect.y + itemMargin,
                rect.width,
                Mathf.Max(0f, rect.height - (itemMargin * 2f)));
            Color containerColor = new Color(0f, 0f, 0f, 0.18f);
            EditorGUI.DrawRect(containerRect, containerColor);
            Color borderColor = new Color(1f, 1f, 1f, 0.14f);
            EditorGUI.DrawRect(new Rect(containerRect.x, containerRect.y, containerRect.width, 1f), borderColor);
            EditorGUI.DrawRect(new Rect(containerRect.x, containerRect.yMax - 1f, containerRect.width, 1f), borderColor);
            EditorGUI.DrawRect(new Rect(containerRect.x, containerRect.y, 1f, containerRect.height), borderColor);
            EditorGUI.DrawRect(new Rect(containerRect.xMax - 1f, containerRect.y, 1f, containerRect.height), borderColor);

            var contentRect = new Rect(
                containerRect.x + innerPadding,
                containerRect.y + outerPadding,
                containerRect.width - (innerPadding * 2f),
                containerRect.height - (outerPadding * 2f));
            float y = contentRect.y;
            float fieldWidth = Mathf.Max(0f, contentRect.width - labelWidth);

            EditorGUI.BeginChangeCheck();
            var actionLabelRect = new Rect(contentRect.x, y, labelWidth, lineHeight);
            var actionFieldRect = new Rect(
                contentRect.x + labelWidth,
                y,
                Mathf.Min(fieldWidth, actionToolbarWidth),
                lineHeight);
            EditorGUI.LabelField(actionLabelRect, "Action");
            int actionSelected = filter.invert ? 1 : 0;
            int newActionSelected = GUI.Toolbar(actionFieldRect, actionSelected, FilterActionLabels);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_settings, "Change Filter");
                filter.invert = newActionSelected == 1;
                ApplyConfigChange(config);
            }

            y += lineHeight + rowSpacing;
            DrawTypeField(contentRect.x, ref y, labelWidth, fieldWidth, lineHeight, rowSpacing, filter, config);
        }

        private void DrawTypeField(float x, ref float y, float labelWidth, float fieldWidth, float lineHeight,
            float rowSpacing, FilterCondition filter, SyncConfig config)
        {
            const float spacing = 2f;
            var labelRect = new Rect(x, y, labelWidth, lineHeight);

            float fieldX = x + labelWidth;
            float valueWidth = Mathf.Max(0f, fieldWidth - FilterTypeModeToggleWidth - spacing);
            var valueRect = new Rect(fieldX, y, valueWidth, lineHeight);
            var toggleRect = new Rect(valueRect.xMax + spacing, y, FilterTypeModeToggleWidth, lineHeight);

            if (!filter.useMultipleTypes)
            {
                EditorGUI.LabelField(labelRect, "Type");
                string typeDisplay = string.IsNullOrEmpty(filter.singleTypeName)
                    ? "(None)"
                    : NicifyTypeName(filter.singleTypeName);
                if (GUI.Button(valueRect, typeDisplay, EditorStyles.popup))
                {
                    var dropdown = new TypeSelectorDropdown(new AdvancedDropdownState(),
                        n =>
                        {
                            Undo.RecordObject(_settings, "Change Type");
                            filter.singleTypeName = n;
                            ApplyConfigChange(config);
                        });
                    dropdown.Show(valueRect);
                }

                using (var ccs = new EditorGUI.ChangeCheckScope())
                {
                    bool nextIsListMode = DrawFilterTypeModeToggle(
                        toggleRect,
                        filter.useMultipleTypes,
                        GetFilterTypeModeToggleContent());
                    if (ccs.changed && nextIsListMode != filter.useMultipleTypes)
                    {
                        Undo.RecordObject(_settings, "Change Type Mode");
                        SwitchFilterTypeMode(filter, nextIsListMode);
                        SetFilterTypeFoldout(filter, true);
                        ApplyConfigChange(config);
                    }
                }
                return;
            }

            bool foldout = GetFilterTypeFoldout(filter);
            bool nextFoldout = EditorGUI.Foldout(labelRect, foldout, "Type", true);
            if (nextFoldout != foldout)
                SetFilterTypeFoldout(filter, nextFoldout);

            const float listSizeButtonWidth = 25f;
            float buttonWidth = Mathf.Round(listSizeButtonWidth);
            float listButtonsWidth = buttonWidth * 2f;
            float buttonsStartX = Mathf.Floor(Mathf.Max(valueRect.x, valueRect.xMax - listButtonsWidth));
            var plusRect = new Rect(
                buttonsStartX,
                y,
                buttonWidth,
                lineHeight);
            var minusRect = new Rect(plusRect.xMax, y, buttonWidth, lineHeight);
            GUIStyle plusButtonStyle = EditorStyles.miniButtonLeft;
            GUIStyle minusButtonStyle = EditorStyles.miniButtonRight;

            if (GUI.Button(plusRect, "+", plusButtonStyle))
            {
                Undo.RecordObject(_settings, "Add Type");
                filter.multipleTypeNames.Add(string.Empty);
                ApplyConfigChange(config);
            }

            using (new EditorGUI.DisabledScope(filter.multipleTypeNames.Count <= 1))
            {
                if (GUI.Button(minusRect, "-", minusButtonStyle))
                {
                    Undo.RecordObject(_settings, "Remove Type");
                    filter.multipleTypeNames.RemoveAt(filter.multipleTypeNames.Count - 1);
                    ApplyConfigChange(config);
                }
            }

            using (var ccs = new EditorGUI.ChangeCheckScope())
            {
                bool nextIsListMode = DrawFilterTypeModeToggle(
                    toggleRect,
                    filter.useMultipleTypes,
                    GetFilterTypeModeToggleContent());
                if (ccs.changed && nextIsListMode != filter.useMultipleTypes)
                {
                    Undo.RecordObject(_settings, "Change Type Mode");
                    SwitchFilterTypeMode(filter, nextIsListMode);
                    ApplyConfigChange(config);
                    return;
                }
            }

            if (!nextFoldout)
                return;

            y += lineHeight + rowSpacing;
            for (int i = 0; i < filter.multipleTypeNames.Count; i++)
            {
                var elementLabelRect = new Rect(x, y, labelWidth, lineHeight);
                var elementFieldRect = new Rect(fieldX, y, fieldWidth, lineHeight);
                EditorGUI.LabelField(elementLabelRect, $"Element {i}");
                string display = string.IsNullOrEmpty(filter.multipleTypeNames[i])
                    ? "(None)"
                    : NicifyTypeName(filter.multipleTypeNames[i]);
                int captured = i;
                if (GUI.Button(elementFieldRect, display, EditorStyles.popup))
                {
                    var dropdown = new TypeSelectorDropdown(new AdvancedDropdownState(),
                        n =>
                        {
                            Undo.RecordObject(_settings, "Change Type");
                            filter.multipleTypeNames[captured] = n;
                            ApplyConfigChange(config);
                        });
                    dropdown.Show(elementFieldRect);
                }

                if (i < filter.multipleTypeNames.Count - 1)
                    y += lineHeight + rowSpacing;
            }
        }

        private static void SwitchFilterTypeMode(FilterCondition filter, bool listMode)
        {
            filter.multipleTypeNames ??= new List<string>();
            if (filter.useMultipleTypes == listMode)
                return;

            if (listMode)
            {
                if (filter.multipleTypeNames.Count == 0)
                    filter.multipleTypeNames.Add(filter.singleTypeName ?? string.Empty);
                else
                    filter.multipleTypeNames[0] = filter.singleTypeName ?? string.Empty;
            }
            else
            {
                if (filter.multipleTypeNames.Count > 0)
                    filter.singleTypeName = filter.multipleTypeNames[0];
            }

            filter.useMultipleTypes = listMode;
        }

        private bool GetFilterTypeFoldout(FilterCondition filter)
        {
            if (_filterTypeFoldouts.TryGetValue(filter, out bool foldout))
                return foldout;

            _filterTypeFoldouts[filter] = true;
            return true;
        }

        private void SetFilterTypeFoldout(FilterCondition filter, bool foldout)
        {
            _filterTypeFoldouts[filter] = foldout;
        }

        private GUIContent GetFilterTypeModeToggleContent()
        {
            if (_filterTypeModeToggleContent != null)
                return _filterTypeModeToggleContent;

            const string iconKey = "d_UnityEditor.SceneHierarchyWindow";
            Texture iconTexture =
                (EditorGUIUtility.IconContent(iconKey)?.image as Texture)
                ?? EditorGUIUtility.FindTexture(iconKey)
                ?? (EditorGUIUtility.Load(iconKey) as Texture)
                ?? (EditorGUIUtility.Load(iconKey + ".png") as Texture);

            if (iconTexture != null)
            {
                _filterTypeModeToggleContent = new GUIContent(iconTexture, "Toggle single/list mode");
                return _filterTypeModeToggleContent;
            }

            _filterTypeModeToggleContent = new GUIContent("L", "Toggle single/list mode");
            return _filterTypeModeToggleContent;
        }

        private static bool DrawFilterTypeModeToggle(Rect rect, bool value, GUIContent content)
        {
            GUIContent buttonContent = content != null && content.image != null
                ? new GUIContent(string.Empty, content.tooltip)
                : content ?? GUIContent.none;

            bool nextValue = GUI.Toggle(rect, value, buttonContent, GUI.skin.button);

            if (content != null && content.image != null && Event.current.type == EventType.Repaint)
            {
                const float iconPadding = 2f;
                float iconSize = Mathf.Max(0f, Mathf.Min(rect.width, rect.height) - (iconPadding * 2f));
                var iconRect = new Rect(
                    rect.x + ((rect.width - iconSize) * 0.5f),
                    rect.y + ((rect.height - iconSize) * 0.5f),
                    iconSize,
                    iconSize);
                GUI.DrawTexture(iconRect, content.image, ScaleMode.ScaleToFit, true);
            }

            return nextValue;
        }

        private void DrawIgnoreList(SyncConfig config)
        {
            config.ignoreGuids ??= new List<string>();
            GetOrCreateIgnoreList(config).DoLayoutList();
        }

        private ReorderableList GetOrCreateIgnoreList(SyncConfig config)
        {
            if (_ignoreLists.TryGetValue(config, out var existing))
            {
                existing.list = config.ignoreGuids;
                return existing;
            }

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
                var next = EditorGUI.ObjectField(fieldRect, current, typeof(UnityEngine.Object), false);
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
            _filterTypeFoldouts.Clear();
            _filterLists.Clear();
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
