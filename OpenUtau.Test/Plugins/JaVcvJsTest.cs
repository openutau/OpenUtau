using System.IO;
using System.Linq;
using System.Reflection;
using OpenUtau.Api;
using OpenUtau.Core;
using OpenUtau.Core.Js;
using Xunit;

namespace OpenUtau.Plugins {
    /// <summary>
    /// Runs the <see cref="JaVcvTest"/> cases against the JavaScript port in Files/ja_vcv_js.
    /// </summary>
    public class JaVcvJsTest : JaVcvTest {
        public JaVcvJsTest(ITestOutputHelper output) : base(output) { }

        protected override Phonemizer CreatePhonemizer() {
            var dir = Path.Join(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "Files", "ja_vcv_js");
            var entrypoint = PackageManager.GetEntrypoints(dir).Single(ep => ep.Entrypoint.loader == JsPhonemizerFactory.LoaderName);
            var factory = JsPhonemizerFactory.Load(entrypoint);
            Assert.Equal("JA VCV JS", factory.tag);
            Assert.Equal("js:ja_vcv_js/ja_vcv.js", factory.Id);
            return factory.Create();
        }
    }
}
