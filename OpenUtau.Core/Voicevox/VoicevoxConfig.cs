using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
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
                var jObj = JsonObject.Parse(response.Item1);
                if (jObj?.AsObject().ContainsKey("detail") == true) {
                    var errorMessage = $"Response was incorrect. : {jObj}";
                    Log.Error(errorMessage);
                    throw new VoicevoxException(errorMessage);
                }
                var manifest = Json.Deserialize<Engine_manifest>(jObj);
                if (manifest != null) {
                    try {
                        manifest.SaveLicenses(singer.Location);
                    } catch (Exception e) {
                        // Licenses are not needed to sing, so keep loading the singer.
                        // The version file is not updated, so saving is retried next time.
                        Log.Error(e, "Could not save Licenses.");
                    }
                }
            } catch (Exception e) {
                var errorMessage = $"Could not load engine manifest.:{e}";
                Log.Error(errorMessage);
                throw new VoicevoxException(errorMessage, e);
            }
            try {

                var response = VoicevoxClient.Inst.SendRequest(new VoicevoxURL() { method = "GET", path = "/singers" });
                var jObj = JsonObject.Parse(response.Item1);
                if (jObj?.AsObject().ContainsKey("detail") == true) {
                    var errorMessage = $"Response was incorrect. : {jObj}";
                    Log.Error(errorMessage);
                    throw new VoicevoxException(errorMessage);
                }
                var configs = Json.Deserialize<List<RawVoicevoxConfig>>(jObj?["json"]);
                var parentDirectory = Directory.GetParent(singer.Location).ToString();
                List<VoicevoxConfig> vvList = new List<VoicevoxConfig>();
                foreach (RawVoicevoxConfig rowVoicevoxConfig in configs) {
                    VoicevoxConfig voicevoxConfig = rowVoicevoxConfig.Convert();
                    var folderPath = Path.Join(parentDirectory, voicevoxConfig.name);
                    var filePath = Path.Join(folderPath, "character.yaml");
                    if (!File.Exists(filePath) || !singer.Version.Equals(voicevoxConfig.version)) {
                        Directory.CreateDirectory(folderPath);
                        string typename = string.Empty;
                        SingerTypeUtils.SingerTypeNames.TryGetValue(USingerType.Voicevox, out typename);
                        var config = new VoicebankConfig() {
                            Name = voicevoxConfig.name,
                            TextFileEncoding = Encoding.UTF8.WebName,
                            SingerType = typename,
                            PortraitHeight = 600,
                            Portrait = $"{voicevoxConfig.name}_portrait.png",
                            Image = voicevoxConfig.styles != null && voicevoxConfig.styles.Count > 0
                                    ? $"{voicevoxConfig.name}_{voicevoxConfig.styles[0].name}_icon.png"
                                    : $"{voicevoxConfig.name}_icon.png",
                            Version = voicevoxConfig.version
                        };
                        using (var stream = File.Open(filePath, FileMode.Create)) {
                            config.Save(stream);
                        }
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
                var jObj = JsonObject.Parse(response.Item1);
                if (jObj?.AsObject().ContainsKey("detail") == true) {
                    Log.Error($"Response was incorrect. : {jObj}");
                } else {
                    var rawSinger_Info = Json.Deserialize<RawSinger_info>(jObj);
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
        public double frame_rate;
        public string terms_of_service;
        public IList<Update_infos> update_infos;
        public IList<Dependency_licenses> dependency_licenses;
        public string supported_vvlib_manifest_version;
        public Supported_features supported_features;

        const string VersionFileName = "manifest_version.txt";

        public void SaveLicenses(string location) {
            var parentDirectory = Directory.GetParent(location).ToString();
            var licenseDirectory = Path.Join(parentDirectory, "Licenses");
            Directory.CreateDirectory(licenseDirectory);

            var versionFilePath = Path.Join(licenseDirectory, VersionFileName);
            string savedVersion = File.Exists(versionFilePath)
                ? File.ReadAllText(versionFilePath).Trim()
                : string.Empty;
            if (savedVersion.Equals(manifest_version)) {
                return;
            }

            foreach (var file in Directory.GetFiles(licenseDirectory)) {
                if (Path.GetFileName(file).Equals(VersionFileName, StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }
                try {
                    File.Delete(file);
                } catch (Exception e) {
                    Log.Warning($"Could not delete {file}: {e.Message}");
                }
            }

            if (!string.IsNullOrEmpty(terms_of_service)) {
                File.WriteAllText(Path.Join(licenseDirectory, "terms_of_service.txt"), terms_of_service);
            }
            if (dependency_licenses != null) {
                var invalidChars = Path.GetInvalidFileNameChars();
                foreach (var item in dependency_licenses) {
                    if (string.IsNullOrEmpty(item.name) || string.IsNullOrEmpty(item.text)) {
                        continue;
                    }
                    var fileName = new string(item.name.Replace("\"", "")
                        .Select(c => invalidChars.Contains(c) ? '_' : c).ToArray());
                    File.WriteAllText(Path.Join(licenseDirectory, $"{fileName}_License.txt"),
                        $"license:{item.license}\nversion:{item.version}\n\n" + item.text);
                }
            }

            if (!string.IsNullOrEmpty(manifest_version)) {
                File.WriteAllText(versionFilePath, manifest_version);
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
