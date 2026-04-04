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

        public static int SyncConfig(SyncConfig config)
        {
            if (!config.enabled)
                return 0;

            string srcAssetPath = config.sourcePath;
            string dstAssetPath = config.destinationPath;

            if (string.IsNullOrEmpty(srcAssetPath) || string.IsNullOrEmpty(dstAssetPath))
            {
                Debug.LogWarning($"[AssetFork] '{config.configName}': Source or Destination is not set.");
                return 0;
            }

            if (srcAssetPath == dstAssetPath)
            {
                Debug.LogWarning($"[AssetFork] '{config.configName}': Source and Destination are the same directory.");
                return 0;
            }

            string srcRoot = ToFullPath(srcAssetPath);
            string dstRoot = ToFullPath(dstAssetPath);

            if (PathsEqual(srcRoot, dstRoot))
            {
                Debug.LogWarning($"[AssetFork] '{config.configName}': Source and Destination are the same directory.");
                return 0;
            }

            // Reject nested roots to avoid self-recursive copy (e.g. Src -> Src/Dst).
            if (IsSubPathOf(srcRoot, dstRoot) || IsSubPathOf(dstRoot, srcRoot))
            {
                Debug.LogWarning($"[AssetFork] '{config.configName}': Source and Destination must not be nested.");
                return 0;
            }

            if (!Directory.Exists(srcRoot))
            {
                Debug.LogWarning($"[AssetFork] '{config.configName}': Source directory does not exist: {srcRoot}");
                return 0;
            }

            Directory.CreateDirectory(dstRoot);

            int count = SyncDirectory(
                srcRoot,
                dstRoot,
                srcAssetPath,
                dstAssetPath,
                config.filters,
                config.includeSubdirectories);
            AssetDatabase.Refresh();
            return count;
        }

        private static int SyncDirectory(
            string srcRoot,
            string dstRoot,
            string srcAssetRoot,
            string dstAssetRoot,
            List<FilterCondition> filters,
            bool includeSubdirectories)
        {
            string manifestPath = GetManifestPath(dstAssetRoot);
            HashSet<string> manifest = LoadManifest(manifestPath);
            bool manifestChanged = false;
            int copiedCount = 0;

            // Phase 1: Copy new and updated files
            SearchOption srcSearchOption = includeSubdirectories
                ? SearchOption.AllDirectories
                : SearchOption.TopDirectoryOnly;

            string[] srcFiles = Directory.GetFiles(srcRoot, "*", srcSearchOption)
                .Where(f => !f.EndsWith(".meta"))
                .ToArray();

            foreach (string srcFile in srcFiles)
            {
                string relPath = srcFile.Substring(srcRoot.Length).TrimStart(Path.DirectorySeparatorChar, '/');
                string normalizedRel = relPath.Replace('\\', '/');
                string assetPath = srcAssetRoot + "/" + normalizedRel;

                if (!IsWithinSyncScope(normalizedRel, includeSubdirectories) || !PassesFilters(assetPath, filters))
                    continue;

                string dstFile = Path.Combine(dstRoot, relPath);

                if (ShouldCopy(srcFile, dstFile))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dstFile));
                    File.Copy(srcFile, dstFile, overwrite: true);
                    copiedCount++;
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
                        bool inScope = IsWithinSyncScope(normalizedRel, includeSubdirectories);
                        if (!inScope || !PassesFilters(srcAssetPath, filters))
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

            return copiedCount;
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

        private static bool PathsEqual(string pathA, string pathB)
        {
            return string.Equals(
                NormalizeFullPath(pathA),
                NormalizeFullPath(pathB),
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSubPathOf(string parentPath, string candidatePath)
        {
            string parent = NormalizeFullPath(parentPath) + Path.DirectorySeparatorChar;
            string candidate = NormalizeFullPath(candidatePath) + Path.DirectorySeparatorChar;
            return candidate.StartsWith(parent, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeFullPath(string path)
        {
            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static bool IsWithinSyncScope(string normalizedRelativePath, bool includeSubdirectories)
        {
            if (includeSubdirectories)
                return true;

            return normalizedRelativePath.IndexOf('/') < 0
                && normalizedRelativePath.IndexOf('\\') < 0;
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

                    bool needsSync = importedAssets.Any(p => IsAssetPathWithinScope(p, srcPath, config.includeSubdirectories))
                        || deletedAssets.Any(p => IsAssetPathWithinScope(p, srcPath, config.includeSubdirectories))
                        || movedAssets.Any(p => IsAssetPathWithinScope(p, srcPath, config.includeSubdirectories))
                        || movedFromAssetPaths.Any(p => IsAssetPathWithinScope(p, srcPath, config.includeSubdirectories));

                    if (needsSync)
                        AssetSyncer.SyncConfig(config);
                }
            }
        }

        internal static bool IsAssetPathWithinRoot(string assetPath, string rootPath)
        {
            if (string.IsNullOrEmpty(assetPath) || string.IsNullOrEmpty(rootPath))
                return false;

            string normalizedAsset = assetPath.Replace('\\', '/');
            string normalizedRoot = rootPath.Replace('\\', '/').TrimEnd('/');
            if (normalizedAsset.Equals(normalizedRoot, StringComparison.Ordinal))
                return true;

            return normalizedAsset.StartsWith(normalizedRoot + "/", StringComparison.Ordinal);
        }

        internal static bool IsAssetPathWithinScope(string assetPath, string rootPath, bool includeSubdirectories)
        {
            if (!IsAssetPathWithinRoot(assetPath, rootPath))
                return false;

            if (includeSubdirectories)
                return true;

            string normalizedAsset = assetPath.Replace('\\', '/');
            string normalizedRoot = rootPath.Replace('\\', '/').TrimEnd('/');
            if (normalizedAsset.Equals(normalizedRoot, StringComparison.Ordinal))
                return true;

            string relative = normalizedAsset.Substring(normalizedRoot.Length + 1);
            return relative.IndexOf('/') < 0;
        }
    }
}
