using Microsoft.ML.OnnxRuntime;
using OpenUtau.Api;
using System;

namespace OpenUtau.Core.Vogen {
    [Phonemizer("Vogen Chinese Yue Phonemizer", "VOGEN ZH-YUE", language: "ZH-YUE", engine: "Vogen")]
    public class VogenYuePhonemizer : VogenBasePhonemizer {
        private static TrieNode? trie;
        private static InferenceSession? g2p;
        private static InferenceSession? prosody;

        public VogenYuePhonemizer() {
            trie ??= TrieNode.LoadDictionary(
                Data.VogenRes.yue
                    .Split(new string[] { "\r\n", "\r", "\n" }, StringSplitOptions.None));
            g2p ??= new InferenceSession(Data.VogenRes.g2p_yue);
            G2p = g2p;
            prosody ??= new InferenceSession(Data.VogenRes.po_yue);
            Prosody = prosody;
        }

        protected override string LangPrefix => "yue:";

        protected override string[] Romanize(string[] lyrics) {
            var result = new string[lyrics.Length];
            int index = 0;
            while (index < lyrics.Length) {
                string[]? romanized = trie!.Query(new Span<string>(lyrics, index, lyrics.Length - index));
                if (romanized == null) {
                    result[index] = lyrics[index];
                    index++;
                } else {
                    Array.Copy(romanized, 0, result, index, romanized.Length);
                    index += romanized.Length;
                }
            }
            return result;
        }
    }
}
