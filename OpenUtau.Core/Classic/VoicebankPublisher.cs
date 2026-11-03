using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

using OpenUtau.Core.Ustx;

namespace OpenUtau.Classic {
    public class VoicebankPublisher {
        private readonly Action<double, string> progress;
        private readonly Ignore.Ignore? ignore;

        public VoicebankPublisher(Action<double, string> progress, string? gitIgnore) {
            this.progress = progress;
            if (gitIgnore != null) {
                ignore = new Ignore.Ignore();
                ignore.Add(gitIgnore.Split("\n"));
            }
        }

        private static void ModifyConfig(USinger singer, Action<VoicebankConfig> modify) {
            var yamlFile = Path.Combine(singer.Location, "character.yaml");
            VoicebankConfig? config = null;
            if (File.Exists(yamlFile)) {
                using (var stream = File.OpenRead(yamlFile)) {
                    config = VoicebankConfig.Load(stream);
                }
            }
            if (config == null) {
                config = new VoicebankConfig();
            }
            modify(config);
            using (var stream = File.Open(yamlFile, FileMode.Create)) {
                config.Save(stream);
            }
        }

        private bool IsIgnored(string relativePath) {
            return ignore?.IsIgnored(relativePath.Replace('\\', '/')) ?? false;
        }

        private List<string> GetFilesToPack(string singerPath) {
            List<string> fileList = Directory.EnumerateFiles(singerPath, "*.*", SearchOption.AllDirectories).ToList();
            List<string> packList = fileList.FindAll(x => !IsIgnored(System.IO.Path.GetRelativePath(singerPath, x)));
            return packList;
        }

        ///<summary>
        ///Compress a voicebank into an optimized zip archive for distribution.
        ///This function only supports voicebanks that follow the classic packaging model,
        ///including utau, enunu and diffsinger.
        ///Vogen voicebanks aren't supported.
        ///</summary>
        public void Publish(USinger singer, string outputFile) {
            var location = singer.Location;
            if (!Directory.Exists(location)) {
                return;
            }
            progress.Invoke(0, $"Publishing {singer.Name}");
            //Write singer type into character.yaml
            try {
                ModifyConfig(singer, config => {
                    config.SingerType = SingerTypeUtils.SingerTypeNames.TryGetValue(singer.SingerType, out var name)
                        ? name
                        : singer.SingerType.ToString().ToLower();
                });
            } catch (Exception e) { }
            var packList = GetFilesToPack(location);
            int index = 0;
            int fileCount = packList.Count();
            using (ZipArchive archive = new ZipArchive(File.Create(outputFile), ZipArchiveMode.Create)) {
                foreach (var absFilePath in packList) {
                    index++;
                    progress.Invoke(100.0 * index / fileCount, $"Compressing {absFilePath}");
                    string reFilePath = Path.GetRelativePath(location, absFilePath).Replace('\\', '/');
                    archive.CreateEntryFromFile(absFilePath, reFilePath);
                }
            }
            progress.Invoke(0, $"Published {singer.Name} to {outputFile}");
        }
    }
}
