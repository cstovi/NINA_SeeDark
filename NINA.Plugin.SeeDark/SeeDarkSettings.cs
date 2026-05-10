using System;
using System.IO;
using Newtonsoft.Json;

namespace NINA.Plugin.SeeDark {

    public class SeeDarkSettings {
        public static readonly string DataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NINA", "SeeDark");

        public double TargetExposure { get; set; } = 20.0;
        public int MaxAgeDays { get; set; } = 180;
        public int Gain { get; set; } = 200;
        public string MasterLibraryFolder { get; set; } = "";
        public int MinFrameCount { get; set; } = 20;
        public int MaxFrameCount { get; set; } = 50;
        public bool DeleteRawsAfterMaxAge { get; set; } = false;
        [JsonProperty("DeleteArchivedRawsAfterMaxAge")]
        private bool LegacyDeleteArchivedRawsAfterMaxAge {
            set => DeleteRawsAfterMaxAge = value;
        }
        /// <summary>When true, the stacker also writes a NINA-format master (BITPIX=16, BZERO=32768) alongside the always-present float32 (F32) master.</summary>
        public bool WriteNinaLiveMasters { get; set; } = false;
        public string DiscordWebhookUrl { get; set; } = "";
        /// <summary>Optional label prepended to every Discord notification, e.g. "Backyard rig".</summary>
        public string DiscordScopeName { get; set; } = "";
        /// <summary>When true, per-frame auto dark capture log lines are mirrored to Discord (noisy).</summary>
        public bool DiscordVerbosePerFrame { get; set; } = false;
        public int TempBucketSize { get; set; } = 2;
        public int StackTolerance { get; set; } = 2;
        public int PreBucketLeadC { get; set; } = 2;
        public int DefaultExecutionMode { get; set; } = 1; // 0=Manual, 1=Auto
        /// <summary>How many discrete temp bands warmer than the run's starting bucket Auto may retarget into (0–3).</summary>
        public int AutoDarkMaxWarmerBucketSteps { get; set; } = 0;

        private static string SettingsPath => Path.Combine(DataFolder, "settings.json");

        public static SeeDarkSettings Load(string ninaImagePath) {
            SeeDarkSettings s;
            try {
                s = File.Exists(SettingsPath)
                    ? JsonConvert.DeserializeObject<SeeDarkSettings>(File.ReadAllText(SettingsPath)) ?? new SeeDarkSettings()
                    : new SeeDarkSettings();
            } catch { s = new SeeDarkSettings(); }

            if (string.IsNullOrEmpty(s.MasterLibraryFolder)) s.MasterLibraryFolder = Path.Combine(ninaImagePath, "MASTERs");

            try {
                Directory.CreateDirectory(DataFolder);
            } catch { }

            return s;
        }

        public void Save() {
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
                File.WriteAllText(SettingsPath, JsonConvert.SerializeObject(this, Formatting.Indented));
            } catch { }
        }
    }
}
