using System;
using System.Linq;
using Xunit;

namespace OpenUtau.Core.Analysis {
    public class GlottalRdTest {
        // cig_lfmodel_spectrum(cig_lfmodel_from_rd(rd, 1 / f0, 1), harmonics 1..6 of f0) from
        // ciglet's C code with its exact complex exponential (c_exp_3; ciglet defaults to a fast
        // approximation): every Rd branch, and an f0 above the model's 800 Hz limit.
        public static TheoryData<double, double, double[]> CigletSpectra => new TheoryData<double, double, double[]> {
            { 0.1, 200.0, new[] { 2.1462838965559116e-05, 4.1372420853606477e-05, 5.8367378674321058e-05, 7.1443952187086313e-05, 8.0072985547249308e-05, 8.4252064440908548e-05 } },
            { 0.1, 900.0, new[] { 5.3657097413897789e-06, 1.0343105213401619e-05, 1.4591844668580268e-05, 1.7860988046771578e-05, 2.0018246386812334e-05, 2.1063016110227134e-05 } },
            { 0.3, 200.0, new[] { 0.00016020846987303071, 0.00025346283776309835, 0.00025875476144393832, 0.00021226653248192055, 0.00016962399333148454, 0.00014467523416783701 } },
            { 0.3, 900.0, new[] { 4.0052117468257678e-05, 6.3365709440774587e-05, 6.4688690360984607e-05, 5.3066633120480138e-05, 4.2405998332871129e-05, 3.6168808541959246e-05 } },
            { 1.0, 200.0, new[] { 0.00090770573172980281, 0.00058330602970292102, 0.00029960559850256071, 0.00017872080504580573, 0.00013547967936391434, 9.784564604658546e-05 } },
            { 1.0, 900.0, new[] { 0.00022690941853714826, 0.0001458408242480015, 7.4901089146900982e-05, 4.4686205054440074e-05, 3.3872769033031013e-05, 2.4461580561761288e-05 } },
            { 2.5, 200.0, new[] { 0.0019741303572098573, 0.00026187359196017118, 0.00014901375420373671, 9.3324276034419853e-05, 5.4896358879706196e-05, 2.9625951539374426e-05 } },
            { 2.5, 900.0, new[] { 0.00049353258926189642, 6.5468397979630619e-05, 3.725343855019255e-05, 2.3331069011546356e-05, 1.3724089722283215e-05, 7.4064878849257145e-06 } },
            { 2.9, 200.0, new[] { 0.0021770118084563834, 0.00021096775106708122, 0.00011838993681375577, 7.7456842723368775e-05, 5.0657289590790522e-05, 3.1370662544881337e-05 } },
            { 2.9, 900.0, new[] { 0.0005442529520956321, 5.2741937762031218e-05, 2.9597484202353703e-05, 1.9364210681640109e-05, 1.2664322398858423e-05, 7.8426656369156229e-06 } },
        };

        [Theory]
        [MemberData(nameof(CigletSpectra))]
        public void LfSpectrumMatchesCiglet(double rd, double f0, double[] expected) {
            var freq = Enumerable.Range(1, 6).Select(k => k * f0).ToArray();
            var actual = LfModel.Spectrum(LfModel.FromRd(rd, 1.0 / f0, 1.0), freq);
            for (int k = 0; k < 6; k++) {
                Assert.Equal(1.0, actual[k] / expected[k], 9);
            }
        }

        [Theory]
        [InlineData(0.3, 220)]
        [InlineData(0.8, 150)]
        [InlineData(1.5, 300)]
        [InlineData(2.5, 180)]
        public void FitRecoversRd(double rd, double f0) {
            int n = (int)(GlottalRd.MaxFitHz / f0);
            // A flat vocal tract: the flow shape through the lips, at an arbitrary level.
            var amplitudes = GlottalRd.FlowShape(rd, f0, n).Select((a, k) => 0.3 * a * GlottalRd.LipGain((k + 1) * f0)).ToArray();
            Assert.Equal(rd, GlottalRd.Fit(amplitudes, f0), 1);
        }

        [Fact]
        public void TensionMapping() {
            Assert.Equal(1.0, GlottalRd.TenseRd(1.0, 0));
            Assert.Equal(0.5, GlottalRd.TenseRd(1.0, 100), 12);
            Assert.Equal(2.0, GlottalRd.TenseRd(1.0, -100), 12);
            Assert.Equal(GlottalRd.MaxRd, GlottalRd.TenseRd(2.5, -100));
            Assert.Equal(GlottalRd.MinRd, GlottalRd.TenseRd(0.03, 100));
        }

        [Fact]
        public void GainsKeepTheFirstHarmonic() {
            var same = GlottalRd.Gains(1.0, 1.0, 200, 40);
            Assert.All(same, g => Assert.Equal(1.0, g, 12));
            var tense = GlottalRd.Gains(1.0, 0.5, 200, 40);
            Assert.Equal(1.0, tense[0], 12);
            Assert.True(tense[10] > 1.5);  // lower Rd: brighter
            var lax = GlottalRd.Gains(1.0, 2.0, 200, 40);
            Assert.True(lax[10] < 0.7);
            Assert.Equal(1.0, GlottalRd.GainAt(tense, 200, 150));  // below the first harmonic
            Assert.Equal(tense[3], GlottalRd.GainAt(tense, 200, 800), 12);
            Assert.InRange(GlottalRd.GainAt(tense, 200, 900), Math.Min(tense[3], tense[4]), Math.Max(tense[3], tense[4]));
        }

        [Fact]
        public void SmoothFillsUnvoiced() {
            var rd = new double[] { 0, 1, 0, 0, 3, 0 };
            var voiced = new[] { false, true, false, false, true, false };
            var filled = GlottalRd.Smooth(rd, voiced, 1);
            var expected = new double[] { 1, 1, 5 / 3.0, 7 / 3.0, 3, 3 };
            for (int i = 0; i < expected.Length; i++) {
                Assert.Equal(expected[i], filled[i], 12);
            }
            Assert.All(GlottalRd.Smooth(new double[4], new bool[4], 3), v => Assert.Equal(1.0, v));
        }
    }
}
