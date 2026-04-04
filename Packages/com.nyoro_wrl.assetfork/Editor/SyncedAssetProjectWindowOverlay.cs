using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Nyorowrl.Assetfork;

namespace Nyorowrl.Assetfork.Editor
{
    [InitializeOnLoad]
    internal static class SyncedAssetProjectWindowOverlay
    {
        private const double AutoRefreshIntervalSeconds = 2.0d;

        private static readonly string[] BadgeIconNames =
        {
            "d_RotateTool",
            "RotateTool",
            "d_Refresh",
            "Refresh",
            "d_PreMatQuad",
            "PreMatQuad"
        };

        private static readonly HashSet<string> SyncedAssetPaths = new HashSet<string>(StringComparer.Ordinal);
        private static readonly GUIContent BadgeIcon;

        private static bool _cacheDirty = true;
        private static double _nextAutoRefreshAt;

        static SyncedAssetProjectWindowOverlay()
        {
            BadgeIcon = ResolveBadgeIcon();

            EditorApplication.projectWindowItemOnGUI += OnProjectWindowItemGUI;
            EditorApplication.projectChanged += MarkCacheDirty;
            Undo.undoRedoPerformed += MarkCacheDirty;
            EditorApplication.update += OnEditorUpdate;
        }

        private static void OnEditorUpdate()
        {
            if (EditorApplication.timeSinceStartup >= _nextAutoRefreshAt)
            {
                _cacheDirty = true;
                _nextAutoRefreshAt = EditorApplication.timeSinceStartup + AutoRefreshIntervalSeconds;
            }

            if (_cacheDirty)
                RebuildSyncedPathCache();
        }

        private static void OnProjectWindowItemGUI(string guid, Rect rect)
        {
            if (_cacheDirty)
                RebuildSyncedPathCache();

            string assetPath = NormalizeAssetPath(AssetDatabase.GUIDToAssetPath(guid));
            if (string.IsNullOrEmpty(assetPath))
                return;

            if (!SyncedAssetPaths.Contains(assetPath))
                return;

            DrawBadge(rect);
        }

        private static void RebuildSyncedPathCache()
        {
            SyncedAssetPaths.Clear();

            string[] settingsGuids = AssetDatabase.FindAssets("t:AssetForkSettings");
            foreach (string settingsGuid in settingsGuids)
            {
                string settingsPath = AssetDatabase.GUIDToAssetPath(settingsGuid);
                var settings = AssetDatabase.LoadAssetAtPath<AssetForkSettings>(settingsPath);
                if (settings == null || settings.syncConfigs == null)
                    continue;

                foreach (SyncConfig config in settings.syncConfigs)
                {
                    if (config == null || !config.enabled)
                        continue;

                    string destinationRoot = NormalizeAssetPath(config.destinationPath);
                    if (string.IsNullOrEmpty(destinationRoot) || config.ownedRelativePaths == null)
                        continue;

                    foreach (string relativePath in config.ownedRelativePaths)
                    {
                        string normalizedRelativePath = AssetSyncer.NormalizeRelativePath(relativePath);
                        if (string.IsNullOrEmpty(normalizedRelativePath))
                            continue;

                        SyncedAssetPaths.Add(destinationRoot + "/" + normalizedRelativePath);
                    }
                }
            }

            _cacheDirty = false;
        }

        private static void DrawBadge(Rect itemRect)
        {
            float size = itemRect.height <= 20f ? 18f : 24f;
            Rect badgeRect = GetBadgeRect(itemRect, size);

            EditorGUI.DrawRect(badgeRect, new Color(0.08f, 0.08f, 0.08f, 0.96f));

            if (BadgeIcon != null && BadgeIcon.image != null)
            {
                float iconInset = Mathf.Max(2f, badgeRect.width * 0.18f);
                Rect iconRect = new Rect(
                    badgeRect.x + iconInset,
                    badgeRect.y + iconInset,
                    badgeRect.width - (iconInset * 2f),
                    badgeRect.height - (iconInset * 2f));
                Color previousColor = GUI.color;
                GUI.color = Color.white;
                GUI.DrawTexture(iconRect, BadgeIcon.image, ScaleMode.ScaleToFit, true);
                GUI.color = previousColor;
                return;
            }

            float inset = Mathf.Max(2f, badgeRect.width * 0.22f);
            var innerRect = new Rect(
                badgeRect.x + inset,
                badgeRect.y + inset,
                Mathf.Max(1f, badgeRect.width - (inset * 2f)),
                Mathf.Max(1f, badgeRect.height - (inset * 2f)));
            EditorGUI.DrawRect(innerRect, Color.white);
        }

        private static Rect GetBadgeRect(Rect itemRect, float size)
        {
            if (itemRect.height <= 20f)
            {
                float iconX = itemRect.x + 1f;
                float iconY = itemRect.y + Mathf.Max(0f, (itemRect.height - 16f) * 0.5f);
                return new Rect(iconX + 16f - (size * 0.85f), iconY + 16f - (size * 0.85f), size, size);
            }

            float iconAreaSize = Mathf.Min(itemRect.width, itemRect.height);
            return new Rect(itemRect.x + iconAreaSize - size - 1f, itemRect.y + iconAreaSize - size - 1f, size, size);
        }

        private static GUIContent ResolveBadgeIcon()
        {
            foreach (string iconName in BadgeIconNames)
            {
                GUIContent content = EditorGUIUtility.IconContent(iconName);
                if (content != null && content.image != null)
                    return content;
            }

            return null;
        }

        private static void MarkCacheDirty()
        {
            _cacheDirty = true;
        }

        private static string NormalizeAssetPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            return path.Replace('\\', '/').TrimEnd('/');
        }
    }
}
