using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using Nyorowrl.AssetSync;

[assembly: InternalsVisibleTo("Nyorowrl.AssetSync.Editor.Tests")]

namespace Nyorowrl.AssetSync.Editor
{
    public static class AssetSyncer
    {
        internal enum IgnoreEntryState
        {
            Source,
            Destination,
            Invalid
        }

        internal enum ConflictResolution
        {
            Sync,
            Ignore
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

        internal readonly struct PreviewCopyEntry
        {
            public readonly string SourceAssetPath;
            public readonly string DestinationAssetPath;

            public PreviewCopyEntry(string sourceAssetPath, string destinationAssetPath)
            {
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

        internal static void ResumeSyncAfterConflictDialog(SyncConfig config)
        {
            if (config == null)
                return;

            SyncConfig(config, out bool stateChanged);
            if (!stateChanged)
                return;

            string[] settingsGuids = AssetDatabase.FindAssets("t:AssetSyncSettings");
            foreach (string settingsGuid in settingsGuids)
            {
                string settingsPath = AssetDatabase.GUIDToAssetPath(settingsGuid);
                var settings = AssetDatabase.LoadAssetAtPath<AssetSyncSettings>(settingsPath);
                if (settings?.syncConfigs == null)
                    continue;
                if (!settings.syncConfigs.Contains(config))
                    continue;

                EditorUtility.SetDirty(settings);
                break;
            }
        }

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
                RemoveSyncFilesFromDestination(config, out bool disabledStateChanged, out bool disabledFileSystemChanged);
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

            var sourceIgnore = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var destinationIgnore = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            BuildIgnorePathSets(config, sourceIgnore, destinationIgnore);

            int copied = SyncDirectory(
                config,
                srcRoot,
                dstRoot,
                config.sourcePath,
                config.destinationPath,
                sourceIgnore,
                destinationIgnore,
                out bool syncStateChanged,
                out bool fileSystemChanged);

            stateChanged |= syncStateChanged;
            if (fileSystemChanged)
                AssetDatabase.Refresh();

            return copied;
        }

        private static void RemoveSyncFilesFromDestination(
            SyncConfig config,
            out bool stateChanged,
            out bool fileSystemChanged)
        {
            stateChanged = false;
            fileSystemChanged = false;

            if (config.syncRelativePaths == null || config.syncRelativePaths.Count == 0)
                return;

            if (string.IsNullOrEmpty(config.destinationPath))
                return;

            string dstRoot = ToFullPath(config.destinationPath);
            var synced = new HashSet<string>(config.syncRelativePaths, StringComparer.OrdinalIgnoreCase);
            var sourceIgnore = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var destinationIgnore = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            BuildIgnorePathSets(config, sourceIgnore, destinationIgnore);

            foreach (string rel in synced.ToList())
            {
                // Destination-ignore assets are never deleted by sync operations.
                if (!destinationIgnore.Contains(rel))
                {
                    string relSystem = NormalizedRelativePathToSystemPath(rel);
                    string dstFile = Path.Combine(dstRoot, relSystem);
                    if (DeleteFileAndMeta(dstFile))
                        fileSystemChanged = true;

                    synced.Remove(rel);
                    stateChanged = true;
                }
            }

            stateChanged |= SetSyncPaths(config, synced);
        }

        internal static bool PruneSyncPathsForDisabledConfig(SyncConfig config)
        {
            if (config?.syncRelativePaths == null || config.syncRelativePaths.Count == 0)
                return false;

            var synced = new HashSet<string>(config.syncRelativePaths, StringComparer.OrdinalIgnoreCase);
            HashSet<string> destinationIgnore = CollectDestinationIgnoreRelativePaths(config);

            bool removedAny = false;
            foreach (string rel in synced.ToList())
            {
                if (destinationIgnore.Contains(rel))
                    continue;

                synced.Remove(rel);
                removedAny = true;
            }

            if (!removedAny)
                return false;

            return SetSyncPaths(config, synced);
        }

        private static bool ValidateSyncConfig(SyncConfig config)
        {
            if (config == null || !config.enabled)
                return false;

            string srcAssetPath = config.sourcePath;
            string dstAssetPath = config.destinationPath;

            if (string.IsNullOrEmpty(srcAssetPath) || string.IsNullOrEmpty(dstAssetPath))
            {
                Debug.LogWarning($"[AssetSync] '{config.configName}': Source or Destination is not set.");
                return false;
            }

            if (TryGetConfigWarning(config, out string warning))
            {
                Debug.LogWarning($"[AssetSync] '{config.configName}': {warning}");
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
            HashSet<string> sourceIgnorePaths,
            HashSet<string> destinationIgnorePaths,
            out bool stateChanged,
            out bool fileSystemChanged)
        {
            stateChanged = false;
            fileSystemChanged = false;
            int copiedCount = 0;

            var synced = new HashSet<string>(config.syncRelativePaths ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            var candidates = CollectSourceCandidates(config, srcRoot, dstRoot, srcAssetRoot, dstAssetRoot, sourceIgnorePaths);

            int resolutionIterations = 0;
            while (true)
            {
                resolutionIterations++;
                if (resolutionIterations > 8)
                {
                    Debug.LogWarning($"[AssetSync] '{config.configName}': Conflict resolution reached maximum retries. Sync cancelled.");
                    stateChanged |= SetSyncPaths(config, synced);
                    return copiedCount;
                }

                var conflicts = new List<SyncConflict>();
                var copyCandidates = new List<SourceCandidate>();

                foreach (var candidate in candidates)
                {
                    string rel = candidate.NormalizedRelativePath;

                    if (destinationIgnorePaths.Contains(rel))
                    {
                        continue;
                    }

                    bool isSync = synced.Contains(rel);
                    bool destinationExists = File.Exists(candidate.DestinationFilePath);

                    if (isSync || !destinationExists)
                    {
                        copyCandidates.Add(candidate);
                        continue;
                    }

                    conflicts.Add(new SyncConflict(rel, candidate.SourceAssetPath, candidate.DestinationAssetPath));
                }

                if (conflicts.Count == 0)
                {
                    copiedCount += ExecuteCopyPhase(copyCandidates, synced, ref stateChanged, ref fileSystemChanged);
                    break;
                }

                if (!TryResolveConflicts(config, conflicts, out var decisions))
                {
                    stateChanged |= SetSyncPaths(config, synced);
                    return copiedCount;
                }

                bool anyDecisionApplied = false;
                foreach (var conflict in conflicts)
                {
                    if (!decisions.TryGetValue(conflict.NormalizedRelativePath, out var decision))
                    {
                        Debug.LogWarning($"[AssetSync] '{config.configName}': Unresolved conflict for '{conflict.NormalizedRelativePath}'.");
                        continue;
                    }

                    if (decision == ConflictResolution.Sync)
                    {
                        if (synced.Add(conflict.NormalizedRelativePath))
                            stateChanged = true;
                        anyDecisionApplied = true;
                        continue;
                    }

                    if (!TryAddDestinationIgnoreGuid(config, conflict.DestinationAssetPath, out string ignoreGuid))
                    {
                        Debug.LogWarning($"[AssetSync] '{config.configName}': Failed to protect '{conflict.DestinationAssetPath}' because GUID could not be resolved.");
                        continue;
                    }

                    if (!config.ignoreGuids.Contains(ignoreGuid))
                    {
                        config.ignoreGuids.Add(ignoreGuid);
                        stateChanged = true;
                    }

                    if (synced.Remove(conflict.NormalizedRelativePath))
                        stateChanged = true;

                    anyDecisionApplied = true;
                }

                if (!anyDecisionApplied)
                {
                    stateChanged |= SetSyncPaths(config, synced);
                    return copiedCount;
                }

                stateChanged |= NormalizeState(config);
                sourceIgnorePaths.Clear();
                destinationIgnorePaths.Clear();
                BuildIgnorePathSets(config, sourceIgnorePaths, destinationIgnorePaths);
                candidates = CollectSourceCandidates(config, srcRoot, dstRoot, srcAssetRoot, dstAssetRoot, sourceIgnorePaths);
            }

            foreach (string rel in synced.ToList())
            {
                if (destinationIgnorePaths.Contains(rel))
                {
                    continue;
                }

                string relSystem = NormalizedRelativePathToSystemPath(rel);
                string srcFile = Path.Combine(srcRoot, relSystem);
                string dstFile = Path.Combine(dstRoot, relSystem);
                string srcAssetPath = srcAssetRoot + "/" + rel;

                bool inScope = IsWithinSyncScope(rel, config.includeSubdirectories);
                bool filteredOut = !PassesFilters(srcAssetPath, config.filters);
                bool sourceIgnore = sourceIgnorePaths.Contains(rel);

                if (!File.Exists(srcFile))
                {
                    if (DeleteFileAndMeta(dstFile))
                        fileSystemChanged = true;
                    synced.Remove(rel);
                    stateChanged = true;
                    continue;
                }

                if (!inScope || filteredOut || sourceIgnore)
                {
                    bool shouldDelete = File.Exists(dstFile) && !ShouldCopy(srcFile, dstFile);
                    if (shouldDelete && DeleteFileAndMeta(dstFile))
                        fileSystemChanged = true;

                    synced.Remove(rel);
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

            stateChanged |= SetSyncPaths(config, synced);
            return copiedCount;
        }

        private static List<SourceCandidate> CollectSourceCandidates(
            SyncConfig config,
            string srcRoot,
            string dstRoot,
            string srcAssetRoot,
            string dstAssetRoot,
            HashSet<string> sourceIgnorePaths)
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

                if (sourceIgnorePaths.Contains(normalizedRel))
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
            HashSet<string> synced,
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

                if (synced.Add(candidate.NormalizedRelativePath))
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

        private static bool TryAddDestinationIgnoreGuid(SyncConfig config, string destinationAssetPath, out string guid)
        {
            guid = AssetDatabase.AssetPathToGUID(destinationAssetPath);
            if (!string.IsNullOrEmpty(guid))
                return true;

            AssetDatabase.ImportAsset(destinationAssetPath, ImportAssetOptions.ForceSynchronousImport);
            guid = AssetDatabase.AssetPathToGUID(destinationAssetPath);
            return !string.IsNullOrEmpty(guid);
        }

        internal static IgnoreEntryState GetIgnoreEntryState(
            SyncConfig config,
            string guid,
            out string assetPath,
            out string normalizedRelativePath)
        {
            assetPath = string.Empty;
            normalizedRelativePath = string.Empty;

            if (string.IsNullOrWhiteSpace(guid) || config == null)
                return IgnoreEntryState.Invalid;

            assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(assetPath))
                return IgnoreEntryState.Invalid;

            var state = ClassifyIgnoreAssetPath(
                assetPath,
                config.sourcePath,
                config.destinationPath,
                out normalizedRelativePath);
            if ((state == IgnoreEntryState.Source || state == IgnoreEntryState.Destination)
                && string.IsNullOrEmpty(normalizedRelativePath))
                return IgnoreEntryState.Invalid;

            return state;
        }

        private static IgnoreEntryState ClassifyIgnoreAssetPath(
            string assetPath,
            string sourceAssetRoot,
            string destinationAssetRoot,
            out string normalizedRelativePath)
        {
            normalizedRelativePath = string.Empty;
            if (string.IsNullOrEmpty(assetPath))
                return IgnoreEntryState.Invalid;

            if (!string.IsNullOrEmpty(destinationAssetRoot)
                && AssetSyncPostprocessor.IsAssetPathWithinRoot(assetPath, destinationAssetRoot))
            {
                normalizedRelativePath = NormalizeRelativePath(GetRelativeAssetPath(assetPath, destinationAssetRoot));
                return IgnoreEntryState.Destination;
            }

            if (!string.IsNullOrEmpty(sourceAssetRoot)
                && AssetSyncPostprocessor.IsAssetPathWithinRoot(assetPath, sourceAssetRoot))
            {
                normalizedRelativePath = NormalizeRelativePath(GetRelativeAssetPath(assetPath, sourceAssetRoot));
                return IgnoreEntryState.Source;
            }

            return IgnoreEntryState.Invalid;
        }

        private static string GetRelativeAssetPath(string assetPath, string rootAssetPath)
        {
            string normalizedAsset = NormalizeAssetPath(assetPath);
            string normalizedRoot = NormalizeAssetPath(rootAssetPath);
            if (normalizedAsset.Equals(normalizedRoot, StringComparison.Ordinal))
                return string.Empty;

            return normalizedAsset.Substring(normalizedRoot.Length + 1);
        }

        private static void BuildIgnorePathSets(
            SyncConfig config,
            HashSet<string> sourceIgnorePaths,
            HashSet<string> destinationIgnorePaths)
        {
            sourceIgnorePaths.Clear();
            destinationIgnorePaths.Clear();

            if (config?.ignoreGuids == null)
                return;

            foreach (string guid in config.ignoreGuids)
            {
                var state = GetIgnoreEntryState(config, guid, out _, out string rel);
                if (string.IsNullOrEmpty(rel))
                    continue;

                if (state == IgnoreEntryState.Source)
                    sourceIgnorePaths.Add(rel);
                else if (state == IgnoreEntryState.Destination)
                    destinationIgnorePaths.Add(rel);
            }
        }

        internal static HashSet<string> CollectDestinationIgnoreRelativePaths(SyncConfig config)
        {
            var sourceIgnorePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var destinationIgnorePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            BuildIgnorePathSets(config, sourceIgnorePaths, destinationIgnorePaths);
            return destinationIgnorePaths;
        }

        internal static IReadOnlyList<PreviewCopyEntry> CollectCopyPreviewEntries(
            SyncConfig config,
            bool includeUnchanged = false)
        {
            if (config == null)
                return Array.Empty<PreviewCopyEntry>();
            if (string.IsNullOrEmpty(config.sourcePath) || string.IsNullOrEmpty(config.destinationPath))
                return Array.Empty<PreviewCopyEntry>();

            string srcRoot = ToFullPath(config.sourcePath);
            string dstRoot = ToFullPath(config.destinationPath);
            if (!Directory.Exists(srcRoot))
                return Array.Empty<PreviewCopyEntry>();

            var warningProbe = new SyncConfig
            {
                enabled = true,
                includeSubdirectories = config.includeSubdirectories,
                sourcePath = config.sourcePath,
                destinationPath = config.destinationPath
            };
            if (TryGetConfigWarning(warningProbe, out _))
                return Array.Empty<PreviewCopyEntry>();

            var sourceIgnorePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var destinationIgnorePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            BuildIgnorePathSets(config, sourceIgnorePaths, destinationIgnorePaths);

            var synced = new HashSet<string>(
                (config.syncRelativePaths ?? new List<string>())
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Select(NormalizeRelativePath)
                    .Where(p => !string.IsNullOrEmpty(p)),
                StringComparer.OrdinalIgnoreCase);

            var candidates = CollectSourceCandidates(
                config,
                srcRoot,
                dstRoot,
                config.sourcePath,
                config.destinationPath,
                sourceIgnorePaths);

            var result = new List<PreviewCopyEntry>();
            foreach (var candidate in candidates.OrderBy(c => c.NormalizedRelativePath, StringComparer.OrdinalIgnoreCase))
            {
                string rel = candidate.NormalizedRelativePath;
                if (destinationIgnorePaths.Contains(rel))
                    continue;

                bool isSynced = synced.Contains(rel);
                bool destinationExists = File.Exists(candidate.DestinationFilePath);
                // Unsynced existing destination files are conflict candidates and are excluded from auto-copy preview.
                if (!isSynced && destinationExists)
                    continue;

                if (!includeUnchanged && !ShouldCopy(candidate.SourceFilePath, candidate.DestinationFilePath))
                    continue;

                result.Add(new PreviewCopyEntry(candidate.SourceAssetPath, candidate.DestinationAssetPath));
            }

            return result;
        }

        internal static IReadOnlyList<string> CollectCopyPreviewDestinationAssetPaths(SyncConfig config)
        {
            return CollectCopyPreviewEntries(config)
                .Select(e => e.DestinationAssetPath)
                .ToList();
        }

        internal static HashSet<string> CollectSyncedDestinationSyncRelativePaths(SyncConfig config)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (config?.syncRelativePaths == null)
                return result;

            HashSet<string> destinationIgnorePaths = CollectDestinationIgnoreRelativePaths(config);
            foreach (string relativePath in config.syncRelativePaths)
            {
                string normalizedRelativePath = NormalizeRelativePath(relativePath);
                if (string.IsNullOrEmpty(normalizedRelativePath))
                    continue;
                if (destinationIgnorePaths.Contains(normalizedRelativePath))
                    continue;

                result.Add(normalizedRelativePath);
            }

            return result;
        }

        internal static HashSet<string> CollectExistingSyncedDestinationFilesForDeletedConfig(SyncConfig config)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (config == null || string.IsNullOrEmpty(config.destinationPath))
                return result;

            string dstRoot = ToFullPath(config.destinationPath);
            HashSet<string> syncedDestinationPaths = CollectSyncedDestinationSyncRelativePaths(config);
            foreach (string rel in syncedDestinationPaths)
            {
                string relSystem = NormalizedRelativePathToSystemPath(rel);
                string dstFilePath = Path.GetFullPath(Path.Combine(dstRoot, relSystem));
                if (File.Exists(dstFilePath))
                    result.Add(dstFilePath);
            }

            return result;
        }

        internal static HashSet<string> CollectExistingSyncedDestinationFilesForDeletedSettings(AssetSyncSettings settings)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (settings?.syncConfigs == null || settings.syncConfigs.Count == 0)
                return result;

            foreach (var config in settings.syncConfigs)
            {
                if (config == null)
                    continue;

                result.UnionWith(CollectExistingSyncedDestinationFilesForDeletedConfig(config));
            }

            return result;
        }

        internal static bool RemoveSyncedFilesForDeletedConfig(SyncConfig config, bool refreshAssetDatabase = true)
        {
            if (config == null || string.IsNullOrEmpty(config.destinationPath))
                return false;

            HashSet<string> syncedDestinationPaths = CollectSyncedDestinationSyncRelativePaths(config);
            if (syncedDestinationPaths.Count == 0)
                return false;

            string dstRoot = ToFullPath(config.destinationPath);
            bool fileSystemChanged = false;

            foreach (string rel in syncedDestinationPaths)
            {
                string relSystem = NormalizedRelativePathToSystemPath(rel);
                string dstFilePath = Path.Combine(dstRoot, relSystem);
                if (DeleteFileAndMeta(dstFilePath))
                    fileSystemChanged = true;
            }

            if (refreshAssetDatabase && fileSystemChanged)
                AssetDatabase.Refresh();

            return fileSystemChanged;
        }

        internal static bool RemoveSyncedFilesForDeletedSettings(AssetSyncSettings settings)
        {
            if (settings?.syncConfigs == null || settings.syncConfigs.Count == 0)
                return false;

            bool fileSystemChanged = false;
            foreach (var config in settings.syncConfigs)
            {
                if (config == null)
                    continue;

                if (RemoveSyncedFilesForDeletedConfig(config, refreshAssetDatabase: false))
                    fileSystemChanged = true;
            }

            if (fileSystemChanged)
                AssetDatabase.Refresh();

            return fileSystemChanged;
        }

        internal static bool NormalizeState(SyncConfig config)
        {
            if (config == null)
                return false;

            config.syncRelativePaths ??= new List<string>();
            bool ignoreListInitialized = false;
            if (config.ignoreGuids == null)
            {
                config.ignoreGuids = new List<string>();
                ignoreListInitialized = true;
            }

            var normalizedSync = config.syncRelativePaths
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(NormalizeRelativePath)
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            bool changed = ignoreListInitialized
                || !ListEquals(config.syncRelativePaths, normalizedSync, StringComparer.OrdinalIgnoreCase);

            if (changed)
            {
                config.syncRelativePaths = normalizedSync;
            }

            return changed;
        }

        private static bool SetSyncPaths(SyncConfig config, HashSet<string> synced)
        {
            config.syncRelativePaths ??= new List<string>();
            var normalizedSync = synced
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(NormalizeRelativePath)
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            bool changed = !ListEquals(config.syncRelativePaths, normalizedSync, StringComparer.OrdinalIgnoreCase);
            if (changed)
                config.syncRelativePaths = normalizedSync;

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

    public class AssetSyncPostprocessor : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            string[] guids = AssetDatabase.FindAssets("t:AssetSyncSettings");
            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                var settings = AssetDatabase.LoadAssetAtPath<AssetSyncSettings>(assetPath);
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

    public class AssetSyncSettingsDeletionProcessor : AssetModificationProcessor
    {
        internal static Func<string, string, string, string, bool> DisplayDialogOverride;

        private static AssetDeleteResult OnWillDeleteAsset(string assetPath, RemoveAssetOptions _)
        {
            return HandleWillDeleteAsset(assetPath);
        }

        internal static AssetDeleteResult HandleWillDeleteAsset(string assetPath)
        {
            HashSet<string> filesToDelete = CollectExistingSyncedDestinationFilesForDeletedSettingsAsset(assetPath);
            if (filesToDelete.Count > 0)
            {
                string fileLabel = filesToDelete.Count == 1 ? "file" : "files";
                bool approved = ShowDeleteWarningDialog(
                    "Delete AssetSync Settings",
                    $"Deleting this target will also delete {filesToDelete.Count} synced destination {fileLabel}.{Environment.NewLine}{Environment.NewLine}Continue?");
                if (!approved)
                    return AssetDeleteResult.FailedDelete;
            }

            TryCleanupSyncedFilesFromDeletedSettingsAsset(assetPath);
            return AssetDeleteResult.DidNotDelete;
        }

        internal static bool TryCleanupSyncedFilesFromDeletedSettingsAsset(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return false;

            if (AssetDatabase.IsValidFolder(assetPath))
            {
                bool changed = false;
                string[] settingsGuids = AssetDatabase.FindAssets("t:AssetSyncSettings", new[] { assetPath });
                foreach (string guid in settingsGuids)
                {
                    string settingsPath = AssetDatabase.GUIDToAssetPath(guid);
                    var settings = AssetDatabase.LoadAssetAtPath<AssetSyncSettings>(settingsPath);
                    if (settings == null)
                        continue;

                    if (AssetSyncer.RemoveSyncedFilesForDeletedSettings(settings))
                        changed = true;
                }

                return changed;
            }

            var singleSettings = AssetDatabase.LoadAssetAtPath<AssetSyncSettings>(assetPath);
            if (singleSettings == null)
                return false;

            return AssetSyncer.RemoveSyncedFilesForDeletedSettings(singleSettings);
        }

        internal static HashSet<string> CollectExistingSyncedDestinationFilesForDeletedSettingsAsset(string assetPath)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(assetPath))
                return result;

            if (AssetDatabase.IsValidFolder(assetPath))
            {
                string[] settingsGuids = AssetDatabase.FindAssets("t:AssetSyncSettings", new[] { assetPath });
                foreach (string guid in settingsGuids)
                {
                    string settingsPath = AssetDatabase.GUIDToAssetPath(guid);
                    var settings = AssetDatabase.LoadAssetAtPath<AssetSyncSettings>(settingsPath);
                    if (settings == null)
                        continue;

                    result.UnionWith(AssetSyncer.CollectExistingSyncedDestinationFilesForDeletedSettings(settings));
                }

                return result;
            }

            var singleSettings = AssetDatabase.LoadAssetAtPath<AssetSyncSettings>(assetPath);
            if (singleSettings == null)
                return result;

            result.UnionWith(AssetSyncer.CollectExistingSyncedDestinationFilesForDeletedSettings(singleSettings));
            return result;
        }

        private static bool ShowDeleteWarningDialog(string title, string message)
        {
            if (DisplayDialogOverride != null)
                return DisplayDialogOverride(title, message, "Delete", "Cancel");

            return EditorUtility.DisplayDialog(title, message, "Delete", "Cancel");
        }
    }
}
