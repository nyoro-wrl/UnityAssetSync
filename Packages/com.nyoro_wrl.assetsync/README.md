# AssetSync

AssetSync is an editor-only Unity package for synchronizing assets between folders in a project.

## Features

- Synchronize files from a source folder to a destination folder.
- Support source folders in the Unity project (`Assets/...`) or external absolute directories.
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
   - `Source` can be entered directly as a path or selected with `Browse`.
   - External source directories are supported.
4. Click `Sync` (bottom-right) to run the first activation sync.
   - `Sync` is available only when `Source` and `Destination` are valid folders.
   - After activation, `Enable` becomes available.
5. Set `Include Subdirectories` to control whether nested folders are synchronized (default: off).
   - When this is `off`, `Source` and `Destination` are allowed to be nested.
   - When this is `on`, nested `Source`/`Destination` pairs are rejected to prevent recursive sync.
6. Set `Keep Empty Directories` if you want to preserve empty source folders in destination (default: off).
7. Optionally add filters.
   - `Type` target: include/exclude by Unity object type.
   - `Asset` target: include/exclude by specific source assets or source folders.
   - `Extension` target: include/exclude by file extension (for example: `.png`, `png`, `.asset`).
   - If `Source` is external, only `Extension` filters are supported.
   - Toggle between single and list value modes for each filter target.
8. Optionally add `Ignore` entries.
   - Ignore destination assets/folders are never copied/updated/deleted by sync.
9. If a destination file already exists as unsynced, resolve it in the conflict dialog (`Overwrite` or `Keep`).
10. Asset synchronization runs automatically when source assets change in project source folders.
11. If selected source/destination folders are moved in the Project, config paths are automatically remapped.

## Notes

- Disabling a config removes destination files tracked as `Sync`.
- Ignore destination files/folders and manual files are kept when a config is disabled.
- Synced badges are shown only for enabled configs and non-ignored destination sync files.
- `Source` and `Destination` are read-only while a config is enabled after sync activation.
- Empty directory synchronization is controlled by `Keep Empty Directories` (default: off).
- External source directories are synchronized when AssetSync executes (for example, activation/config updates), not by AssetPostprocessor path watching.
