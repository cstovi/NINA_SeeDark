# Changelog

All notable changes to this project are documented in this file.

## v1.16.2 - 2026-05-09

- Auto dark save: use NINA **Image File Path** as `FileSaveInfo.FilePath` (not the narrowed `CALIBRATION\DARKs` scan root). Passing the narrowed root together with a DARK pattern that already starts with `CALIBRATION\$$IMAGETYPE$$s\` caused `SaveToDisk` to nest `CALIBRATION\DARKs` twice.

## v1.16.1 - 2026-05-09

- Plugin options UI: remove duplicate Ettaswell-Jon credit line (credit remains in catalog `LongDescription` only).

## v1.16.0 - 2026-05-09

- Removed the plugin **Raw darks folder** option (it was added in v1.15.0). Raw DARK discovery, Auto save, same-night counts, and optional raw purge use **only** NINA **Image File Path** and the profile DARK pattern, with the same `$$IMAGETYPE$$` path narrowing as before. Updated docs accordingly.

## v1.15.3 - 2026-05-09

- README and workflow guide: recommend a dedicated raw-DARK folder or `$$IMAGETYPE$$`-in-path layout so **Stack Master Darks** does not scan the entire NINA image tree when other FITS live there; README stacker/deletion wording aligned with resolved raw-dark root.
- `CLAUDE.md`: stacker bullet notes the same library-layout guidance for agents.

## v1.15.2 - 2026-05-09

- Plugin catalog long description and options panel: credit line for early testing feedback.

## v1.15.1 - 2026-05-09

- Auto dark save: set sequential `ExposureNumber` on each frame before `SaveToDisk` so `$$FRAMENR$$` in the NINA file pattern increments (NINA derives it from image metadata, which stayed at 0 for sequencer-only captures).

## v1.15.0 - 2026-05-09

- Raw DARK discovery uses a resolved root: optional **Raw darks folder** (plugin setting) when set; otherwise, when the NINA DARK pattern includes `$$IMAGETYPE$$` in a path segment, only the expanded DARK subtree under NINA image file path (e.g. `CALIBRATION\DARKs`); otherwise the full NINA image path. Auto dark save and optional raw purge use the same root.
- Plugin options: new **Raw darks folder (optional)** with browse button.

## v1.14.0 - 2026-05-09

- Stack Master Darks: master filenames now include camera gain (`_g{gain}_`) so different gains no longer share the same prefix and overwrite each other; superseded-file cleanup is gain-scoped. Master discovery still uses FITS headers, so older gain-less filenames remain readable.

## v1.13.0 - 2026-05-09

- Stack Master Darks text log now records full paths when writing SIRIL/NINALIVE masters and when removing superseded files (not only filenames).

## v1.12.0 - 2026-05-08

- Removed raw dark archiving behavior and lifecycle archive toggle.
- Replaced archive cleanup with a single optional in-place raw cleanup setting: `DeleteRawsAfterMaxAge`.
- Stacker now always uses active raws only; when cleanup is enabled it deletes DARK raws older than `MaxAgeDays` from the active NINA save location.
- Updated plugin settings/UI and docs to reflect the simplified raw retention model.

## v1.11.0 - 2026-05-08

- Updated SeeDark Auto capture flow so newly captured DARK raws are relocated into the configured `Raw darks folder`.
- Kept stacker and same-night sufficiency checks reading from that same configured folder for a single explicit source of truth.
- Updated UI/docs wording to clarify `Raw darks folder` can be independent from NINA's general image save path.

## v1.10.0 - 2026-05-08

- Added required `Raw darks folder` plugin option so raw DARK source is explicit and user-configured.
- Replaced hardcoded `<NINA FilePath>\CALIBRATION\DARKs` assumptions in stacker and same-night raw sufficiency checks.
- Added clear validation errors when raw darks folder is missing or does not exist.
- Updated plugin options UI and docs to reflect explicit raw dark source configuration.

## v1.9.0 - 2026-05-08

- Added same-night raw sufficiency checks in Auto mode so SeeDark can skip additional capture for a bucket when enough raws already exist for tonight, even before master rebuild runs.
- Preserved warmer-bucket progression by allowing bounded overshoot in the current bucket up to 60 raws when the next warmer bucket still needs collection.
- Updated Auto behavior docs to reflect same-night sufficiency guard, bounded overshoot, and end-of-session stacking guidance.
- Added STACKCNT-gated master rebuild improvement: fresh masters now rebuild when valid contributor count metadata exists and more eligible raws are available than were used previously (legacy masters without STACKCNT remain missing/expired-only).

## v1.8.0 - 2026-05-07

- Clarified the `Additional NINA masters` option tooltip to explain the NINA-format master is primarily for Livestack and potentially other NINA dark-consuming workflows.

## v1.7.1 - 2026-05-07

- `_archived` subfolder is now only created inside the raw darks folder when lifecycle management is enabled, avoiding unused folder creation for users who never enable it.

## v1.7.0 - 2026-05-07

- Skip notification now confirms both checked buckets: "Matching dark exists — skipping (22°C ✓, 24°C ✓)".
- Execution mode line suppressed from Discord in Auto mode (still written to the file log).
- Plugin options: added "overridable per container" hint next to default exposure and gain fields.
- Renamed "Stack SeeDark Master Darks" instruction to "SeeDark Stack Master Darks" for consistent naming.
- Updated plugin description and short description for accuracy (reflects Auto-only UI, proactive warmup, skip behaviour).
- Added changelog URL pointing to GitHub releases.

## v1.6.0 - 2026-05-06

- Auto dark capture **retargets** when the sensor moves to another temperature bucket that also has no acceptable master (same exposure, gain, scope, and max-age rules as matching). The accepted-frame counter resets for the new bucket so stacking groups stay coherent; drift still stops the run when the new bucket already has a master.

## v1.5.0 - 2026-05-06

- Reordered options so `Default mode (new containers)` now appears below the age/lifecycle controls.
- Improved option helper text readability with concise wording and wrapping in the right-hand description column.

## v1.4.0 - 2026-05-06

- Added a user-facing `README.md` for GitHub with setup, Auto-first mode behavior, matching model, lifecycle notes, logging paths, and release links.
- Kept `CLAUDE.md` as implementation/agent guidance and clarified user-facing docs should live in README.

## v1.3.0 - 2026-05-06

- Set default mode for new `SeeDark Dark Manager` containers to `Auto`.
- Added plugin option `Default mode (new containers)` with clear behavior text (`Auto` vs `Manual`).
- Kept `Manual` selectable via plugin configuration for users who prefer child-instruction workflows.
- Cleaned up temporary TODO list and updated project docs to match current shipped behavior.

## v1.2.0 - 2026-05-06

- Added release tracking with this `CHANGELOG.md`.
- Established release cadence: each version bump should include an updated changelog entry and a GitHub release.

## v1.1.0 - 2026-05-06

- Added Auto mode dark capture in `SeeDark Dark Manager` (DARK filter/type, user gain/exposure, default offset, segment target/min/max strategy with temperature drift guard).
- Updated container behavior so Auto mode ignores internal child instructions/triggers/conditions and shows explicit UI warning.
- Introduced non-overlapping shared temperature bucketing logic across match and stack flows.
- Added opt-in lifecycle controls for raw dark archives and optional age-based archive cleanup.
- Added strict age-window master rebuild logic using FITS dates and archived-frame recovery.
- Simplified settings/UI with auto-save behavior, hidden internal advanced controls, and improved Seestar-focused defaults.
