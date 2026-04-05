# Changelog
All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.2.1] - 2026-04-05

### Changed

- Reworked the `Filters` editor UI to use a list-style layout consistent with `Ignore Assets`.
- Updated filter type editing to support SmartAddresser-style single/list mode switching with a right-side mode toggle button.
- Updated list mode controls from count input to connected `+` / `-` buttons (minimum 1 item enforced).
- Updated filter mode actions to use connected `Include` / `Exclude` toolbar buttons.
- Changed unselected type display text from `Select Type...` to `(None)`.
- Added horizontal margins to `Filters` and `Ignore Assets` sections for clearer spacing.
- Removed per-element labels from `Ignore Assets` object fields.
- Updated the type mode toggle icon to use `d_UnityEditor.SceneHierarchyWindow` and centered icon rendering in the button.
- Preserved bidirectional value synchronization for type mode switching (`singleTypeName` and list index `0`).

## [1.2.0] - 2026-04-05

### Added

- Added a guided activation flow for configs with a bottom-right `Sync` button that becomes available only when `Source` and `Destination` are valid.
- Added `isSyncActivated` state to keep `Enable` hidden until the first successful sync activation.
- Added pre-delete confirmation dialogs (with delete counts and cancel support) for config deletion and settings deletion when synced destination files would be removed.
- Added automatic synced destination cleanup when deleting `AssetSyncSettings` assets (including folder deletion cases).

### Changed

- Changed config defaults to start disabled (`Enable = false`).
- Changed config list context menu text from `削除` to `Remove`.
- Changed editor selection behavior to auto-select the first config when available and remember the previously selected config per settings asset.
- Changed `Source`/`Destination` editing rules so they become read-only while the config is enabled after sync activation, and editable again when disabled.
- Changed invalid `Source`/`Destination` field presentation to use Unity-style red `GUI.backgroundColor` highlighting for the field only.
- Changed disabled-state `Enable` interaction so invalid configurations cannot be toggled from `false` to `true`.

## [1.1.1] - 2026-04-04

### Changed

- Consolidated package code layout to Editor-only by moving config model classes from `Runtime` to `Editor`.
- Removed the separate `Nyorowrl.AssetSync` runtime assembly and kept the package assembly structure Editor-only.

## [1.1.0] - 2026-04-04

### Added

- Added config-synced path tracking with `syncRelativePaths`.
- Added manual ignore asset list with `ignoreGuids` (GUID-based).
- Added conflict resolution dialog for existing destination unsynced file collisions.
- Added list-style `Ignore Assets` UI in the editor window.
- Added synced-asset badge overlay in the Project window using the packaged icon.

### Changed

- Changed terminology from `Ignore/Owned` to `Ignore/Sync`.
- Changed sync truth source to `SyncConfig` state only (Git-managed settings asset).
- Changed conflict dialog actions to user-facing `Overwrite` / `Keep` with improved path affordance.
- Changed disabled-config behavior to remove destination files tracked as `Sync` while preserving destination `Ignore` and manual files.
- Changed synced badge rules to hide badges for destination `Ignore` assets and disabled configs.

### Fixed

- Prevented automatic overwrite on destination existing unsynced collisions.
- Ensured ignore destination files are excluded from copy/update/delete effects.
- Ensured source-ignore files behave the same as source type-excluded assets.
- Fixed sync-state persistence for destination `Ignore` assets.
- Fixed unignore-while-disabled flow to drop `Sync` state without deleting the destination file.
- Fixed conflict dialog IMGUI layout errors when closing the dialog.

## [1.0.1] - 2026-04-04

### Changed

- Allowed nested Source/Destination paths when `Include Subdirectories` is off.

### Fixed

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

- Initial release of AssetSync with folder-to-folder synchronization and type-based filtering.
