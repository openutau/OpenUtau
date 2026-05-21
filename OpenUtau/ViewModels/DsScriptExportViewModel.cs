using OpenUtau.Core.DiffSinger;
using OpenUtau.Core.Util;
using ReactiveUI.Fody.Helpers;

namespace OpenUtau.App.ViewModels {
    public class DsScriptExportViewModel : ViewModelBase {
        [Reactive] public bool ExportPitch { get; set; } = true;
        [Reactive] public bool ExportVariance { get; set; } = false;
        public bool TensorCacheEnabled => Preferences.Default.DiffSingerTensorCache;

        public DsScriptExportOptions BuildOptions() {
            return new DsScriptExportOptions {
                exportPitch = ExportPitch,
                exportVariance = TensorCacheEnabled && ExportVariance,
            };
        }
    }
}
