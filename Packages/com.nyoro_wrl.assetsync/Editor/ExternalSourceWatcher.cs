using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Nyorowrl.AssetSync.Editor
{
    [InitializeOnLoad]
    internal static class ExternalSourceWatcher
    {
        private static readonly Dictionary<string, FileSystemWatcher> _watchers
            = new Dictionary<string, FileSystemWatcher>(StringComparer.OrdinalIgnoreCase);

        private static readonly ConcurrentDictionary<string, DateTime> _pendingPaths
            = new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        private const double DebounceSeconds = 0.5;

        static ExternalSourceWatcher()
        {
            EditorApplication.update += OnUpdate;
            EditorApplication.delayCall += RebuildWatchers;
        }

        internal static void RebuildWatchers()
        {
            HashSet<string> neededPaths = CollectExternalSourcePaths();

            foreach (string path in _watchers.Keys.Except(neededPaths, StringComparer.OrdinalIgnoreCase).ToList())
            {
                _watchers[path].Dispose();
                _watchers.Remove(path);
            }

            foreach (string path in neededPaths)
            {
                if (_watchers.ContainsKey(path))
                    continue;

                try
                {
                    var watcher = new FileSystemWatcher(path)
                    {
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.DirectoryName,
                        IncludeSubdirectories = true,
                        EnableRaisingEvents = true
                    };

                    string capturedPath = path;
                    FileSystemEventHandler handler = (_, __) => OnExternalChange(capturedPath);
                    watcher.Changed += handler;
                    watcher.Created += handler;
                    watcher.Deleted += handler;
                    watcher.Renamed += (_, __) => OnExternalChange(capturedPath);
                    watcher.Error += (_, e) => OnWatcherError(capturedPath, e);

                    _watchers[path] = watcher;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[AssetSync] Failed to start watching '{path}': {e.Message}");
                }
            }
        }

        private static HashSet<string> CollectExternalSourcePaths()
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] guids = AssetDatabase.FindAssets("t:AssetSyncSettings");

            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                var settings = AssetDatabase.LoadAssetAtPath<AssetSyncSettings>(assetPath);
                if (settings?.syncConfigs == null)
                    continue;

                foreach (var config in settings.syncConfigs)
                {
                    if (config == null || !config.enabled)
                        continue;
                    if (!AssetSyncer.IsExternalSourceDirectoryPath(config.sourcePath))
                        continue;

                    string fullPath = Path.GetFullPath(config.sourcePath);
                    if (Directory.Exists(fullPath))
                        result.Add(fullPath);
                }
            }

            return result;
        }

        private static void OnExternalChange(string sourcePath)
        {
            _pendingPaths[sourcePath] = DateTime.UtcNow;
        }

        private static void OnWatcherError(string sourcePath, ErrorEventArgs e)
        {
            Debug.LogWarning($"[AssetSync] Watcher error for '{sourcePath}': {e.GetException()?.Message}");
            if (_watchers.TryGetValue(sourcePath, out var watcher))
            {
                watcher.Dispose();
                _watchers.Remove(sourcePath);
            }

            EditorApplication.delayCall += RebuildWatchers;
        }

        private static void OnUpdate()
        {
            if (_pendingPaths.IsEmpty)
                return;

            DateTime now = DateTime.UtcNow;
            var readyPaths = new List<string>();

            foreach (var kvp in _pendingPaths)
            {
                if ((now - kvp.Value).TotalSeconds >= DebounceSeconds)
                    readyPaths.Add(kvp.Key);
            }

            if (readyPaths.Count == 0)
                return;

            foreach (string path in readyPaths)
                _pendingPaths.TryRemove(path, out _);

            SyncConfigsForPaths(readyPaths);
        }

        private static void SyncConfigsForPaths(IReadOnlyList<string> sourcePaths)
        {
            string[] guids = AssetDatabase.FindAssets("t:AssetSyncSettings");

            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                var settings = AssetDatabase.LoadAssetAtPath<AssetSyncSettings>(assetPath);
                if (settings?.syncConfigs == null)
                    continue;

                bool settingsChanged = false;

                foreach (var config in settings.syncConfigs)
                {
                    if (config == null || !config.enabled)
                        continue;
                    if (!AssetSyncer.IsExternalSourceDirectoryPath(config.sourcePath))
                        continue;

                    string configSourceFullPath = Path.GetFullPath(config.sourcePath);
                    bool matches = sourcePaths.Any(p =>
                        string.Equals(p, configSourceFullPath, StringComparison.OrdinalIgnoreCase));

                    if (!matches)
                        continue;

                    AssetSyncer.SyncConfig(config, out bool stateChanged);
                    if (stateChanged)
                        settingsChanged = true;
                }

                if (settingsChanged)
                    EditorUtility.SetDirty(settings);
            }
        }
    }
}
