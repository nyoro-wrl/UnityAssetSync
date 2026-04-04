using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditorInternal;
using UnityEngine;
using Nyorowrl.Assetfork;

namespace Nyorowrl.Assetfork.Editor
{
    public class AssetForkWindow : EditorWindow
    {
        private const string SettingsPathPrefKey = "AssetFork.SettingsPath";

        private AssetForkSettings _settings;
        private Vector2 _detailScrollPosition;
        private float _listPanelWidth = 150f;
        private bool _isResizing;

        [SerializeField] private TreeViewState<int> _treeViewState;
        private ConfigTreeView _configTreeView;

        private readonly Dictionary<FilterCondition, ReorderableList> _typesLists =
            new Dictionary<FilterCondition, ReorderableList>();
        private readonly Dictionary<SyncConfig, ReorderableList> _protectedLists =
            new Dictionary<SyncConfig, ReorderableList>();

        private int SelectedConfigIndex => _configTreeView?.SelectedIndex ?? -1;

        [MenuItem("Window/AssetFork")]
        public static void Open()
        {
            GetWindow<AssetForkWindow>("AssetFork");
        }

        private void OnEnable()
        {
            minSize = new Vector2(480, 320);

            if (_treeViewState == null)
                _treeViewState = new TreeViewState<int>();

            string savedPath = EditorPrefs.GetString(SettingsPathPrefKey, "");
            if (!string.IsNullOrEmpty(savedPath))
                _settings = AssetDatabase.LoadAssetAtPath<AssetForkSettings>(savedPath);

            RebuildTreeView();
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
                onAddRequested: AddConfig
            );
        }

        private void OnGUI()
        {
            DrawSettingsField();

            if (_settings == null)
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox("Settings を選択するか New ボタンで新しい設定ファイルを作成してください。", MessageType.Info);
                return;
            }

            EditorGUILayout.Space();

            EditorGUILayout.BeginHorizontal();
            DrawConfigList();
            DrawResizeHandle();
            DrawConfigDetail();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSettingsField()
        {
            EditorGUILayout.BeginHorizontal();

            EditorGUI.BeginChangeCheck();
            _settings = (AssetForkSettings)EditorGUILayout.ObjectField("Settings", _settings, typeof(AssetForkSettings), false);
            if (EditorGUI.EndChangeCheck())
            {
                _typesLists.Clear();
                _protectedLists.Clear();
                string path = _settings != null ? AssetDatabase.GetAssetPath(_settings) : "";
                EditorPrefs.SetString(SettingsPathPrefKey, path);
                RebuildTreeView();
            }

            if (GUILayout.Button("New", GUILayout.Width(50)))
            {
                string path = EditorUtility.SaveFilePanelInProject(
                    "Create AssetFork Settings",
                    "AssetForkSettings",
                    "asset",
                    "Create a new AssetFork settings file");

                if (!string.IsNullOrEmpty(path))
                {
                    var newSettings = CreateInstance<AssetForkSettings>();
                    AssetDatabase.CreateAsset(newSettings, path);
                    AssetDatabase.SaveAssets();
                    _settings = newSettings;
                    _typesLists.Clear();
                    _protectedLists.Clear();
                    EditorPrefs.SetString(SettingsPathPrefKey, path);
                    RebuildTreeView();
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawConfigList()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(_listPanelWidth));

            Rect treeRect = GUILayoutUtility.GetRect(
                _listPanelWidth, _listPanelWidth,
                0, float.MaxValue,
                GUILayout.ExpandHeight(true));
            _configTreeView.OnGUI(treeRect);

            EditorGUILayout.EndVertical();
        }

        private void AddConfig()
        {
            Undo.RecordObject(_settings, "Add Config");
            _settings.syncConfigs.Add(new SyncConfig
            {
                configName = $"Config {_settings.syncConfigs.Count + 1}"
            });
            EditorUtility.SetDirty(_settings);
            int newIndex = _settings.syncConfigs.Count - 1;
            _configTreeView.Reload();
            _configTreeView.SelectAndBeginRename(newIndex);
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
            EditorGUILayout.BeginVertical();
            _detailScrollPosition = EditorGUILayout.BeginScrollView(_detailScrollPosition);

            int idx = SelectedConfigIndex;
            if (idx < 0 || idx >= _settings.syncConfigs.Count)
            {
                GUILayout.Label("リストから Config を選択してください。", EditorStyles.centeredGreyMiniLabel);
                EditorGUILayout.EndScrollView();
                EditorGUILayout.EndVertical();
                return;
            }

            var config = _settings.syncConfigs[idx];

            EditorGUI.BeginChangeCheck();
            bool newEnabled = EditorGUILayout.Toggle("Enable", config.enabled);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_settings, "Toggle Config");
                config.enabled = newEnabled;
                ApplyConfigChange(config);
            }

            var srcObj = string.IsNullOrEmpty(config.sourcePath)
                ? null : AssetDatabase.LoadAssetAtPath<DefaultAsset>(config.sourcePath);
            var newSrcObj = (DefaultAsset)EditorGUILayout.ObjectField("Source", srcObj, typeof(DefaultAsset), false);
            if (newSrcObj != srcObj)
            {
                string selectedPath = AssetDatabase.GetAssetPath(newSrcObj);
                if (!string.IsNullOrEmpty(selectedPath) && !AssetDatabase.IsValidFolder(selectedPath))
                {
                    Debug.LogWarning("[AssetFork] Source must be a folder.");
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
            var newDstObj = (DefaultAsset)EditorGUILayout.ObjectField("Destination", dstObj, typeof(DefaultAsset), false);
            if (newDstObj != dstObj)
            {
                string selectedPath = AssetDatabase.GetAssetPath(newDstObj);
                if (!string.IsNullOrEmpty(selectedPath) && !AssetDatabase.IsValidFolder(selectedPath))
                {
                    Debug.LogWarning("[AssetFork] Destination must be a folder.");
                }
                else
                {
                    Undo.RecordObject(_settings, "Set Destination");
                    config.destinationPath = selectedPath;
                    ApplyConfigChange(config);
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

            if (AssetSyncer.TryGetConfigWarning(config, out string warning))
            {
                EditorGUILayout.HelpBox(warning, MessageType.Warning);
            }

            EditorGUILayout.Space();
            DrawFilterList(config);
            EditorGUILayout.Space();
            DrawProtectedList(config);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawFilterList(SyncConfig config)
        {
            EditorGUILayout.BeginHorizontal();
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
            EditorGUILayout.EndHorizontal();

            int deleteIndex = -1;
            for (int i = 0; i < config.filters.Count; i++)
                DrawFilterCondition(config.filters[i], i, ref deleteIndex, config);

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
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
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
            EditorGUILayout.EndHorizontal();

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

            EditorGUILayout.EndVertical();
        }

        private void DrawProtectedList(SyncConfig config)
        {
            config.protectedGuids ??= new List<string>();
            GetOrCreateProtectedList(config).DoLayoutList();
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

        private ReorderableList GetOrCreateProtectedList(SyncConfig config)
        {
            if (_protectedLists.TryGetValue(config, out var existing))
                return existing;

            config.protectedGuids ??= new List<string>();

            var list = new ReorderableList(config.protectedGuids, typeof(string),
                draggable: true, displayHeader: true, displayAddButton: true, displayRemoveButton: true);

            list.drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Protected Assets");
            list.elementHeight = EditorGUIUtility.singleLineHeight + 2;

            list.drawElementCallback = (rect, index, isActive, isFocused) =>
            {
                if (index < 0 || index >= config.protectedGuids.Count)
                    return;

                string guid = config.protectedGuids[index];
                string path = AssetDatabase.GUIDToAssetPath(guid);
                UnityEngine.Object current = string.IsNullOrEmpty(path)
                    ? null
                    : AssetDatabase.LoadMainAssetAtPath(path);

                var fieldRect = new Rect(rect.x, rect.y + 1, rect.width, EditorGUIUtility.singleLineHeight);
                EditorGUI.BeginChangeCheck();
                var next = EditorGUI.ObjectField(fieldRect, $"Element {index}", current, typeof(UnityEngine.Object), false);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(_settings, "Change Protected Asset");
                    string nextPath = AssetDatabase.GetAssetPath(next);
                    config.protectedGuids[index] = string.IsNullOrEmpty(nextPath)
                        ? string.Empty
                        : AssetDatabase.AssetPathToGUID(nextPath);
                    ApplyConfigChange(config);
                }
            };

            list.onAddCallback = _ =>
            {
                Undo.RecordObject(_settings, "Add Protected Asset");
                config.protectedGuids.Add(string.Empty);
                ApplyConfigChange(config);
            };

            list.onRemoveCallback = _ =>
            {
                if (list.index < 0 || list.index >= config.protectedGuids.Count)
                    return;

                Undo.RecordObject(_settings, "Delete Protected Asset");
                config.protectedGuids.RemoveAt(list.index);
                ApplyConfigChange(config);
            };

            _protectedLists[config] = list;
            return list;
        }

        private void DeleteConfig(int idx)
        {
            if (_settings == null || idx < 0 || idx >= _settings.syncConfigs.Count) return;

            Undo.RecordObject(_settings, "Delete Config");
            _settings.syncConfigs.RemoveAt(idx);
            EditorUtility.SetDirty(_settings);
            _typesLists.Clear();
            _protectedLists.Clear();

            _configTreeView.Reload();
            int next = Mathf.Clamp(idx - 1, 0, _settings.syncConfigs.Count - 1);
            if (_settings.syncConfigs.Count > 0)
                _configTreeView.SetSelection(new[] { next }, TreeViewSelectionOptions.RevealAndFrame);
            else
                _configTreeView.SetSelection(new List<int>());
        }

        private void ApplyConfigChange(SyncConfig config)
        {
            EditorUtility.SetDirty(_settings);
            if (string.IsNullOrEmpty(config.sourcePath) || string.IsNullOrEmpty(config.destinationPath))
                return;
            AssetSyncer.SyncConfig(config);
        }

        private static string NicifyTypeName(string assemblyQualifiedName)
        {
            if (string.IsNullOrEmpty(assemblyQualifiedName)) return "(None)";
            int comma = assemblyQualifiedName.IndexOf(',');
            string fullName = comma >= 0 ? assemblyQualifiedName.Substring(0, comma).Trim() : assemblyQualifiedName;
            int dot = fullName.LastIndexOf('.');
            return ObjectNames.NicifyVariableName(dot >= 0 ? fullName.Substring(dot + 1) : fullName);
        }
    }
}
