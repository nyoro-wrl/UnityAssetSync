using System.Collections.Generic;

namespace Nyorowrl.AssetSync
{
    public enum FilterConditionTargetKind
    {
        Type = 0,
        Asset = 1,
        Extension = 2
    }

    [System.Serializable]
    public class FilterCondition
    {
        public FilterConditionTargetKind targetKind;
        public bool invert;
        public List<string> multipleTypeNames = new List<string>();
        public List<string> multipleAssetGuids = new List<string>();
        public List<string> multipleExtensions = new List<string>();
    }
}
