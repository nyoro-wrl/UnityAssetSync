using System.IO;
using UnityEditor;
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

                bool selected = _selectedConfigIndex == i;
                bool newSelected = GUILayout.Toggle(selected, label, "Button");
                if (newSelected && !selected)
                    _selectedConfigIndex = i;
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

            EditorGUI.BeginChangeCheck();

            config.configName = EditorGUILayout.TextField("Name", config.configName);

            var srcObj = string.IsNullOrEmpty(config.sourcePath)
                ? null : AssetDatabase.LoadAssetAtPath<DefaultAsset>(config.sourcePath);
            var newSrcObj = (DefaultAsset)EditorGUILayout.ObjectField("Source", srcObj, typeof(DefaultAsset), false);
            if (newSrcObj != srcObj)
                config.sourcePath = AssetDatabase.GetAssetPath(newSrcObj);

            var dstObj = string.IsNullOrEmpty(config.destinationPath)
                ? null : AssetDatabase.LoadAssetAtPath<DefaultAsset>(config.destinationPath);
            var newDstObj = (DefaultAsset)EditorGUILayout.ObjectField("Destination", dstObj, typeof(DefaultAsset), false);
            if (newDstObj != dstObj)
                config.destinationPath = AssetDatabase.GetAssetPath(newDstObj);

            if (EditorGUI.EndChangeCheck())
                MarkSettingsDirty();

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
                DrawFilterCondition(config.filters[i], i, ref deleteIndex);

            if (deleteIndex >= 0)
            {
                config.filters.RemoveAt(deleteIndex);
                MarkSettingsDirty();
            }

            if (GUILayout.Button("Add Filter"))
            {
                config.filters.Add(new FilterCondition());
                MarkSettingsDirty();
            }
        }

        private void DrawFilterCondition(FilterCondition filter, int index, ref int deleteIndex)
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

            if (filter.useMultipleTypes)
            {
                EditorGUILayout.LabelField("Types:", EditorStyles.miniLabel);
                EditorGUI.indentLevel++;

                for (int i = 0; i < filter.multipleTypeNames.Count; i++)
                {
                    EditorGUILayout.BeginHorizontal();
                    filter.multipleTypeNames[i] = EditorGUILayout.TextField(filter.multipleTypeNames[i]);
                    if (GUILayout.Button("x", GUILayout.Width(20)))
                    {
                        filter.multipleTypeNames.RemoveAt(i);
                        break;
                    }
                    EditorGUILayout.EndHorizontal();
                }

                if (GUILayout.Button("Add Type"))
                    filter.multipleTypeNames.Add("");

                EditorGUI.indentLevel--;
            }
            else
            {
                filter.singleTypeName = EditorGUILayout.TextField("Type", filter.singleTypeName);
            }

            if (EditorGUI.EndChangeCheck())
                MarkSettingsDirty();

            EditorGUILayout.EndVertical();
        }

        private void MarkSettingsDirty()
        {
            EditorUtility.SetDirty(_settings);
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
