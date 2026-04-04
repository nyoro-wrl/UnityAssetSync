# Changelog
All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.1.0] - 2026-04-04

### Added

- Added config-synced path tracking with `syncRelativePaths`.
- Added manual ignore asset list with `ignoreGuids` (GUID-based).
- Added conflict resolution dialog for existing destination unsynced file collisions.
- Added Ignore list UI in the editor window with `Source`/`Destination`/`Invalid` states.

### Changed

- Changed sync state model to derive `Unsynced` as `!Sync && !Ignore`.
- Changed sync truth source to `SyncConfig` state only (Git-managed settings asset).
- Allowed nested Source/Destination paths when `Include Subdirectories` is off.

### Fixed

- Prevented automatic overwrite on destination existing unsynced collisions.
- Ensured ignore destination files are excluded from copy/update/delete effects.
- Ensured source-ignore files behave the same as source type-excluded assets.
- Kept nested path safety checks active when switching `Include Subdirectories` on.
- Remapped config source/destination paths automatically when selected folders are moved.

## [1.0.0] - 2026-04-04

### Added

- Added `Include Subdirectories` sync option (default: off).

### Fixed

- Prevented recursive sync by rejecting nested Source/Destination paths.
- Tightened asset change detection in postprocessing to avoid false positives from prefix-only path matches.

### Changed

- Updated package metadata and documentation from templates to real content.
- Marked package assemblies as Editor-only.
- Removed placeholder sample and runtime test files.

## [0.1.0] - 2026-04-03

### Added

- Initial release of AssetFork with folder-to-folder synchronization and type-based filtering.
