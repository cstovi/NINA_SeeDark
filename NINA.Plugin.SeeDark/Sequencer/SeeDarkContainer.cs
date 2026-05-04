using System;
using System.ComponentModel.Composition;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
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
        private static readonly HttpClient _http = new();

        private string? _cachedCsvPath;
        private DateTime _csvLastWrite = DateTime.MinValue;
        private CsvRow[]? _csvCache;

        [ImportingConstructor]
        public SeeDarkContainer(SeeDarkPlugin plugin) : base(new SequentialStrategy()) {
            _plugin = plugin;
            Name = "SeeDark";
            if (System.Windows.Application.Current?.Resources["SeeDark_Icon"] is System.Windows.Media.GeometryGroup icon)
                Icon = icon;
        }

        private SeeDarkContainer(SeeDarkContainer cloneMe) : base(new SequentialStrategy()) {
            _plugin = cloneMe._plugin;
            Name = "SeeDark";
        }

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            if (await NeedsDarks(token))
                await base.Execute(progress, token);
        }

        private async Task<bool> NeedsDarks(CancellationToken token) {
            double temp = GetSensorTempFromMediator();
            if (double.IsNaN(temp))
                temp = await GetTempViaAlpaca(token);
            if (double.IsNaN(temp))
                return true; // can't read temp → take darks to be safe

            int bucket = (int)(Math.Round(temp / 2.0) * 2);
            var rows = LoadCsv();
            if (rows == null)
                return true; // no CSV or not configured → take darks

            var cutoff = DateTime.Now.AddDays(-_plugin.Settings.MaxAgeDays);
            return !rows.Any(r =>
                r.Temp == bucket &&
                Math.Abs(r.Exposure - _plugin.Settings.TargetExposure) < 0.5 &&
                r.DateCreated >= cutoff);
        }

        private double GetSensorTempFromMediator() {
            try {
                var info = _plugin.CameraMediator.GetInfo();
                if (info != null && info.Connected && !double.IsNaN(info.Temperature))
                    return info.Temperature;
            } catch { }
            return double.NaN;
        }

        private async Task<double> GetTempViaAlpaca(CancellationToken token) {
            try {
                var url = $"http://localhost:{_plugin.Settings.AlpacaPort}/api/v1/camera/0/ccdtemperature";
                var response = await _http.GetStringAsync(url, token);
                using var doc = JsonDocument.Parse(response);
                if (doc.RootElement.TryGetProperty("Value", out var val))
                    return val.GetDouble();
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
                return _csvCache;
            } catch { return null; }
        }

        private static CsvRow? ParseRow(string line) {
            try {
                var parts = line.Split(',');
                if (parts.Length < 6) return null;
                return new CsvRow(
                    (int)double.Parse(parts[0].Trim(), CultureInfo.InvariantCulture),
                    double.Parse(parts[1].Trim(), CultureInfo.InvariantCulture),
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

        private record CsvRow(int Temp, double Exposure, DateTime DateCreated);
    }
}
