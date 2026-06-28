using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using OpenUtau.Classic;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Serilog;

/*
 * This source code is partially based on the VOICEVOX engine.
 * https://github.com/VOICEVOX/voicevox_engine/blob/master/LGPL_LICENSE
 */

namespace OpenUtau.Core.Voicevox {
    public class VoicevoxConfig {
        //Information that each Singer has
        public Supported_features supported_features;
        public string name = string.Empty;
        public string speaker_uuid = string.Empty;
        public List<Styles> styles;
        public string version = string.Empty;
        public string policy = string.Empty;
        public string portraitPath = string.Empty;

        public List<Style_infos> style_infos;
        //Prepare for future additions of Teacher Singer.
        public List<(string name, Styles styles)> base_singer_style;
        public string base_singer_name = string.Empty;
        public string base_singer_style_name = string.Empty;

        public static VoicevoxConfig Load(USinger singer) {
            try {
                var response = VoicevoxClient.Inst.SendRequest(new VoicevoxURL() { method = "GET", path = "/engine_manifest" });
                var jObj = JObject.Parse(response.Item1);
                if (jObj.ContainsKey("detail")) {
                    var errorMessage = $"Response was incorrect. : {jObj}";
                    Log.Error(errorMessage);
                    throw new VoicevoxException(errorMessage);
                }
                var manifest = jObj.ToObject<Engine_manifest>();
                manifest.SaveLicenses(singer.Location);
            } catch (Exception e) {
                var errorMessage = $"Could not load Licenses.:{e}";
                Log.Error(errorMessage);
                throw new VoicevoxException(errorMessage, e);
            }
            try {

                var response = VoicevoxClient.Inst.SendRequest(new VoicevoxURL() { method = "GET", path = "/singers" });
                var jObj = JObject.Parse(response.Item1);
                if (jObj.ContainsKey("detail")) {
                    var errorMessage = $"Response was incorrect. : {jObj}";
                    Log.Error(errorMessage);
                    throw new VoicevoxException(errorMessage);
                }
                var configs = jObj["json"].ToObject<List<RawVoicevoxConfig>>();
                var parentDirectory = Directory.GetParent(singer.Location).ToString();
                List<VoicevoxConfig> vvList = new List<VoicevoxConfig>();
                foreach (RawVoicevoxConfig rowVoicevoxConfig in configs) {
                    VoicevoxConfig voicevoxConfig = rowVoicevoxConfig.Convert();
                    var folderPath = Path.Join(parentDirectory, voicevoxConfig.name);
                    var filePath = Path.Join(folderPath, "character.yaml");
                    if (!File.Exists(filePath)) {
                        Directory.CreateDirectory(folderPath);
                        string typename = string.Empty;
                        SingerTypeUtils.SingerTypeNames.TryGetValue(USingerType.Voicevox, out typename);
                        var config = new VoicebankConfig() {
                            Name = voicevoxConfig.name,
                            TextFileEncoding = Encoding.UTF8.WebName,
                            SingerType = typename,
                            PortraitHeight = 600,
                            Portrait = $"{voicevoxConfig.name}_portrait.png"
                        };
                        using (var stream = File.Open(filePath, FileMode.Create)) {
                            config.Save(stream);
                        }
                        //Create an empty file to read. May write information in the future?
                        File.WriteAllText(Path.Join(folderPath, "character.txt"), string.Empty);
                    }
                    vvList.Add(voicevoxConfig);
                }
                return vvList.Where(vv => vv.name.Equals(singer.Name)).ToList()[0];
            } catch (Exception e) {
                var errorMessage = "Could not load VOICEVOX singer.";
                Log.Error(errorMessage);
                throw new VoicevoxException(errorMessage, e);
            }

            return new VoicevoxConfig();
        }
        public void LoadInfo(VoicevoxConfig voicevoxConfig, string location) {
            if (voicevoxConfig.style_infos == null) {
                var queryurl = new VoicevoxURL() { method = "GET", path = "/singer_info", query = new Dictionary<string, string> { { "speaker_uuid", voicevoxConfig.speaker_uuid } } };
                var response = VoicevoxClient.Inst.SendRequest(queryurl);
                var jObj = JObject.Parse(response.Item1);
                if (jObj.ContainsKey("detail")) {
                    Log.Error($"Response was incorrect. : {jObj}");
                } else {
                    var rawSinger_Info = jObj.ToObject<RawSinger_info>();
                    if (rawSinger_Info != null) {
                        rawSinger_Info.SetInfo(voicevoxConfig, location);
                    }
                }
            }

        }
    }

    public class Engine_manifest {
        public class Update_infos {
            public string version;
            public IList<string> descriptions;
            public IList<string> contributors;

        }
        public class Dependency_licenses {
            public string name;
            public string version;
            public string license;
            public string text;

        }
        public class Supported_features {
            public bool adjust_mora_pitch;
            public bool adjust_phoneme_length;
            public bool adjust_speed_scale;
            public bool adjust_pitch_scale;
            public bool adjust_intonation_scale;
            public bool adjust_volume_scale;
            public bool interrogative_upspeak;
            public bool synthesis_morphing;
            public bool sing;
            public bool manage_library;

        }

        public string manifest_version;
        public string name;
        public string brand_name;
        public string uuid;
        public string url;
        public string icon;
        public int default_sampling_rate;
        public int frame_rate;
        public string terms_of_service;
        public IList<Update_infos> update_infos;
        public IList<Dependency_licenses> dependency_licenses;
        public string supported_vvlib_manifest_version;
        public Supported_features supported_features;

        public void SaveLicenses(string location) {
            var parentDirectory = Directory.GetParent(location).ToString();
            var licenseDirectory = Path.Join(parentDirectory, "Licenses");
            if (!Directory.Exists(licenseDirectory)) {
                Directory.CreateDirectory(licenseDirectory);
            }
            var filePath = Path.Join(licenseDirectory, "terms_of_service.txt");
            if (!string.IsNullOrEmpty(terms_of_service)) {
                File.WriteAllText(filePath, terms_of_service);
            }
            foreach (var item in dependency_licenses) {
                item.name = item.name.Replace("\"","");
                filePath = Path.Join(licenseDirectory, $"{item.name}_License.txt");
                if (!string.IsNullOrEmpty(item.text)) {
                    File.WriteAllText(filePath, $"license:{item.license}\nversion:{item.version}\n\n" + item.text);
                }
            }
        }
    }

    public class Style_infos {
        public int id;
        public string icon = string.Empty;
        public string portrait = string.Empty;
        public IList<string> voice_samples = new List<string>();

    }
    class RawSinger_info {
        public string policy = string.Empty;
        public string portrait = string.Empty;
        public IList<Style_infos> style_infos = new List<Style_infos>();

        public void SetInfo(VoicevoxConfig voicevoxConfig, string location) {
            Log.Information($"Begin setup of Voicevox SingerInfo.");
            try {
                var readmePath = Path.Join(location, "readme.txt");
                if (!string.IsNullOrEmpty(this.policy)) {
                    voicevoxConfig.policy = this.policy;
                    File.WriteAllText(readmePath, this.policy);
                }
                voicevoxConfig.portraitPath = Path.Join(location, $"{voicevoxConfig.name}_portrait.png");
                Base64.Base64ToFile(this.portrait, voicevoxConfig.portraitPath);
                if (this.style_infos != null) {
                    voicevoxConfig.style_infos = new List<Style_infos>();
                    for (int i = 0; i < this.style_infos.Count; i++) {
                        voicevoxConfig.style_infos.Add(new Style_infos());
                        for (int a = 0; a < style_infos[i].voice_samples.Count; a++) {
                            voicevoxConfig.style_infos[i].voice_samples.Add(Path.Join(location, $"{voicevoxConfig.name}_{voicevoxConfig.styles[i].name}_{a}.wav"));
                            checkAndSetFiles(this.style_infos[i].voice_samples[a], voicevoxConfig.style_infos[i].voice_samples[a]);
                        }
                        voicevoxConfig.style_infos[i].icon = Path.Join(location, $"{voicevoxConfig.name}_{voicevoxConfig.styles[i].name}_icon.png");
                        checkAndSetFiles(this.style_infos[i].icon, voicevoxConfig.style_infos[i].icon);

                        voicevoxConfig.style_infos[i].portrait = Path.Join(location, $"{voicevoxConfig.name}_{voicevoxConfig.styles[i].name}_portrait.png");
                        checkAndSetFiles(this.portrait, voicevoxConfig.style_infos[i].portrait);

                        voicevoxConfig.style_infos[i].id = this.style_infos[i].id;
                    }
                }
            } catch (Exception e) {
                Log.Error($"Could not create character file. : {e}");
            }
            Log.Information($"Voicevox SingerInfo setup complete.");
        }

        public void checkAndSetFiles(string base64str, string filePath) {
            if (!String.IsNullOrEmpty(base64str)) {
                Base64.Base64ToFile(base64str, filePath);
            }
        }
    }

    public class Supported_features {
        public string permitted_synthesis_morphing;

    }
    public class Styles {
        public string name;
        public int id;
        public string type;

    }
    class RawVoicevoxConfig {
        public Supported_features supported_features;
        public string name;
        public string speaker_uuid;
        public List<Styles> styles;
        public string version;
        public string base_singer_name;
        public string base_singer_style_name;

        public VoicevoxConfig Convert() {
            VoicevoxConfig voicevoxConfig = new VoicevoxConfig();
            voicevoxConfig.name = this.name.Replace("/", "_");
            voicevoxConfig.version = this.version;
            voicevoxConfig.speaker_uuid = this.speaker_uuid;
            voicevoxConfig.styles = this.styles;
            voicevoxConfig.supported_features = this.supported_features;
            voicevoxConfig.base_singer_name = this.base_singer_name;
            voicevoxConfig.base_singer_style_name = this.base_singer_style_name;
            return voicevoxConfig;
        }
    }
}
