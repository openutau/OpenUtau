using OpenUtau.Core.Format;
using OpenUtau.Core.Ustx;
using Xunit;

namespace OpenUtau.Test.Core.USTx {
    public class CrossSynthesisExpressionTest {
        [Fact]
        public void RegistersCrossSynthesisExpressions() {
            var project = new UProject();

            Ustx.AddDefaultExpressions(project);

            Assert.Equal(UExpressionType.Options, project.expressions[Ustx.CLRY].type);
            Assert.Equal(UExpressionType.Curve, project.expressions[Ustx.XSY].type);
            Assert.Equal(0, project.expressions[Ustx.XSY].min);
            Assert.Equal(100, project.expressions[Ustx.XSY].max);
        }
    }
}
