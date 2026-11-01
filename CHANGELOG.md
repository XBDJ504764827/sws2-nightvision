# Changelog

All notable changes to this project are documented in this file.

## [Unreleased]

### Changed

- Night vision turns off when a player leaves the server and on a cold server start. Map changes keep the setting. The saved intensity is kept.
- Players with night vision off are held at exposure `0.5` once they spawn, instead of using the map's native exposure.

### Fixed

- Turning night vision off now holds the map exposure at `0.5` instead of mirroring the map's exposure range, which left the map too bright after toggling.

## [1.0.1] - 2026-10-25

### Changed

- Default night vision intensity is now `1.0` instead of `3.0`.

### Fixed

- Disabling night vision now keeps the post-processing volume attached in an idle state using the map's exposure range, avoiding a remove/recreate brightness spike. Idle volumes are cleaned up on disconnect, map change and plugin unload.

## [1.0.0] - 2026-10-25

### Added

- Initial release of `sws2-nightvision`.
- Per-player night vision using a `post_processing_volume` with exposure control.
- Owner-only entity transmission and automatic cleanup.
- `!nv`, `!nvi`, `!nvstatus` and `!nvdebug` commands with translations.
- Hot-reloadable configuration and persisted player preferences.
- Post-processing volume attachment to the owner's camera services.
- Per-player transmission isolation for newly connected players.
- Build, CI, PR pre-release and main-branch release workflows.
