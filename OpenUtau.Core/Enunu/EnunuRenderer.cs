using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using K4os.Hash.xxHash;
using NAudio.Wave;
using NumSharp;
using OpenUtau.Core.Format;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using Serilog;

namespace OpenUtau.Core.Enunu {
    public class EnunuRenderer : IRenderer {
        public const int headTicks = 240;
        public const int tailTicks = 240;
        /// <summary>
        /// Rests shorter than this (half a beat) do not split phrases (see <see cref="ShouldMergePhrases"/>).
        /// </summary>
        const int mergeGapTicks = 240;

        static readonly HashSet<string> supportedExp = new HashSet<string>(){
            Format.Ustx.DYN,
            Format.Ustx.CLR,
            Format.Ustx.PITD,
            Format.Ustx.GENC,
            Format.Ustx.BREC,
            Format.Ustx.TENC,
            Format.Ustx.VOIC,
            Format.Ustx.VEL,
            Format.Ustx.SHFT
        };

        /// <summary>
        /// Files of one phrase. The UST and the server's work folder are named from the phrase content
        /// without pitch, so pitch edits reuse them; the wav also depends on pitch.
        /// </summary>
        record EnunuPaths(string TmpPath, string WavPath, string VoicebankNameHash) {
            public string UstPath => TmpPath + ".tmp";
            public string EnutmpPath => TmpPath + "_enutemp";
            public string F0Path => Path.Join(EnutmpPath, "f0.npy");
            public string EditorF0Path => Path.Join(EnutmpPath, "editorf0.npy");
            public string MelPath => Path.Join(EnutmpPath, "mel.npy");
            public string VuvPath => Path.Join(EnutmpPath, "vuv.npy");
            public string SpPath => Path.Join(EnutmpPath, "spectrogram.npy");
            public string ApPath => Path.Join(EnutmpPath, "aperiodicity.npy");
        }

        // Serializes Render and LoadRenderedPitch: both write the phrase's UST and read its work folder.
        static readonly object lockObj = new object();

        public USingerType SingerType => USingerType.Enunu;

        public bool SupportsRenderPitch => true;

        public bool SupportsPhonemeEnvelope => false;

        public bool SupportsExpression(UExpressionDescriptor descriptor) {
            return supportedExp.Contains(descriptor.abbr);
        }

        /// <summary>
        /// Keeps phrases together when the rest between them is shorter than mergeGapTicks, so short
        /// rests do not cut the song into short phrases. The acoustic model's GV post-filter scales the
        /// spectrum by the variance within the phrase, and short phrases come out over-emphasized.
        /// Merging across longer rests made phrases too long to re-render quickly. The rests inside a
        /// merged phrase are written to the UST as "R" notes.
        /// </summary>
        public bool ShouldMergePhrases(UProject project, UTrack track, UPhoneme prev, UPhoneme next) {
            return prev != null && next != null && next.position - prev.End < mergeGapTicks;
        }

        public RenderResult Layout(RenderPhrase phrase) {
            var (headMs, tailMs) = HeadTailMs(phrase);
            return new RenderResult() {
                leadingMs = headMs,
                positionMs = phrase.positionMs,
                estimatedLengthMs = headMs + phrase.durationMs + tailMs,
            };
        }

        public Task<RenderResult> Render(RenderPhrase phrase, Progress progress, int trackNo, CancellationTokenSource cancellation, bool isPreRender, RenderPhraseEvents? renderEvents = null) {
            var task = Task.Run(() => {
                lock (lockObj) {
                    if (cancellation.IsCancellationRequested) {
                        return new RenderResult();
                    }
                    string progressInfo = $"Track {trackNo + 1}: {this} \"{string.Join(" ", phrase.phones.Select(p => p.phoneme))}\"";
                    progress.Complete(0, progressInfo);
                    var features = EnunuConnection.Inst.GetFeatures();
                    var paths = PreparePaths(phrase);
                    phrase.AddCacheFile(paths.TmpPath);
                    phrase.AddCacheFile(paths.WavPath);
                    var config = EnunuConfig.Load(phrase.singer);
                    var result = Layout(phrase);
                    if (!File.Exists(paths.WavPath)) {
                        EnsureAcousticFeatures(phrase, config, paths, features);
                        if (cancellation.IsCancellationRequested) {
                            return new RenderResult();
                        }
                        if (config.UsesSynthe) {
                            SynthesizeOnServer(phrase, config, paths);
                        } else {
                            SynthesizeWithWorld(phrase, config, paths);
                        }
                    }
                    progress.Complete(phrase.phones.Length, progressInfo);
                    if (File.Exists(paths.WavPath)) {
                        using (var waveStream = Wave.OpenFile(paths.WavPath)) {
                            result.samples = Wave.GetSamples(waveStream.ToSampleProvider().ToMono(1, 0));
                        }
                        if (result.samples != null) {
                            Renderers.ApplyDynamics(phrase, result);
                        }
                    } else {
                        result.samples = new float[0];
                    }
                    return result;
                }
            });
            return task;
        }

        /// <summary>
        /// Has the server write the phrase's npy files (f0, and mel / vuv or spectrogram / aperiodicity).
        /// </summary>
        void EnsureAcousticFeatures(RenderPhrase phrase, EnunuConfig config, EnunuPaths paths, EnunuServerFeatures? features) {
            if (features?.SupportsPitch == true) {
                // The server caches features itself, so ask every time: the npy files depend on the pitch.
                RunAcousticWithEditorPitch(phrase, config, paths);
                return;
            }
            bool hasNpy = config.UsesSynthe
                ? File.Exists(paths.F0Path) && File.Exists(paths.MelPath) && File.Exists(paths.VuvPath)
                : File.Exists(paths.F0Path) && File.Exists(paths.SpPath) && File.Exists(paths.ApPath);
            if (!hasNpy) {
                Log.Information($"Starting enunu acoustic \"{paths.UstPath}\"");
                EnsureUst(phrase, config, paths.UstPath);
                EnunuConnection.Inst.Acoustic(paths.UstPath, paths.VoicebankNameHash);
            }
        }

        /// <summary>
        /// The server's vocoder writes the wav.
        /// </summary>
        void SynthesizeOnServer(RenderPhrase phrase, EnunuConfig config, EnunuPaths paths) {
            int frames = np.Load<double[]>(paths.F0Path).Length;
            // Always write it: synthe replaces the cached pitch with this file, and a stale one
            // would win. It is also how models without lf0 conditioning get the editor pitch.
            np.Save(EditorF0(phrase, config, frames), paths.EditorF0Path);
            EnunuConnection.Inst.Synthe(paths.UstPath, paths.WavPath, paths.VoicebankNameHash);
        }

        /// <summary>
        /// WORLD synthesis from the server's spectrogram and aperiodicity, with the editor pitch and curves.
        /// </summary>
        void SynthesizeWithWorld(RenderPhrase phrase, EnunuConfig config, EnunuPaths paths) {
            var f0 = np.Load<double[]>(paths.F0Path);
            var sp = np.Load<double[,]>(paths.SpPath);
            var ap = np.Load<double[,]>(paths.ApPath);
            int totalFrames = f0.Length;
            var (headFrames, tailFrames) = HeadTailFrames(phrase, config);
            var editorF0 = EditorF0(phrase, config, totalFrames);
            var gender = SampleCurve(phrase, phrase.gender, 0.5, config.framePeriod, totalFrames, headFrames, tailFrames, x => 0.5 + 0.005 * x);
            var tension = SampleCurve(phrase, phrase.tension, 0.5, config.framePeriod, totalFrames, headFrames, tailFrames, x => 0.5 + 0.005 * x);
            var breathiness = SampleCurve(phrase, phrase.breathiness, 0.5, config.framePeriod, totalFrames, headFrames, tailFrames, x => 0.5 + 0.005 * x);
            var voicing = SampleCurve(phrase, phrase.voicing, 1.0, config.framePeriod, totalFrames, headFrames, tailFrames, x => 0.01 * x);
            int fftSize = (sp.GetLength(1) - 1) * 2;
            for (int i = 0; i < f0.Length; i++) {
                if (f0[i] < 50) {
                    editorF0[i] = 0;
                }
            }
            var samples = Worldline.WorldSynthesis(
                editorF0,
                sp, false, sp.GetLength(1),
                ap, false, fftSize,
                config.framePeriod, config.sampleRate,
                gender, tension, breathiness, voicing);
            var floats = samples.Select(d => (float)d).ToArray();
            Wave.CorrectSampleScale(floats);
            if (config.sampleRate != 44100) {
                var signal = new NWaves.Signals.DiscreteSignal(config.sampleRate, floats);
                signal = NWaves.Operations.Operation.Resample(signal, 44100);
                floats = signal.Samples;
            }
            Wave.WriteMono16Wav(paths.WavPath, floats);
        }

        /// <summary>
        /// The editor pitch in Hz for each server frame; 0 in the head and tail rests.
        /// </summary>
        double[] EditorF0(RenderPhrase phrase, EnunuConfig config, int frames) {
            var (headFrames, tailFrames) = HeadTailFrames(phrase, config);
            return SampleCurve(phrase, phrase.pitches, 0, config.framePeriod, frames, headFrames, tailFrames, x => MusicMath.ToneToFreq(x * 0.01));
        }

        /// <summary>
        /// ENUNUServer 2: pitch → acoustic_f0, so the voice follows the editor pitch.
        /// Models without an lf0_model ignore the editor pitch in acoustic_f0 but would still miss the
        /// server's cache on every pitch edit, so they use plain acoustic and get the pitch in synthe.
        /// </summary>
        void RunAcousticWithEditorPitch(RenderPhrase phrase, EnunuConfig config, EnunuPaths paths) {
            var connection = EnunuConnection.Inst;
            Log.Information($"Starting enunu acoustic \"{paths.UstPath}\"");
            EnsureUst(phrase, config, paths.UstPath);
            int frames = 0;
            if (connection.Lf0Conditioning(paths.VoicebankNameHash) != false) {
                frames = connection.Pitch(paths.UstPath, paths.VoicebankNameHash).result.n_frames;
            }
            if (connection.Lf0Conditioning(paths.VoicebankNameHash) == true) {
                connection.AcousticF0(paths.UstPath, paths.VoicebankNameHash, EditorF0(phrase, config, frames));
            } else {
                connection.Acoustic(paths.UstPath, paths.VoicebankNameHash);
            }
        }

        EnunuPaths PreparePaths(RenderPhrase phrase) {
            ulong hash = HashPhraseGroups(phrase);
            var tmpPath = Path.Join(PathManager.Inst.CachePath, $"enu-{hash:x16}");
            ulong wavHash = phrase.hash + hash;
            var wavPath = Path.Join(PathManager.Inst.CachePath, $"enu-{wavHash:x16}.wav");
            var voicebankNameHash = $"{(phrase.singer as EnunuSinger)!.voicebankNameHash:x16}";
            return new EnunuPaths(tmpPath, wavPath, voicebankNameHash);
        }

        // The UST path is a hash of its content, so an existing file already has the right notes.
        static void EnsureUst(RenderPhrase phrase, EnunuConfig config, string ustPath) {
            if (File.Exists(ustPath)) {
                return;
            }
            var enunuNotes = PhraseToEnunuNotes(phrase, config);
            // TODO: using first note tempo as ust tempo.
            EnunuUtils.WriteUst(enunuNotes, phrase.phones.First().tempo, phrase.singer, ustPath);
        }

        /// <summary>
        /// Lengths of the head and tail rests that the UST adds around the phrase.
        /// </summary>
        static (double headMs, double tailMs) HeadTailMs(RenderPhrase phrase) {
            var headMs = phrase.positionMs - phrase.timeAxis.TickPosToMsPos(phrase.position - headTicks);
            var tailMs = phrase.timeAxis.TickPosToMsPos(phrase.end + tailTicks) - phrase.endMs;
            return (headMs, tailMs);
        }

        (int headFrames, int tailFrames) HeadTailFrames(RenderPhrase phrase, EnunuConfig config) {
            var (headMs, tailMs) = HeadTailMs(phrase);
            return ((int)Math.Round(headMs / config.framePeriod), (int)Math.Round(tailMs / config.framePeriod));
        }

        /// <summary>
        /// Time of a server frame. Frame 0 is the start of the UST, whose first note is the
        /// headTicks-long rest before the phrase.
        /// </summary>
        static double FrameMs(RenderPhrase phrase, double framePeriod, int frame) {
            return phrase.timeAxis.TickPosToMsPos(phrase.position - headTicks) + frame * framePeriod;
        }

        double[] SampleCurve(RenderPhrase phrase, float[] curve, double defaultValue, double frameMs, int length, int headFrames, int tailFrames, Func<double, double> convert) {
            const int interval = 5;
            var result = new double[length];
            Array.Fill(result, defaultValue);
            if (curve == null) {
                return result;
            }
            for (int i = headFrames; i < length - tailFrames; i++) {
                int ticks = phrase.timeAxis.MsPosToTickPos(FrameMs(phrase, frameMs, i)) - (phrase.position - phrase.leading);
                int index = Math.Max(0, ticks / interval);
                if (index < curve.Length) {
                    result[i] = convert(curve[index]);
                }
            }
            return result;
        }

        public RenderPitchResult LoadRenderedPitch(RenderPhrase phrase) {
            lock (lockObj) {
                var features = EnunuConnection.Inst.GetFeatures();
                var paths = PreparePaths(phrase);
                var config = EnunuConfig.Load(phrase.singer);
                string f0Path = paths.F0Path;
                if (features?.SupportsPitch == true) {
                    // Only the pitch model runs, so this works before the phrase has been rendered.
                    try {
                        EnsureUst(phrase, config, paths.UstPath);
                        f0Path = EnunuConnection.Inst.Pitch(paths.UstPath, paths.VoicebankNameHash).result.path_f0;
                    } catch (Exception e) {
                        Log.Error(e, $"Failed to load the ENUNU pitch of \"{paths.UstPath}\"");
                        return null;
                    }
                }
                if (!File.Exists(f0Path)) {
                    return null;
                }
                return BuildPitchResult(phrase, config, np.Load<double[]>(f0Path));
            }
        }

        RenderPitchResult BuildPitchResult(RenderPhrase phrase, EnunuConfig config, double[] f0) {
            var tones = F0ToTones(f0);
            if (tones == null) {
                return null;
            }
            var ticks = new float[f0.Length];
            var voiced = new bool[f0.Length];
            int phone = 0;
            for (int i = 0; i < f0.Length; i++) {
                ticks[i] = phrase.timeAxis.MsPosToTickPos(FrameMs(phrase, config.framePeriod, i)) - phrase.position;
                // The head and tail rests and the rests inside a merged phrase are silence;
                // everything within the phonemes is written back.
                while (phone < phrase.phones.Length - 1 && ticks[i] >= phrase.phones[phone].end) {
                    phone++;
                }
                voiced[i] = ticks[i] >= phrase.phones[phone].position && ticks[i] < phrase.phones[phone].end;
            }
            return new RenderPitchResult() {
                ticks = ticks,
                tones = tones,
                voiced = voiced,
            };
        }

        /// <summary>
        /// Tone for every frame. Frames without pitch (f0 = 0: consonants, breaths) take the tone
        /// interpolated between the pitched frames around them, so loading the pitch also replaces
        /// the curve there instead of leaving what was drawn for the old notes. Null when no frame has pitch.
        /// </summary>
        internal static float[]? F0ToTones(double[] f0) {
            var tones = new float[f0.Length];
            int prev = -1;
            for (int i = 0; i < f0.Length; i++) {
                if (f0[i] <= 0) {
                    continue;
                }
                tones[i] = (float)MusicMath.FreqToTone(f0[i]);
                for (int k = prev + 1; k < i; k++) {
                    tones[k] = prev < 0
                        ? tones[i]
                        : tones[prev] + (tones[i] - tones[prev]) * (k - prev) / (i - prev);
                }
                prev = i;
            }
            if (prev < 0) {
                return null;
            }
            for (int k = prev + 1; k < f0.Length; k++) {
                tones[k] = tones[prev];
            }
            return tones;
        }

        static EnunuNote[] PhraseToEnunuNotes(RenderPhrase phrase, EnunuConfig config) {
            var notes = new List<EnunuNote>();
            notes.Add(new EnunuNote {
                lyric = "R",
                length = headTicks,
                noteNum = phrase.phones[0].tone,
            });
            for (int p = 0; p < phrase.phones.Length; p++) {
                var phone = phrase.phones[p];
                // Merged phrases (ShouldMergePhrases) have rests between their phonemes.
                if (p > 0 && phone.position > phrase.phones[p - 1].end) {
                    notes.Add(new EnunuNote {
                        lyric = "R",
                        length = phone.position - phrase.phones[p - 1].end,
                        noteNum = phrase.phones[p - 1].tone,
                    });
                }
                string timbre = string.Empty;
                string result = string.Empty;
                if (!string.IsNullOrEmpty(phone.suffix)) {
                    timbre = phone.suffix + "/";
                }

                foreach (var formatEntry in config.extensions.style_format) {
                    string key = formatEntry.Key;
                    var styleFormats = formatEntry.Value;
                    List<string> datas = new List<string>();
                    string part = key + ":";

                    bool hasMatch = false;
                    int i = System.Text.RegularExpressions.Regex.Matches(styleFormats.format, @"\{[^}]*\}").Count + 1;

                    foreach (var styleName in styleFormats.index) {

                        var matchingFlag = phone.flags.FirstOrDefault(f => {
                            var nameParts = f.Item1.Split('/');
                            return nameParts.Length > 1 && nameParts[0] == key && f.Item3 == styleName.ToLower();
                        });

                        if (matchingFlag != default) {
                            hasMatch = true;
                            datas.Add(matchingFlag.Item2.ToString());
                        }
                    }
                    if (i > 2 && datas.Count == (i-1)) {
                        part += String.Format(styleFormats.format, datas.ToArray());
                    } else {
                        Log.Warning($"Invalid format for {key}: {styleFormats.format}");
                    }

                    if (hasMatch) {
                        result += part + "/";
                    }
                }
                timbre += result;
                timbre = timbre.TrimEnd('/');
                notes.Add(new EnunuNote {
                    lyric = phone.phoneme,
                    length = phone.duration,
                    noteNum = phone.tone,
                    style_shift = phone.toneShift,
                    timbre = timbre,
                    velocity = (int)phone.velocity * 100,
                });
            }
            notes.Add(new EnunuNote {
                lyric = "R",
                length = tailTicks,
                noteNum = phrase.phones[^1].tone,
            });
            return notes.ToArray();
        }

        public UExpressionDescriptor[] GetSuggestedExpressions(USinger singer, URenderSettings renderSettings) {
            EnunuSinger? ensinger = singer as EnunuSinger;
            if (ensinger == null) {
                return null;
            }
            var config = EnunuConfig.Load(ensinger);
            var result = new List<UExpressionDescriptor>();
            foreach (var exp in config.extensions.styles) {
                result.Add(new UExpressionDescriptor(exp.Value.name, exp.Key, exp.Value.min, exp.Value.max, exp.Value.default_value, exp.Value.flag + "/" + exp.Key));
            }

            return result.ToArray();
        }

        public override string ToString() => Renderers.ENUNU;


        ulong HashPhraseGroups(RenderPhrase phrase) {
            using (var stream = new MemoryStream()) {
                using (var writer = new BinaryWriter(stream)) {
                    writer.Write(phrase.preEffectHash);
                    foreach (var phone in phrase.phones) {
                        // The phone hash has no position: without it, merged phrases that differ
                        // only in the length of a rest would share the UST.
                        writer.Write(phone.position);
                        writer.Write(phone.toneShift);
                        writer.Write(phone.velocity);
                    }
                    return XXH64.DigestOf(stream.ToArray());
                }
            }
        }
    }
}
