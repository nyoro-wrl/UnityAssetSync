# About AssetSync

AssetSync is an editor-only package that synchronizes assets from a source folder to a destination folder inside a Unity project.

It is designed for workflows where shared assets are copied into feature-specific folders while preserving manual files in the destination.

# Installing AssetSync

Install this package with the Unity Package Manager.

# Using AssetSync

1. Open `Window > AssetSync`.
2. Create a new `AssetSyncSettings` asset with the `New` button, or select an existing one.
3. Add a config from the left panel.
4. Set `Source` and `Destination` folders.
5. Set `Include Subdirectories`:
   - default: `off`
   - `on`: synchronize files under nested folders.
   - `off`: synchronize only files directly under `Source`.
6. Enable filters if needed:
   - `Action = Include`: include matching targets.
   - `Action = Exclude`: exclude matching targets.
   - `Target = Type`: select Unity object types.
   - `Target = Asset`: select source assets/folders.
   - `List Mode = on`: one filter condition can match any of several targets.
7. Optionally add assets to `Protected`:
   - destination protected entries are never copied/updated/deleted by sync.
8. If a destination file already exists as unsynced, resolve it in the conflict dialog with `Overwrite` or `Keep`.
9. Edit assets in the source folder. AssetSync re-syncs automatically.

## Sync behavior

- Copies only files that are new or content-changed.
- Never copies `.meta` files from source.
- Tracks synchronized files per config in `SyncConfig.syncRelativePaths` (saved in settings assets).
- Supports manual `Protected` entries (GUID-based): destination protected files/folders are never copied/updated/deleted.
- Supports filter target kinds `Type` and `Asset`; asset target folders apply recursively to all descendants.
- If a destination file already exists and is neither sync nor protected, AssetSync opens a conflict dialog to choose `Overwrite` or `Keep`.
- Disabling a config removes destination files tracked as sync, while preserving manual files and protected destination entries.
- Synced destination assets are shown with an icon badge in the Project window (excluded for protected entries or disabled configs).
- If selected source or destination folders are moved, stored config paths are remapped automatically.

## Safety rules

- Source and destination must be different folders.
- If `Include Subdirectories` is `on`, source and destination must not be nested in each other.

# Technical details

## Requirements

- Unity `6000.3.9f1` or compatible `6000.3` release.
- Unity Editor only (this package is not for player runtime use).

## Package contents

| Location | Description |
|---|---|
| `Editor/AssetSyncWindow.cs` | Main UI for config management. |
| `Editor/AssetSyncer.cs` | File synchronization and postprocess trigger logic. |
| `Editor/ConflictResolutionDialog.cs` | Batch conflict resolution dialog (`Overwrite` / `Keep`). |
| `Editor/ConfigTreeView.cs` | Config list/tree UI. |
| `Editor/TypeSelectorDropdown.cs` | Type selection dropdown for filters. |
| `Editor/SyncedAssetProjectWindowOverlay.cs` | Project-window icon overlay for synced destination assets. |
| `Runtime/*.cs` | Serializable config models (`AssetSyncSettings`, `SyncConfig`, `FilterCondition`) compiled for Editor only. |
| `Tests/Editor/*` | Editor tests for sync and filter logic. |
