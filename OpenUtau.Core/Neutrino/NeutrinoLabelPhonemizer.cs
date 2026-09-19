using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenUtau.Api;
using OpenUtau.Core.Hts;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.Core.Neutrino {
    [Phonemizer("Neutrino Label Phonemizer", "NEUTRINO")]
    public class NeutrinoLabelPhonemizer : HTSLabelPhonemizer {
        string NeutrinoExe = string.Empty;
        string NeutrinoClientExe = string.Empty;
        string NeutrinoServerExe = string.Empty;

        protected new NeutrinoSinger singer;

        List<string> macronLyrics = new List<string>();

        NeutrinoVersion Version => NeutrinoUtils.DetectVersion(this.singer.singerVersion);

        public override void SetSinger(USinger singer) {
            this.singer = singer as NeutrinoSinger;
            if (this.singer == null) {
                return;
            }
            lang = "JPN";//TODO: use singer.language
            tablePath = NeutrinoUtils.TableFile;
            if (!NeutrinoUtils.TryResolveBasePath(this.singer.singerVersion, out string basePath, out string error)) {
                Log.Error(error);
                return;
            }
            Log.Debug($"Base path: {basePath}");
            //Load Dictionary
            try {
                phoneDict.Clear();
                LoadDict(NeutrinoUtils.DicPath(basePath, NeutrinoUtils.ConfFile), singer.TextFileEncoding);
                LoadDict(NeutrinoUtils.DicPath(basePath, NeutrinoUtils.TableFile), singer.TextFileEncoding);
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
            Log.Debug(String.IsNullOrEmpty(NeutrinoClientExe) ? $"No NeutrinoClientExe" : $"NeutrinoClientExe: {NeutrinoClientExe}");
            NeutrinoServerLauncher.EnsureStarted(NeutrinoServerExe);
        }
        protected IG2p LoadG2p() {
            var g2ps = new List<IG2p>();
            var builder = G2pDictionary.NewBuilder();
            vowels.AddRange(phoneDict["VOWELS"]);
            breaks.AddRange(phoneDict["BREAK"]);
            pauses.AddRange(phoneDict["PAUSES"]);
            silences.AddRange(phoneDict["SILENCES"]);
            consonants.AddRange(phoneDict["PHONEME_CL"]);
            macronLyrics.AddRange(phoneDict["MACRON"]);
            foreach (var dict in phoneDict.Values) {
                foreach (var phoneme in dict) {
                    if (!consonants.Contains(phoneme) && !vowels.Contains(phoneme) &&
                        !breaks.Contains(phoneme) && !pauses.Contains(phoneme) &&
                        !silences.Contains(phoneme)) {
                        consonants.Add(phoneme);
                    }
                    if (!consonants.Contains(phoneme)) {
                        builder.AddSymbol(phoneme, true);
                    } else {
                        builder.AddSymbol(phoneme, false);
                    }
                }
            }
            foreach (var entry in phoneDict.Keys) {
                builder.AddEntry(entry, phoneDict[entry]);
                foreach (var reduction in phoneDict["VOWEL_REDUCTION"]) {
                    var phonemes = phoneDict[entry].Except(vowels).ToList();
                    if (phonemes.Count == 0) continue;
                    builder.AddEntry(entry + reduction, phonemes);
                }
                foreach (var macron in phoneDict["MACRON"]) {
                    var addPhonemes = phoneDict[entry].Where(x => vowels.Contains(x)).ToList();
                    if (addPhonemes.Count == 0) continue;
                    var phonemes = phoneDict[entry].ToList();
                    phonemes.AddRange(addPhonemes);
                    builder.AddEntry(entry + macron, phonemes);
                    macronLyrics.Add(entry + macron);
                }
            }
            g2ps.Add(builder.Build());
            return new G2pFallbacks(g2ps.ToArray());
        }

        protected override Note[][] PhraseAdjustments(Note[][] phrese) {
            for (int i = 0; i < phrese.Length; i++) {
                var lyric = phrese[i][0].lyric;
                if (phoneDict["MACRON"].Contains(lyric) && (i > 0)) {
                    if (g2p.IsValidSymbol(lyric)) {
                        var vowel = g2p.Query(phrese[i - 1][0].lyric).FirstOrDefault(phoneme => vowels.Contains(phoneme));
                        if (!string.IsNullOrEmpty(vowel)) {
                            phrese[i][0].lyric = vowel;
                        }
                    }
                }
            }
            return phrese;
        }

        protected override HTSNote CustomHTSNoteContext(HTSNote htsNote, Note note) {
            var fixs = GetPrefixAndSuffix(note);
            if (!htsNote.isRest && !htsNote.isSlur) {
                htsNote.langDependent = "0"; // no macron
                if (macronLyrics.Contains(note.lyric)) {
                    htsNote.langDependent = "1"; // macron
                }
            }
            return htsNote;
        }

        protected override HTSPhoneme[] CustomHTSPhonemeContext(HTSPhoneme[] htsPhonemes, Note[] notes) {
            var fixs = GetPrefixAndSuffix(notes[0]);
            foreach (var htsPhoneme in htsPhonemes) {
                htsPhoneme.flag1 = "00"; // NEUTRINO Default.
            }
            return htsPhonemes;
        }

        protected override void SendScore(Note[][] phrase) {
            if (string.IsNullOrEmpty(this.singer.singerVersion)) {
                return;
            }
            if (File.Exists(fullScorePath) && !File.Exists(monoTimingPath)) {
                var voicebankNameHash = $"{this.singer.voicebankNameHash:x16}";
                string f0Path = Path.Join(htstmpPath, $"{voicebankNameHash}_tmp.f0");
                string melspecPath = Path.Join(htstmpPath, $"{voicebankNameHash}_tmp.melspec");
                string wavPath = Path.Join(htstmpPath, $"{voicebankNameHash}_tmp.wav");
                string modelDir = NeutrinoUtils.ModelDir(this.singer);
                var attr = phrase[0][0].phonemeAttributes?.FirstOrDefault(attr => attr.index == 0) ?? default;
                int toneShift = attr.toneShift ?? GetParentToneShift();
                int numThreads = Preferences.Default.NumRenderThreads;
                string ArgParam = string.Empty;
                if (Version == NeutrinoVersion.V27) {
                    ArgParam = $"{fullScorePath} {monoTimingPath} {f0Path} {melspecPath} {modelDir} -a -k {toneShift} -d 3 -n 1 -p {numThreads} -m -t";
                } else if (Version == NeutrinoVersion.V3) {
                    //TODO: -S support model
                    ArgParam = $"\"{fullScorePath}\" \"{monoTimingPath}\" \"{f0Path}\" \"{melspecPath}\" \"{wavPath}\" \"{modelDir}\" --skip-f0 --skip-melspec --skip-wav -k {toneShift} -m -t";
                } else {
                    Log.Error($"Unsupported NEUTRINO version: {this.singer.singerVersion}");
                    return;
                }
                Log.Information($"NEUTRINO timing args: {ArgParam}");
                if (File.Exists(NeutrinoClientExe)) {
                    ProcessRunner.Run(NeutrinoClientExe, ArgParam, Log.Logger);
                } else {
                    ProcessRunner.Run(NeutrinoExe, ArgParam, Log.Logger);
                }
            }
        }
    }
}
