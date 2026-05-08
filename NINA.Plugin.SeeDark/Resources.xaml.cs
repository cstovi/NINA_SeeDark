using System.ComponentModel.Composition;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

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

        private void CommitOnEnter_KeyDown(object sender, KeyEventArgs e) {
            if (e.Key != Key.Enter)
                return;
            if (sender is not TextBox textBox)
                return;

            // Enter should commit the edit immediately without being required.
            textBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            e.Handled = true;
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
