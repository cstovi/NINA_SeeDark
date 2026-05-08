# NINA SeeDark

SeeDark helps Seestar users manage dark frames in N.I.N.A. by only capturing/stacking when masters are missing or stale.

At runtime, it checks for a matching master dark by:

- non-overlapping temperature bucket (`2C` or `3C`),
- exposure (`+-0.5s` tolerance),
- exact gain,
- exact scope ID,
- max age (`MaxAgeDays`).

If a valid master exists, it skips capture. If not, it captures internally using the current Auto workflow.

## Main Components

- `SeeDark Dark Manager` (sequence container)
  - Decision gate for "is dark needed?"
  - Auto capture is the primary runtime behavior for current releases
  - Legacy Manual child-instruction path still exists internally for backward compatibility, but is hidden from the normal UI
- `SeeDark Stack Master Darks` (sequence instruction)
  - Scans raw dark FITS and builds master FITS

## Install

Since SeeDark is not currently in the NINA plugin repository, install it manually:

1. Create this folder if it does not exist:
   - `%LOCALAPPDATA%\NINA\Plugins\3.0.0\SeeDark\`
2. Drop `NINA.Plugin.SeeDark.dll` into that folder.
3. Restart NINA.

## Quick Start

1. Set plugin option:
   - `Master library folder` (required for runtime matching/capture decisions)
2. Add `SeeDark Dark Manager` to your sequence.
3. Set container exposure and gain to your intended dark profile.
4. Run sequence.

For a plain-English explanation of runtime behavior and settings implications, see:

- `docs/SeeDark-Workflow-Guide.md`

Notes:

- Current container workflow is Auto dark capture.
- Current simple UI exposes exposure/gain only on the container.
- Manual mode is not a normal user-facing mode at this time and should be treated as legacy/internal behavior.

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

- if current bucket is already satisfied (fresh master, or enough same-night raws cached) but the next warmer bucket still needs collection, SeeDark starts immediately for that warmer bucket (no start-window wait),
- while still below target band, it takes warmup exposures that are not counted toward the accepted-frame target and do not trigger drift-stop logic.

Same-night raw sufficiency guard:

- Auto checks same-night raw dark count per key `(temp bucket, exposure, gain, scope ID)` and avoids extra capture once enough raws are already cached for that bucket.
- To preserve A->B thermal progression on uncooled sensors, Auto may temporarily overshoot bucket A up to 60 raws while warming into a needed bucket B.
- The intent is to avoid all-night over-capture in stable temperatures, while still allowing useful warmer-bucket seeding.

## Stacker and Lifecycle

`SeeDark Stack Master Darks`:

- scans NINA's darks location (`<NINA FilePath>\CALIBRATION\DARKs`) recursively for dark FITS,
- groups by `(temp bucket, exposure, gain, scope ID)`,
- uses most recent valid frames within age rules,
- rebuilds only missing/expired masters,
- writes SIRIL/PixInsight float32 masters (and optional NINA live masters).

Operational guidance:

- In normal use, run stacking once near end of session rather than after every dark container.

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
- master library folder (runtime),
- temp bucket size (`2C` or `3C`),
- lifecycle/archive options,
- optional Discord webhook + verbose per-frame posting.

Advanced/internal controls (not shown in normal UI) include:

- pre-bucket lead (`PreBucketLeadC`),
- internal stack/capture guardrails (`MinFrameCount`, `MaxFrameCount`),
- `DefaultExecutionMode` (reserved for advanced/manual workflows).

## Notes for Existing Libraries

If you want to keep pre-plugin masters untouched, use a dedicated `Master library folder` for SeeDark-managed masters.

The stacker reads raws from NINA's darks location (`<NINA FilePath>\CALIBRATION\DARKs`), not a SeeDark-specific folder setting.

## Logs

- Container logs: `%LOCALAPPDATA%\\NINA\\SeeDark\\seedark_yyyy-MM-dd_HH-mm-ss.log`
- Stacker logs: `%LOCALAPPDATA%\\NINA\\SeeDark\\stack_yyyy-MM-dd_HH-mm-ss.log`

## Releases

- GitHub Releases: <https://github.com/cstovi/NINA_SeeDark/releases>
- Changelog: `CHANGELOG.md`
