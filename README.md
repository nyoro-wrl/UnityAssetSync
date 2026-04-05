![Unity Asset Sync](Packages/com.nyoro_wrl.assetsync/Editor/Icons/icon.png)

# Unity Asset Sync

フォルダから別のフォルダに、`.meta`ファイル以外のファイルを単方向で同期するEditor専用パッケージ。

アセットをコピーして、それぞれに別のインポート設定を割り当てる際などに活用できます。[EnforcePresetPostProcessor](https://docs.unity3d.com/Manual/DefaultPresetsByFolder.html)と併用するのがおすすめです。

## インストール

Package Manager の `Add package from git URL...` に、次の URL をそのまま入力してください。

```text
https://github.com/nyoro-wrl/UnityAssetSync.git?path=/Packages/com.nyoro_wrl.assetsync
```

## 主な機能

Source に指定したフォルダの内容を Destination に同期します。  

必要に応じて次のような制御ができます。

- サブディレクトリを含めて同期する
- Unity の型を使って同期対象を絞る
- 同期元の特定のアセット（またはディレクトリ）を追加/除外する
- 同期先の特定のアセットを除外する

## 使い方

1. `ウィンドウ > Asset Sync` を開きます。
2. `New` ボタンで `AssetSyncSettings` アセットを作成します。
3. `Add Config` でConfigを追加して、`Source(同期元)` と `Destination(同期先)` のフォルダを指定します。
4. `Sync` ボタンで同期が開始します。
5. それ以降は `Enable` で有効/無効を切り替えます。
