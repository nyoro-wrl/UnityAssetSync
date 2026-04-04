# Unity Asset Sync

フォルダから別のフォルダへアセットを一方向に同期するEditor専用パッケージです。

.metaファイルは同期対象に含まれないため、同じアセットで別のインポート設定（プリセット）を適用することができます。

アセットの同期条件は型でフィルタリングでき、必要なものだけ同期するか除外するかを選べます。

## インストール

Package Manager の `Add package from git URL...` に、次の URL をそのまま入力してください。

```text
https://github.com/nyoro-wrl/Unity-AssetSync.git?path=/Packages/com.nyoro_wrl.assetsync
```
