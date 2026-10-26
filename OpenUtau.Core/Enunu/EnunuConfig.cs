using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Core.Enunu {
    class EnunuConfig {
        public string enunu_type = string.Empty;

        public string feature_type = string.Empty;
        public string tablePath = string.Empty;
        public string questionPath = string.Empty;
        public int sampleRate;
        public double framePeriod;
        public bool unLoadSubBanks = false;
        public EnunuExtensions extensions;

        /// <summary>
        /// Whether the server synthesizes the wav (synthe) instead of OpenUtau running WORLD on spectrogram.npy / aperiodicity.npy.
        /// The server skips those files when this is true (ENUNU.client_reads_world_params is its negation); change both together.
        /// </summary>
        public bool UsesSynthe => extensions.wav_synthesizer.Contains("synthe") || feature_type.Equals("melf0");

        public static EnunuConfig Load(USinger singer) {
            var configPath = Path.Join(singer.Location, "enuconfig.yaml");
            var config = new RawEnunuConfig();
            if (File.Exists(configPath)) {
                var configTxt = File.ReadAllText(configPath);
                config = Yaml.DefaultDeserializer.Deserialize<RawEnunuConfig>(configTxt);
                config.enunu_type = "ENUNU";
            } else {
                config = SetSimpleENUNUConfig(singer.Location);
                config.enunu_type = "SimpleENUNU";
            }
            return config.Convert();
        }

        /// <summary>
        /// Reads config.yaml of an NNSVS model placed in the voicebank folder or its "model" folder.
        /// tablePath and questionPath are made relative to the voicebank folder, as EnunuSinger joins them with its Location.
        /// </summary>
        public static RawEnunuConfig SetSimpleENUNUConfig(string location) {
            string[] modelPaths = new string[] { location, Path.Join(location, "model") };
            string configYaml = "config.yaml";
            var config = new RawEnunuConfig();
            foreach (string modelPath in modelPaths) {
                if (File.Exists(Path.Join(modelPath, configYaml))) {
                    var configTxt = File.ReadAllText(Path.Join(modelPath, configYaml));
                    config = Yaml.DefaultDeserializer.Deserialize<RawEnunuConfig>(configTxt);
                    // table_path in config.yaml is relative to the model folder.
                    string? table = string.IsNullOrEmpty(config.tablePath) ? null : Path.GetFullPath(Path.Join(modelPath, config.tablePath));
                    if (table == null || !File.Exists(table)) {
                        table = FindFile(new[] { modelPath, location }, "*.table");
                    }
                    string? question = FindFile(new[] { modelPath, location }, "*.hed");
                    config.tablePath = table == null ? string.Empty : Path.GetRelativePath(location, table);
                    config.questionPath = question == null ? string.Empty : Path.GetRelativePath(location, question);
                }
            }
            return config;
        }

        static string? FindFile(string[] directories, string pattern) {
            foreach (string directory in directories) {
                string? file = Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly).FirstOrDefault();
                if (file != null) {
                    return file;
                }
            }
            return null;
        }
    }

    public class StyleFormatEntry {
        public string format = string.Empty;
        public List<string> index = new List<string>();
    }

    public class ExpressionDetail {
        public string name = string.Empty;
        public string type = string.Empty;
        public float min = 0;
        public float max = 0;
        public float default_value = 0;
        public string flag = string.Empty;
    }

    class EnunuExtensions {
        public List<string> ust_editor = new List<string>();
        public List<string> ust_converter = new List<string>();
        public List<string> score_editor = new List<string>();
        public List<string> timing_calculator = new List<string>();
        public List<string> timing_editor = new List<string>();
        public List<string> acoustic_calculator = new List<string>();
        public List<string> acoustic_editor = new List<string>();
        public List<string> wav_synthesizer = new List<string>();
        public List<string> wav_editor = new List<string>();
        public Dictionary<string, StyleFormatEntry> style_format = new Dictionary<string, StyleFormatEntry>();
        public Dictionary<string, ExpressionDetail> styles = new Dictionary<string, ExpressionDetail>();
    }

    class RawEnunuExtensions {
        public object ust_editor;
        public object ust_converter;
        public object score_editor;
        public object timing_calculator;
        public object timing_editor;
        public object acoustic_calculator;
        public object acoustic_editor;
        public object wav_synthesizer;
        public object wav_editor;
        public object style_format;
        public object styles;
    }

    class RawEnunuConfig {
        public string enunu_type = string.Empty;
        public string feature_type = string.Empty;
        public string tablePath = string.Empty;
        public string questionPath = string.Empty;
        public int sampleRate;
        public double framePeriod;
        public bool unload_subbanks;
        public RawEnunuExtensions extensions;

        public EnunuConfig Convert() {
            EnunuConfig enunuConfig = new EnunuConfig();
            enunuConfig.enunu_type = this.enunu_type;

            enunuConfig.feature_type = this.feature_type;
            enunuConfig.tablePath = this.tablePath;
            enunuConfig.questionPath = this.questionPath;
            enunuConfig.sampleRate = this.sampleRate;
            enunuConfig.framePeriod = this.framePeriod;
            enunuConfig.unLoadSubBanks = this.unload_subbanks;
            enunuConfig.extensions = new EnunuExtensions();
            if (this.extensions != null) {
                ParseEnunuExtension(enunuConfig.extensions.ust_editor, this.extensions.ust_editor);
                ParseEnunuExtension(enunuConfig.extensions.ust_converter, this.extensions.ust_converter);
                ParseEnunuExtension(enunuConfig.extensions.score_editor, this.extensions.score_editor);
                ParseEnunuExtension(enunuConfig.extensions.timing_calculator, this.extensions.timing_calculator);
                ParseEnunuExtension(enunuConfig.extensions.timing_editor, this.extensions.timing_editor);
                ParseEnunuExtension(enunuConfig.extensions.acoustic_calculator, this.extensions.acoustic_calculator);
                ParseEnunuExtension(enunuConfig.extensions.acoustic_editor, this.extensions.acoustic_editor);
                ParseEnunuExtension(enunuConfig.extensions.wav_synthesizer, this.extensions.wav_synthesizer);
                ParseEnunuExtension(enunuConfig.extensions.wav_editor, this.extensions.wav_editor);
                ParseEnunuStyleFormat(enunuConfig.extensions.style_format, this.extensions.style_format);
                ParseEnunuStyles(enunuConfig.extensions.styles, this.extensions.styles);
            }
            return enunuConfig;
        }

        private void ParseEnunuExtension(List<string> enunuExtension, object rawEnunuExtension) {
            if (rawEnunuExtension is string str) {
                enunuExtension.Add(str);
            } else if (rawEnunuExtension is List<object> list) {
                foreach (object o in list) {
                    if (o is string s) {
                        enunuExtension.Add(s);
                    }
                }
            }
        }

        private void ParseEnunuStyles(Dictionary<string, ExpressionDetail> enunuStyles, object rawEnunuStyle) {
            if (rawEnunuStyle is Dictionary<object, object> rawDict) {
                foreach (var kvp in rawDict) {
                    if (kvp.Key is string key && kvp.Value is Dictionary<object, object> innerDict) {
                        var exp = new ExpressionDetail();
                        foreach (var innerKvp in innerDict) {
                            if (innerKvp.Key is string innerKey && innerKvp.Value is string innerValue) {
                                switch (innerKey) {
                                    case "name":
                                        exp.name = innerValue;
                                        break;
                                    case "type":
                                        exp.type = innerValue;
                                        break;
                                    case "min":
                                        if (float.TryParse(innerValue, out var min)) {
                                            exp.min = min;
                                        }
                                        break;
                                    case "max":
                                        if (float.TryParse(innerValue, out var max)) {
                                            exp.max = max;
                                        }
                                        break;
                                    case "default_value":
                                        if (float.TryParse(innerValue, out var defaultValue)) {
                                            exp.default_value = defaultValue;
                                        }
                                        break;
                                    case "flag":
                                        exp.flag = innerValue;
                                        break;
                                }
                            }
                        }
                        enunuStyles.Add(key, exp);
                    }
                }
            }
        }

        private void ParseEnunuStyleFormat(Dictionary<string, StyleFormatEntry> enunuStyleFormat, object rawEnunuStyleFormat) {
            if (rawEnunuStyleFormat is Dictionary<object, object> rawDict) {
                foreach (var kvp in rawDict) {
                    var dict = new StyleFormatEntry();
                    var flag = false;
                    if (kvp.Key is string key) {
                        if (kvp.Value is Dictionary<object, object> innerDict) {
                            foreach (var item in innerDict) {
                                if(item.Key is string key2) {
                                    if (key2.Equals("format") && item.Value is string format) {
                                        dict.format = format;
                                        flag = true;
                                    } else if (key2.Equals("index") && item.Value is List<object> innerDict2) {
                                        foreach (var item2 in innerDict2) {
                                            if (item2 is string value) {
                                                dict.index.Add(value);
                                                flag = true;
                                            }
                                        }
                                    } else {
                                        flag = false;
                                    }
                                }
                            }
                        }
                        if (flag) {
                            enunuStyleFormat.Add(key, dict);
                        }
                    }
                }
            }
        }
    }
}
