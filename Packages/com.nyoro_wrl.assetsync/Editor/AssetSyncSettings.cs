using System.Collections.Generic;
using UnityEngine;

namespace Nyorowrl.AssetSync
{
    public class AssetSyncSettings : ScriptableObject
    {
        public List<SyncConfig> syncConfigs = new List<SyncConfig>();
    }
}
