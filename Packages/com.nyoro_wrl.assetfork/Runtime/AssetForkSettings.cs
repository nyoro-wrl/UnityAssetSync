using System.Collections.Generic;
using UnityEngine;

namespace Nyorowrl.Assetfork
{
    public class AssetForkSettings : ScriptableObject
    {
        public const string ResourcesPath = "AssetFork/AssetForkSettings";

        public List<SyncConfig> syncConfigs = new List<SyncConfig>();
    }
}
