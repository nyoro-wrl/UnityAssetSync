# Changelog
All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
