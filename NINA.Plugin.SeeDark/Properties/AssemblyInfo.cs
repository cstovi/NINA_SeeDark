using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;

[assembly: AssemblyTitle("SeeDark")]
[assembly: AssemblyMetadata("MinimumApplicationVersion", "3.0.0.1001")]
[assembly: AssemblyMetadata("FeaturedImageURL", "https://i.ibb.co/NdXb8D39/seedark.png")]
[assembly: AssemblyDescription("Designed for Seestar. Checks whether a master dark is needed for the current sensor temperature, gain, and exposure — and runs your instructions to capture darks only if so. Optionally stacks raw darks into master files.")]
[assembly: AssemblyMetadata("ShortDescription", "For Seestar users: only runs dark capture when a matching master is missing for current temp/gain/exposure, with optional master-dark stacking.")]
[assembly: AssemblyMetadata("LongDescription", @"Drop a SeeDark Dark Manager container anywhere in your sequence and add your dark-capture instructions inside it — e.g. a Smart Exposure taking darks with the dark filter. Set the exposure and gain on the container to match what you're imaging — the container will run its children only if no master dark exists for the current sensor temperature, gain, and exposure. For multiple filters or exposures, use one container per combination.

When you've accumulated enough raw darks, optionally add a Stack SeeDark Master Darks instruction to median-stack them into master files automatically. Set the master library folder at the top, and configure the raw darks folder and minimum frame count in the Dark Stacker section above.")]
[assembly: AssemblyCompany("Carl Stovell")]
[assembly: AssemblyProduct("NINA.Plugin.SeeDark")]
[assembly: AssemblyVersion("1.3.0.0")]
[assembly: AssemblyFileVersion("1.3.0.0")]
[assembly: Guid("A5E7F3C1-2D4B-4A8E-9F1C-3B6D7E8A0F2C")]
[assembly: ThemeInfo(
    ResourceDictionaryLocation.None,
    ResourceDictionaryLocation.SourceAssembly
)]
