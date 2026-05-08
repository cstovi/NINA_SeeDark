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
using NINA.Image.Interfaces;
using NINA.Sequencer.Container;
using NINA.Sequencer.Container.ExecutionStrategy;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Utility;

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
            _plugin.RefreshRuntimeSettingsFromDisk();
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
            const int maxAttemptsPerSegment = 30;
            const int maxConsecutiveBucketMisses = 3;
            const int maxSameNightOvershootFrames = 60;

            double startTemp = GetSensorTempFromMediator();
            if (double.IsNaN(startTemp)) {
                Log("⚠️ Could not read sensor temperature at start; skipping dark capture.");
                await base.Execute(progress, token);
                return;
            }

            int bucketStepC = Math.Max(1, _plugin.Settings.TempBucketSize);
            int maxNeededFramesPerBucket = Math.Max(1, _plugin.Settings.MaxFrameCount);
            int currentBucket = TemperatureBucketing.ToBucket(startTemp, bucketStepC);
            var masters = ScanMasterFolder();
            var masterCutoff = DateTime.Now.AddDays(-_plugin.Settings.MaxAgeDays);
            var scopeId = _plugin.GetScopeId();
            DateTime sessionDate = ToSessionDate(DateTime.Now);

            int currentTonightCount = CountSameNightRawFrames(currentBucket, TargetExposure, Gain, scopeId, bucketStepC, sessionDate);
            bool enoughCurrentTonight = currentTonightCount >= maxNeededFramesPerBucket;
            bool lackCurrent = masters.Length == 0 || LacksAcceptableMaster(currentBucket, masters, masterCutoff, scopeId);
            bool currentSatisfied = !lackCurrent || enoughCurrentTonight;

            int nextWarmerBucket = currentBucket + bucketStepC;
            int nextWarmerTonightCount = CountSameNightRawFrames(nextWarmerBucket, TargetExposure, Gain, scopeId, bucketStepC, sessionDate);
            bool lackNextWarmer = masters.Length > 0 && LacksAcceptableMaster(nextWarmerBucket, masters, masterCutoff, scopeId);
            bool nextWarmerNeedsCollection = lackNextWarmer && nextWarmerTonightCount < maxNeededFramesPerBucket;
            bool proactiveWarmup = ExecutionMode == DarkExecutionMode.Auto && masters.Length > 0 && currentSatisfied && nextWarmerNeedsCollection;
            int targetBucket = proactiveWarmup ? currentBucket + bucketStepC : currentBucket;

            var darkFilter = _plugin.GetDarkFilter();
            if (darkFilter == null) {
                Log("⚠️ Could not find a DARK filter definition. Configure a DARK filter to continue.");
                return;
            }

            int maxWarmerSteps = Math.Clamp(_plugin.Settings.AutoDarkMaxWarmerBucketSteps, 0, 3);

            if (proactiveWarmup)
                Log($"🌡️ Proactive Auto: warming toward {targetBucket}°C bucket (current {currentBucket}°C is already satisfied by master/raw sufficiency; capturing until temp reaches target band).");
            Log($"🤖 Auto mode starting dark capture: target {targetFrames} accepted per segment, min {minFrames}, max {maxAttemptsPerSegment} attempts per segment, starting bucket {targetBucket}°C, warmer continuation ≤{maxWarmerSteps} band(s) above that bucket.");
            Log("🔁 Retargeting to another bucket (no master there) resets the segment attempt count; cooler buckets always allowed. Warmer retargets beyond your setting are blocked.");

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
            int anchorBucket = targetBucket;
            Log($"📷 Dark capture in progress for {targetBucket}°C bucket ({goodFrames}/{targetFrames} accepted).");

            ReportAutoDarkCaptureProgress(progress, goodFrames, targetFrames);

            while (!token.IsCancellationRequested && attempts < maxAttemptsPerSegment && goodFrames < targetFrames) {
                int targetTonightCount = CountSameNightRawFrames(targetBucket, TargetExposure, Gain, scopeId, bucketStepC, sessionDate);
                bool targetHasBaseSufficiency = targetTonightCount >= maxNeededFramesPerBucket;
                int targetNextWarmerBucket = targetBucket + bucketStepC;
                int targetNextWarmerTonightCount = CountSameNightRawFrames(targetNextWarmerBucket, TargetExposure, Gain, scopeId, bucketStepC, sessionDate);
                bool targetNextWarmerMissingMaster = LacksAcceptableMaster(targetNextWarmerBucket, masters, masterCutoff, scopeId);
                bool targetNextWarmerNeedsCollection = targetNextWarmerMissingMaster && targetNextWarmerTonightCount < maxNeededFramesPerBucket;
                bool allowOvershootForWarmerProgress = targetHasBaseSufficiency
                    && targetTonightCount < maxSameNightOvershootFrames
                    && targetNextWarmerNeedsCollection;
                if (targetHasBaseSufficiency && !allowOvershootForWarmerProgress) {
                    Log($"🛑 Bucket {targetBucket}°C already has enough same-night raws ({targetTonightCount}/{maxNeededFramesPerBucket}); stopping capture for this bucket.");
                    break;
                }
                if (allowOvershootForWarmerProgress) {
                    Log($"🌡️ Bucket {targetBucket}°C reached same-night sufficiency ({targetTonightCount}/{maxNeededFramesPerBucket}); allowing bounded overshoot toward warmer bucket {targetNextWarmerBucket}°C (limit {maxSameNightOvershootFrames}).", discordVerboseOnly: true);
                }

                attempts++;
                var capture = new CaptureSequence {
                    ExposureTime = TargetExposure,
                    Gain = Gain,
                    Offset = -1,
                    ImageType = CaptureSequence.ImageTypes.DARK,
                };

                ReportAutoDarkCaptureProgress(progress, goodFrames, targetFrames);

                IExposureData? exposureData = null;
                try {
                    exposureData = await _plugin.ImagingMediator.CaptureImage(capture, token, progress);
                } catch (Exception ex) {
                    Log($"⚠️ Auto dark capture failed on frame {attempts}: {ex.Message}");
                    break;
                }

                double frameTemp = GetSensorTempFromMediator();
                if (double.IsNaN(frameTemp)) {
                    Log($"⚠️ Frame {attempts}: temperature unavailable; saving and counting frame toward target.");
                    await SaveRawDarkAsync(exposureData, token);
                    goodFrames++;
                    lifetimeAccepted++;
                    ReportAutoDarkCaptureProgress(progress, goodFrames, targetFrames);
                    continue;
                }

                int frameBucket = TemperatureBucketing.ToBucket(frameTemp, bucketStepC);
                if (proactiveWarmup && frameBucket < targetBucket) {
                    consecutiveBucketMisses = 0;
                    if (LacksAcceptableMaster(frameBucket, masters, masterCutoff, scopeId)) {
                        await SaveRawDarkAsync(exposureData, token);
                        Log($"📸 Frame {attempts}: temp {frameTemp:F1}°C bucket {frameBucket}°C — warmup frame saved (bucket {frameBucket}°C has no master); warming toward {targetBucket}°C", discordVerboseOnly: true);
                    } else {
                        Log($"📸 Frame {attempts}: temp {frameTemp:F1}°C bucket {frameBucket}°C — warming toward {targetBucket}°C (bucket {frameBucket}°C already has a master; frame discarded)", discordVerboseOnly: true);
                    }
                    ReportAutoDarkCaptureProgress(progress, goodFrames, targetFrames);
                    continue;
                }
                if (frameBucket == targetBucket) {
                    await SaveRawDarkAsync(exposureData, token);
                    goodFrames++;
                    lifetimeAccepted++;
                    consecutiveBucketMisses = 0;
                    Log($"📸 Frame {attempts}: temp {frameTemp:F1}°C bucket {frameBucket}°C (target) — accepted ({goodFrames}/{targetFrames})", discordVerboseOnly: true);
                    if (goodFrames % 5 == 0 || goodFrames == targetFrames)
                        Log($"📷 Dark capture in progress for {targetBucket}°C bucket ({goodFrames}/{targetFrames} accepted).");
                    ReportAutoDarkCaptureProgress(progress, goodFrames, targetFrames);
                } else if (LacksAcceptableMaster(frameBucket, masters, masterCutoff, scopeId)) {
                    if (!MayRetargetToMissingMasterBucket(frameBucket, anchorBucket, bucketStepC, maxWarmerSteps)) {
                        consecutiveBucketMisses++;
                        Log($"📸 Frame {attempts}: temp {frameTemp:F1}°C bucket {frameBucket}°C — warmer than starting bucket {anchorBucket}°C beyond allowed +{maxWarmerSteps} band(s); not retargeting (drift {consecutiveBucketMisses}/{maxConsecutiveBucketMisses}).", discordVerboseOnly: true);
                        ReportAutoDarkCaptureProgress(progress, goodFrames, targetFrames);
                        if (consecutiveBucketMisses >= maxConsecutiveBucketMisses) {
                            Log($"⏹️ Auto capture stopping: sustained drift with warmer bucket {frameBucket}°C blocked by warmer-step limit (anchor {anchorBucket}°C, max +{maxWarmerSteps}).");
                            break;
                        }
                    } else {
                        await SaveRawDarkAsync(exposureData, token);
                        int previousTarget = targetBucket;
                        targetBucket = frameBucket;
                        goodFrames = 1;
                        lifetimeAccepted++;
                        consecutiveBucketMisses = 0;
                        Log($"📸 Frame {attempts}: temp {frameTemp:F1}°C bucket {frameBucket}°C — drifted out of {previousTarget}°C target band; bucket {frameBucket}°C also has no acceptable master in the library — retargeting here ({goodFrames}/{targetFrames}); segment attempts reset to 0.", discordVerboseOnly: true);
                        Log($"📷 Dark capture in progress for {targetBucket}°C bucket ({goodFrames}/{targetFrames} accepted).");
                        attempts = 0;
                        ReportAutoDarkCaptureProgress(progress, goodFrames, targetFrames);
                    }
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

            if (!token.IsCancellationRequested && goodFrames < targetFrames && attempts >= maxAttemptsPerSegment) {
                Log($"⚠️ Auto dark capture hit segment attempt limit ({maxAttemptsPerSegment}) for bucket {targetBucket}°C with {goodFrames}/{targetFrames} accepted — run again if needed.");
            }

            if (goodFrames >= targetFrames) {
                Log($"✅ Auto dark capture complete with {goodFrames} accepted frame(s) for bucket {targetBucket}°C ({lifetimeAccepted} accepted in total this run across bucket(s); raws saved).");
            } else if (goodFrames >= minFrames) {
                Log($"✅ Auto dark capture stopped with {goodFrames} accepted for bucket {targetBucket}°C (below target {targetFrames} but ≥{minFrames} to stack; {lifetimeAccepted} accepted in total across bucket(s)).");
            } else {
                Log($"⚠️ Auto dark capture stopped after {attempts} attempt(s) in the final segment with {goodFrames} accepted for bucket {targetBucket}°C (need {minFrames}+ there to stack that group). {lifetimeAccepted} frame(s) accepted in total this run across bucket(s) — earlier buckets have their own partial sets on disk. Run again when temperature is stable.");
            }
        }

        /// <summary>
        /// Cooler buckets than <paramref name="anchorBucket"/> always allowed; warmer buckets allowed only within <paramref name="maxWarmerSteps"/> discrete bands above anchor.
        /// </summary>
        private static bool MayRetargetToMissingMasterBucket(int frameBucket, int anchorBucket, int bucketStepC, int maxWarmerSteps) {
            if (frameBucket <= anchorBucket) return true;
            int step = Math.Max(1, bucketStepC);
            int warmerSteps = (frameBucket - anchorBucket) / step;
            return warmerSteps <= maxWarmerSteps;
        }

        private static void ReportAutoDarkCaptureProgress(IProgress<ApplicationStatus>? progress, int accepted, int target) {
            progress?.Report(new ApplicationStatus {
                Source = "SeeDark",
                Status = "Auto darks",
                Progress = accepted,
                MaxProgress = Math.Max(1, target),
                ProgressType = ApplicationStatus.StatusProgressType.ValueOfMaxValue,
            });
        }

        private bool NeedsDarks() {
            double temp = GetSensorTempFromMediator();
            Log($"⚙️ Execution mode: {ExecutionMode}", fileOnly: ExecutionMode != DarkExecutionMode.Manual);
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
            DateTime sessionDate = ToSessionDate(DateTime.Now);
            int maxNeededFramesPerBucket = Math.Max(1, _plugin.Settings.MaxFrameCount);
            int currentTonightCount = CountSameNightRawFrames(bucket, TargetExposure, Gain, scopeId, bucketStepC, sessionDate);
            bool enoughCurrentTonight = currentTonightCount >= maxNeededFramesPerBucket;
            bool lackCurrent = masters.Length == 0 || LacksAcceptableMaster(bucket, masters, cutoff, scopeId);
            bool currentSatisfied = !lackCurrent || enoughCurrentTonight;
            int nextWarmerBucket = bucket + bucketStepC;
            int nextWarmerTonightCount = CountSameNightRawFrames(nextWarmerBucket, TargetExposure, Gain, scopeId, bucketStepC, sessionDate);
            bool lackNextWarmer = masters.Length > 0 && LacksAcceptableMaster(nextWarmerBucket, masters, cutoff, scopeId);
            bool nextWarmerNeedsCollection = lackNextWarmer && nextWarmerTonightCount < maxNeededFramesPerBucket;
            bool proactiveAutoNext = ExecutionMode == DarkExecutionMode.Auto && masters.Length > 0 && currentSatisfied && nextWarmerNeedsCollection;

            bool needsDarks;
            if (masters.Length == 0) {
                Log("🌑 No masters found in master library folder — darks needed!");
                needsDarks = true;
            } else if (proactiveAutoNext) {
                Log($"🌑 Proactive Auto: have master for {bucket}°C bucket but not for next warmer {nextWarmerBucket}°C — starting capture early before temp reaches next band.");
                needsDarks = true;
            } else {
                if (lackCurrent && enoughCurrentTonight) {
                    Log($"✅ Missing master for {bucket}°C but enough same-night raws already cached ({currentTonightCount}/{maxNeededFramesPerBucket}) — skipping this run.");
                }
                needsDarks = !currentSatisfied;
                if (needsDarks) {
                    Log("🌑 No matching dark found — darks needed!");
                } else if (!lackCurrent) {
                    Log($"✅ Matching dark exists — skipping ({bucket}°C ✓, {nextWarmerBucket}°C ✓)");
                } else {
                    Log($"✅ Same-night raw sufficiency reached for {bucket}°C ({currentTonightCount}/{maxNeededFramesPerBucket}) — skipping additional capture this run.");
                }
            }
            if (!needsDarks) return false;

            if (proactiveAutoNext)
                return true;

            double halfStep = bucketStepC / 2.0;
            double startThreshold = (bucket - halfStep) - lead;
            // Cap start temperature below nominal bucket °C so a warming sensor has headroom in-band
            // (restores the historical ~0.5°C margin; upper band edge alone allows 24.9°C for a "24°C" bucket).
            const double startBelowNominalC = 0.5;
            double endThresholdExclusive = Math.Min(bucket + halfStep, bucket - startBelowNominalC);
            bool inWindow = temp >= startThreshold && temp < endThresholdExclusive;
            bool autoBypassHighInBand = !inWindow &&
                ExecutionMode == DarkExecutionMode.Auto &&
                temp >= startThreshold &&
                temp < bucket + halfStep &&
                LacksAcceptableMaster(nextWarmerBucket, masters, cutoff, scopeId);

            if (!inWindow && !autoBypassHighInBand) {
                Log($"⏳ Missing dark for {bucket}°C bucket, but sensor {temp:F1}°C outside start window [{startThreshold:F1},{endThresholdExclusive:F1})°C (need temp < {bucket - startBelowNominalC:F1}°C) — skipping this run");
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

        private int CountSameNightRawFrames(
            int bucket,
            double exposure,
            int gain,
            string scopeId,
            int bucketStepC,
            DateTime sessionDate) {
            var rawFolder = _plugin.GetConfiguredRawDarksFolder();
            if (string.IsNullOrWhiteSpace(rawFolder) || !Directory.Exists(rawFolder))
                return 0;

            string archiveFolder = Path.Combine(rawFolder, "_archived");
            int count = 0;
            foreach (var path in Directory.GetFiles(rawFolder, "*.fit*", SearchOption.AllDirectories)) {
                if (IsUnderDirectory(path, archiveFolder))
                    continue;
                if (!TryReadRawFrameInfo(path, bucketStepC, out var info))
                    continue;
                if (!string.IsNullOrWhiteSpace(info.ScopeId) &&
                    !string.IsNullOrWhiteSpace(scopeId) &&
                    !string.Equals(info.ScopeId, scopeId, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (info.TempBucket != bucket)
                    continue;
                if (Math.Abs(info.Exposure - exposure) >= 0.5)
                    continue;
                if (info.Gain != gain)
                    continue;
                if (info.SessionDate.Date != sessionDate.Date)
                    continue;
                count++;
            }
            return count;
        }

        private static bool TryReadRawFrameInfo(string path, int bucketStepC, out RawFrameInfo info) {
            info = default;
            var h = FitsHeaderReader.ReadHeaders(path);
            if (h == null)
                return false;

            h.TryGetValue("DATE-LOC", out var dateStr);
            if (string.IsNullOrWhiteSpace(dateStr))
                h.TryGetValue("DATE-OBS", out dateStr);
            h.TryGetValue("EXPTIME", out var exptimeStr);
            if (string.IsNullOrWhiteSpace(exptimeStr))
                h.TryGetValue("EXPOSURE", out exptimeStr);
            h.TryGetValue("CCD-TEMP", out var tempStr);
            if (string.IsNullOrWhiteSpace(tempStr))
                h.TryGetValue("SET-TEMP", out tempStr);
            h.TryGetValue("INSTRUME", out var instrume);
            h.TryGetValue("GAIN", out var gainStr);

            if (string.IsNullOrWhiteSpace(dateStr) ||
                string.IsNullOrWhiteSpace(exptimeStr) ||
                string.IsNullOrWhiteSpace(tempStr) ||
                string.IsNullOrWhiteSpace(instrume))
                return false;

            if (!DateTime.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var date))
                return false;

            if (!double.TryParse(exptimeStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var exposure))
                return false;
            if (!double.TryParse(tempStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var temp))
                return false;
            int gain = int.TryParse(gainStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var g) ? g : 0;

            var parts = instrume.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string scopeId = parts.Length >= 2 ? parts[1] : instrume;
            int bucket = TemperatureBucketing.ToBucket(temp, Math.Max(1, bucketStepC));
            info = new RawFrameInfo(exposure, bucket, gain, scopeId, ToSessionDate(date));
            return true;
        }

        private static DateTime ToSessionDate(DateTime date) {
            if (date.Hour < 12) date = date.AddDays(-1);
            return date.Date;
        }

        private static bool IsUnderDirectory(string filePath, string directoryPath) {
            if (string.IsNullOrWhiteSpace(directoryPath))
                return false;
            var fullFile = Path.GetFullPath(filePath);
            var fullDir = Path.GetFullPath(directoryPath);
            if (!fullDir.EndsWith(Path.DirectorySeparatorChar.ToString()))
                fullDir += Path.DirectorySeparatorChar;
            return fullFile.StartsWith(fullDir, StringComparison.OrdinalIgnoreCase);
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

        private async Task SaveRawDarkAsync(IExposureData? exposureData, CancellationToken token) {
            if (exposureData == null) return;
            try {
                var rawDarksFolder = _plugin.GetConfiguredRawDarksFolder();
                if (string.IsNullOrWhiteSpace(rawDarksFolder)) {
                    Log("⚠️ Raw darks folder is not configured. Set it in SeeDark plugin options to save captures.");
                    return;
                }
                Directory.CreateDirectory(rawDarksFolder);
                var saveStartUtc = DateTime.UtcNow.AddSeconds(-2);
                var imageData = await exposureData.ToImageData(null, token);
                if (imageData == null) return;
                // Route through NINA's save pipeline for file creation, then relocate into configured RawDarksFolder.
                var prepareTask = _plugin.ImagingMediator.PrepareImage(
                    imageData,
                    new NINA.Core.Utility.PrepareImageParameters(null, false),
                    token);
                await _plugin.ImageSaveMediator.Enqueue(imageData, prepareTask, null, token);

                var savedPath = ResolveSavedImagePath(imageData);
                if (string.IsNullOrWhiteSpace(savedPath) || !File.Exists(savedPath)) {
                    savedPath = FindLatestSavedDarkPath(saveStartUtc);
                }
                if (string.IsNullOrWhiteSpace(savedPath) || !File.Exists(savedPath)) {
                    Log("⚠️ Saved dark frame path could not be resolved for relocation.");
                    return;
                }
                if (IsUnderDirectory(savedPath, rawDarksFolder)) {
                    return;
                }

                var fileName = Path.GetFileName(savedPath);
                var destinationPath = Path.Combine(rawDarksFolder, fileName);
                if (File.Exists(destinationPath)) {
                    destinationPath = Path.Combine(
                        rawDarksFolder,
                        $"{Path.GetFileNameWithoutExtension(fileName)}_{DateTime.Now:yyyyMMddHHmmssfff}{Path.GetExtension(fileName)}");
                }
                File.Move(savedPath, destinationPath);
            } catch (Exception ex) {
                Log($"⚠️ Failed to save raw dark frame: {ex.Message}");
            }
        }

        private string? ResolveSavedImagePath(object imageData) {
            try {
                var type = imageData.GetType();
                foreach (var propName in new[] { "FilePath", "Path", "FileName", "Filename" }) {
                    var prop = type.GetProperty(propName);
                    if (prop == null) continue;
                    if (prop.GetValue(imageData) is string path && !string.IsNullOrWhiteSpace(path))
                        return path;
                }
            } catch { }
            return null;
        }

        private string? FindLatestSavedDarkPath(DateTime saveStartUtc) {
            try {
                var basePath = _plugin.ProfileService.ActiveProfile?.ImageFileSettings?.FilePath;
                if (string.IsNullOrWhiteSpace(basePath) || !Directory.Exists(basePath))
                    return null;

                return Directory.GetFiles(basePath, "*.fit*", SearchOption.AllDirectories)
                    .Where(path => File.GetLastWriteTimeUtc(path) >= saveStartUtc)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();
            } catch {
                return null;
            }
        }

        private void Log(string message, bool discordVerboseOnly = false, bool fileOnly = false) {
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(_logFilePath)!);
                File.AppendAllText(_logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            } catch { }
            if (fileOnly) return;
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

        private readonly record struct RawFrameInfo(
            double Exposure,
            int TempBucket,
            int Gain,
            string ScopeId,
            DateTime SessionDate);

        private record MasterRecord(int Temp, double Exposure, int Gain, string Scope, DateTime DateCreated);
    }
}
