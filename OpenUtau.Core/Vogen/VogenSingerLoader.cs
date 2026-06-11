using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Serilog;
using SharpCompress.Archives;
using SharpCompress.Readers;

namespace OpenUtau.Core.Vogen {
    [Serializable]
    class VogenMeta {
        public string name;
        public string id;
        public string version;
        public string builtBy;
        public string voiceBy;
        public string avatar;
        public string portrait;
        public float portraitOpacity = 0.67f;
        public int portraitHeight = 0;
        public string web;
        public string misc;
    }

    class VogenSingerLoader {
        readonly string basePath;

        public static IEnumerable<USinger> FindAllSingers() {
            List<USinger> singers = new List<USinger>();
            foreach (var path in PathManager.Inst.SingersPaths) {
                var loader = new VogenSingerLoader(path);
                singers.AddRange(loader.SearchAll());
            }
            return singers;
        }

        public VogenSingerLoader(string basePath) {
            this.basePath = basePath;
        }

        public IEnumerable<USinger> SearchAll() {
            var result = new List<USinger>();
            if (!Directory.Exists(basePath)) {
                return result;
            }
            IEnumerable<string> files;
            if (Preferences.Default.LoadDeepFolderSinger) {
                files = Directory.EnumerateFiles(basePath, "*.vogeon", SearchOption.AllDirectories);
            } else {
                // TopDirectoryOnly
                files = Directory.EnumerateFiles(basePath, "*.vogeon");
            }
            result.AddRange(files
                .Select(filePath => {
                    try {
                        return LoadSinger(filePath);
                    } catch (Exception e) {
                        Log.Error(e, "Failed to load Vogen singer.");
                        return null;
                    }
                })
                .OfType<USinger>());
            return result;
        }

        public USinger LoadSinger(string filePath) {
            VogenMeta meta;
            byte[] model;
            byte[] avatar = null;
            using (var archive = ArchiveFactory.OpenArchive(filePath, new ReaderOptions())) {
                var metaEntry = archive.Entries.First(e => e.Key == "meta.json");
                if (metaEntry == null) {
                    throw new ArgumentException("missing meta.json");
                }
                using (var stream = metaEntry.OpenEntryStream()) {
                    using var reader = new StreamReader(stream, Encoding.UTF8);
                    JsonSerializer serializer = new JsonSerializer();
                    meta = (VogenMeta)serializer.Deserialize(reader, typeof(VogenMeta));
                }
                model = Zip.ExtractBytes(archive, "model.onnx");
                if (model == null) {
                    throw new ArgumentException("missing model.onnx");
                }
                if (!string.IsNullOrEmpty(meta.avatar)) {
                    avatar = Zip.ExtractBytes(archive, meta.avatar);
                }
            }
            return new VogenSinger(filePath, meta, model, avatar);
        }
    }
}
