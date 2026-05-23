using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenUtau.Core.Format;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using Serilog;
using ThirdParty;

/*
 * This source code is partially based on the VOICEVOX engine.
 * https://github.com/VOICEVOX/voicevox_engine/blob/master/LGPL_LICENSE
 */

namespace OpenUtau.Core.Voicevox {
    public class VoicevoxRenderer : IRenderer {
        const string VOLC = VoicevoxUtils.VOLC;
        const string SMOC = VoicevoxUtils.SMOC;
        const string REPM = VoicevoxUtils.REPM;
        const string DUCM = VoicevoxUtils.DUCM;
        const string PITD = Format.Ustx.PITD;

        static readonly HashSet<string> supportedExp = new HashSet<string>(){
            Format.Ustx.DYN,
            PITD,
            Format.Ustx.CLR,
            Format.Ustx.VOL,
            //Format.Ustx.SHFC,
            Format.Ustx.SHFT,
            VOLC,
            SMOC,
            REPM,
            DUCM
        };

        static readonly object lockObj = new object();

        public USingerType SingerType => USingerType.Voicevox;

        public bool SupportsRenderPitch => true;

        public bool SupportsExpression(UExpressionDescriptor descriptor) {
            return supportedExp.Contains(descriptor.abbr);
        }

        public RenderResult Layout(RenderPhrase phrase) {
            return new RenderResult() {
                leadingMs = phrase.leadingMs,
                positionMs = phrase.positionMs - ((VoicevoxUtils.headS * 1000)),
                estimatedLengthMs = phrase.durationMs + phrase.leadingMs,
            };
        }

        public Task<RenderResult> Render(RenderPhrase phrase, Progress progress, int trackNo, CancellationTokenSource cancellation, bool isPreRender) {
            var task = Task.Run(() => {
                lock (lockObj) {
                    if (cancellation.IsCancellationRequested) {
                        return new RenderResult();
                    }
                    string progressInfo = $"Track {trackNo + 1}: {this} \"{string.Join(" ", phrase.phones.Select(p => p.phoneme))}\"";
                    progress.Complete(0, progressInfo);
                    var wavPath = Path.Join(PathManager.Inst.CachePath, $"vv-{phrase.hash:x16}.wav");
                    phrase.AddCacheFile(wavPath);
                    var result = Layout(phrase);
                    if (!File.Exists(wavPath)) {
                        var singer = phrase.singer as VoicevoxSinger;
                        if (singer != null) {
                            if (VoicevoxUtils.dic == null) {
                                VoicevoxUtils.Loaddic(singer);
                            }
                            try {
                                VoicevoxSynthParams vsParams = PhraseToVoicevoxSynthParams(phrase, phrase.singer as VoicevoxSinger);

                                int vvTotalFrames = 0;
                                double frameMs = (1000d / VoicevoxUtils.fps);
                                vsParams.phonemes.ForEach(x => vvTotalFrames += x.frame_length);
                                if (!phrase.phones[0].direct) {
                                    vsParams.f0 = VoicevoxUtils.SampleCurve(phrase, phrase.pitches, 0, frameMs, vvTotalFrames, vsParams.phonemes[0].frame_length, vsParams.phonemes[^1].frame_length, 0, x => MusicMath.ToneToFreq(x * 0.01)).ToList();
                                } else {
                                    //vsParams.f0 = ToneShift(phrase, vsParams);
                                    vsParams.f0 = vsParams.f0.Select(f0 => f0 = f0 * Math.Pow(2, ((phrase.phones[0].toneShift * -1) / 12d))).ToList();
                                }

                                //Volume parameter for synthesis. Scheduled to be revised
                                var volumeCurve = phrase.curves.FirstOrDefault(c => c.Item1 == VOLC);
                                if (volumeCurve != null) {
                                    var volumes = VoicevoxUtils.SampleCurve(phrase, volumeCurve.Item2, 0, frameMs, vvTotalFrames, vsParams.phonemes[0].frame_length, vsParams.phonemes[^1].frame_length, -10, x => x * 0.01);
                                    vsParams.volume = vsParams.volume.Select((vol, i) => vol = vol * volumes[i]).ToList();
                                } else {
                                    vsParams.volume = vsParams.volume.Select(vol => vol = vol * phrase.phones[0].volume).ToList();
                                }
                                for (int i = 0; i < vsParams.phonemes[0].frame_length; i++) {
                                    vsParams.volume[i] = 0;
                                }
                                for (int i = vsParams.volume.Count - vsParams.phonemes[vsParams.phonemes.Count - 1].frame_length; i < vsParams.volume.Count; i++) {
                                    vsParams.volume[i] = 0;
                                }

                                if (vsParams.phonemes.Count() > 0) {
                                    result.positionMs = phrase.positionMs - phrase.timeAxis.TickPosToMsPos((vsParams.phonemes.First().frame_length / VoicevoxUtils.fps) * 1000d);
                                }

                                int speaker = 0;
                                singer.voicevoxConfig.styles.ForEach(style => {
                                    if (style.name.Equals(phrase.singer.Subbanks[1].Suffix) && style.type.Equals("frame_decode")) {
                                        speaker = style.id;
                                    }
                                    if (style.name.Equals(phrase.phones[0].suffix) && style.type.Equals("frame_decode")) {
                                        speaker = style.id;
                                    } else if ((style.name + "_" + style.type).Equals(phrase.phones[0].suffix)) {
                                        speaker = style.id;
                                    }
                                });
                                var queryurl = new VoicevoxURL() { method = "POST", path = "/frame_synthesis", query = new Dictionary<string, string> { { "speaker", speaker.ToString() } }, body = JsonConvert.SerializeObject(vsParams), accept = "audio/wav" };
                                var response = VoicevoxClient.Inst.SendRequest(queryurl);
                                byte[] bytes = null;
                                if (!response.Item2.Equals(null)) {
                                    bytes = response.Item2;
                                } else if (!string.IsNullOrEmpty(response.Item1)) {
                                    var jObj = JObject.Parse(response.Item1);
                                    if (jObj.ContainsKey("detail")) {
                                        Log.Error($"Voice synthesis failed with the VOICEVOX engine. : {jObj}");
                                    }
                                }
                                if (bytes != null) {
                                    File.WriteAllBytes(wavPath, bytes);
                                }
                            } catch (Exception e) {
                                Log.Error(e, "Failed to create a voice base.");
                            }
                            if (cancellation.IsCancellationRequested) {
                                return new RenderResult();
                            }
                        }
                    }
                    progress.Complete(phrase.phones.Length, progressInfo);
                    if (File.Exists(wavPath)) {
                        using (var waveStream = new WaveFileReader(wavPath)) {

                            result.samples = Wave.GetSamples(waveStream.ToSampleProvider().ToMono(1, 0));
                        }
                        if (result.samples != null) {
                            Renderers.ApplyDynamics(phrase, result);
                        }
                    }
                    return result;
                }
            });
            return task;
        }

        private VoicevoxSynthParams PhraseToVoicevoxSynthParams(RenderPhrase phrase, VoicevoxSinger singer) {

            VoicevoxSynthParams vsParams = new VoicevoxSynthParams();
            //Prepare for future additions of Teacher Singer.
            string baseSingerID = VoicevoxUtils.getBaseSingerID(singer);

            if (phrase.phones.All(p => VoicevoxUtils.IsDicKana(p.phoneme) || VoicevoxUtils.IsDicPau(p.phoneme))) {
                // TODO: slur support
                List<VoicevoxNote> vnotes = new List<VoicevoxNote>();
                //if (slur) {
                for (int i = 0; i < phrase.phones.Length; i++) {
                    vnotes.Add(new VoicevoxNote() {
                        lyric = phrase.phones[i].phoneme,
                        positionMs = phrase.phones[i].positionMs,
                        durationMs = phrase.phones[i].durationMs,
                        tone = (int)(phrase.phones[i].tone + phrase.phones[i].toneShift)
                    });
                }
                //} else {
                //}

                VoicevoxQueryMain vqMain = VoicevoxUtils.NoteGroupsToVQuery(vnotes.ToArray(), phrase.timeAxis);

                vsParams = VoicevoxUtils.VoicevoxVoiceBase(vqMain, baseSingerID);
            } else if (phrase.phones.All(p => VoicevoxUtils.IsVowel(p.phoneme) || VoicevoxUtils.IsConsonant(p.phoneme))) {
                List<VoicevoxNote> vnotes = new List<VoicevoxNote>();
                for (int i = 0; i < phrase.notes.Length; i++) {
                    var durationMs = phrase.notes[i].durationMs;
                    var currentLyric = phrase.notes[i].lyric.Normalize();
                    var lyricList = currentLyric.Split(" ");
                    var shiftTone = phrase.phones[0].toneShift;
                    if (lyricList.Length > 1) {
                        currentLyric = lyricList[1];
                    }
                    if (!VoicevoxUtils.IsSyllableVowelExtensionNote(currentLyric)) {
                        if (VoicevoxUtils.IsDicPau(currentLyric)) {
                            currentLyric = string.Empty;
                        } else if (VoicevoxUtils.dic.IsDic(currentLyric)) {
                            currentLyric = VoicevoxUtils.dic.Lyrictodic(currentLyric);
                        } else if (!VoicevoxUtils.IsDicKana(currentLyric)) {
                            currentLyric = string.Empty;
                        }
                    } else if (vnotes.Count >= i - 1 && 0 <= i - 1) {
                        // TODO: slur support
                        //if (slur) {
                        var tempNote = vnotes[vnotes.Count - 1];
                        tempNote.durationMs += durationMs;
                        vnotes[vnotes.Count - 1] = tempNote;
                        continue;
                        //} else {
                        //    if (VoicevoxUtils.phoneme_List.kanas.TryGetValue(vnotes[i - 1].lyric, out string str)) {
                        //        currentLyric = str;
                        //    }
                        //}
                    }
                    vnotes.Add(new VoicevoxNote() {
                        lyric = currentLyric,
                        positionMs = phrase.notes[i].positionMs,
                        durationMs = durationMs,
                        tone = (int)(phrase.notes[i].tone + shiftTone)
                    });
                }
                //Match the phonemes in the synthesis parameters to the scores in the score to update F0 and volume  
                //Create parameters for the update source. 
                VoicevoxQueryMain vqMain = VoicevoxUtils.NoteGroupsToVQuery(vnotes.ToArray(), phrase.timeAxis);
                VoicevoxSynthParams vsParams_1 = VoicevoxUtils.VoicevoxVoiceBase(vqMain, baseSingerID);

                //Create parameters for the update destination.
                vsParams = PhonemeToVoicevoxSynthParams(phrase);
                VoicevoxSynthParams vsParams_2 = vsParams.Clone();


                if (vsParams.phonemes.Count == vsParams_1.phonemes.Count) {
                    for (int i = 0; i < vsParams_1.phonemes.Count; i++) {
                        // TODO: Develop a VOICEVOX engine dedicated to OpenUtau so that synthesis parameters are updated when phonemes are changed.
                        //var flag = phrase.phones[i].flags.FirstOrDefault(f => f.Item1 == VoicevoxUtils.REPM);
                        //if (flag != null) {
                        //    if (flag.Item3.Equals(VoicevoxUtils.REPLACE)) {
                        vsParams.phonemes[i].phoneme = vsParams_1.phonemes[i].phoneme;
                        //    }
                        //}
                    }
                }
                //Update F0 and volume
                vsParams.f0 = VoicevoxUtils.QueryToF0(vqMain, vsParams, baseSingerID);
                vsParams.volume = VoicevoxUtils.QueryToVolume(vqMain, vsParams, baseSingerID);
                //Update phoneme
                for (int i = 0; i < vsParams_2.phonemes.Count; i++) {
                    //var flag = phrase.phones[i].flags.FirstOrDefault(f => f.Item1 == VoicevoxUtils.REPM);
                    //if (flag != null) {
                    //    if (flag.Item3.Equals(VoicevoxUtils.REPLACE)) {
                    vsParams.phonemes[i].phoneme = vsParams_2.phonemes[i].phoneme;
                    //    }
                    //}
                }
            } else {
                throw new MessageCustomizableException(
                    $"Failed to create a voice base. The phoneme is not supported by the VOICEVOX engine.\n{string.Join(" ", phrase.phones.Select(p => p.phoneme))}",
                    $"You are confusing phonemes and hiragana.\n{string.Join(" ", phrase.phones.Select(p => p.phoneme))}", new VoicevoxException());
            }
            return vsParams;
        }

        private VoicevoxSynthParams PhonemeToVoicevoxSynthParams(RenderPhrase phrase) {
            VoicevoxSynthParams vsParams = new VoicevoxSynthParams();
            int headFrames = (int)Math.Round((VoicevoxUtils.headS * VoicevoxUtils.fps), MidpointRounding.AwayFromZero);
            int tailFrames = (int)Math.Round((VoicevoxUtils.tailS * VoicevoxUtils.fps), MidpointRounding.AwayFromZero);
            try {
                vsParams.phonemes.Add(new Phonemes() {
                    phoneme = "pau",
                    frame_length = headFrames
                });
                int short_length_count = 0;
                for (int i = 0; i < phrase.phones.Length; i++) {
                    double durationMs = phrase.phones[i].durationMs;
                    int length = (int)Math.Round((durationMs / 1000f) * VoicevoxUtils.fps, MidpointRounding.AwayFromZero);
                    if (length < 2) {
                        length = 2;
                    }
                    int correction = 0;
                    var flag = phrase.phones[i].flags.FirstOrDefault(f => f.Item3.Equals(DUCM));
                    if (flag != null) {
                        switch (flag.Item1) {
                            case VoicevoxUtils.ON:
                                correction = 1;
                                break;
                            case VoicevoxUtils.OFF:
                                correction = 2;
                                break;
                        }
                    }
                    if (correction != 2) {
                        if (durationMs > (length / VoicevoxUtils.fps) * 1000f) {
                            if (short_length_count >= 2) {
                                length += 1;
                                short_length_count = 0;
                            } else {
                                short_length_count += 1;
                            }
                        } else if (correction == 1) {
                            length += 1;
                        }
                    }
                    vsParams.phonemes.Add(new Phonemes() {
                        phoneme = phrase.phones[i].phoneme,
                        frame_length = length
                    });
                }
                vsParams.phonemes.Add(new Phonemes() {
                    phoneme = "pau",
                    frame_length = tailFrames
                });
            } catch (Exception e) {
                throw new VoicevoxException("Failed to create a voice base.", e);
            }

            int totalFrames = 0;
            vsParams.phonemes.ForEach(x => totalFrames += x.frame_length);

            vsParams.f0 = Enumerable.Repeat(0.0, totalFrames).ToList();
            vsParams.volume = Enumerable.Repeat(0.0, totalFrames).ToList();
            return vsParams;
        }

        // TODO: Development of a more stable tone shift
        //private List<double> ToneShift(RenderPhrase phrase, VoicevoxSynthParams vsParams) {
        //    //Compatible with toneShift (key shift), for adjusting the range of tones when synthesizing
        //    List<double> result = new List<double>();
        //    if (IsPhonemeNoteCountMatch(phrase) && phrase.phones.All(p => VoicevoxUtils.phoneme_List.kanas.ContainsKey(p.phoneme))) {
        //        List<int> shifts = new List<int>() { 0 };
        //        shifts.AddRange(phrase.phones.Select(x => x.toneShift).ToList());
        //        shifts.Add(0);
        //        int totalFrames = 0;
        //        int shiftidx = 0;
        //        for (int i = 0; i <= vsParams.phonemes.Count - 1; i++) {
        //            var f0 = vsParams.f0.GetRange(totalFrames, vsParams.phonemes[i].frame_length);
        //            f0 = f0.Select(f0 => f0 = f0 * Math.Pow(2, ((shifts[shiftidx] * -1) / 12d))).ToList();
        //            result.AddRange(f0);
        //            totalFrames += vsParams.phonemes[i].frame_length;
        //            if (VoicevoxUtils.IsVowel(vsParams.phonemes[i].phoneme) && shiftidx <= shifts.Count) {
        //                shiftidx += 1;
        //            }
        //        }
        //        Log.Debug($"ToneShift_Count: {shifts.Count},Phonemes_Count: {vsParams.phonemes.Count},vsParams_length: {vsParams.volume.Count},totalFrames: {totalFrames}");
        //    } else {
        //        List<int> shifts = new List<int>() { 0 };
        //        shifts.AddRange(phrase.phones.Select(x => x.toneShift).ToList());
        //        shifts.Add(0);
        //        int totalFrames = 0;
        //        for (int i = 0; i <= Math.Min(vsParams.phonemes.Count, shifts.Count) - 1; i++) {
        //            var f0 = vsParams.f0.GetRange(totalFrames, vsParams.phonemes[i].frame_length);
        //            f0 = f0.Select(f0 => f0 = f0 * Math.Pow(2, ((shifts[i] * -1) / 12d))).ToList();
        //            result.AddRange(f0);
        //            totalFrames += vsParams.phonemes[i].frame_length;
        //        }
        //        Log.Debug($"ToneShift_Count: {shifts.Count},Phonemes_Count: {vsParams.phonemes.Count},vsParams_length: {vsParams.volume.Count},totalFrames: {totalFrames}");
        //    }
        //    return result;
        //}

        public UExpressionDescriptor[] GetSuggestedExpressions(USinger singer, URenderSettings renderSettings) {
            //under development
            var result = new List<UExpressionDescriptor> {
                //volumes
                new UExpressionDescriptor{
                    name="input volume (curve)",
                    abbr=VOLC,
                    type=UExpressionType.Curve,
                    min=0,
                    max=200,
                    defaultValue=100,
                    isFlag = false,
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
                //phoneme replace mode
                //new UExpressionDescriptor{
                //    name = "phoneme replace mode",
                //    abbr = REPM,
                //    type = UExpressionType.Options,
                //    options = new string[] { VoicevoxUtils.REPLACE, VoicevoxUtils.OVERWRITE},
                //    isFlag = false,
                //},
                //duration correction mode
                new UExpressionDescriptor{
                    name = "duration correction mode",
                    abbr = DUCM,
                    type = UExpressionType.Options,
                    options = new string[] { VoicevoxUtils.AUTO, VoicevoxUtils.ON, VoicevoxUtils.OFF},
                    isFlag = true,
                },
            };

            return result.ToArray();
        }

        public override string ToString() => Renderers.VOICEVOX;

        RenderPitchResult IRenderer.LoadRenderedPitch(RenderPhrase phrase) {
            try {
                var singer = phrase.singer as VoicevoxSinger;
                if (singer != null) {

                    string baseSingerID = VoicevoxUtils.getBaseSingerID(singer);
                    VoicevoxSynthParams vsParams = PhraseToVoicevoxSynthParams(phrase, phrase.singer as VoicevoxSinger/*, true*/);
                    double frameMs = (1000d / VoicevoxUtils.fps);
                    int vvTotalFrames = 0;
                    vsParams.phonemes.ForEach(x => vvTotalFrames += x.frame_length);
                    //vsParams.f0 = ToneShift(phrase, vsParams);
                    vsParams.f0 = vsParams.f0.Select(f0 => f0 = f0 * Math.Pow(2, ((phrase.phones[0].toneShift * -1) / 12d))).ToList();
                    List<double> f0 = vsParams.f0;


                    var exprCurve = phrase.curves.FirstOrDefault(curve => curve.Item1.Equals(SMOC));
                    if (exprCurve != null) {
                        List<int> exprs = VoicevoxUtils.SampleCurve(phrase, exprCurve.Item2, 0, frameMs, vvTotalFrames, vsParams.phonemes[0].frame_length, vsParams.phonemes[^1].frame_length, -(VoicevoxUtils.headS + 10), x => x).Select(x => (int)x).ToList();
                        var f0S = new F0Smoother(f0);
                        f0S.SmoothenWidthList = exprs;
                        f0 = f0S.GetSmoothenedF0List(f0);
                    }

                    var result = new RenderPitchResult {
                        tones = f0.Select(value => (float)MusicMath.FreqToTone(value)).ToArray(),
                        ticks = new float[vvTotalFrames]
                    };
                    var layout = Layout(phrase);
                    var t = layout.positionMs - layout.leadingMs;
                    for (int i = 0; i < result.tones.Length; i++) {
                        t += (1000d / VoicevoxUtils.fps);
                        result.ticks[i] = phrase.timeAxis.MsPosToTickPos(t) - phrase.position;
                    }
                    return result;
                }
            } catch (Exception e) {
                throw new VoicevoxException("Failed to create pitch data.", e);
            }
            return null;
        }
    }
}
