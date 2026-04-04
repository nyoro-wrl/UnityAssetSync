using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using Nyorowrl.Assetfork;

[assembly: InternalsVisibleTo("Nyorowrl.Assetfork.Editor.Tests")]

namespace Nyorowrl.Assetfork.Editor
{
    public static class AssetSyncer
    {
        internal enum ProtectedEntryState
        {
            Source,
            Destination,
            Invalid
        }

        internal enum ConflictResolution
        {
            Owned,
            Protected
        }

        internal readonly struct SyncConflict
        {
            public readonly string NormalizedRelativePath;
            public readonly string SourceAssetPath;
            public readonly string DestinationAssetPath;

            public SyncConflict(string normalizedRelativePath, string sourceAssetPath, string destinationAssetPath)
            {
                NormalizedRelativePath = normalizedRelativePath;
                SourceAssetPath = sourceAssetPath;
                DestinationAssetPath = destinationAssetPath;
            }
        }

        internal delegate bool ConflictResolverDelegate(
            SyncConfig config,
            IReadOnlyList<SyncConflict> conflicts,
            out Dictionary<string, ConflictResolution> decisions);

        private readonly struct SourceCandidate
        {
            public readonly string NormalizedRelativePath;
            public readonly string SourceFilePath;
            public readonly string DestinationFilePath;
            public readonly string SourceAssetPath;
            public readonly string DestinationAssetPath;

            public SourceCandidate(
                string normalizedRelativePath,
                string sourceFilePath,
                string destinationFilePath,
                string sourceAssetPath,
                string destinationAssetPath)
            {
                NormalizedRelativePath = normalizedRelativePath;
                SourceFilePath = sourceFilePath;
                DestinationFilePath = destinationFilePath;
                SourceAssetPath = sourceAssetPath;
                DestinationAssetPath = destinationAssetPath;
            }
        }

        internal static ConflictResolverDelegate ConflictResolverOverride;

        public static int SyncConfig(SyncConfig config)
        {
            return SyncConfig(config, out _);
        }

        internal static int SyncConfig(SyncConfig config, out bool stateChanged)
        {
            stateChanged = false;
            if (config == null)
                return 0;

            stateChanged = NormalizeState(config);

            if (!config.enabled)
            {
                RemoveOwnedFilesFromDestination(config, out bool disabledStateChanged, out bool disabledFileSystemChanged);
                stateChanged |= disabledStateChanged;
                if (disabledFileSystemChanged)
                    AssetDatabase.Refresh();
                return 0;
            }

            if (!ValidateSyncConfig(config))
                return 0;

            string srcRoot = ToFullPath(config.sourcePath);
            string dstRoot = ToFullPath(config.destinationPath);
            Directory.CreateDirectory(dstRoot);

            var sourceProtected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var destinationProtected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            BuildProtectedPathSets(config, sourceProtected, destinationProtected);

            int copied = SyncDirectory(
                config,
                srcRoot,
                dstRoot,
                config.sourcePath,
                config.destinationPath,
                sourceProtected,
                destinationProtected,
                out bool syncStateChanged,
                out bool fileSystemChanged);

            stateChanged |= syncStateChanged;
            if (fileSystemChanged)
                AssetDatabase.Refresh();

            return copied;
        }

        private static void RemoveOwnedFilesFromDestination(
            SyncConfig config,
            out bool stateChanged,
            out bool fileSystemChanged)
        {
            stateChanged = false;
            fileSystemChanged = false;

            if (config.ownedRelativePaths == null || config.ownedRelativePaths.Count == 0)
                return;

            if (string.IsNullOrEmpty(config.destinationPath))
                return;

            string dstRoot = ToFullPath(config.destinationPath);
            var owned = new HashSet<string>(config.ownedRelativePaths, StringComparer.OrdinalIgnoreCase);
            var sourceProtected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var destinationProtected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            BuildProtectedPathSets(config, sourceProtected, destinationProtected);

            foreach (string rel in owned.ToList())
            {
                // Destination-protected assets are never deleted by sync operations.
                if (!destinationProtected.Contains(rel))
                {
                    string relSystem = NormalizedRelativePathToSystemPath(rel);
                    string dstFile = Path.Combine(dstRoot, relSystem);
                    if (DeleteFileAndMeta(dstFile))
                        fileSystemChanged = true;
                }

                owned.Remove(rel);
                stateChanged = true;
            }

            stateChanged |= SetOwnedPaths(config, owned);
        }

        private static bool ValidateSyncConfig(SyncConfig config)
        {
            if (config == null || !config.enabled)
                return false;

            string srcAssetPath = config.sourcePath;
            string dstAssetPath = config.destinationPath;

            if (string.IsNullOrEmpty(srcAssetPath) || string.IsNullOrEmpty(dstAssetPath))
            {
                Debug.LogWarning($"[AssetFork] '{config.configName}': Source or Destination is not set.");
                return false;
            }

            if (TryGetConfigWarning(config, out string warning))
            {
                Debug.LogWarning($"[AssetFork] '{config.configName}': {warning}");
                return false;
            }

            return true;
        }

        private static int SyncDirectory(
            SyncConfig config,
            string srcRoot,
            string dstRoot,
            string srcAssetRoot,
            string dstAssetRoot,
            HashSet<string> sourceProtectedPaths,
            HashSet<string> destinationProtectedPaths,
            out bool stateChanged,
            out bool fileSystemChanged)
        {
            stateChanged = false;
            fileSystemChanged = false;
            int copiedCount = 0;

            var owned = new HashSet<string>(config.ownedRelativePaths ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            var candidates = CollectSourceCandidates(config, srcRoot, dstRoot, srcAssetRoot, dstAssetRoot, sourceProtectedPaths);

            int resolutionIterations = 0;
            while (true)
            {
                resolutionIterations++;
                if (resolutionIterations > 8)
                {
                    Debug.LogWarning($"[AssetFork] '{config.configName}': Conflict resolution reached maximum retries. Sync cancelled.");
                    stateChanged |= SetOwnedPaths(config, owned);
                    return copiedCount;
                }

                var conflicts = new List<SyncConflict>();
                var copyCandidates = new List<SourceCandidate>();

                foreach (var candidate in candidates)
                {
                    string rel = candidate.NormalizedRelativePath;

                    if (destinationProtectedPaths.Contains(rel))
                    {
                        if (owned.Remove(rel))
                            stateChanged = true;
                        continue;
                    }

                    bool isOwned = owned.Contains(rel);
                    bool destinationExists = File.Exists(candidate.DestinationFilePath);

                    if (isOwned || !destinationExists)
                    {
                        copyCandidates.Add(candidate);
                        continue;
                    }

                    conflicts.Add(new SyncConflict(rel, candidate.SourceAssetPath, candidate.DestinationAssetPath));
                }

                if (conflicts.Count == 0)
                {
                    copiedCount += ExecuteCopyPhase(copyCandidates, owned, ref stateChanged, ref fileSystemChanged);
                    break;
                }

                if (!TryResolveConflicts(config, conflicts, out var decisions))
                {
                    stateChanged |= SetOwnedPaths(config, owned);
                    return copiedCount;
                }

                bool anyDecisionApplied = false;
                foreach (var conflict in conflicts)
                {
                    if (!decisions.TryGetValue(conflict.NormalizedRelativePath, out var decision))
                    {
                        Debug.LogWarning($"[AssetFork] '{config.configName}': Unresolved conflict for '{conflict.NormalizedRelativePath}'.");
                        continue;
                    }

                    if (decision == ConflictResolution.Owned)
                    {
                        if (owned.Add(conflict.NormalizedRelativePath))
                            stateChanged = true;
                        anyDecisionApplied = true;
                        continue;
                    }

                    if (!TryAddDestinationProtectedGuid(config, conflict.DestinationAssetPath, out string protectedGuid))
                    {
                        Debug.LogWarning($"[AssetFork] '{config.configName}': Failed to protect '{conflict.DestinationAssetPath}' because GUID could not be resolved.");
                        continue;
                    }

                    if (!config.protectedGuids.Contains(protectedGuid))
                    {
                        config.protectedGuids.Add(protectedGuid);
                        stateChanged = true;
                    }

                    if (owned.Remove(conflict.NormalizedRelativePath))
                        stateChanged = true;

                    anyDecisionApplied = true;
                }

                if (!anyDecisionApplied)
                {
                    stateChanged |= SetOwnedPaths(config, owned);
                    return copiedCount;
                }

                stateChanged |= NormalizeState(config);
                sourceProtectedPaths.Clear();
                destinationProtectedPaths.Clear();
                BuildProtectedPathSets(config, sourceProtectedPaths, destinationProtectedPaths);
                candidates = CollectSourceCandidates(config, srcRoot, dstRoot, srcAssetRoot, dstAssetRoot, sourceProtectedPaths);
            }

            foreach (string rel in owned.ToList())
            {
                if (destinationProtectedPaths.Contains(rel))
                {
                    owned.Remove(rel);
                    stateChanged = true;
                    continue;
                }

                string relSystem = NormalizedRelativePathToSystemPath(rel);
                string srcFile = Path.Combine(srcRoot, relSystem);
                string dstFile = Path.Combine(dstRoot, relSystem);
                string srcAssetPath = srcAssetRoot + "/" + rel;

                bool inScope = IsWithinSyncScope(rel, config.includeSubdirectories);
                bool filteredOut = !PassesFilters(srcAssetPath, config.filters);
                bool sourceProtected = sourceProtectedPaths.Contains(rel);

                if (!File.Exists(srcFile))
                {
                    if (DeleteFileAndMeta(dstFile))
                        fileSystemChanged = true;
                    owned.Remove(rel);
                    stateChanged = true;
                    continue;
                }

                if (!inScope || filteredOut || sourceProtected)
                {
                    bool shouldDelete = File.Exists(dstFile) && !ShouldCopy(srcFile, dstFile);
                    if (shouldDelete && DeleteFileAndMeta(dstFile))
                        fileSystemChanged = true;

                    owned.Remove(rel);
                    stateChanged = true;
                    continue;
                }

                if (!File.Exists(dstFile))
                {
                    EnsureParentDirectory(dstFile);
                    File.Copy(srcFile, dstFile, overwrite: true);
                    copiedCount++;
                    fileSystemChanged = true;
                }
            }

            stateChanged |= SetOwnedPaths(config, owned);
            return copiedCount;
        }

        private static List<SourceCandidate> CollectSourceCandidates(
            SyncConfig config,
            string srcRoot,
            string dstRoot,
            string srcAssetRoot,
            string dstAssetRoot,
            HashSet<string> sourceProtectedPaths)
        {
            var result = new List<SourceCandidate>();

            SearchOption srcSearchOption = config.includeSubdirectories
                ? SearchOption.AllDirectories
                : SearchOption.TopDirectoryOnly;

            string[] srcFiles = Directory.GetFiles(srcRoot, "*", srcSearchOption)
                .Where(f => !f.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            foreach (string srcFile in srcFiles)
            {
                string relPath = srcFile.Substring(srcRoot.Length).TrimStart(Path.DirectorySeparatorChar, '/');
                string normalizedRel = NormalizeRelativePath(relPath);
                if (!IsWithinSyncScope(normalizedRel, config.includeSubdirectories))
                    continue;

                string srcAssetPath = srcAssetRoot + "/" + normalizedRel;
                if (!PassesFilters(srcAssetPath, config.filters))
                    continue;

                if (sourceProtectedPaths.Contains(normalizedRel))
                    continue;

                string relSystem = NormalizedRelativePathToSystemPath(normalizedRel);
                string dstFile = Path.Combine(dstRoot, relSystem);
                string dstAssetPath = dstAssetRoot + "/" + normalizedRel;

                result.Add(new SourceCandidate(
                    normalizedRel,
                    srcFile,
                    dstFile,
                    srcAssetPath,
                    dstAssetPath));
            }

            return result;
        }

        private static int ExecuteCopyPhase(
            IEnumerable<SourceCandidate> copyCandidates,
            HashSet<string> owned,
            ref bool stateChanged,
            ref bool fileSystemChanged)
        {
            int copiedCount = 0;
            foreach (var candidate in copyCandidates)
            {
                if (ShouldCopy(candidate.SourceFilePath, candidate.DestinationFilePath))
                {
                    EnsureParentDirectory(candidate.DestinationFilePath);
                    File.Copy(candidate.SourceFilePath, candidate.DestinationFilePath, overwrite: true);
                    copiedCount++;
                    fileSystemChanged = true;
                }

                if (owned.Add(candidate.NormalizedRelativePath))
                    stateChanged = true;
            }

            return copiedCount;
        }

        private static void EnsureParentDirectory(string filePath)
        {
            string parent = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(parent))
                Directory.CreateDirectory(parent);
        }

        private static bool TryResolveConflicts(
            SyncConfig config,
            IReadOnlyList<SyncConflict> conflicts,
            out Dictionary<string, ConflictResolution> decisions)
        {
            if (ConflictResolverOverride != null)
                return ConflictResolverOverride(config, conflicts, out decisions);

            return ConflictResolutionDialog.ShowDialog(config, conflicts, out decisions);
        }

        private static bool TryAddDestinationProtectedGuid(SyncConfig config, string destinationAssetPath, out string guid)
        {
            guid = AssetDatabase.AssetPathToGUID(destinationAssetPath);
            if (!string.IsNullOrEmpty(guid))
                return true;

            AssetDatabase.ImportAsset(destinationAssetPath, ImportAssetOptions.ForceSynchronousImport);
            guid = AssetDatabase.AssetPathToGUID(destinationAssetPath);
            return !string.IsNullOrEmpty(guid);
        }

        internal static ProtectedEntryState GetProtectedEntryState(
            SyncConfig config,
            string guid,
            out string assetPath,
            out string normalizedRelativePath)
        {
            assetPath = string.Empty;
            normalizedRelativePath = string.Empty;

            if (string.IsNullOrWhiteSpace(guid) || config == null)
                return ProtectedEntryState.Invalid;

            assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(assetPath))
                return ProtectedEntryState.Invalid;

            var state = ClassifyProtectedAssetPath(
                assetPath,
                config.sourcePath,
                config.destinationPath,
                out normalizedRelativePath);
            if ((state == ProtectedEntryState.Source || state == ProtectedEntryState.Destination)
                && string.IsNullOrEmpty(normalizedRelativePath))
                return ProtectedEntryState.Invalid;

            return state;
        }

        private static ProtectedEntryState ClassifyProtectedAssetPath(
            string assetPath,
            string sourceAssetRoot,
            string destinationAssetRoot,
            out string normalizedRelativePath)
        {
            normalizedRelativePath = string.Empty;
            if (string.IsNullOrEmpty(assetPath))
                return ProtectedEntryState.Invalid;

            if (!string.IsNullOrEmpty(destinationAssetRoot)
                && AssetForkPostprocessor.IsAssetPathWithinRoot(assetPath, destinationAssetRoot))
            {
                normalizedRelativePath = NormalizeRelativePath(GetRelativeAssetPath(assetPath, destinationAssetRoot));
                return ProtectedEntryState.Destination;
            }

            if (!string.IsNullOrEmpty(sourceAssetRoot)
                && AssetForkPostprocessor.IsAssetPathWithinRoot(assetPath, sourceAssetRoot))
            {
                normalizedRelativePath = NormalizeRelativePath(GetRelativeAssetPath(assetPath, sourceAssetRoot));
                return ProtectedEntryState.Source;
            }

            return ProtectedEntryState.Invalid;
        }

        private static string GetRelativeAssetPath(string assetPath, string rootAssetPath)
        {
            string normalizedAsset = NormalizeAssetPath(assetPath);
            string normalizedRoot = NormalizeAssetPath(rootAssetPath);
            if (normalizedAsset.Equals(normalizedRoot, StringComparison.Ordinal))
                return string.Empty;

            return normalizedAsset.Substring(normalizedRoot.Length + 1);
        }

        private static void BuildProtectedPathSets(
            SyncConfig config,
            HashSet<string> sourceProtectedPaths,
            HashSet<string> destinationProtectedPaths)
        {
            sourceProtectedPaths.Clear();
            destinationProtectedPaths.Clear();

            if (config?.protectedGuids == null)
                return;

            foreach (string guid in config.protectedGuids)
            {
                var state = GetProtectedEntryState(config, guid, out _, out string rel);
                if (string.IsNullOrEmpty(rel))
                    continue;

                if (state == ProtectedEntryState.Source)
                    sourceProtectedPaths.Add(rel);
                else if (state == ProtectedEntryState.Destination)
                    destinationProtectedPaths.Add(rel);
            }
        }

        internal static bool NormalizeState(SyncConfig config)
        {
            if (config == null)
                return false;

            config.ownedRelativePaths ??= new List<string>();
            bool protectedListInitialized = false;
            if (config.protectedGuids == null)
            {
                config.protectedGuids = new List<string>();
                protectedListInitialized = true;
            }

            var normalizedOwned = config.ownedRelativePaths
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(NormalizeRelativePath)
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            bool changed = protectedListInitialized
                || !ListEquals(config.ownedRelativePaths, normalizedOwned, StringComparer.OrdinalIgnoreCase);

            if (changed)
            {
                config.ownedRelativePaths = normalizedOwned;
            }

            return changed;
        }

        private static bool SetOwnedPaths(SyncConfig config, HashSet<string> owned)
        {
            config.ownedRelativePaths ??= new List<string>();
            var normalizedOwned = owned
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(NormalizeRelativePath)
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            bool changed = !ListEquals(config.ownedRelativePaths, normalizedOwned, StringComparer.OrdinalIgnoreCase);
            if (changed)
                config.ownedRelativePaths = normalizedOwned;

            return changed;
        }

        private static bool ListEquals(IReadOnlyList<string> a, IReadOnlyList<string> b, StringComparer comparer)
        {
            if (ReferenceEquals(a, b))
                return true;
            if (a == null || b == null)
                return false;
            if (a.Count != b.Count)
                return false;

            for (int i = 0; i < a.Count; i++)
            {
                if (!comparer.Equals(a[i], b[i]))
                    return false;
            }

            return true;
        }

        private static bool DeleteFileAndMeta(string filePath)
        {
            bool changed = false;
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                changed = true;
            }

            string metaFile = filePath + ".meta";
            if (File.Exists(metaFile))
            {
                File.Delete(metaFile);
                changed = true;
            }

            return changed;
        }

        internal static string NormalizeRelativePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            return path.Replace('\\', '/').Trim('/');
        }

        private static string NormalizeAssetPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;
            return path.Replace('\\', '/').TrimEnd('/');
        }

        private static string NormalizedRelativePathToSystemPath(string normalizedRelativePath)
        {
            return normalizedRelativePath.Replace('/', Path.DirectorySeparatorChar);
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

        internal static bool TryGetConfigWarning(SyncConfig config, out string warning)
        {
            warning = null;
            if (config == null || !config.enabled)
                return false;
            if (string.IsNullOrEmpty(config.sourcePath) || string.IsNullOrEmpty(config.destinationPath))
                return false;

            string srcRoot = ToFullPath(config.sourcePath);
            string dstRoot = ToFullPath(config.destinationPath);

            if (PathsEqual(srcRoot, dstRoot))
            {
                warning = "Source and Destination are the same directory.";
                return true;
            }

            bool hasNestedRoots = AreRootsNested(srcRoot, dstRoot);
            if (config.includeSubdirectories && hasNestedRoots)
            {
                warning = "Source and Destination must not be nested when Include Subdirectories is enabled.";
                return true;
            }

            if (!Directory.Exists(srcRoot))
            {
                warning = $"Source directory does not exist: {srcRoot}";
                return true;
            }

            return false;
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

        private static bool AreRootsNested(string sourceRoot, string destinationRoot)
        {
            return IsSubPathOf(sourceRoot, destinationRoot) || IsSubPathOf(destinationRoot, sourceRoot);
        }

        internal static bool AreAssetPathsNested(string sourceAssetPath, string destinationAssetPath)
        {
            if (string.IsNullOrEmpty(sourceAssetPath) || string.IsNullOrEmpty(destinationAssetPath))
                return false;

            string sourceRoot = ToFullPath(sourceAssetPath);
            string destinationRoot = ToFullPath(destinationAssetPath);
            if (PathsEqual(sourceRoot, destinationRoot))
                return false;

            return AreRootsNested(sourceRoot, destinationRoot);
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

                bool settingsChanged = false;
                foreach (var config in settings.syncConfigs)
                {
                    if (config == null)
                        continue;

                    bool sourcePathMoved = TryRemapPathByMoves(config.sourcePath, movedAssets, movedFromAssetPaths, out string remappedSourcePath);
                    if (sourcePathMoved)
                    {
                        config.sourcePath = remappedSourcePath;
                        settingsChanged = true;
                    }

                    bool destinationPathMoved = TryRemapPathByMoves(config.destinationPath, movedAssets, movedFromAssetPaths, out string remappedDestinationPath);
                    if (destinationPathMoved)
                    {
                        config.destinationPath = remappedDestinationPath;
                        settingsChanged = true;
                    }

                    string srcPath = config.sourcePath;
                    if (string.IsNullOrEmpty(srcPath))
                        continue;

                    bool needsSync = sourcePathMoved
                        || destinationPathMoved
                        || importedAssets.Any(p => IsAssetPathWithinScope(p, srcPath, config.includeSubdirectories))
                        || deletedAssets.Any(p => IsAssetPathWithinScope(p, srcPath, config.includeSubdirectories))
                        || movedAssets.Any(p => IsAssetPathWithinScope(p, srcPath, config.includeSubdirectories))
                        || movedFromAssetPaths.Any(p => IsAssetPathWithinScope(p, srcPath, config.includeSubdirectories));

                    if (needsSync)
                    {
                        AssetSyncer.SyncConfig(config, out bool configStateChanged);
                        if (configStateChanged)
                            settingsChanged = true;
                    }
                }

                if (settingsChanged)
                    EditorUtility.SetDirty(settings);
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

        internal static bool TryRemapPathByMoves(
            string currentPath,
            string[] movedAssets,
            string[] movedFromAssetPaths,
            out string remappedPath)
        {
            remappedPath = currentPath;
            if (string.IsNullOrEmpty(currentPath) || movedAssets == null || movedFromAssetPaths == null)
                return false;

            int pairCount = Math.Min(movedAssets.Length, movedFromAssetPaths.Length);
            if (pairCount == 0)
                return false;

            string normalizedCurrent = NormalizeAssetPath(currentPath);
            string bestFrom = null;
            string bestTo = null;

            for (int i = 0; i < pairCount; i++)
            {
                string movedTo = NormalizeAssetPath(movedAssets[i]);
                string movedFrom = NormalizeAssetPath(movedFromAssetPaths[i]);

                if (string.IsNullOrEmpty(movedFrom) || string.IsNullOrEmpty(movedTo))
                    continue;
                if (!AssetDatabase.IsValidFolder(movedTo))
                    continue;
                if (!IsAssetPathWithinRoot(normalizedCurrent, movedFrom))
                    continue;

                if (bestFrom == null || movedFrom.Length > bestFrom.Length)
                {
                    bestFrom = movedFrom;
                    bestTo = movedTo;
                }
            }

            if (bestFrom == null)
                return false;

            string suffix = normalizedCurrent.Length > bestFrom.Length
                ? normalizedCurrent.Substring(bestFrom.Length)
                : string.Empty;
            remappedPath = bestTo + suffix;

            return !string.Equals(remappedPath, normalizedCurrent, StringComparison.Ordinal);
        }

        private static string NormalizeAssetPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;
            return path.Replace('\\', '/').TrimEnd('/');
        }
    }
}
