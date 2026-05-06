# Changelog

All notable changes to this project are documented in this file.

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
