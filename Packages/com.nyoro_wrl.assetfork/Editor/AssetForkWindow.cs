using System.IO;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using Nyorowrl.Assetfork;

namespace Nyorowrl.Assetfork.Editor
{
    public class AssetForkWindow : EditorWindow
    {
        private AssetForkSettings _settings;
        private Vector2 _scrollPosition;
        private int _selectedConfigIndex = -1;

        [MenuItem("Window/AssetFork")]
        public static void Open()
        {
            GetWindow<AssetForkWindow>("AssetFork");
        }

        private void OnEnable()
        {
            _settings = LoadOrCreateSettings();
        }

        private void OnGUI()
        {
            if (_settings == null)
            {
                _settings = LoadOrCreateSettings();
                return;
            }

            DrawToolbar();
            EditorGUILayout.Space();

            EditorGUILayout.BeginHorizontal();
            DrawConfigList();
            DrawConfigDetail();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button("Add Config", EditorStyles.toolbarButton, GUILayout.Width(80)))
            {
                _settings.syncConfigs.Add(new SyncConfig
                {
                    configName = $"Config {_settings.syncConfigs.Count + 1}"
                });
                _selectedConfigIndex = _settings.syncConfigs.Count - 1;
                MarkSettingsDirty();
            }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Sync All", EditorStyles.toolbarButton, GUILayout.Width(70)))
                AssetSyncer.SyncAll(_settings);

            EditorGUILayout.EndHorizontal();
        }

        private void DrawConfigList()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(150));
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            for (int i = 0; i < _settings.syncConfigs.Count; i++)
            {
                var config = _settings.syncConfigs[i];
                string label = string.IsNullOrEmpty(config.configName) ? $"Config {i + 1}" : config.configName;

                EditorGUILayout.BeginHorizontal();

                EditorGUI.BeginChangeCheck();
                config.enabled = EditorGUILayout.Toggle(config.enabled, GUILayout.Width(16));
                if (EditorGUI.EndChangeCheck())
                    ApplyConfigChange(config);

                bool selected = _selectedConfigIndex == i;
                GUI.enabled = config.enabled;
                bool newSelected = GUILayout.Toggle(selected, label, "Button");
                GUI.enabled = true;
                if (newSelected && !selected)
                    _selectedConfigIndex = i;

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawConfigDetail()
        {
            EditorGUILayout.BeginVertical();

            if (_selectedConfigIndex < 0 || _selectedConfigIndex >= _settings.syncConfigs.Count)
            {
                GUILayout.Label("Select a config from the list.", EditorStyles.centeredGreyMiniLabel);
                EditorGUILayout.EndVertical();
                return;
            }

            var config = _settings.syncConfigs[_selectedConfigIndex];

            // configName: ラベル変更のみ、同期は不要
            EditorGUI.BeginChangeCheck();
            config.configName = EditorGUILayout.TextField("Name", config.configName);
            if (EditorGUI.EndChangeCheck())
                MarkSettingsDirty();

            // Source / Destination: 変更時に同期
            var srcObj = string.IsNullOrEmpty(config.sourcePath)
                ? null : AssetDatabase.LoadAssetAtPath<DefaultAsset>(config.sourcePath);
            var newSrcObj = (DefaultAsset)EditorGUILayout.ObjectField("Source", srcObj, typeof(DefaultAsset), false);
            if (newSrcObj != srcObj)
            {
                config.sourcePath = AssetDatabase.GetAssetPath(newSrcObj);
                ApplyConfigChange(config);
            }

            var dstObj = string.IsNullOrEmpty(config.destinationPath)
                ? null : AssetDatabase.LoadAssetAtPath<DefaultAsset>(config.destinationPath);
            var newDstObj = (DefaultAsset)EditorGUILayout.ObjectField("Destination", dstObj, typeof(DefaultAsset), false);
            if (newDstObj != dstObj)
            {
                config.destinationPath = AssetDatabase.GetAssetPath(newDstObj);
                ApplyConfigChange(config);
            }

            EditorGUILayout.Space();
            DrawFilterList(config);
            EditorGUILayout.Space();

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Sync This Config"))
                AssetSyncer.SyncConfig(config);

            if (GUILayout.Button("Delete Config"))
            {
                _settings.syncConfigs.RemoveAt(_selectedConfigIndex);
                _selectedConfigIndex = Mathf.Clamp(_selectedConfigIndex - 1, -1, _settings.syncConfigs.Count - 1);
                MarkSettingsDirty();
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawFilterList(SyncConfig config)
        {
            EditorGUILayout.LabelField("Filters", EditorStyles.boldLabel);

            int deleteIndex = -1;
            for (int i = 0; i < config.filters.Count; i++)
                DrawFilterCondition(config.filters[i], i, ref deleteIndex, config);

            if (deleteIndex >= 0)
            {
                config.filters.RemoveAt(deleteIndex);
                ApplyConfigChange(config);
            }

            if (GUILayout.Button("Add Filter"))
            {
                config.filters.Add(new FilterCondition());
                ApplyConfigChange(config);
            }
        }

        private void DrawFilterCondition(FilterCondition filter, int index, ref int deleteIndex, SyncConfig config)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.LabelField($"Filter {index}", EditorStyles.miniBoldLabel);

            if (GUILayout.Button("Delete", GUILayout.Width(55)))
                deleteIndex = index;

            EditorGUILayout.EndHorizontal();

            EditorGUI.BeginChangeCheck();

            filter.invert = EditorGUILayout.Toggle("Invert", filter.invert);
            filter.useMultipleTypes = EditorGUILayout.Toggle("Multiple Types", filter.useMultipleTypes);

            if (EditorGUI.EndChangeCheck())
                ApplyConfigChange(config);

            if (filter.useMultipleTypes)
            {
                EditorGUILayout.LabelField("Types:", EditorStyles.miniLabel);
                EditorGUI.indentLevel++;

                int removeTypeIndex = -1;
                for (int i = 0; i < filter.multipleTypeNames.Count; i++)
                {
                    EditorGUILayout.BeginHorizontal();
                    string displayName = NicifyTypeName(filter.multipleTypeNames[i]);
                    EditorGUILayout.LabelField(displayName, GUILayout.ExpandWidth(true));
                    int captured = i;
                    if (GUILayout.Button("...", GUILayout.Width(25)))
                    {
                        var dropdown = new TypeSelectorDropdown(new AdvancedDropdownState(),
                            n => { filter.multipleTypeNames[captured] = n; ApplyConfigChange(config); });
                        dropdown.Show(GUILayoutUtility.GetLastRect());
                    }
                    if (GUILayout.Button("x", GUILayout.Width(20)))
                        removeTypeIndex = i;
                    EditorGUILayout.EndHorizontal();
                }
                if (removeTypeIndex >= 0)
                {
                    filter.multipleTypeNames.RemoveAt(removeTypeIndex);
                    ApplyConfigChange(config);
                }

                if (GUILayout.Button("Add Type"))
                {
                    filter.multipleTypeNames.Add("");
                    ApplyConfigChange(config);
                }

                EditorGUI.indentLevel--;
            }
            else
            {
                EditorGUILayout.BeginHorizontal();
                string display = NicifyTypeName(filter.singleTypeName);
                EditorGUILayout.LabelField("Type", display);
                if (GUILayout.Button("Select", GUILayout.Width(55)))
                {
                    var dropdown = new TypeSelectorDropdown(new AdvancedDropdownState(),
                        n => { filter.singleTypeName = n; ApplyConfigChange(config); });
                    dropdown.Show(GUILayoutUtility.GetLastRect());
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
        }

        private void MarkSettingsDirty()
        {
            EditorUtility.SetDirty(_settings);
        }

        private void ApplyConfigChange(SyncConfig config)
        {
            MarkSettingsDirty();
            AssetSyncer.SyncConfig(config);
        }

        private static string NicifyTypeName(string assemblyQualifiedName)
        {
            if (string.IsNullOrEmpty(assemblyQualifiedName))
                return "(None)";
            int comma = assemblyQualifiedName.IndexOf(',');
            string fullName = comma >= 0 ? assemblyQualifiedName.Substring(0, comma).Trim() : assemblyQualifiedName;
            int dot = fullName.LastIndexOf('.');
            return ObjectNames.NicifyVariableName(dot >= 0 ? fullName.Substring(dot + 1) : fullName);
        }

        private static AssetForkSettings LoadOrCreateSettings()
        {
            var settings = Resources.Load<AssetForkSettings>(AssetForkSettings.ResourcesPath);
            if (settings != null)
                return settings;

            const string dirPath = "Assets/Resources/AssetFork";
            if (!Directory.Exists(dirPath))
                Directory.CreateDirectory(dirPath);

            settings = CreateInstance<AssetForkSettings>();
            AssetDatabase.CreateAsset(settings, dirPath + "/AssetForkSettings.asset");
            AssetDatabase.SaveAssets();
            return settings;
        }
    }
}
