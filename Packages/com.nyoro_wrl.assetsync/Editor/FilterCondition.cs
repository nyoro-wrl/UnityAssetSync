using System.Collections.Generic;

namespace Nyorowrl.AssetSync
{
    public enum FilterConditionTargetKind
    {
        Type = 0,
        Asset = 1
    }

    [System.Serializable]
    public class FilterCondition
    {
        public FilterConditionTargetKind targetKind;
        public bool invert;
        public bool useMultipleTypes;
        public string singleTypeName;
        public List<string> multipleTypeNames = new List<string>();
        public string singleAssetGuid;
        public List<string> multipleAssetGuids = new List<string>();
    }
}
