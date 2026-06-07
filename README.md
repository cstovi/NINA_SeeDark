# NINA SeeDark

SeeDark helps Seestar users manage dark frames in N.I.N.A. by only capturing/stacking when masters are missing, stale, or can be improved with more valid raws.

## License

Mozilla Public License 2.0 — see `LICENSE.txt`.

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

## Screenshots

<img width="2306" height="187" alt="image" src="https://github.com/user-attachments/assets/3fa2b32f-9a08-48ca-8be7-2bed83c79418" />

<img width="1111" height="1178" alt="image" src="https://github.com/user-attachments/assets/39976391-cb83-4f06-8f60-7427b1b23f11" />

<img width="2122" height="527" alt="image" src="https://github.com/user-attachments/assets/a6a618bf-c7d5-4a97-ac49-158d5acf9a88" />


## Main Components

- **SeeDark Dark Manager** — sequence container that decides whether darks are needed. Auto mode (default) captures internally; legacy Manual mode exists for backward compatibility.
- **SeeDark Stack Master Darks** — sequence instruction that scans raw dark FITS and builds per-pixel median master FITS.

## Behavior Overview

At runtime SeeDark checks for a matching master dark by temperature bucket (2°C or 3°C), exposure (±0.5s), gain, scope ID, and max age. Missing or stale → captures; valid → skips. Auto capture uses segment-based collection with temperature drift guard, can retarget to cooler/warmer missing buckets, and includes same-night raw sufficiency checks to avoid over-collecting.

For a detailed plain-English explanation of all runtime behavior, settings implications, and troubleshooting, see:

➡ **[SeeDark Workflow Guide](docs/SeeDark-Workflow-Guide.md)**

## Settings

Saved to `%LOCALAPPDATA%\NINA\SeeDark\settings.json`.

- **Target Exposure** — exposure to match/capture
- **Gain** — camera gain
- **MaxAgeDays** — freshness window for acceptable masters
- **Master Library Folder** — where masters are written and scanned
- **Temp Bucket Size** — 2°C or 3°C
- **AutoDarkMaxWarmerBucketSteps** — warmer retarget limit above start bucket
- **DeleteRawsAfterMaxAge** — opt-in raw DARK cleanup
- **WriteNinaLiveMasters** — also write NINALIVE-format masters
- **DiscordWebhookUrl / DiscordGeneralWebhookUrl** — Discord integration

Advanced hidden defaults: `PreBucketLeadC=2°C`, `MinFrameCount=20`, `MaxFrameCount=50`, `DefaultExecutionMode=Auto`.

## Logs

- Container: `%LOCALAPPDATA%\NINA\SeeDark\seedark_yyyy-MM-dd_HH-mm-ss.log`
- Stacker: `%LOCALAPPDATA%\NINA\SeeDark\stack_yyyy-MM-dd_HH-mm-ss.log`

## Releases

- GitHub Releases: <https://github.com/cstovi/NINA_SeeDark/releases>
- Changelog: `CHANGELOG.md`

## Support

If you use and like anything I've done, support on [Ko-fi](https://ko-fi.com/turnpike47298) is appreciated to encourage me to keep going!
