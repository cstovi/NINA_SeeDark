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
using NINA.Core.Model.Equipment;
using NINA.Equipment.Model;
using NINA.Sequencer.Container;
using NINA.Sequencer.Container.ExecutionStrategy;
using NINA.Sequencer.SequenceItem;

namespace NINA.Plugin.SeeDark.Sequencer {
    public enum DarkExecutionMode {
        Manual = 0,
        Auto = 1
    }

    [Export(typeof(ISequenceItem))]
    [Export(typeof(ISequenceContainer))]
    [ExportMetadata("Name", "SeeDark Dark Manager")]
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

        private DarkExecutionMode _executionMode = DarkExecutionMode.Auto;
        [JsonProperty]
        public DarkExecutionMode ExecutionMode {
            get => _executionMode;
            set { _executionMode = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(ShowManualChildren)); }
        }

        public bool ShowManualChildren => ExecutionMode == DarkExecutionMode.Manual;

        [ImportingConstructor]
        public SeeDarkContainer(SeeDarkPlugin plugin) : base(new SequentialStrategy()) {
            _plugin = plugin;
            TargetExposure = plugin.Settings.TargetExposure;
            Gain           = plugin.Settings.Gain;
            ExecutionMode  = plugin.DefaultExecutionMode;
            Name = "SeeDark Dark Manager";
            if (System.Windows.Application.Current?.Resources["SeeDark_Icon"] is System.Windows.Media.GeometryGroup icon)
                Icon = icon;
            _logFilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NINA", "SeeDark", $"seedark_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log");
        }

        private SeeDarkContainer(SeeDarkContainer cloneMe) : base(new SequentialStrategy()) {
            _plugin = cloneMe._plugin;
            _logFilePath = cloneMe._logFilePath;
            TargetExposure = cloneMe.TargetExposure;
            Gain           = cloneMe.Gain;
            ExecutionMode  = cloneMe.ExecutionMode;
            Name = "SeeDark Dark Manager";
            Icon = cloneMe.Icon;
        }

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            if (!NeedsDarks()) return;
            if (ExecutionMode == DarkExecutionMode.Auto) {
                await ExecuteAutoCapture(progress, token);
                return;
            }
            await base.Execute(progress, token);
        }

        private async Task ExecuteAutoCapture(IProgress<ApplicationStatus> progress, CancellationToken token) {
            try {
                await ExecuteAutoCaptureCore(progress, token);
            } finally {
                progress?.Report(new ApplicationStatus());
            }
        }

        private async Task ExecuteAutoCaptureCore(IProgress<ApplicationStatus> progress, CancellationToken token) {
            const int targetFrames = 30;
            const int minFrames = 20;
            const int maxFrames = 50;
            const int maxConsecutiveBucketMisses = 3;

            double startTemp = GetSensorTempFromMediator();
            if (double.IsNaN(startTemp)) {
                Log("⚠️ Auto mode could not read sensor temperature at start; skipping auto capture and running child instructions instead.");
                await base.Execute(progress, token);
                return;
            }

            int bucketStepC = Math.Max(1, _plugin.Settings.TempBucketSize);
            int targetBucket = TemperatureBucketing.ToBucket(startTemp, bucketStepC);
            var darkFilter = _plugin.GetDarkFilter();
            if (darkFilter == null) {
                Log("⚠️ Auto mode could not find a DARK filter definition. Configure a DARK filter or use Manual mode.");
                return;
            }

            Log($"🤖 Auto mode starting dark capture: target {targetFrames}, min {minFrames}, max {maxFrames}, target bucket {targetBucket}°C");
            Log("🔁 If the sensor drifts to another temperature bucket, capture continues when that bucket also has no acceptable master (same exposure, gain, scope, max age).");

            var masters = ScanMasterFolder();
            var masterCutoff = DateTime.Now.AddDays(-_plugin.Settings.MaxAgeDays);
            var scopeId = _plugin.GetScopeId();

            try {
                await _plugin.FilterWheelMediator.ChangeFilter(darkFilter, token, progress);
                Log($"🎛️ Switched filter to {darkFilter.Name}");
            } catch (Exception ex) {
                Log($"⚠️ Failed to switch to DARK filter: {ex.Message}");
                return;
            }

            int goodFrames = 0;
            int lifetimeAccepted = 0;
            int attempts = 0;
            int consecutiveBucketMisses = 0;

            ReportAutoDarkCaptureProgress(progress, goodFrames, targetFrames);

            while (!token.IsCancellationRequested && attempts < maxFrames && goodFrames < targetFrames) {
                attempts++;
                var capture = new CaptureSequence {
                    ExposureTime = TargetExposure,
                    Gain = Gain,
                    Offset = -1,
                    ImageType = CaptureSequence.ImageTypes.DARK,
                };

                ReportAutoDarkCaptureProgress(progress, goodFrames, targetFrames);

                try {
                    await _plugin.ImagingMediator.CaptureImage(capture, token, progress);
                } catch (Exception ex) {
                    Log($"⚠️ Auto dark capture failed on frame {attempts}: {ex.Message}");
                    break;
                }

                double frameTemp = GetSensorTempFromMediator();
                if (double.IsNaN(frameTemp)) {
                    Log($"⚠️ Frame {attempts}: temperature unavailable; counting frame toward target.");
                    goodFrames++;
                    lifetimeAccepted++;
                    ReportAutoDarkCaptureProgress(progress, goodFrames, targetFrames);
                    continue;
                }

                int frameBucket = TemperatureBucketing.ToBucket(frameTemp, bucketStepC);
                if (frameBucket == targetBucket) {
                    goodFrames++;
                    lifetimeAccepted++;
                    consecutiveBucketMisses = 0;
                    Log($"📸 Frame {attempts}: temp {frameTemp:F1}°C bucket {frameBucket}°C (target) — accepted ({goodFrames}/{targetFrames})", discordVerboseOnly: true);
                    ReportAutoDarkCaptureProgress(progress, goodFrames, targetFrames);
                } else if (LacksAcceptableMaster(frameBucket, masters, masterCutoff, scopeId)) {
                    int previousTarget = targetBucket;
                    targetBucket = frameBucket;
                    goodFrames = 1;
                    lifetimeAccepted++;
                    consecutiveBucketMisses = 0;
                    Log($"📸 Frame {attempts}: temp {frameTemp:F1}°C bucket {frameBucket}°C — drifted out of {previousTarget}°C target band; bucket {frameBucket}°C also has no acceptable master in the library — retargeting capture here ({goodFrames}/{targetFrames}).", discordVerboseOnly: true);
                    ReportAutoDarkCaptureProgress(progress, goodFrames, targetFrames);
                } else {
                    consecutiveBucketMisses++;
                    Log($"📸 Frame {attempts}: temp {frameTemp:F1}°C bucket {frameBucket}°C (target {targetBucket}°C) — drift count {consecutiveBucketMisses}/{maxConsecutiveBucketMisses}", discordVerboseOnly: true);
                    ReportAutoDarkCaptureProgress(progress, goodFrames, targetFrames);
                    if (consecutiveBucketMisses >= maxConsecutiveBucketMisses) {
                        Log("⏹️ Auto capture stopping early due to sustained bucket drift.");
                        break;
                    }
                }
            }

            if (goodFrames >= minFrames) {
                Log($"✅ Auto dark capture complete with {goodFrames} accepted frame(s) for bucket {targetBucket}°C ({lifetimeAccepted} accepted in total this run across bucket(s); raws saved).");
            } else {
                Log($"⚠️ Auto dark capture stopped after {attempts} attempt(s) with {goodFrames} accepted for the final bucket {targetBucket}°C (need {minFrames}+ there to stack that group). {lifetimeAccepted} frame(s) accepted in total this run across bucket(s) — earlier buckets have their own partial sets on disk. Run again when temperature is stable.");
            }
        }

        private static void ReportAutoDarkCaptureProgress(IProgress<ApplicationStatus>? progress, int accepted, int target) {
            progress?.Report(new ApplicationStatus {
                Source = "SeeDark",
                Status = $"Auto darks {accepted}/{target}",
                Progress = accepted,
                MaxProgress = Math.Max(1, target),
                ProgressType = ApplicationStatus.StatusProgressType.ValueOfMaxValue,
            });
        }

        private bool NeedsDarks() {
            double temp = GetSensorTempFromMediator();
            Log($"⚙️ Execution mode: {ExecutionMode}");
            if (double.IsNaN(temp)) {
                Log("⚠️ Camera temperature unavailable — darks needed!");
                return true;
            }

            var scopeId = _plugin.GetScopeId();
            if (string.IsNullOrEmpty(scopeId)) {
                Log("⚠️ Scope ID unavailable (camera not connected) — darks needed!");
                return true;
            }

            int bucketStepC = Math.Max(1, _plugin.Settings.TempBucketSize);
            int bucket = TemperatureBucketing.ToBucket(temp, bucketStepC);
            double lead = Math.Max(0.0, _plugin.Settings.PreBucketLeadC);
            Log($"🌡️ Sensor temp {temp:F1}°C → bucket {bucket}°C ({bucketStepC}°C steps), gain {Gain}, scope {scopeId}, target exposure {TargetExposure}s, pre-range lead {lead:F1}°C");

            var masters = ScanMasterFolder();
            var cutoff = DateTime.Now.AddDays(-_plugin.Settings.MaxAgeDays);
            bool needsDarks;
            if (masters.Length == 0) {
                Log("🌑 No masters found in master library folder — darks needed!");
                needsDarks = true;
            } else {
                needsDarks = LacksAcceptableMaster(bucket, masters, cutoff, scopeId);
                Log(needsDarks ? "🌑 No matching dark found — darks needed!" : "✅ Matching dark exists — skipping");
            }
            if (!needsDarks) return false;

            double halfStep = bucketStepC / 2.0;
            double startThreshold = (bucket - halfStep) - lead;
            // Cap start temperature below nominal bucket °C so a warming sensor has headroom in-band
            // (restores the historical ~0.5°C margin; upper band edge alone allows 24.9°C for a "24°C" bucket).
            const double startBelowNominalC = 0.5;
            double endThresholdExclusive = Math.Min(bucket + halfStep, bucket - startBelowNominalC);
            bool inWindow = temp >= startThreshold && temp < endThresholdExclusive;
            int nextWarmerBucket = bucket + bucketStepC;
            bool autoBypassHighInBand = !inWindow &&
                ExecutionMode == DarkExecutionMode.Auto &&
                temp >= startThreshold &&
                temp < bucket + halfStep &&
                LacksAcceptableMaster(nextWarmerBucket, masters, cutoff, scopeId);

            if (!inWindow && !autoBypassHighInBand) {
                Log($"⏳ Missing dark for {bucket}°C bucket, but sensor {temp:F1}°C outside start window [{startThreshold:F1},{endThresholdExclusive:F1})°C (need temp < {bucket - startBelowNominalC:F1}°C) — waiting");
                return false;
            }

            if (autoBypassHighInBand) {
                Log($"🌑 Missing dark for {bucket}°C bucket; sensor {temp:F1}°C is high in-band but bucket {nextWarmerBucket}°C also has no master — starting Auto capture (may retarget upward).");
                return true;
            }

            Log($"🌑 Missing dark for {bucket}°C bucket and sensor {temp:F1}°C is inside start window [{startThreshold:F1},{endThresholdExclusive:F1})°C — darks needed!");
            return true;
        }

        /// <summary>True when no master in the library matches this bucket, exposure, gain, scope, and age cutoff.</summary>
        private bool LacksAcceptableMaster(int bucket, MasterRecord[] masters, DateTime masterCutoff, string scopeId) {
            if (masters.Length == 0)
                return true;
            if (string.IsNullOrEmpty(scopeId))
                return true;
            return !masters.Any(r =>
                r.Temp == bucket &&
                Math.Abs(r.Exposure - TargetExposure) < 0.5 &&
                r.Gain == Gain &&
                r.Scope == scopeId &&
                r.DateCreated >= masterCutoff);
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
                    int    bucketStepC = Math.Max(1, _plugin.Settings.TempBucketSize);
                    int    masterTemp  = TemperatureBucketing.ToBucket(rawTemp, bucketStepC);
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

        private void Log(string message, bool discordVerboseOnly = false) {
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(_logFilePath)!);
                File.AppendAllText(_logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            } catch { }
            var mirrorDiscord = discordVerboseOnly ? _plugin.DiscordVerbosePerFrame : true;
            if (mirrorDiscord)
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
