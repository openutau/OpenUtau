using YamlDotNet.Serialization;

namespace OpenUtau.Core.Ustx {
    /// <summary>
    /// A half-open tick range on a part's own timeline. Used by replaceable
    /// render layers such as the DiffSinger take lock.
    /// </summary>
    public class UTickRange {
        [YamlMember(Order = 100)] public int start;
        [YamlMember(Order = 101)] public int end;

        public UTickRange() { }

        public UTickRange(int start, int end) {
            this.start = start;
            this.end = end;
        }

        public UTickRange Clone() => new UTickRange(start, end);

        public bool Overlaps(int rangeStart, int rangeEnd) => start < rangeEnd && end > rangeStart;
    }
}
