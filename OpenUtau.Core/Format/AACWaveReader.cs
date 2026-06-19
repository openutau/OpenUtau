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
            var track = (AudioTrack)tracks[0];
            waveFormat = new WaveFormat(track.GetSampleRate(), 16, track.GetChannelCount());
            wavData = Decode(track);
        }

        private static byte[] Decode(AudioTrack track) {
            var decoder = new Decoder(track.GetDecoderSpecificInfo());
            using var pcmStream = new MemoryStream();
            while (track.HasMoreFrames()) {
                var frame = track.ReadNextFrame();
                // Fresh buffer per frame: SampleBuffer.BigEndian starts true,
                // and SetData() doesn't reset it, so reusing one buffer across
                // frames would skip the LE swap after the first frame.
                var buf = new SampleBuffer();
                try {
                    decoder.DecodeFrame(frame.GetData(), buf);
                } catch (AACException e) {
                    // Ignoring the error just kind of works for some reason
                    Serilog.Log.Error($"AACException on DecodeFrame caught (continuing): {e.Message}");
                }
                buf.SetBigEndian(false);
                pcmStream.Write(buf.Data, 0, buf.Data.Length);
            }
            return pcmStream.ToArray();
        }

        public override WaveFormat WaveFormat => waveFormat;
        public override long Length => wavData.LongLength;
        public override long Position {
            get => position;
            set => position = value;
        }

        public override int Read(byte[] buffer, int offset, int count) {
            int n = (int)Math.Min(wavData.Length - position, count);
            Array.Copy(wavData, position, buffer, offset, n);
            position += n;
            return n;
        }

        protected override void Dispose(bool disposing) {
            base.Dispose(disposing);
        }
    }
}
