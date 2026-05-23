using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using OpenUtau.Api;
using OpenUtau.Classic;

namespace OpenUtau.Core.DiffSinger
{
    class DiffSingerG2pDictionaryData : G2pDictionaryData{
        public struct Replacement{
            public string from;
            public string to;
        }
        public Replacement[]? replacements;

        public Dictionary<string, string> replacementsDict(){
            var dict = new Dictionary<string, string>();
            if(replacements!=null){
                foreach(var r in replacements){
                    dict[r.from] = r.to;
                }
            }
            return dict;
        }
    }

    /// <summary>
    /// Base class for DiffSinger phonemizers based on OpenUtau's builtin G2p.
    /// </summary>
    public abstract class DiffSingerG2pPhonemizer : DiffSingerBasePhonemizer
    {
        protected virtual IG2p LoadBaseG2p()=>null;
        //vowels and consonants of BaseG2p
        protected virtual string[] GetBaseG2pVowels()=>new string[]{};
        protected virtual string[] GetBaseG2pConsonants()=>new string[]{};

        private Dictionary<string, bool> phonemeSymbols = new Dictionary<string, bool>();
        protected bool HasPhoneme(string phoneme) {
            return phonemeSymbols.ContainsKey(phoneme);
        }

        protected override IG2p LoadG2p(string rootPath, bool useLangId = false) {
            //Each phonemizer has a delicated dictionary name, such as dsdict-en.yaml, dsdict-ru.yaml.
            //If this dictionary exists, load it.
            //If not, load dsdict.yaml.
            var dictionaryNames = new string[] {GetDictionaryName(), "dsdict.yaml"};
            var g2ps = new List<IG2p>();

            // Load dictionary from singer folder.
            G2pDictionary.Builder g2pBuilder = new G2pDictionary.Builder();
            var replacements = new Dictionary<string,string>();
            bool dictFound = false;
            foreach(var dictionaryName in dictionaryNames){
                string dictionaryPath = Path.Combine(rootPath, dictionaryName);
                if (File.Exists(dictionaryPath)) {
                    try {
                        string dictText = File.ReadAllText(dictionaryPath);
                        var dictData = Yaml.DefaultDeserializer.Deserialize<DiffSingerG2pDictionaryData>(dictText);
                        g2pBuilder.Load(dictData);
                        replacements = dictData.replacementsDict();
                        // Collect all symbols from the dictionary and add them to phonemeSymbols
                        if (dictData.symbols != null) {
                            foreach (var symbol in dictData.symbols) {
                                phonemeSymbols[symbol.symbol.Trim()] = true;
                            }
                        }
                        Log.Information("Loaded symbols: " + string.Join(", ", phonemeSymbols.Keys));
                    } catch (Exception e) {
                        Log.Error(e, $"Failed to load {dictionaryPath}");
                        throw new Exception($"Failed to load {dictionaryPath}", e);
                    }
                    dictFound = true;
                    break;
                }
            }
            if(!dictFound){
                var triedPaths = string.Join(", ", dictionaryNames.Select(n => Path.Combine(rootPath, n)));
                throw new FileNotFoundException(
                    $"No dictionary file found. Tried: {triedPaths}");
            }
            //SP and AP should always be vowel
            g2pBuilder.AddSymbol("SP", true);
            g2pBuilder.AddSymbol("AP", true);
            g2ps.Add(g2pBuilder.Build());

            // Load base g2p.
            var baseG2p = LoadBaseG2p();
            if(baseG2p == null){
                return new G2pFallbacks(g2ps.ToArray());
            }
            foreach(var v in GetBaseG2pVowels()){
                phonemeSymbols[v]=true;
            }
            foreach(var c in GetBaseG2pConsonants()){
                phonemeSymbols[c]=false;
            }
            var langCode = GetLangCode();
            if(!string.IsNullOrEmpty(langCode)){
                //For diffsinger multi dict voicebanks, the replacements of g2p phonemes default to the <langcode>/<phoneme>
                foreach(var ph in GetBaseG2pVowels().Concat(GetBaseG2pConsonants())){
                    if(!replacements.ContainsKey(ph)){
                        replacements[ph]=langCode + "/" + ph;
                    }
                }
            }
            foreach(var from in replacements.Keys){
                var to = replacements[from];
                if(baseG2p.IsValidSymbol(to)){
                    if(baseG2p.IsVowel(to)){
                        phonemeSymbols[from]=true;
                    }else{
                        phonemeSymbols[from]=false;
                    }
                }
            }
            g2ps.Add(new G2pRemapper(baseG2p, phonemeSymbols, replacements));
            return new G2pFallbacks(g2ps.ToArray());
        }
    }
}
