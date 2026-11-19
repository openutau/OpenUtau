using OpenUtau.Core.Ustx;
using Xunit;

namespace OpenUtau.Test.Core.USTx {
    public class UExpressionDescriptorTest {
        static UExpressionDescriptor Numerical() => new UExpressionDescriptor("gender", "gen", -100, 100, 0, "g");
        static UExpressionDescriptor Options() => new UExpressionDescriptor("resampler engine", "eng", false, new[] { "", "a" });

        [Fact]
        public void EqualToACopy() {
            Assert.True(Numerical().Equals(Numerical()));
            Assert.True(Options().Equals(Options()));
            Assert.True(Numerical().Equals(Numerical().Clone()));
        }

        [Fact]
        public void ComparesSkipOutputWithoutOptions() {
            var other = Numerical();
            other.skipOutputIfDefault = true;
            Assert.False(Numerical().Equals(other));
        }

        [Fact]
        public void NoOptionsOnOneSideDoesNotThrow() {
            var withOptions = Numerical();
            withOptions.options = new[] { "x" };
            Assert.False(Numerical().Equals(withOptions));
            Assert.False(withOptions.Equals(Numerical()));

            // No options and an empty list mean the same.
            var empty = Numerical();
            empty.options = new string[0];
            Assert.True(Numerical().Equals(empty));
        }

        [Fact]
        public void ComparesOptions() {
            var other = Options();
            other.options = new[] { "", "b" };
            Assert.False(Options().Equals(other));
        }

        [Fact]
        public void NotEqualToNull() {
            Assert.False(Numerical().Equals(null!));
        }
    }
}
