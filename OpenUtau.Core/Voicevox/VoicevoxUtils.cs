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
        public int vqnindex;

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
                "あ ぁ ば びゃ ゔゃ ちゃ だ   でゃ ふぁ が ぐゎ ぐぁ ぎゃ は ひゃ じゃ ぢゃ か くゎ くぁ きゃ ま みゃ な にゃ ぱ ぴゃ ら りゃ さ   しゃ た   つぁ てゃ ゔぁ わ   ゎ や  ゃ ざ".Split(),
                "い ぃ び びぃ      ち   でぃ      ふぃ ぎ ぐぃ      ぎぃ ひ      じ   ぢ   き くぃ      きぃ み みぃ に      ぴ ぴぃ り りぃ すぃ し   てぃ つぃ      ゔぃ うぃ           ずぃ".Split(),
                "う ぅ ぶ びゅ ゔゅ ちゅ どぅ でゅ ふ   ぐ ぐぅ      ぎゅ    ひゅ じゅ ぢゅ く くぅ      きゅ む みゅ ぬ にゅ ぷ ぴゅ る りゅ す   しゅ とぅ つ   てゅ ゔ   うぅ    ゆ  ゅ ず".Split(),
                "え ぇ べ びぇ      ちぇ で   でぇ ふぇ げ ぐぇ      ぎぇ へ ひぇ じぇ ぢぇ け くぇ      きぇ め みぇ ね にぇ ぺ ぴぇ れ りぇ せ   しぇ て   つぇ てぇ ゔぇ うぇ    いぇ   ぜ".Split(),
                "お ぉ ぼ びょ ゔょ ちょ ど   でょ ふぉ ご ぐぉ      ぎょ ほ ひょ じょ ぢょ こ くぉ      きょ も みょ の にょ ぽ ぴょ ろ りょ そ   しょ と   つぉ てょ ゔぉ うぉ を よ  ょ ぞ".Split(),
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
        public const string DUCM = "ducm";
        // phoneme replace mode
        public const string REPLACE = "replace";
        public const string OVERWRITE = "overwrite";
        // duration correction mode
        public const string AUTO = "auto";
        public const string ON = "on";
        public const string OFF = "off";
        // VOICEVOX constants
        public const int headS = 1;
        public const int tailS = 1;
        public const double fps = 93.75;
        public const string defaultID = "6000";
        // Phonemes and dictionaries
        public static Dictionary_list dic = new Dictionary_list();
        public static Phoneme_list phoneme_List = new Phoneme_list();

        public static VoicevoxSynthParams VoicevoxVoiceBase(VoicevoxQueryMain qNotes, string id) {
            var queryurl = new VoicevoxURL() { method = "POST", path = "/sing_frame_audio_query", query = new Dictionary<string, string> { { "speaker", id } }, body = JsonConvert.SerializeObject(qNotes) };
            var response = VoicevoxClient.Inst.SendRequest(queryurl);
            VoicevoxSynthParams vvNotes;
            var jObj = JObject.Parse(response.Item1);
            if (jObj.ContainsKey("detail")) {
                Log.Error($"Response was incorrect. : {jObj}");
            } else {
                vvNotes = jObj.ToObject<VoicevoxSynthParams>();
                return vvNotes;
            }
            return new VoicevoxSynthParams();
        }

        public static void Loaddic(VoicevoxSinger singer) {
            dic.Loaddic(singer.Location);
        }

        public static VoicevoxQueryMain NoteGroupsToVQuery(VoicevoxNote[] vNotes, TimeAxis timeAxis) {
            VoicevoxQueryMain vqMain = new VoicevoxQueryMain();
            int index = 0;
            try {
                vqMain.notes.Add(new VoicevoxQueryNotes() {
                    lyric = "",
                    frame_length = (int)Math.Round((headS * fps), MidpointRounding.AwayFromZero),
                    key = null,
                    vqnindex = -1
                });
                int short_length_count = 0;
                while (index < vNotes.Length) {
                    string lyric = dic.Notetodic(vNotes, index);
                    //Avoid synthesis without at least two frames.
                    double durationMs = vNotes[index].durationMs;
                    int length = (int)Math.Round((durationMs / 1000f) * VoicevoxUtils.fps, MidpointRounding.AwayFromZero);
                    if (length < 2) {
                        length = 2;
                    }
                    if (durationMs > (length / VoicevoxUtils.fps) * 1000f) {
                        if (short_length_count >= 2) {
                            length += 1;
                            short_length_count = 0;
                        } else {
                            short_length_count += 1;
                        }
                    }
                    int? tone = null;
                    if (!string.IsNullOrEmpty(lyric)) {
                        tone = vNotes[index].tone;
                    } else {
                        lyric = "";
                    }
                    vqMain.notes.Add(new VoicevoxQueryNotes {
                        lyric = lyric,
                        frame_length = length,
                        key = tone,
                        vqnindex = index
                    });
                    index++;
                }
                vqMain.notes.Add(new VoicevoxQueryNotes {
                    lyric = "",
                    frame_length = (int)Math.Round((tailS * fps), MidpointRounding.AwayFromZero),
                    key = null,
                    vqnindex = -1
                });

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
            } else {
                volumes = jObj["json"].ToObject<List<double>>();
            }
            return volumes;
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

        public static bool IsDicKana(string s) {
            return phoneme_List.kanas.ContainsKey(s);
        }

        public static bool IsDicPau(string s) {
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
            if (singer.voicevoxConfig.base_singer_style != null) {
                foreach (var s in singer.voicevoxConfig.base_singer_style) {
                    if (s.name.Equals(singer.voicevoxConfig.base_singer_name)) {
                        if (s.styles.name.Equals(singer.voicevoxConfig.base_singer_style_name)) {
                            return s.styles.id.ToString();
                        }
                    }
                }
            }
            return defaultID;
        }

        public static bool IsSyllableVowelExtensionNote(string lyric) {
            return lyric.StartsWith("+~") || lyric.StartsWith("+*") || lyric.StartsWith("+") || lyric.StartsWith("-");
        }
    }
}
