using System.Collections.Generic;
using UnityEngine;

namespace Nyorowrl.Assetfork
{
    public class AssetForkSettings : ScriptableObject
    {
        public List<SyncConfig> syncConfigs = new List<SyncConfig>();
    }
}
