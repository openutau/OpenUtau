using System;
using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using K4os.Hash.xxHash;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Core.DawIntegration {
    /// <summary>
    /// Data-plane helpers: part audio extraction, XXH64 hashing and binary frame framing.
    /// See PROTOCOL.md §5.2 and §6.1 (<c>getAudio</c>).
    /// </summary>
    public static class DawAudio {
        /// <summary>Hard engine limit, mirrors <c>PlaybackManager.cs:35</c>. Never negotiated (PROTOCOL.md §1).</summary>
        public const int SampleRate = 44100;
        public const int Channels = 2;

        /// <summary>Data-plane frame header prefix. A received line starting with this is binary, not control.</summary>
        public const string FramePrefix = "audio ";

        /// <summary>
        /// Upper bound on a single data-plane frame payload (256 MiB = 44.1 kHz stereo float32,
        /// roughly 12.7 minutes of audio). Frames are length-prefixed by the peer, so before this
        /// bound a hostile or corrupted header could make the receiver allocate up to 2 GiB
        /// (PROTOCOL.md §6.1). Parts larger than this cannot be shipped over the wire in v1.
        /// </summary>
        public const int MaxFrameBytes = 1 << 28;

        /// <summary>
        /// Converts project time to an index into OpenUtau's interleaved stereo sample space.
        /// The signal chain is addressed in absolute project samples, two floats per frame
        /// (see <c>WaveSource.offset</c>).
        /// </summary>
        public static int MsToInterleavedIndex(double ms) {
            if (ms <= 0) {
                return 0;
            }
            return (int)(ms * SampleRate / 1000) * Channels;
        }

        /// <summary>
        /// Extracts the rendered audio covering a part's own tick range.
        /// </summary>
        /// <remarks>
        /// Voice parts only. <see cref="UWavePart"/> audio is out of v1 scope: it is user-supplied
        /// material the DAW can import directly, and its source is produced by
        /// <c>UWavePart.TrimSamples</c> rather than the render pipeline.
        /// </remarks>
        /// <returns>
        /// False when the part carries no mix yet, or when the renderer has not finished the
        /// window. Callers must skip such parts rather than shipping partial audio, because
        /// <see cref="SignalChain.ISignalSource.Mix"/> would leave the gap silent and the
        /// resulting hash would be wrong for the finished audio.
        /// </returns>
        public static bool TryExtractPart(UProject project, UVoicePart part, out float[] samples) {
            samples = Array.Empty<float>();
            var mix = part.Mix;
            if (mix == null) {
                return false;
            }
            int start = MsToInterleavedIndex(project.timeAxis.TickPosToMsPos(part.position));
            int end = MsToInterleavedIndex(project.timeAxis.TickPosToMsPos(part.End));
            int count = end - start;
            if (count <= 0) {
                return false;
            }
            if (!mix.IsReady(start, count)) {
                return false;
            }
            // ISignalSource.Mix adds into the buffer, so it must start zeroed.
            var buffer = new float[count];
            mix.Mix(start, buffer, 0, count);
            // §6.1 pre-fader output trim: OpenUtau pans constant-power, so a mix that
            // bypasses panning sits a systematic 3 dB above what the performance was tuned
            // against. Scale by cos(π/4) before hashing and serving; the trim is not mixer
            // state and never follows volume, pan or muted.
            float trim = MathF.Sqrt(0.5f);
            for (int i = 0; i < count; i++) {
                buffer[i] *= trim;
            }
            samples = buffer;
            return true;
        }

        /// <summary>Serializes samples as raw little-endian float32, the fixed wire payload (PROTOCOL.md §6.1).</summary>
        public static byte[] ToPcmBytes(float[] samples) {
            var bytes = new byte[samples.Length * sizeof(float)];
            if (BitConverter.IsLittleEndian) {
                Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
            } else {
                for (int i = 0; i < samples.Length; i++) {
                    BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * sizeof(float)), samples[i]);
                }
            }
            return bytes;
        }

        /// <summary>Reads raw little-endian float32 back into samples. Used by tests and conformance tooling.</summary>
        public static float[] FromPcmBytes(byte[] bytes) {
            if (bytes.Length % sizeof(float) != 0) {
                throw new DawProtocolException($"PCM payload of {bytes.Length} bytes is not a whole number of float32 samples.");
            }
            var samples = new float[bytes.Length / sizeof(float)];
            if (BitConverter.IsLittleEndian) {
                Buffer.BlockCopy(bytes, 0, samples, 0, bytes.Length);
            } else {
                for (int i = 0; i < samples.Length; i++) {
                    samples[i] = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(i * sizeof(float)));
                }
            }
            return samples;
        }

        public static ulong Hash(byte[] pcm) => XXH64.DigestOf(pcm);

        /// <summary>
        /// Hashes are always decimal strings on the wire — a 64-bit value exceeds the 2^53
        /// safe-integer range of JSON number parsers (PROTOCOL.md §5.2).
        /// </summary>
        public static string FormatHash(ulong hash) => hash.ToString(CultureInfo.InvariantCulture);

        public static bool TryParseHash(string? text, out ulong hash) {
            return ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out hash);
        }

        /// <summary>Builds the <c>audio &lt;hash&gt; &lt;length&gt;\n</c> header. The payload follows verbatim.</summary>
        public static byte[] BuildFrameHeader(string hash, int length) {
            return Encoding.UTF8.GetBytes($"{FramePrefix}{hash} {length.ToString(CultureInfo.InvariantCulture)}\n");
        }

        public static bool IsFrameHeader(string line) => line.StartsWith(FramePrefix, StringComparison.Ordinal);

        /// <summary>
        /// Parses a data-plane header line (without the trailing newline). Rejects negative,
        /// unparseable or oversized lengths so a malformed header cannot desynchronize the stream
        /// and a hostile length cannot drive the receiver into a huge allocation (§8).
        /// </summary>
        public static bool TryParseFrameHeader(string line, out string hash, out int length) {
            hash = string.Empty;
            length = 0;
            if (!IsFrameHeader(line)) {
                return false;
            }
            var parts = line.Substring(FramePrefix.Length).Split(' ');
            if (parts.Length != 2) {
                return false;
            }
            if (!TryParseHash(parts[0], out _)) {
                return false;
            }
            if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out length)) {
                return false;
            }
            if (length > MaxFrameBytes) {
                return false;
            }
            hash = parts[0];
            return true;
        }
    }
}
