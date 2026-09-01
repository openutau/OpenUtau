using DynamicData.Binding;
using OpenUtau.Core;
using OpenUtau.Core.DawIntegration;
using ReactiveUI.SourceGenerators;
using System.Threading.Tasks;

namespace OpenUtau.App.ViewModels {
    public partial class DawIntegrationTerminalViewModel : ViewModelBase {
        [Reactive] public partial DawServer? SelectedServer { get; set; } = null;
        [Reactive] public partial bool CanConnect { get; set; } = true;
        public ObservableCollectionExtended<DawServer> ServerList { get; set; } = new ObservableCollectionExtended<DawServer>();

        public DawIntegrationTerminalViewModel() {
            Task.Run(() => RefreshServerList());
        }

        public async Task RefreshServerList() {
            var servers = await DawServerFinder.FindServers();

            ServerList.Load(servers);
            if (servers.Count == 0) {
                SelectedServer = null;
            } else {
                SelectedServer = ServerList[0];
            }
        }

        public async Task Connect() {
            if (SelectedServer == null) {
                return;
            }
            try {
                CanConnect = false;
                var ustx = await DawManager.Inst.Connect(SelectedServer);

                if (ustx.Length > 0) {
                    DocManager.Inst.ExecuteCmd(new LoadProjectNotification(Core.Format.Ustx.LoadText(ustx)));
                }
                await DawManager.Inst.Synchronize();
            } finally {
                CanConnect = true;
            }
        }

    }
}
