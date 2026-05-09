using System;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.IO;
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
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.Plugin.SeeDark.Sequencer;

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
        public IImageSaveMediator ImageSaveMediator { get; }
        public IProfileService ProfileService { get; }
        public SeeDarkSettings Settings { get; }

        [ImportingConstructor]
        public SeeDarkPlugin(
            ICameraMediator cameraMediator,
            IImagingMediator imagingMediator,
            IFilterWheelMediator filterWheelMediator,
            IImageSaveMediator imageSaveMediator,
            IProfileService profileService) {
            CameraMediator = cameraMediator;
            ImagingMediator = imagingMediator;
            FilterWheelMediator = filterWheelMediator;
            ImageSaveMediator = imageSaveMediator;
            ProfileService = profileService;
            Settings = SeeDarkSettings.Load(profileService.ActiveProfile.ImageFileSettings.FilePath);
            NormalizeSimpleThermalSettings();
            _isInitializing = true;

            TargetExposure        = Settings.TargetExposure;
            MaxAgeDays            = Settings.MaxAgeDays;
            Gain                  = Settings.Gain;
            MasterLibraryFolder   = Settings.MasterLibraryFolder;
            MinFrameCount         = Settings.MinFrameCount;
            MaxFrameCount         = Settings.MaxFrameCount;
            DefaultExecutionMode  = NormalizeExecutionMode((DarkExecutionMode)Settings.DefaultExecutionMode);
            DeleteRawsAfterMaxAge = Settings.DeleteRawsAfterMaxAge;
            WriteNinaLiveMasters  = Settings.WriteNinaLiveMasters;
            DiscordWebhookUrl     = Settings.DiscordWebhookUrl;
            DiscordScopeName      = Settings.DiscordScopeName;
            DiscordVerbosePerFrame = Settings.DiscordVerbosePerFrame;
            TempBucketSize        = Settings.TempBucketSize;
            StackTolerance        = Settings.StackTolerance;
            PreBucketLeadC        = Settings.PreBucketLeadC;
            AutoDarkMaxWarmerBucketSteps = Settings.AutoDarkMaxWarmerBucketSteps;
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
                Settings.TargetExposure      = _targetExposure;
                Settings.MaxAgeDays          = _maxAgeDays;
                Settings.Gain                = _gain;
                Settings.MasterLibraryFolder = _masterLibraryFolder;
                Settings.MinFrameCount       = _minFrameCount;
                Settings.MaxFrameCount       = _maxFrameCount;
                Settings.DefaultExecutionMode = (int)_defaultExecutionMode;
                Settings.DeleteRawsAfterMaxAge = _deleteRawsAfterMaxAge;
                Settings.WriteNinaLiveMasters = _writeNinaLiveMasters;
                Settings.DiscordWebhookUrl   = _discordWebhookUrl;
                Settings.DiscordScopeName    = _discordScopeName;
                Settings.DiscordVerbosePerFrame = _discordVerbosePerFrame;
                Settings.TempBucketSize      = _tempBucketSize;
                Settings.StackTolerance      = _stackTolerance;
                Settings.PreBucketLeadC      = _preBucketLeadC;
                Settings.AutoDarkMaxWarmerBucketSteps = _autoDarkMaxWarmerBucketSteps;
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
            Settings.AutoDarkMaxWarmerBucketSteps = NormalizeMaxWarmerBucketSteps(Settings.AutoDarkMaxWarmerBucketSteps);
        }

        private static int NormalizeBucketSize(int value) => value == 3 ? 3 : 2;
        private static int DeriveInternalTolerance(int bucketSize) => bucketSize;
        private static int DeriveInternalPreBucketLeadC() => 2;
        private static int DeriveInternalMinFrameCount() => 20;
        private static int DeriveInternalMaxFrameCount() => 50;
        private static int NormalizeMaxWarmerBucketSteps(int value) => Math.Clamp(value, 0, 3);

        private static DarkExecutionMode NormalizeExecutionMode(DarkExecutionMode value)
            => value == DarkExecutionMode.Manual ? DarkExecutionMode.Manual : DarkExecutionMode.Auto;

        /// <summary>
        /// Refreshes runtime fields from persisted settings so sequencer runs pick up option changes
        /// made since plugin construction (for example, after toggling verbose Discord notifications).
        /// </summary>
        public void RefreshRuntimeSettingsFromDisk() {
            try {
                var latest = SeeDarkSettings.Load(ProfileService.ActiveProfile.ImageFileSettings.FilePath);
                latest.TempBucketSize = NormalizeBucketSize(latest.TempBucketSize);
                latest.StackTolerance = DeriveInternalTolerance(latest.TempBucketSize);
                latest.PreBucketLeadC = DeriveInternalPreBucketLeadC();
                latest.MinFrameCount = DeriveInternalMinFrameCount();
                latest.MaxFrameCount = DeriveInternalMaxFrameCount();
                latest.AutoDarkMaxWarmerBucketSteps = NormalizeMaxWarmerBucketSteps(latest.AutoDarkMaxWarmerBucketSteps);

                Settings.TargetExposure = latest.TargetExposure;
                Settings.MaxAgeDays = latest.MaxAgeDays;
                Settings.Gain = latest.Gain;
                Settings.MasterLibraryFolder = latest.MasterLibraryFolder;
                Settings.MinFrameCount = latest.MinFrameCount;
                Settings.MaxFrameCount = latest.MaxFrameCount;
                Settings.DefaultExecutionMode = latest.DefaultExecutionMode;
                Settings.DeleteRawsAfterMaxAge = latest.DeleteRawsAfterMaxAge;
                Settings.WriteNinaLiveMasters = latest.WriteNinaLiveMasters;
                Settings.DiscordWebhookUrl = latest.DiscordWebhookUrl;
                Settings.DiscordScopeName = latest.DiscordScopeName;
                Settings.DiscordVerbosePerFrame = latest.DiscordVerbosePerFrame;
                Settings.TempBucketSize = latest.TempBucketSize;
                Settings.StackTolerance = latest.StackTolerance;
                Settings.PreBucketLeadC = latest.PreBucketLeadC;
                Settings.AutoDarkMaxWarmerBucketSteps = latest.AutoDarkMaxWarmerBucketSteps;

                _targetExposure = latest.TargetExposure;
                _maxAgeDays = latest.MaxAgeDays;
                _gain = latest.Gain;
                _masterLibraryFolder = latest.MasterLibraryFolder;
                _minFrameCount = latest.MinFrameCount;
                _maxFrameCount = latest.MaxFrameCount;
                _defaultExecutionMode = NormalizeExecutionMode((DarkExecutionMode)latest.DefaultExecutionMode);
                _deleteRawsAfterMaxAge = latest.DeleteRawsAfterMaxAge;
                _writeNinaLiveMasters = latest.WriteNinaLiveMasters;
                _discordWebhookUrl = latest.DiscordWebhookUrl;
                _discordScopeName = latest.DiscordScopeName;
                _discordVerbosePerFrame = latest.DiscordVerbosePerFrame;
                _tempBucketSize = latest.TempBucketSize;
                _stackTolerance = latest.StackTolerance;
                _preBucketLeadC = latest.PreBucketLeadC;
                _autoDarkMaxWarmerBucketSteps = latest.AutoDarkMaxWarmerBucketSteps;
            } catch { }
        }

        public async Task SendDiscordAsync(string msg) {
            var url = DiscordWebhookUrl;
            if (string.IsNullOrWhiteSpace(url)) return;
            var name = DiscordScopeName?.Trim();
            var payload = string.IsNullOrEmpty(name) ? msg : $"{name} - {msg}";
            try {
                using var http = new HttpClient();
                await http.PostAsync(url,
                    new StringContent(
                        $"{{\"content\":{JsonConvert.ToString(payload)}}}",
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

        /// <summary>NINA profile image file path only (no IMAGETYPE narrowing).</summary>
        public string GetNinaImageFileRoot() {
            try {
                return ProfileService.ActiveProfile?.ImageFileSettings?.FilePath?.Trim() ?? "";
            } catch {
                return "";
            }
        }

        /// <summary>Root folder for raw DARK discovery and Auto save: NINA image path, narrowed through the first <c>$$IMAGETYPE$$</c> path segment in the DARK file pattern when possible.</summary>
        public string GetNinaDarkRawRootFolder() {
            try {
                return RawDarkScanRootResolver.Resolve(GetNinaImageFileRoot(), GetNinaDarkFilePattern());
            } catch {
                return "";
            }
        }

        public string GetNinaDarkFilePattern() {
            try {
                var imageSettings = ProfileService.ActiveProfile?.ImageFileSettings;
                if (imageSettings == null)
                    return "";
                return imageSettings.GetFilePattern("DARK")?.Trim() ?? "";
            } catch {
                return "";
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

        private DarkExecutionMode _defaultExecutionMode = DarkExecutionMode.Auto;
        public DarkExecutionMode DefaultExecutionMode {
            get => _defaultExecutionMode;
            set { _defaultExecutionMode = NormalizeExecutionMode(value); RaisePropertyChanged(); SyncAndSaveSettings(); }
        }

        private bool _deleteRawsAfterMaxAge = false;
        public bool DeleteRawsAfterMaxAge {
            get => _deleteRawsAfterMaxAge;
            set { _deleteRawsAfterMaxAge = value; RaisePropertyChanged(); SyncAndSaveSettings(); }
        }

        private bool _writeNinaLiveMasters = false;
        public bool WriteNinaLiveMasters {
            get => _writeNinaLiveMasters;
            set { _writeNinaLiveMasters = value; RaisePropertyChanged(); SyncAndSaveSettings(); }
        }

        private string _discordWebhookUrl = "";
        public string DiscordWebhookUrl {
            get => _discordWebhookUrl;
            set { _discordWebhookUrl = value; RaisePropertyChanged(); SyncAndSaveSettings(); }
        }

        private string _discordScopeName = "";
        public string DiscordScopeName {
            get => _discordScopeName;
            set { _discordScopeName = value; RaisePropertyChanged(); SyncAndSaveSettings(); }
        }

        private bool _discordVerbosePerFrame = false;
        public bool DiscordVerbosePerFrame {
            get => _discordVerbosePerFrame;
            set { _discordVerbosePerFrame = value; RaisePropertyChanged(); SyncAndSaveSettings(); }
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

        private int _autoDarkMaxWarmerBucketSteps = 0;
        public int AutoDarkMaxWarmerBucketSteps {
            get => _autoDarkMaxWarmerBucketSteps;
            set { _autoDarkMaxWarmerBucketSteps = NormalizeMaxWarmerBucketSteps(value); RaisePropertyChanged(); SyncAndSaveSettings(); }
        }

    }
}
