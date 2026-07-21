#pragma warning disable CS0618, CS0649, CS8632, CS0108
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenUtau.Api;
using OpenUtau.Core.Ustx;
using Serilog;

namespace OpenUtau.Plugin.Builtin {
    /// <summary>
    /// A base diphone phonemizer for latin languages.
    /// </summary>
    public abstract class LatinDiphonePhonemizer : PhonemeBasedPhonemizer {
        protected override string GetPhonemeOrFallback(string prevSymbol, string symbol, int tone, string color, string alt) {
            if (!string.IsNullOrEmpty(alt) && singer.TryGetMappedOto($"{prevSymbol} {symbol}{alt}", tone, color, out var oto)) {
                return oto.Alias;
            }
            if (singer.TryGetMappedOto($"{prevSymbol} {symbol}", tone, color, out var oto1)) {
                return oto1.Alias;
            }
            if (vowelFallback.TryGetValue(symbol, out string[] fallbacks)) {
                foreach (var fallback in fallbacks) {
                    if (singer.TryGetMappedOto($"{prevSymbol} {fallback}", tone, color, out var oto2)) {
                        return oto2.Alias;
                    }
                }
            }
            if (singer.TryGetMappedOto($"- {symbol}", tone, color, out var oto3)) {
                return oto3.Alias;
            }
            return $"{prevSymbol} {symbol}{alt}";
        }

    }
}
