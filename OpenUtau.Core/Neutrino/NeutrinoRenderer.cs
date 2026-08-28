using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using K4os.Hash.xxHash;
using NAudio.Wave;
using NWaves.Operations;
using NWaves.Signals;
using OpenUtau.Core.Format;
using OpenUtau.Core.Hts;
using OpenUtau.Core.Render;
using OpenUtau.Core.SignalChain;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Serilog;
using SharpCompress;
using ThirdParty;

namespace OpenUtau.Core.Neutrino {
    public class NeutrinoRenderer : HTSLabelRenderer {
        const string NTYP = "ntyp";
        const string NMOD = "nmod";
        const string NMEL = "nmel";
        const string SMOC = "smoc";

        enum NeutrinoRenderType {
            WORLD,
            NSF,
        }

        enum NeutrinoRenderMode {
            Elements = 2,
            Standard = 3,
            Advanced = 4,
        }

        enum NsfModel {
            va,
            vs,
            ve,
        }

        static readonly HashSet<string> supportedExp = new HashSet<string>(){
            Format.Ustx.DYN,
            Format.Ustx.PITD,
            Format.Ustx.SHFT,
            Format.Ustx.GENC,
            Format.Ustx.TENC,
            Format.Ustx.BREC,
            Format.Ustx.VOIC,
            Format.Ustx.DIR,
            NTYP,
            NMOD,
            SMOC
        };

        static readonly object lockObj = new object();

        public override USingerType SingerType => USingerType.Neutrino;

        public override bool SupportsRenderPitch => true;

        public override bool SupportsExpression(UExpressionDescriptor descriptor) {
            return supportedExp.Contains(descriptor.abbr);
        }

        protected NeutrinoSinger singer;
        string NeutrinoExe = string.Empty;
        string NeutrinoClientExe = string.Empty;
        string NeutrinoServerExe = string.Empty;
        string NsfExe = string.Empty;
        string WorldExe = string.Empty;
        string VocoderClientExe = string.Empty;
        string VocoderServerExe = string.Empty;
        bool existNeutrinoClient = false;
        int sampleRate = 48000;
        // NEUTRINO version the executable paths and dictionary were resolved for.
        NeutrinoVersion setUpVersion = NeutrinoVersion.Unsupported;

        NeutrinoVersion Version => NeutrinoUtils.DetectVersion(this.singer.singerVersion);

        public override void SetUp() {
            lang = "JPN";//TODO: use singer.language
            tablePath = NeutrinoUtils.TableFile;
            if (!NeutrinoUtils.TryResolveBasePath(this.singer.singerVersion, out string basePath, out string error)) {
                Log.Error(error);
                return;
            }
            //Load Dictionary
            try {
                phoneDict.Clear();
                LoadDict(NeutrinoUtils.DicPath(basePath, NeutrinoUtils.ConfFile), this.singer.TextFileEncoding);
                LoadDict(NeutrinoUtils.DicPath(basePath, NeutrinoUtils.TableFile), this.singer.TextFileEncoding);
                // Lyrics often handled in OpenUtau
                phoneDict.Add("R", new string[] { "pau" });
                phoneDict.Add("-", new string[] { "pau" });
                phoneDict.Add("SP", new string[] { "pau" });
                phoneDict.Add("AP", new string[] { "br" });
                g2p = this.LoadG2p();
            } catch (Exception e) {
                Log.Error(e, $"failed to load dictionary from {tablePath}");
                return;
            }
            Log.Debug("Loaded dictionary successfully.");
            var paths = NeutrinoPaths.Create(basePath);
            NeutrinoExe = paths.Neutrino;
            NeutrinoClientExe = paths.NeutrinoClient;
            NeutrinoServerExe = paths.NeutrinoServer;
            NsfExe = paths.Nsf;
            WorldExe = paths.World;
            VocoderClientExe = paths.VocoderClient;
            VocoderServerExe = paths.VocoderServer;
            existNeutrinoClient = paths.HasClient;
            Log.Debug($"NeutrinoExe: {NeutrinoExe}");
            Log.Debug(existNeutrinoClient ? $"No NeutrinoClientExe" : $"NeutrinoClientExe: {NeutrinoClientExe}");
            Log.Debug(String.IsNullOrEmpty(WorldExe) ? $"No WorldExe" : $"WorldExe: {WorldExe}");
            NeutrinoServerLauncher.EnsureStarted(NeutrinoServerExe);
            NeutrinoServerLauncher.EnsureStarted(VocoderServerExe, 23456);
            setUpVersion = Version;
        }

        protected override HTSPhoneme[] CustomHTSPhonemeContext(HTSPhoneme[] htsPhonemes, RenderNote notes) {
            var fixs = GetPrefixAndSuffix(notes);
            foreach (var htsPhoneme in htsPhonemes) {
                htsPhoneme.flag1 = "00"; // NEUTRINO Default.
            }
            return htsPhonemes;
        }

        protected override List<monoLabel> CustomMonoLabel(List<monoLabel> monoLabels) {
            if (Version == NeutrinoVersion.V27) {
                for (int i = 0; i < monoLabels.Count; i++) {
                    var label = monoLabels[i];
                    label.startMs = Math.Round(label.startMs / 10.0) * 10.0;
                    label.endMs = Math.Round(label.endMs / 10.0) * 10.0;
                    monoLabels[i] = label;
                }
            }
            return monoLabels;
        }

        public double[] LoadFile(string filePath) {
            if (File.Exists(filePath)) {
                using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read)) {
                    using (BinaryReader reader = new BinaryReader(fs)) {
                        long fileSize = fs.Length;
                        int Count = (int)(fileSize / sizeof(float));
                        double[] data = new double[Count];
                        for (int i = 0; i < Count; i++) {
                            data[i] = reader.ReadSingle();
                        }
                        return data;
                    }
                }
            }
            return new double[0];
        }

        public void SaveFile(string filePath, double[] doubles) {
            try {
                using (FileStream fs = new FileStream(filePath, FileMode.Create, FileAccess.Write)) {
                    using (BinaryWriter writer = new BinaryWriter(fs)) {
                        foreach (double pitch in doubles) {
                            writer.Write((float)pitch);
                        }
                    }
                }
            } catch (Exception ex) {
                Log.Error($"Error: {ex.Message}");
            }
        }

        public override Task<RenderResult> Render(RenderPhrase phrase, Progress progress, int trackNo, CancellationTokenSource cancellation, bool isPreRender) {
            var task = Task.Run(() => {
                lock (lockObj) {
                    if (cancellation.IsCancellationRequested) {
                        return new RenderResult();
                    }
                    string progressInfo = $"Track {trackNo + 1}: {this} \"{string.Join(" ", phrase.phones.Select(p => p.phoneme))}\"";
                    progress.Complete(0, progressInfo);
                    this.singer = phrase.singer as NeutrinoSinger;
                    // The renderer instance is reused for the whole track, so switching the
                    // singer to another NEUTRINO version must re-resolve the paths and dictionary.
                    if (g2p == null || string.IsNullOrEmpty(NeutrinoExe) || setUpVersion != Version) {
                        SetUp();
                    }
                    var result = Layout(phrase);
                    var hash = HashPhraseGroups(phrase);
                    string tmpPath = Path.Join(PathManager.Inst.CachePath, $"ne-{phrase.preEffectHash:x16}_temp");
                    if (!Directory.Exists(tmpPath)) {
                        Directory.CreateDirectory(tmpPath);
                    }
                    int toneShift = phrase.phones[0] != null ? phrase.phones[0].toneShift : 0;
                    ulong frameHash = HashPhraseFrames(phrase);
                    string wavPath = Path.Join(tmpPath, $"ne-{frameHash}.wav");
                    string f0Path = Path.Join(tmpPath, $"ne-{frameHash}.f0");
                    string editorf0Path = Path.Join(tmpPath, $"ne-{frameHash}-edit.f0");
                    string melspecPath = Path.Join(tmpPath, $"ne-{frameHash}.melspec");
                    string mgcPath = Path.Join(tmpPath, $"ne-{frameHash}.mgc");
                    string bapPath = Path.Join(tmpPath, $"ne-{frameHash}.bap");
                    fullScorePath = Path.Join(tmpPath, $"ne-{hash}_full_score.lab");
                    monoTimingPath = Path.Join(tmpPath, $"ne-{hash}_mono_timing.lab");
                    string modelDir = NeutrinoUtils.ModelDir(this.singer);
                    int numThreads = Preferences.Default.NumRenderThreads;
                    if (!File.Exists(fullScorePath) && !File.Exists(monoTimingPath)) {
                        ProcessPart(phrase);
                    }
                    var flag1 = phrase.phones[0].flags.FirstOrDefault(f => f.Item3.Equals(NTYP));
                    string eng = string.Empty;
                    if (flag1 != null) {
                        eng = flag1.Item1;
                    }
                    string ArgParam = string.Empty;
                    if (Version == NeutrinoVersion.V27) {
                        if (eng.Equals(NeutrinoRenderType.NSF.ToString())) {
                            var flag2 = phrase.phones[0].flags.FirstOrDefault(f => f.Item3.Equals(NMOD));
                            string nsf = "vs";
                            if (flag2 != null) {
                                if (flag2.Item2 == 4) {
                                    nsf = NsfModel.va.ToString();
                                    sampleRate = 48000;
                                } else if (flag2.Item2 == 3) {
                                    nsf = NsfModel.vs.ToString();
                                    sampleRate = 48000;
                                } else if (flag2.Item2 == 2) {
                                    nsf = NsfModel.ve.ToString();
                                    sampleRate = 24000;
                                }
                            }
                            if (!File.Exists(f0Path) || !File.Exists(melspecPath)) {
                                ArgParam = $"\"{fullScorePath}\" \"{monoTimingPath}\" \"{f0Path}\" \"{melspecPath}\" \"{modelDir}\" -s -n 1 -o {numThreads} -k {toneShift} -m -t";
                                if (existNeutrinoClient) {
                                    ProcessRunner.Run(NeutrinoClientExe, ArgParam, Log.Logger);
                                } else {
                                    ProcessRunner.Run(NeutrinoExe, ArgParam, Log.Logger);
                                }
                            }
                            if (cancellation.IsCancellationRequested) {
                                return new RenderResult();
                            }
                            if (!File.Exists(wavPath) && File.Exists(f0Path) && File.Exists(melspecPath)) {
                                if (phrase.phones[0].direct) {
                                    ArgParam = $"\"{f0Path}\" \"{melspecPath}\" \"{modelDir}{nsf}.bin\" \"{wavPath}\" -l \"{monoTimingPath}\" -n 1 -p {numThreads} -s{(int)sampleRate / 1000} -m -t";
                                } else {
                                    double[] f0 = LoadFile(f0Path);
                                    double[] melspec = LoadFile(melspecPath);
                                    int totalFrames = f0.Length;
                                    int headFrames = (int)Math.Round(headMs / framePeriod);
                                    int tailFrames = (int)Math.Round(tailMs / framePeriod);
                                    double[] editorF0 = SampleCurve(phrase, phrase.pitches, 0, framePeriod, totalFrames, headFrames, tailFrames, x => MusicMath.ToneToFreq(x * 0.01));
                                    SaveFile(editorf0Path, editorF0);
                                    ArgParam = $"\"{editorf0Path}\" \"{melspecPath}\" \"{modelDir}{nsf}.bin\" \"{wavPath}\" -l \"{monoTimingPath}\" -n 1 -p {numThreads} -s{(int)sampleRate / 1000} -m -t";
                                }
                                if (File.Exists(VocoderClientExe)) {
                                    ProcessRunner.Run(VocoderClientExe, ArgParam, Log.Logger);
                                } else {
                                    ProcessRunner.Run(NsfExe, ArgParam, Log.Logger);
                                }
                                if (!File.Exists(wavPath)) {
                                    Log.Error($"NEUTRINO produced no wav at {wavPath}. args: {ArgParam}");
                                    result.samples = new float[0];
                                    return result;
                                }
                                using (var waveStream = new WaveFileReader(wavPath)) {
                                    result.samples = Wave.GetSamples(waveStream.ToSampleProvider());
                                }
                                Wave.CorrectSampleScale(result.samples);
                                var signal = new DiscreteSignal(sampleRate, result.samples);
                                signal = Operation.Resample(signal, 44100);
                                var source = new WaveSource(0, 0, 0, 1);
                                source.SetSamples(result.samples);
                                WaveFileWriter.CreateWaveFile16(wavPath, new ExportAdapter(source).ToMono(1, 0));
                            }
                        } else {
                            if (!File.Exists(f0Path) || !File.Exists(mgcPath) || !File.Exists(bapPath)) {
                                ArgParam = $"\"{fullScorePath}\" \"{monoTimingPath}\" \"{f0Path}\" \"{melspecPath}\" \"{modelDir}\" -w \"{mgcPath}\" \"{bapPath}\" -s -n 1 -o {numThreads} -k {toneShift} -m -t";
                                if (existNeutrinoClient) {
                                    ProcessRunner.Run(NeutrinoClientExe, ArgParam, Log.Logger);
                                } else {
                                    ProcessRunner.Run(NeutrinoExe, ArgParam, Log.Logger);
                                }
                            }
                            if (cancellation.IsCancellationRequested) {
                                return new RenderResult();
                            }
                            if (!File.Exists(wavPath) && File.Exists(f0Path) && File.Exists(mgcPath) && File.Exists(bapPath)) {
                                if (phrase.phones[0].direct) {
                                    float gender = 1f + (phrase.phones[0].flags.FirstOrDefault(f => f.Item3.Equals(Format.Ustx.GEN)).Item2 / 100f) ?? 1f;
                                    float breathiness = phrase.phones[0].flags.FirstOrDefault(f => f.Item3.Equals(Format.Ustx.BRE)).Item2 ?? 0f;
                                    ArgParam = $"\"{f0Path}\" \"{mgcPath}\" \"{bapPath}\" \"{wavPath}\" -n 1 -m {gender} -b {breathiness} -t";
                                    // vocoder_server only speaks NSF. Handing it the WORLD
                                    // arguments kills the server, so always run WORLD directly.
                                    ProcessRunner.Run(WorldExe, ArgParam, Log.Logger);
                                    if (!File.Exists(wavPath)) {
                                        Log.Error($"NEUTRINO produced no wav at {wavPath}. args: {ArgParam}");
                                        result.samples = new float[0];
                                        return result;
                                    }
                                    using (var waveStream = new WaveFileReader(wavPath)) {
                                        result.samples = Wave.GetSamples(waveStream.ToSampleProvider());
                                    }
                                    Wave.CorrectSampleScale(result.samples);
                                    var signal = new DiscreteSignal(sampleRate, result.samples);
                                    signal = Operation.Resample(signal, 44100);
                                    var source = new WaveSource(0, 0, 0, 1);
                                    source.SetSamples(result.samples);
                                    WaveFileWriter.CreateWaveFile16(wavPath, new ExportAdapter(source).ToMono(1, 0));
                                } else {
                                    double[] f0 = LoadFile(f0Path);
                                    double[] mgc = LoadFile(mgcPath);
                                    double[] bap = LoadFile(bapPath);
                                    int totalFrames = f0.Length;
                                    int headFrames = (int)Math.Round(headMs / framePeriod);
                                    int tailFrames = (int)Math.Round(tailMs / framePeriod);

                                    var editorF0 = SampleCurve(phrase, phrase.pitches, 0, framePeriod, totalFrames, headFrames, tailFrames, x => MusicMath.ToneToFreq(x * 0.01));
                                    var gender = SampleCurve(phrase, phrase.gender, 0.5, framePeriod, totalFrames, headFrames, tailFrames, x => 0.5 + 0.005 * x);
                                    var tension = SampleCurve(phrase, phrase.tension, 0.5, framePeriod, totalFrames, headFrames, tailFrames, x => 0.5 + 0.005 * x);
                                    var breathiness = SampleCurve(phrase, phrase.breathiness, 0.5, framePeriod, totalFrames, headFrames, tailFrames, x => 0.5 + 0.005 * x);
                                    var voicing = SampleCurve(phrase, phrase.voicing, 1.0, framePeriod, totalFrames, headFrames, tailFrames, x => 0.01 * x);

                                    for (int i = 0; i < f0.Length; i++) {
                                        if (f0[i] < 50) {
                                            editorF0[i] = 0;
                                        }
                                    }

                                    var samples = Worldline.WorldSynthesis(
                                        editorF0,
                                        mgc, true, 60,
                                        bap, true, 2048,
                                        framePeriod, sampleRate,
                                        gender, tension, breathiness, voicing);
                                    result.samples = samples.Select(d => (float)d).ToArray();
                                    Wave.CorrectSampleScale(result.samples);
                                    var signal = new DiscreteSignal(sampleRate, result.samples);
                                    signal = Operation.Resample(signal, 44100);
                                    result.samples = signal.Samples;
                                    var source = new WaveSource(0, 0, 0, 1);
                                    source.SetSamples(result.samples);
                                    WaveFileWriter.CreateWaveFile16(wavPath, new ExportAdapter(source).ToMono(1, 0));
                                }
                            }
                        }
                    } else if (Version == NeutrinoVersion.V3) {
                        // F0ファイル生成
                        if (!File.Exists(f0Path)) {
                            ArgParam = $"\"{fullScorePath}\" \"{monoTimingPath}\" \"{f0Path}\" \"{melspecPath}\" \"{wavPath}\" \"{modelDir}\" --skip-timing --skip-melspec --skip-wav -k {toneShift} -m -t";
                            if (existNeutrinoClient) {
                                ProcessRunner.Run(NeutrinoClientExe, ArgParam, Log.Logger);
                            } else {
                                ProcessRunner.Run(NeutrinoExe, ArgParam, Log.Logger);
                            }
                        }
                        if (cancellation.IsCancellationRequested) {
                            return new RenderResult();
                        }
                        //メルスペクトグラムファイル生成
                        if (File.Exists(f0Path) && !File.Exists(melspecPath)) {
                            if (phrase.phones[0].direct) {
                                ArgParam = $"\"{fullScorePath}\" \"{monoTimingPath}\" \"{f0Path}\" \"{melspecPath}\" \"{wavPath}\" \"{modelDir}\" --skip-timing --skip-f0 --skip-wav -k {toneShift} -m -t";
                            } else {
                                double[] f0 = LoadFile(f0Path);
                                int totalFrames = f0.Length;
                                int headFrames = (int)Math.Ceiling(headMs / 1000.0 * 99.84);
                                int tailFrames = (int)Math.Floor(tailMs / 1000.0 * 99.84);
                                var editorF0 = SampleCurve(phrase, phrase.pitches, 0, 9.984, totalFrames, headFrames, tailFrames, x => MusicMath.ToneToFreq(x * 0.01));
                                SaveFile(editorf0Path, editorF0);
                                // F0の編集とメルスペクトグラムの生成はセット
                                ArgParam = $"\"{fullScorePath}\" \"{monoTimingPath}\" \"{editorf0Path}\" \"{melspecPath}\" \"{wavPath}\" \"{modelDir}\" --skip-timing --skip-f0 --skip-wav -k {toneShift} -m -t";
                            }
                            if (existNeutrinoClient) {
                                ProcessRunner.Run(NeutrinoClientExe, ArgParam, Log.Logger);
                            } else {
                                ProcessRunner.Run(NeutrinoExe, ArgParam, Log.Logger);
                            }
                        }
                        if (cancellation.IsCancellationRequested) {
                            return new RenderResult();
                        }
                        //音声ファイル生成
                        if (!File.Exists(wavPath) && File.Exists(f0Path) && File.Exists(melspecPath)) {
                            if (phrase.phones[0].direct) {
                                ArgParam = $"\"{fullScorePath}\" \"{monoTimingPath}\" \"{f0Path}\" \"{melspecPath}\" \"{wavPath}\" \"{modelDir}\" --skip-timing --skip-f0 --skip-melspec -k {toneShift} -m -t";
                            } else {
                                // TODO:メルスペクトグラムの編集
                                ArgParam = $"\"{fullScorePath}\" \"{monoTimingPath}\" \"{editorf0Path}\" \"{melspecPath}\" \"{wavPath}\" \"{modelDir}\" --skip-timing --skip-f0 --skip-melspec -k {toneShift} -m -t";
                            }
                            if (existNeutrinoClient) {
                                ProcessRunner.Run(NeutrinoClientExe, ArgParam, Log.Logger);
                            } else {
                                ProcessRunner.Run(NeutrinoExe, ArgParam, Log.Logger);
                            }
                            if (!File.Exists(wavPath)) {
                                Log.Error($"NEUTRINO produced no wav at {wavPath}. args: {ArgParam}");
                                result.samples = new float[0];
                                return result;
                            }
                            using (var waveStream = new WaveFileReader(wavPath)) {
                                result.samples = Wave.GetSamples(waveStream.ToSampleProvider());
                            }
                            Wave.CorrectSampleScale(result.samples);
                            var signal = new DiscreteSignal(sampleRate, result.samples);
                            signal = Operation.Resample(signal, 44100);
                            var source = new WaveSource(0, 0, 0, 1);
                            source.SetSamples(result.samples);
                            WaveFileWriter.CreateWaveFile16(wavPath, new ExportAdapter(source).ToMono(1, 0));
                        }
                    } else {
                        Log.Error($"Unsupported NEUTRINO version: {this.singer.singerVersion}");
                        result.samples = new float[0];
                        return result;
                    }
                    progress.Complete(phrase.phones.Length, progressInfo);
                    try {
                        if (File.Exists(wavPath)) {
                            using (var waveStream = new WaveFileReader(wavPath)) {

                                result.samples = Wave.GetSamples(waveStream.ToSampleProvider().ToMono(1, 0));
                            }
                            if (result.samples != null) {
                                Renderers.ApplyDynamics(phrase, result);
                            }
                        }
                    } catch (Exception e) {
                        Log.Error(e.Message);
                        result.samples = new float[0];
                    }
                    return result;
                }
            });
            return task;
        }

        double[] SampleCurve(RenderPhrase phrase, float[] curve, double defaultValue, double frameMs, int length, int headFrames, int tailFrames, Func<double, double> convert) {
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


        public override UExpressionDescriptor[] GetSuggestedExpressions(USinger singer, URenderSettings renderSettings) {
            var result = new List<UExpressionDescriptor> {
                //energy
                //new UExpressionDescriptor{
                //    name="energy (curve)",
                //    abbr=ENE,
                //    type=UExpressionType.Curve,
                //    min=-100,
                //    max=100,
                //    defaultValue=0,
                //    isFlag=false,
                //},
                ////engine
                new UExpressionDescriptor {
                    name = "NEUTRINO engine type (~2.x)",
                    abbr = NTYP,
                    type = UExpressionType.Options,
                    options = Enum.GetNames<NeutrinoRenderType>(),
                    isFlag = false
                },
                ////engine mode
                new UExpressionDescriptor {
                    name = "NEUTRINO engine mode (~2.x)",
                    abbr = NMOD,
                    type = UExpressionType.Options,
                    options = Enum.GetNames<NeutrinoRenderMode>(),
                    isFlag = false
                },
                //expressiveness
                new UExpressionDescriptor {
                    name = "pitch smoothened (curve)",
                    abbr = SMOC,
                    type = UExpressionType.Curve,
                    min = 0,
                    max = 10,
                    defaultValue = 0,
                    isFlag = false
                },
            };
            return result.ToArray();
        }

        public override string ToString() => Renderers.NEUTRINO;

        public override RenderPitchResult LoadRenderedPitch(RenderPhrase phrase) {
            if (this.singer == null) {
                // Nothing rendered yet, so Version cannot be resolved.
                return null;
            }
            var result = new RenderPitchResult();
            try {
                string tmpPath = Path.Join(PathManager.Inst.CachePath, $"ne-{phrase.preEffectHash:x16}_temp");
                string f0Path = Path.Join(tmpPath, $"ne-{HashPhraseFrames(phrase)}.f0");
                if (!File.Exists(f0Path)) {
                    return null;
                }
                double[] f0 = LoadFile(f0Path);

                int totalFrames = f0.Length;
                int headFrames = 0;
                int tailFrames = 0;
                if (Version == NeutrinoVersion.V3) {
                    headFrames = (int)Math.Round(headMs / 1000.0 * 99.84);
                    tailFrames = (int)Math.Round(tailMs / 1000.0 * 99.84);
                } else {
                    headFrames = (int)Math.Round(headMs / framePeriod);
                    tailFrames = (int)Math.Round(tailMs / framePeriod);
                }
                var exprCurve = phrase.curves.FirstOrDefault(curve => curve.Item1.Equals(SMOC));
                if (exprCurve != null) {

                    List<int> exprs = SampleCurve(phrase, exprCurve.Item2, 0, framePeriod, totalFrames, headFrames, tailFrames, x => x).Select(x => (int)x).ToList();
                    var f0S = new F0Smoother(f0.ToList());
                    f0S.SmoothenWidthList = exprs;
                    f0 = f0S.GetSmoothenedF0List(f0.ToList()).ToArray();
                }

                int toneShift = phrase.phones[0] != null ? phrase.phones[0].toneShift : 0;
                result = new RenderPitchResult() {
                    tones = f0.Select(f => (float)MusicMath.FreqToTone(f * Math.Pow(2, ((toneShift * -1) / 12d)))).ToArray(),
                };
                result.ticks = new float[result.tones.Length];
                var layout = Layout(phrase);
                var t = layout.positionMs - layout.leadingMs;
                for (int i = 0; i < result.tones.Length; i++) {
                    if (Version == NeutrinoVersion.V3) {
                        t += 10;
                    } else {
                        t += framePeriod;
                    }
                    result.ticks[i] = phrase.timeAxis.MsPosToTickPos(t) - phrase.position;
                }
            } catch (Exception e) {
                Log.Error(e, "Failed to load rendered pitch.");
                return null;
            }
            return result;
        }


        // toneShift (SHFT / NEUTRINO StyleShift) is not written by RenderPhone.Hash(), so it
        // never reaches phrase.preEffectHash or phrase.hash. Mix it into the cache keys here,
        // otherwise changing it reuses the cached files and -k never takes effect.
        ulong HashWithToneShift(ulong baseHash, RenderPhrase phrase) {
            using (var stream = new MemoryStream()) {
                using (var writer = new BinaryWriter(stream)) {
                    writer.Write(baseHash);
                    writer.Write(phrase.phones[0] != null ? phrase.phones[0].toneShift : 0);
                    return XXH64.DigestOf(stream.ToArray());
                }
            }
        }

        // Score level key. The label files depend on the notes and StyleShift only,
        // so curve edits must not force the timing estimation to run again.
        ulong HashPhraseGroups(RenderPhrase phrase) => HashWithToneShift(phrase.preEffectHash, phrase);

        // Frame level key. f0/melspec/mgc/bap/wav additionally depend on the edited curves,
        // which phrase.hash covers.
        ulong HashPhraseFrames(RenderPhrase phrase) => HashWithToneShift(phrase.hash, phrase);
    }
}
