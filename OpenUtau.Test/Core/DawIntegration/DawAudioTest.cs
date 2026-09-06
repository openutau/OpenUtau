using System;
using System.Linq;
using OpenUtau.Core.SignalChain;
using OpenUtau.Core.Ustx;
using Xunit;

namespace OpenUtau.Core.DawIntegration {
    /// <summary>Data-plane contract: index space, PCM encoding, hashing and frame headers (§5.2, §6.1).</summary>
    [Collection(DawIntegrationCollection.Name)]
    public class DawAudioTest {
        /// <summary>Writes each slot's absolute sample index, so any extracted window describes itself.</summary>
        private sealed class RampSource : ISignalSource {
            public bool Ready = true;

            public bool IsReady(int position, int count) => Ready;

            public int Mix(int position, float[] buffer, int index, int count) {
                for (int i = 0; i < count; i++) {
                    buffer[index + i] += position + i;
                }
                return position + count;
            }
        }

        private static UProject NewProject() {
            // Defaults are 4/4 at 120 bpm with 480 ticks per beat, so 480 ticks is exactly 500 ms.
            var project = new UProject();
            project.timeAxis.BuildSegments(project);
            return project;
        }

        [Fact]
        public void EngineFormatIsFixed() {
            // PROTOCOL.md §1: never negotiated, mirrors PlaybackManager.
            Assert.Equal(44100, DawAudio.SampleRate);
            Assert.Equal(2, DawAudio.Channels);
        }

        [Fact]
        public void MsMapsToInterleavedStereoIndex() {
            Assert.Equal(0, DawAudio.MsToInterleavedIndex(0));
            Assert.Equal(0, DawAudio.MsToInterleavedIndex(-25));
            Assert.Equal(88200, DawAudio.MsToInterleavedIndex(1000));
            Assert.Equal(44100, DawAudio.MsToInterleavedIndex(500));
        }

        [Fact]
        public void IndexIsAlwaysFrameAligned() {
            // An odd index would put the left channel in the right channel's slot.
            for (double ms = 0; ms < 5; ms += 0.017) {
                Assert.Equal(0, DawAudio.MsToInterleavedIndex(ms) % DawAudio.Channels);
            }
        }

        [Fact]
        public void PcmRoundTrips() {
            var samples = new[] { 0f, 1f, -1f, 0.5f, -0.25f, float.Epsilon };

            byte[] pcm = DawAudio.ToPcmBytes(samples);

            Assert.Equal(samples.Length * sizeof(float), pcm.Length);
            Assert.Equal(samples, DawAudio.FromPcmBytes(pcm));
        }

        [Fact]
        public void PartialFloatIsRejected() {
            Assert.Throws<DawProtocolException>(() => DawAudio.FromPcmBytes(new byte[7]));
        }

        [Fact]
        public void HashIsStableAndContentAddressed() {
            byte[] one = DawAudio.ToPcmBytes(new[] { 1f, 2f, 3f });
            byte[] same = DawAudio.ToPcmBytes(new[] { 1f, 2f, 3f });
            byte[] other = DawAudio.ToPcmBytes(new[] { 1f, 2f, 4f });

            Assert.Equal(DawAudio.Hash(one), DawAudio.Hash(same));
            Assert.NotEqual(DawAudio.Hash(one), DawAudio.Hash(other));
        }

        [Fact]
        public void HashMatchesTheReferenceVectorForEmptyInput() {
            // Pins this side to standard XXH64 with seed 0 — the published vector, not something
            // K4os.Hash.xxHash agrees with only because it computed both numbers. The plugin
            // hashes with its own library; if either stops being the reference algorithm, every
            // part looks missing forever and nothing else in the protocol reports it.
            Assert.Equal(0xEF46DB3751D8E999UL, DawAudio.Hash(Array.Empty<byte>()));
        }

        [Fact]
        public void SharedPcmVectorHashesToTheValueThePluginExpects() {
            // Interleaved little-endian float32 is the wire encoding (§6.1), so the bytes are
            // spelled out rather than round-tripped: this is the one assertion that would catch a
            // big-endian or non-IEEE encoding, which no same-side round trip can.
            byte[] pcm = DawAudio.ToPcmBytes(new[] { 1f, -1f, 0.5f });

            Assert.Equal(new byte[] {
                0x00, 0x00, 0x80, 0x3F, 0x00, 0x00, 0x80, 0xBF, 0x00, 0x00, 0x00, 0x3F,
            }, pcm);
            // The plugin's tests/test_hash.cpp asserts this same constant against its own XXH64.
            // Two suites, two libraries, one number: neither repository can drift alone.
            Assert.Equal(12033956788804169010UL, DawAudio.Hash(pcm));
            Assert.Equal("12033956788804169010", DawAudio.FormatHash(DawAudio.Hash(pcm)));
        }

        [Fact]
        public void HashSerializesAsDecimalStringBeyondDoublePrecision() {
            // §5.2: a JSON number would round this; the wire format is a string for that reason.
            string text = DawAudio.FormatHash(ulong.MaxValue);

            Assert.Equal("18446744073709551615", text);
            Assert.True(DawAudio.TryParseHash(text, out ulong parsed));
            Assert.Equal(ulong.MaxValue, parsed);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("-1")]
        [InlineData("0x1f")]
        [InlineData(" 12")]
        [InlineData("18446744073709551616")]
        public void MalformedHashesAreRefused(string? text) {
            Assert.False(DawAudio.TryParseHash(text, out _));
        }

        [Fact]
        public void FrameHeaderRoundTrips() {
            byte[] header = DawAudio.BuildFrameHeader("13507256038857166760", 4096);

            string line = System.Text.Encoding.UTF8.GetString(header);
            Assert.Equal("audio 13507256038857166760 4096\n", line);

            Assert.True(DawAudio.TryParseFrameHeader(line.TrimEnd('\n'), out string hash, out int length));
            Assert.Equal("13507256038857166760", hash);
            Assert.Equal(4096, length);
        }

        [Theory]
        [InlineData("notaudio 1 2")]
        [InlineData("audio 1")]
        [InlineData("audio 1 2 3")]
        [InlineData("audio abc 2")]
        [InlineData("audio 1 -2")]
        [InlineData("audio 1 abc")]
        [InlineData("audio 1 268435457")] // MaxFrameBytes + 1: a hostile length must not drive allocation.
        public void MalformedFrameHeadersAreRefused(string line) {
            // A header we cannot trust would desynchronize the stream, so it must never parse.
            Assert.False(DawAudio.TryParseFrameHeader(line, out _, out _));
        }

        [Fact]
        public void FrameHeaderAcceptsTheLargestLegalLength() {
            Assert.True(DawAudio.TryParseFrameHeader($"audio 1 {DawAudio.MaxFrameBytes}", out _, out int length));
            Assert.Equal(DawAudio.MaxFrameBytes, length);
        }

        [Fact]
        public void ControlLinesAreNotMistakenForFrames() {
            Assert.False(DawAudio.IsFrameHeader("notification:ping {}"));
            Assert.False(DawAudio.IsFrameHeader("close"));
            Assert.True(DawAudio.IsFrameHeader("audio 1 2"));
        }

        [Fact]
        public void ExtractionReadsThePartsOwnAbsoluteWindow() {
            var project = NewProject();
            var part = new UVoicePart { trackNo = 0, position = 480, duration = 480 };
            part.SetMix(new RampSource());

            Assert.True(DawAudio.TryExtractPart(project, part, out float[] samples));

            // 480 ticks in, 480 ticks long: 500 ms to 1000 ms of the project timeline.
            // §6.1: extraction applies the pre-fader output trim (√0.5), so the ramp values
            // come out scaled.
            float trim = MathF.Sqrt(0.5f);
            int start = DawAudio.MsToInterleavedIndex(500);
            Assert.Equal(DawAudio.MsToInterleavedIndex(1000) - start, samples.Length);
            Assert.Equal(start * trim, samples[0]);
            Assert.Equal((start + samples.Length - 1) * trim, samples[^1]);
        }

        [Fact]
        public void ExtractionStartsFromAZeroedBuffer() {
            // ISignalSource.Mix adds rather than assigns, so a reused buffer would double the signal.
            var project = NewProject();
            var part = new UVoicePart { trackNo = 0, position = 0, duration = 480 };
            part.SetMix(new RampSource());

            Assert.True(DawAudio.TryExtractPart(project, part, out float[] first));
            Assert.True(DawAudio.TryExtractPart(project, part, out float[] second));

            Assert.Equal(first, second);
        }

        [Fact]
        public void UnrenderedPartsAreRefused() {
            var project = NewProject();
            var empty = new UVoicePart { trackNo = 0, position = 0, duration = 480 };
            var unfinished = new UVoicePart { trackNo = 0, position = 0, duration = 480 };
            unfinished.SetMix(new RampSource { Ready = false });
            var zeroLength = new UVoicePart { trackNo = 0, position = 0, duration = 0 };
            zeroLength.SetMix(new RampSource());

            Assert.False(DawAudio.TryExtractPart(project, empty, out _));
            Assert.False(DawAudio.TryExtractPart(project, unfinished, out _));
            Assert.False(DawAudio.TryExtractPart(project, zeroLength, out _));
        }

        [Fact]
        public void ExtractionMatchesTheEngineWaveSource() {
            var project = NewProject();
            var samples = Enumerable.Range(0, 44100 * 2).Select(i => i / 100000f).ToArray();
            // WaveSource is what RenderEngine hands to a part, addressed in absolute project ms.
            var source = new WaveSource(0, 500, 0, 2);
            source.SetSamples(samples);
            var part = new UVoicePart { trackNo = 0, position = 0, duration = 480 };
            part.SetMix(source);

            Assert.True(DawAudio.TryExtractPart(project, part, out float[] extracted));

            Assert.Equal(DawAudio.MsToInterleavedIndex(500), extracted.Length);
            // §6.1: extraction applies the pre-fader output trim (√0.5), so what is served
            // matches the level OpenUtau's constant-power pan produces per channel.
            float trim = MathF.Sqrt(0.5f);
            Assert.Equal(samples.Take(extracted.Length).Select(s => s * trim), extracted);
        }
    }
}
