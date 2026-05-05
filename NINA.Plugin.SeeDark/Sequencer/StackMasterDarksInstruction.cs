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
    [ExportMetadata("Name", "Stack SeeDark Master Darks")]
    [ExportMetadata("Description", "Median-stacks raw dark frames into master darks")]
    [ExportMetadata("Icon", "SeeDark_Icon")]
    [ExportMetadata("Category", "SeeDark")]
    public class StackMasterDarksInstruction : SequenceItem {

        private readonly SeeDarkPlugin _plugin;
        private readonly string _logFilePath;

        [ImportingConstructor]
        public StackMasterDarksInstruction(SeeDarkPlugin plugin) {
            _plugin = plugin;
            Name = "Stack SeeDark Master Darks";
            if (System.Windows.Application.Current?.Resources["SeeDark_Icon"] is GeometryGroup icon)
                Icon = icon;
            _logFilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NINA", "SeeDark", $"stack_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log");
        }

        private StackMasterDarksInstruction(StackMasterDarksInstruction cloneMe) {
            _plugin = cloneMe._plugin;
            _logFilePath = cloneMe._logFilePath;
            Name = "Stack SeeDark Master Darks";
            Icon = cloneMe.Icon;
        }

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            await Task.Run(() => RunStack(token), token);
        }

        public override object Clone() => new StackMasterDarksInstruction(this);

        private void RunStack(CancellationToken token) {
            var rawFolder    = _plugin.Settings.RawDarksFolder;
            var masterFolder = _plugin.Settings.MasterLibraryFolder;

            if (string.IsNullOrWhiteSpace(rawFolder) || !Directory.Exists(rawFolder)) {
                Log("❌ Raw darks folder not configured or missing — aborting"); return;
            }
            if (string.IsNullOrWhiteSpace(masterFolder)) {
                Log("❌ Master library folder not configured — aborting"); return;
            }
            Directory.CreateDirectory(masterFolder);

            Log($"🔭 Scanning {rawFolder}");
            Log($"🔭 Masters → {masterFolder}");
            var allFiles = Directory.GetFiles(rawFolder, "*.fit*", SearchOption.AllDirectories);
            Log($"🔭 Found {allFiles.Length} FITS file(s)");

            var frameInfos = new List<FrameInfo>();
            foreach (var path in allFiles) {
                token.ThrowIfCancellationRequested();
                var info = ReadFrameInfo(path);
                if (info == null) continue;
                if (!string.Equals(info.Filter, "DARK", StringComparison.OrdinalIgnoreCase)) continue;
                frameInfos.Add(info);
            }

            int stackBs = Math.Clamp(_plugin.Settings.StackTolerance, 1, 2);
            var uniqueKeys = frameInfos
                .Select(f => (f.TempBucket, f.Exposure, f.Gain, f.ScopeId))
                .Distinct()
                .ToList();
            Log($"🔭 {frameInfos.Count} DARK frame(s) across {uniqueKeys.Count} bucket key(s)");

            foreach (var key in uniqueKeys) {
                token.ThrowIfCancellationRequested();
                var frames = frameInfos
                    .Where(f => Math.Abs(f.TempBucket - key.TempBucket) <= stackBs &&
                                Math.Abs(f.Exposure - key.Exposure) < 0.5 &&
                                f.Gain == key.Gain &&
                                f.ScopeId == key.ScopeId)
                    .ToList();
                Log($"🔭 Group {key.TempBucket}°C ±{stackBs}°C / {key.Exposure:F0}s / gain {key.Gain} / {key.ScopeId}: {frames.Count} frame(s) (window {key.TempBucket - stackBs}–{key.TempBucket + stackBs}°C)");

                if (frames.Count < _plugin.Settings.MinFrameCount) {
                    Log($"⏭️ Skipped — need {_plugin.Settings.MinFrameCount}, have {frames.Count}");
                    continue;
                }

                var pixelArrays = new List<float[]>(frames.Count);
                int width = 0, height = 0;
                foreach (var frame in frames) {
                    token.ThrowIfCancellationRequested();
                    var pixels = LoadPixels(frame.Path, out int w, out int h);
                    if (pixels == null) { Log($"⚠️ Skipped unreadable frame: {frame.Path}"); continue; }
                    if (width == 0) { width = w; height = h; }
                    else if (w != width || h != height) { Log($"⚠️ Skipped mismatched frame: {frame.Path}"); continue; }
                    pixelArrays.Add(pixels);
                }

                if (pixelArrays.Count < _plugin.Settings.MinFrameCount) {
                    Log($"⏭️ Only {pixelArrays.Count} frame(s) loaded — skipping group");
                    continue;
                }

                Log($"🔧 Stacking {pixelArrays.Count} frames ({width}×{height})...");
                var median = ComputeMedian(pixelArrays);

                var prefix = $"master_dark_{key.Exposure:F0}s_{key.TempBucket}c_{key.ScopeId}_";
                foreach (var old in Directory.GetFiles(masterFolder, prefix + "*.fit*")) {
                    File.Delete(old);
                    Log($"  Removed superseded: {Path.GetFileName(old)}");
                }

                var ts          = DateTime.Now.ToString("yyyyMMddHHmmss");
                var sessionDate = frames.Max(f => f.SessionDate);
                var extraHeaders = new Dictionary<string, object> {
                    ["EXPTIME"]  = key.Exposure,
                    ["CCD-TEMP"] = (double)key.TempBucket,
                    ["INSTRUME"] = key.ScopeId,
                    ["GAIN"]     = (long)key.Gain,
                    ["DATE-OBS"] = sessionDate.ToString("yyyy-MM-dd"),
                    ["IMAGETYP"] = "DARK",
                    ["STACKCNT"] = (long)pixelArrays.Count,
                };

                var sirilPath    = Path.Combine(masterFolder, $"{prefix}SIRIL_{ts}.fit");
                var ninalivePath = Path.Combine(masterFolder, $"{prefix}NINALIVE_{ts}.fit");
                WriteFitsFloat(sirilPath, median, width, height, extraHeaders);
                Log($"💾 Written SIRIL: {Path.GetFileName(sirilPath)}");
                WriteFitsUInt16(ninalivePath, median, width, height, extraHeaders);
                Log($"💾 Written NINALIVE: {Path.GetFileName(ninalivePath)}");
            }

            Log("✅ Stack Master Darks complete");
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

                var date = DateTime.Parse(dateStr ?? DateTime.Now.ToString(), CultureInfo.InvariantCulture);
                if (date.Hour < 12) date = date.AddDays(-1);

                double exposure = double.Parse(exptimeStr, CultureInfo.InvariantCulture);
                double temp     = double.Parse(tempStr,    CultureInfo.InvariantCulture);
                int    bs       = 2;
                int    bucket   = (int)(Math.Floor(temp / bs) * bs);
                var    parts    = instrume.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                string scopeId  = parts.Length >= 2 ? parts[1] : instrume;
                int    gain     = int.TryParse(gainStr, out var g) ? g : 0;

                return new FrameInfo(path, filter?.Trim() ?? "", date.Date, exposure, bucket, gain, scopeId);
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
            _ = _plugin.SendDiscordAsync(msg);
        }

        private record FrameInfo(
            string Path, string Filter, DateTime SessionDate,
            double Exposure, int TempBucket, int Gain, string ScopeId);
    }
}
