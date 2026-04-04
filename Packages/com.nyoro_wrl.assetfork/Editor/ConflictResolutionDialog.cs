using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Nyorowrl.Assetfork;

namespace Nyorowrl.Assetfork.Editor
{
    internal sealed class ConflictResolutionDialog : EditorWindow
    {
        private const float PreviewSize = 72f;
        private const float ActionSegmentWidth = 220f;
        private const float RowHeight = 86f;
        private const float HeaderHeight = 58f;
        private const float FooterHeight = 56f;
        private const float MinWindowHeight = 220f;
        private const float MaxWindowHeight = 620f;
        private const float MaxListHeight = 430f;
        private const float RowVerticalSpacing = 4f;
        private const float WindowPadding = 6f;
        private static readonly string[] ActionLabels = { "Overwrite", "Ignore" };
        private static GUIStyle _pathButtonStyle;
        private static readonly Dictionary<SyncConfig, Dictionary<string, AssetSyncer.ConflictResolution>> PendingDecisionsByConfig
            = new Dictionary<SyncConfig, Dictionary<string, AssetSyncer.ConflictResolution>>();
        private static readonly HashSet<SyncConfig> OpenConfigs = new HashSet<SyncConfig>();

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
        private SyncConfig _config;
        private bool _finalized;

        internal static bool ShowDialog(
            SyncConfig config,
            IReadOnlyList<AssetSyncer.SyncConflict> conflicts,
            out Dictionary<string, AssetSyncer.ConflictResolution> decisions)
        {
            if (TryConsumePendingDecisions(config, conflicts, out decisions))
                return true;

            decisions = null;
            if (config == null || conflicts == null || conflicts.Count == 0)
                return false;
            if (OpenConfigs.Contains(config))
                return false;

            var window = CreateInstance<ConflictResolutionDialog>();
            window.Initialize(config, conflicts);
            window.Show();
            window.Focus();
            return false;
        }

        private void Initialize(SyncConfig config, IReadOnlyList<AssetSyncer.SyncConflict> conflicts)
        {
            titleContent = new GUIContent("AssetFork Conflicts");
            float targetHeight = ComputeWindowHeight(conflicts?.Count ?? 0);
            minSize = new Vector2(760f, MinWindowHeight);
            maxSize = new Vector2(10000f, 10000f);
            float targetWidth = Mathf.Max(760f, position.width);
            position = new Rect(position.x, position.y, targetWidth, targetHeight);

            _configName = config?.configName ?? "(Unnamed)";
            _config = config;
            _rows.Clear();
            _decisions.Clear();
            _finalized = false;
            OpenConfigs.Add(config);

            foreach (var conflict in conflicts)
            {
                _rows.Add(new ConflictRow
                {
                    Conflict = conflict,
                    Resolution = AssetSyncer.ConflictResolution.Protected
                });
            }
        }

        private void OnDisable()
        {
            FinalizeAsApply();
        }

        private void OnGUI()
        {
            float width = position.width - (WindowPadding * 2f);
            float y = WindowPadding;
            float lineHeight = EditorGUIUtility.singleLineHeight;
            float applyButtonHeight = lineHeight * 2f;
            float footerBoxHeight = applyButtonHeight + 8f;

            EditorGUI.LabelField(
                new Rect(WindowPadding, y, width, lineHeight),
                $"Config: {_configName}",
                EditorStyles.boldLabel);
            y += lineHeight + 2f;

            EditorGUI.LabelField(
                new Rect(WindowPadding, y, width, lineHeight),
                "Destination files already exist. Choose Overwrite or Ignore for each conflict.");
            y += lineHeight + 4f;

            float footerTop = position.height - WindowPadding - footerBoxHeight;
            Rect listRect = new Rect(
                WindowPadding,
                y,
                width,
                Mathf.Max(0f, footerTop - y - 4f));
            DrawConflictList(listRect);

            Rect footerRect = new Rect(WindowPadding, footerTop, width, footerBoxHeight);
            GUI.Box(footerRect, GUIContent.none, EditorStyles.helpBox);
            Rect applyRect = new Rect(
                footerRect.x + 3f,
                footerRect.y + 4f,
                footerRect.width - 6f,
                applyButtonHeight);

            if (GUI.Button(applyRect, "Apply"))
            {
                FinalizeAsApply();
                Close();
                GUIUtility.ExitGUI();
            }
        }

        private void DrawConflictList(Rect listRect)
        {
            float rowPitch = RowHeight + RowVerticalSpacing;
            float viewWidth = Mathf.Max(0f, listRect.width - 16f);
            float contentHeight = Mathf.Max(listRect.height, _rows.Count * rowPitch);
            Rect viewRect = new Rect(0f, 0f, viewWidth, contentHeight);

            using (var scrollView = new GUI.ScrollViewScope(listRect, _scroll, viewRect))
            {
                _scroll = scrollView.scrollPosition;
                float y = 0f;
                foreach (var row in _rows)
                {
                    DrawConflictRow(row, new Rect(0f, y, viewWidth, RowHeight));
                    y += rowPitch;
                }
            }
        }

        private void DrawConflictRow(ConflictRow row, Rect rowRect)
        {
            GUI.Box(rowRect, GUIContent.none, EditorStyles.helpBox);
            Rect contentRect = new Rect(
                rowRect.x + 4f,
                rowRect.y + 3f,
                Mathf.Max(0f, rowRect.width - 8f),
                Mathf.Max(0f, rowRect.height - 6f));

            GUILayout.BeginArea(contentRect);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(PreviewSize)))
                {
                    GUILayout.FlexibleSpace();
                    DrawPreview(row);
                    GUILayout.FlexibleSpace();
                }

                using (new EditorGUILayout.VerticalScope())
                {
                    DrawAssetPathField("Source", row.Conflict.SourceAssetPath);
                    DrawAssetPathField("Destination", row.Conflict.DestinationAssetPath);
                    DrawActionButtons(row);
                }
            }
            GUILayout.EndArea();
        }

        private void DrawPreview(ConflictRow row)
        {
            UnityEngine.Object destinationAsset = AssetDatabase.LoadMainAssetAtPath(row.Conflict.DestinationAssetPath);
            Texture preview = AssetPreview.GetAssetPreview(destinationAsset);
            if (preview == null)
                preview = AssetPreview.GetMiniThumbnail(destinationAsset);

            Rect rect = GUILayoutUtility.GetRect(
                PreviewSize,
                PreviewSize,
                GUILayout.Width(PreviewSize),
                GUILayout.Height(PreviewSize));

            if (preview != null)
                GUI.DrawTexture(rect, preview, ScaleMode.ScaleToFit);
            else
                EditorGUI.LabelField(rect, "No Preview", EditorStyles.centeredGreyMiniLabel);

            GUI.Box(rect, GUIContent.none);
        }

        private void DrawActionButtons(ConflictRow row)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Action", GUILayout.Width(120f));
                int selected = row.Resolution == AssetSyncer.ConflictResolution.Owned ? 0 : 1;
                selected = GUILayout.Toolbar(selected, ActionLabels, GUILayout.Width(ActionSegmentWidth));
                row.Resolution = selected == 0
                    ? AssetSyncer.ConflictResolution.Owned
                    : AssetSyncer.ConflictResolution.Protected;
            }
        }

        private void DrawAssetPathField(string label, string assetPath)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, GUILayout.Width(120f));
                GUIStyle style = GetPathButtonStyle();
                if (GUILayout.Button(string.IsNullOrEmpty(assetPath) ? "(Unknown)" : assetPath, style))
                    SelectAssetInProject(assetPath);
            }
        }

        private static void SelectAssetInProject(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return;

            UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (asset == null)
                return;

            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        private static GUIStyle GetPathButtonStyle()
        {
            if (_pathButtonStyle != null)
                return _pathButtonStyle;

            _pathButtonStyle = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                stretchWidth = true
            };
            return _pathButtonStyle;
        }

        private void CommitDecisions()
        {
            _decisions.Clear();
            foreach (var row in _rows)
                _decisions[row.Conflict.NormalizedRelativePath] = row.Resolution;
        }

        private void FinalizeAsApply()
        {
            if (_finalized)
                return;

            _finalized = true;
            if (_config != null)
                OpenConfigs.Remove(_config);

            CommitDecisions();

            if (_config == null)
                return;

            PendingDecisionsByConfig[_config] = new Dictionary<string, AssetSyncer.ConflictResolution>(_decisions);
            var capturedConfig = _config;
            EditorApplication.delayCall += () => AssetSyncer.ResumeSyncAfterConflictDialog(capturedConfig);
        }

        private static bool TryConsumePendingDecisions(
            SyncConfig config,
            IReadOnlyList<AssetSyncer.SyncConflict> conflicts,
            out Dictionary<string, AssetSyncer.ConflictResolution> decisions)
        {
            decisions = null;
            if (config == null || conflicts == null || conflicts.Count == 0)
                return false;

            if (!PendingDecisionsByConfig.TryGetValue(config, out var pending))
                return false;

            foreach (var conflict in conflicts)
            {
                if (!pending.ContainsKey(conflict.NormalizedRelativePath))
                {
                    PendingDecisionsByConfig.Remove(config);
                    return false;
                }
            }

            PendingDecisionsByConfig.Remove(config);
            decisions = new Dictionary<string, AssetSyncer.ConflictResolution>(pending);
            return true;
        }

        private static float ComputeListHeight(int rowCount)
        {
            int effectiveRows = Mathf.Max(rowCount, 1);
            float rowsHeight = effectiveRows * (RowHeight + RowVerticalSpacing);
            return Mathf.Min(rowsHeight, MaxListHeight);
        }

        private static float ComputeWindowHeight(int rowCount)
        {
            float rawHeight = HeaderHeight + ComputeListHeight(rowCount) + FooterHeight;
            return Mathf.Clamp(rawHeight, MinWindowHeight, MaxWindowHeight);
        }

    }
}
