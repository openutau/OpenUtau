using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
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
        readonly double frameMs;
        byte[]? vocoderBytes;

        public WorldlineRenderer(int version) {
            if (version != 1 && version != 2) {
                throw new ArgumentException($"Unsupported WorldlineRenderer version: {version}");
            }
            this.version = version;
            frameMs = version == 1 ? 10 : 512.0 * 1000.0 / 44100.0;
        }
        static readonly HashSet<string> supportedExp = new HashSet<string>(){
            Ustx.DYN,
            Ustx.PITD,
            Ustx.CLR,
            Ustx.CLRY,
            Ustx.XSY,
            Ustx.SHFT,
            Ustx.VEL,
            Ustx.VOL,
            Ustx.MOD,
            Ustx.MODP,
            Ustx.ALT,
            Ustx.GENC,
            Ustx.BREC,
            Ustx.TENC,
            Ustx.POWC,
            Ustx.VOIC,
            Ustx.DIR,
        };
        public USingerType SingerType => USingerType.Classic;

        public bool SupportsRenderPitch => false;

        public bool SupportsExpression(UExpressionDescriptor descriptor) {
            return supportedExp.Contains(descriptor.abbr);
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
                var powerCurve = phrase.curves.FirstOrDefault(c => c.Item1 == Ustx.POWC)?.Item2;
                bool hasPower = powerCurve?.Any(value => value != 0) == true;
                if (File.Exists(wavPath)) {
                    using (var waveStream = Wave.OpenFile(wavPath)) {
                        result.samples = Wave.GetSamples(waveStream.ToSampleProvider().ToMono(1, 0));
                    }
                }
                if (result.samples == null) {
                    var phraseSynth = new Worldline.PhraseSynthV2(44100, version == 1 ? 441 : 512, 2048);
                    double posOffsetMs = phrase.positionMs - phrase.leadingMs;
                    foreach (var item in resamplerItems) {
                        if (cancellation.IsCancellationRequested) {
                            return result;
                        }
                        double posMs = item.phone.positionMs - item.phone.leadingMs - (phrase.positionMs - phrase.leadingMs);
                        double skipMs = item.skipOver;
                        double lengthMs = item.phone.envelope[4].X - item.phone.envelope[0].X;
                        double fadeInMs = item.phone.envelope[1].X - item.phone.envelope[0].X;
                        double fadeOutMs = item.phone.envelope[4].X - item.phone.envelope[3].X;
                        try {
                            phraseSynth.AddRequest(item, posMs, skipMs, lengthMs, fadeInMs, fadeOutMs);
                        } catch (SynthRequestError e) {
                            if (e is CutOffExceedDurationError cee) {
                                throw new MessageCustomizableException(
                                    $"Failed to render\n Oto error: cutoff exceeds audio duration \n{item.phone.phoneme}",
                                    $"<translate:errors.failed.synth.cutoffexceedduration>\n{item.phone.phoneme}",
                                    e);
                            }
                            if (e is CutOffBeforeOffsetError cbe) {
                                throw new MessageCustomizableException(
                                    $"Failed to render\n Oto error: cutoff before offset \n{item.phone.phoneme}",
                                    $"<translate:errors.failed.synth.cutoffbeforeoffset>\n{item.phone.phoneme}",
                                    e);
                            }
                            throw e;
                        }
                    }
                    int frames = (int)Math.Ceiling(result.estimatedLengthMs / frameMs);
                    var f0 = SampleCurve(phrase, phrase.pitches, 0, frames, x => MusicMath.ToneToFreq(x * 0.01));
                    var gender = SampleCurve(phrase, phrase.gender, 0, frames, x => x);
                    var tension = SampleCurve(phrase, phrase.tension, 0, frames, x => x);
                    var breathiness = SampleCurve(phrase, phrase.breathiness, 0, frames, x => x);
                    var voicing = SampleCurve(phrase, phrase.voicing, 100, frames, x => x);
                    if (hasPower) {
                        var power = SampleCurve(phrase, powerCurve!, 0, frames, x => Math.Clamp(x, -100, 100));
                        ApplyPowerToExpressions(power, gender, tension, breathiness, voicing);
                    }
                    gender = gender.Select(x => 0.5 + 0.005 * x).ToArray();
                    tension = tension.Select(x => 0.5 + 0.005 * x).ToArray();
                    breathiness = breathiness.Select(x => 0.5 + 0.005 * x).ToArray();
                    voicing = voicing.Select(x => 0.01 * x).ToArray();
                    phraseSynth.SetCurves(f0, gender, tension, breathiness, voicing);
                    if (version == 1) {
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
                        const string vocoderPkg = "pc-nsf-hifigan";
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
                        var samplesCopy = (float[])result.samples.Clone();
                        Task.Run(() => {
                            try {
                                Wave.WriteMono16Wav(wavPath, samplesCopy);
                            } catch (Exception e) {
                                Serilog.Log.Error(e, $"Failed to write cache file: {wavPath}");
                            }
                        });
                    }
                }
                progress.Complete(phrase.phones.Length, progressInfo);
                if (result.samples != null) {
                    if (hasPower) {
                        ApplyPowerDynamics(phrase, result, powerCurve!);
                    } else {
                        Renderers.ApplyDynamics(phrase, result);
                    }
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
        private static void ApplyPowerToExpressions(
            double[] power,
            double[] gender,
            double[] tension,
            double[] breathiness,
            double[] voicing) {
            const double maxGenderChange = 4;
            const double maxTensionChange = 75;
            const double maxStrongBreathinessChange = 40;
            const double maxWeakBreathinessChange = 80;
            const double maxWeakVoicingChange = 75;

            for (int i = 0; i < power.Length; ++i) {
                double normalizedPower = Math.Clamp(power[i], -100, 100) / 100;
                gender[i] = Math.Clamp(
                    gender[i] - maxGenderChange * normalizedPower,
                    -100,
                    100);
                tension[i] = Math.Clamp(
                    tension[i] + maxTensionChange * normalizedPower,
                    -100,
                    100);
                if (normalizedPower >= 0) {
                    breathiness[i] = Math.Clamp(
                        breathiness[i] - maxStrongBreathinessChange * normalizedPower,
                        -100,
                        100);
                } else {
                    double weakness = -normalizedPower;
                    breathiness[i] = Math.Clamp(
                        breathiness[i] + maxWeakBreathinessChange * weakness,
                        -100,
                        100);
                    voicing[i] = Math.Clamp(
                        voicing[i] - maxWeakVoicingChange * weakness,
                        0,
                        100);
                }
            }
        }
        private static void ApplyPowerDynamics(RenderPhrase phrase, RenderResult result, float[] power) {
            const int interval = 5;
            int startTick = phrase.position - phrase.leading;
            double startMs = result.positionMs - result.leadingMs;
            int startSample = 0;
            int curveLength = Math.Max(phrase.dynamics?.Length ?? 0, power.Length);
            for (int i = 0; i < curveLength; ++i) {
                int endTick = startTick + interval;
                double endMs = phrase.timeAxis.TickPosToMsPos(endTick);
                int endSample = Math.Min((int)((endMs - startMs) / 1000 * 44100), result.samples.Length);
                float dynamicsA = phrase.dynamics != null && i < phrase.dynamics.Length ? phrase.dynamics[i] : 1;
                float dynamicsB = phrase.dynamics != null && i + 1 < phrase.dynamics.Length ? phrase.dynamics[i + 1] : dynamicsA;
                float powerA = i < power.Length ? power[i] : 0;
                float powerB = i + 1 < power.Length ? power[i + 1] : powerA;
                float a = CombineDynamicsAndPower(dynamicsA, powerA);
                float b = CombineDynamicsAndPower(dynamicsB, powerB);
                for (int j = startSample; j < endSample; ++j) {
                    result.samples[j] *= a + (b - a) * (j - startSample) / (endSample - startSample);
                }
                startTick = endTick;
                startSample = endSample;
            }
        }
        private static float CombineDynamicsAndPower(float dynamics, float power) {
            const double minDynamicsDb = -24;
            const double maxDynamicsDb = 12;
            const double maxPowerChangeDb = 1.5;

            if (dynamics <= 0) {
                return 0;
            }
            double dynamicsDb = 20 * Math.Log10(dynamics);
            double powerDb = Math.Clamp(power, -100, 100) / 100 * maxPowerChangeDb;
            double combinedDb = Math.Clamp(
                dynamicsDb + powerDb,
                minDynamicsDb,
                maxDynamicsDb);
            return (float)MusicMath.DecibelToLinear(combinedDb);
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
            return new UExpressionDescriptor[] { };
        }

        public override string ToString() => version == 1 ? Renderers.WORLDLINE_R : Renderers.WORLDLINE_R2;
    }
}
