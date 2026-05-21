using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using NAudio.Wave;
using NumSharp;
using OpenUtau.Core.Format;
using OpenUtau.Core.Render;
using OpenUtau.Core.SignalChain;
using OpenUtau.Core.Ustx;
using Serilog;
using VocalShaper;

namespace OpenUtau.Core.Vogen {
    public class VogenRenderer : IRenderer {
        const int fs = 44100;
        const int fftSize = 2048;
        const float headMs = 500;
        const float tailMs = 500;
        const float frameMs = 10;

        static readonly object lockObj = new object();

        static readonly HashSet<string> supportedExp = new HashSet<string>(){
            Format.Ustx.DYN,
            Format.Ustx.PITD,
            Format.Ustx.GENC,
            Format.Ustx.BREC,
            Format.Ustx.SHFC,
            Format.Ustx.TENC,
            Format.Ustx.VOIC,
        };

        static readonly VSVocoder vocoder = new VSVocoder(fs, frameMs);

        public USingerType SingerType => USingerType.Vogen;

        public bool SupportsRenderPitch => true;

        public bool SupportsExpression(UExpressionDescriptor descriptor) {
            return supportedExp.Contains(descriptor.abbr);
        }

        public RenderResult Layout(RenderPhrase phrase) {
            return new RenderResult() {
                leadingMs = headMs,
                positionMs = phrase.positionMs,
                estimatedLengthMs = headMs + phrase.durationMs + tailMs,
            };
        }

        public Task<RenderResult> Render(RenderPhrase phrase, Progress progress, int trackNo, CancellationTokenSource cancellation, bool isPreRender = false) {
            var task = Task.Run(() => {
                lock (lockObj) {
                    if (cancellation.IsCancellationRequested) {
                        return new RenderResult();
                    }
                    var result = Layout(phrase);
                    var wavPath = Path.Join(PathManager.Inst.CachePath, $"vog-{phrase.hash:x16}.wav");
                    phrase.AddCacheFile(wavPath);
                    string progressInfo = $"Track {trackNo + 1}: {this} \"{string.Join(" ", phrase.phones.Select(p => p.phoneme))}\"";
                    progress.Complete(0, progressInfo);
                    if (File.Exists(wavPath)) {
                        try {
                            using (var waveStream = Wave.OpenFile(wavPath)) {
                                result.samples = Wave.GetSamples(waveStream.ToSampleProvider().ToMono(1, 0));
                            }
                        } catch (Exception e) {
                            Log.Error(e, "Failed to render.");
                        }
                    }
                    if (result.samples == null) {
                        result.samples = InvokeVogen(phrase);
                        var source = new WaveSource(0, 0, 0, 1);
                        source.SetSamples(result.samples);
                        WaveFileWriter.CreateWaveFile16(wavPath, new ExportAdapter(source).ToMono(1, 0));
                    }
                    if (result.samples != null) {
                        Renderers.ApplyDynamics(phrase, result);
                    }
                    progress.Complete(phrase.phones.Length, progressInfo);
                    return result;
                }
            });
            return task;
        }

        float[] InvokeVogen(RenderPhrase phrase) {
            const int pitchInterval = 5;
            int headFrames = (int)(headMs / frameMs);
            int tailFrames = (int)(tailMs / frameMs);
            var result = Layout(phrase);
            var singer = phrase.singer as VogenSinger;
            var notePitches = phrase.notes
                .Select(n => (float)n.tone)
                .Prepend(0)
                .Append(0)
                .ToList();
            var noteDurs = phrase.notes
                .Select(n => (long)Math.Round(n.durationMs / frameMs))
                .Prepend(headFrames)
                .Append(tailFrames)
                .ToList();
            var noteToCharIndex = Enumerable.Range(0, noteDurs.Count)
                .Select(i => (long)i)
                .ToList();
            var phonemes = phrase.phones
                .Select(p => p.phoneme)
                .Prepend("")
                .Append("")
                .ToList();
            var phDurs = phrase.phones
                .Select(p => (long)Math.Round(p.durationMs / frameMs))
                .Prepend(headFrames)
                .Append(tailFrames)
                .ToList();
            var totalFrames = (int)phDurs.Sum();
            var f0 = SampleCurve(phrase, phrase.pitches, 0, totalFrames, headFrames, tailFrames, x => MusicMath.ToneToFreq(x * 0.01));
            float[] f0Shifted = f0.Select(f => (float)f).ToArray();
            if (phrase.toneShift != null) {
                for (int i = 0; i < f0.Length - headFrames - tailFrames; i++) {
                    double posMs = phrase.positionMs - phrase.leadingMs + i * frameMs;
                    int ticks = phrase.timeAxis.MsPosToTickPos(posMs) - (phrase.position - phrase.leading);
                    int index = Math.Max(0, (int)((double)ticks / pitchInterval));
                    if (index < phrase.pitches.Length) {
                        f0Shifted[i + headFrames] = (float)MusicMath.ToneToFreq((phrase.pitches[index] + phrase.toneShift[index]) * 0.01);
                    }
                }
            }
            noteDurs[0] += phDurs.Sum() - noteDurs.Sum();
            var breAmp = new float[f0.Length];
            Array.Fill(breAmp, 0);
            var inputs = new List<NamedOnnxValue>();
            inputs.Add(NamedOnnxValue.CreateFromTensor("notePitches",
                new DenseTensor<float>(notePitches.ToArray(), new int[] { notePitches.Count })));
            inputs.Add(NamedOnnxValue.CreateFromTensor("noteDurs",
                new DenseTensor<long>(noteDurs.ToArray(), new int[] { noteDurs.Count })));
            inputs.Add(NamedOnnxValue.CreateFromTensor("noteToCharIndex",
                new DenseTensor<long>(noteToCharIndex.ToArray(), new int[] { noteToCharIndex.Count })));
            inputs.Add(NamedOnnxValue.CreateFromTensor("phs",
                new DenseTensor<string>(phonemes.ToArray(), new int[] { phonemes.Count })));
            inputs.Add(NamedOnnxValue.CreateFromTensor("phDurs",
                new DenseTensor<long>(phDurs.ToArray(), new int[] { phonemes.Count })));
            using (var session = Onnx.getInferenceSession(Data.VogenRes.f0_man, OnnxRunnerChoice.CPU)) {
                using var outputs = session.Run(inputs);
                var f0Out = outputs.First().AsTensor<float>();
                var f0Path = Path.Join(PathManager.Inst.CachePath, $"vog-{phrase.hash:x16}-f0.npy");
                var f0Array = new float[f0.Length];
                for (int i = 0; i < f0.Length; ++i) {
                    f0Array[i] = f0Out[i];
                    if (f0Out[i] < float.Epsilon) {
                        f0[i] = 0;
                    }
                }
                np.Save(f0Array, f0Path);
            }
            inputs.Clear();
            inputs.Add(NamedOnnxValue.CreateFromTensor("phs",
                new DenseTensor<string>(phonemes.ToArray(), new int[] { 1, phonemes.Count })));
            inputs.Add(NamedOnnxValue.CreateFromTensor("phDurs",
                new DenseTensor<long>(phDurs.ToArray(), new int[] { 1, phonemes.Count })));
            inputs.Add(NamedOnnxValue.CreateFromTensor("f0",
                new DenseTensor<float>(f0Shifted, new int[] { 1, f0.Length })));
            inputs.Add(NamedOnnxValue.CreateFromTensor("breAmp",
                new DenseTensor<float>(breAmp, new int[] { 1, f0.Length })));
            double[,] sp;
            double[,] ap;
            using (var session = Onnx.getInferenceSession(singer.model, OnnxRunnerChoice.CPU)) {
                using var outputs = session.Run(inputs);
                var mgc = outputs.First().AsTensor<float>().Select(f => (double)f).ToArray();
                var bap = outputs.Last().AsTensor<float>().Select(f => (double)f).ToArray();
                sp = Worldline.DecodeMgc(f0.Length, mgc, fftSize, fs);
                ap = Worldline.DecodeBap(f0.Length, bap, fftSize, fs);
            }
            var gender = SampleCurve(phrase, phrase.gender, 0.5, totalFrames, headFrames, tailFrames, x => 0.5 + 0.005 * x);
            var tension = SampleCurve(phrase, phrase.tension, 0.5, totalFrames, headFrames, tailFrames, x => 0.5 + 0.005 * x);
            var breathiness = SampleCurve(phrase, phrase.breathiness, 0.5, totalFrames, headFrames, tailFrames, x => 0.5 + 0.005 * x);
            var voicing = SampleCurve(phrase, phrase.voicing, 1.0, totalFrames, headFrames, tailFrames, x => 0.01 * x);
            var samples = Worldline.WorldSynthesis(
                f0,
                sp, false, sp.GetLength(1),
                ap, false, fftSize,
                frameMs, 44100,
                gender, tension, breathiness, voicing);
            return samples.Select(f => (float)f).ToArray();
        }

        double[] SampleCurve(RenderPhrase phrase, float[] curve, double defaultValue, int length, int headFrames, int tailFrames, Func<double, double> convert) {
            const int interval = 5;
            var result = new double[length];
            if (curve == null) {
                Array.Fill(result, defaultValue);
                return result;
            }
            for (int i = 0; i < length - headFrames - tailFrames; i++) {
                double posMs = phrase.positionMs - phrase.leadingMs + i * frameMs;
                int ticks = phrase.timeAxis.MsPosToTickPos(posMs) - (phrase.position - phrase.leading);
                int index = Math.Max(0, (int)((double)ticks / interval));
                if (index < curve.Length) {
                    result[i + headFrames] = convert(curve[index]);
                }
            }
            Array.Fill(result, defaultValue, 0, headFrames);
            Array.Fill(result, defaultValue, length - tailFrames, tailFrames);
            return result;
        }

        public RenderPitchResult LoadRenderedPitch(RenderPhrase phrase) {
            var f0Path = Path.Join(PathManager.Inst.CachePath, $"vog-{phrase.hash:x16}-f0.npy");
            if (!File.Exists(f0Path)) {
                return null;
            }
            var result = new RenderPitchResult() {
                tones = np.Load<float[]>(f0Path).Select(f => (float)MusicMath.FreqToTone(f)).ToArray(),
            };
            result.ticks = new float[result.tones.Length];
            var layout = Layout(phrase);
            var t = layout.positionMs - layout.leadingMs;
            for (int i = 0; i < result.tones.Length; i++) {
                t += frameMs;
                result.ticks[i] = phrase.timeAxis.MsPosToTickPos(t) - phrase.position;
            }
            return result;
        }

        public UExpressionDescriptor[] GetSuggestedExpressions(USinger singer, URenderSettings renderSettings) {
            return new UExpressionDescriptor[] { };
        }

        public override string ToString() => Renderers.VOGEN;
    }
}
