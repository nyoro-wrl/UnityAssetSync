using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using Nyorowrl.Assetfork;

namespace Nyorowrl.Assetfork.Editor
{
    internal class ConfigTreeView : TreeView<int>
    {
        public AssetForkSettings Settings { get; set; }

        private readonly Action<SyncConfig, bool> _onEnabledChanged;
        private readonly Action<SyncConfig, string> _onRenamed;
        private readonly Action<int> _onDeleted;

        public int SelectedIndex =>
            HasSelection() ? GetSelection()[0] : -1;

        public ConfigTreeView(TreeViewState<int> state, AssetForkSettings settings,
            Action<SyncConfig, bool> onEnabledChanged, Action<SyncConfig, string> onRenamed,
            Action<int> onDeleted)
            : base(state)
        {
            Settings = settings;
            _onEnabledChanged = onEnabledChanged;
            _onRenamed = onRenamed;
            _onDeleted = onDeleted;
            showAlternatingRowBackgrounds = false;
            showBorder = true;
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
                    if (string.IsNullOrEmpty(name)) name = $"Config {i + 1}";
                    items.Add(new TreeViewItem<int>(i, 0, name));
                }
            }

            SetupParentsAndChildrenFromDepths(root, items);
            return root;
        }

        protected override bool CanRename(TreeViewItem<int> item) => true;

        protected override void RenameEnded(RenameEndedArgs args)
        {
            if (!args.acceptedRename || Settings == null) return;
            if (args.itemID < 0 || args.itemID >= Settings.syncConfigs.Count) return;

            var config = Settings.syncConfigs[args.itemID];
            string newName = string.IsNullOrEmpty(args.newName)
                ? (string.IsNullOrEmpty(config.configName) ? $"Config {args.itemID + 1}" : config.configName)
                : args.newName;

            _onRenamed?.Invoke(config, newName);

            var item = (TreeViewItem<int>)FindItem(args.itemID, rootItem);
            if (item != null) item.displayName = newName;
        }

        protected override bool CanMultiSelect(TreeViewItem<int> item) => false;

        protected override void ContextClickedItem(int id)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("削除"), false, () => _onDeleted?.Invoke(id));
            menu.ShowAsContext();
        }

        protected override void RowGUI(RowGUIArgs args)
        {
            if (Settings == null || args.item.id >= Settings.syncConfigs.Count) return;

            var config = Settings.syncConfigs[args.item.id];
            Rect row = args.rowRect;
            float indent = GetContentIndent(args.item);

            // 有効/無効トグル
            var toggleRect = new Rect(row.x + indent, row.y + 1, 16, row.height - 2);
            bool newEnabled = EditorGUI.Toggle(toggleRect, config.enabled);
            if (newEnabled != config.enabled)
                _onEnabledChanged?.Invoke(config, newEnabled);

            // 名前ラベル（リネーム時はテキストフィールド）
            if (!config.enabled) GUI.color = new Color(1, 1, 1, 0.5f);
            args.rowRect = new Rect(toggleRect.xMax + 2, row.y, row.xMax - toggleRect.xMax - 2, row.height);
            base.RowGUI(args);
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
