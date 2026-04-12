# About AssetSync

AssetSync is an editor-only package that synchronizes assets from a source folder to a destination folder inside a Unity project.

It is designed for workflows where shared assets are copied into feature-specific folders while preserving manual files in the destination.

# Installing AssetSync

Install this package with the Unity Package Manager.

# Using AssetSync

1. Open `Window > Asset Sync`.
2. Create a new `AssetSyncSettings` asset with the `New` button, or select an existing one.
3. Add a config from the left panel.
4. Set `Source` and `Destination` folders.
   - `Source` accepts either a project folder path (for example: `Assets/Shared`) or an external absolute directory path.
   - Use the `Browse` button on `Source` to select any directory. Paths under the project that are valid Unity folders are stored as project asset paths.
5. Click `Sync` (bottom-right) to run the initial activation sync.
   - The button is enabled only when `Source` and `Destination` are valid folders.
   - After activation, the `Enable` toggle is shown for that config.
6. Set `Include Subdirectories`:
   - default: `off`
   - `on`: synchronize files under nested folders.
   - `off`: synchronize only files directly under `Source`.
7. Set `Keep Empty Directories` (optional):
   - default: `off`
   - `on`: keep empty source directory structure in destination.
   - `off`: create only directories required for synchronized files.
8. Enable filters if needed:
   - `Action = Include`: include matching targets.
   - `Action = Exclude`: exclude matching targets.
   - `Target = Type`: select Unity object types.
   - `Target = Asset`: select source assets/folders.
   - `Target = Extension`: select file extensions (for example: `.png`, `png`, `.asset`).
   - Filters support single/list value mode switching.
9. Optionally add assets to `Ignore`:
   - destination ignored entries are never copied/updated/deleted by sync.
10. If a destination file already exists as unsynced, resolve it in the conflict dialog with `Overwrite` or `Keep`.
11. Edit assets in the source folder.
   - For project source folders, AssetSync re-syncs automatically.
   - For external source folders, synchronization runs when AssetSync executes (for example, on activation/config updates).

## Sync behavior

- Copies only files that are new or content-changed.
- Never copies `.meta` files from source.
- Tracks synchronized files per config in `SyncConfig.syncRelativePaths` (saved in settings assets).
- Supports manual `Ignore` entries (GUID-based): destination ignored files/folders are never copied/updated/deleted.
- Supports filter target kinds `Type`, `Asset`, and `Extension`; asset target folders apply recursively to all descendants.
- Empty directory sync is controlled by `Keep Empty Directories` (`off` by default).
- When `Source` is an external directory, only `Extension` filters are supported (`Type` and `Asset` filters are rejected with a warning).
- If a destination file already exists and is neither sync nor ignored, AssetSync opens a conflict dialog to choose `Overwrite` or `Keep`.
- Disabling a config removes destination files tracked as sync, while preserving manual files and ignored destination entries.
- Synced destination assets are shown with an icon badge in the Project window (excluded for ignored entries or disabled configs).
- `Source` and `Destination` become read-only while the config is enabled after activation.
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
| `Editor/AssetSyncSettings.cs`, `Editor/SyncConfig.cs`, `Editor/FilterCondition.cs` | Serializable config models compiled for Editor only. |
| `Tests/Editor/*` | Editor tests for sync and filter logic. |
