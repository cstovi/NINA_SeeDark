# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**SeeDark** is a C# plugin for [NINA (Nighttime Imaging 'N' Astronomy)](https://nighttime-imaging.eu/), a Windows astronomical imaging application. The plugin integrates into NINA's sequencer to conditionally trigger dark frame acquisition only when master darks are missing or stale for the current camera temperature and exposure time.

## Build Commands

```powershell
# Debug build
dotnet build NINA.Plugin.SeeDark/NINA.Plugin.SeeDark.csproj

# Release build
dotnet build NINA.Plugin.SeeDark/NINA.Plugin.SeeDark.csproj -c Release
```

- Target framework: `net8.0-windows`
- Output: `NINA.Plugin.SeeDark/bin/Debug/net8.0-windows/` or `bin/Release/…`
- No test projects exist in this repo.

## Architecture

### Plugin Composition (MEF)

NINA discovers plugins via .NET MEF (Managed Extensibility Framework). This plugin exports four components:

| Class | Interface | Purpose |
|---|---|---|
| `SeeDarkPlugin` | `IPluginManifest` | Plugin manifest, settings properties, settings UI binding |
| `SeeDarkContainer` | `ISequenceContainer` | Core sequencer item — runs the dark-needed decision |
| `StackMasterDarksInstruction` | `ISequenceItem` | Stacks raw FITS darks into master darks, regenerates CSV |
| `Resources` (xaml.cs) | `ResourceDictionary` | WPF UI resources (icon geometry, data templates, options panel) |

### Core Logic (`SeeDarkContainer.cs`)

The sequencer item's algorithm:
1. Gets camera sensor temperature via NINA's `ICameraMediator`
2. Rounds temperature to nearest 2°C bucket (e.g., 20.3°C → 20°C)
3. Resolves the scope ID from the camera driver name (second whitespace token of `ICameraMediator.GetInfo().Name`, matching the FITS `INSTRUME` header value written by NINA — e.g. `"Seestar S30_0ac17a9b Telephoto Camera"` → `"S30_0ac17a9b"`)
4. Parses the master dark library CSV (cached by file write-time to avoid re-reads)
5. Checks whether any CSV row matches: temperature bucket + exposure time (±0.5s tolerance) + gain + scope ID + within `MaxAgeDays`
6. Executes child sequence items only if no matching dark is found

### Dark Stacker (`StackMasterDarksInstruction.cs`)

A NINA sequencer item that replaces the Python `master_darks_with_checks_and_ninalive.py` script:
1. Scans `RawDarksFolder` for `.fit`/`.fits` files with `FILTER=DARK`
2. Reads FITS headers per frame: `DATE-LOC`, `EXPTIME`, `CCD-TEMP`, `INSTRUME`, `GAIN`, `OFFSET`
3. Session date rebasing: if `hour < 12`, subtract one day (astronomy sessions span midnight)
4. Groups frames by `(tempBucket, exposure, gain, scopeId)` — skips groups with fewer than `MinFrameCount` frames
5. Per group: per-pixel median stack → writes SIRIL master (BITPIX=-32, float32) and NINALIVE master (BITPIX=16, BZERO=32768, uint16-in-int16)
6. Deletes superseded masters for the same group key before writing new ones
7. Regenerates `DarkLibrary.csv` by scanning `MasterLibraryFolder`

FITS I/O is inline (no NuGet): `BinaryPrimitives` for big-endian reads/writes; FITS headers are 80-char fixed-width cards in 2880-byte blocks.

Master filename pattern: `master_dark_{exp:F0}s_{bucket}c_{scopeId}_{SIRIL|NINALIVE}_{yyyyMMddHHmmss}.fit`

### Settings (`SeeDarkSettings.cs` + `SeeDarkPlugin.cs`)

Settings persist as JSON at `%LOCALAPPDATA%\NINA\SeeDark\settings.json` via `SeeDarkSettings.Load()` / `Save()`. User-facing properties live on `SeeDarkPlugin` with `INotifyPropertyChanged` for WPF data binding.

| Setting | Type | Default | Description |
|---|---|---|---|
| `DarkLibraryCsvPath` | string | _(empty)_ | Path to master dark library CSV |
| `TargetExposure` | double | 20s | Exposure time to match against |
| `MaxAgeDays` | int | 180 | Max age of an acceptable dark |
| `Gain` | int | 200 | Camera gain to match (Seestar S30 default) |
| `RawDarksFolder` | string | _(empty)_ | Folder scanned by the stacker for raw dark frames |
| `MasterLibraryFolder` | string | _(empty)_ | Folder where stacker writes master FITS files |
| `MinFrameCount` | int | 20 | Minimum frames required to stack a group |

### Expected CSV Format

`DarkLibrary.csv` has a header row then data rows with columns (0-indexed):
- **0**: Temperature bucket (int, °C)
- **1**: Exposure time (double, seconds)
- **2**: Gain (int)
- **3**: Offset (int, may be empty)
- **4**: Scope ID (string, e.g. `S30_0ac17a9b`)
- **5**: Date created (DateTime string)

### Logging

Session log files are written to `%LOCALAPPDATA%\NINA\SeeDark\seedark_{yyyy-MM-dd_HH-mm-ss}.log` — one file per `SeeDarkContainer` construction (i.e., per NINA session load). The stacker writes its own session log at `%LOCALAPPDATA%\NINA\SeeDark\stack_{yyyy-MM-dd_HH-mm-ss}.log`. Entries use the format `[yyyy-MM-dd HH:mm:ss] {message}`. Errors are silently swallowed so logging never disrupts sequencer execution.

### WPF / NINA UI

- `Resources.xaml` defines the crescent moon icon (`GeometryGroup`), implicit data templates for `SeeDarkContainer` and `StackMasterDarksInstruction`, and the options panel (`SeeDark_Options`)
- The options panel has three sections: **Dark Library** (CSV path), **Dark Matching** (exposure, age, gain), and **Dark Stacker** (raw folder, master folder, min frames)
- `Resources.xaml.cs` exports the `ResourceDictionary` via MEF

### Key Dependencies

- `NINA.Plugin` v3.2.0.9001 — NINA plugin framework and sequencer interfaces
- `Newtonsoft.Json` v13.0.3 — Settings serialization
- `System.Buffers.Binary.BinaryPrimitives` — Big-endian FITS I/O (built into .NET 8, no NuGet)
