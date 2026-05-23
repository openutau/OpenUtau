using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Serilog;

namespace OpenUtau.Plugin.Builtin
{
    public abstract class MonophonePhonemizer : PhonemeBasedPhonemizer
    {
        public MonophonePhonemizer(){
            addTail = false;
        }
        
        protected override string GetPhonemeOrFallback(string prevSymbol, string symbol, int tone, string color, string alt) {
            if (!string.IsNullOrEmpty(alt) && singer.TryGetMappedOto($"{symbol}{alt}", tone, color, out var oto)) {
                return oto.Alias;
            }
            if (singer.TryGetMappedOto(symbol, tone, color, out var oto1)) {
                return oto1.Alias;
            }
            if (vowelFallback.TryGetValue(symbol, out string[] fallbacks)) {
                foreach (var fallback in fallbacks) {
                    if (singer.TryGetMappedOto(fallback, tone, color, out var oto2)) {
                        return oto2.Alias;
                    }
                }
            }
            return $"{symbol}{alt}";
        }
    }
}