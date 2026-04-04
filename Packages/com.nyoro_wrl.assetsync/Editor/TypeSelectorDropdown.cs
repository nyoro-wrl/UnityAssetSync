using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Nyorowrl.AssetSync.Editor
{
    internal class TypeSelectorDropdown : AdvancedDropdown
    {
        private class TypeDropdownItem : AdvancedDropdownItem
        {
            public readonly string TypeName;

            public TypeDropdownItem(string displayName, string typeName)
                : base(displayName)
            {
                TypeName = typeName;
            }
        }

        private readonly Action<string> _onSelected;

        internal TypeSelectorDropdown(AdvancedDropdownState state, Action<string> onSelected)
            : base(state)
        {
            _onSelected = onSelected;
            minimumSize = new Vector2(220, 320);
        }

        protected override AdvancedDropdownItem BuildRoot()
        {
            var root = new AdvancedDropdownItem("Type");

            root.AddChild(new TypeDropdownItem("(None)", ""));

            var types = TypeCache.GetTypesDerivedFrom<UnityEngine.Object>()
                .Where(t => !t.IsAbstract)
                .OrderBy(t => t.FullName);

            var namespaceGroups = new Dictionary<string, AdvancedDropdownItem>();

            foreach (var type in types)
            {
                string displayName = ObjectNames.NicifyVariableName(type.Name);

                AdvancedDropdownItem parent = string.IsNullOrEmpty(type.Namespace)
                    ? root
                    : GetOrCreateNamespaceGroup(root, namespaceGroups, type.Namespace);

                parent.AddChild(new TypeDropdownItem(displayName, type.AssemblyQualifiedName));
            }

            return root;
        }

        private static AdvancedDropdownItem GetOrCreateNamespaceGroup(
            AdvancedDropdownItem root,
            Dictionary<string, AdvancedDropdownItem> groups,
            string ns)
        {
            if (groups.TryGetValue(ns, out var existing))
                return existing;

            int dotIndex = ns.LastIndexOf('.');
            AdvancedDropdownItem parent;
            string groupName;

            if (dotIndex < 0)
            {
                parent = root;
                groupName = ns;
            }
            else
            {
                string parentNs = ns.Substring(0, dotIndex);
                parent = GetOrCreateNamespaceGroup(root, groups, parentNs);
                groupName = ns.Substring(dotIndex + 1);
            }

            var group = new AdvancedDropdownItem(groupName);
            groups[ns] = group;
            parent.AddChild(group);
            return group;
        }

        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            if (item is TypeDropdownItem typeItem)
                _onSelected(typeItem.TypeName);
        }
    }
}
