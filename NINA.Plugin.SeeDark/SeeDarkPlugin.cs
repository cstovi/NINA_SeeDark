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

            DarkLibraryCsvPath = Settings.DarkLibraryCsvPath;
            TargetExposure = Settings.TargetExposure;
            MaxAgeDays = Settings.MaxAgeDays;
            AlpacaPort = Settings.AlpacaPort;

            SaveSettingsCommand = new RelayCommand(_ => ApplyAndSave());
        }

        public ICommand SaveSettingsCommand { get; }

        private void ApplyAndSave() {
            Settings.DarkLibraryCsvPath = DarkLibraryCsvPath;
            Settings.TargetExposure = TargetExposure;
            Settings.MaxAgeDays = MaxAgeDays;
            Settings.AlpacaPort = AlpacaPort;
            Settings.Save();
        }

        private string _darkLibraryCsvPath = "";
        public string DarkLibraryCsvPath {
            get => _darkLibraryCsvPath;
            set { _darkLibraryCsvPath = value; RaisePropertyChanged(); }
        }

        private double _targetExposure = 60.0;
        public double TargetExposure {
            get => _targetExposure;
            set { _targetExposure = value; RaisePropertyChanged(); }
        }

        private int _maxAgeDays = 180;
        public int MaxAgeDays {
            get => _maxAgeDays;
            set { _maxAgeDays = value; RaisePropertyChanged(); }
        }

        private int _alpacaPort = 11111;
        public int AlpacaPort {
            get => _alpacaPort;
            set { _alpacaPort = value; RaisePropertyChanged(); }
        }
    }
}
