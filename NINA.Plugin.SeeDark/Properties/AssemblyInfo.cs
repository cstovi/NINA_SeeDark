using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;

[assembly: AssemblyTitle("SeeDark")]
[assembly: AssemblyMetadata("MinimumApplicationVersion", "3.0.0.1001")]
[assembly: AssemblyMetadata("FeaturedImageURL", "https://i.ibb.co/NdXb8D39/seedark.png")]
[assembly: AssemblyDescription("SeeDark manages master darks for Seestar automatically — it captures darks only when your library is missing a fresh master for the current temperature, gain, and exposure, and skips silently when one already exists. A companion instruction stacks raw darks into per-pixel median masters.")]
[assembly: AssemblyMetadata("ShortDescription", "Captures master darks only when needed for the current sensor temperature, gain, and exposure — skips automatically when a fresh master exists. Includes a stacker to build median masters from raw darks.")]
[assembly: AssemblyMetadata("LongDescription", @"SeeDark Dark Manager

Add a SeeDark Dark Manager container anywhere in your NINA sequence. Each time it runs, it reads the current sensor temperature, maps it to the nearest temperature band (2°C or 3°C, configurable), and checks whether a fresh master dark already exists for that band, gain, and exposure.

Already covered — the container skips silently and your sequence continues. Missing or stale — it automatically captures 30 dark frames in the correct band. Proactive warmup — if the current band is already covered but the next warmer band isn't, SeeDark starts capturing immediately, counting frames once the sensor reaches the target band.

Each container has its own exposure and gain settings, so you can run multiple containers for different imaging configurations in the same sequence.

SeeDark Stack Master Darks

Add this instruction to build masters from raws. It scans the resolved raw-dark root (optional Raw darks folder, or the DARK subtree under NINA image path when your pattern uses `$$IMAGETYPE$$` as a folder, or the full image path), groups frames by temperature band, gain, and exposure, and writes a per-pixel median master FITS for each group that is missing or stale — leaving fresh masters untouched.

An optional Discord webhook mirrors key events (skip decisions, capture results, stack completions) to a channel of your choice. Enable Verbose in the plugin options to also receive per-frame capture lines — a dedicated channel is recommended for that setting.

Credit to @Ettaswell-Jon for single handedly exposing all the bugs in early versions!")]
[assembly: AssemblyCompany("Carl Stovell")]
[assembly: AssemblyProduct("NINA.Plugin.SeeDark")]
[assembly: AssemblyVersion("1.15.2.0")]
[assembly: AssemblyFileVersion("1.15.2.0")]
[assembly: AssemblyMetadata("ChangelogURL", "https://github.com/cstovi/NINA_SeeDark/releases")]
[assembly: Guid("A5E7F3C1-2D4B-4A8E-9F1C-3B6D7E8A0F2C")]
[assembly: ThemeInfo(
    ResourceDictionaryLocation.None,
    ResourceDictionaryLocation.SourceAssembly
)]
