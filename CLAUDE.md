# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

@../NINA_shared/NINA_plugin_guide.md

## Project Overview

**SeeDark** conditionally triggers dark frame acquisition when master darks are missing/stale or when same-night raw dark sufficiency has not yet been met for the current camera temperature, gain, scope, and exposure time. It also includes a FITS stacker that replaces the Python `master_darks_with_checks_and_ninalive.py` script.

## Change Hygiene (Mandatory)

Any meaningful project change must include updates to related user-facing text/docs in the same session.

- `README.md`
- `CLAUDE.md` (this file)
- `CHANGELOG.md`
- Relevant docs in `docs/` (for example `docs/SeeDark-Workflow-Guide.md`)

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
8. `ExecutionMode=Auto`: runs internal dark capture loop (DARK filter/type, gain/exposure from container, offset default, target 30 accepted frames per **segment** (fixed), **30 capture attempts per segment** (resets on retarget), min 20 to stack, drift guard). Starting bucket is fixed at run start (`anchorBucket`); retarget to a **warmer** bucket with no acceptable master is allowed only within `AutoDarkMaxWarmerBucketSteps` (0–3) discrete bands above that anchor; **cooler** missing-master retargets always allowed. Each allowed retarget resets segment attempt count. Auto-captured DARK raws are written with `SaveToDisk` using NINA **Image File Path** as `FilePath` and the profile DARK pattern from `GetFilePattern("DARK")` (not the narrowed raw scan root, so path tokens like `CALIBRATION\$$IMAGETYPE$$s` are not doubled). Before each save, `ImageMetaData.Image.ExposureNumber` is set to a per-run counter so `$$FRAMENR$$` increments like interactive captures; on save failure Auto aborts immediately (hard-fail).
9. **Auto raw sufficiency guard:** same-night raw counts are evaluated per key `(bucket, exposure, gain, scopeId)` using session-date rebasing (`hour < 12` => previous day). If a bucket already has enough same-night raws (`MaxFrameCount`, currently 50), additional capture for that bucket is skipped. To preserve uncooled thermal progression into a needed warmer bucket, bounded overshoot in the current bucket is allowed up to 60 raws.
10. **Auto only — proactive next bucket:** If the **current** bucket is already satisfied (acceptable master, or enough same-night raws) but the **next warmer** bucket still needs collection, start Auto capture immediately (no start-window wait), target the next warmer bucket, and take **warmup** exposures while still colder (not counted toward target / no drift stop) until the sensor reaches the target band.

## Current Session State (May 2026)

- Plugin is intentionally in **simple thermal mode**:
  - bucket size user-selectable as `2°C` or `3°C`
  - bucket assignment and matching use non-overlapping bucket bands
  - stack/match tolerance is internally derived from bucket size and hidden in UI
  - pre-range lead is fixed internally to `2°C` and hidden in UI
  - stack frame counts are fixed internally to min `20` and max `50` (most recent frames), hidden in UI
  - optional in-place raw cleanup (`DeleteRawsAfterMaxAge`) is separate and off by default
- Container UI: exposure and gain only (no execution-mode picker, no hint text); `Auto` is the default for new containers. Legacy sequences deserialized with `Manual` still work and show child UI when applicable.
- Plugin options auto-save on edit (no manual Save button)
- Notifications at the bottom: `Discord webhook URL`, optional **Verbose** per-frame auto dark lines (chatty; dedicated webhook/channel recommended)
- Design intent remains:
  - `SeeDarkContainer` = decision gate
  - Manual mode = user-controlled children (hidden from UI for now; see Next Planned Work)
  - Auto mode = container-owned internal dark capture

## Next Planned Work (Temporary)

> Remove or update these items once implemented.

- [ ] Re-expose **Manual** execution mode (and plugin default for new containers) as an **advanced** option; keep `DarkExecutionMode`, `ShowManualChildren`, and manual execution path until then.
- [ ] Decide whether to expose minimal Auto capture count controls, or keep internal fixed 20/30/50 behavior.
- [ ] Add optional filter-specific exposure/gain overrides (`IR`, `LP`) behind a default-off toggle; fall back to global defaults when unset.
- [ ] Consider re-introducing pre-range lead as an advanced-only option if real-world testing justifies it.
- [ ] Consider exposing stack min/max frame counts as advanced-only options if real-world testing justifies it.

## Dark Stacker (`StackMasterDarksInstruction.cs`)

1. Scans the resolved raw-dark root recursively for `*.fit*` with `FILTER=DARK`: when the NINA DARK pattern includes `$$IMAGETYPE$$` in a folder segment, under `ImageFileSettings.FilePath` through that expanded segment (e.g. `...\CALIBRATION\DARKs`); otherwise the full NINA image save root (document for users: prefer NINA patterns that put DARKs under a dedicated subtree so large mixed libraries are not fully enumerated)
2. Header fallbacks: `DATE-LOC`→`DATE-OBS`, `EXPTIME`→`EXPOSURE`, `CCD-TEMP`→`SET-TEMP`
3. Session date rebasing: if `hour < 12`, subtract one day (sessions span midnight)
4. Groups by `(tempBucket, exposure, gain, scopeId)` and uses the most recent valid frames (strict age window by FITS date)
5. Rebuilds a master when missing or expired (`MaxAgeDays`); additionally, for fresh masters with readable `STACKCNT`, rebuilds when current eligible raw count exceeds that `STACKCNT` (legacy masters without `STACKCNT` remain skip-safe)
6. Per-pixel median stack → SIRIL master (BITPIX=-32, float32) + NINALIVE master (BITPIX=16, BZERO=32768)
7. Deletes superseded masters for the same group key before writing
8. If raw cleanup is enabled, DARK raws older than `MaxAgeDays` are deleted in place from the raw-dark scan root
10. Master discovery remains header-driven by scanning `MasterLibraryFolder` FITS files (no CSV index dependency)
11. New masters include `STACKCNT` in FITS headers so future runs can detect whether more valid raws are now available for a quality-improving rebuild

FITS I/O is inline — no NuGet. Uses `System.Buffers.Binary.BinaryPrimitives` for big-endian reads/writes. Headers are 80-char fixed-width cards in 2880-byte blocks.

Master filename: `master_dark_{exp:F0}s_{bucket}c_{scopeId}_g{gain}_{SIRIL|NINALIVE}_{yyyyMMddHHmmss}.fit`

## Settings

Persisted at `%LOCALAPPDATA%\NINA\SeeDark\settings.json`.

| Setting | Type | Default | Description |
|---|---|---|---|
| `TargetExposure` | double | 20s | Exposure time to match against |
| `MaxAgeDays` | int | 180 | Max age of an acceptable dark |
| `Gain` | int | 200 | Camera gain to match (Seestar S30 default) |
| `MasterLibraryFolder` | string | _(empty)_ | Folder where stacker writes master FITS files |
| `MinFrameCount` | int | 20 | Internal fixed minimum frames required to stack a group (hidden in UI) |
| `MaxFrameCount` | int | 50 | Internal fixed maximum frames stacked per group (most recent frames, hidden in UI) |
| `DefaultExecutionMode` | int | 1 | Default for new containers (`1=Auto`, `0=Manual`); no UI — reserved for future advanced mode |
| `DeleteRawsAfterMaxAge` | bool | false | Opt-in: delete DARK raws older than `MaxAgeDays` from the raw-dark scan root |
| `WriteNinaLiveMasters` | bool | false | Opt-in: also write a NINA-format master (BITPIX=16, BZERO=32768) alongside the always-present SIRIL/PixInsight float32 master |
| `AutoDarkMaxWarmerBucketSteps` | int | 0 | Auto capture may follow rising temp into up to N warmer bands (vs. run-start anchor) when no master exists there; 0 = stay in starting band only |
| `TempBucketSize` | int | 2 | User-selectable: 2 or 3 in simple mode |
| `StackTolerance` | int | 2 | Internal/derived from bucket size (not shown in UI) |
| `PreBucketLeadC` | int | 2 | Internal fixed pre-range lead (hidden in UI) |
| `DiscordWebhookUrl` | string | _(empty)_ | Optional; mirrors log lines to Discord |
| `DiscordVerbosePerFrame` | bool | false | Also mirror per-frame auto dark lines (noisy; own channel recommended) |

## Master Discovery Source of Truth

Runtime dark-gap checks and matching are driven directly from FITS headers in `MasterLibraryFolder`.
No `DarkLibrary.csv` is required or consumed by the plugin.

## Logging

- SeeDarkContainer: `%LOCALAPPDATA%\NINA\SeeDark\seedark_{yyyy-MM-dd_HH-mm-ss}.log`
- StackMasterDarksInstruction: `%LOCALAPPDATA%\NINA\SeeDark\stack_{yyyy-MM-dd_HH-mm-ss}.log` (written and superseded master lines include full file paths under the configured master library folder)
