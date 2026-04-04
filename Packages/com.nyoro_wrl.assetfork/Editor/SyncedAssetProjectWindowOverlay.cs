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
        private const string CustomBadgeIconPath = "Packages/com.nyoro_wrl.assetfork/Editor/Icons/icon.png";

        private static readonly HashSet<string> SyncedAssetPaths = new HashSet<string>(StringComparer.Ordinal);

        private static bool _cacheDirty = true;
        private static double _nextAutoRefreshAt;
        private static Texture2D _customBadgeIcon;

        static SyncedAssetProjectWindowOverlay()
        {
            _customBadgeIcon = ResolveCustomBadgeIcon();

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

                    HashSet<string> syncedOwnedRelativePaths = AssetSyncer.CollectSyncedDestinationOwnedRelativePaths(config);
                    foreach (string normalizedRelativePath in syncedOwnedRelativePaths)
                    {
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

            if (_customBadgeIcon == null)
                _customBadgeIcon = ResolveCustomBadgeIcon();

            if (_customBadgeIcon == null)
                return;

            GUI.DrawTexture(badgeRect, _customBadgeIcon, ScaleMode.ScaleToFit, true);
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

        private static Texture2D ResolveCustomBadgeIcon()
        {
            return AssetDatabase.LoadAssetAtPath<Texture2D>(CustomBadgeIconPath);
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
