# AssetFork

AssetFork is an editor-only Unity package for synchronizing assets between folders in a project.

## Features

- Synchronize files from a source folder to a destination folder.
- Copy only changed files (content hash based).
- Keep destination changes that were not managed by AssetFork.
- Filter synchronized assets by Unity object type (single or multiple types, include/exclude).

## Usage

1. Open `Window > AssetFork`.
2. Create or assign an `AssetForkSettings` asset.
3. Add a config and set `Source` and `Destination`.
4. Set `Include Subdirectories` to control whether nested folders are synchronized (default: off).
5. Optionally add filters by type.
6. Asset synchronization runs automatically when source assets change.
