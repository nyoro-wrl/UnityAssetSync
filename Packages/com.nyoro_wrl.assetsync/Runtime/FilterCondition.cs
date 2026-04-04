using System.Collections.Generic;

namespace Nyorowrl.AssetSync
{
    [System.Serializable]
    public class FilterCondition
    {
        public bool invert;
        public bool useMultipleTypes;
        public string singleTypeName;
        public List<string> multipleTypeNames = new List<string>();
    }
}
