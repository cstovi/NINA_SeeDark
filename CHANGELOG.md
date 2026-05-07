# Changelog

All notable changes to this project are documented in this file.

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

- Added a user-facing `README.md` for GitHub with setup, mode behavior, matching model, lifecycle notes, logging paths, and release links.
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

- Added Auto mode dark capture in `SeeDark Dark Manager` (DARK filter/type, user gain/exposure, default offset, target/min/max frame strategy with temperature drift guard).
- Updated container behavior so Auto mode ignores internal child instructions/triggers/conditions and shows explicit UI warning.
- Introduced non-overlapping shared temperature bucketing logic across match and stack flows.
- Added opt-in lifecycle controls for raw dark archives and optional age-based archive cleanup.
- Added strict age-window master rebuild logic using FITS dates and archived-frame recovery.
- Simplified settings/UI with auto-save behavior, hidden internal advanced controls, and improved Seestar-focused defaults.
