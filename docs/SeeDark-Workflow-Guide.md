# SeeDark Workflow Guide

This guide explains how SeeDark behaves during a session in plain English: what it does, why it does it, and what each setting means in practice.

## What SeeDark is trying to do

SeeDark has one core goal: keep your dark library healthy without wasting the night on unnecessary dark capture.

In practical terms, it tries to:

- skip work when a usable master dark already exists,
- gather enough raw darks when a usable master does not exist,
- avoid over-collecting the same bucket all night,
- still follow natural sensor warming so nearby warmer buckets can be seeded.

## Session workflow (high-level)

Think of the run in three phases:

1. **Decide**
   - "Do I already have what I need for this temperature bucket?"
2. **Capture if needed**
   - If not, collect dark raws for the current bucket.
3. **Build masters**
   - Usually once near the end of session, not after every container run.

## How the decision is made

For the current bucket, SeeDark considers it "satisfied" when either:

- a fresh matching master already exists, or
- enough same-night raws are already cached for that bucket key (temperature bucket, exposure, gain, scope).

If the current bucket is not satisfied, SeeDark captures.
If it is satisfied, SeeDark skips that bucket unless it should proactively help the next warmer bucket.

## Same-night raw sufficiency (why it exists)

Without this guard, a roof-closed or repeated loop can keep collecting far more raws than needed in one bucket.

SeeDark now checks "do we already have enough raws tonight for this bucket?" and can skip additional capture when the answer is yes.

Important details:

- this check is **same-night only** (not previous nights),
- it is an additional practical gate above "has master",
- it helps prevent runaway over-capture before stacking is run.

## Warmer-bucket progression (A -> B behavior)

SeeDark is designed to keep useful temperature progression:

- if bucket A is already satisfied but warmer bucket B still needs collection, SeeDark can start early for B,
- while still colder than B, it can take warmup exposures until temperature reaches B range,
- if temperature drifts into a missing warmer bucket, retargeting remains allowed (within your warmer-step setting).

### Bounded overshoot rule

To avoid blocking thermal progression on uncooled sensors, SeeDark can briefly continue bucket A beyond the normal per-bucket sufficiency point when trying to reach a needed B.

Current behavior:

- normal "enough raws" threshold aligns with the stack max frame count (default/internal 50),
- bounded warmup overshoot allows A up to 60 raws while B is still needed.

This keeps a balance:

- no unlimited all-night A capture,
- but no premature stop that prevents reaching B.

## Recommended operational pattern

For most users:

- let SeeDark collect raws during the session as needed,
- run `SeeDark Stack Master Darks` once near end of session.

Why this is preferred:

- it reduces repeated rebuild churn,
- it still gives you fresh masters from the most recent valid raws,
- it works well with same-night sufficiency checks.

## Master rebuild policy

`SeeDark Stack Master Darks` rebuilds when:

- no matching master exists, or
- matching master exists but is older than `MaxAgeDays`, or
- matching master is fresh **and** has valid `STACKCNT`, and current eligible raws for that key now exceed `STACKCNT`.

Legacy-safe rule:

- if existing master has no usable `STACKCNT`, SeeDark does **not** guess contributor count and keeps the fresh master unchanged (missing/expired behavior still applies normally).

Practical example:

- if last master used 20 raws (`STACKCNT=20`) and you now have 40 valid raws, SeeDark rebuilds,
- if `STACKCNT` is missing (older master), SeeDark skips this count-based trigger.

## Settings guide and implications

Below are the main settings and what changing them usually means.

### Target Exposure

- What it does: sets the exposure profile SeeDark matches/captures.
- Raise/lower when: you intentionally use a different dark exposure library.
- Implication: mismatched exposure means existing masters will not be considered valid for this run.

### Gain

- What it does: sets the gain profile for matching/capture.
- Change only when your imaging workflow gain changes.
- Implication: gain mismatch creates a separate dark library track.

### MaxAgeDays

- What it does: freshness window for acceptable masters.
- Higher value: fewer rebuilds, older masters accepted longer.
- Lower value: more rebuilds, stricter freshness.

### Master Library Folder

- What it does: where SeeDark writes and scans master FITS.
- Implication: this folder is the runtime source of truth for master availability checks.

### Raw darks folder (optional)

- What it does: when set, this folder is the root for reading raw DARKs (stacker, same-night counts), saving Auto-captured DARKs, and optional raw purge.
- When empty: SeeDark uses NINA `Image File Path`, but if your DARK **pattern** puts `$$IMAGETYPE$$` in a **folder** (e.g. `CALIBRATION\$$IMAGETYPE$$s\...`), the scan is limited to that expanded subtree (typically `...\CALIBRATION\DARKs`) when that folder exists; if it does not exist yet, SeeDark falls back to scanning the full NINA image path until the folder appears.

**Recommendation — keep DARK raws in a dedicated tree.** The stacker finds DARKs by walking every `*.fit*` under the resolved root and reading FITS headers. If that root is your whole imaging library (lights, flats, masters, etc.), runs can take a long time and look stuck. Prefer either:

- setting **Raw darks folder** to a path used only for DARK raws, or  
- a NINA DARK pattern that places `$$IMAGETYPE$$` in the **path** so saves land under a DARK-only branch (e.g. `CALIBRATION\DARKs\...`).

If your pattern only customizes the **file name** and everything shares one big **Image File Path**, expect a full-library scan whenever you stack.

### NINA DARK file pattern (save layout)

- What it does: SeeDark uses NINA's DARK file pattern when saving Auto captures (`SaveToDisk` rules).
- Source of truth: NINA `Image File Path` (or **Raw darks folder** when set) as `FilePath`, plus the profile DARK pattern from `GetFilePattern("DARK")`.
- Implication: Auto-captured DARK raws use NINA token-expanded subfolders/filenames under that root.
- Read side: files are still selected by FITS headers (`FILTER=DARK`, etc.); the pattern does not filter filenames on read, only helps derive the scan root when `$$IMAGETYPE$$` is a path segment and **Raw darks folder** is empty.
- Failure behavior: if an Auto-captured raw cannot be saved via NINA pattern rules, Auto dark capture aborts immediately (hard-fail).

### Temp Bucket Size (2C or 3C)

- What it does: controls temperature band width.
- 2C: more buckets, more precision, potentially more master variants.
- 3C: fewer buckets, less churn, broader grouping.

### AutoDarkMaxWarmerBucketSteps

- What it does: limits how far above the start bucket Auto is allowed to follow warming into missing buckets.
- Lower values: tighter containment to starting conditions.
- Higher values: more willingness to follow warming trend.

### DeleteRawsAfterMaxAge

- What it does: deletes DARK raws older than age policy from the resolved raw-dark root (same as stacker scan root).
- Default: off.
- Implication: saves storage, but is irreversible for removed raws.

### WriteNinaLiveMasters

- What it does: writes additional NINA-format masters alongside SIRIL/PixInsight float masters.
- Implication: useful for NINA-specific consumers; adds extra output files.

### DiscordWebhookUrl / DiscordVerbosePerFrame

- What it does: mirrors status to Discord.
- Implication: verbose mode can be very chatty; best in a dedicated channel.

## What to check when behavior surprises you

If capture seems to skip unexpectedly:

- verify exposure/gain match your intended profile,
- confirm bucket size and current sensor bucket,
- check whether same-night raws already satisfy that bucket,
- check whether a fresh master already exists in the master library.

If capture seems to continue "longer than expected":

- it may be bounded overshoot while trying to reach a needed warmer bucket,
- check warmer-step limit and current temperature trend.

## Bottom line

SeeDark is intentionally conservative with your night:

- collect when needed,
- stop when enough has been collected,
- keep just enough flexibility to follow natural warming into the next useful bucket,
- then build masters in one clean pass near session end.
