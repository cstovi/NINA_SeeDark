# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

@../NINA_shared/NINA_plugin_guide.md

## Project Overview

**SeeDark** conditionally triggers dark frame acquisition only when master darks are missing or stale for the current camera temperature, gain, scope, and exposure time. It also includes a FITS stacker that replaces the Python `master_darks_with_checks_and_ninalive.py` script.

## Plugin Components (MEF)

| Class | Interface | Purpose |
|---|---|---|
| `SeeDarkPlugin` | `IPluginManifest` | Plugin manifest, settings properties, settings UI binding |
| `SeeDarkContainer` | `ISequenceContainer` | Runs the dark-needed decision; executes children only if gap found |
| `StackMasterDarksInstruction` | `ISequenceItem` | Median-stacks raw FITS darks into masters, regenerates CSV |
| `Resources` (xaml.cs) | `ResourceDictionary` | Icon, data templates, options panel |

## Core Logic (`SeeDarkContainer.cs`)

1. Gets camera sensor temperature via `ICameraMediator`
2. Rounds to nearest 2°C bucket: `(int)(Math.Round(temp / 2.0) * 2)`
3. Resolves scope ID from camera driver name (see shared guide)
4. Loads and caches `DarkLibrary.csv` (invalidated on file write-time change)
5. Matches: temp bucket + exposure (±0.5s) + gain + scope ID + within `MaxAgeDays`
6. Executes children only if no matching row found

## Dark Stacker (`StackMasterDarksInstruction.cs`)

1. Scans `RawDarksFolder` recursively for `*.fit*` with `FILTER=DARK`
2. Header fallbacks: `DATE-LOC`→`DATE-OBS`, `EXPTIME`→`EXPOSURE`, `CCD-TEMP`→`SET-TEMP`
3. Session date rebasing: if `hour < 12`, subtract one day (sessions span midnight)
4. Groups by `(tempBucket, exposure, gain, scopeId)`; skips groups below `MinFrameCount`
5. Per-pixel median stack → SIRIL master (BITPIX=-32, float32) + NINALIVE master (BITPIX=16, BZERO=32768)
6. Deletes superseded masters for the same group key before writing
7. Regenerates `DarkLibrary.csv` by scanning `MasterLibraryFolder`

FITS I/O is inline — no NuGet. Uses `System.Buffers.Binary.BinaryPrimitives` for big-endian reads/writes. Headers are 80-char fixed-width cards in 2880-byte blocks.

Master filename: `master_dark_{exp:F0}s_{bucket}c_{scopeId}_{SIRIL|NINALIVE}_{yyyyMMddHHmmss}.fit`

## Settings

Persisted at `%LOCALAPPDATA%\NINA\SeeDark\settings.json`.

| Setting | Type | Default | Description |
|---|---|---|---|
| `DarkLibraryCsvPath` | string | _(empty)_ | Path to master dark library CSV |
| `TargetExposure` | double | 20s | Exposure time to match against |
| `MaxAgeDays` | int | 180 | Max age of an acceptable dark |
| `Gain` | int | 200 | Camera gain to match (Seestar S30 default) |
| `RawDarksFolder` | string | _(empty)_ | Folder scanned by the stacker for raw dark frames |
| `MasterLibraryFolder` | string | _(empty)_ | Folder where stacker writes master FITS files |
| `MinFrameCount` | int | 20 | Minimum frames required to stack a group |

## DarkLibrary.csv Format

Header row, then columns (0-indexed):

| Col | Type | Value |
|---|---|---|
| 0 | int | Temperature bucket (°C) |
| 1 | double | Exposure time (seconds) |
| 2 | int | Gain |
| 3 | int | Offset (may be empty) |
| 4 | string | Scope ID (e.g. `S30_0ac17a9b`) |
| 5 | DateTime | Date created |

## Logging

- SeeDarkContainer: `%LOCALAPPDATA%\NINA\SeeDark\seedark_{yyyy-MM-dd_HH-mm-ss}.log`
- StackMasterDarksInstruction: `%LOCALAPPDATA%\NINA\SeeDark\stack_{yyyy-MM-dd_HH-mm-ss}.log`
