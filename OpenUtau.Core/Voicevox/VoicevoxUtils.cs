using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenUtau.Core.Render;
using Serilog;

/*
 * This source code is partially based on the VOICEVOX engine.
 * https://github.com/VOICEVOX/voicevox_engine/blob/master/LGPL_LICENSE
 */

namespace OpenUtau.Core.Voicevox {

    public class VoicevoxException : Exception {
        public VoicevoxException() { }
        public VoicevoxException(string message) : base(message) { }
        public VoicevoxException(string message, Exception innerException) : base(message, innerException) { }
    }

    public struct VoicevoxNote {
        public string lyric;
        public double positionMs;
        public double durationMs;
        public int tone;
    }

    public class Phonemes {
        public string phoneme;
        public int frame_length;

        public Phonemes Clone() {
            return new Phonemes {
                phoneme = this.phoneme,
                frame_length = this.frame_length
            };
        }
    }
    public class VoicevoxSynthParams {
        public List<double> f0 = new List<double>();
        public List<double> volume = new List<double>();
        public List<Phonemes> phonemes = new List<Phonemes>();
        public int volumeScale = 1;
        public int outputSamplingRate = 24000;
        public bool outputStereo = false;

        public VoicevoxSynthParams Clone() {
            return new VoicevoxSynthParams() {
                f0 = new List<double>(this.f0),
                volume = new List<double>(this.volume),
                phonemes = this.phonemes.Select(p => p.Clone()).ToList(),
                volumeScale = this.volumeScale,
                outputSamplingRate = this.outputSamplingRate,
                outputStereo = this.outputStereo
            };
        }
    }

    public class VoicevoxQueryNotes {
        public int? key;
        public int frame_length;
        public string lyric;
        // Index for Phonemizer
        public int vqnindex;
        // Index for Slur
        public int slur_index;
        public override string ToString() {
            return $"vqnindex:{vqnindex}: lyric:{lyric}, key:{key}, frame_length:{frame_length}";
        }
    }

    public class VoicevoxQueryMain {
        public List<VoicevoxQueryNotes> notes = new List<VoicevoxQueryNotes>();
    }

    public class VoicevoxQueryParams {
        public VoicevoxQueryMain score = new VoicevoxQueryMain();
        public VoicevoxSynthParams frame_audio_query = new VoicevoxSynthParams();
    }

    public class Phoneme_list {
        public string[] vowels = "a i u e o A I U E O N pau cl".Split();
        public string[] consonants = "b by ch d dy f g gw gy h hy j k kw ky m my n ny p py r ry s sh t ts ty v w y z".Split();
        public Dictionary<string, string> kanas = new Dictionary<string, string>();
        public Dictionary<string, string> paus = new Dictionary<string, string>();
        public Phoneme_list() {
            var kanaGroups = new List<string[]> {
                "あ ぁ ば びゃ ゔゃ ヴャ ちゃ だ   でゃ ふぁ が ぐゎ ぐぁ ぎゃ は ひゃ じゃ ぢゃ か くゎ くぁ きゃ ま みゃ な にゃ ぱ ぴゃ ら りゃ さ   しゃ た   つぁ てゃ ゔぁ ヴァ わ   ゎ や  ゃ ざ".Split(),
                "い ぃ び びぃ           ち   でぃ      ふぃ ぎ ぐぃ      ぎぃ ひ      じ   ぢ   き くぃ      きぃ み みぃ に      ぴ ぴぃ り りぃ すぃ し   てぃ つぃ      ゔぃ ヴィ うぃ           ずぃ".Split(),
                "う ぅ ぶ びゅ ゔゅ ヴュ ちゅ どぅ でゅ ふ   ぐ ぐぅ      ぎゅ    ひゅ じゅ ぢゅ く くぅ      きゅ む みゅ ぬ にゅ ぷ ぴゅ る りゅ す   しゅ とぅ つ   てゅ ゔ   ヴ   うぅ    ゆ  ゅ ず".Split(),
                "え ぇ べ びぇ           ちぇ で   でぇ ふぇ げ ぐぇ      ぎぇ へ ひぇ じぇ ぢぇ け くぇ      きぇ め みぇ ね にぇ ぺ ぴぇ れ りぇ せ   しぇ て   つぇ てぇ ゔぇ ヴェ うぇ    いぇ   ぜ".Split(),
                "お ぉ ぼ びょ ゔょ ヴョ ちょ ど   でょ ふぉ ご ぐぉ      ぎょ ほ ひょ じょ ぢょ こ くぉ      きょ も みょ の にょ ぽ ぴょ ろ りょ そ   しょ と   つぉ てょ ゔぉ ヴォ うぉ を よ  ょ ぞ".Split(),
                "ん".Split(),
                "っ".Split()
            };

            foreach (var group in kanaGroups) {
                foreach (var kana in group) {
                    if (!kanas.ContainsKey(kana)) {
                        kanas.Add(kana.Normalize(), group[0].Normalize());
                    }
                }
            }
            string[] pauseGroups = "R pau AP SP".Split();

            foreach (string group in pauseGroups) {
                if (!paus.ContainsKey(group)) {
                    paus.Add(group.Normalize(), pauseGroups[0].Normalize());
                }
            }
        }
    }

    public class Dictionary_list {
        public Dictionary<string, string> dict = new Dictionary<string, string>();

        public void Loaddic(string location) {
            try {
                var parentDirectory = Directory.GetParent(location).ToString();
                var yamlPath = Path.Join(parentDirectory, "dictionary.yaml");
                if (File.Exists(yamlPath)) {
                    var yamlTxt = File.ReadAllText(yamlPath);
                    var yamlObj = Yaml.DefaultDeserializer.Deserialize<Dictionary<string, List<Dictionary<string, string>>>>(yamlTxt);
                    var list = yamlObj["list"];
                    dict = new Dictionary<string, string>();

                    foreach (var item in list) {
                        foreach (var pair in item) {
                            dict[pair.Key] = pair.Value;
                        }
                    }

                }
            } catch (Exception e) {
                Log.Error($"Failed to read dictionary file. : {e}");
            }
        }
        public string Notetodic(VoicevoxNote[] notes, int index) {
            if (dict.TryGetValue(notes[index].lyric, out var lyric_)) {
                if (string.IsNullOrEmpty(lyric_)) {
                    return "";
                }
                return lyric_;
            }
            return notes[index].lyric;
        }

        public string Lyrictodic(string lyric) {
            if (dict.TryGetValue(lyric, out var lyric_)) {
                if (string.IsNullOrEmpty(lyric_)) {
                    return "";
                }
                return lyric_;
            }
            return lyric;
        }

        public bool IsDic(string lyric) {
            return dict.ContainsKey(lyric);
        }
    }


    public static class VoicevoxUtils {
        // expression addr
        public const string VOLC = "volc";
        public const string REPM = "repm";
        public const string SMOC = "smoc";
        // phoneme replace mode
        public const string REPLACE = "replace";
        public const string OVERWRITE = "overwrite";
        // VOICEVOX constants
        public const int headS = 1;
        public const int tailS = 1;
        public const double fps = 93.75;
        public const string defaultID = "6000";
        // Minimum number of frames the engine needs in order to synthesize a segment.
        public const int minFrames = 2;
        // Phonemes and dictionaries
        public static Dictionary_list dic = new Dictionary_list();
        public static Phoneme_list phoneme_List = new Phoneme_list();

        public static bool InitializedSpeaker(string id, bool skipReinit = false) {
            var queryurl = new VoicevoxURL() { method = "GET", path = "/is_initialized_speaker", query = new Dictionary<string, string> { { "speaker", id } } };
            var response = VoicevoxClient.Inst.SendRequest(queryurl);
            var jObj = JObject.Parse(response.Item1);
            if (jObj.ContainsKey("detail")) {
                Log.Error($"Response was incorrect. : {jObj}");
                return false;
            } else if (jObj.TryGetValue("json", out var jsonToken)) {
                if (!jsonToken.Value<bool>()) {
                    queryurl = new VoicevoxURL() { method = "POST", path = "/initialize_speaker", query = new Dictionary<string, string> { { "speaker", id }, { "skip_reinit", skipReinit.ToString() } } };
                    response = VoicevoxClient.Inst.SendRequest(queryurl);
                    jObj = JObject.Parse(response.Item1);
                    if (jObj.ContainsKey("detail")) {
                        Log.Error($"Response was incorrect. : {jObj}");
                        return false;
                    }
                    return true;
                } else {
                    return true;
                }
            }
            return false;
        }

        public static VoicevoxSynthParams VoicevoxVoiceBase(VoicevoxQueryMain vqMain, string id) {
            var queryurl = new VoicevoxURL() { method = "POST", path = "/sing_frame_audio_query", query = new Dictionary<string, string> { { "speaker", id } }, body = JsonConvert.SerializeObject(vqMain) };
            var response = VoicevoxClient.Inst.SendRequest(queryurl);
            var jObj = JObject.Parse(response.Item1);
            if (jObj.ContainsKey("detail")) {
                Log.Error($"Response was incorrect. : {jObj}");
                throw new VoicevoxException($"Response was incorrect. : \n{jObj}\nScore:{string.Join(" ", vqMain.notes.Select(n => n.lyric))}");
            }
            return jObj.ToObject<VoicevoxSynthParams>();
        }

        public static void Loaddic(VoicevoxSinger singer) {
            dic.Loaddic(singer.Location);
        }

        private static readonly string[] plosives = {
            "p", "py",
            "t", "ty", "ts",
            "k", "ky", "kw",
            "ch"
        };

        //Takes a phoneme, or the romaji of a lyric, and reports whether it starts with a plosive.
        public static bool IsPlosive(string lyric) {
            if (string.IsNullOrEmpty(lyric)) {
                return false;
            }
            return plosives.Any(p => lyric.StartsWith(p));
        }

        //Returns the end frame of a segment starting at startFrame.
        //Segments are chained through startFrame so rounding never accumulates,
        //and minLength keeps a segment long enough for the engine to synthesize it.
        public static int ToEndFrame(int startFrame, double endMs, bool isPlosive, int minLength) {
            double exactEnd = (endMs / 1000.0) * fps;
            int endFrame = isPlosive
                ? (int)Math.Ceiling(exactEnd)
                : (int)Math.Round(exactEnd, MidpointRounding.AwayFromZero);
            if (endFrame - startFrame < minLength) {
                endFrame = startFrame + minLength;
            }
            return endFrame;
        }

        public static VoicevoxQueryMain NoteGroupsToVQuery(VoicevoxNote[] vNotes, TimeAxis timeAxis, bool pitch_slur = false) {
            VoicevoxQueryMain vqMain = new VoicevoxQueryMain();
            try {
                vqMain.notes.Add(new VoicevoxQueryNotes() {
                    lyric = "",
                    frame_length = (int)Math.Round((headS * fps), MidpointRounding.AwayFromZero),
                    key = null,
                    vqnindex = -1
                });
                int slur_index = 0;
                VoicevoxNote lastNote = new VoicevoxNote();
                //Holds the end frame of the previous note so that notes stay contiguous.
                int cursor = vNotes.Length > 0
                    ? (int)Math.Round((vNotes[0].positionMs / 1000.0) * fps, MidpointRounding.AwayFromZero)
                    : 0;
                for (int index = 0; index < vNotes.Length;) {
                    string lyric = dic.Notetodic(vNotes, index);
                    // When slurs are considered in pitch generation, vowel-stretched notes inherit the Kana of the previous note
                    if (IsSyllableVowelExtensionNote(vNotes[index].lyric) && pitch_slur) {
                        if (index > 0 && !String.IsNullOrEmpty(lastNote.lyric)) {
                            if (VoicevoxUtils.phoneme_List.kanas.TryGetValue(lastNote.lyric, out string str)) {
                                lyric = str;
                                slur_index++;
                            }
                        } else {
                            slur_index = 0;
                        }
                    } else {
                        slur_index = 0;
                        lastNote = vNotes[index];
                    }
                    //Usually synthesis adds the length of the slur to the previous note,
                    //so a merged note needs no minimum length of its own.
                    bool merge = IsSyllableVowelExtensionNote(vNotes[index].lyric) && !pitch_slur;
                    double endMs = vNotes[index].positionMs + vNotes[index].durationMs;

                    int startFrame = cursor;
                    int endFrame = ToEndFrame(
                        startFrame,
                        endMs,
                        IsPlosive(WanaKanaNet.WanaKana.ToRomaji(lyric)),
                        merge ? 0 : minFrames);

                    int length = endFrame - startFrame;
                    cursor = endFrame;

                    if (merge) {
                        vqMain.notes[^1].frame_length += length;
                        index++;
                        continue;
                    }

                    //Set tone to null if lyric is empty
                    int? tone = null;
                    if (!string.IsNullOrEmpty(lyric)) {
                        tone = vNotes[index].tone;
                    } else {
                        // Explicitly set to empty string.
                        lyric = "";
                    }
                    vqMain.notes.Add(new VoicevoxQueryNotes {
                        lyric = lyric,
                        frame_length = length,
                        key = tone,
                        vqnindex = index,
                        slur_index = slur_index
                    });
                    index++;
                }
                vqMain.notes.Add(new VoicevoxQueryNotes {
                    lyric = "",
                    frame_length = (int)Math.Round((tailS * fps), MidpointRounding.AwayFromZero),
                    key = null,
                    vqnindex = -1
                });
                int expectedFrames = (int)Math.Round( (vNotes.Sum(x => x.durationMs) / 1000.0) * VoicevoxUtils.fps, MidpointRounding.AwayFromZero);

                int actualFrames = vqMain.notes.Sum(x => x.frame_length) - vqMain.notes[0].frame_length - vqMain.notes[^1].frame_length;

                int diff = expectedFrames - actualFrames;
                if (diff != 0) {
                    bool applied = false;
                    for (int i = vqMain.notes.Count - 2; i >= 1; i--) {
                        if (vqMain.notes[i].frame_length + diff >= minFrames) {
                            vqMain.notes[i].frame_length += diff;
                            applied = true;
                            break;
                        }
                    }
                    if (!applied) {
                        Log.Warning($"No note could absorb a frame length difference of {diff}. The phrase may drift by {(diff / fps) * 1000d:F1}ms.");
                    }
                }
            } catch (Exception e) {
                Log.Error(e, $"VoicevoxQueryNotes setup error: {e.Message}");
            }
            return vqMain;
        }

        public static List<double> QueryToF0(VoicevoxQueryMain vqMain, VoicevoxSynthParams vsParams, string id) {
            VoicevoxQueryParams vqParams = new VoicevoxQueryParams() { score = vqMain, frame_audio_query = vsParams };
            var queryurl = new VoicevoxURL() { method = "POST", path = "/sing_frame_f0", query = new Dictionary<string, string> { { "speaker", id } }, body = JsonConvert.SerializeObject(vqParams) };
            var response = VoicevoxClient.Inst.SendRequest(queryurl);
            List<double> f0s = new List<double>();
            var jObj = JObject.Parse(response.Item1);
            if (jObj.ContainsKey("detail")) {
                Log.Error($"Response was incorrect. : {jObj}");
                throw new VoicevoxException($"Response was incorrect. : \n{jObj}\nScore:{string.Join(" ", vqMain.notes.Select(n => n.lyric))}");
            } else {
                f0s = jObj["json"].ToObject<List<double>>();
            }
            return f0s;
        }

        public static List<double> QueryToVolume(VoicevoxQueryMain vqMain, VoicevoxSynthParams vsParams, string id) {
            VoicevoxQueryParams vqParams = new VoicevoxQueryParams() { score = vqMain, frame_audio_query = vsParams };
            var queryurl = new VoicevoxURL() { method = "POST", path = "/sing_frame_volume", query = new Dictionary<string, string> { { "speaker", id } }, body = JsonConvert.SerializeObject(vqParams) };
            var response = VoicevoxClient.Inst.SendRequest(queryurl);
            List<double> volumes = new List<double>();
            var jObj = JObject.Parse(response.Item1);
            if (jObj.ContainsKey("detail")) {
                Log.Error($"Response was incorrect. : {jObj}");
                throw new VoicevoxException($"Response was incorrect. : \n{jObj}\nScore:{string.Join(" ", vqMain.notes.Select(n => n.lyric))}");
            } else {
                volumes = jObj["json"].ToObject<List<double>>();
            }
            return volumes;
        }

        public static void AdjustF0ForSlur(VoicevoxQueryMain vqMain, List<double> f0) {
            if (vqMain == null || vqMain.notes == null || f0 == null) {
                return;
            }

            int offset = 0;
            int? baseKey = null;
            foreach (var note in vqMain.notes) {
                int start = offset;
                int end = Math.Min(f0.Count, offset + note.frame_length);

                if (note.key.HasValue) {
                    if (note.slur_index == 0) {
                        baseKey = note.key;
                    } else if (note.slur_index > 0 && baseKey.HasValue) {
                        int delta = note.key.Value - baseKey.Value;
                        if (delta != 0) {
                            double factor = Math.Pow(2d, delta / 12d);
                            for (int i = start; i < end; i++) {
                                f0[i] *= factor;
                            }
                        }
                    }
                }

                offset += note.frame_length;
                if (offset >= f0.Count) {
                    break;
                }
            }
        }

        public static double[] SampleCurve(RenderPhrase phrase, float[] curve, double defaultValue, double frameMs, int length, int headFrames, int tailFrames, double offset, Func<double, double> convert) {
            const int interval = 5;
            var result = new double[length];
            try {
                if (curve == null) {
                    Array.Fill(result, defaultValue);
                    return result;
                }

                for (int i = 0; i < length - headFrames - tailFrames; i++) {
                    double posMs = phrase.positionMs - phrase.leadingMs + (i * frameMs) + offset;
                    int ticks = phrase.timeAxis.MsPosToTickPos(posMs) - (phrase.position - phrase.leading);
                    int index = Math.Max(0, (int)((double)ticks / interval));
                    if (index < curve.Length) {
                        result[i + headFrames] = convert(curve[index]);
                    }
                }
                //Fill head and tail
                Array.Fill(result, convert(curve[0]), 0, headFrames);
                Array.Fill(result, convert(curve[^1]), length - tailFrames, tailFrames);
            } catch (IndexOutOfRangeException e) {
                Log.Error($"SampleCurve (IndexOutOfRangeException): {e}");
            } catch (ArgumentException e) {
                Log.Error($"SampleCurve (ArgumentException): {e}");
            }
            return result;
        }
        public static bool IsKana(string s) {
            return phoneme_List.kanas.ContainsKey(s);
        }
        public static bool IsPau(string s) {
            return phoneme_List.paus.ContainsKey(s);
        }
        public static bool IsVowel(string s) {
            return phoneme_List.vowels.Contains(s);
        }
        public static bool IsConsonant(string s) {
            return phoneme_List.consonants.Contains(s);
        }

        public static bool TryGetPau(string s, out string str) {
            phoneme_List.paus.TryGetValue(s, out str);
            return phoneme_List.paus.ContainsKey(s);
        }

        public static string getBaseSingerID(VoicevoxSinger singer) {
            if (singer.voicevoxConfig == null) {
                return defaultID;
            }
            if (singer.voicevoxConfig.base_singer_style == null) {
                return defaultID;
            }
            foreach (var s in singer.voicevoxConfig.base_singer_style) {
                if (s.name.Equals(singer.voicevoxConfig.base_singer_name)
                    && s.styles.name.Equals(singer.voicevoxConfig.base_singer_style_name)) {
                    return s.styles.id.ToString();
                }
            }
            return defaultID;
        }

        public static bool IsSyllableVowelExtensionNote(string lyric) {
            return lyric.StartsWith("+~") || lyric.StartsWith("+*") || lyric.StartsWith("+") || lyric.StartsWith("-");
        }
    }
}
