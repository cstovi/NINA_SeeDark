# NINA SeeDark

SeeDark helps Seestar users manage dark frames in N.I.N.A. by only capturing/stacking when masters are missing or stale.

At runtime, it checks for a matching master dark by:

- non-overlapping temperature bucket (`2C` or `3C`),
- exposure (`+-0.5s` tolerance),
- exact gain,
- exact scope ID,
- max age (`MaxAgeDays`).

If a valid master exists, it skips capture. If not, it captures (Auto) or can run child instructions (Manual path kept for legacy sequences).

## Main Components

- `SeeDark Dark Manager` (sequence container)
  - Decision gate for "is dark needed?"
  - `Auto`: captures darks internally
  - `Manual`: child-instruction path (legacy/advanced path; currently hidden in normal container UI)
- `SeeDark Stack Master Darks` (sequence instruction)
  - Scans raw dark FITS and builds master FITS

## Quick Start

1. Set plugin options:
   - `Master library folder`
   - `Raw darks folder`
2. Add `SeeDark Dark Manager` to your sequence.
3. Set container exposure and gain to your intended dark profile.
4. Run sequence.

Notes:

- New containers default to Auto behavior.
- Current simple UI exposes exposure/gain only on the container.

## Auto Capture Behavior

In Auto mode the container:

- uses `DARK` filter and image type,
- uses container exposure/gain (offset remains camera default),
- captures by segment: target 30 accepted, min 20 to stack, up to 30 attempts per segment,
- applies drift guard logic so capture stops when no longer needed for the current target bucket.

Temperature movement behavior:

- run starts with an anchor bucket,
- missing-master retarget to cooler buckets is allowed,
- missing-master retarget to warmer buckets is allowed only up to `AutoDarkMaxWarmerBucketSteps` above anchor,
- each allowed retarget resets segment attempt count.

Proactive next-bucket behavior (Auto only):

- if current bucket already has a valid master but the next warmer bucket does not, SeeDark starts immediately for that warmer bucket (no start-window wait),
- while still below target band, it takes warmup exposures that are not counted toward the accepted-frame target and do not trigger drift-stop logic.

## Stacker and Lifecycle

`SeeDark Stack Master Darks`:

- scans `RawDarksFolder` recursively for dark FITS,
- groups by `(temp bucket, exposure, gain, scope ID)`,
- uses most recent valid frames within age rules,
- rebuilds only missing/expired masters,
- writes SIRIL/PixInsight float32 masters (and optional NINA live masters).

Lifecycle controls are opt-in:

- `Enable lifecycle management`
  - archives contributing raws under `_archived`,
  - enables archive-based recovery rebuilds.
- `Delete archived raws after max age`
  - removes archived raws older than `MaxAgeDays`.

If lifecycle management is disabled, raw files remain non-destructive and `_archived` is not created.

## Current Settings Snapshot

Saved to `%LOCALAPPDATA%\\NINA\\SeeDark\\settings.json`.

Common user-facing options include:

- target exposure, gain, max age,
- raw darks folder, master library folder,
- temp bucket size (`2C` or `3C`),
- lifecycle/archive options,
- optional Discord webhook + verbose per-frame posting.

Advanced/internal controls (not shown in normal UI) include:

- pre-bucket lead (`PreBucketLeadC`),
- internal stack/capture guardrails (`MinFrameCount`, `MaxFrameCount`),
- `DefaultExecutionMode` (reserved for advanced/manual workflows).

## Notes for Existing Libraries

If you want to keep pre-plugin masters untouched, use a dedicated `Master library folder` for SeeDark-managed masters.

## Logs

- Container logs: `%LOCALAPPDATA%\\NINA\\SeeDark\\seedark_yyyy-MM-dd_HH-mm-ss.log`
- Stacker logs: `%LOCALAPPDATA%\\NINA\\SeeDark\\stack_yyyy-MM-dd_HH-mm-ss.log`

## Releases

- GitHub Releases: <https://github.com/cstovi/NINA_SeeDark/releases>
- Changelog: `CHANGELOG.md`
