using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Nyorowrl.AssetSync;

namespace Nyorowrl.AssetSync.Editor
{
    [InitializeOnLoad]
    internal static class SyncedAssetProjectWindowOverlay
    {
        private const double AutoRefreshIntervalSeconds = 2.0d;
        private const string CustomBadgeIconPath = "Packages/com.nyoro_wrl.assetsync/Editor/Icons/icon.png";
        private const float SmallRowBadgeSize = 10f;
        private const float LargeItemBadgeSize = 24f;

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

            string[] settingsGuids = AssetDatabase.FindAssets("t:AssetSyncSettings");
            foreach (string settingsGuid in settingsGuids)
            {
                string settingsPath = AssetDatabase.GUIDToAssetPath(settingsGuid);
                var settings = AssetDatabase.LoadAssetAtPath<AssetSyncSettings>(settingsPath);
                if (settings == null || settings.syncConfigs == null)
                    continue;

                foreach (SyncConfig config in settings.syncConfigs)
                {
                    if (config == null || !config.enabled)
                        continue;

                    string destinationRoot = NormalizeAssetPath(config.destinationPath);
                    if (string.IsNullOrEmpty(destinationRoot) || config.syncRelativePaths == null)
                        continue;

                    HashSet<string> syncedSyncRelativePaths = AssetSyncer.CollectSyncedDestinationSyncRelativePaths(config);
                    foreach (string normalizedRelativePath in syncedSyncRelativePaths)
                    {
                        AddManagedDestinationPathWithParentFolders(destinationRoot, normalizedRelativePath);
                    }
                }
            }

            _cacheDirty = false;
        }

        private static void DrawBadge(Rect itemRect)
        {
            float size = itemRect.height <= 20f ? SmallRowBadgeSize : LargeItemBadgeSize;
            Rect badgeRect = GetBadgeRect(itemRect, size);

            if (_customBadgeIcon == null)
                _customBadgeIcon = ResolveCustomBadgeIcon();

            if (_customBadgeIcon == null)
                return;

            GUI.DrawTexture(badgeRect, _customBadgeIcon, ScaleMode.ScaleToFit, true);
        }

        private static void AddManagedDestinationPathWithParentFolders(string destinationRoot, string normalizedRelativePath)
        {
            if (string.IsNullOrEmpty(destinationRoot) || string.IsNullOrEmpty(normalizedRelativePath))
                return;

            SyncedAssetPaths.Add(NormalizeAssetPath(destinationRoot + "/" + normalizedRelativePath));

            int separatorIndex = normalizedRelativePath.LastIndexOf('/');
            while (separatorIndex >= 0)
            {
                string folderRelativePath = normalizedRelativePath.Substring(0, separatorIndex);
                if (string.IsNullOrEmpty(folderRelativePath))
                    break;

                SyncedAssetPaths.Add(NormalizeAssetPath(destinationRoot + "/" + folderRelativePath));
                separatorIndex = folderRelativePath.LastIndexOf('/');
            }
        }

        private static Rect GetBadgeRect(Rect itemRect, float size)
        {
            if (itemRect.height <= 20f)
            {
                float iconX = itemRect.x + 1f;
                float iconY = itemRect.y + Mathf.Max(0f, (itemRect.height - 16f) * 0.5f);
                return new Rect(iconX + 16f - size, iconY + 16f - size, size, size);
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
