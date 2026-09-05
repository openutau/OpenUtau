using System.Collections.ObjectModel;
using OpenUtau.Core;
using ReactiveUI.SourceGenerators;
using ReactiveUI;

namespace OpenUtau.App.ViewModels {
    public class PasteParamViewModel {

        public PasteParamViewModel() {
            Params.Add(new PasteParameter("pitch points", ""));
            Params.Add(new PasteParameter("vibrato", ""));
            foreach(var exp in DocManager.Inst.Project.expressions) {
                if(exp.Value.type != Core.Ustx.UExpressionType.Curve) {
                    Params.Add(new PasteParameter(exp.Value.name, exp.Key));
                }
            }
            Params[0].IsSelected = true;
        }

        public ObservableCollection<PasteParameter> Params { get; } = new ObservableCollection<PasteParameter>();
    }

    public partial class PasteParameter: ReactiveObject {
        public PasteParameter(string name, string abbr) {
            Name = name;
            Abbr = abbr;
        }
        public string Name { get; set; }
        public string Abbr { get; set; }
        [Reactive] public partial bool IsSelected { get; set; } = false;

        public override string ToString() { return Name; }
    }
}
