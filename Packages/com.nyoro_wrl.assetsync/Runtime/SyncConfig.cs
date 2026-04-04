using System.Collections.Generic;

namespace Nyorowrl.AssetSync
{
    [System.Serializable]
    public class SyncConfig
    {
        public bool enabled = true;
        public bool includeSubdirectories = false;
        public string configName;
        public string sourcePath;
        public string destinationPath;
        public List<FilterCondition> filters = new List<FilterCondition>();
        public List<string> syncRelativePaths = new List<string>();
        public List<string> ignoreGuids = new List<string>();
    }
}
