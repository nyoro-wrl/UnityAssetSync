![Unity Asset Sync](Packages/com.nyoro_wrl.assetsync/Editor/Icons/icon.png)

# Unity Asset Sync

フォルダから別のフォルダに、`.meta`ファイル以外のファイルを単方向で同期するEditor専用パッケージ。

1つのアセットを元に、異なるプリセットを複数用意するなどの使い道があります。

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
- 特定のアセットだけ同期対象から外す

なお、同期先のファイルには🔃アイコンが表示され、同期によって作成されたファイルかどうか見分けがつくようになっています。

## 使い方

1. `Window > AssetSync` を開きます。
2. `New` ボタンで `AssetSyncSettings` アセットを作成します。
3. Config を追加して、`Source` と `Destination` のフォルダを指定します。
4. 必要に応じて `Include Subdirectories`, `Filters`, `Ignore Assets` を設定します。
5. `Source` と `Destination` にそれぞれ同期元と同期先のフォルダを指定します。
6. 同期が開始されます。
