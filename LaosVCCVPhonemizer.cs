#pragma warning disable CS0618, CS0649, CS8632, CS0108
using OpenUtau.Api;
using OpenUtau.Plugin.Builtin;

namespace OpenUtau.Plugin.Builtin {
    [Phonemizer("Lao VCCV Phonemizer", "LO VCCV", "DELTA SYNTH", language: "LO")]
    // Version: v
    public class LaoVCCVPhonemizer : ThaiVCCVPhonemizer {
        // Inherits all logic from Thai VCCV but registers as Lao (LO)
    }
}
