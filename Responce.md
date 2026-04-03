Editorウィンドウでの型選択UIの実装方法について、SmartAddresserのコードベースから主要な実装パターンを抽出しました。

## 型選択UIの実装パターン

### 1. 動的型検出とコンテキストメニューによる選択
`AssetGroupPanelPresenter.AddFilter()` で実装されているパターンです。 [1](#0-0) 

```csharp
// IAssetFilterを継承した全ての型を取得
var types = TypeCache.GetTypesDerivedFrom<IAssetFilter>()
    .Where(x => !x.IsAbstract)
    .Where(x => !typeof(AssetFilterAsset).IsAssignableFrom(x))
    .Where(x => x.GetCustomAttribute<IgnoreAssetFilterAttribute>() == null);

// GenericMenuを生成して選択肢を表示
var menu = new GenericMenu();
foreach (var type in types)
{
    var attribute = type.GetCustomAttribute<AssetFilterAttribute>();
    var menuName = attribute == null ? ObjectNames.NicifyVariableName(type.Name) : attribute.MenuName;
    menu.AddItem(new GUIContent(menuName), false, () =>
    {
        var filter = (IAssetFilter)Activator.CreateInstance(type);
        // 選択された型のインスタンスを処理
    });
}
menu.ShowAsContext();
```

### 2. アセット選択ドロップダウン
`LayoutRuleEditorPresenter.OnAssetSelectButtonClicked()` での実装です。 [2](#0-1) 

```csharp
var menu = new GenericMenu();
var sourceAssets = _dataRepository.LoadAll().ToList();
var sourceAssetNames = sourceAssets.Select(y => y.name).ToArray();
var activeSourceAssetIndex = sourceAssets.IndexOf(_editingData.Value);

for (var i = 0; i < sourceAssetNames.Length; i++)
{
    var menuName = $"{sourceAssetNames[i]}";
    var isActive = activeSourceAssetIndex == i;
    var idx = i;
    menu.AddItem(new GUIContent(menuName), isActive, () =>
    {
        var asset = sourceAssets[idx];
        SetupActiveView(asset);
    });
}
menu.ShowAsContext();
```

### 3. ObjectFieldによる型選択
`SmartAddresserProjectSettingsProvider` での実装です。 [3](#0-2) 

```csharp
projectSettings.PrimaryData = (BaseLayoutRuleData)EditorGUILayout.ObjectField(
    "Primary Data",
    projectSettings.PrimaryData,
    typeof(BaseLayoutRuleData),
    false);
```

## 参考すべき主要コンポーネント

| コンポーネント | 用途 | 参照箇所 |
|---------|------|---------|
| `TypeCache.GetTypesDerivedFrom<T>()` | 特定の型を継承した全ての型を動的に取得 | AssetGroupPanelPresenter.cs:244-247 |
| `GenericMenu` | コンテキストメニューを表示 | AssetGroupPanelPresenter.cs:250-270 |
| `EditorGUILayout.ObjectField()` | Unity標準のオブジェクト選択フィールド | SmartAddresserProjectSettingsProvider.cs:39-42 |
| `AssetFilterAttribute` | 型の表示名をカスタマイズする属性 | AssetGroupPanelPresenter.cs:253-254 |

## 実装のポイント

1. **型の動的検出**: `TypeCache` を使用して実行時に特定のインターフェースを継承した型を取得
2. **表示名のカスタマイズ**: `AssetFilterAttribute` のようなカスタム属性でメニュー表示名を制御
3. **履歴管理**: `AutoIncrementHistory` を使用してUndo/Redo機能を実装
4. **UIの分離**: ViewとPresenterを分離してMVPパターンで実装

これらのパターンを組み合わせることで、柔軟な型選択UIを実装できます。

## Notes
- `AssetFilterView.cs` でも右クリックメニューによる型選択が実装されていますが、こちらはフィルターの操作メニューが中心です [4](#0-3) 
- 型選択後のインスタンス生成は `Activator.CreateInstance(type)` で行っています
- UnityのIMGUIを使用した実装であり、UI Toolkitの場合は異なるアプローチが必要です

Wiki pages you might want to explore:
- [Overview (CyberAgentGameEntertainment/SmartAddresser)](/wiki/CyberAgentGameEntertainment/SmartAddresser#1)
- [Layout Rule Editor (CyberAgentGameEntertainment/SmartAddresser)](/wiki/CyberAgentGameEntertainment/SmartAddresser#3.1)

### Citations

**File:** Assets/SmartAddresser/Editor/Core/Tools/Addresser/Shared/AssetGroups/AssetGroupPanelPresenter.cs (L243-270)
```csharp
                // Get types of all asset filter.
                var types = TypeCache.GetTypesDerivedFrom<IAssetFilter>()
                    .Where(x => !x.IsAbstract)
                    .Where(x => !typeof(AssetFilterAsset).IsAssignableFrom(x))
                    .Where(x => x.GetCustomAttribute<IgnoreAssetFilterAttribute>() == null);

                // Show filter selection menu.
                var menu = new GenericMenu();
                foreach (var type in types)
                {
                    var attribute = type.GetCustomAttribute<AssetFilterAttribute>();
                    var menuName = attribute == null ? ObjectNames.NicifyVariableName(type.Name) : attribute.MenuName;
                    menu.AddItem(new GUIContent(menuName), false, () =>
                    {
                        var filter = (IAssetFilter)Activator.CreateInstance(type);
                        _history.Register($"Add Filter {filter.Id}", () =>
                        {
                            _group.Filters.Add(filter);
                            _saveService.Save();
                        }, () =>
                        {
                            _group.Filters.Remove(filter);
                            _saveService.Save();
                        });
                    });
                }

                menu.ShowAsContext();
```

**File:** Assets/SmartAddresser/Editor/Core/Tools/Addresser/LayoutRuleEditor/LayoutRuleEditorPresenter.cs (L212-241)
```csharp
            void OnAssetSelectButtonClicked()
            {
                if (!_didSetupView)
                    return;

                var menu = new GenericMenu();

                var sourceAssets = _dataRepository.LoadAll().ToList();
                var sourceAssetNames = sourceAssets.Select(y => y.name).ToArray();
                var activeSourceAssetIndex = sourceAssets.IndexOf(_editingData.Value);
                if (activeSourceAssetIndex == -1)
                    activeSourceAssetIndex = 0;

                for (var i = 0; i < sourceAssetNames.Length; i++)
                {
                    var menuName = $"{sourceAssetNames[i]}";
                    var isActive = activeSourceAssetIndex == i;
                    var idx = i;
                    menu.AddItem(new GUIContent(menuName),
                        isActive,
                        () =>
                        {
                            var asset = sourceAssets[idx];
                            SetupActiveView(asset);
                            _dataRepository.SetEditingData(asset);
                        });
                }

                menu.ShowAsContext();
            }
```

**File:** Assets/SmartAddresser/Editor/Core/Tools/Shared/SmartAddresserProjectSettingsProvider.cs (L37-42)
```csharp
                    var oldData = projectSettings.PrimaryData;
                    projectSettings.PrimaryData =
                        (BaseLayoutRuleData)EditorGUILayout.ObjectField("Primary Data",
                                                                        projectSettings.PrimaryData,
                                                                        typeof(BaseLayoutRuleData),
                                                                        false);
```

**File:** Assets/SmartAddresser/Editor/Core/Tools/Addresser/Shared/AssetGroups/AssetFilterView.cs (L123-149)
```csharp
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent(RemoveMenuName), false,
                    () => _removeMenuExecutedSubject.OnNext(Empty.Default));
                menu.AddItem(new GUIContent(MoveUpMenuName), false,
                    () => _moveUpMenuExecutedSubject.OnNext(Empty.Default));

                var moveUpByList = GetMoveUpByOptions?.Invoke();
                if (moveUpByList == null || moveUpByList.Count == 0)
                    menu.AddDisabledItem(new GUIContent(MoveUpByMenuName), false);
                else
                    foreach (var count in moveUpByList)
                        menu.AddItem(new GUIContent($"{MoveUpByMenuName}/{count}"), false,
                                     () => _moveUpByMenuExecutedSubject.OnNext(count));

                menu.AddItem(new GUIContent(MoveDownMenuName), false,
                    () => _moveDownMenuExecutedSubject.OnNext(Empty.Default));

                var moveDownByList = GetMoveDownByOptions?.Invoke();
                if (moveDownByList == null || moveDownByList.Count == 0)
                    menu.AddDisabledItem(new GUIContent(MoveDownByMenuName), false);
                else
                    foreach (var count in moveDownByList)
                        menu.AddItem(new GUIContent($"{MoveDownByMenuName}/{count}"), false,
                                     () => _moveDownByMenuExecutedSubject.OnNext(count));

                menu.AddItem(new GUIContent(CopyMenuName), false,
                    () => _copyMenuExecutedSubject.OnNext(Empty.Default));
```
