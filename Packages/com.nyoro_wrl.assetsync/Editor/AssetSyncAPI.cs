using UnityEditor;

namespace Nyorowrl.AssetSync.Editor
{
    /// <summary>
    /// Scripting API for triggering asset synchronization from editor scripts.
    /// </summary>
    public static class AssetSyncAPI
    {
        /// <summary>
        /// Syncs all enabled configs found across every AssetSyncSettings asset in the project.
        /// </summary>
        /// <returns>Total number of files copied.</returns>
        public static int SyncAll()
        {
            int total = 0;
            string[] guids = AssetDatabase.FindAssets("t:AssetSyncSettings");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var settings = AssetDatabase.LoadAssetAtPath<AssetSyncSettings>(path);
                if (settings?.syncConfigs == null)
                    continue;
                foreach (var config in settings.syncConfigs)
                    total += AssetSyncer.SyncConfig(config);
            }
            return total;
        }

        /// <summary>
        /// Syncs the first config whose <c>configName</c> matches <paramref name="configName"/>.
        /// </summary>
        /// <returns>Number of files copied, or -1 if no matching config was found.</returns>
        public static int SyncByName(string configName)
        {
            string[] guids = AssetDatabase.FindAssets("t:AssetSyncSettings");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var settings = AssetDatabase.LoadAssetAtPath<AssetSyncSettings>(path);
                if (settings?.syncConfigs == null)
                    continue;
                foreach (var config in settings.syncConfigs)
                {
                    if (config.configName == configName)
                        return AssetSyncer.SyncConfig(config);
                }
            }
            return -1;
        }
    }
}
