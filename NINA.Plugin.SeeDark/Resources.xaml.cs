using System.ComponentModel.Composition;
using System.Windows;

namespace NINA.Plugin.SeeDark {

    [Export(typeof(ResourceDictionary))]
    public partial class Resources : ResourceDictionary {
        public Resources() {
            InitializeComponent();
        }
    }
}
