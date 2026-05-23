using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Serilog;

namespace OpenUtau.Plugin.Builtin {
    public static class ThaiDictionaryLoader {
        private static readonly Lazy<Dictionary<string, string>> _dictionary =
            new Lazy<Dictionary<string, string>>(() => LoadDictionary(), true);

        public static Dictionary<string, string> Dictionary => _dictionary.Value;

        private static Dictionary<string, string> LoadDictionary() {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try {
                var searchPaths = new[] {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "words_th.txt"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins", "words_th.txt"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "OpenUtau", "Plugins", "words_th.txt")
                };

                string dictPath = searchPaths.FirstOrDefault(File.Exists);
                if (dictPath != null) {
                    var lines = File.ReadAllLines(dictPath, Encoding.UTF8);
                    foreach (var line in lines) {
                        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                        var parts = line.Split('=');
                        if (parts.Length >= 2) {
                            string key = parts[0].Trim();
                            string val = string.Join("=", parts.Skip(1)).Trim();
                            if (!string.IsNullOrEmpty(key)) dict[key] = val;
                        }
                    }
                    Log.Information($"[TH Phonemizers] Successfully loaded Thai dictionary: {dict.Count} entries from {dictPath}");
                } else {
                    Log.Warning("[TH Phonemizers] Could not find words_th.txt in any expected location.");
                }
            } catch (Exception ex) {
                Log.Error(ex, "[TH Phonemizers] Failed to load words_th.txt");
            }
            return dict;
        }
    }
}
