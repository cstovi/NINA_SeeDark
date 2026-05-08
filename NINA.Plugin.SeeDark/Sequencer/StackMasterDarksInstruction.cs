using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using NINA.Core.Model;
using NINA.Sequencer.SequenceItem;

namespace NINA.Plugin.SeeDark.Sequencer {

    [Export(typeof(ISequenceItem))]
    [ExportMetadata("Name", "SeeDark Stack Master Darks")]
    [ExportMetadata("Description", "Median-stacks raw dark frames into master darks")]
    [ExportMetadata("Icon", "SeeDark_Icon")]
    [ExportMetadata("Category", "SeeDark")]
    public class StackMasterDarksInstruction : SequenceItem {
        private readonly SeeDarkPlugin _plugin;
        private readonly string _logFilePath;

        [ImportingConstructor]
        public StackMasterDarksInstruction(SeeDarkPlugin plugin) {
            _plugin = plugin;
            Name = "SeeDark Stack Master Darks";
            if (System.Windows.Application.Current?.Resources["SeeDark_Icon"] is GeometryGroup icon)
                Icon = icon;
            _logFilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NINA", "SeeDark", $"stack_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log");
        }

        private StackMasterDarksInstruction(StackMasterDarksInstruction cloneMe) {
            _plugin = cloneMe._plugin;
            _logFilePath = cloneMe._logFilePath;
            Name = "SeeDark Stack Master Darks";
            Icon = cloneMe.Icon;
        }

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            await Task.Run(() => RunStack(token), token);
        }

        public override object Clone() => new StackMasterDarksInstruction(this);

        private void RunStack(CancellationToken token) {
            var rawFolder    = _plugin.GetNinaDarkRawRootFolder();
            var masterFolder = _plugin.Settings.MasterLibraryFolder;
            var darkPattern = _plugin.GetNinaDarkFilePattern();

            if (string.IsNullOrWhiteSpace(rawFolder)) {
                Log("❌ NINA image file path is not configured — aborting");
                return;
            }
            if (!Directory.Exists(rawFolder)) {
                Log($"❌ NINA image file path does not exist: {rawFolder} — aborting");
                return;
            }
            if (string.IsNullOrWhiteSpace(masterFolder)) {
                Log("❌ Master library folder not configured — aborting"); return;
            }
            Directory.CreateDirectory(masterFolder);
            bool deleteRawsEnabled = _plugin.Settings.DeleteRawsAfterMaxAge;

            Log($"🔭 Scanning {rawFolder}");
            if (!string.IsNullOrWhiteSpace(darkPattern))
                Log($"🔭 NINA DARK pattern: {darkPattern}");
            Log($"🔭 Masters → {masterFolder}");
            Log($"🔭 Raw cleanup: {(deleteRawsEnabled ? "enabled" : "disabled")}");
            var allFiles = Directory.GetFiles(rawFolder, "*.fit*", SearchOption.AllDirectories);
            Log($"🔭 Found {allFiles.Length} active FITS file(s)");

            var activeFrameInfos = new List<FrameInfo>();
            foreach (var path in allFiles) {
                token.ThrowIfCancellationRequested();
                var info = ReadFrameInfo(path);
                if (info == null) continue;
                if (!string.Equals(info.Filter, "DARK", StringComparison.OrdinalIgnoreCase)) continue;
                activeFrameInfos.Add(info);
            }

            int bucketStepC = Math.Max(1, _plugin.Settings.TempBucketSize);
            int minFrameCount = Math.Max(1, _plugin.Settings.MinFrameCount);
            int maxFrameCount = Math.Max(minFrameCount, _plugin.Settings.MaxFrameCount);
            var maxAgeDays = Math.Max(1, _plugin.Settings.MaxAgeDays);
            var masterCutoff = DateTime.Now.AddDays(-maxAgeDays);
            var rawCutoff = DateTime.Now.AddDays(-maxAgeDays);
            var allFrameInfos = activeFrameInfos.ToList();
            var uniqueKeys = allFrameInfos
                .Select(f => (f.TempBucket, f.Exposure, f.Gain, f.ScopeId))
                .Distinct()
                .ToList();
            Log($"🔭 {allFrameInfos.Count} DARK frame(s) across {uniqueKeys.Count} bucket key(s)");

            foreach (var key in uniqueKeys) {
                token.ThrowIfCancellationRequested();
                var matchingMaster = FindNewestMaster(masterFolder, key.TempBucket, key.Exposure, key.Gain, key.ScopeId);
                var validActiveFrames = activeFrameInfos
                    .Where(f => f.TempBucket == key.TempBucket &&
                                Math.Abs(f.Exposure - key.Exposure) < 0.5 &&
                                f.Gain == key.Gain &&
                                f.ScopeId == key.ScopeId &&
                                f.SessionTimestamp >= rawCutoff)
                    .ToList();
                var candidateFrames = validActiveFrames;
                var selectedFrames = candidateFrames
                    .OrderByDescending(f => f.SessionTimestamp)
                    .Take(maxFrameCount)
                    .ToList();
                double lowerBound = key.TempBucket - (bucketStepC / 2.0);
                double upperBound = key.TempBucket + (bucketStepC / 2.0);
                int eligibleCount = validActiveFrames.Count;
                string sourceText = "active";
                bool hasFreshMaster = matchingMaster != null && matchingMaster.DateCreated >= masterCutoff;
                bool hasKnownContributorCount = matchingMaster?.StackCount is > 0;
                bool shouldRebuildForMoreRaws = hasFreshMaster && hasKnownContributorCount && eligibleCount > matchingMaster!.StackCount!.Value;
                if (hasFreshMaster && !shouldRebuildForMoreRaws) {
                    if (!hasKnownContributorCount) {
                        Log($"✅ Existing master is fresh for {key.TempBucket}°C/{key.Exposure:F0}s/gain {key.Gain}/{key.ScopeId} ({matchingMaster!.DateCreated:yyyy-MM-dd}) but STACKCNT is missing/invalid — skipping rebuild (legacy-safe)");
                    } else {
                        Log($"✅ Existing master is fresh for {key.TempBucket}°C/{key.Exposure:F0}s/gain {key.Gain}/{key.ScopeId} ({matchingMaster!.DateCreated:yyyy-MM-dd}) with STACKCNT={matchingMaster!.StackCount}; eligible {sourceText} raws={eligibleCount} — skipping rebuild");
                    }
                    continue;
                }
                if (shouldRebuildForMoreRaws) {
                    Log($"🔄 Existing master is fresh for {key.TempBucket}°C/{key.Exposure:F0}s/gain {key.Gain}/{key.ScopeId} but STACKCNT={matchingMaster!.StackCount} and eligible {sourceText} raws={eligibleCount} (cap {maxFrameCount}) — rebuilding");
                }
                Log($"🔭 Group {key.TempBucket}°C [{lowerBound:F1},{upperBound:F1}) / {key.Exposure:F0}s / gain {key.Gain} / {key.ScopeId}: using {selectedFrames.Count}/{eligibleCount} most recent valid frame(s) from {sourceText}");

                if (selectedFrames.Count < minFrameCount) {
                    string reason = matchingMaster == null
                        ? "missing"
                        : shouldRebuildForMoreRaws
                            ? "more raws available"
                            : "expired";
                    Log($"⚠️ Master {reason} for {key.TempBucket}°C/{key.Exposure:F0}s/gain {key.Gain}/{key.ScopeId}, but only {selectedFrames.Count} valid cached frame(s); need {minFrameCount}. Run a new dark sequence.");
                    continue;
                }

                var pixelArrays = new List<float[]>(selectedFrames.Count);
                var loadedFrames = new List<FrameInfo>(selectedFrames.Count);
                int width = 0, height = 0;
                foreach (var frame in selectedFrames) {
                    token.ThrowIfCancellationRequested();
                    var pixels = LoadPixels(frame.Path, out int w, out int h);
                    if (pixels == null) { Log($"⚠️ Skipped unreadable frame: {frame.Path}"); continue; }
                    if (width == 0) { width = w; height = h; }
                    else if (w != width || h != height) { Log($"⚠️ Skipped mismatched frame: {frame.Path}"); continue; }
                    pixelArrays.Add(pixels);
                    loadedFrames.Add(frame);
                }

                if (pixelArrays.Count < minFrameCount) {
                    Log($"⏭️ Only {pixelArrays.Count} frame(s) loaded — skipping group");
                    continue;
                }

                Log($"🔧 Stacking {pixelArrays.Count} frames ({width}×{height})...");
                var median = ComputeMedian(pixelArrays);
                var frameStats = pixelArrays.Select(ComputeMeanStd).ToList();
                var medianOfMeans = Median(frameStats.Select(s => s.mean));
                var brightnessThreshold = medianOfMeans * 1.10;
                var brightOutliers = frameStats
                    .Select((stats, index) => new { stats.mean, index })
                    .Where(x => x.mean > brightnessThreshold)
                    .Select(x => Path.GetFileName(loadedFrames[x.index].Path))
                    .Take(5)
                    .ToList();
                if (brightOutliers.Count > 0) {
                    Log($"⚠️ Potential light leak: {brightOutliers.Count} unusually bright frame(s) above {brightnessThreshold:F2} mean ADU (showing up to 5)");
                    foreach (var name in brightOutliers) Log($"   • {name}");
                }
                var spatialMetrics = pixelArrays.Select(p => ComputeSpatialLeakMetric(p, width, height)).ToList();
                var baselineCornerToCenterRatio = Median(spatialMetrics.Select(m => m.cornerToCenterRatio));
                int spatialMinFlagCount = Math.Max(3, (int)Math.Ceiling(pixelArrays.Count * 0.10));
                var spatialFlags = spatialMetrics
                    .Select((metric, index) => new { metric, index })
                    .Where(x =>
                        x.metric.cornerToCenterRatio > baselineCornerToCenterRatio * 1.08 &&
                        x.metric.cornerMinusCenter > 10.0)
                    .ToList();
                if (spatialFlags.Count >= spatialMinFlagCount) {
                    var sampleNames = spatialFlags
                        .Take(3)
                        .Select(x => Path.GetFileName(loadedFrames[x.index].Path))
                        .ToList();
                    Log(
                        $"⚠️ Spatial leak check: {spatialFlags.Count}/{pixelArrays.Count} frame(s) show elevated corner glow " +
                        $"(baseline corner/center ratio {baselineCornerToCenterRatio:F3}; trigger >= {baselineCornerToCenterRatio * 1.08:F3}).");
                    if (sampleNames.Count > 0)
                        Log($"⚠️ Spatial leak examples: {string.Join(", ", sampleNames)}");
                }
                var representativeSingleStd = Median(frameStats.Select(s => s.stdDev));
                var masterStats = ComputeMeanStd(median);
                Log($"📊 Noise stats: representative single-frame σ={representativeSingleStd:F2}, master σ={masterStats.stdDev:F2}");
                if (masterStats.stdDev >= representativeSingleStd)
                    Log("⚠️ Master noise is not lower than representative single-frame noise; inspect contributing raws.");
                else
                    Log("✅ Noise reduction check passed (master noise lower than representative single-frame noise).");

                var prefix = $"master_dark_{key.Exposure:F0}s_{key.TempBucket}c_{key.ScopeId}_";
                foreach (var old in Directory.GetFiles(masterFolder, prefix + "*.fit*")) {
                    File.Delete(old);
                    Log($"  Removed superseded: {Path.GetFileName(old)}");
                }

                var ts          = DateTime.Now.ToString("yyyyMMddHHmmss");
                var sessionDate = selectedFrames.Max(f => f.SessionTimestamp).Date;
                var extraHeaders = new Dictionary<string, object> {
                    ["EXPTIME"]  = key.Exposure,
                    ["CCD-TEMP"] = (double)key.TempBucket,
                    ["INSTRUME"] = key.ScopeId,
                    ["GAIN"]     = (long)key.Gain,
                    ["DATE-OBS"] = sessionDate.ToString("yyyy-MM-dd"),
                    ["IMAGETYP"] = "DARK",
                    ["STACKCNT"] = (long)pixelArrays.Count,
                };

                var sirilPath = Path.Combine(masterFolder, $"{prefix}SIRIL_{ts}.fit");
                WriteFitsFloat(sirilPath, median, width, height, extraHeaders);
                Log($"💾 Written SIRIL: {Path.GetFileName(sirilPath)}");
                if (_plugin.Settings.WriteNinaLiveMasters) {
                    var ninalivePath = Path.Combine(masterFolder, $"{prefix}NINALIVE_{ts}.fit");
                    WriteFitsUInt16(ninalivePath, median, width, height, extraHeaders);
                    Log($"💾 Written NINALIVE: {Path.GetFileName(ninalivePath)}");
                }

                string newestUsedDate = selectedFrames.Max(f => f.SessionTimestamp).ToString("yyyy-MM-dd");
                string reasonText = matchingMaster == null
                    ? "missing"
                    : shouldRebuildForMoreRaws
                        ? "more raws available"
                        : "expired";
                Log($"✅ Master Dark {key.TempBucket}°C/{key.Exposure:F0}s/gain {key.Gain}/{key.ScopeId} was {reasonText}. Successfully rebuilt using cached raw frames from {newestUsedDate}.");
            }

            if (deleteRawsEnabled) {
                PurgeRawDarksOlderThan(rawFolder, masterFolder, rawCutoff);
            }

            Log("✅ Stack Master Darks complete");
        }

        private void PurgeRawDarksOlderThan(string rawFolder, string masterFolder, DateTime cutoff) {
            foreach (var path in Directory.GetFiles(rawFolder, "*.fit*", SearchOption.AllDirectories)) {
                if (IsUnderDirectory(path, masterFolder))
                    continue;
                try {
                    var info = ReadFrameInfo(path);
                    if (info == null) continue;
                    if (!string.Equals(info.Filter, "DARK", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (info.SessionTimestamp < cutoff) {
                        File.Delete(path);
                        Log($"🗑️ Deleted raw dark older than age window: {Path.GetFileName(path)}");
                    }
                } catch (Exception ex) {
                    Log($"⚠️ Failed deleting raw dark {Path.GetFileName(path)}: {ex.Message}");
                }
            }
        }

        private static bool IsUnderDirectory(string filePath, string directoryPath) {
            var fullFile = Path.GetFullPath(filePath);
            var fullDir = Path.GetFullPath(directoryPath);
            if (!fullDir.EndsWith(Path.DirectorySeparatorChar.ToString()))
                fullDir += Path.DirectorySeparatorChar;
            return fullFile.StartsWith(fullDir, StringComparison.OrdinalIgnoreCase);
        }

        private MasterInfo? FindNewestMaster(string masterFolder, int bucket, double exposure, int gain, string scopeId) {
            MasterInfo? newest = null;
            foreach (var path in Directory.GetFiles(masterFolder, "*.fit*")) {
                var h = FitsHeaderReader.ReadHeaders(path);
                if (h == null) continue;
                if (!h.TryGetValue("IMAGETYP", out var imagetyp) ||
                    imagetyp.IndexOf("DARK", StringComparison.OrdinalIgnoreCase) < 0) continue;

                h.TryGetValue("CCD-TEMP", out var tempStr);
                h.TryGetValue("EXPTIME", out var expStr);
                h.TryGetValue("GAIN", out var gainStr);
                h.TryGetValue("INSTRUME", out var instrume);
                h.TryGetValue("DATE-OBS", out var dateStr);
                if (string.IsNullOrEmpty(tempStr) || string.IsNullOrEmpty(expStr) || string.IsNullOrEmpty(instrume))
                    continue;

                try {
                    int bucketStepC = Math.Max(1, _plugin.Settings.TempBucketSize);
                    int masterBucket = TemperatureBucketing.ToBucket(double.Parse(tempStr, CultureInfo.InvariantCulture), bucketStepC);
                    double masterExposure = double.Parse(expStr, CultureInfo.InvariantCulture);
                    int masterGain = int.TryParse(gainStr, out var mg) ? mg : 0;
                    var parts = instrume.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    string masterScope = parts.Length >= 2 ? parts[1] : instrume;
                    DateTime created = string.IsNullOrWhiteSpace(dateStr)
                        ? File.GetLastWriteTime(path)
                        : DateTime.Parse(dateStr, CultureInfo.InvariantCulture);
                    int? stackCount = null;
                    if (h.TryGetValue("STACKCNT", out var stackCountStr) && int.TryParse(stackCountStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedStackCount) && parsedStackCount > 0) {
                        stackCount = parsedStackCount;
                    }

                    if (masterBucket != bucket ||
                        Math.Abs(masterExposure - exposure) >= 0.5 ||
                        masterGain != gain ||
                        masterScope != scopeId) continue;

                    if (newest == null || created > newest.DateCreated)
                        newest = new MasterInfo(path, created, stackCount);
                } catch { }
            }
            return newest;
        }

        private FrameInfo? ReadFrameInfo(string path) {
            try {
                var h = FitsHeaderReader.ReadHeaders(path);
                if (h == null) return null;

                h.TryGetValue("FILTER",   out var filter);
                h.TryGetValue("DATE-LOC", out var dateStr);
                if (string.IsNullOrEmpty(dateStr)) h.TryGetValue("DATE-OBS",  out dateStr);
                h.TryGetValue("EXPTIME",  out var exptimeStr);
                if (string.IsNullOrEmpty(exptimeStr)) h.TryGetValue("EXPOSURE", out exptimeStr);
                h.TryGetValue("CCD-TEMP", out var tempStr);
                if (string.IsNullOrEmpty(tempStr))    h.TryGetValue("SET-TEMP", out tempStr);
                h.TryGetValue("INSTRUME", out var instrume);
                h.TryGetValue("GAIN",     out var gainStr);

                if (string.IsNullOrEmpty(exptimeStr) || string.IsNullOrEmpty(tempStr) || string.IsNullOrEmpty(instrume))
                    return null;

                if (string.IsNullOrWhiteSpace(dateStr)) return null;
                if (!DateTime.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var date)) {
                    Log($"⚠️ Skipped frame with invalid DATE-LOC/DATE-OBS: {Path.GetFileName(path)}");
                    return null;
                }
                if (date.Hour < 12) date = date.AddDays(-1);

                double exposure = double.Parse(exptimeStr, CultureInfo.InvariantCulture);
                double temp     = double.Parse(tempStr,    CultureInfo.InvariantCulture);
                int    bucketStepC = Math.Max(1, _plugin.Settings.TempBucketSize);
                int    bucket      = TemperatureBucketing.ToBucket(temp, bucketStepC);
                var    parts    = instrume.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                string scopeId  = parts.Length >= 2 ? parts[1] : instrume;
                int    gain     = int.TryParse(gainStr, out var g) ? g : 0;

                return new FrameInfo(path, filter?.Trim() ?? "", date, exposure, bucket, gain, scopeId);
            } catch { return null; }
        }

        private static float[]? LoadPixels(string path, out int width, out int height) {
            width = 0; height = 0;
            try {
                var h = FitsHeaderReader.ReadHeaders(path, out long dataOffset);
                if (h == null) return null;
                int bitpix = h.TryGetValue("BITPIX", out var bp) ? int.Parse(bp) : 16;
                width  = h.TryGetValue("NAXIS1", out var n1) ? int.Parse(n1) : 0;
                height = h.TryGetValue("NAXIS2", out var n2) ? int.Parse(n2) : 0;
                int count = width * height;
                if (count == 0) return null;

                double bzero  = h.TryGetValue("BZERO",  out var bz) ? double.Parse(bz,  CultureInfo.InvariantCulture) : 0.0;
                double bscale = h.TryGetValue("BSCALE", out var bs) ? double.Parse(bs,  CultureInfo.InvariantCulture) : 1.0;

                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                fs.Seek(dataOffset, SeekOrigin.Begin);
                var pixels = new float[count];

                if (bitpix == 16) {
                    var buf = new byte[count * 2];
                    fs.Read(buf, 0, buf.Length);
                    for (int i = 0; i < count; i++) {
                        short raw = BinaryPrimitives.ReadInt16BigEndian(buf.AsSpan(i * 2));
                        pixels[i] = (float)(raw * bscale + bzero);
                    }
                } else if (bitpix == -32) {
                    var buf = new byte[count * 4];
                    fs.Read(buf, 0, buf.Length);
                    for (int i = 0; i < count; i++)
                        pixels[i] = BinaryPrimitives.ReadSingleBigEndian(buf.AsSpan(i * 4));
                } else {
                    return null;
                }
                return pixels;
            } catch { return null; }
        }

        private static float[] ComputeMedian(List<float[]> arrays) {
            int n   = arrays.Count;
            int len = arrays[0].Length;
            var result = new float[len];
            var buf    = new float[n];
            for (int i = 0; i < len; i++) {
                for (int j = 0; j < n; j++) buf[j] = arrays[j][i];
                Array.Sort(buf);
                result[i] = n % 2 == 1
                    ? buf[n / 2]
                    : (buf[n / 2 - 1] + buf[n / 2]) * 0.5f;
            }
            return result;
        }

        private static double Median(IEnumerable<double> values) {
            var sorted = values.OrderBy(v => v).ToArray();
            if (sorted.Length == 0) return 0.0;
            return sorted.Length % 2 == 1
                ? sorted[sorted.Length / 2]
                : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) * 0.5;
        }

        private static (double mean, double stdDev) ComputeMeanStd(float[] values) {
            if (values.Length == 0) return (0.0, 0.0);
            double sum = 0.0;
            double sumSq = 0.0;
            foreach (var value in values) {
                sum += value;
                sumSq += value * value;
            }
            double mean = sum / values.Length;
            double variance = Math.Max(0.0, (sumSq / values.Length) - (mean * mean));
            return (mean, Math.Sqrt(variance));
        }

        private static (double cornerToCenterRatio, double cornerMinusCenter) ComputeSpatialLeakMetric(float[] pixels, int width, int height) {
            if (pixels.Length == 0 || width <= 0 || height <= 0) return (1.0, 0.0);
            int regionW = Math.Max(16, width / 6);
            int regionH = Math.Max(16, height / 6);
            regionW = Math.Min(regionW, width);
            regionH = Math.Min(regionH, height);

            double tl = RegionMean(pixels, width, 0, 0, regionW, regionH);
            double tr = RegionMean(pixels, width, Math.Max(0, width - regionW), 0, regionW, regionH);
            double bl = RegionMean(pixels, width, 0, Math.Max(0, height - regionH), regionW, regionH);
            double br = RegionMean(pixels, width, Math.Max(0, width - regionW), Math.Max(0, height - regionH), regionW, regionH);
            double cornerMean = (tl + tr + bl + br) * 0.25;

            int centerX = Math.Max(0, (width - regionW) / 2);
            int centerY = Math.Max(0, (height - regionH) / 2);
            double centerMean = RegionMean(pixels, width, centerX, centerY, regionW, regionH);
            if (centerMean <= 0.0) return (1.0, cornerMean - centerMean);

            return (cornerMean / centerMean, cornerMean - centerMean);
        }

        private static double RegionMean(float[] pixels, int width, int startX, int startY, int regionW, int regionH) {
            double sum = 0.0;
            int count = 0;
            for (int y = startY; y < startY + regionH; y++) {
                int rowOffset = y * width;
                for (int x = startX; x < startX + regionW; x++) {
                    sum += pixels[rowOffset + x];
                    count++;
                }
            }
            return count > 0 ? sum / count : 0.0;
        }

        private static void WriteFitsFloat(string path, float[] pixels, int width, int height,
                                           Dictionary<string, object> extra) {
            WriteFits(path, BuildPrimaryCards("-32", width, height, extra), writer => {
                var buf = new byte[pixels.Length * 4];
                for (int i = 0; i < pixels.Length; i++)
                    BinaryPrimitives.WriteSingleBigEndian(buf.AsSpan(i * 4), pixels[i]);
                writer.Write(buf);
            });
        }

        private static void WriteFitsUInt16(string path, float[] pixels, int width, int height,
                                            Dictionary<string, object> extra) {
            WriteFits(path, BuildPrimaryCards("16", width, height, extra, bzeroUint16: true), writer => {
                var buf = new byte[pixels.Length * 2];
                for (int i = 0; i < pixels.Length; i++) {
                    ushort physical = (ushort)Math.Clamp((int)Math.Round(pixels[i]), 0, 65535);
                    BinaryPrimitives.WriteInt16BigEndian(buf.AsSpan(i * 2), (short)(physical - 32768));
                }
                writer.Write(buf);
            });
        }

        private static List<string> BuildPrimaryCards(string bitpix, int width, int height,
                                                      Dictionary<string, object> extra,
                                                      bool bzeroUint16 = false) {
            var cards = new List<string> {
                FitsCardBool("SIMPLE",  true),
                FitsCardNum("BITPIX",   bitpix),
                FitsCardNum("NAXIS",    "2"),
                FitsCardNum("NAXIS1",   width.ToString()),
                FitsCardNum("NAXIS2",   height.ToString()),
            };
            if (bzeroUint16) {
                cards.Add(FitsCardNum("BZERO",  "32768"));
                cards.Add(FitsCardNum("BSCALE", "1"));
            }
            foreach (var kv in extra)
                cards.Add(FitsCardAuto(kv.Key, kv.Value));
            cards.Add("END".PadRight(80));
            return cards;
        }

        private static void WriteFits(string path, List<string> cards, Action<FileStream> writeData) {
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);

            var headerBytes = Encoding.ASCII.GetBytes(string.Concat(cards));
            int rem = headerBytes.Length % 2880;
            if (rem == 0) {
                fs.Write(headerBytes);
            } else {
                var padded = new byte[headerBytes.Length + (2880 - rem)];
                Array.Copy(headerBytes, padded, headerBytes.Length);
                for (int i = headerBytes.Length; i < padded.Length; i++) padded[i] = 0x20;
                fs.Write(padded);
            }

            long dataPosStart = fs.Position;
            writeData(fs);
            int dataRem = (int)((fs.Position - dataPosStart) % 2880);
            if (dataRem > 0) fs.Write(new byte[2880 - dataRem]);
        }

        private static string FitsCardBool(string kw, bool val) {
            kw = kw.PadRight(8)[..8].ToUpper();
            return $"{kw}= {(val ? "T" : "F"),20}".PadRight(80)[..80];
        }

        private static string FitsCardNum(string kw, string numVal) {
            kw = kw.PadRight(8)[..8].ToUpper();
            return $"{kw}= {numVal,20}".PadRight(80)[..80];
        }

        private static string FitsCardStr(string kw, string strVal) {
            kw = kw.PadRight(8)[..8].ToUpper();
            var quoted = $"'{strVal.PadRight(8)}'";
            return $"{kw}= {quoted,-20}".PadRight(80)[..80];
        }

        private static string FitsCardAuto(string kw, object val) => val switch {
            string s => FitsCardStr(kw, s),
            double d => FitsCardNum(kw, d.ToString("G", CultureInfo.InvariantCulture)),
            long l   => FitsCardNum(kw, l.ToString()),
            int iv   => FitsCardNum(kw, iv.ToString()),
            _        => FitsCardNum(kw, Convert.ToString(val, CultureInfo.InvariantCulture) ?? "")
        };

        private void Log(string msg) {
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(_logFilePath)!);
                File.AppendAllText(_logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {msg}{Environment.NewLine}");
            } catch { }
            if (ShouldSendToDiscord(msg))
                _ = _plugin.SendDiscordAsync(msg);
        }

        private static bool ShouldSendToDiscord(string msg) {
            if (string.IsNullOrWhiteSpace(msg)) return false;
            if (msg.StartsWith("❌", StringComparison.Ordinal)) return true;
            if (msg.StartsWith("⚠️", StringComparison.Ordinal)) return true;
            if (!msg.StartsWith("✅", StringComparison.Ordinal)) return false;
            return msg.Contains("Successfully rebuilt", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("Stack Master Darks complete", StringComparison.OrdinalIgnoreCase);
        }

        private record FrameInfo(
            string Path, string Filter, DateTime SessionTimestamp,
            double Exposure, int TempBucket, int Gain, string ScopeId);

        private record MasterInfo(string Path, DateTime DateCreated, int? StackCount);
    }
}
