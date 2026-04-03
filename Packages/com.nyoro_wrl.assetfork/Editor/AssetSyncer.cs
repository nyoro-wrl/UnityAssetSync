using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using Nyorowrl.Assetfork;

[assembly: InternalsVisibleTo("Nyorowrl.Assetfork.Editor.Tests")]

namespace Nyorowrl.Assetfork.Editor
{
    public static class AssetSyncer
    {
        [Serializable]
        private class ManifestData
        {
            public List<string> files = new List<string>();
        }

        public static void SyncAll(AssetForkSettings settings)
        {
            foreach (var config in settings.syncConfigs)
                SyncConfig(config);
        }

        public static void SyncConfig(SyncConfig config)
        {
            if (!config.enabled)
                return;

            string srcAssetPath = config.sourcePath;
            string dstAssetPath = config.destinationPath;

            if (string.IsNullOrEmpty(srcAssetPath) || string.IsNullOrEmpty(dstAssetPath))
            {
                Debug.LogWarning($"[AssetFork] '{config.configName}': Source or Destination is not set.");
                return;
            }

            if (srcAssetPath == dstAssetPath)
            {
                Debug.LogWarning($"[AssetFork] '{config.configName}': Source and Destination are the same directory.");
                return;
            }

            string srcRoot = ToFullPath(srcAssetPath);
            string dstRoot = ToFullPath(dstAssetPath);

            if (!Directory.Exists(srcRoot))
            {
                Debug.LogWarning($"[AssetFork] '{config.configName}': Source directory does not exist: {srcRoot}");
                return;
            }

            Directory.CreateDirectory(dstRoot);

            SyncDirectory(srcRoot, dstRoot, srcAssetPath, dstAssetPath, config.filters);
            AssetDatabase.Refresh();
        }

        private static void SyncDirectory(string srcRoot, string dstRoot, string srcAssetRoot, string dstAssetRoot, List<FilterCondition> filters)
        {
            string manifestPath = GetManifestPath(dstAssetRoot);
            HashSet<string> manifest = LoadManifest(manifestPath);
            bool manifestChanged = false;

            // Phase 1: Copy new and updated files
            string[] srcFiles = Directory.GetFiles(srcRoot, "*", SearchOption.AllDirectories)
                .Where(f => !f.EndsWith(".meta"))
                .ToArray();

            foreach (string srcFile in srcFiles)
            {
                string relPath = srcFile.Substring(srcRoot.Length).TrimStart(Path.DirectorySeparatorChar, '/');
                string normalizedRel = relPath.Replace('\\', '/');
                string assetPath = srcAssetRoot + "/" + normalizedRel;

                if (!PassesFilters(assetPath, filters))
                    continue;

                string dstFile = Path.Combine(dstRoot, relPath);

                if (ShouldCopy(srcFile, dstFile))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dstFile));
                    File.Copy(srcFile, dstFile, overwrite: true);
                }

                // マニフェストに記録（同期管理対象として登録）
                if (manifest.Add(normalizedRel))
                    manifestChanged = true;
            }

            // Phase 2: マニフェストに記録されたファイルのうち、不要になったものを削除
            if (Directory.Exists(dstRoot))
            {
                string[] dstFiles = Directory.GetFiles(dstRoot, "*", SearchOption.AllDirectories)
                    .Where(f => !f.EndsWith(".meta"))
                    .ToArray();

                foreach (string dstFile in dstFiles)
                {
                    string relPath = dstFile.Substring(dstRoot.Length).TrimStart(Path.DirectorySeparatorChar, '/');
                    string normalizedRel = relPath.Replace('\\', '/');

                    // マニフェストにないファイルは管理外（手動配置）→ 触らない
                    if (!manifest.Contains(normalizedRel))
                        continue;

                    string srcFile = Path.Combine(srcRoot, relPath);

                    bool shouldDelete;
                    if (!File.Exists(srcFile))
                    {
                        // ソースから削除された
                        shouldDelete = true;
                    }
                    else
                    {
                        string srcAssetPath = srcAssetRoot + "/" + normalizedRel;
                        if (!PassesFilters(srcAssetPath, filters))
                        {
                            // フィルタで除外 → 内容一致なら削除（同期済み）、不一致なら保護（手動変更）
                            shouldDelete = !ShouldCopy(srcFile, dstFile);
                        }
                        else
                        {
                            shouldDelete = false;
                        }
                    }

                    if (shouldDelete)
                    {
                        File.Delete(dstFile);
                        string metaFile = dstFile + ".meta";
                        if (File.Exists(metaFile))
                            File.Delete(metaFile);
                        manifest.Remove(normalizedRel);
                        manifestChanged = true;
                    }
                }
            }

            if (manifestChanged)
                SaveManifest(manifestPath, manifest);
        }

        private static string GetManifestPath(string dstAssetRoot)
        {
            using var md5 = MD5.Create();
            string hash = BitConverter.ToString(md5.ComputeHash(Encoding.UTF8.GetBytes(dstAssetRoot))).Replace("-", "");
            string dir = Path.Combine(Application.dataPath, "..", "Library", "AssetFork");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, hash + ".json");
        }

        private static HashSet<string> LoadManifest(string manifestPath)
        {
            if (!File.Exists(manifestPath))
                return new HashSet<string>();
            var data = JsonUtility.FromJson<ManifestData>(File.ReadAllText(manifestPath));
            return data?.files != null ? new HashSet<string>(data.files) : new HashSet<string>();
        }

        private static void SaveManifest(string manifestPath, HashSet<string> files)
        {
            var data = new ManifestData { files = new List<string>(files) };
            File.WriteAllText(manifestPath, JsonUtility.ToJson(data));
        }

        internal static bool ShouldCopy(string srcFile, string dstFile)
        {
            if (!File.Exists(dstFile))
                return true;

            var srcInfo = new FileInfo(srcFile);
            var dstInfo = new FileInfo(dstFile);

            if (srcInfo.Length != dstInfo.Length)
                return true;

            return ComputeMD5(srcFile) != ComputeMD5(dstFile);
        }

        internal static bool PassesFilters(string assetPath, List<FilterCondition> filters)
        {
            if (filters == null || filters.Count == 0)
                return true;

            return filters.All(f => EvaluateCondition(f, assetPath));
        }

        internal static bool EvaluateCondition(FilterCondition condition, string assetPath)
        {
            bool matched;

            if (condition.useMultipleTypes)
            {
                if (condition.multipleTypeNames == null || condition.multipleTypeNames.Count == 0)
                    return true;
                matched = condition.multipleTypeNames.Any(n => TypeMatchesAsset(n, assetPath));
            }
            else
            {
                if (string.IsNullOrEmpty(condition.singleTypeName))
                    return true;
                matched = TypeMatchesAsset(condition.singleTypeName, assetPath);
            }

            return condition.invert ? !matched : matched;
        }

        private static bool TypeMatchesAsset(string typeName, string assetPath)
        {
            Type type = Type.GetType(typeName)
                ?? Type.GetType(typeName + ", UnityEngine")
                ?? Type.GetType(typeName + ", UnityEditor");

            if (type == null)
                return false;

            UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
            return asset != null && type.IsAssignableFrom(asset.GetType());
        }

        private static string ComputeMD5(string filePath)
        {
            using var md5 = MD5.Create();
            using var stream = File.OpenRead(filePath);
            byte[] hash = md5.ComputeHash(stream);
            return BitConverter.ToString(hash);
        }

        private static string ToFullPath(string assetPath)
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
        }
    }

    public class AssetForkPostprocessor : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            string[] guids = AssetDatabase.FindAssets("t:AssetForkSettings");
            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                var settings = AssetDatabase.LoadAssetAtPath<AssetForkSettings>(assetPath);
                if (settings == null)
                    continue;

                foreach (var config in settings.syncConfigs)
                {
                    string srcPath = config.sourcePath;
                    if (string.IsNullOrEmpty(srcPath))
                        continue;

                    bool needsSync = importedAssets.Any(p => p.StartsWith(srcPath))
                        || deletedAssets.Any(p => p.StartsWith(srcPath))
                        || movedAssets.Any(p => p.StartsWith(srcPath))
                        || movedFromAssetPaths.Any(p => p.StartsWith(srcPath));

                    if (needsSync)
                        AssetSyncer.SyncConfig(config);
                }
            }
        }
    }
}
