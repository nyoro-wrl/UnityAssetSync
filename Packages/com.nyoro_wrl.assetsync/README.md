# AssetSync

AssetSync is an editor-only Unity package for synchronizing assets between folders in a project.

## Features

- Synchronize files from a source folder to a destination folder.
- Copy only changed files (content hash based).
- Activate synchronization with an explicit first-run `Sync` action.
- Show `Enable` only after initial sync activation.
- Track synced files per config in `SyncConfig.syncRelativePaths` (settings asset).
- Keep destination files that are not managed by AssetSync unless explicitly resolved.
- Mark destination assets to ignore manually with a GUID-based `Ignore` list (`ignoreGuids`).
- Resolve destination collisions with a conflict dialog (`Overwrite` or `Keep`).
- Show synced destination assets with an icon badge in the Project window.
- Filter synchronized assets by `Type`, `Asset`, or `Extension` targets (single or multiple, include/exclude) with single/list mode switching.

## Usage

1. Open `Window > Asset Sync`.
2. Create or assign an `AssetSyncSettings` asset.
3. Add a config and set `Source` and `Destination`.
4. Click `Sync` (bottom-right) to run the first activation sync.
   - `Sync` is available only when `Source` and `Destination` are valid folders.
   - After activation, `Enable` becomes available.
5. Set `Include Subdirectories` to control whether nested folders are synchronized (default: off).
   - When this is `off`, `Source` and `Destination` are allowed to be nested.
   - When this is `on`, nested `Source`/`Destination` pairs are rejected to prevent recursive sync.
6. Optionally add filters.
   - `Type` target: include/exclude by Unity object type.
   - `Asset` target: include/exclude by specific source assets or source folders.
   - `Extension` target: include/exclude by file extension (for example: `.png`, `png`, `.asset`).
   - Toggle between single and list value modes for each filter target.
7. Optionally add `Ignore` entries.
   - Ignore destination assets/folders are never copied/updated/deleted by sync.
8. If a destination file already exists as unsynced, resolve it in the conflict dialog (`Overwrite` or `Keep`).
9. Asset synchronization runs automatically when source assets change.
10. If selected source/destination folders are moved in the Project, config paths are automatically remapped.

## Notes

- Disabling a config removes destination files tracked as `Sync`.
- Ignore destination files/folders and manual files are kept when a config is disabled.
- Synced badges are shown only for enabled configs and non-ignored destination sync files.
- `Source` and `Destination` are read-only while a config is enabled after sync activation.
