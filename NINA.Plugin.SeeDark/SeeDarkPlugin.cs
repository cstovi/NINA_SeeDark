using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using NINA.Core.Utility;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Plugin;
using NINA.Plugin.Interfaces;

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
        public SeeDarkPlugin(ICameraMediator cameraMediator) {
            CameraMediator = cameraMediator;
            Settings = SeeDarkSettings.Load();

            DarkLibraryCsvPath    = Settings.DarkLibraryCsvPath;
            TargetExposure        = Settings.TargetExposure;
            MaxAgeDays            = Settings.MaxAgeDays;
            Gain                  = Settings.Gain;
            RawDarksFolder        = Settings.RawDarksFolder;
            MasterLibraryFolder   = Settings.MasterLibraryFolder;
            MinFrameCount         = Settings.MinFrameCount;

            SaveSettingsCommand = new RelayCommand(_ => ApplyAndSave());
        }

        public ICommand SaveSettingsCommand { get; }

        private void ApplyAndSave() {
            Settings.DarkLibraryCsvPath  = DarkLibraryCsvPath;
            Settings.TargetExposure      = TargetExposure;
            Settings.MaxAgeDays          = MaxAgeDays;
            Settings.Gain                = Gain;
            Settings.RawDarksFolder      = RawDarksFolder;
            Settings.MasterLibraryFolder = MasterLibraryFolder;
            Settings.MinFrameCount       = MinFrameCount;
            Settings.Save();
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

        private string _darkLibraryCsvPath = "";
        public string DarkLibraryCsvPath {
            get => _darkLibraryCsvPath;
            set { _darkLibraryCsvPath = value; RaisePropertyChanged(); }
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
    }
}
