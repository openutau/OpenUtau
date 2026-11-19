using System;
using System.IO;
using System.Linq;
using OpenUtau.Api;
using OpenUtau.Core;
using OpenUtau.Core.Js;
using Xunit;

namespace OpenUtau.Plugins {
    public class JsPhonemizerTest : IDisposable {
        readonly string dir = Path.Combine(Path.GetTempPath(), "OpenUtau.Test", nameof(JsPhonemizerTest), Guid.NewGuid().ToString("N"));

        public void Dispose() {
            if (Directory.Exists(dir)) {
                Directory.Delete(dir, true);
            }
        }

        const string Info = "export const info = { name: 'Test', tag: 'TEST' };\n";

        JsPhonemizerFactory LoadPackage(string script) {
            var packageDir = Path.Combine(dir, "pkg");
            Directory.CreateDirectory(packageDir);
            File.WriteAllText(Path.Combine(packageDir, "oudep.yaml"),
                "id: test_pkg\nversion: '1.0'\nentrypoints:\n  - loader: JsPhonemizer\n    path: main.js\n");
            File.WriteAllText(Path.Combine(packageDir, "main.js"), script);
            var entrypoint = PackageManager.GetEntrypoints(packageDir).Single();
            return JsPhonemizerFactory.Load(entrypoint);
        }

        static Phonemizer.Note Note(string lyric) => new Phonemizer.Note { lyric = lyric, tone = 60, position = 0, duration = 480 };

        static Phonemizer.Result Process(Phonemizer phonemizer, string lyric = "a") {
            return phonemizer.Process(new[] { Note(lyric) }, null, null, null, null, Array.Empty<Phonemizer.Note>());
        }

        static string Phoneme(Phonemizer phonemizer, string lyric = "a") => Process(phonemizer, lyric).phonemes.Single().phoneme;

        [Fact]
        public void ReadsInfo() {
            var factory = LoadPackage(
                "export const info = { name: 'Test Phonemizer', tag: 'XX TEST', author: 'me', language: 'XX' };\n" +
                "export function process(notes) { return { phonemes: [] }; }");
            Assert.Equal("Test Phonemizer", factory.name);
            Assert.Equal("XX TEST", factory.tag);
            Assert.Equal("me", factory.author);
            Assert.Equal("XX", factory.language);
            Assert.Equal("js:test_pkg/main.js", factory.Id);
            Assert.Equal(factory.Id, factory.Create().Id);
        }

        [Fact]
        public void RequiresInfoAndProcess() {
            Assert.Throws<JsSandboxException>(() => LoadPackage("export function process(notes) { return { phonemes: [] }; }"));
            Assert.Throws<JsSandboxException>(() => LoadPackage("export const info = { name: 'Test' };\nexport function process() {}"));
            Assert.Throws<JsSandboxException>(() => LoadPackage(Info));
        }

        [Fact]
        public void ConvertsNotesAndResult() {
            var phonemizer = LoadPackage(Info + @"
export function process(notes, prev, next, prevNeighbour, nextNeighbour, prevNeighbours) {
    const n = notes[0];
    return { phonemes: [
        { phoneme: `${n.lyric}|${n.phoneticHint}|${n.tone}|${n.duration}|${prev}|${prevNeighbours.length}` },
        { phoneme: 'b', position: 120, expressions: [{ abbr: 'vel', value: 50 }] },
    ] };
}").Create();
            var result = Process(phonemizer, "x");
            Assert.Equal("x|null|60|480|null|0", result.phonemes[0].phoneme);
            Assert.Equal(0, result.phonemes[0].position);
            Assert.Equal(120, result.phonemes[1].position);
            Assert.Equal("vel", result.phonemes[1].expressions.Single().abbr);
            Assert.Equal(50, result.phonemes[1].expressions.Single().value);
        }

        [Fact]
        public void CreatesPackageFromTemplate() {
            var id = "test_template_" + Guid.NewGuid().ToString("N");
            var packageDir = JsPhonemizerFactory.CreatePackage(id);
            try {
                Assert.Throws<IOException>(() => JsPhonemizerFactory.CreatePackage(id));
                var factory = JsPhonemizerFactory.Load(PackageManager.GetEntrypoints(packageDir).Single());
                Assert.Equal($"js:{id}/main.js", factory.Id);
                Assert.Equal(id, factory.name);
                Assert.Equal(id.ToUpperInvariant().Replace('_', ' '), factory.tag);
                Assert.StartsWith("TEST TEMPLATE ", factory.tag);
                Assert.Equal("a", Phoneme(factory.Create(), "a"));
                Assert.True(File.Exists(Path.Combine(packageDir, "openutau.d.ts")));
            } finally {
                Directory.Delete(packageDir, true);
            }
        }

        [Fact]
        public void RejectsInvalidResult() {
            var phonemizer = LoadPackage(Info + "export function process() { return [{ phoneme: 'a' }]; }").Create();
            Assert.Throws<JsSandboxException>(() => Process(phonemizer));
            phonemizer = LoadPackage(Info + "export function process() { return { phonemes: [{ phoneme: 1 }] }; }").Create();
            Assert.Throws<JsSandboxException>(() => Process(phonemizer));
        }

        [Fact]
        public void EachPhonemizerHasItsOwnVm() {
            var factory = LoadPackage(Info + @"
globalThis.loads = (globalThis.loads ?? 0) + 1;
let calls = 0;
export function process() {
    calls++;
    return { phonemes: [{ phoneme: `${globalThis.loads}/${calls}` }] };
}");
            var a = factory.Create();
            var b = factory.Create();
            Assert.Equal("1/1", Phoneme(a));
            Assert.Equal("1/2", Phoneme(a));
            Assert.Equal("1/1", Phoneme(b));
        }

        [Fact]
        public void HasNoHostAccess() {
            var phonemizer = LoadPackage(Info + @"
export function process() {
    const names = ['System', 'importNamespace', 'require', 'fetch', 'XMLHttpRequest', 'process', 'host', 'clr'];
    return { phonemes: [{ phoneme: names.map(n => typeof globalThis[n]).join(',') }] };
}").Create();
            Assert.Equal(string.Join(",", Enumerable.Repeat("undefined", 8)), Phoneme(phonemizer));
        }

        [Fact]
        public void DisallowsStringCompilation() {
            var phonemizer = LoadPackage(Info + @"
export function process() {
    try { eval('1'); return { phonemes: [{ phoneme: 'eval' }] }; } catch (e) { }
    try { new Function('return 1')(); return { phonemes: [{ phoneme: 'Function' }] }; } catch (e) { }
    return { phonemes: [{ phoneme: 'ok' }] };
}").Create();
            Assert.Equal("ok", Phoneme(phonemizer));
        }

        [Fact]
        public void ImportsOnlyFromPackage() {
            Directory.CreateDirectory(Path.Combine(dir, "pkg", "lib"));
            File.WriteAllText(Path.Combine(dir, "pkg", "lib", "util.js"), "export const value = 'util';");
            File.WriteAllText(Path.Combine(dir, "outside.js"), "export const value = 'outside';");
            var phonemizer = LoadPackage(Info + "import { value } from './lib/util.js';\nexport function process() { return { phonemes: [{ phoneme: value }] }; }").Create();
            Assert.Equal("util", Phoneme(phonemizer));
            Assert.Throws<JsSandboxException>(() => LoadPackage(Info + "import { value } from '../outside.js';\nexport function process() { return { phonemes: [] }; }"));
        }

        [Fact]
        public void ReadsPackageText() {
            Directory.CreateDirectory(Path.Combine(dir, "pkg", "data"));
            File.WriteAllText(Path.Combine(dir, "pkg", "data", "dict.txt"), "あ=a\n");
            var phonemizer = LoadPackage(Info + @"
import { readText } from 'openutau';
const atLoad = readText('data/dict.txt').trim();
export function process() {
    return { phonemes: [{ phoneme: atLoad }, { phoneme: readText('./data/../data/dict.txt').trim() }] };
}").Create();
            Assert.Equal(new[] { "あ=a", "あ=a" }, Process(phonemizer).phonemes.Select(p => p.phoneme));
        }

        const string ReadTextErrors = @"
import { readText } from 'openutau';
export function process(notes) {
    try {
        readText(notes[0].lyric);
        return { phonemes: [{ phoneme: 'read' }] };
    } catch (e) {
        return { phonemes: [{ phoneme: `${e.name}: ${e.message}` }] };
    }
}";

        [Theory]
        [InlineData("../outside.txt", "Error: Path is outside of the package")]
        [InlineData("data/../../outside.txt", "Error: Path is outside of the package")]
        [InlineData("/outside.txt", "TypeError: Path must be relative to the package")]
        [InlineData("", "TypeError: Path must be relative to the package")]
        [InlineData("missing.txt", "Error: File not found")]
        [InlineData("data", "Error: File not found")]
        public void ReadTextErrorsAreCatchable(string path, string error) {
            Directory.CreateDirectory(Path.Combine(dir, "pkg", "data"));
            File.WriteAllText(Path.Combine(dir, "outside.txt"), "outside");
            var phonemizer = LoadPackage(Info + ReadTextErrors).Create();
            Assert.StartsWith(error, Phoneme(phonemizer, path));
        }

        [Fact]
        public void ReadTextRefusesLinks() {
            Directory.CreateDirectory(Path.Combine(dir, "pkg"));
            File.WriteAllText(Path.Combine(dir, "outside.txt"), "outside");
            try {
                File.CreateSymbolicLink(Path.Combine(dir, "pkg", "link.txt"), Path.Combine(dir, "outside.txt"));
                Directory.CreateSymbolicLink(Path.Combine(dir, "pkg", "linkdir"), dir);
            } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) {
                Assert.Skip($"Cannot create symbolic links: {e.Message}");
            }
            var phonemizer = LoadPackage(Info + ReadTextErrors).Create();
            Assert.StartsWith("Error: Path is a link", Phoneme(phonemizer, "link.txt"));
            Assert.StartsWith("Error: Path is a link", Phoneme(phonemizer, "linkdir/outside.txt"));
        }

        [Fact]
        public void ReadTextLimitsSize() {
            Directory.CreateDirectory(Path.Combine(dir, "pkg"));
            using (var stream = File.Create(Path.Combine(dir, "pkg", "big.txt"))) {
                stream.SetLength(JsSandbox.MaxReadSize + 1);
            }
            var phonemizer = LoadPackage(Info + ReadTextErrors).Create();
            Assert.StartsWith("Error: File is larger than 64 MB", Phoneme(phonemizer, "big.txt"));
        }

        [Fact]
        public void ReportsScriptErrors() {
            var phonemizer = LoadPackage(Info + "export function process() {\n    throw new Error('boom');\n}").Create();
            var e = Assert.Throws<JsSandboxException>(() => Process(phonemizer));
            Assert.Contains("boom", e.Message);
            Assert.Contains("main.js", e.Message);
        }

        [Fact]
        public void ReportsSyntaxErrors() {
            var e = Assert.Throws<JsSandboxException>(() => LoadPackage(Info + "export function process( {"));
            Assert.Contains("main.js:2:", e.Message);
            Assert.DoesNotContain(dir, e.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void LimitsRecursion() {
            var phonemizer = LoadPackage(Info + "function f(n) { return 1 + f(n + 1); }\nexport function process() { f(0); }").Create();
            Assert.Throws<JsSandboxException>(() => Process(phonemizer));
        }

        [Fact]
        public void LimitsMemory() {
            var phonemizer = LoadPackage(Info + "export function process() { const a = []; while (true) { a.push(new Array(1000000).fill(1)); } }").Create();
            Assert.Throws<JsSandboxException>(() => Process(phonemizer));
        }

        [Fact]
        public void LimitsTime() {
            var phonemizer = LoadPackage(Info + "export function process() { while (true) { } }").Create();
            var e = Assert.Throws<JsSandboxException>(() => Process(phonemizer));
            Assert.IsType<TimeoutException>(e.InnerException);
        }
    }
}
