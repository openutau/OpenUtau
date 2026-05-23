using System;
using System.IO;
using Concentus.Oggfile;
using Concentus.Structs;
using NAudio.Wave;

namespace OpenUtau.Core.Format {
    // Preliminary blocking Opus reader.
    public class OpusOggWaveReader : WaveStream {
        WaveFormat waveFormat;
        MemoryStream oggStream;
        OpusDecoder decoder;
        OpusOggReadStream decodeStream;
        byte[] wavData;

        public OpusOggWaveReader(string oggFile) {
            using (FileStream fileStream = new FileStream(oggFile, FileMode.Open, FileAccess.Read)) {
                oggStream = new MemoryStream();
                fileStream.CopyTo(oggStream);
            }
            oggStream.Seek(0, SeekOrigin.Begin);
            waveFormat = new WaveFormat(48000, 16, 2);
            decoder = new OpusDecoder(48000, 2);
            decodeStream = new OpusOggReadStream(decoder, oggStream);
        }

        byte[] Decode() {
            using (var wavStream = new MemoryStream()) {
                var decoder = new OpusDecoder(48000, 2);
                var oggIn = new OpusOggReadStream(decoder, oggStream);
                while (oggIn.HasNextPacket) {
                    short[] packet = oggIn.DecodeNextPacket();
                    if (packet != null) {
                        byte[] binary = ShortsToBytes(packet);
                        wavStream.Write(binary, 0, binary.Length);
                    }
                }
                return wavStream.ToArray();
            }
        }

        public override WaveFormat WaveFormat => waveFormat;

        public override long Length => wavData.LongLength;

        public override TimeSpan TotalTime => decodeStream.TotalTime;

        public override long Position { get; set; }

        public override int Read(byte[] buffer, int offset, int count) {
            if (wavData == null) {
                wavData = Decode();
            }
            int n = (int)Math.Min(wavData.Length - Position, count);
            Array.Copy(wavData, Position, buffer, offset, n);
            Position += n;
            return n;
        }

        static byte[] ShortsToBytes(short[] input) {
            byte[] output = new byte[input.Length * sizeof(short) / sizeof(byte)];
            for (int i = 0; i < input.Length; ++i) {
                output[i * 2] = (byte)(input[i] & 0xFF);
                output[i * 2 + 1] = (byte)((input[i] >> 8) & 0xFF);
            }
            return output;
        }

        protected override void Dispose(bool disposing) {
            oggStream.Dispose();
            base.Dispose(disposing);
        }
    }
}
