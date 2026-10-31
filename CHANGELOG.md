# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
