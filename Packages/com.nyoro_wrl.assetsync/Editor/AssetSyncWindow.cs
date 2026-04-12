using System.Collections.Generic;
using System;
using System.IO;
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
        private const float ListSectionHorizontalMargin = 6f;
        private const float PreviewListMaxHeight = 180f;
        private const float PreviewListMinHeight = 64f;
        private const float PreviewIconSize = 16f;
        internal static Func<string, string, string, string, bool> DisplayDialogOverride;

        private AssetSyncSettings _settings;
        private Vector2 _detailScrollPosition;
        private Vector2 _previewScrollPosition;
        [SerializeField] private bool _isPreviewExpanded = true;
        [SerializeField] private string _selectedPreviewEntryKey;
        private float _listPanelWidth = 150f;
        private bool _isResizing;
        private readonly Queue<Action> _deferredSyncActions = new Queue<Action>();
        private bool _deferredSyncScheduled;

        [SerializeField] private TreeViewState<int> _treeViewState;
        private ConfigTreeView _configTreeView;

        private readonly Dictionary<SyncConfig, ReorderableList> _filterLists =
            new Dictionary<SyncConfig, ReorderableList>();
        private readonly Dictionary<SyncConfig, ReorderableList> _ignoreLists =
            new Dictionary<SyncConfig, ReorderableList>();
        private readonly Dictionary<SyncConfig, SourceInputMode> _sourceInputModes =
            new Dictionary<SyncConfig, SourceInputMode>();

        private int SelectedConfigIndex => _configTreeView?.SelectedIndex ?? -1;

        [MenuItem("Window/Asset Sync")]
        public static void Open()
        {
            GetWindow<AssetSyncWindow>("Asset Sync");
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
                    _filterLists.Clear();
                    _ignoreLists.Clear();
                    _sourceInputModes.Clear();
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
                        _filterLists.Clear();
                        _ignoreLists.Clear();
                        _sourceInputModes.Clear();
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
            Undo.RecordObject(_settings, "Add Sync");
            _settings.syncConfigs.Add(new SyncConfig
            {
                configName = $"Sync {_settings.syncConfigs.Count + 1}",
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
                        bool destinationIsValid = IsDestinationFolderSelectionValid(config.destinationPath);

                        DrawSourcePathField(config, sourceIsValid);

                        var dstObj = string.IsNullOrEmpty(config.destinationPath)
                            ? null : AssetDatabase.LoadAssetAtPath<DefaultAsset>(config.destinationPath);
                        Color previousBackgroundColor = GUI.backgroundColor;
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

                    EditorGUI.BeginChangeCheck();
                    bool newKeepEmptyDirectories = EditorGUILayout.Toggle("Keep Empty Directories", config.keepEmptyDirectories);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(_settings, "Toggle Keep Empty Directories");
                        config.keepEmptyDirectories = newKeepEmptyDirectories;
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
                    EditorGUILayout.Space();
                    DrawListSectionWithHorizontalMargin(() => DrawCopyPreview(config));
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
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("Type"), false, () => AddFilter(config, FilterConditionTargetKind.Type));
                menu.AddItem(new GUIContent("Asset"), false, () => AddFilter(config, FilterConditionTargetKind.Asset));
                menu.AddItem(new GUIContent("Extension"), false, () => AddFilter(config, FilterConditionTargetKind.Extension));
                menu.ShowAsContext();
            };

            list.onRemoveCallback = _ =>
            {
                if (list.index < 0 || list.index >= config.filters.Count)
                    return;

                Undo.RecordObject(_settings, "Delete Filter");
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
            EnsureFilterCollections(filter);

            int listCount = GetFilterListElementCountForUi(filter);
            float contentHeight = lineHeight + rowSpacing; // action
            contentHeight += listCount * lineHeight;
            contentHeight += (listCount - 1) * rowSpacing;

            return contentHeight + (outerPadding * 2f) + (itemMargin * 2f);
        }

        private void DrawFilterElement(Rect rect, FilterCondition filter, SyncConfig config)
        {
            EnsureFilterCollections(filter);
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
            if (filter.targetKind == FilterConditionTargetKind.Asset)
                DrawAssetField(contentRect.x, ref y, labelWidth, fieldWidth, lineHeight, rowSpacing, filter, config);
            else if (filter.targetKind == FilterConditionTargetKind.Extension)
                DrawExtensionField(contentRect.x, ref y, labelWidth, fieldWidth, lineHeight, rowSpacing, filter, config);
            else
                DrawTypeField(contentRect.x, ref y, labelWidth, fieldWidth, lineHeight, rowSpacing, filter, config);
        }

        private void AddFilter(SyncConfig config, FilterConditionTargetKind targetKind)
        {
            if (config == null)
                return;

            Undo.RecordObject(_settings, "Add Filter");
            var filter = new FilterCondition
            {
                targetKind = targetKind
            };
            EnsureFilterCollections(filter);
            if (targetKind == FilterConditionTargetKind.Asset)
                filter.multipleAssetGuids.Add(string.Empty);
            else if (targetKind == FilterConditionTargetKind.Extension)
                filter.multipleExtensions.Add(string.Empty);
            else
                filter.multipleTypeNames.Add(string.Empty);

            config.filters.Add(filter);
            ApplyConfigChange(config);
        }

        private void DrawTypeField(float x, ref float y, float labelWidth, float fieldWidth, float lineHeight,
            float rowSpacing, FilterCondition filter, SyncConfig config)
        {
            const float spacing = 2f;
            EnsureFilterListMode(filter);
            float fieldX = x + labelWidth;
            const float listSizeButtonWidth = 25f;
            float buttonWidth = Mathf.Round(listSizeButtonWidth);
            for (int i = 0; i < filter.multipleTypeNames.Count; i++)
            {
                var elementLabelRect = new Rect(x, y, labelWidth, lineHeight);
                var elementAddRect = new Rect(
                    fieldX + Mathf.Max(0f, fieldWidth - (buttonWidth * 2f)),
                    y,
                    buttonWidth,
                    lineHeight);
                var elementRemoveRect = new Rect(
                    fieldX + Mathf.Max(0f, fieldWidth - buttonWidth),
                    y,
                    buttonWidth,
                    lineHeight);
                var elementFieldRect = new Rect(
                    fieldX,
                    y,
                    Mathf.Max(0f, fieldWidth - (buttonWidth * 2f) - (spacing * 2f)),
                    lineHeight);
                EditorGUI.LabelField(elementLabelRect, i == 0 ? "Type" : string.Empty);

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

                if (GUI.Button(elementAddRect, "+", EditorStyles.miniButton))
                {
                    Undo.RecordObject(_settings, "Add Type");
                    filter.multipleTypeNames.Insert(captured + 1, string.Empty);
                    ApplyConfigChange(config);
                    break;
                }

                using (new EditorGUI.DisabledScope(filter.multipleTypeNames.Count <= 1))
                {
                    if (GUI.Button(elementRemoveRect, "-", EditorStyles.miniButton))
                    {
                        Undo.RecordObject(_settings, "Remove Type");
                        filter.multipleTypeNames.RemoveAt(captured);
                        ApplyConfigChange(config);
                        break;
                    }
                }

                if (i < filter.multipleTypeNames.Count - 1)
                    y += lineHeight + rowSpacing;
            }
        }

        private void DrawAssetField(float x, ref float y, float labelWidth, float fieldWidth, float lineHeight,
            float rowSpacing, FilterCondition filter, SyncConfig config)
        {
            const float spacing = 2f;
            EnsureFilterListMode(filter);
            float fieldX = x + labelWidth;
            const float listSizeButtonWidth = 25f;
            float buttonWidth = Mathf.Round(listSizeButtonWidth);
            for (int i = 0; i < filter.multipleAssetGuids.Count; i++)
            {
                var elementLabelRect = new Rect(x, y, labelWidth, lineHeight);
                var elementAddRect = new Rect(
                    fieldX + Mathf.Max(0f, fieldWidth - (buttonWidth * 2f)),
                    y,
                    buttonWidth,
                    lineHeight);
                var elementRemoveRect = new Rect(
                    fieldX + Mathf.Max(0f, fieldWidth - buttonWidth),
                    y,
                    buttonWidth,
                    lineHeight);
                var elementFieldRect = new Rect(
                    fieldX,
                    y,
                    Mathf.Max(0f, fieldWidth - (buttonWidth * 2f) - (spacing * 2f)),
                    lineHeight);
                EditorGUI.LabelField(elementLabelRect, i == 0 ? "Asset" : string.Empty);
                int captured = i;
                DrawAssetFieldControl(elementFieldRect, filter.multipleAssetGuids[captured], config, isFilterAsset: true, "Change Asset", guid =>
                {
                    filter.multipleAssetGuids[captured] = guid;
                    ApplyConfigChange(config);
                });

                if (GUI.Button(elementAddRect, "+", EditorStyles.miniButton))
                {
                    Undo.RecordObject(_settings, "Add Asset");
                    filter.multipleAssetGuids.Insert(captured + 1, string.Empty);
                    ApplyConfigChange(config);
                    break;
                }

                using (new EditorGUI.DisabledScope(filter.multipleAssetGuids.Count <= 1))
                {
                    if (GUI.Button(elementRemoveRect, "-", EditorStyles.miniButton))
                    {
                        Undo.RecordObject(_settings, "Remove Asset");
                        filter.multipleAssetGuids.RemoveAt(captured);
                        ApplyConfigChange(config);
                        break;
                    }
                }

                if (i < filter.multipleAssetGuids.Count - 1)
                    y += lineHeight + rowSpacing;
            }
        }

        private void DrawExtensionField(float x, ref float y, float labelWidth, float fieldWidth, float lineHeight,
            float rowSpacing, FilterCondition filter, SyncConfig config)
        {
            const float spacing = 2f;
            EnsureFilterListMode(filter);
            float fieldX = x + labelWidth;
            const float listSizeButtonWidth = 25f;
            float buttonWidth = Mathf.Round(listSizeButtonWidth);
            for (int i = 0; i < filter.multipleExtensions.Count; i++)
            {
                var elementLabelRect = new Rect(x, y, labelWidth, lineHeight);
                var elementAddRect = new Rect(
                    fieldX + Mathf.Max(0f, fieldWidth - (buttonWidth * 2f)),
                    y,
                    buttonWidth,
                    lineHeight);
                var elementRemoveRect = new Rect(
                    fieldX + Mathf.Max(0f, fieldWidth - buttonWidth),
                    y,
                    buttonWidth,
                    lineHeight);
                var elementFieldRect = new Rect(
                    fieldX,
                    y,
                    Mathf.Max(0f, fieldWidth - (buttonWidth * 2f) - (spacing * 2f)),
                    lineHeight);
                EditorGUI.LabelField(elementLabelRect, i == 0 ? "Extension" : string.Empty);

                int captured = i;
                EditorGUI.BeginChangeCheck();
                string currentValue = filter.multipleExtensions[captured] ?? string.Empty;
                string nextValue = EditorGUI.DelayedTextField(elementFieldRect, currentValue);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(_settings, "Change Extension");
                    filter.multipleExtensions[captured] = nextValue;
                    ApplyConfigChange(config);
                }

                if (GUI.Button(elementAddRect, "+", EditorStyles.miniButton))
                {
                    Undo.RecordObject(_settings, "Add Extension");
                    filter.multipleExtensions.Insert(captured + 1, string.Empty);
                    ApplyConfigChange(config);
                    break;
                }

                using (new EditorGUI.DisabledScope(filter.multipleExtensions.Count <= 1))
                {
                    if (GUI.Button(elementRemoveRect, "-", EditorStyles.miniButton))
                    {
                        Undo.RecordObject(_settings, "Remove Extension");
                        filter.multipleExtensions.RemoveAt(captured);
                        ApplyConfigChange(config);
                        break;
                    }
                }

                if (i < filter.multipleExtensions.Count - 1)
                    y += lineHeight + rowSpacing;
            }
        }

        private void DrawAssetFieldControl(
            Rect rect,
            string guid,
            SyncConfig config,
            bool isFilterAsset,
            string undoLabel,
            Action<string> onChanged)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            UnityEngine.Object current = string.IsNullOrEmpty(assetPath)
                ? null
                : AssetDatabase.LoadMainAssetAtPath(assetPath);
            bool isValid = isFilterAsset
                ? IsFilterAssetGuidValid(config, guid)
                : IsIgnoreGuidValid(config, guid);

            Color previousBackgroundColor = GUI.backgroundColor;
            if (!isValid)
                GUI.backgroundColor = Color.red;
            EditorGUI.BeginChangeCheck();
            var next = EditorGUI.ObjectField(rect, current, typeof(UnityEngine.Object), false);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_settings, undoLabel);
                string nextPath = AssetDatabase.GetAssetPath(next);
                string nextGuid = string.IsNullOrEmpty(nextPath)
                    ? string.Empty
                    : AssetDatabase.AssetPathToGUID(nextPath);
                onChanged?.Invoke(nextGuid);
            }
            GUI.backgroundColor = previousBackgroundColor;
        }

        private void EnsureFilterListMode(FilterCondition filter)
        {
            EnsureFilterCollections(filter);
            bool changed = false;

            if (filter.targetKind == FilterConditionTargetKind.Asset)
            {
                if (filter.multipleAssetGuids.Count == 0)
                {
                    filter.multipleAssetGuids.Add(string.Empty);
                    changed = true;
                }
            }
            else if (filter.targetKind == FilterConditionTargetKind.Extension)
            {
                if (filter.multipleExtensions.Count == 0)
                {
                    filter.multipleExtensions.Add(string.Empty);
                    changed = true;
                }
            }
            else
            {
                if (filter.multipleTypeNames.Count == 0)
                {
                    filter.multipleTypeNames.Add(string.Empty);
                    changed = true;
                }
            }

            if (!changed)
                return;

            EditorUtility.SetDirty(_settings);
        }

        private static int GetFilterListElementCountForUi(FilterCondition filter)
        {
            int count;
            if (filter.targetKind == FilterConditionTargetKind.Asset)
                count = filter.multipleAssetGuids.Count;
            else if (filter.targetKind == FilterConditionTargetKind.Extension)
                count = filter.multipleExtensions.Count;
            else
                count = filter.multipleTypeNames.Count;

            return Mathf.Max(1, count);
        }

        private static void EnsureFilterCollections(FilterCondition filter)
        {
            filter.multipleTypeNames ??= new List<string>();
            filter.multipleAssetGuids ??= new List<string>();
            filter.multipleExtensions ??= new List<string>();
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

            list.drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Ignore Destination Assets");
            list.elementHeight = EditorGUIUtility.singleLineHeight + 2;

            list.drawElementCallback = (rect, index, isActive, isFocused) =>
            {
                if (index < 0 || index >= config.ignoreGuids.Count)
                    return;

                var fieldRect = new Rect(rect.x, rect.y + 1, rect.width, EditorGUIUtility.singleLineHeight);
                DrawAssetFieldControl(fieldRect, config.ignoreGuids[index], config, isFilterAsset: false, "Change Ignore Asset", guid =>
                {
                    config.ignoreGuids[index] = guid;
                    ApplyConfigChange(config);
                });
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

        private static bool IsFilterAssetGuidValid(SyncConfig config, string guid)
        {
            return IsGuidWithinRoot(guid, config?.sourcePath);
        }

        private static bool IsIgnoreGuidValid(SyncConfig config, string guid)
        {
            return IsGuidWithinRoot(guid, config?.destinationPath);
        }

        private static bool IsGuidWithinRoot(string guid, string rootPath)
        {
            if (string.IsNullOrWhiteSpace(guid))
                return true;
            if (string.IsNullOrEmpty(rootPath))
                return false;

            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(assetPath))
                return false;

            return AssetSyncPostprocessor.IsAssetPathWithinRoot(assetPath, rootPath);
        }

        private void DrawCopyPreview(SyncConfig config)
        {
            bool canPreview = CanPreviewCopyTargets(config, out string blockedReason);
            bool includeUnchangedForPreview = (config != null && config.enabled)
                || _deferredSyncScheduled
                || _deferredSyncActions.Count > 0;
            IReadOnlyList<AssetSyncer.PreviewCopyEntry> previewEntries = canPreview
                ? AssetSyncer.CollectCopyPreviewEntries(config, includeUnchanged: includeUnchangedForPreview)
                : Array.Empty<AssetSyncer.PreviewCopyEntry>();

            _isPreviewExpanded = EditorGUILayout.Foldout(_isPreviewExpanded, $"Preview ({previewEntries.Count})", true);
            if (!_isPreviewExpanded)
                return;

            if (!canPreview)
            {
                EditorGUILayout.HelpBox(blockedReason, MessageType.Info);
                return;
            }

            if (previewEntries.Count == 0)
            {
                EditorGUILayout.HelpBox("No assets will be synchronized.", MessageType.None);
                return;
            }

            bool selectedEntryExists = false;
            foreach (var previewEntry in previewEntries)
            {
                if (GetPreviewEntryKey(previewEntry) == _selectedPreviewEntryKey)
                {
                    selectedEntryExists = true;
                    break;
                }
            }
            if (!selectedEntryExists)
                _selectedPreviewEntryKey = null;

            float rowHeight = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            float desiredHeight = (previewEntries.Count * rowHeight) + 8f;
            float listHeight = Mathf.Clamp(desiredHeight, PreviewListMinHeight, PreviewListMaxHeight);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (var scroll = new EditorGUILayout.ScrollViewScope(_previewScrollPosition, GUILayout.Height(listHeight)))
                {
                    _previewScrollPosition = scroll.scrollPosition;
                    foreach (var previewEntry in previewEntries)
                    {
                        float entryHeight = Mathf.Max(PreviewIconSize, EditorGUIUtility.singleLineHeight);
                        Rect rowRect = EditorGUILayout.GetControlRect(false, entryHeight);
                        string entryKey = GetPreviewEntryKey(previewEntry);
                        bool isSelected = entryKey == _selectedPreviewEntryKey;
                        if (isSelected)
                            EditorGUI.DrawRect(rowRect, GetPreviewSelectedRowColor());

                        if (GUI.Button(rowRect, GUIContent.none, GUIStyle.none))
                        {
                            _selectedPreviewEntryKey = entryKey;
                            SelectPreviewEntryInProjectWindow(previewEntry, preferDestination: config.enabled);
                        }
                        EditorGUIUtility.AddCursorRect(rowRect, MouseCursor.Link);

                        Texture icon = GetPreviewIcon(previewEntry.SourceAssetPath, previewEntry.DestinationAssetPath);

                        float iconY = rowRect.y + ((rowRect.height - PreviewIconSize) * 0.5f);
                        var iconRect = new Rect(rowRect.x, iconY, PreviewIconSize, PreviewIconSize);
                        GUI.Label(iconRect, icon != null ? new GUIContent(icon) : GUIContent.none);

                        const float iconTextSpacing = 4f;
                        float textX = iconRect.xMax + iconTextSpacing;
                        float textHeight = EditorGUIUtility.singleLineHeight;
                        float textY = rowRect.y + ((rowRect.height - textHeight) * 0.5f);
                        var textRect = new Rect(textX, textY, Mathf.Max(0f, rowRect.xMax - textX), textHeight);
                        EditorGUI.LabelField(textRect, previewEntry.DestinationAssetPath, EditorStyles.miniLabel);
                    }
                }
            }
        }

        private static Texture GetPreviewIcon(string sourceAssetPath, string destinationAssetPath)
        {
            Texture icon = GetKnownPreviewIconFromPath(sourceAssetPath);
            if (icon != null)
                return icon;

            icon = GetKnownPreviewIconFromPath(destinationAssetPath);
            if (icon != null)
                return icon;

            icon = GetAssetIcon(sourceAssetPath);
            if (icon != null)
                return icon;

            icon = GetAssetIcon(destinationAssetPath);
            if (icon != null)
                return icon;

            return EditorGUIUtility.IconContent("DefaultAsset Icon")?.image;
        }

        private static Texture GetAssetIcon(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return null;

            if (!TryGetAssetDatabasePath(assetPath, out string assetDatabasePath))
                return null;

            var asset = AssetDatabase.LoadMainAssetAtPath(assetDatabasePath);
            Texture icon = null;
            if (asset != null)
            {
                icon = AssetPreview.GetMiniThumbnail(asset);
                if (icon == null)
                    icon = EditorGUIUtility.ObjectContent(asset, asset.GetType()).image;
            }

            icon ??= AssetDatabase.GetCachedIcon(assetDatabasePath);
            return icon;
        }

        private static Texture GetKnownPreviewIconFromPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            if (IsDirectoryPath(path))
                return GetEditorIcon("Folder Icon", "FolderEmpty Icon", "DefaultAsset Icon");

            string extension = GetNormalizedPathExtension(path);
            if (string.IsNullOrEmpty(extension))
                return null;

            switch (extension)
            {
                case ".cs":
                    return GetEditorIcon("cs Script Icon", "Script Icon", "TextAsset Icon");
                case ".js":
                    return GetEditorIcon("js Script Icon", "Script Icon", "TextAsset Icon");
                case ".shader":
                    return GetEditorIcon("Shader Icon", "TextAsset Icon");
                case ".mat":
                    return GetEditorIcon("Material Icon");
                case ".prefab":
                    return GetEditorIcon("Prefab Icon");
                case ".unity":
                    return GetEditorIcon("SceneAsset Icon");
                case ".anim":
                    return GetEditorIcon("AnimationClip Icon");
                case ".controller":
                    return GetEditorIcon("AnimatorController Icon");
                case ".txt":
                case ".md":
                case ".json":
                case ".xml":
                case ".csv":
                case ".bytes":
                case ".yaml":
                case ".yml":
                    return GetEditorIcon("TextAsset Icon");
                case ".png":
                case ".jpg":
                case ".jpeg":
                case ".tga":
                case ".psd":
                case ".gif":
                case ".bmp":
                case ".tif":
                case ".tiff":
                case ".exr":
                    return GetEditorIcon("Texture Icon");
                case ".wav":
                case ".mp3":
                case ".ogg":
                case ".aif":
                case ".aiff":
                    return GetEditorIcon("AudioClip Icon");
                default:
                    return null;
            }
        }

        private static Texture GetEditorIcon(params string[] iconNames)
        {
            if (iconNames == null)
                return null;

            foreach (string iconName in iconNames)
            {
                if (string.IsNullOrWhiteSpace(iconName))
                    continue;

                Texture icon = EditorGUIUtility.IconContent(iconName)?.image;
                if (icon != null)
                    return icon;
            }

            return null;
        }

        private static bool IsDirectoryPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            string normalizedPath = path.Replace('\\', '/');
            if (!Path.IsPathRooted(path))
            {
                if (AssetDatabase.IsValidFolder(normalizedPath))
                    return true;
                if (normalizedPath.EndsWith("/", StringComparison.Ordinal))
                    return true;
                return false;
            }

            try
            {
                string fullPath = Path.GetFullPath(path);
                if (Directory.Exists(fullPath))
                    return true;
                if (File.Exists(fullPath))
                    return false;
                return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                    || path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        private static string GetNormalizedPathExtension(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            string normalizedPath = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string extension = Path.GetExtension(normalizedPath);
            return string.IsNullOrEmpty(extension)
                ? string.Empty
                : extension.ToLowerInvariant();
        }

        private static string GetPreviewEntryKey(AssetSyncer.PreviewCopyEntry entry)
        {
            return $"{entry.SourceAssetPath}|{entry.DestinationAssetPath}";
        }

        private static Color GetPreviewSelectedRowColor()
        {
            return EditorGUIUtility.isProSkin
                ? new Color(0.24f, 0.49f, 0.90f, 0.35f)
                : new Color(0.24f, 0.49f, 0.90f, 0.22f);
        }

        private static void SelectPreviewEntryInProjectWindow(
            AssetSyncer.PreviewCopyEntry previewEntry,
            bool preferDestination)
        {
            string primaryPath = preferDestination
                ? previewEntry.DestinationAssetPath
                : previewEntry.SourceAssetPath;
            string secondaryPath = preferDestination
                ? previewEntry.SourceAssetPath
                : previewEntry.DestinationAssetPath;

            var asset = LoadAssetForProjectWindow(primaryPath);
            if (asset == null)
                asset = LoadAssetForProjectWindow(secondaryPath);
            if (asset == null)
                return;

            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
            EditorUtility.FocusProjectWindow();
        }

        private static UnityEngine.Object LoadAssetForProjectWindow(string path)
        {
            if (!TryGetAssetDatabasePath(path, out string assetDatabasePath))
                return null;

            return AssetDatabase.LoadMainAssetAtPath(assetDatabasePath);
        }

        private static bool TryGetAssetDatabasePath(string path, out string assetDatabasePath)
        {
            assetDatabasePath = string.Empty;
            if (string.IsNullOrWhiteSpace(path))
                return false;

            if (!Path.IsPathRooted(path))
            {
                assetDatabasePath = path.Replace('\\', '/');
                return true;
            }

            try
            {
                string normalizedPath = NormalizeFullPathForAssetDatabase(path);
                string projectRoot = NormalizeFullPathForAssetDatabase(Path.GetDirectoryName(Application.dataPath));
                if (!IsSubPathOrSame(projectRoot, normalizedPath))
                    return false;

                string relativePath = normalizedPath.Substring(projectRoot.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (string.IsNullOrEmpty(relativePath))
                    return false;

                assetDatabasePath = relativePath.Replace(Path.DirectorySeparatorChar, '/');
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string NormalizeFullPathForAssetDatabase(string path)
        {
            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static bool IsSubPathOrSame(string parentPath, string candidatePath)
        {
            if (string.Equals(parentPath, candidatePath, StringComparison.OrdinalIgnoreCase))
                return true;

            string prefix = parentPath + Path.DirectorySeparatorChar;
            return candidatePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private static bool CanPreviewCopyTargets(SyncConfig config, out string reason)
        {
            reason = null;
            if (config == null)
            {
                reason = "Config is not selected.";
                return false;
            }

            if (!IsFolderSelectionValid(config.sourcePath) || !IsDestinationFolderSelectionValid(config.destinationPath))
            {
                reason = "Set valid Source and Destination folders to show preview.";
                return false;
            }

            if (TryGetConfigWarningForActivation(config, out string warning))
            {
                reason = warning;
                return false;
            }

            return true;
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
            _filterLists.Clear();
            _ignoreLists.Clear();
            _sourceInputModes.Clear();

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
                || (config.syncRelativePaths != null && config.syncRelativePaths.Count > 0)
                || (config.syncRelativeDirectoryPaths != null && config.syncRelativeDirectoryPaths.Count > 0);
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
            if (!IsFolderSelectionValid(config.sourcePath) || !IsDestinationFolderSelectionValid(config.destinationPath))
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
            return AssetSyncer.IsSourceFolderPathValid(assetPath);
        }

        private static bool IsDestinationFolderSelectionValid(string assetPath)
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
                keepEmptyDirectories = config?.keepEmptyDirectories ?? false,
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

        private void DrawSourcePathField(SyncConfig config, bool sourceIsValid)
        {
            SourceInputMode currentMode = GetSourceInputMode(config);
            bool isExternalSource = currentMode == SourceInputMode.External;
            EditorGUI.BeginChangeCheck();
            bool nextIsExternalSource = EditorGUILayout.Toggle("External Source", isExternalSource);
            if (EditorGUI.EndChangeCheck())
            {
                SwitchSourceMode(config, nextIsExternalSource ? SourceInputMode.External : SourceInputMode.Internal);
                isExternalSource = nextIsExternalSource;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel("Source");

                Color previousBackgroundColor = GUI.backgroundColor;
                if (!sourceIsValid)
                    GUI.backgroundColor = Color.red;

                if (!isExternalSource)
                {
                    var currentSourceObject = string.IsNullOrEmpty(config.sourcePath)
                        ? null
                        : AssetDatabase.LoadAssetAtPath<DefaultAsset>(config.sourcePath);
                    var nextSourceObject = (DefaultAsset)EditorGUILayout.ObjectField(
                        currentSourceObject,
                        typeof(DefaultAsset),
                        false);
                    if (nextSourceObject != currentSourceObject)
                    {
                        string selectedPath = AssetDatabase.GetAssetPath(nextSourceObject);
                        if (!string.IsNullOrEmpty(selectedPath) && !AssetDatabase.IsValidFolder(selectedPath))
                        {
                            Debug.LogWarning("[AssetSync] Source must be a folder.");
                        }
                        else
                        {
                            SetSourcePath(config, selectedPath);
                        }
                    }
                }
                else
                {
                    EditorGUI.BeginChangeCheck();
                    string currentSourcePath = config.sourcePath ?? string.Empty;
                    string changedSourcePath = EditorGUILayout.DelayedTextField(currentSourcePath);
                    if (EditorGUI.EndChangeCheck())
                        SetSourcePath(config, NormalizeExternalSourcePathInput(changedSourcePath));

                    GUI.backgroundColor = previousBackgroundColor;
                    if (GUILayout.Button("Browse", GUILayout.Width(58f)))
                        BrowseExternalSourceFolder(config);
                    return;
                }

                GUI.backgroundColor = previousBackgroundColor;
            }
        }

        private void BrowseExternalSourceFolder(SyncConfig config)
        {
            string initialPath = GetInitialSourceFolderPickerPath(config?.sourcePath);
            string selectedPath = EditorUtility.OpenFolderPanel("Select Source Folder", initialPath, string.Empty);
            if (string.IsNullOrEmpty(selectedPath))
                return;

            SetSourcePath(config, NormalizeExternalSourcePathInput(selectedPath));
        }

        private void SetSourcePath(SyncConfig config, string nextSourcePath)
        {
            if (_settings == null || config == null)
                return;

            string normalizedNext = nextSourcePath ?? string.Empty;
            if (string.Equals(config.sourcePath ?? string.Empty, normalizedNext, StringComparison.Ordinal))
                return;

            Undo.RecordObject(_settings, "Set Source");
            config.sourcePath = normalizedNext;
            ApplyConfigChange(config);
        }

        private static string NormalizeExternalSourcePathInput(string sourcePathInput)
        {
            if (string.IsNullOrWhiteSpace(sourcePathInput))
                return string.Empty;

            string trimmedPath = sourcePathInput.Trim();

            try
            {
                return Path.GetFullPath(trimmedPath);
            }
            catch
            {
                return trimmedPath;
            }
        }

        private void SwitchSourceMode(SyncConfig config, SourceInputMode mode)
        {
            if (config == null)
                return;

            _sourceInputModes[config] = mode;
            string currentPath = config.sourcePath ?? string.Empty;
            if (mode == SourceInputMode.External)
            {
                if (AssetSyncer.IsProjectAssetFolderPath(currentPath))
                {
                    SetSourcePath(config, GetProjectAssetFolderFullPath(currentPath));
                    return;
                }

                SetSourcePath(config, NormalizeExternalSourcePathInput(currentPath));
                return;
            }

            if (AssetSyncer.TryConvertFullPathToProjectAssetPath(currentPath, out string projectAssetPath))
            {
                SetSourcePath(config, projectAssetPath);
                return;
            }

            SetSourcePath(config, string.Empty);
        }

        private SourceInputMode GetSourceInputMode(SyncConfig config)
        {
            if (config == null)
                return SourceInputMode.Internal;

            string sourcePath = config.sourcePath;
            if (AssetSyncer.IsProjectAssetFolderPath(sourcePath))
                return SourceInputMode.Internal;
            if (!string.IsNullOrWhiteSpace(sourcePath))
                return SourceInputMode.External;

            if (_sourceInputModes.TryGetValue(config, out SourceInputMode mode))
                return mode;

            return SourceInputMode.Internal;
        }

        private static string GetInitialSourceFolderPickerPath(string currentSourcePath)
        {
            if (string.IsNullOrWhiteSpace(currentSourcePath))
                return Path.GetDirectoryName(Application.dataPath);

            if (AssetSyncer.IsProjectAssetFolderPath(currentSourcePath))
                return GetProjectAssetFolderFullPath(currentSourcePath);

            if (AssetSyncer.IsExternalSourceDirectoryPath(currentSourcePath))
                return Path.GetFullPath(currentSourcePath);

            return Path.GetDirectoryName(Application.dataPath);
        }

        private static string GetProjectAssetFolderFullPath(string projectAssetPath)
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            return Path.GetFullPath(Path.Combine(projectRoot, projectAssetPath));
        }

        private static string NicifyTypeName(string assemblyQualifiedName)
        {
            if (string.IsNullOrEmpty(assemblyQualifiedName)) return "(None)";
            int comma = assemblyQualifiedName.IndexOf(',');
            string fullName = comma >= 0 ? assemblyQualifiedName.Substring(0, comma).Trim() : assemblyQualifiedName;
            int dot = fullName.LastIndexOf('.');
            return ObjectNames.NicifyVariableName(dot >= 0 ? fullName.Substring(dot + 1) : fullName);
        }

        private enum SourceInputMode
        {
            Internal = 0,
            External = 1
        }

        private static void EnsureConfigCollections(SyncConfig config)
        {
            config.filters ??= new List<FilterCondition>();
            config.ignoreGuids ??= new List<string>();
            config.syncRelativePaths ??= new List<string>();
            config.syncRelativeDirectoryPaths ??= new List<string>();
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
            _sourceInputModes.Clear();
        }
    }
}

