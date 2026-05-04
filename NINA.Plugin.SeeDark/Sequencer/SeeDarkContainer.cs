using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NINA.Core.Model;
using NINA.Profile.Interfaces;
using NINA.Sequencer.Container;
using NINA.Sequencer.Container.ExecutionStrategy;
using NINA.Sequencer.SequenceItem;

namespace NINA.Plugin.SeeDark.Sequencer {

    [Export(typeof(ISequenceItem))]
    [Export(typeof(ISequenceContainer))]
    [ExportMetadata("Name", "SeeDark Dark Gap Check")]
    [ExportMetadata("Description", "Runs child instructions only when no matching master dark exists for the current sensor temperature, gain, and exposure")]
    [ExportMetadata("Icon", "SeeDark_Icon")]
    [ExportMetadata("Category", "SeeDark")]
    public class SeeDarkContainer : SequenceContainer {

        private readonly SeeDarkPlugin _plugin;
        private readonly string _logFilePath;

        private double _targetExposure = 20.0;
        [JsonProperty]
        public double TargetExposure {
            get => _targetExposure;
            set { _targetExposure = value; RaisePropertyChanged(); }
        }

        private int _gain = 200;
        [JsonProperty]
        public int Gain {
            get => _gain;
            set { _gain = value; RaisePropertyChanged(); }
        }

        [ImportingConstructor]
        public SeeDarkContainer(
            SeeDarkPlugin plugin,
            IProfileService profileService,
            [ImportMany] IEnumerable<ISequenceItem> sequenceItems)
            : base(new SequentialStrategy()) {
            _plugin = plugin;
            TargetExposure = plugin.Settings.TargetExposure;
            Gain           = plugin.Settings.Gain;
            Name = "SeeDark Dark Gap Check";
            if (System.Windows.Application.Current?.Resources["SeeDark_Icon"] is System.Windows.Media.GeometryGroup icon)
                Icon = icon;
            _logFilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NINA", "SeeDark", $"seedark_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log");

            if (plugin.Settings.SeedSmartExposure) {
                var proto = sequenceItems.FirstOrDefault(i => i.GetType().Name == "SmartExposure");
                if (proto != null) {
                    var se = proto.Clone() as ISequenceItem;
                    if (se != null) {
                        SetProp(se, "ExposureTime",         TargetExposure);
                        SetProp(se, "MinExposure",          TargetExposure);
                        SetProp(se, "MaxExposure",          TargetExposure);
                        SetProp(se, "Gain",                 Gain);
                        SetProp(se, "ExposureCount",        20);
                        SetProp(se, "DitherAfterExposures", 0);
                        SetProp(se, "ImageType",            "DARK");
                        var darkFilter = profileService.ActiveProfile.FilterWheelSettings.FilterWheelFilters
                            .Cast<object>()
                            .FirstOrDefault(f => "Dark".Equals(
                                f.GetType().GetProperty("Name")?.GetValue(f)?.ToString(),
                                StringComparison.OrdinalIgnoreCase));
                        if (darkFilter != null)
                            SetProp(se, "Filter", darkFilter);
                        se.AttachNewParent(this);
                        Add(se);
                    }
                }
            }
        }

        private static void SetProp(object obj, string name, object val) {
            try { obj.GetType().GetProperty(name)?.SetValue(obj, val); } catch { }
        }

        private SeeDarkContainer(SeeDarkContainer cloneMe) : base(new SequentialStrategy()) {
            _plugin = cloneMe._plugin;
            _logFilePath = cloneMe._logFilePath;
            TargetExposure = cloneMe.TargetExposure;
            Gain           = cloneMe.Gain;
            Name = "SeeDark Dark Gap Check";
            Icon = cloneMe.Icon;
        }

        [System.Runtime.Serialization.OnDeserializing]
        private void OnDeserializing(System.Runtime.Serialization.StreamingContext context) {
            Items.Clear();
        }

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            if (NeedsDarks())
                await base.Execute(progress, token);
        }

        private bool NeedsDarks() {
            double temp = GetSensorTempFromMediator();
            if (double.IsNaN(temp)) {
                Log("⚠️ Camera temperature unavailable — darks needed!");
                return true;
            }

            var scopeId = _plugin.GetScopeId();
            if (string.IsNullOrEmpty(scopeId)) {
                Log("⚠️ Scope ID unavailable (camera not connected) — darks needed!");
                return true;
            }

            int bs = _plugin.Settings.TempBucketSize;
            int bucket = (int)(Math.Round(temp / bs) * bs);
            Log($"🌡️ Sensor temp {temp:F1}°C → bucket {bucket}°C ({bs}°C steps), gain {Gain}, scope {scopeId}, target exposure {TargetExposure}s");

            var masters = ScanMasterFolder();
            if (masters.Length == 0) {
                Log("🌑 No masters found in master library folder — darks needed!");
                return true;
            }

            int tol = _plugin.Settings.StackTolerance;
            var cutoff = DateTime.Now.AddDays(-_plugin.Settings.MaxAgeDays);
            bool needsDarks = !masters.Any(r =>
                Math.Abs(r.Temp - temp) <= tol &&
                Math.Abs(r.Exposure - TargetExposure) < 0.5 &&
                r.Gain == Gain &&
                r.Scope == scopeId &&
                r.DateCreated >= cutoff);

            Log(needsDarks ? "🌑 No matching dark found — darks needed!" : "✅ Matching dark exists — skipping");

            double warmThreshold = bucket + (tol - 2) * 0.5;
            if (needsDarks && temp > warmThreshold) {
                Log($"🌡️ Sensor at {temp:F1}°C already past entry point for {bucket}°C bucket ({warmThreshold:F1}°C) — skipping");
                return false;
            }
            return needsDarks;
        }

        private double GetSensorTempFromMediator() {
            try {
                var info = _plugin.CameraMediator.GetInfo();
                if (info != null && info.Connected && !double.IsNaN(info.Temperature))
                    return info.Temperature;
            } catch { }
            return double.NaN;
        }

        private MasterRecord[] ScanMasterFolder() {
            var folder = _plugin.Settings.MasterLibraryFolder;
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                return Array.Empty<MasterRecord>();

            var results = new List<MasterRecord>();
            foreach (var path in Directory.GetFiles(folder, "*.fit*")) {
                var h = FitsHeaderReader.ReadHeaders(path);
                if (h == null) { Log($"Skipped unreadable: {Path.GetFileName(path)}"); continue; }

                if (!h.TryGetValue("IMAGETYP", out var imagetyp) ||
                    imagetyp.IndexOf("DARK", StringComparison.OrdinalIgnoreCase) < 0) continue;

                h.TryGetValue("CCD-TEMP", out var tempStr);
                h.TryGetValue("EXPTIME",  out var expStr);
                h.TryGetValue("GAIN",     out var gainStr);
                h.TryGetValue("INSTRUME", out var instrume);
                h.TryGetValue("DATE-OBS", out var dateStr);

                if (string.IsNullOrEmpty(tempStr) || string.IsNullOrEmpty(expStr) || string.IsNullOrEmpty(instrume))
                    continue;

                try {
                    double rawTemp    = double.Parse(tempStr, CultureInfo.InvariantCulture);
                    int    masterTemp = (int)Math.Round(rawTemp);
                    double exp        = double.Parse(expStr, CultureInfo.InvariantCulture);
                    var    parts   = instrume.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    string scopeId = parts.Length >= 2 ? parts[1] : instrume;
                    int    gain    = int.TryParse(gainStr, out var g) ? g : 0;
                    var    date    = string.IsNullOrEmpty(dateStr)
                        ? DateTime.MinValue
                        : DateTime.Parse(dateStr, CultureInfo.InvariantCulture);
                    results.Add(new MasterRecord(masterTemp, exp, gain, scopeId, date));
                } catch { }
            }

            Log($"🔭 Scanned {results.Count} master dark(s) from {folder}");
            return results.ToArray();
        }

        private void Log(string message) {
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(_logFilePath)!);
                File.AppendAllText(_logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            } catch { }
            _ = _plugin.SendDiscordAsync(message);
        }

        public override object Clone() {
            var clone = new SeeDarkContainer(this);
            foreach (var item in Items) {
                if (item.Clone() is ISequenceItem cloned) {
                    cloned.AttachNewParent(clone);
                    clone.Add(cloned);
                }
            }
            return clone;
        }

        private record MasterRecord(int Temp, double Exposure, int Gain, string Scope, DateTime DateCreated);
    }
}
