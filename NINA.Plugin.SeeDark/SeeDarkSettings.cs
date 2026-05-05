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
        public string RawDarksFolder { get; set; } = "";
        public string MasterLibraryFolder { get; set; } = "";
        public int MinFrameCount { get; set; } = 20;
        public string DiscordWebhookUrl { get; set; } = "";
        public int TempBucketSize { get; set; } = 2;
        public int StackTolerance { get; set; } = 2;
        public double PreBucketLeadC { get; set; } = 0.5;

        private static string SettingsPath => Path.Combine(DataFolder, "settings.json");

        public static SeeDarkSettings Load(string ninaImagePath) {
            SeeDarkSettings s;
            try {
                s = File.Exists(SettingsPath)
                    ? JsonConvert.DeserializeObject<SeeDarkSettings>(File.ReadAllText(SettingsPath)) ?? new SeeDarkSettings()
                    : new SeeDarkSettings();
            } catch { s = new SeeDarkSettings(); }

            if (string.IsNullOrEmpty(s.RawDarksFolder))      s.RawDarksFolder      = Path.Combine(ninaImagePath, "DARKs");
            if (string.IsNullOrEmpty(s.MasterLibraryFolder)) s.MasterLibraryFolder = Path.Combine(ninaImagePath, "MASTERs");

            try {
                Directory.CreateDirectory(DataFolder);
                Directory.CreateDirectory(s.RawDarksFolder);
                Directory.CreateDirectory(s.MasterLibraryFolder);
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
