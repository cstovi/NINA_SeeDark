# NINA SeeDark

SeeDark helps Seestar users manage dark frames in N.I.N.A. by only capturing/stacking when masters are missing, stale, or can be materially improved with more valid raws.

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

1. Set plugin options:
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

## Raw DARKs and stacking (important)

The stacker (and same-night raw checks) discover raw DARK FITS under NINA **Image File Path**, **narrowed** to a `DARKs`-style subtree when your DARK **pattern** includes `$$IMAGETYPE$$` in a folder segment (see NINA file pattern settings).

**If that resolved root is the same place you store lights, flats, and other FITS** (for example, pattern only varies the file name, not folders), SeeDark still has to enumerate **every** `*.fit*` there and open headers until it finds `FILTER=DARK`. On a large library that is slow and can look like a hang.

**What to do:** configure NINA so DARK saves live under a dedicated branch of **Image File Path**—typically a pattern with `$$IMAGETYPE$$` in the path (e.g. `CALIBRATION\DARKs\...`). Then stacking stays scoped to a smaller tree without any SeeDark-only path override.

## Stacker and Lifecycle

`SeeDark Stack Master Darks`:

- scans the resolved raw-dark root recursively for FITS, keeps frames whose headers identify them as DARKs,
- groups by `(temp bucket, exposure, gain, scope ID)`,
- uses most recent valid frames within age rules,
- rebuilds masters when:
  - missing,
  - expired, or
  - fresh but `STACKCNT` is present and more eligible raws now exist than were previously stacked,
- writes float32 (F32) FITS masters (BITPIX=-32) for general post-processing, and optionally NINA-format (16-bit + BZERO) masters for NINA plugins such as Livestack.

Contributor metadata notes:

- newly written masters include `STACKCNT` in FITS headers (`number of raws stacked`),
- count-based rebuilds are only attempted when the existing master has a valid `STACKCNT` value,
- legacy masters without `STACKCNT` remain on missing/expired-only rebuild behavior.

Operational guidance:

- In normal use, run stacking once near end of session rather than after every dark container.

Lifecycle controls are opt-in:

- `Delete old raw darks`
  - deletes DARK raws older than `MaxAgeDays` under the same resolved raw-dark root as the stacker.
  - disabled by default; irreversible when enabled.

## Current Settings Snapshot

Saved to `%LOCALAPPDATA%\\NINA\\SeeDark\\settings.json`.

Common user-facing options include:

- target exposure, gain, max age,
- master library folder (runtime),
- temp bucket size (`2C` or `3C`),
- optional old-raw cleanup option,
- optional Discord webhook + verbose per-frame posting.

Advanced/internal controls (not shown in normal UI) include:

- pre-bucket lead (`PreBucketLeadC`),
- internal stack/capture guardrails (`MinFrameCount`, `MaxFrameCount`),
- `DefaultExecutionMode` (reserved for advanced/manual workflows).

## Notes for Existing Libraries

If you want to keep pre-plugin masters untouched, use a dedicated `Master library folder` for SeeDark-managed masters.

For **raw DARKs**, use a NINA layout that puts DARKs under their own subtree when possible so **Stack Master Darks** does not scan your entire library (see **Raw DARKs and stacking** above).

Auto capture writes DARK raws via NINA's DARK pattern under the same resolved root. If save fails, Auto capture aborts immediately.

## Logs

- Container logs: `%LOCALAPPDATA%\\NINA\\SeeDark\\seedark_yyyy-MM-dd_HH-mm-ss.log`
- Stacker logs: `%LOCALAPPDATA%\\NINA\\SeeDark\\stack_yyyy-MM-dd_HH-mm-ss.log`

## Releases

- GitHub Releases: <https://github.com/cstovi/NINA_SeeDark/releases>
- Changelog: `CHANGELOG.md`
