using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NAudio.Wave;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Classic {
    public class OtoFrq {
        public double[] toneDiffFix = new double[0];
        public double[] toneDiffStretch = new double[0];
        public int hopSize;
        public bool loaded = false;

        public OtoFrq(UOto oto, Dictionary<string, IFrqFiles> dict) {
            if (!dict.TryGetValue(oto.File, out IFrqFiles? frq)) {
                Load(oto.File, out frq);
                if (frq != null) {
                    dict.Add(oto.File, frq);
                }
            }

            if(frq != null) {
                hopSize = frq.hopSize;
                int offset = ConvertMsToFrqLength(frq, oto.Offset);
                int consonant = ConvertMsToFrqLength(frq, oto.Offset + oto.Consonant);
                int cutoff = oto.Cutoff < 0 ?
                    ConvertMsToFrqLength(frq, oto.Offset - oto.Cutoff)
                    : frq.f0.Length - ConvertMsToFrqLength(frq, oto.Cutoff);
                var completionF0 = Completion(frq.f0);
                var averageTone = MusicMath.FreqToTone(frq.averageF0);
                toneDiffFix = completionF0.Skip(offset).Take(consonant - offset).Select(f => MusicMath.FreqToTone(f) - averageTone).ToArray();
                toneDiffStretch = completionF0.Skip(consonant).Take(cutoff - consonant).Select(f => MusicMath.FreqToTone(f) - averageTone).ToArray();

                loaded = true;
            }
        }

        private void Load(string otoPath, out IFrqFiles? frqFile) {
            var frq = new Frq();
            if (frq.Load(otoPath)) {
                frqFile = frq;
                return;
            }
            var mrq = new Mrq();
            if (mrq.Load(otoPath)) {
                frqFile = mrq;
                return;
            }
            // Please write a code to read other frequency files!

            frqFile = null;
        }

        private int ConvertMsToFrqLength(IFrqFiles frq, double lengthMs) {
            return (int)Math.Floor(lengthMs * frq.wavSampleRate / 1000 / frq.hopSize);
        }

        /// <summary>
        /// When a pitch is out of tune, it is complemented by the preceding and following pitches.
        /// </summary>
        private double[] Completion(double[] frqs) {
            var list = new List<double>();
            for (int i = 0; i < frqs.Length; i++) {
                if (frqs[i] <= 60) {
                    int min = i - 1;
                    double minFrq = 0;
                    while (min >= 0) {
                        if (frqs[min] > 60) {
                            minFrq = frqs[min];
                            break;
                        }
                        min--;
                    }
                    int max = i + 1;
                    double maxFrq = 0;
                    while (max < frqs.Length) {
                        if (frqs[max] > 60) {
                            maxFrq = frqs[max];
                            break;
                        }
                        max++;
                    }
                    if (minFrq <= 60) {
                        list.Add(maxFrq);
                    } else if (maxFrq <= 60) {
                        list.Add(minFrq);
                    } else {
                        list.Add(MusicMath.Linear(min, max, minFrq, maxFrq, i));
                    }
                } else {
                    list.Add(frqs[i]);
                }
            }
            return list.ToArray();
        }
    }

    public interface IFrqFiles {
        public int hopSize { get; }
        public double averageF0 { get; }
        public double[] f0 { get; }
        public int wavSampleRate { get; set; }

        /// <summary>
        /// If the wav path is null (machine learning voicebank), return false.
        /// </summary>
        public bool Load(string wavPath);

        /* Might be needed in the future if the frequency files of engines that can handle wav's other than 44100Hz are supported.
        if (frq.wavSampleRate == 0) {
            try {
                using (var waveStream = Core.Format.Wave.OpenFile(oto.File)) {
                    var sampleProvider = waveStream.ToSampleProvider();
                    frq.wavSampleRate = sampleProvider.WaveFormat.SampleRate;
                }
            } catch {
                frq.wavSampleRate = 44100;
            }
        }*/
    }

    public class Frq : IFrqFiles {
        public const int kHopSize = 256;

        public int hopSize { get; private set; }
        public double averageF0 { get; private set; }
        public double[] f0 { get; private set; } = new double[0];
        public double[] amp { get; private set; } = new double[0];
        public int wavSampleRate { get; set; } = 44100; // Sample rate for .frq files is fixed

        public bool Load(string wavPath) {
            if (string.IsNullOrEmpty(wavPath)) {
                return false;
            }
            string frqFile = VoicebankFiles.GetFrqFile(wavPath);
            if (!File.Exists(frqFile)) {
                return false;
            }
            try {
                using (var fileStream = File.OpenRead(frqFile)) {
                    using (var reader = new BinaryReader(fileStream)) {
                        string header = new string(reader.ReadChars(8));
                        if (header != "FREQ0003") {
                            throw new FormatException("FREQ0003 header not found.");
                        }
                        hopSize = reader.ReadInt32();
                        averageF0 = reader.ReadDouble();
                        _ = reader.ReadBytes(16); // blank
                        int length = reader.ReadInt32();
                        f0 = new double[length];
                        amp = new double[length];
                        for (int i = 0; i < length; i++) {
                            f0[i] = reader.ReadDouble();
                            amp[i] = reader.ReadDouble();
                        }
                    }
                }
                return true;
            } catch (Exception e) {
                var customEx = new MessageCustomizableException("Failed to load frq file", "<translate:errors.failed.load>: frq file", e);
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(customEx));
                return false;
            }
        }

        public void Save(Stream stream) {
            using (var writer = new BinaryWriter(stream)) {
                writer.Write(Encoding.ASCII.GetBytes("FREQ0003"));
                writer.Write(hopSize);
                writer.Write(averageF0);
                writer.Write(0);
                writer.Write(0);
                writer.Write(0);
                writer.Write(0);
                writer.Write(f0.Length);
                for (int i = 0; i < f0.Length; ++i) {
                    writer.Write(f0[i]);
                    writer.Write(amp[i]);
                }
            }
        }

        public static Frq Build(float[] samples, double[] f0) {
            var frq = new Frq();
            frq.hopSize = kHopSize;
            frq.f0 = f0;
            frq.averageF0 = frq.f0.Where(f => f > 0).DefaultIfEmpty(0).Average();

            double ampMult = Math.Pow(2, 15);
            frq.amp = new double[frq.f0.Length];
            for (int i = 0; i < frq.amp.Length; ++i) {
                double sum = 0;
                int count = 0;
                for (int j = frq.hopSize * i; j < frq.hopSize * (i + 1) && j < samples.Length; ++j) {
                    sum += Math.Abs(samples[j]);
                    count++;
                }
                frq.amp[i] = count == 0 ? 0 : sum * ampMult / count;
            }
            return frq;
        }
    }
    public class Mrq : IFrqFiles {
        public int hopSize { get; private set; }
        public double averageF0 { get; private set; }
        public double[] f0 { get; private set; } = new double[0];
        public int wavSampleRate { get; set; } = 44100;

        public bool Load(string wavPath) {
            if (string.IsNullOrEmpty(wavPath)) return false;
            string mrqPath = VoicebankFiles.GetMrqFile(wavPath);
            if (!File.Exists(mrqPath)) return false;

            try {
                using var fs = File.OpenRead(mrqPath);
                using var br = new BinaryReader(fs);

                // i wish there was a better way to do this
                var magic = new string(br.ReadChars(4));
                if (magic != "mrq ")
                    throw new FormatException("mrq header not found");

                int ver = br.ReadInt32();
                if (ver <= 0) throw new FormatException("invalid mrq version");

                int nentries = br.ReadInt32();
                string target = Path.GetFileName(wavPath);

                for (int i = 0; i < nentries; i++) {
                    int nfilename = br.ReadInt32();
                    if (nfilename < 0) throw new FormatException("bad name length");

                    // moresampler stores filenames as UTF-16LE, because that's what Windows uses
                    byte[] nameBytes = br.ReadBytes(checked(nfilename * 2));
                    if (nameBytes.Length != nfilename * 2) throw new EndOfStreamException();
                    string filename = Encoding.Unicode.GetString(nameBytes);

                    int size = br.ReadInt32();
                    if (size < 0) throw new FormatException("negative payload size");

                    if (!string.Equals(filename, target, StringComparison.Ordinal)) {
                        fs.Seek(size, SeekOrigin.Current); // skip if it's not the target file
                        continue;
                    }

                    int nf0 = br.ReadInt32();
                    wavSampleRate = br.ReadInt32();
                    hopSize = br.ReadInt32();

                    if (nf0 < 0 || size < 12 || size < 12 + nf0 * 4)
                        throw new FormatException("f0 length mismatch");

                    f0 = new double[nf0]; // i can't believe that cs lets me do this
                    for (int j = 0; j < nf0; j++) f0[j] = br.ReadSingle();

                    // optional tail: timestamp + modified (8 bytes), but we don't need it
                    int used = 12 + nf0 * 4;
                    if (size - used >= 8) { _ = br.ReadInt32(); _ = br.ReadInt32(); }


                    var voiced = f0.Where(v => v > 0).ToArray();
                    if (voiced.Length == 0) {
                        averageF0 = 0.0;
                    } else {
                        // mean in log space, then convert back to hz
                        double meanLog2 = voiced.Average(v => Math.Log(v, 2.0));
                        averageF0 = Math.Pow(2.0, meanLog2);
                    }
                    return true;
                }

                return false; // entry not found
            } catch (Exception e) {
                var ex = new MessageCustomizableException("Failed to load mrq file",
                    "<translate:errors.failed.load>: mrq file", e);
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(ex));
                return false;
            }
        }
    }
}
