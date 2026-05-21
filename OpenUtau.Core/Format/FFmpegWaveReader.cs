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
            memStream.Seek(0, SeekOrigin.Begin);
            return memStream;
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
