using System;
using System.IO;
using Newtonsoft.Json;

namespace NINA.Plugin.SeeDark {

    public class SeeDarkSettings {
        public string DarkLibraryCsvPath { get; set; } = "";
        public double TargetExposure { get; set; } = 60.0;
        public int MaxAgeDays { get; set; } = 180;
        public int AlpacaPort { get; set; } = 11111;

        private static string SettingsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NINA", "SeeDark", "settings.json");

        public static SeeDarkSettings Load() {
            try {
                if (File.Exists(SettingsPath))
                    return JsonConvert.DeserializeObject<SeeDarkSettings>(File.ReadAllText(SettingsPath)) ?? new SeeDarkSettings();
            } catch { }
            return new SeeDarkSettings();
        }

        public void Save() {
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
                File.WriteAllText(SettingsPath, JsonConvert.SerializeObject(this, Formatting.Indented));
            } catch { }
        }
    }
}
