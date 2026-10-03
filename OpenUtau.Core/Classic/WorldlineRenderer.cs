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
using OpenUtau.Core;
using OpenUtau.Core.Format;
using OpenUtau.Core.Render;
using OpenUtau.Core.SignalChain;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Classic {
    public class WorldlineRenderer : IRenderer {

        readonly int version;
        readonly int hopSize;
        readonly double frameMs;
        byte[]? vocoderBytes;

        /// <param name="version">
        /// 10 (Worldline-R): WORLD synthesis, 10 ms frames. 11 (Worldline-R1.1): hnsep
        /// features, WORLD's periodic half and continuous noise, 5 ms frames. 20
        /// (Worldline-R2): WORLD features into the vocoder package, at its hop of 512.
        /// </param>
        public WorldlineRenderer(int version) {
            hopSize = version switch {
                10 => 441,
                11 => 220,
                20 => 512,
                _ => throw new ArgumentException($"Unsupported WorldlineRenderer version: {version}"),
            };
            this.version = version;
            frameMs = hopSize * 1000.0 / 44100.0;
        }

        static readonly HashSet<string> supportedExp = new HashSet<string>(){
            Ustx.DYN,
            Ustx.PITD,
            Ustx.CLR,
            Ustx.SHFT,
            Ustx.VEL,
            Ustx.VOL,
            Ustx.MOD,
            Ustx.MODP,
            Ustx.ALT,
            Ustx.GENC,
            Ustx.BREC,
            Ustx.TENC,
            Ustx.VOIC,
            Ustx.DIR,
        };

        public USingerType SingerType => USingerType.Classic;

        public bool SupportsRenderPitch => false;

        public bool SupportsExpression(UExpressionDescriptor descriptor) {
            return descriptor.isFlag
                || !string.IsNullOrEmpty(descriptor.flag)
                || supportedExp.Contains(descriptor.abbr)
                || descriptor.type == UExpressionType.MorphingCurve
                || descriptor.abbr.StartsWith("cl", StringComparison.OrdinalIgnoreCase);
        }

        public RenderResult Layout(RenderPhrase phrase) {
            return new RenderResult() {
                leadingMs = phrase.leadingMs,
                positionMs = phrase.positionMs,
                estimatedLengthMs = phrase.durationMs + phrase.leadingMs,
            };
        }

        public Task<RenderResult> Render(RenderPhrase phrase, Progress progress, int trackNo, CancellationTokenSource cancellation, bool isPreRender, RenderPhraseEvents? renderEvents = null) {
            var resamplerItems = new List<ResamplerItem>();
            foreach (var phone in phrase.phones) {
                resamplerItems.Add(new ResamplerItem(phrase, phone));
            }
            var task = Task.Run(() => {
                var result = Layout(phrase);
                var wavPath = Path.Join(PathManager.Inst.CachePath, $"wdl-v{version}-{phrase.hash:x16}.wav");
                phrase.AddCacheFile(wavPath);
                string progressInfo = $"Track {trackNo + 1}: {this} {string.Join(" ", phrase.phones.Select(p => p.phoneme))}";
                progress.Complete(0, progressInfo);
                var cacheLock = Renderers.GetCacheLock(wavPath);
                lock (cacheLock) {
                    if (File.Exists(wavPath)) {
                        using (var waveStream = Wave.OpenFile(wavPath)) {
                            result.samples = Wave.GetSamples(waveStream.ToSampleProvider().ToMono(1, 0));
                        }
                    }
                }

                if (result.samples == null) {
                    var phraseSynth = new Worldline.PhraseSynthV2(44100, hopSize, 2048, useHnsep: version == 11);
                    double posOffsetMs = phrase.positionMs - phrase.leadingMs;
                    foreach (var item in resamplerItems) {
                        double posMs = item.phone.positionMs - item.phone.leadingMs - (phrase.positionMs - phrase.leadingMs);
                        double skipMs = item.skipOver;
                        double lengthMs = item.phone.envelope[4].X - item.phone.envelope[0].X;
                        double fadeInMs = item.phone.envelope[1].X - item.phone.envelope[0].X;
                        double fadeOutMs = item.phone.envelope[4].X - item.phone.envelope[3].X;
                        phraseSynth.AddRequest(item, posMs, skipMs, lengthMs, fadeInMs, fadeOutMs);
                    }
                    try {
                        phraseSynth.AnalyzeRequests(cancellation.Token);
                    } catch (OperationCanceledException) {
                        return result;
                    } catch (SynthRequestError e) {
                        string phoneme = e.Item?.phone.phoneme ?? string.Empty;
                        if (e is CutOffExceedDurationError cee) {
                            throw new MessageCustomizableException(
                                $"Failed to render\n Oto error: cutoff exceeds audio duration \n{phoneme}",
                                $"<translate:errors.failed.synth.cutoffexceedduration>\n{phoneme}",
                                e);
                        }
                        if (e is CutOffBeforeOffsetError cbe) {
                            throw new MessageCustomizableException(
                                $"Failed to render\n Oto error: cutoff before offset \n{phoneme}",
                                $"<translate:errors.failed.synth.cutoffbeforeoffset>\n{phoneme}",
                                e);
                        }
                        throw;
                    }
                    int frames = (int)Math.Ceiling(result.estimatedLengthMs / frameMs);
                    var f0 = SampleCurve(phrase, phrase.pitches, 0, frames, x => MusicMath.ToneToFreq(x * 0.01));
                    var gender = SampleCurve(phrase, phrase.gender, 0.5, frames, x => 0.5 + 0.005 * x);
                    var tension = SampleCurve(phrase, phrase.tension, 0.5, frames, x => 0.5 + 0.005 * x);
                    var breathiness = SampleCurve(phrase, phrase.breathiness, 0.5, frames, x => 0.5 + 0.005 * x);
                    var voicing = SampleCurve(phrase, phrase.voicing, 1.0, frames, x => 0.01 * x);
                    phraseSynth.SetCurves(f0, gender, tension, breathiness, voicing);
                    if (version == 11) {
                        result.samples = phraseSynth.SynthContinuousNoise(seed: phrase.hash);
                    } else if (version == 10) {
                        result.samples = phraseSynth.Synth();
                    } else {
                        var (totalFrames, f0Out, spEnvOut, apOut) = phraseSynth.SynthFeatures();
                        int paddedLength = totalFrames + 8;
                        paddedLength = (int)Math.Ceiling((float)(paddedLength + 4) / 16) * 16;
                        int leftPadding = 4;
                        int rightPadding = paddedLength - totalFrames - leftPadding;
                        int spSize = spEnvOut.shape[1];
                        f0Out = np.concatenate(new NDArray[] { np.zeros((leftPadding)), f0Out, np.zeros((rightPadding)) }, axis: 0);
                        spEnvOut = np.concatenate(new NDArray[] { np.zeros((leftPadding, spSize)), spEnvOut, np.zeros((rightPadding, spSize)) }, axis: 0);
                        apOut = np.concatenate(new NDArray[] { np.ones((leftPadding, spSize)), apOut, np.ones((rightPadding, spSize)) }, axis: 0);
                        var f0Tensor = new DenseTensor<float>(f0Out.astype(typeof(float)).ToArray<float>(), new int[] { 1, paddedLength });
                        var spEnvTensor = new DenseTensor<float>(spEnvOut.astype(typeof(float)).ToArray<float>(), new int[] { 1, paddedLength, spEnvOut.shape[1] });
                        var apTensor = new DenseTensor<float>(apOut.astype(typeof(float)).ToArray<float>(), new int[] { 1, paddedLength, apOut.shape[1] });
                        var inputs = new List<NamedOnnxValue> {
                            NamedOnnxValue.CreateFromTensor("f0", f0Tensor),
                            NamedOnnxValue.CreateFromTensor("sp_env", spEnvTensor),
                            NamedOnnxValue.CreateFromTensor("ap", apTensor)
                        };
                        var session = Onnx.getInferenceSession(OpenUtau.Core.Classic.Data.Resources.mel, OnnxRunnerChoice.CPU);
                        using var results = session.Run(inputs);
                        var melOutput = results.First(r => r.Name == "mel").AsTensor<float>();
                        const string vocoderPkg = Hifisampler.HifiVocoder.PackageId;
                        string vocoderPath = PackageManager.Inst.GetInstalledPath(vocoderPkg) ?? "";
                        if (vocoderBytes == null) {
                            var configPath = Path.Combine(vocoderPath, "vocoder.yaml");
                            if (!File.Exists(configPath)) {
                                throw new MessageCustomizableException(
                                    $"Error loading package \"{vocoderPkg}\"",
                                    $"<translate:packages.errors.missing>",
                                    new Exception($"Error loading package \"{vocoderPkg}\""),
                                true,
                                    new string[] { vocoderPkg });
                            }
                            var config = Yaml.DefaultDeserializer.Deserialize<Core.DiffSinger.DsVocoderConfig>(
                                File.ReadAllText(configPath, System.Text.Encoding.UTF8));
                            vocoderBytes = File.ReadAllBytes(Path.Combine(vocoderPath, config.model));
                        }
                        var vocoderSession = Onnx.getInferenceSession(vocoderBytes!, OnnxRunnerChoice.Default);
                        var vocoderInputs = new List<NamedOnnxValue> {
                            NamedOnnxValue.CreateFromTensor("mel", melOutput),
                            NamedOnnxValue.CreateFromTensor("f0", f0Tensor),
                        };
                        using var vocoderResults = vocoderSession.Run(vocoderInputs);
                        var audioOutput = vocoderResults.First().AsTensor<float>();
                        result.samples = audioOutput.ToArray();
                        result.samples = result.samples.Skip(leftPadding * 512).Take(totalFrames * 512).ToArray();
                        int easeInSamples = Math.Min(result.samples.Length, 512);
                        for (int i = 0; i < easeInSamples; ++i) {
                            double gain = (double)i / easeInSamples;
                            result.samples[i] = (float)(result.samples[i] * gain);
                        }
                        int easeOutSamples = Math.Min(result.samples.Length, 512);
                        for (int i = 0; i < easeOutSamples; ++i) {
                            double gain = (double)(easeOutSamples - i) / easeOutSamples;
                            result.samples[result.samples.Length - easeOutSamples + i] = (float)(result.samples[result.samples.Length - easeOutSamples + i] * gain);
                        }
                    }
                    AddDirects(phrase, resamplerItems, result);
                    if (result.samples != null) {
                        // Synchronous: a detached write races a subsequent cold re-render's read.
                        try {
                            lock (cacheLock) {
                                Wave.WriteMono16Wav(wavPath, result.samples);
                            }
                        } catch (Exception e) {
                            Serilog.Log.Error(e, $"Failed to write cache file: {wavPath}");
                        }
                    }
                }
                progress.Complete(phrase.phones.Length, progressInfo);
                if (result.samples != null) {
                    Renderers.ApplyDynamics(phrase, result);
                }
                return result;
            });
            return task;
        }

        double[] SampleCurve(RenderPhrase phrase, float[] curve, double defaultValue, int length, Func<double, double> convert) {
            const int interval = 5;
            var result = new double[length];
            if (curve == null) {
                Array.Fill(result, defaultValue);
                return result;
            }
            for (int i = 0; i < length; i++) {
                double posMs = phrase.positionMs - phrase.leadingMs + i * frameMs;
                int ticks = phrase.timeAxis.MsPosToTickPos(posMs) - (phrase.position - phrase.leading);
                int index = Math.Max(0, (int)((double)ticks / interval));
                if (index < curve.Length) {
                    result[i] = convert(curve[index]);
                }
            }
            return result;
        }

        private static void AddDirects(RenderPhrase phrase, List<ResamplerItem> resamplerItems, RenderResult result) {
            foreach (var item in resamplerItems) {
                if (!item.phone.direct) {
                    continue;
                }
                double posMs = item.phone.positionMs - item.phone.leadingMs - (phrase.positionMs - phrase.leadingMs);
                int startPhraseIndex = (int)(posMs / 1000 * 44100);
                using (var waveStream = Wave.OpenFile(item.phone.oto.File)) {
                    if (waveStream == null) {
                        continue;
                    }
                    float[] samples = Wave.GetSamples(waveStream!.ToSampleProvider().ToMono(1, 0));
                    int offset = (int)(item.phone.oto.Offset / 1000 * 44100);
                    int cutoff = (int)(item.phone.oto.Cutoff / 1000 * 44100);
                    int length = cutoff >= 0 ? (samples.Length - offset - cutoff) : -cutoff;
                    samples = samples.Skip(offset).Take(length).ToArray();
                    item.ApplyEnvelope(samples);
                    for (int i = 0; i < Math.Min(samples.Length, result.samples.Length - startPhraseIndex); ++i) {
                        result.samples[startPhraseIndex + i] = samples[i];
                    }
                }
            }
        }

        public RenderPitchResult LoadRenderedPitch(RenderPhrase phrase) {
            return null;
        }

        public UExpressionDescriptor[] GetSuggestedExpressions(USinger singer, URenderSettings renderSettings) {
            var expressions = new List<UExpressionDescriptor>();
            if (singer != null && singer.Subbanks != null) {
                var uniqueColors = singer.Subbanks.Select(s => s.Color).Where(c => !string.IsNullOrEmpty(c)).Distinct().ToList();
                int colorIndex = 1;
                foreach (var colorName in uniqueColors) {
                    expressions.Add(new UExpressionDescriptor {
                        name = $"voice color {colorIndex:D2} {colorName}",
                        abbr = $"cl{colorIndex:D2}",
                        type = UExpressionType.MorphingCurve,
                        min = 0,
                        max = 100,
                        defaultValue = 0,
                        isFlag = false,
                        flag = ""
                    });
                    colorIndex++;
                }
            }
            return expressions.ToArray();
        }

        // The Worldline-R variants render the same expressions, so share Worldline-R's graphs.
        public string ExpressionGraphSlot => Renderers.WORLDLINE_R;

        public override string ToString() => version switch {
            11 => Renderers.WORLDLINE_R11,
            20 => Renderers.WORLDLINE_R2,
            _ => Renderers.WORLDLINE_R,
        };
    }
}