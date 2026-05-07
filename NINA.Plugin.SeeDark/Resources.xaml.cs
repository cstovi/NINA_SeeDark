using System.ComponentModel.Composition;
using Microsoft.Win32;
using System.Windows;

namespace NINA.Plugin.SeeDark {

    [Export(typeof(ResourceDictionary))]
    public partial class Resources : ResourceDictionary {
        public Resources() {
            InitializeComponent();
        }

        private void BrowseMasterLibraryFolder_Click(object sender, RoutedEventArgs e) {
            if (sender is not FrameworkElement element || element.DataContext is not SeeDarkPlugin plugin) return;
            var selected = BrowseForFolder(plugin.MasterLibraryFolder, "Select master dark library folder");
            if (!string.IsNullOrWhiteSpace(selected)) {
                plugin.MasterLibraryFolder = selected;
            }
        }

        private static string? BrowseForFolder(string currentPath, string description) {
            var dialog = new OpenFolderDialog {
                Title = description,
                Multiselect = false
            };
            if (!string.IsNullOrWhiteSpace(currentPath))
                dialog.InitialDirectory = currentPath;
            return dialog.ShowDialog() == true
                ? dialog.FolderName
                : null;
        }
    }
}
