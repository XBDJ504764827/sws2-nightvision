# Changelog

All notable changes to this project are documented in this file.

## [Unreleased]

### Fixed

- Attach the generated post-processing volume to the owner's `PostProcessingVolumes` list so the client actually applies it.
- Avoid blocking the owner during initial entity transmission.
- Block new players from receiving other players' night vision volumes.
- Add `!nvdebug` diagnostics and a post-processing-disabled hint.
- Give every PR CI run a traceable version such as `1.0.1-pr.12.37`.

## [1.0.0] - 2026-10-25

### Added

- Initial release of `sws2-nightvision`.
- Per-player night vision using a `post_processing_volume` with exposure control.
- Owner-only entity transmission and automatic cleanup.
- `!nv`, `!nvi` and `!nvstatus` commands with translations.
- Hot-reloadable configuration and persisted player preferences.
- Build, CI, PR pre-release and main-branch release workflows.
