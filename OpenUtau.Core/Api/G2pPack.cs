using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenUtau.Core.Util;

namespace OpenUtau.Api {
    public abstract class G2pPack : IG2p {
        protected readonly static Regex kAllPunct = new Regex(@"^[\p{P}]$");

        protected Dictionary<string, int> GraphemeIndexes { get; set; }
        protected string[] Phonemes { get; set; }
        protected IG2p Dict { get; set; }
        protected InferenceSession Session { get; set; }
        protected Dictionary<string, string[]> PredCache { get; set; }
        
        /// <summary>
        /// When true (default), graphemes are used as-is.
        /// When false, graphemes are lowercased before dictionary lookup and prediction.
        /// </summary>
        public bool CaseSensitive { get; set; } = true;

        protected Tuple<IG2p, InferenceSession> LoadPack(
            byte[] data,
            Func<string, string> prepGrapheme = null,
            Func<string, string> prepPhoneme = null) {
            prepGrapheme = prepGrapheme ?? ((string s) => s);
            prepPhoneme = prepPhoneme ?? ((string s) => s);
            string[] dictTxt = Zip.ExtractText(data, "dict.txt");
            string[] phonesTxt = Zip.ExtractText(data, "phones.txt");
            byte[] g2pData = Zip.ExtractBytes(data, "g2p.onnx");
            var builder = G2pDictionary.NewBuilder();
            phonesTxt.Select(line => line.Trim())
                .Select(line => line.Split())
                .Where(parts => parts.Length == 2)
                .ToList()
                .ForEach(parts => builder.AddSymbol(prepPhoneme(parts[0]), parts[1]));
            dictTxt.Where(line => !line.StartsWith(";;;"))
                .Select(line => line.Trim())
                .Select(line => line.Split(new string[] { "  " }, StringSplitOptions.None))
                .Where(parts => parts.Length == 2)
                .ToList()
                .ForEach(parts => builder.AddEntry(
                    prepGrapheme(parts[0]),
                    parts[1].Split().Select(symbol => prepPhoneme(symbol))));
            var dict = builder.Build();
            var session = new InferenceSession(g2pData);
            return Tuple.Create((IG2p)dict, session);
        }

        public static string RemoveTailDigits(string s) {
            while (s.Length > 0 && char.IsDigit(s.Last())) {
                s = s.Substring(0, s.Length - 1);
            }
            return s;
        }

        public bool IsValidSymbol(string symbol) {
            return Dict.IsValidSymbol(symbol);
        }

        public bool IsVowel(string symbol) {
            return Dict.IsVowel(symbol);
        }

        public bool IsGlide(string symbol) {
            return Dict.IsGlide(symbol);
        }

        public string[] Query(string grapheme) {
            if (grapheme.Length == 0 || kAllPunct.IsMatch(grapheme)) {
                return null;
            }
            if (!CaseSensitive) {
                grapheme = grapheme.ToLowerInvariant();
            }
            var phonemes = Dict.Query(grapheme);
            if (phonemes == null && !PredCache.TryGetValue(grapheme, out phonemes)) {
                phonemes = Predict(grapheme);
                if (phonemes.Length == 0) {
                    return null;
                }
                PredCache.Add(grapheme, phonemes);
            }
            return phonemes.Clone() as string[];
        }

        public string[] UnpackHint(string hint, char separator = ' ') {
            return Dict.UnpackHint(hint, separator);
        }

        protected virtual string[] Predict(string grapheme) {
            Tensor<int> src = EncodeWord(grapheme);
            if (src.Length == 0 || Session == null) {
                return new string[0];
            }
            Tensor<int> tgt = new int[,] { { 2 } }.ToTensor();
            Tensor<int> t = new DenseTensor<int>(1);
            var srcLength = src.Dimensions[1];
            var inputs = new List<NamedOnnxValue>();
            while (t[0] < srcLength && tgt.Length < 48) {
                inputs.Clear();
                inputs.Add(NamedOnnxValue.CreateFromTensor("src", src));
                inputs.Add(NamedOnnxValue.CreateFromTensor("tgt", tgt));
                inputs.Add(NamedOnnxValue.CreateFromTensor("t", t));
                var outputs = Session.Run(inputs);
                var pred = outputs.First().AsTensor<int>()[0];
                if (pred != 2) {
                    var newTgt = new DenseTensor<int>(new int[] { 1, tgt.Dimensions[1] + 1 });
                    for (int i = 0; i < tgt.Dimensions[1]; ++i) {
                        newTgt[0, i] = tgt[0, i];
                    }
                    newTgt[0, tgt.Dimensions[1]] = pred;
                    tgt = newTgt;
                } else {
                    t[0] += 1;
                }
                outputs.Dispose();
            }
            var phonemes = DecodePhonemes(tgt.Skip(1).ToArray());
            return phonemes;
        }

        protected Tensor<int> EncodeWord(string grapheme) {
            var encoded = new List<int>();
            foreach (char c in grapheme.ToLowerInvariant()) {
                if (GraphemeIndexes.TryGetValue(c.ToString(), out var index)) {
                    encoded.Add(index);
                }
            }
            var tensor = new DenseTensor<int>(new int[] { 1, encoded.Count });
            for (int i = 0; i < encoded.Count; ++i) {
                tensor[0, i] = encoded[i];
            }
            return tensor;
        }

        protected string[] DecodePhonemes(int[] indexes) {
            return indexes.Select(idx => Phonemes[idx]).ToArray();
        }
    }
}
