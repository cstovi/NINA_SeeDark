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
4. Scans `MasterLibraryFolder` FITS masters and filters for DARK frames
5. Matches: bucket-centered temp tolerance + exposure (±0.5s) + gain + scope ID + within `MaxAgeDays`
6. Uses pre-range trigger window:
   - `start = (bucket - StackTolerance) - PreBucketLeadC`
   - `end = bucket + StackTolerance`
7. Executes children only if no matching master exists **and** sensor temp is inside start window
8. `ExecutionMode` enum now exists in container (`Manual` default, `Auto` scaffold only; no auto-capture behavior yet)

## Current Session State (May 2026)

- Plugin is intentionally in **simple thermal mode**:
  - bucket size fixed to `2°C`
  - stack/match tolerance limited to `1` or `2`
  - pre-range lead limited to `1`, `2`, or `3` °C
- Container UI now includes:
  - `Execution Mode` selector (`Manual`, `Auto (soon)`)
  - hint text reminding that check exposure/gain must match child dark-capture settings
- Design intent remains:
  - `SeeDarkContainer` = decision gate
  - user sequence children currently perform actual dark capture
  - auto-capture may be added later behind execution mode

## Next Planned Work (Temporary)

> Remove or update these items once implemented.

- [ ] Wire `ExecutionMode=Auto` to first strategy: "take N darks all at once".
- [ ] Add minimal auto-capture options in container UI (frame count `N` only for v1).
- [ ] Keep `ExecutionMode=Manual` as default and preserve current sequence-driven behavior.
- [ ] If auto mode ships, update this file and remove completed checklist entries.

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
| `TargetExposure` | double | 20s | Exposure time to match against |
| `MaxAgeDays` | int | 180 | Max age of an acceptable dark |
| `Gain` | int | 200 | Camera gain to match (Seestar S30 default) |
| `RawDarksFolder` | string | _(empty)_ | Folder scanned by the stacker for raw dark frames |
| `MasterLibraryFolder` | string | _(empty)_ | Folder where stacker writes master FITS files |
| `MinFrameCount` | int | 20 | Minimum frames required to stack a group |
| `TempBucketSize` | int | 2 | Fixed to 2 in simple mode |
| `StackTolerance` | int | 2 | Clamped to 1..2 in simple mode |
| `PreBucketLeadC` | int | 1 | Pre-range lead below lower match bound; clamped to 1..3 |

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
