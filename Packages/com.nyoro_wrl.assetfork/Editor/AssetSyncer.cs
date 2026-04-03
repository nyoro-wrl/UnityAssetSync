using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using Nyorowrl.Assetfork;

namespace Nyorowrl.Assetfork.Editor
{
    public static class AssetSyncer
    {
        public static void SyncAll(AssetForkSettings settings)
        {
            foreach (var config in settings.syncConfigs)
                SyncConfig(config);
        }

        public static void SyncConfig(SyncConfig config)
        {
            string srcAssetPath = config.sourcePath;
            string dstAssetPath = config.destinationPath;

            if (string.IsNullOrEmpty(srcAssetPath) || string.IsNullOrEmpty(dstAssetPath))
            {
                Debug.LogWarning($"[AssetFork] '{config.configName}': Source or Destination is not set.");
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

            SyncDirectory(srcRoot, dstRoot, srcAssetPath, config.filters);
            AssetDatabase.Refresh();
        }

        private static void SyncDirectory(string srcRoot, string dstRoot, string srcAssetRoot, List<FilterCondition> filters)
        {
            // Phase 1: Copy new and updated files
            string[] srcFiles = Directory.GetFiles(srcRoot, "*", SearchOption.AllDirectories)
                .Where(f => !f.EndsWith(".meta"))
                .ToArray();

            foreach (string srcFile in srcFiles)
            {
                string relPath = srcFile.Substring(srcRoot.Length).TrimStart(Path.DirectorySeparatorChar, '/');
                string assetPath = srcAssetRoot + "/" + relPath.Replace('\\', '/');

                if (!PassesFilters(assetPath, filters))
                    continue;

                string dstFile = Path.Combine(dstRoot, relPath);

                if (ShouldCopy(srcFile, dstFile))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dstFile));
                    File.Copy(srcFile, dstFile, overwrite: true);
                }
            }

            // Phase 2: Delete files removed from source
            if (!Directory.Exists(dstRoot))
                return;

            string[] dstFiles = Directory.GetFiles(dstRoot, "*", SearchOption.AllDirectories)
                .Where(f => !f.EndsWith(".meta"))
                .ToArray();

            foreach (string dstFile in dstFiles)
            {
                string relPath = dstFile.Substring(dstRoot.Length).TrimStart(Path.DirectorySeparatorChar, '/');
                string srcFile = Path.Combine(srcRoot, relPath);

                if (!File.Exists(srcFile))
                {
                    File.Delete(dstFile);
                    string metaFile = dstFile + ".meta";
                    if (File.Exists(metaFile))
                        File.Delete(metaFile);
                }
            }
        }

        private static bool ShouldCopy(string srcFile, string dstFile)
        {
            if (!File.Exists(dstFile))
                return true;

            var srcInfo = new FileInfo(srcFile);
            var dstInfo = new FileInfo(dstFile);

            if (srcInfo.Length != dstInfo.Length)
                return true;

            return ComputeMD5(srcFile) != ComputeMD5(dstFile);
        }

        private static bool PassesFilters(string assetPath, List<FilterCondition> filters)
        {
            if (filters == null || filters.Count == 0)
                return true;

            return filters.All(f => EvaluateCondition(f, assetPath));
        }

        private static bool EvaluateCondition(FilterCondition condition, string assetPath)
        {
            bool matched;

            if (condition.useMultipleTypes)
            {
                if (condition.multipleTypeNames == null || condition.multipleTypeNames.Count == 0)
                    matched = true;
                else
                    matched = condition.multipleTypeNames.Any(n => TypeMatchesAsset(n, assetPath));
            }
            else
            {
                if (string.IsNullOrEmpty(condition.singleTypeName))
                    matched = true;
                else
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
            AssetForkSettings settings = Resources.Load<AssetForkSettings>(AssetForkSettings.ResourcesPath);
            if (settings == null)
                return;

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
