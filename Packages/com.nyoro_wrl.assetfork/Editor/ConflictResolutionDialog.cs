using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Nyorowrl.Assetfork;

namespace Nyorowrl.Assetfork.Editor
{
    internal sealed class ConflictResolutionDialog : EditorWindow
    {
        private sealed class ConflictRow
        {
            public AssetSyncer.SyncConflict Conflict;
            public AssetSyncer.ConflictResolution Resolution;
        }

        private readonly List<ConflictRow> _rows = new List<ConflictRow>();
        private readonly Dictionary<string, AssetSyncer.ConflictResolution> _decisions
            = new Dictionary<string, AssetSyncer.ConflictResolution>();

        private Vector2 _scroll;
        private string _configName;
        private bool _confirmed;

        internal static bool ShowDialog(
            SyncConfig config,
            IReadOnlyList<AssetSyncer.SyncConflict> conflicts,
            out Dictionary<string, AssetSyncer.ConflictResolution> decisions)
        {
            var window = CreateInstance<ConflictResolutionDialog>();
            window.Initialize(config, conflicts);
            window.ShowModal();

            if (window._confirmed)
            {
                decisions = new Dictionary<string, AssetSyncer.ConflictResolution>(window._decisions);
                return true;
            }

            decisions = null;
            return false;
        }

        private void Initialize(SyncConfig config, IReadOnlyList<AssetSyncer.SyncConflict> conflicts)
        {
            titleContent = new GUIContent("AssetFork Conflicts");
            minSize = new Vector2(720f, 420f);

            _configName = config?.configName ?? "(Unnamed)";
            _rows.Clear();
            _decisions.Clear();

            foreach (var conflict in conflicts)
            {
                _rows.Add(new ConflictRow
                {
                    Conflict = conflict,
                    Resolution = AssetSyncer.ConflictResolution.Protected
                });
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                $"Config '{_configName}' has existing destination files not managed by AssetFork.\nChoose how to resolve each conflict.",
                MessageType.Warning);

            EditorGUILayout.Space(4f);
            using (var scrollView = new EditorGUILayout.ScrollViewScope(_scroll))
            {
                _scroll = scrollView.scrollPosition;
                foreach (var row in _rows)
                {
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        EditorGUILayout.LabelField("Path", row.Conflict.NormalizedRelativePath, EditorStyles.boldLabel);
                        EditorGUILayout.LabelField("Source", row.Conflict.SourceAssetPath);
                        EditorGUILayout.LabelField("Destination", row.Conflict.DestinationAssetPath);
                        row.Resolution = (AssetSyncer.ConflictResolution)EditorGUILayout.EnumPopup("Action", row.Resolution);
                    }
                }
            }

            GUILayout.FlexibleSpace();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Cancel"))
                {
                    _confirmed = false;
                    Close();
                    GUIUtility.ExitGUI();
                }

                if (GUILayout.Button("Apply"))
                {
                    _decisions.Clear();
                    foreach (var row in _rows)
                        _decisions[row.Conflict.NormalizedRelativePath] = row.Resolution;

                    _confirmed = true;
                    Close();
                    GUIUtility.ExitGUI();
                }
            }
        }
    }
}
