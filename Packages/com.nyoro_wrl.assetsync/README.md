# AssetSync

AssetSync is an editor-only Unity package for synchronizing assets between folders in a project.

## Features

- Synchronize files from a source folder to a destination folder.
- Copy only changed files (content hash based).
- Track synced files per config in `SyncConfig.syncRelativePaths` (settings asset).
- Keep destination files that are not managed by AssetSync unless explicitly resolved.
- Protect destination assets manually with a GUID-based `Protected` list (`protectedGuids`).
- Resolve destination collisions with a conflict dialog (`Overwrite` or `Keep`).
- Show synced destination assets with an icon badge in the Project window.
- Filter synchronized assets by `Type` or `Asset` targets (single or multiple, include/exclude).

## Usage

1. Open `Window > AssetSync`.
2. Create or assign an `AssetSyncSettings` asset.
3. Add a config and set `Source` and `Destination`.
4. Set `Include Subdirectories` to control whether nested folders are synchronized (default: off).
   - When this is `off`, `Source` and `Destination` are allowed to be nested.
   - When this is `on`, nested `Source`/`Destination` pairs are rejected to prevent recursive sync.
5. Optionally add filters.
   - `Type` target: include/exclude by Unity object type.
   - `Asset` target: include/exclude by specific source assets or source folders.
6. Optionally add `Protected` entries.
   - Protected destination assets/folders are never copied/updated/deleted by sync.
7. If a destination file already exists as unsynced, resolve it in the conflict dialog (`Overwrite` or `Keep`).
8. Asset synchronization runs automatically when source assets change.
9. If selected source/destination folders are moved in the Project, config paths are automatically remapped.

## Notes

- Disabling a config removes destination files tracked as `Sync`.
- Protected destination files/folders and manual files are kept when a config is disabled.
- Synced badges are shown only for enabled configs and non-protected destination sync files.
