using System;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Linq;
using Newtonsoft.Json;
using NINA.Core.Model.Equipment;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Plugin;
using NINA.Plugin.Interfaces;
using NINA.Profile.Interfaces;

namespace NINA.Plugin.SeeDark {

    [Export(typeof(IPluginManifest))]
    [Export]
    public class SeeDarkPlugin : PluginBase, IPluginManifest, INotifyPropertyChanged {
        private bool _isInitializing;
        private bool _isSyncing;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void RaisePropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public ICameraMediator CameraMediator { get; }
        public IImagingMediator ImagingMediator { get; }
        public IFilterWheelMediator FilterWheelMediator { get; }
        public IProfileService ProfileService { get; }
        public SeeDarkSettings Settings { get; }

        [ImportingConstructor]
        public SeeDarkPlugin(
            ICameraMediator cameraMediator,
            IImagingMediator imagingMediator,
            IFilterWheelMediator filterWheelMediator,
            IProfileService profileService) {
            CameraMediator = cameraMediator;
            ImagingMediator = imagingMediator;
            FilterWheelMediator = filterWheelMediator;
            ProfileService = profileService;
            Settings = SeeDarkSettings.Load(profileService.ActiveProfile.ImageFileSettings.FilePath);
            NormalizeSimpleThermalSettings();
            _isInitializing = true;

            TargetExposure        = Settings.TargetExposure;
            MaxAgeDays            = Settings.MaxAgeDays;
            Gain                  = Settings.Gain;
            RawDarksFolder        = Settings.RawDarksFolder;
            MasterLibraryFolder   = Settings.MasterLibraryFolder;
            MinFrameCount         = Settings.MinFrameCount;
            MaxFrameCount         = Settings.MaxFrameCount;
            EnableLifecycleManagement = Settings.EnableLifecycleManagement;
            DeleteArchivedRawsAfterMaxAge = Settings.DeleteArchivedRawsAfterMaxAge;
            DiscordWebhookUrl     = Settings.DiscordWebhookUrl;
            TempBucketSize        = Settings.TempBucketSize;
            StackTolerance        = Settings.StackTolerance;
            PreBucketLeadC        = Settings.PreBucketLeadC;
            _isInitializing = false;
            SyncAndSaveSettings();
        }

        private void SyncAndSaveSettings() {
            if (_isInitializing || _isSyncing) return;
            _isSyncing = true;
            try {
                int normalizedBucketSize = NormalizeBucketSize(_tempBucketSize);
                if (_tempBucketSize != normalizedBucketSize) {
                    _tempBucketSize = normalizedBucketSize;
                    RaisePropertyChanged(nameof(TempBucketSize));
                }
                int normalizedTolerance = DeriveInternalTolerance(_tempBucketSize);
                if (_stackTolerance != normalizedTolerance) {
                    _stackTolerance = normalizedTolerance;
                    RaisePropertyChanged(nameof(StackTolerance));
                }
                int normalizedPreLead = DeriveInternalPreBucketLeadC();
                if (_preBucketLeadC != normalizedPreLead) {
                    _preBucketLeadC = normalizedPreLead;
                    RaisePropertyChanged(nameof(PreBucketLeadC));
                }
                int normalizedMin = DeriveInternalMinFrameCount();
                if (_minFrameCount != normalizedMin) {
                    _minFrameCount = normalizedMin;
                    RaisePropertyChanged(nameof(MinFrameCount));
                }
                int normalizedMax = DeriveInternalMaxFrameCount();
                if (_maxFrameCount != normalizedMax) {
                    _maxFrameCount = normalizedMax;
                    RaisePropertyChanged(nameof(MaxFrameCount));
                }
                if (!_enableLifecycleManagement && _deleteArchivedRawsAfterMaxAge) {
                    _deleteArchivedRawsAfterMaxAge = false;
                    RaisePropertyChanged(nameof(DeleteArchivedRawsAfterMaxAge));
                }

                Settings.TargetExposure      = _targetExposure;
                Settings.MaxAgeDays          = _maxAgeDays;
                Settings.Gain                = _gain;
                Settings.RawDarksFolder      = _rawDarksFolder;
                Settings.MasterLibraryFolder = _masterLibraryFolder;
                Settings.MinFrameCount       = _minFrameCount;
                Settings.MaxFrameCount       = _maxFrameCount;
                Settings.EnableLifecycleManagement = _enableLifecycleManagement;
                Settings.DeleteArchivedRawsAfterMaxAge = _deleteArchivedRawsAfterMaxAge;
                Settings.DiscordWebhookUrl   = _discordWebhookUrl;
                Settings.TempBucketSize      = _tempBucketSize;
                Settings.StackTolerance      = _stackTolerance;
                Settings.PreBucketLeadC      = _preBucketLeadC;
                Settings.Save();
            } finally {
                _isSyncing = false;
            }
        }

        private void NormalizeSimpleThermalSettings() {
            Settings.TempBucketSize = NormalizeBucketSize(Settings.TempBucketSize);
            Settings.StackTolerance = DeriveInternalTolerance(Settings.TempBucketSize);
            Settings.PreBucketLeadC = DeriveInternalPreBucketLeadC();
            Settings.MinFrameCount  = DeriveInternalMinFrameCount();
            Settings.MaxFrameCount  = DeriveInternalMaxFrameCount();
        }

        private static int NormalizeBucketSize(int value) => value == 3 ? 3 : 2;
        private static int DeriveInternalTolerance(int bucketSize) => bucketSize;
        private static int DeriveInternalPreBucketLeadC() => 2;
        private static int DeriveInternalMinFrameCount() => 20;
        private static int DeriveInternalMaxFrameCount() => 50;

        public async Task SendDiscordAsync(string msg) {
            var url = DiscordWebhookUrl;
            if (string.IsNullOrWhiteSpace(url)) return;
            try {
                using var http = new HttpClient();
                await http.PostAsync(url,
                    new StringContent(
                        $"{{\"content\":{JsonConvert.ToString(msg)}}}",
                        Encoding.UTF8, "application/json"));
            } catch { }
        }

        // Returns the scope ID token from the camera driver name (second whitespace token).
        // Matches the INSTRUME FITS header value written by NINA.
        // Returns "" if the camera is not connected.
        public string GetScopeId() {
            try {
                var info = CameraMediator.GetInfo();
                if (info == null || !info.Connected) return "";
                var parts = info.Name.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
                return parts.Length >= 2 ? parts[1] : info.Name;
            } catch { return ""; }
        }

        public FilterInfo? GetDarkFilter() {
            try {
                var filters = ProfileService.ActiveProfile.FilterWheelSettings.FilterWheelFilters;
                if (filters == null) return null;
                var exact = filters.FirstOrDefault(f => string.Equals(f.Name, "DARK", StringComparison.OrdinalIgnoreCase));
                if (exact != null) return exact;
                return filters.FirstOrDefault(f => f.Name != null && f.Name.IndexOf("DARK", StringComparison.OrdinalIgnoreCase) >= 0);
            } catch {
                return null;
            }
        }

        private double _targetExposure = 20.0;
        public double TargetExposure {
            get => _targetExposure;
            set { _targetExposure = value; RaisePropertyChanged(); SyncAndSaveSettings(); }
        }

        private int _maxAgeDays = 180;
        public int MaxAgeDays {
            get => _maxAgeDays;
            set { _maxAgeDays = value; RaisePropertyChanged(); SyncAndSaveSettings(); }
        }

        private int _gain = 200;
        public int Gain {
            get => _gain;
            set { _gain = value; RaisePropertyChanged(); SyncAndSaveSettings(); }
        }

        private string _rawDarksFolder = "";
        public string RawDarksFolder {
            get => _rawDarksFolder;
            set { _rawDarksFolder = value; RaisePropertyChanged(); SyncAndSaveSettings(); }
        }

        private string _masterLibraryFolder = "";
        public string MasterLibraryFolder {
            get => _masterLibraryFolder;
            set { _masterLibraryFolder = value; RaisePropertyChanged(); SyncAndSaveSettings(); }
        }

        private int _minFrameCount = 20;
        public int MinFrameCount {
            get => _minFrameCount;
            set { _minFrameCount = DeriveInternalMinFrameCount(); RaisePropertyChanged(); SyncAndSaveSettings(); }
        }

        private int _maxFrameCount = 50;
        public int MaxFrameCount {
            get => _maxFrameCount;
            set { _maxFrameCount = DeriveInternalMaxFrameCount(); RaisePropertyChanged(); SyncAndSaveSettings(); }
        }

        private bool _enableLifecycleManagement = false;
        public bool EnableLifecycleManagement {
            get => _enableLifecycleManagement;
            set { _enableLifecycleManagement = value; RaisePropertyChanged(); SyncAndSaveSettings(); }
        }

        private bool _deleteArchivedRawsAfterMaxAge = false;
        public bool DeleteArchivedRawsAfterMaxAge {
            get => _deleteArchivedRawsAfterMaxAge;
            set { _deleteArchivedRawsAfterMaxAge = value; RaisePropertyChanged(); SyncAndSaveSettings(); }
        }

        private string _discordWebhookUrl = "";
        public string DiscordWebhookUrl {
            get => _discordWebhookUrl;
            set { _discordWebhookUrl = value; RaisePropertyChanged(); SyncAndSaveSettings(); }
        }

        private int _tempBucketSize = 2;
        public int TempBucketSize {
            get => _tempBucketSize;
            set { _tempBucketSize = NormalizeBucketSize(value); RaisePropertyChanged(); SyncAndSaveSettings(); }
        }

        private int _stackTolerance = 2;
        public int StackTolerance {
            get => _stackTolerance;
            set { _stackTolerance = Math.Clamp(value, 1, 2); RaisePropertyChanged(); SyncAndSaveSettings(); }
        }

        private int _preBucketLeadC = 2;
        public int PreBucketLeadC {
            get => _preBucketLeadC;
            set { _preBucketLeadC = DeriveInternalPreBucketLeadC(); RaisePropertyChanged(); SyncAndSaveSettings(); }
        }
    }
}
