# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.2.0] - 2026-11-01

### Added
- Real-time Steam librarycache watcher (`SteamLocalWatcherService`) detecting in-game unlocks within <1s with 0% ban risk.
- Borderless Fullscreen helper (`WindowHelper`) to transform windowed games into true borderless fullscreen windows.
- Dedicated UI button `[🖥️ Без рамок (Borderless)]` in the Process Tracker panel.
- Automatic borderless mode enforcement when starting game tracking to guarantee overlay visibility with active DWM.
- On-the-fly and batch Russian translation for achievement titles and descriptions via `GoogleTranslateService`.
- Xbox 360 chime and Xbox One rare diamond fanfare audio effects for bronze/silver/gold/platinum tiers.
- Adaptive multi-line overlay window sizing with dynamic height and text wrapping.

### Fixed
- Fixed overlay window top-most Z-ordering and positioning on high-DPI displays by using `SWP_NOMOVE`.
- Fixed concurrency file sharing violation (`IOException`) in `LocalCacheManager.SaveGameAsync` via atomic temporary file writes and retries.
- Fixed overlay display in exclusive fullscreen games (e.g. *Dead Island DE*) via borderless mode with active DWM compositor.

## [0.1.0] - 2026-11-01

### Added
- Architectural standard and engineering baseline (storage isolation, SemVer, CI/CD, fast rollback).
- Multi-project .NET 8 WPF architecture (`Core`, `Infrastructure`, `Injector`, `Overlay`, `UI`).
- Steam achievement tracker with direct memory scanning and Web API sync fallback.
- Audio notifications via NAudio WASAPI with custom sound packs.
- In-game transparent overlay window with smooth slide-in animations.
- Deterministic builds via `Directory.Build.props` and `packages.lock.json`.
- Continuous Integration workflow for automated build and tests on Windows runner.
- Binary Release workflow for publishing self-contained `win-x64` single-file executables with `SHA256SUMS.txt`.
- Emergency rollback script `scripts/rollback.ps1` and local build script `scripts/build-release.ps1`.
