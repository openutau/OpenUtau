using System;
using System.IO;
using Serilog;
using NAudio.Wave;
using SharpJaad.AAC;
using SharpJaad.ADTS;

namespace OpenUtau.Core.Format {
    public class AACWaveReader : WaveStream {
        private readonly WaveFormat waveFormat = new(48000, 16, 2);
        private readonly MemoryStream aacStream = new();
        private readonly ADTSDemultiplexer adts;
        private readonly Decoder decoder;
        private byte[] wavData = [];

        public AACWaveReader(string aacFile) {
            using (FileStream fileStream = new FileStream(aacFile, FileMode.Open, FileAccess.Read)) {
                fileStream.CopyTo(aacStream);
            }
            aacStream.Seek(0, SeekOrigin.Begin);
            try {
                adts = new ADTSDemultiplexer(aacStream);
            } catch (IOException e) {
                Log.Error("AAC decoder: no ADTS header found", e);
                throw;
            }
            decoder = new Decoder(adts.GetDecoderSpecificInfo());
        }

        private byte[] Decode() {
            SharpJaad.WAV.WaveFileWriter? wavWriter = null;
            using var wavStream = new MemoryStream();
            try {
                byte[] b;
                SampleBuffer buf = new();
                while (true) {
                    try {
                        b = adts.ReadNextFrame();
                    } catch (EndOfStreamException) {
                        break;
                    }
                    decoder.DecodeFrame(b, buf);
                }
                wavWriter ??= new(wavStream, buf.SampleRate, buf.Channels, buf.BitsPerSample);
                wavWriter.Write(buf.Data);
                return wavStream.ToArray();
            } finally {
                wavWriter?.Close();
            }
        }

        public override WaveFormat? WaveFormat => waveFormat;
        public override long Length => wavData != null ? wavData.LongLength : 0L;
        public override long Position { get; set; }
        public override int Read(byte[] buffer, int offset, int count) {
            wavData ??= Decode();
            int n = (int)Math.Min(wavData.Length - Position, count);
            Array.Copy(wavData, Position, buffer, offset, n);
            Position += n;
            return n;
        }

        protected override void Dispose(bool disposing) {
            if (disposing) {
                aacStream.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
