using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using K4os.Hash.xxHash;
using NAudio.Wave;
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
        const string PITD = Format.Ustx.PITD;

        static readonly HashSet<string> supportedExp = new HashSet<string>(){
            Format.Ustx.DYN,
            PITD,
            Format.Ustx.CLR,
            Format.Ustx.VOL,
            Format.Ustx.DIR,
            Format.Ustx.SHFT,
            VOLC,
            SMOC,
            REPM,
        };

        static readonly object lockObj = new object();

        public USingerType SingerType => USingerType.Voicevox;

        public bool SupportsRenderPitch => true;

        public bool SupportsExpression(UExpressionDescriptor descriptor) {
            return supportedExp.Contains(descriptor.abbr);
        }

        public RenderResult Layout(RenderPhrase phrase) {
            double frameMs = 1000.0 / VoicevoxUtils.fps;
            int headFrames = (int)Math.Round(VoicevoxUtils.headS * VoicevoxUtils.fps);
            const int AlignmentFrames = 1;

            double correctionMs = (headFrames + AlignmentFrames) * frameMs;

            return new RenderResult() {
                leadingMs = phrase.leadingMs,
                positionMs = phrase.positionMs - correctionMs,
                estimatedLengthMs = phrase.durationMs + phrase.leadingMs,
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
                    ulong toneHash = HashPhraseGroups(phrase);
                    var wavPath = Path.Join(PathManager.Inst.CachePath, $"vv-{phrase.hash:x16}_{toneHash:x16}.wav");
                    phrase.AddCacheFile(wavPath);
                    var result = Layout(phrase);
                    if (!File.Exists(wavPath)) {
                        var singer = phrase.singer as VoicevoxSinger;
                        if (singer != null) {
                            if (VoicevoxUtils.dic == null) {
                                VoicevoxUtils.Loaddic(singer);
                            }
                            try {
                                VoicevoxSynthParams vsParams = PhraseToVoicevoxSynthParams(phrase, phrase.singer as VoicevoxSinger, false);

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

                                int speakerID = 0;
                                singer.voicevoxConfig.styles.ForEach(style => {
                                    if (style.name.Equals(phrase.singer.Subbanks[1].Suffix) && style.type.Equals("frame_decode")) {
                                        speakerID = style.id;
                                    }
                                    // Apply the voice color setting value
                                    if (style.name.Equals(phrase.phones[0].suffix) && style.type.Equals("frame_decode")) {
                                        speakerID = style.id;
                                    } else // Supports styles with the same name but different types
                                    if ((style.name + "_" + style.type).Equals(phrase.phones[0].suffix)) {
                                        speakerID = style.id;
                                    }
                                });
                                VoicevoxUtils.InitializedSpeaker(speakerID.ToString(), false);
                                var queryurl = new VoicevoxURL() { method = "POST", path = "/frame_synthesis", query = new Dictionary<string, string> { { "speaker", speakerID.ToString() } }, body = Json.Serialize(vsParams), accept = "audio/wav" };
                                var response = VoicevoxClient.Inst.SendRequest(queryurl);
                                byte[] bytes = null;
                                if (!response.Item2.Equals(null)) {
                                    bytes = response.Item2;
                                } else if (!string.IsNullOrEmpty(response.Item1)) {
                                    var jObj = JsonObject.Parse(response.Item1);
                                    if (jObj?.AsObject().ContainsKey("detail") == true) {
                                        Log.Error($"Voice synthesis failed with the VOICEVOX engine. : {jObj}");
                                    }
                                }
                                if (bytes != null) {
                                    File.WriteAllBytes(wavPath, bytes);
                                }
                            } catch (MessageCustomizableException) {
                                //BuildVNotes has already built a message for the user.
                                throw;
                            } catch (VoicevoxException e) {
                                throw new MessageCustomizableException("Failed to create the audio.", e.Message, e);
                            } catch (Exception e) {
                                Log.Error(e, "Failed to create the audio.");
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
                            PlaybackManager.Inst.LiveWaveformCache[phrase.hash.ToString()] = (trackNo, phrase.positionMs - phrase.leadingMs, result.samples, DateTime.Now);
                            Task.Factory.StartNew(() => {
                                DocManager.Inst.ExecuteCmd(new WaveformReadyNotification());
                            }, CancellationToken.None, TaskCreationOptions.None, DocManager.Inst.MainScheduler);
                        }
                    }
                    return result;
                }
            });
            return task;
        }

        //Match the phonemes in the synthesis parameters to the scores in the score to update F0 and volume  
        private VoicevoxSynthParams PhraseToVoicevoxSynthParams(RenderPhrase phrase, VoicevoxSinger singer, bool pitch_slur) {

            //Prepare for future additions of Teacher Singer.
            string baseSingerID = VoicevoxUtils.getBaseSingerID(singer);
            VoicevoxUtils.InitializedSpeaker(baseSingerID, true);
            List<VoicevoxNote> vNotes = BuildVNotes(phrase);

            //Create parameters for the update source. 
            VoicevoxQueryMain vqMain = VoicevoxUtils.NoteGroupsToVQuery(vNotes.ToArray(), phrase.timeAxis);
            VoicevoxSynthParams vsParams;
            if (IsPhonemeNoteCountMatch(phrase)) {
                vsParams = VoicevoxUtils.VoicevoxVoiceBase(vqMain, baseSingerID);
            } else {
                //vsParamsServer is a parameter to hold phonemes generated from note lyrics
                VoicevoxSynthParams vsParamsServer = VoicevoxUtils.VoicevoxVoiceBase(vqMain, baseSingerID);

                //Create parameters for the update destination.
                vsParams = PhonemeToVoicevoxSynthParams(phrase);
                //vsParamsUser is a parameter to hold phonemes changed by the user
                VoicevoxSynthParams vsParamsUser = vsParams.Clone();
                if (vsParams.phonemes.Count == vsParamsServer.phonemes.Count) {
                    for (int i = 0; i < vsParamsServer.phonemes.Count; i++) {
                        vsParams.phonemes[i].phoneme = vsParamsServer.phonemes[i].phoneme;
                    }
                }
                //Update F0 and volume
                vsParams.f0 = VoicevoxUtils.QueryToF0(vqMain, vsParams, baseSingerID);
                vsParams.volume = VoicevoxUtils.QueryToVolume(vqMain, vsParams, baseSingerID);
                //Update phoneme
                for (int i = 0; i < vsParamsUser.phonemes.Count; i++) {
                    vsParams.phonemes[i].phoneme = vsParamsUser.phonemes[i].phoneme;
                }
            }
            if (pitch_slur) {
                VoicevoxUtils.AdjustF0ForSlur(VoicevoxUtils.NoteGroupsToVQuery(vNotes.ToArray(), phrase.timeAxis, true), vsParams.f0);
            }
            return vsParams;
        }

        private List<VoicevoxNote> BuildVNotes(RenderPhrase phrase) {
            List<VoicevoxNote> vNotes = new List<VoicevoxNote>();
            try {
                for (int i = 0; i < phrase.notes.Length; i++) {
                    var durationMs = phrase.notes[i].durationMs;
                    var currentLyric = phrase.notes[i].lyric.Normalize();
                    var lyricList = currentLyric.Split(" ");
                    var shiftTone = phrase.phones[0].toneShift;
                    if (!VoicevoxUtils.IsSyllableVowelExtensionNote(lyricList[^1])) {
                        if (VoicevoxUtils.IsPau(lyricList[^1])) {
                            currentLyric = string.Empty;
                        } else if (VoicevoxUtils.dic.IsDic(lyricList[^1])) {
                            currentLyric = VoicevoxUtils.dic.Lyrictodic(lyricList[^1]);
                        } else if (VoicevoxUtils.IsKana(lyricList[^1])) {
                            currentLyric = lyricList[^1];
                        } else {
                            currentLyric = string.Empty;
                        }
                    }
                    vNotes.Add(new VoicevoxNote() {
                        lyric = currentLyric,
                        positionMs = phrase.notes[i].positionMs,
                        durationMs = durationMs,
                        tone = (int)(phrase.notes[i].tone + shiftTone)
                    });
                }
            } catch (Exception e) {
                var phonemeText = string.Join(" ", phrase.phones.Select(p => p.phoneme));
                throw new MessageCustomizableException(
                    $"Failed to create a voice base. One or more phonemes may not be supported by the VOICEVOX engine.\n{phonemeText}",
                    $"An error occurred while creating a voice base from the current phrase. This may be caused by unsupported phonemes, invalid input, or a mismatch between phonemes and hiragana.\nPhonemes: {phonemeText}\nDetails: {e.Message}",
                    new VoicevoxException());
            }

            return vNotes;
        }

        private bool IsPhonemeNoteCountMatch(RenderPhrase phrase) {
            return phrase.phones.Length == phrase.notes.Where(note => !VoicevoxUtils.IsSyllableVowelExtensionNote(note.lyric)).Count() && phrase.phones.All(p => VoicevoxUtils.phoneme_List.kanas.ContainsKey(p.phoneme));
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
                //Holds the end frame of the previous phoneme so that phonemes stay contiguous.
                int cursor = phrase.phones.Length > 0
                    ? (int)Math.Round((phrase.phones[0].positionMs / 1000.0) * VoicevoxUtils.fps, MidpointRounding.AwayFromZero)
                    : 0;
                for (int i = 0; i < phrase.phones.Length; i++) {
                    double endMs = phrase.phones[i].positionMs + phrase.phones[i].durationMs;

                    int startFrame = cursor;
                    int endFrame = VoicevoxUtils.ToEndFrame(
                        startFrame,
                        endMs,
                        VoicevoxUtils.IsPlosive(phrase.phones[i].phoneme),
                        VoicevoxUtils.minFrames);

                    int length = endFrame - startFrame;
                    cursor = endFrame;

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
            };

            return result.ToArray();
        }

        public override string ToString() => Renderers.VOICEVOX;

        RenderPitchResult IRenderer.LoadRenderedPitch(RenderPhrase phrase) {
            try {
                var singer = phrase.singer as VoicevoxSinger;
                if (singer != null) {
                    // TODO: Support Teacher Singer in the future
                    // string baseSingerID = VoicevoxUtils.getBaseSingerID(singer);
                    VoicevoxSynthParams vsParams = PhraseToVoicevoxSynthParams(phrase, phrase.singer as VoicevoxSinger, true);
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

        ulong HashPhraseGroups(RenderPhrase phrase) {
            using (var stream = new MemoryStream()) {
                using (var writer = new BinaryWriter(stream)) {
                    writer.Write(phrase.preEffectHash);
                    writer.Write(phrase.phones[0].tone);
                    writer.Write(phrase.phones[0].direct);
                    //phrase.hash does not cover the SHFT expression, so it has to be mixed in here.
                    writer.Write(phrase.phones[0].toneShift);
                    foreach (var phone in phrase.phones) {
                        writer.Write(phone.tone);
                    }
                    writer.Write(phrase.phones[0].volume);
                    return XXH64.DigestOf(stream.ToArray());
                }
            }
        }

    }
}
