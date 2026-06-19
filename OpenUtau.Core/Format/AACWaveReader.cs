using System;
using System.IO;
using NAudio.Wave;
using SharpJaad.AAC;
using SharpJaad.MP4;
using SharpJaad.MP4.API;

namespace OpenUtau.Core.Format {
    public class AACWaveReader : WaveStream {
        private readonly WaveFormat waveFormat;
        private readonly byte[] wavData;
        private long position;

        public AACWaveReader(string aacFile) {
            using var fileStream = File.OpenRead(aacFile);
            var container = new MP4Container(fileStream);
            var movie = container.GetMovie();
            var tracks = movie.GetTracks(AudioTrack.AudioCodec.AAC);
            if (tracks.Count == 0)
                throw new Exception("M4A file does not contain an AAC audio track.");
            if (tracks.Count > 1)
                Serilog.Log.Warning($"M4A file has {tracks.Count} AAC tracks; using the first one.");
            var track = (AudioTrack)tracks[0];
            if (track.GetProtection() != null)
                throw new Exception("M4A file is DRM-protected and cannot be decoded.");
            var (data, sampleRate, channels, bitsPerSample) = Decode(track);
            if (data.Length == 0)
                throw new Exception("M4A file produced no audio data; all frames failed to decode.");
            waveFormat = new WaveFormat(
                sampleRate > 0 ? sampleRate : track.GetSampleRate(),
                bitsPerSample > 0 ? bitsPerSample : 16,
                channels > 0 ? channels : track.GetChannelCount()
            );
            wavData = data;
        }

        private static (byte[] data, int sampleRate, int channels, int bitsPerSample) Decode(AudioTrack track) {
            var decoderSpecificInfo = track.GetDecoderSpecificInfo() ?? throw new Exception("M4A audio track is missing decoder configuration (unsupported codec variant).");
            Decoder decoder;
            try {
                decoder = new Decoder(decoderSpecificInfo);
            } catch (AACException e) {
                throw new Exception($"Unsupported AAC profile in M4A file: {e.Message}");
            } catch (Exception e) {
                throw new Exception($"Failed to initialize AAC decoder: {e.Message}", e);
            }
            using var pcmStream = new MemoryStream();
            int sampleRate = 0, channels = 0, bitsPerSample = 0;
            while (track.HasMoreFrames()) {
                var frame = track.ReadNextFrame();
                // Fresh buffer per frame: SampleBuffer.BigEndian starts true,
                // and SetData() doesn't reset it, so reusing one buffer across
                // frames would skip the LE swap after the first frame.
                var buf = new SampleBuffer();
                try {
                    decoder.DecodeFrame(frame.GetData(), buf);
                } catch (AACException e) {
                    Serilog.Log.Error($"AACException on DecodeFrame caught (continuing): {e.Message}");
                    continue;
                }
                if (buf.Data.Length == 0) continue;
                buf.SetBigEndian(false);
                pcmStream.Write(buf.Data, 0, buf.Data.Length);
                sampleRate = buf.SampleRate;
                channels = buf.Channels;
                bitsPerSample = buf.BitsPerSample;
            }
            return (pcmStream.ToArray(), sampleRate, channels, bitsPerSample);
        }

        public override WaveFormat WaveFormat => waveFormat;
        public override long Length => wavData.LongLength;
        public override long Position {
            get => position;
            set => position = value;
        }

        public override int Read(byte[] buffer, int offset, int count) {
            int n = (int)Math.Max(0, Math.Min(wavData.Length - position, count));
            Array.Copy(wavData, position, buffer, offset, n);
            position += n;
            return n;
        }

        protected override void Dispose(bool disposing) {
            base.Dispose(disposing);
        }
    }
}
