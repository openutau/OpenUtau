
using System;

namespace OpenUtau.Classic.Flags {
    public class UstFlag {
        public readonly string Key;
        public readonly int Value;

        public UstFlag(string key, int value) {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Value = value;
        }

        public override string ToString() {
            return Key + Value;
        }
    }
}
