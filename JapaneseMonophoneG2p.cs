using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using OpenUtau.Api;
using OpenUtau.Core.Util;

namespace OpenUtau.Core.G2p {
    public class JapaneseMonophoneG2p : G2pPack {
        private static readonly string[] graphemes = new string[] {
            "", "", "", "", "a", "b", "c", "d", "e", "f", "g",
            "h", "i", "j", "k", "m", "n", "o", "p", "r", "s",
            "t", "u", "v", "w", "y", "z", "あ", "い", "う", "え",
            "お", "ぁ", "ぃ", "ぅ", "ぇ", "ぉ", "か", "き", "く",
            "け", "こ", "さ", "し", "す", "せ", "そ", "ざ", "じ", "ず",
            "ぜ", "ぞ", "た", "ち", "つ", "て", "と", "だ", "ぢ", "づ", "で",
            "ど", "な", "に", "ぬ", "ね", "の", "は", "ひ", "ふ", "へ", "ほ",
            "ば", "び", "ぶ", "べ", "ぼ", "ぱ", "ぴ", "ぷ", "ぺ", "ぽ", "ま",
            "み", "む", "め", "も", "や", "ゆ", "よ", "ゃ", "ゅ", "ょ", "ら",
            "り", "る", "れ", "ろ", "わ", "を", "ん", "っ", "ヴ", "ゔ","゜",
            "ゐ", "ゑ", "ア", "イ", "ウ", "エ", "オ", "ァ", "ィ", "ゥ", "ェ",
            "ォ", "カ", "キ", "ク", "ケ", "コ", "サ", "シ", "ス", "セ", "ソ",
            "ザ", "ジ", "ズ", "ゼ", "ゾ", "タ", "チ", "ツ", "テ", "ト", "ダ",
            "ヂ", "ヅ", "デ", "ド", "ナ", "ニ", "ヌ", "ネ", "ノ", "ハ", "ヒ",
            "フ", "ヘ", "ホ", "バ", "ビ", "ブ", "ベ", "ボ", "パ", "ピ", "プ",
            "ペ", "ポ", "マ", "ミ", "ム", "メ", "モ", "ヤ", "ユ", "ヨ", "ャ",
            "ュ", "ョ", "ラ", "リ", "ル", "レ", "ロ", "ワ", "ヲ", "ン", "ッ",
            "ヰ", "ヱ", "息", "吸", "-", "R"
        };

        private static readonly string[] phonemes = new string[] {
            "", "", "", "", "A", "AP", "E", "I", "N", "O", "U",
            "SP", "a", "b", "by", "ch", "cl", "d", "dy", "e", "f", "g", "gw",
            "gy", "h", "hy", "i", "j", "k", "kw", "ky", "m", "my", "n",
            "ng", "ngy", "ny", "o", "p", "py", "r", "ry", "s", "sh", "t", "ts",
            "ty", "u", "v", "w", "y", "z"
        };

        private static object lockObj = new object();
        private static Dictionary<string, int> graphemeIndexes;
        private static IG2p hiragana;
        private static IG2p katakana;
        private static IG2p romaji;
        private static IG2p special;
        private static InferenceSession session;
        private static Dictionary<string, string[]> predCache = new Dictionary<string, string[]>();

        protected Tuple<IG2p, InferenceSession> LoadPack(
            byte[] data,
            Func<string, string> prepGrapheme = null,
            Func<string, string> prepPhoneme = null) {
            prepGrapheme = prepGrapheme ?? ((string s) => s);
            prepPhoneme = prepPhoneme ?? ((string s) => s);
            string[] hiraganaTxt = Zip.ExtractText(data, "hiragana.txt");
            string[] katakanaTxt = Zip.ExtractText(data, "katakana.txt");
            string[] romajiTxt = Zip.ExtractText(data, "romaji.txt");
            string[] specialTxt = Zip.ExtractText(data, "special.txt");
            string[] phonesTxt = Zip.ExtractText(data, "phones.txt");
            var builder = G2pDictionary.NewBuilder();
            phonesTxt.Select(line => line.Trim())
                .Select(line => line.Split())
                .Where(parts => parts.Length == 2)
                .ToList()
                .ForEach(parts => builder.AddSymbol(prepPhoneme(parts[0]), parts[1]));
            hiraganaTxt.Where(line => !line.StartsWith(";;;"))
                .Select(line => line.Trim())
                .Select(line => line.Split(new string[] { "  " }, StringSplitOptions.None))
                .Where(parts => parts.Length == 2)
                .ToList()
                .ForEach(parts => builder.AddEntry(
                    prepGrapheme(parts[0]),
                    parts[1].Split().Select(symbol => prepPhoneme(symbol))));
            katakanaTxt.Where(line => !line.StartsWith(";;;"))
                .Select(line => line.Trim())
                .Select(line => line.Split(new string[] { "  " }, StringSplitOptions.None))
                .Where(parts => parts.Length == 2)
                .ToList()
                .ForEach(parts => builder.AddEntry(
                    prepGrapheme(parts[0]),
                    parts[1].Split().Select(symbol => prepPhoneme(symbol))));
            romajiTxt.Where(line => !line.StartsWith(";;;"))
                .Select(line => line.Trim())
                .Select(line => line.Split(new string[] { "  " }, StringSplitOptions.None))
                .Where(parts => parts.Length == 2)
                .ToList()
                .ForEach(parts => builder.AddEntry(
                    prepGrapheme(parts[0]),
                    parts[1].Split().Select(symbol => prepPhoneme(symbol))));
            specialTxt.Where(line => !line.StartsWith(";;;"))
                .Select(line => line.Trim())
                .Select(line => line.Split(new string[] { "  " }, StringSplitOptions.None))
                .Where(parts => parts.Length == 2)
                .ToList()
                .ForEach(parts => builder.AddEntry(
                    prepGrapheme(parts[0]),
                    parts[1].Split().Select(symbol => prepPhoneme(symbol))));
            var dict = builder.Build();
            return Tuple.Create((IG2p) dict, session);
        }

        public JapaneseMonophoneG2p() {
            lock (lockObj) {
                if (graphemeIndexes == null) {
                    graphemeIndexes = graphemes
                        .Skip(4)
                        .Select((g, i) => Tuple.Create(g, i))
                        .ToDictionary(t => t.Item1, t => t.Item2 + 4);
                    var tuple = LoadPack(Data.Resources.g2p_ja_mono);
                    hiragana = tuple.Item1;
                    katakana = tuple.Item1;
                    romaji = tuple.Item1;
                    special = tuple.Item1;
                    session = tuple.Item2;
                }
            }
            GraphemeIndexes = graphemeIndexes;
            Phonemes = phonemes;
            Session = session;
            Dict = hiragana;
            Dict = katakana;
            Dict = romaji;
            Dict = special;
            PredCache = predCache;
        }
    }
}
