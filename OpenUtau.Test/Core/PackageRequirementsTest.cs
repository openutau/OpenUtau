using System;
using System.IO;
using OpenUtau.Classic;
using OpenUtau.Classic.Hifisampler;
using OpenUtau.Core.Analysis;
using OpenUtau.Core.DiffSinger;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Xunit;

namespace OpenUtau.Core {
    public class PackageRequirementsTest {
        [Fact]
        public void DiffSingerFallsBackToPcNsfWhenNamedVocoderMissing() {
            string dir = Path.Combine(Path.GetTempPath(), $"OpenUtau.VocoderFallback.{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);
            try {
                File.WriteAllText(Path.Combine(dir, VoicebankLoader.kCharTxt), "name=fallback-test\n");
                File.WriteAllText(Path.Combine(dir, VoicebankLoader.kDsconfigYaml),
                    "vocoder: nsf_hifigan\nacoustic: acoustic.onnx\nphonemes: phonemes.txt\n");
                var voicebank = new Voicebank {
                    File = Path.Combine(dir, VoicebankLoader.kCharTxt),
                    BasePath = dir,
                };
                VoicebankLoader.LoadVoicebank(voicebank);
                var singer = new DiffSingerSinger(voicebank);
                // The universal pc-nsf-hifigan must be absent for the fallback to raise.
                var pc = HifiVocoder.PackageId;
                if (PackageManager.Inst.GetInstalledPath(pc) != null ||
                    PackageManager.Inst.GetInstalledPath("nsf_hifigan") != null) {
                    return; // Environment already has a vocoder; skipping keeps the assertion meaningful.
                }
                var ex = Assert.Throws<MissingPackageException>(() => singer.getVocoder());
                Assert.Contains(pc, MissingPackageException.Collect(ex));
            } finally {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void EnginesNeedTheirPackages() {
            Assert.Equal(new[] { Hnsep.PackageId },
                PackageRequirements.For(new URenderSettings { renderer = Renderers.WORLDLINE_R11 }));
            Assert.Equal(new[] { HifiVocoder.PackageId },
                PackageRequirements.For(new URenderSettings { renderer = Renderers.WORLDLINE_R2 }));
            Assert.Equal(new[] { HifiVocoder.PackageId, Hnsep.PackageId },
                PackageRequirements.For(new URenderSettings { renderer = Renderers.CLASSIC, resampler = "hifisampler" }));
            Assert.Empty(PackageRequirements.For(new URenderSettings { renderer = Renderers.CLASSIC, resampler = "worldline" }));
            Assert.Empty(PackageRequirements.For(new URenderSettings { renderer = Renderers.WORLDLINE_R }));
        }

        [Fact]
        public void EnginePackagesAreInstallable() {
            Assert.Contains(Hnsep.PackageId, PackageRequirements.Installable);
            Assert.Contains(HifiVocoder.PackageId, PackageRequirements.Installable);
            Assert.Contains("game", PackageRequirements.Installable);
            Assert.Contains("rmvpe", PackageRequirements.Installable);
        }

        [Fact]
        public void CollectFindsMissingPackagesAnywhere() {
            var e = new AggregateException(
                new MissingPackageException("a"),
                new AggregateException(new InvalidOperationException("other", new MissingPackageException("b"))),
                // Rewrapped by a message that keeps only the translatable message and replaces.
                new MessageCustomizableException("Failed", "<translate:errors.failed.render>", new MissingPackageException("c", "a")),
                new MessageCustomizableException("Failed", "<translate:errors.failed.render>", new Exception("x", new MissingPackageException("d"))));
            Assert.Equal(new[] { "a", "b", "c", "d" }, MissingPackageException.Collect(e));
            Assert.Empty(MissingPackageException.Collect(new InvalidOperationException("other")));
            Assert.Empty(MissingPackageException.Collect(null));
        }
    }
}
