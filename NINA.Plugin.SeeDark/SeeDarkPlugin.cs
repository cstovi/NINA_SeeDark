using System;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using Newtonsoft.Json;
using NINA.Core.Utility;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Plugin;
using NINA.Plugin.Interfaces;
using NINA.Profile.Interfaces;

namespace NINA.Plugin.SeeDark {

    [Export(typeof(IPluginManifest))]
    [Export]
    public class SeeDarkPlugin : PluginBase, IPluginManifest, INotifyPropertyChanged {

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void RaisePropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public ICameraMediator CameraMediator { get; }
        public SeeDarkSettings Settings { get; }

        [ImportingConstructor]
        public SeeDarkPlugin(ICameraMediator cameraMediator, IProfileService profileService) {
            CameraMediator = cameraMediator;
            Settings = SeeDarkSettings.Load(profileService.ActiveProfile.ImageFileSettings.FilePath);
            NormalizeSimpleThermalSettings();

            TargetExposure        = Settings.TargetExposure;
            MaxAgeDays            = Settings.MaxAgeDays;
            Gain                  = Settings.Gain;
            RawDarksFolder        = Settings.RawDarksFolder;
            MasterLibraryFolder   = Settings.MasterLibraryFolder;
            MinFrameCount         = Settings.MinFrameCount;
            DiscordWebhookUrl     = Settings.DiscordWebhookUrl;
            TempBucketSize        = Settings.TempBucketSize;
            StackTolerance        = Settings.StackTolerance;
            PreBucketLeadC        = Settings.PreBucketLeadC;

            SaveSettingsCommand = new RelayCommand(_ => ApplyAndSave());
        }

        public ICommand SaveSettingsCommand { get; }

        private void ApplyAndSave() {
            TempBucketSize = 2;
            StackTolerance = Math.Clamp(StackTolerance, 1, 2);
            PreBucketLeadC = Math.Clamp(PreBucketLeadC, 1, 3);

            Settings.TargetExposure      = TargetExposure;
            Settings.MaxAgeDays          = MaxAgeDays;
            Settings.Gain                = Gain;
            Settings.RawDarksFolder      = RawDarksFolder;
            Settings.MasterLibraryFolder = MasterLibraryFolder;
            Settings.MinFrameCount       = MinFrameCount;
            Settings.DiscordWebhookUrl   = DiscordWebhookUrl;
            Settings.TempBucketSize      = TempBucketSize;
            Settings.StackTolerance      = StackTolerance;
            Settings.PreBucketLeadC      = PreBucketLeadC;
            Settings.Save();
        }

        private void NormalizeSimpleThermalSettings() {
            Settings.TempBucketSize = 2;
            Settings.StackTolerance = Math.Clamp(Settings.StackTolerance, 1, 2);
            Settings.PreBucketLeadC = Math.Clamp(Settings.PreBucketLeadC, 1, 3);
        }

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

        private double _targetExposure = 20.0;
        public double TargetExposure {
            get => _targetExposure;
            set { _targetExposure = value; RaisePropertyChanged(); }
        }

        private int _maxAgeDays = 180;
        public int MaxAgeDays {
            get => _maxAgeDays;
            set { _maxAgeDays = value; RaisePropertyChanged(); }
        }

        private int _gain = 200;
        public int Gain {
            get => _gain;
            set { _gain = value; RaisePropertyChanged(); }
        }

        private string _rawDarksFolder = "";
        public string RawDarksFolder {
            get => _rawDarksFolder;
            set { _rawDarksFolder = value; RaisePropertyChanged(); }
        }

        private string _masterLibraryFolder = "";
        public string MasterLibraryFolder {
            get => _masterLibraryFolder;
            set { _masterLibraryFolder = value; RaisePropertyChanged(); }
        }

        private int _minFrameCount = 20;
        public int MinFrameCount {
            get => _minFrameCount;
            set { _minFrameCount = value; RaisePropertyChanged(); }
        }

        private string _discordWebhookUrl = "";
        public string DiscordWebhookUrl {
            get => _discordWebhookUrl;
            set { _discordWebhookUrl = value; RaisePropertyChanged(); }
        }

        private int _tempBucketSize = 2;
        public int TempBucketSize {
            get => _tempBucketSize;
            set { _tempBucketSize = 2; RaisePropertyChanged(); }
        }

        private int _stackTolerance = 2;
        public int StackTolerance {
            get => _stackTolerance;
            set { _stackTolerance = Math.Clamp(value, 1, 2); RaisePropertyChanged(); }
        }

        private int _preBucketLeadC = 1;
        public int PreBucketLeadC {
            get => _preBucketLeadC;
            set { _preBucketLeadC = Math.Clamp(value, 1, 3); RaisePropertyChanged(); }
        }
    }
}
