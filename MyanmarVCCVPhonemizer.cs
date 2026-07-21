#pragma warning disable CS0618, CS0649, CS8632, CS0108
using OpenUtau.Api;
using OpenUtau.Plugin.Builtin;

namespace OpenUtau.Plugin.Builtin {
    [Phonemizer("Myanmar VCCV Phonemizer", "MY VCCV", "DELTA SYNTH", language: "MY")]
    // Version: v
    public class MyanmarVCCVPhonemizer : ThaiVCCVPhonemizer {
        // Inherits all logic from Thai VCCV but registers as Myanmar (MY)
    }
}
