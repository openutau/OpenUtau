using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using NAudio.Wave;

namespace OpenUtau.Core.Format {
    public class FFmpegWaveReader(string filepath) : WaveStream {
        private readonly WaveFileReader inner = new(DecodeToMemory(filepath));
        private static MemoryStream DecodeToMemory(string filepath) {
            using var proc = new Process {
                StartInfo = new ProcessStartInfo {
                    FileName = "ffmpeg",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                }
            };
            proc.StartInfo.ArgumentList.Add("-i");
            proc.StartInfo.ArgumentList.Add(filepath);
            proc.StartInfo.ArgumentList.Add("-f");
            proc.StartInfo.ArgumentList.Add("wav");
            proc.StartInfo.ArgumentList.Add("pipe:1");
            var stderrLog = new StringBuilder();
            proc.ErrorDataReceived += (_, e) => { if (e.Data != null) stderrLog.AppendLine(e.Data); };
            try {
                proc.Start();
            } catch (Exception) {
                throw new Exception("ffmpeg not found. Install ffmpeg and ensure it is on PATH.");
            }
            proc.BeginErrorReadLine();
            var memStream = new MemoryStream();
            proc.StandardOutput.BaseStream.CopyTo(memStream);
            proc.WaitForExit();
            if (proc.ExitCode != 0) {
                throw new Exception($"ffmpeg failed to decode '{Path.GetFileName(filepath)}'.\n{stderrLog}");
            }
            // ffmpeg cannot seek back to fix WAV chunk sizes when writing to a non-seekable
            // pipe, leaving them as 0 or 0xFFFFFFFF. Patch them now that the full size is known.
            PatchWavHeader(memStream);
            return memStream;
        }

        private static void PatchWavHeader(MemoryStream stream) {
            int total = (int)stream.Length;
            // Fix RIFF chunk size at offset 4
            stream.Position = 4;
            stream.Write(BitConverter.GetBytes(total - 8), 0, 4);
            // Scan sub-chunks starting after "RIFF" + size + "WAVE"
            stream.Position = 12;
            var id = new byte[4];
            var sz = new byte[4];
            while (stream.Position <= stream.Length - 8) {
                stream.Read(id, 0, 4);
                stream.Read(sz, 0, 4);
                if (id[0] == 'd' && id[1] == 'a' && id[2] == 't' && id[3] == 'a') {
                    stream.Position -= 4;
                    stream.Write(BitConverter.GetBytes(total - (int)stream.Position - 4), 0, 4);
                    break;
                }
                int chunkSize = BitConverter.ToInt32(sz, 0);
                if (chunkSize < 0) break;
                stream.Position += chunkSize;
            }
            stream.Position = 0;
        }

        public override WaveFormat WaveFormat => inner.WaveFormat;
        public override long Length => inner.Length;
        public override long Position {
            get => inner.Position;
            set => inner.Position = value;
        }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        protected override void Dispose(bool disposing) {
            if (disposing) {
                inner.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
