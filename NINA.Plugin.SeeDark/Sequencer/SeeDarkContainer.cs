using System;
using System.ComponentModel.Composition;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NINA.Core.Model;
using NINA.Sequencer.Container;
using NINA.Sequencer.Container.ExecutionStrategy;
using NINA.Sequencer.SequenceItem;

namespace NINA.Plugin.SeeDark.Sequencer {

    [Export(typeof(ISequenceContainer))]
    [ExportMetadata("Name", "SeeDark")]
    [ExportMetadata("Description", "Takes master darks only when the dark library has a gap at the current sensor temperature")]
    [ExportMetadata("Icon", "SeeDark_Icon")]
    [ExportMetadata("Category", "SeeDark")]
    public class SeeDarkContainer : SequenceContainer {

        private readonly SeeDarkPlugin _plugin;
        private readonly string _logFilePath;

        private string? _cachedCsvPath;
        private DateTime _csvLastWrite = DateTime.MinValue;
        private CsvRow[]? _csvCache;

        [ImportingConstructor]
        public SeeDarkContainer(SeeDarkPlugin plugin) : base(new SequentialStrategy()) {
            _plugin = plugin;
            Name = "SeeDark";
            if (System.Windows.Application.Current?.Resources["SeeDark_Icon"] is System.Windows.Media.GeometryGroup icon)
                Icon = icon;
            _logFilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NINA", "SeeDark", $"seedark_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log");
            Log("SeeDark initialised");
        }

        private SeeDarkContainer(SeeDarkContainer cloneMe) : base(new SequentialStrategy()) {
            _plugin = cloneMe._plugin;
            _logFilePath = cloneMe._logFilePath;
            Name = "SeeDark";
        }

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            if (NeedsDarks())
                await base.Execute(progress, token);
        }

        private bool NeedsDarks() {
            double temp = GetSensorTempFromMediator();
            if (double.IsNaN(temp)) {
                Log("Camera temperature unavailable — taking darks to be safe");
                return true;
            }

            var scopeId = _plugin.GetScopeId();
            if (string.IsNullOrEmpty(scopeId)) {
                Log("Scope ID unavailable (camera not connected) — taking darks to be safe");
                return true;
            }

            int bucket = (int)(Math.Round(temp / 2.0) * 2);
            Log($"Sensor temp {temp:F1}°C → bucket {bucket}°C, gain {_plugin.Settings.Gain}, scope {scopeId}, target exposure {_plugin.Settings.TargetExposure}s");

            var rows = LoadCsv();
            if (rows == null) {
                Log("CSV not configured or unreadable — taking darks");
                return true;
            }

            var cutoff = DateTime.Now.AddDays(-_plugin.Settings.MaxAgeDays);
            bool needsDarks = !rows.Any(r =>
                r.Temp == bucket &&
                Math.Abs(r.Exposure - _plugin.Settings.TargetExposure) < 0.5 &&
                r.Gain == _plugin.Settings.Gain &&
                r.Scope == scopeId &&
                r.DateCreated >= cutoff);

            Log(needsDarks ? "No matching dark found — taking darks" : "Matching dark exists — skipping");
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

        private CsvRow[]? LoadCsv() {
            var path = _plugin.Settings.DarkLibraryCsvPath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return null;
            try {
                var lastWrite = File.GetLastWriteTime(path);
                if (_csvCache != null && path == _cachedCsvPath && lastWrite == _csvLastWrite)
                    return _csvCache;

                _csvCache = File.ReadAllLines(path)
                    .Skip(1)
                    .Select(ParseRow)
                    .Where(r => r != null)
                    .ToArray()!;
                _cachedCsvPath = path;
                _csvLastWrite = lastWrite;
                Log($"CSV loaded: {_csvCache.Length} rows from {path}");
                return _csvCache;
            } catch { return null; }
        }

        private void Log(string message) {
            WriteToLog($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}");
        }

        private void WriteToLog(string line) {
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(_logFilePath)!);
                File.AppendAllText(_logFilePath, line + Environment.NewLine);
            } catch { }
        }

        private static CsvRow? ParseRow(string line) {
            try {
                var parts = line.Split(',');
                if (parts.Length < 6) return null;
                var gainStr = parts[2].Trim();
                int gain = string.IsNullOrEmpty(gainStr)
                    ? 0 : (int)double.Parse(gainStr, CultureInfo.InvariantCulture);
                return new CsvRow(
                    (int)double.Parse(parts[0].Trim(), CultureInfo.InvariantCulture),
                    double.Parse(parts[1].Trim(), CultureInfo.InvariantCulture),
                    gain,
                    parts[4].Trim(),
                    DateTime.Parse(parts[5].Trim(), CultureInfo.InvariantCulture));
            } catch { return null; }
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

        private record CsvRow(int Temp, double Exposure, int Gain, string Scope, DateTime DateCreated);
    }
}
