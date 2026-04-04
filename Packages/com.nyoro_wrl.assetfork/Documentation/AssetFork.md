# About AssetFork

AssetFork is an editor-only package that synchronizes assets from a source folder to a destination folder inside a Unity project.

It is designed for workflows where shared assets are copied into feature-specific folders while preserving manual files in the destination.

# Installing AssetFork

Install this package with the Unity Package Manager.

# Using AssetFork

1. Open `Window > AssetFork`.
2. Create a new `AssetForkSettings` asset with the `New` button, or select an existing one.
3. Add a config from the left panel.
4. Set `Source` and `Destination` folders.
5. Set `Include Subdirectories`:
   - default: `off`
   - `on`: synchronize files under nested folders.
   - `off`: synchronize only files directly under `Source`.
6. Enable filters if needed:
   - `Exclude = off`: include matching asset types.
   - `Exclude = on`: exclude matching asset types.
   - `Multiple Types = on`: one filter condition can match any of several types.
7. Edit assets in the source folder. AssetFork re-syncs automatically.

## Sync behavior

- Copies only files that are new or content-changed.
- Never copies `.meta` files from source.
- Tracks synchronized files per config in `SyncConfig.ownedRelativePaths` (saved in settings assets).
- Supports manual `Protected` entries (GUID-based): destination-protected files are never copied/updated/deleted.
- Treats source-protected files as sync-excluded (same behavior as filter exclusion).
- If a destination file already exists and is neither owned nor protected, AssetFork opens a conflict dialog to choose `Owned` or `Protected`.
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
| `Editor/AssetForkWindow.cs` | Main UI for config management. |
| `Editor/AssetSyncer.cs` | File synchronization and postprocess trigger logic. |
| `Editor/ConflictResolutionDialog.cs` | Batch conflict resolution dialog (`Owned` / `Protected`). |
| `Editor/ConfigTreeView.cs` | Config list/tree UI. |
| `Editor/TypeSelectorDropdown.cs` | Type selection dropdown for filters. |
| `Runtime/*.cs` | Serializable config models (`AssetForkSettings`, `SyncConfig`, `FilterCondition`) compiled for Editor only. |
| `Tests/Editor/*` | Editor tests for sync and filter logic. |
