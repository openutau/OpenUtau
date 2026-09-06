using System.Collections.Generic;
using OpenUtau.Classic;
using OpenUtau.Core.Format;
using OpenUtau.Core.Ustx;
using Xunit;

namespace OpenUtau.Test.Core.USTx {
    public class MorphingExpressionTest {
        [Fact]
        public void RegistersMorphingCurvesForSingerColors() {
            var project = new UProject();
            Ustx.AddDefaultExpressions(project);

            // Create a multi-color test singer
            var singer = new ClassicSinger(new Voicebank {
                Name = "TestSinger",
                Subbanks = new List<Subbank> {
                    new Subbank { Color = "power", Suffix = "_P" },
                    new Subbank { Color = "whisper", Suffix = "_W" },
                }
            });

            var renderer = new ClassicRenderer();
            var settings = new URenderSettings();
            var suggested = renderer.GetSuggestedExpressions(singer, settings);

            // Verify that morphing curves (cl01, cl02, etc.) are generated
            Assert.Contains(suggested, exp => exp.abbr == "cl01" && exp.type == UExpressionType.MorphingCurve);
            Assert.Contains(suggested, exp => exp.abbr == "cl02" && exp.type == UExpressionType.MorphingCurve);

            var cl01 = System.Array.Find(suggested, exp => exp.abbr == "cl01");
            Assert.NotNull(cl01);
            Assert.Equal(0, cl01.min);
            Assert.Equal(100, cl01.max);
            Assert.Equal(0, cl01.defaultValue);
            Assert.Equal(UExpressionType.MorphingCurve, cl01.type);
        }
    }
}