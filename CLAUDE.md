# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

@../NINA_shared/NINA_plugin_guide.md

## Project Overview

**SeeDark** conditionally triggers dark frame acquisition only when master darks are missing or stale for the current camera temperature, gain, scope, and exposure time. It also includes a FITS stacker that replaces the Python `master_darks_with_checks_and_ninalive.py` script.

## Plugin Components (MEF)

| Class | Interface | Purpose |
|---|---|---|
| `SeeDarkPlugin` | `IPluginManifest` | Plugin manifest, settings properties, settings UI binding |
| `SeeDarkContainer` | `ISequenceContainer` | Runs dark-needed decision; Manual runs children, Auto runs internal dark capture |
| `StackMasterDarksInstruction` | `ISequenceItem` | Median-stacks raw FITS darks into masters |
| `Resources` (xaml.cs) | `ResourceDictionary` | Icon, data templates, options panel |

## Core Logic (`SeeDarkContainer.cs`)

1. Gets camera sensor temperature via `ICameraMediator`
2. Buckets temperature via shared non-overlapping bucketing (`TemperatureBucketing.ToBucket`)
3. Resolves scope ID from camera driver name (see shared guide)
4. Scans `MasterLibraryFolder` FITS masters and filters for DARK frames
5. Matches: exact bucket + exposure (±0.5s) + gain + scope ID + within `MaxAgeDays`
6. Uses pre-range trigger window:
   - `start = (bucket - halfStep) - PreBucketLeadC`
   - `end = bucket + halfStep` (exclusive)
7. `ExecutionMode=Manual`: executes children only if no matching master exists and sensor temp is inside start window
8. `ExecutionMode=Auto`: runs internal dark capture loop (DARK filter/type, gain/exposure from container, offset default, 20-50 frames with target 30 and temperature drift guard)

## Current Session State (May 2026)

- Plugin is intentionally in **simple thermal mode**:
  - bucket size user-selectable as `2°C` or `3°C`
  - bucket assignment and matching use non-overlapping bucket bands
  - stack/match tolerance is internally derived from bucket size and hidden in UI
  - pre-range lead is fixed internally to `2°C` and hidden in UI
  - stack frame counts are fixed internally to min `20` and max `50` (most recent frames), hidden in UI
  - lifecycle management is opt-in for archive/recovery behavior; archive deletion is a separate opt-in
- Container UI now includes:
  - `Execution Mode` selector (`Manual`, `Auto`)
  - hint text reminding that check exposure/gain must match child dark-capture settings
  - in Auto mode, container details/children/triggers/conditions are hidden/disabled and an explicit warning is shown
- Plugin options auto-save on edit (no manual Save button)
- `Discord webhook URL` is intentionally placed at the bottom of plugin options
- Design intent remains:
  - `SeeDarkContainer` = decision gate
  - Manual mode = user-controlled children
  - Auto mode = container-owned internal dark capture

## Next Planned Work (Temporary)

> Remove or update these items once implemented.

- [ ] Decide whether `ExecutionMode` default for new containers should become `Auto` or remain `Manual`.
- [ ] Decide whether to expose minimal Auto capture count controls, or keep internal fixed 20/30/50 behavior.
- [ ] Add optional filter-specific exposure/gain overrides (`IR`, `LP`) behind a default-off toggle; fall back to global defaults when unset.
- [ ] Consider re-introducing pre-range lead as an advanced-only option if real-world testing justifies it.
- [ ] Consider exposing stack min/max frame counts as advanced-only options if real-world testing justifies it.
- [ ] If auto mode ships, update this file and remove completed checklist entries.

## Dark Stacker (`StackMasterDarksInstruction.cs`)

1. Scans `RawDarksFolder` recursively for `*.fit*` with `FILTER=DARK`
2. Header fallbacks: `DATE-LOC`→`DATE-OBS`, `EXPTIME`→`EXPOSURE`, `CCD-TEMP`→`SET-TEMP`
3. Session date rebasing: if `hour < 12`, subtract one day (sessions span midnight)
4. Groups by `(tempBucket, exposure, gain, scopeId)` and uses the most recent valid frames (strict age window by FITS date)
5. Rebuilds a master only when missing or expired (`MaxAgeDays`), otherwise keeps fresh masters unchanged
6. Per-pixel median stack → SIRIL master (BITPIX=-32, float32) + NINALIVE master (BITPIX=16, BZERO=32768)
7. Deletes superseded masters for the same group key before writing
8. If lifecycle management is enabled, archives contributing active raw frames under `RawDarksFolder\_archived` and uses archive for recovery
9. If archive deletion is enabled, archived raws older than `MaxAgeDays` are removed
10. Master discovery remains header-driven by scanning `MasterLibraryFolder` FITS files (no CSV index dependency)

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
| `MinFrameCount` | int | 20 | Internal fixed minimum frames required to stack a group (hidden in UI) |
| `MaxFrameCount` | int | 50 | Internal fixed maximum frames stacked per group (most recent frames, hidden in UI) |
| `EnableLifecycleManagement` | bool | false | Opt-in: archive used raws and allow archive-based recovery rebuilds |
| `DeleteArchivedRawsAfterMaxAge` | bool | false | Opt-in: delete archived raws older than `MaxAgeDays` |
| `TempBucketSize` | int | 2 | User-selectable: 2 or 3 in simple mode |
| `StackTolerance` | int | 2 | Internal/derived from bucket size (not shown in UI) |
| `PreBucketLeadC` | int | 2 | Internal fixed pre-range lead (hidden in UI) |

## Master Discovery Source of Truth

Runtime dark-gap checks and matching are driven directly from FITS headers in `MasterLibraryFolder`.
No `DarkLibrary.csv` is required or consumed by the plugin.

## Logging

- SeeDarkContainer: `%LOCALAPPDATA%\NINA\SeeDark\seedark_{yyyy-MM-dd_HH-mm-ss}.log`
- StackMasterDarksInstruction: `%LOCALAPPDATA%\NINA\SeeDark\stack_{yyyy-MM-dd_HH-mm-ss}.log`
