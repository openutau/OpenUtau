using System;
using OpenUtau.Core.Util;
using Xunit;

namespace OpenUtau.Core.Util {
    public class FrozenTest {
        [Fact]
        public void SpanRespectsLength() {
            var data = new float[] { 1, 2, 3, 4, 5 };
            var frozen = data.Freeze(3);
            Assert.Equal(3, frozen.Length);
            Assert.Equal(5, frozen.Buffer.Length);
            Assert.Equal(new[] { 1f, 2f, 3f }, frozen.Span.ToArray());
        }

        [Fact]
        public void FreezeDefaultsToWholeArray() {
            var data = new int[] { 7, 8 };
            var frozen = data.Freeze();
            Assert.Equal(2, frozen.Length);
            Assert.Equal(new[] { 7, 8 }, frozen.Span.ToArray());
        }

        [Fact]
        public void InvalidLengthThrows() {
            var data = new float[4];
            Assert.Throws<ArgumentOutOfRangeException>(() => data.Freeze(5));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Frozen<float>(data, -1));
            Assert.Throws<ArgumentNullException>(() => new Frozen<float>(null, 0));
        }

        [Fact]
        public void HashIsContentAddressed() {
            var a = new float[] { 1, 2, 3, 4 }.Freeze();
            var b = new float[] { 1, 2, 3, 4 }.Freeze();
            var c = new float[] { 1, 2, 3, 5 }.Freeze();
            Assert.Equal(a.DebugHash, b.DebugHash);
            Assert.NotEqual(a.DebugHash, c.DebugHash);
        }

        [Fact]
        public void LengthParticipatesInHash() {
            var a = new float[] { 1, 2, 3, 4 }.Freeze(2);
            var b = new float[] { 1, 2, 3, 4 }.Freeze(4);
            Assert.NotEqual(a.DebugHash, b.DebugHash);
        }

        [Fact]
        public void VerifyPassesOnUntouchedBuffer() {
            var frozen = new float[] { 9.5f, -3f, 0f, 1f }.Freeze();
            frozen.Verify(); // must not throw
        }

        [Fact]
        public void VerifyThrowsWhenMutated() {
            var data = new float[] { 9.5f, -3f, 0f, 1f };
            var frozen = data.Freeze();
            data[2] = 42f; // violates the ownership contract
            Assert.Throws<InvalidOperationException>(() => frozen.Verify());
        }

        [Fact]
        public void EmptyBufferHashesStably() {
            var a = Array.Empty<float>().Freeze();
            var b = Array.Empty<float>().Freeze();
            Assert.Equal(a.DebugHash, b.DebugHash);
            a.Verify();
        }
    }
}
