# AssetFork

AssetFork is an editor-only Unity package for synchronizing assets between folders in a project.

## Features

- Synchronize files from a source folder to a destination folder.
- Copy only changed files (content hash based).
- Track synced files per config in `SyncConfig.syncRelativePaths` (settings asset).
- Keep destination files that are not managed by AssetFork unless explicitly resolved.
- Protect assets manually with a GUID-based `Ignore` list (`ignoreGuids`).
- Resolve destination collisions with a conflict dialog (`Overwrite` or `Keep`).
- Show synced destination assets with an icon badge in the Project window.
- Filter synchronized assets by Unity object type (single or multiple types, include/exclude).

## Usage

1. Open `Window > AssetFork`.
2. Create or assign an `AssetForkSettings` asset.
3. Add a config and set `Source` and `Destination`.
4. Set `Include Subdirectories` to control whether nested folders are synchronized (default: off).
   - When this is `off`, `Source` and `Destination` are allowed to be nested.
   - When this is `on`, nested `Source`/`Destination` pairs are rejected to prevent recursive sync.
5. Optionally add filters by type.
6. Optionally add `Ignore Assets` entries.
   - Source ignore assets are treated as sync-excluded.
   - Destination ignore assets are never copied/updated/deleted by sync.
7. If a destination file already exists as unsynced, resolve it in the conflict dialog (`Overwrite` or `Keep`).
8. Asset synchronization runs automatically when source assets change.
9. If selected source/destination folders are moved in the Project, config paths are automatically remapped.

## Notes

- Disabling a config removes destination files tracked as `Sync`.
- Destination ignore files and manual files are kept when a config is disabled.
- Synced badges are shown only for enabled configs and non-ignore destination sync files.
