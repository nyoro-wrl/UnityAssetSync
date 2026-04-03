using System.Collections.Generic;

namespace Nyorowrl.Assetfork
{
    [System.Serializable]
    public class SyncConfig
    {
        public bool enabled = true;
        public string configName;
        public string sourcePath;
        public string destinationPath;
        public List<FilterCondition> filters = new List<FilterCondition>();
    }
}
