using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using OpenUtau.Core.G2p;
using OpenUtau.Core.Util;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtau.App.ViewModels {
    public class PhoneticAssistantViewModel : ViewModelBase {
        public class G2pOption {
            public string name;
            public Type klass;
            public G2pOption(Type klass) {
                name = klass.Name;
                this.klass = klass;
            }
            public override string ToString() => name;
        }
        public List<G2pOption> G2ps => g2ps;

        [Reactive] public G2pOption? G2p { get; set; }
        [Reactive] public string? Grapheme { get; set; }
        [Reactive] public string Phonemes { get; set; }

        private readonly List<G2pOption> g2ps = new List<G2pOption>() {
            new G2pOption(typeof(ArpabetG2p)),
            new G2pOption(typeof(ArpabetPlusG2p)),
            new G2pOption(typeof(FrenchG2p)),
            new G2pOption(typeof(FrenchMillefeuilleG2p)),
            new G2pOption(typeof(GermanG2p)),
            new G2pOption(typeof(GermanMarzipanG2p)),
            new G2pOption(typeof(ItalianG2p)),
            new G2pOption(typeof(PortugueseG2p)),
            new G2pOption(typeof(RussianG2p)),
            new G2pOption(typeof(SpanishG2p)),
            new G2pOption(typeof(KoreanG2p)),
            new G2pOption(typeof(FilipinoG2p)),
        };

        private Api.G2pPack? g2p;

        public PhoneticAssistantViewModel() {
            G2p = g2ps.FirstOrDefault(x=>x.name == Preferences.Default.PhoneticAssistant) ?? g2ps.First();
            Grapheme = string.Empty;
            Phonemes = string.Empty;
            this.WhenAnyValue(x => x.G2p)
                .Subscribe(option => {
                    g2p = null;
                    if (option != null) {
                        g2p = Activator.CreateInstance(option.klass) as Api.G2pPack;
                        Preferences.Default.PhoneticAssistant = option.name;
                        Preferences.Save();
                        Refresh();
                    }
                });
            this.WhenAnyValue(x => x.Grapheme)
                .Subscribe(_ => Refresh());
        }

        private void Refresh() {
            if (Grapheme == null || g2p == null) {
                Phonemes = string.Empty;
                return;
            }
            string[] phonemes = g2p.Query(Grapheme);
            if (phonemes == null) {
                Phonemes = string.Empty;
                return;
            }
            Phonemes = string.Join(' ', phonemes);
        }
    }
}
