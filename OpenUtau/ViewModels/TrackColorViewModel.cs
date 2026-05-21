using System.Collections.ObjectModel;
using System.Linq;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using ReactiveUI.Fody.Helpers;

namespace OpenUtau.App.ViewModels {
    public class TrackColorViewModel : ViewModelBase {
        public ObservableCollection<TrackColor> TrackColors { get; } = new ObservableCollection<TrackColor>(ThemeManager.TrackColors);
        [Reactive] public TrackColor SelectedColor { get; set; }
        private UTrack track;

        public TrackColorViewModel(UTrack track) {
            SelectedColor = TrackColors.FirstOrDefault(c => c.Name == track.TrackColor) ?? TrackColors.First(c => c.Name == "Blue");
            this.track = track;
        }

        public void Finish() {
            if(SelectedColor.Name != track.TrackColor) {
                DocManager.Inst.StartUndoGroup("command.track.setting");
                DocManager.Inst.ExecuteCmd(new ChangeTrackColorCommand(DocManager.Inst.Project, track, SelectedColor.Name));
                DocManager.Inst.EndUndoGroup();
            }
        }
    }
}
