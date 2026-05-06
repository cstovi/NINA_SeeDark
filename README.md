# NINA SeeDark

SeeDark helps Seestar users manage dark frames in N.I.N.A. with less manual setup.

It checks whether a matching master dark already exists for the current camera conditions (temperature bucket, exposure, gain, scope ID, age). If not, it can either:

- run your own child instructions (`Manual` mode), or
- capture darks internally (`Auto` mode).

## Main Components

- `SeeDark Dark Manager` (sequence container)
  - Decides whether darks are needed.
  - `Auto`: captures darks internally.
  - `Manual`: runs child instructions when darks are needed.
- `Stack SeeDark Master Darks` (sequence instruction)
  - Scans raw darks and builds master dark FITS files.

## Quick Start

1. Set `Master library folder` and `Raw darks folder` in plugin options.
2. Add `SeeDark Dark Manager` to your sequence.
3. Choose mode:
   - `Auto` for internal dark capture.
   - `Manual` if you want child instructions to do capture.
4. Set exposure and gain on the container to match your intended dark capture profile.

## Auto vs Manual

- `Auto` mode:
  - Uses DARK filter and DARK image type.
  - Uses container exposure and gain.
  - Leaves offset at camera default.
  - Targets dark capture block around 30 frames (internal min/max guardrails apply).
  - Hides/disables child instructions, triggers, and conditions inside the container.
- `Manual` mode:
  - Runs child instructions only when darks are needed and the sensor temperature is in range.

## Matching Model

SeeDark matches masters using:

- non-overlapping temperature buckets (`2C` or `3C`),
- exposure tolerance (`+-0.5s`),
- exact gain,
- exact scope ID,
- max master age (`MaxAgeDays`).

## Stacker and Lifecycle

Stacker groups by bucket/exposure/gain/scope and stacks most recent valid frames.

Lifecycle controls are opt-in:

- `Enable lifecycle management`
  - archives used raw darks to `_archived`,
  - allows archive-based recovery rebuilds.
- `Delete archived raws after max age`
  - removes archived raws older than `MaxAgeDays`.

If lifecycle is disabled, SeeDark stays non-destructive for raw files.

## Notes for Existing Libraries

If you want to keep pre-plugin masters untouched, use a dedicated `Master library folder` for SeeDark.

## Logs

- Container logs: `%LOCALAPPDATA%\\NINA\\SeeDark\\seedark_yyyy-MM-dd_HH-mm-ss.log`
- Stacker logs: `%LOCALAPPDATA%\\NINA\\SeeDark\\stack_yyyy-MM-dd_HH-mm-ss.log`

## Releases

- GitHub Releases: <https://github.com/cstovi/NINA_SeeDark/releases>
- Project changelog: `CHANGELOG.md`
