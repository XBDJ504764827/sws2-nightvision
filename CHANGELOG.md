# Changelog

All notable changes to this project are documented in this file.

## [Unreleased]

## [1.0.0] - 2026-10-25

### Added

- Initial release of `sws2-nightvision`.
- Per-player night vision using a `post_processing_volume` with exposure control.
- Owner-only entity transmission and automatic cleanup.
- `!nv`, `!nvi`, `!nvstatus` and `!nvdebug` commands with translations.
- Hot-reloadable configuration and persisted player preferences.
- Post-processing volume attachment to the owner's camera services.
- Per-player transmission isolation for newly connected players.
- Default intensity is now `1.0` instead of `3.0`.
- Disabling night vision restores the map/tonemap exposure for one transition tick before removing the volume, avoiding a brightness overshoot.
- Build, CI, PR pre-release and main-branch release workflows.
