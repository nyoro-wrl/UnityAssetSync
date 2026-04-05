using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using Nyorowrl.AssetSync;

namespace Nyorowrl.AssetSync.Editor
{
    internal class ConfigTreeView : TreeView<int>
    {
        private const int AddRowId = -2;
        private const float ListRowHeight = 22f;

        public AssetSyncSettings Settings { get; set; }

        private readonly Action<SyncConfig, string> _onRenamed;
        private readonly Action<int> _onDeleted;
        private readonly Action _onAddRequested;
        private readonly Action<int> _onSelectionChanged;

        public int SelectedIndex =>
            HasSelection() ? GetSelection()[0] : -1;

        public ConfigTreeView(TreeViewState<int> state, AssetSyncSettings settings,
            Action<SyncConfig, string> onRenamed,
            Action<int> onDeleted, Action onAddRequested, Action<int> onSelectionChanged = null)
            : base(state)
        {
            Settings = settings;
            _onRenamed = onRenamed;
            _onDeleted = onDeleted;
            _onAddRequested = onAddRequested;
            _onSelectionChanged = onSelectionChanged;
            showAlternatingRowBackgrounds = false;
            showBorder = true;
            rowHeight = ListRowHeight;
            Reload();
        }

        protected override TreeViewItem<int> BuildRoot()
        {
            var root = new TreeViewItem<int>(-1, -1, "Root");
            var items = new List<TreeViewItem<int>>();

            if (Settings != null)
            {
                for (int i = 0; i < Settings.syncConfigs.Count; i++)
                {
                    string name = Settings.syncConfigs[i].configName;
                    if (string.IsNullOrEmpty(name)) name = $"Sync {i + 1}";
                    items.Add(new TreeViewItem<int>(i, 0, name));
                }
            }

            items.Add(new TreeViewItem<int>(AddRowId, 0, "+ Add Sync"));

            SetupParentsAndChildrenFromDepths(root, items);
            return root;
        }

        protected override bool CanRename(TreeViewItem<int> item) => item.id != AddRowId;

        protected override void RenameEnded(RenameEndedArgs args)
        {
            if (!args.acceptedRename || Settings == null) return;
            if (args.itemID < 0 || args.itemID >= Settings.syncConfigs.Count) return;

            var config = Settings.syncConfigs[args.itemID];
            string newName = string.IsNullOrEmpty(args.newName)
                ? (string.IsNullOrEmpty(config.configName) ? $"Sync {args.itemID + 1}" : config.configName)
                : args.newName;

            _onRenamed?.Invoke(config, newName);

            var item = (TreeViewItem<int>)FindItem(args.itemID, rootItem);
            if (item != null) item.displayName = newName;
        }

        protected override bool CanMultiSelect(TreeViewItem<int> item) => false;

        protected override void ContextClickedItem(int id)
        {
            if (id == AddRowId) return;

            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Remove"), false, () => _onDeleted?.Invoke(id));
            menu.ShowAsContext();
        }

        protected override void SelectionChanged(IList<int> selectedIds)
        {
            base.SelectionChanged(selectedIds);

            int selectedId = selectedIds != null && selectedIds.Count > 0
                ? selectedIds[0]
                : -1;
            if (selectedId == AddRowId)
                selectedId = -1;

            _onSelectionChanged?.Invoke(selectedId);
        }

        protected override void RowGUI(RowGUIArgs args)
        {
            if (args.item.id == AddRowId)
            {
                var addRowRect = new Rect(args.rowRect.x + 4f, args.rowRect.y + 2f, args.rowRect.width - 8f, args.rowRect.height - 4f);
                if (GUI.Button(addRowRect, args.label, EditorStyles.miniButton))
                    _onAddRequested?.Invoke();
                return;
            }

            if (Settings == null || args.item.id >= Settings.syncConfigs.Count) return;

            var config = Settings.syncConfigs[args.item.id];
            Rect row = args.rowRect;
            bool hasWarning = AssetSyncer.TryGetConfigWarning(config, out string warningMessage);

            if (!config.enabled) GUI.color = new Color(1, 1, 1, 0.5f);
            float labelHeight = EditorGUIUtility.singleLineHeight;
            float labelY = row.y + (row.height - labelHeight) * 0.5f;
            float labelX = row.x + 2f;
            float reservedRight = hasWarning ? 20f : 4f;
            args.rowRect = new Rect(labelX, labelY, row.width - reservedRight, labelHeight);
            base.RowGUI(args);

            if (hasWarning)
            {
                var iconContent = EditorGUIUtility.IconContent("console.warnicon.sml");
                var iconRect = new Rect(row.xMax - 18f, row.y + (row.height - 16f) * 0.5f, 16f, 16f);
                GUI.Label(iconRect, new GUIContent(iconContent.image, warningMessage));
            }

            GUI.color = Color.white;
        }

        protected override void KeyEvent()
        {
            base.KeyEvent();

            Event evt = Event.current;
            if (evt == null || evt.type != EventType.KeyDown || EditorGUIUtility.editingTextField)
                return;

            if ((evt.keyCode != KeyCode.Delete && evt.keyCode != KeyCode.Backspace) || !HasSelection())
                return;

            int selectedId = GetSelection()[0];
            if (selectedId < 0 || Settings == null || selectedId >= Settings.syncConfigs.Count)
                return;

            _onDeleted?.Invoke(selectedId);
            evt.Use();
        }

        public void SelectAndBeginRename(int id)
        {
            SetSelection(new[] { id }, TreeViewSelectionOptions.RevealAndFrame);
            var item = (TreeViewItem<int>)FindItem(id, rootItem);
            if (item != null) BeginRename(item);
        }
    }
}
