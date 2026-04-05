![Unity Asset Sync](Packages/com.nyoro_wrl.assetsync/Editor/Icons/icon.png)

# Unity Asset Sync

フォルダから別のフォルダに、`.meta`ファイル以外のファイルを単方向で同期するEditor専用パッケージ。

使い道は1つに決めていませんが、例えばアセットをコピーして、それぞれに別のインポート設定を割り当てる際などに活用できます。
この場合は[EnforcePresetPostProcessor](https://docs.unity3d.com/Manual/DefaultPresetsByFolder.html)と組み合わせるのがおすすめです。

## インストール

Package Manager の `Add package from git URL...` に、次の URL をそのまま入力してください。

```text
https://github.com/nyoro-wrl/UnityAssetSync.git?path=/Packages/com.nyoro_wrl.assetsync
```

## 主な機能

Source に指定したフォルダの内容を Destination に同期します。  

必要に応じて次のような制御ができます。

- サブフォルダを含めて同期する
- Unity の型を使って同期対象を絞る
- 同期元の特定のアセットを追加/除外する
- 同期先の特定のアセットを除外する

なお、同期先のファイルには🔃アイコンが表示され、同期によって作成されたファイルかどうか見分けがつくようになっています。

## 使い方

1. `Window > AssetSync` を開きます。
2. `New` ボタンで `AssetSyncSettings` アセットを作成します。
3. Config を追加して、`Source` と `Destination` のフォルダを指定します。
4. 必要に応じて `Include Subdirectories`, `Filters`, `Ignore Assets` を設定します。
5. `Source` と `Destination` にそれぞれ同期元と同期先のフォルダを指定します。
6. 同期が開始されます。
