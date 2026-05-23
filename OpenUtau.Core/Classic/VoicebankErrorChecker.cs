using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NAudio.Wave;

namespace OpenUtau.Classic {
    public class VoicebankError {
        public string messageKey = string.Empty;
        public string[] strings = Array.Empty<string>();
        public FileTrace? trace;
        public string soundFile = string.Empty;
        public Exception? e;
    }

    public class VoicebankErrorChecker {
        public List<VoicebankError> Errors = new List<VoicebankError>();
        public List<VoicebankError> Infos = new List<VoicebankError>();

        readonly string path;
        readonly Voicebank voicebank;
        readonly Dictionary<string, double> fileDurations = new Dictionary<string, double>();

        public VoicebankErrorChecker(string path, string basePath) {
            this.path = path;
            string charTxt = Path.Combine(path, VoicebankLoader.kCharTxt);
            voicebank = new Voicebank() {
                File = charTxt,
                BasePath = basePath,
            };
        }

        public void Check() {
            if (!File.Exists(voicebank.File)) {
                Errors.Add(new VoicebankError() {
                    messageKey = "singererror.txtnotfound",
                });
                return;
            }
            string charYaml = Path.Combine(path, VoicebankLoader.kCharYaml);
            if (!File.Exists(charYaml)) {
                Infos.Add(new VoicebankError() {
                    messageKey = "singererror.yamlnotfound",
                });
            }
            try {
                VoicebankLoader.LoadVoicebank(voicebank);
            } catch (Exception e) {
                Errors.Add(new VoicebankError() {
                    messageKey = "singererror.failedload",
                    e = e,
                });
                return;
            }
            foreach (var otoSet in voicebank.OtoSets) {
                string dir = Path.Combine(path, Path.GetDirectoryName(otoSet.File));
                foreach (var oto in otoSet.Otos) {
                    if (string.IsNullOrEmpty(oto.FileTrace?.line) || oto.FileTrace.line.StartsWith("#Charset:")) {
                        continue;
                    }
                    if (!oto.IsValid) {
                        Errors.Add(new VoicebankError() {
                            trace = oto.FileTrace,
                            messageKey = "singererror.invalidoto",
                        });
                        continue;
                    }
                    string filePath = Path.Combine(dir, oto.Wav);
                    if (!TryGetFileDuration(filePath, oto, out double fileDuration)) {
                        continue;
                    }
                    if (fileDuration <= 0) {
                        Errors.Add(new VoicebankError() {
                            soundFile = filePath,
                            messageKey = "singererror.invalidduration",
                            strings = new string[] { fileDuration.ToString() }
                        });
                        continue;
                    }
                    CheckOto(oto, fileDuration);
                }
                CheckNFDFiles(otoSet);
            }
            if (FindDuplication(out List<Oto> duplicates)) {
                string message = "";
                duplicates.ForEach(oto => message += $"\n{oto.FileTrace?.file} line {oto.FileTrace?.lineNumber}: {oto.Alias}");
                Errors.Add(new VoicebankError() {
                    messageKey = "singererror.duplicatealias",
                    strings = new string[] { message },
                });
            }
            foreach (var otoSet in voicebank.OtoSets) {
                CheckCaseMatchForFileReference(otoSet);
                CheckDuplicatedNameIgnoringCase(otoSet);
            }
            CheckCaseMatchForFileReference(voicebank.BasePath, new string[]{
                "chatacter.txt",
                "character.yaml",
                "prefix.map",
                });
        }

        bool TryGetFileDuration(string filePath, Oto oto, out double fileDuration) {
            if (fileDurations.TryGetValue(filePath, out fileDuration)) {
                return true;
            }
            if (!File.Exists(filePath)) {
                Errors.Add(new VoicebankError() {
                    trace = oto.FileTrace,
                    soundFile = filePath,
                    messageKey = "singererror.soundmissing",
                });
                return false;
            }
            try {
                using (var wav = Core.Format.Wave.OpenFile(filePath)) {
                    fileDuration = wav.TotalTime.TotalMilliseconds;
                    var waveFormat = wav.WaveFormat;
                    if (waveFormat.SampleRate != 44100) {
                        Errors.Add(new VoicebankError() {
                            soundFile = filePath,
                            messageKey = "singererror.samplerate"
                        });
                    }
                    if (waveFormat.Channels != 1) {
                        Infos.Add(new VoicebankError() {
                            soundFile = filePath,
                            messageKey = "singererror.mono"
                        });
                    }
                    if (waveFormat.BitsPerSample != 16) {
                        Errors.Add(new VoicebankError() {
                            soundFile = filePath,
                            messageKey = "singererror.bitdepth"
                        });
                    }
                }
            } catch (Exception e) {
                Errors.Add(new VoicebankError() {
                    soundFile = filePath,
                    messageKey = "singererror.soundnotopened",
                    e = e,
                });
                return false;
            }
            fileDurations.Add(filePath, fileDuration);
            return true;
        }

        bool CheckOto(Oto oto, double fileDuration) {
            bool valid = true;
            if (oto.Offset < 0) {
                Errors.Add(new VoicebankError() {
                    trace = oto.FileTrace,
                    messageKey = "singererror.offsetshort",
                });
                valid = false;
            }
            if (oto.Offset > fileDuration) {
                Errors.Add(new VoicebankError() {
                    trace = oto.FileTrace,
                    messageKey = "singererror.offsetoutofduration"
                });
                valid = false;
            }
            if (oto.Preutter < 0) {
                Errors.Add(new VoicebankError() {
                    trace = oto.FileTrace,
                    messageKey = "singererror.preuttershort",
                });
                valid = false;
            }
            if (oto.Preutter + oto.Offset > fileDuration) {
                Errors.Add(new VoicebankError() {
                    trace = oto.FileTrace,
                    messageKey = "singererror.preutteroutofduration"
                });
                valid = false;
            }
            if (oto.Consonant < 0) {
                Errors.Add(new VoicebankError() {
                    trace = oto.FileTrace,
                    messageKey = "singererror.consonantshort",
                });
                valid = false;
            }
            if (oto.Consonant + oto.Offset > fileDuration) {
                Errors.Add(new VoicebankError() {
                    trace = oto.FileTrace,
                    messageKey = "singererror.consonantoutofduration"
                });
                valid = false;
            }
            if (oto.Overlap + oto.Offset > fileDuration) {
                Errors.Add(new VoicebankError() {
                    trace = oto.FileTrace,
                    messageKey = "singererror.overlapoutofduration"
                });
                valid = false;
            }
            double cutoff = oto.Cutoff < 0 ? oto.Offset - oto.Cutoff : fileDuration - oto.Cutoff;
            if (cutoff < oto.Offset + oto.Preutter) {
                Errors.Add(new VoicebankError() {
                    trace = oto.FileTrace,
                    messageKey = "singererror.cutoffpreutter",
                });
                valid = false;
            }
            if (cutoff < oto.Offset + oto.Overlap) {
                Errors.Add(new VoicebankError() {
                    trace = oto.FileTrace,
                    messageKey = "singererror.cutoffoverlap",
                });
                valid = false;
            }
            if (cutoff <= oto.Offset + oto.Consonant) {
                Errors.Add(new VoicebankError() {
                    trace = oto.FileTrace,
                    messageKey = "singererror.cutoffconsonant",
                });
                valid = false;
            }
            if (cutoff > fileDuration) {
                Errors.Add(new VoicebankError() {
                    trace = oto.FileTrace,
                    messageKey = "singererror.cutoffoutofduration"
                });
                valid = false;
            }

            if (oto.Alias.StartsWith(" ") || oto.Alias.EndsWith(" ")) {
                Infos.Add(new VoicebankError() {
                    trace = oto.FileTrace,
                    messageKey = "singererror.aliasstartwithspace",
                });
            } else if (Regex.Matches(oto.Alias, " ").Count > 1) {
                Infos.Add(new VoicebankError() {
                    trace = oto.FileTrace,
                    messageKey = "singererror.aliasmultiplespaces",
                });
            }
            if (oto.Alias.Contains('　')) {
                Infos.Add(new VoicebankError() {
                    trace = oto.FileTrace,
                    messageKey = "singererror.aliasfullwidthspaces",
                });
            }
            if (!oto.Alias.IsNormalized()) {
                Infos.Add(new VoicebankError() {
                    trace = oto.FileTrace,
                    messageKey = "singererror.aliasisnfd",
                });
            }
            return valid;
        }

        void CheckNFDFiles(OtoSet otoSet) {
            var wavGroups = otoSet.Otos.Where(oto => oto.IsValid).GroupBy(oto => oto.Wav);
            foreach (var group in wavGroups) {
                string? fileTraceLine = group.First().FileTrace?.line;
                if (fileTraceLine == null) {
                    continue;
                }
                if (group.Key != fileTraceLine.Split('=')[0].Trim() && !group.Key.IsNormalized()) {
                    Errors.Add(new VoicebankError() {
                        soundFile = Path.Combine(Path.GetDirectoryName(otoSet.File), group.Key),
                        messageKey = "singererror.wavisnfd",
                    });
                }
            }
        }

        bool FindDuplication(out List<Oto> duplicates) {
            duplicates = voicebank.OtoSets
                .SelectMany(set => set.Otos)
                .Where(oto => !string.IsNullOrWhiteSpace(oto.Alias))
                .GroupBy(oto => oto.Alias)
                .Where(alias => alias.Count() > 1)
                .SelectMany(group => group).ToList();

            return duplicates.Count > 0;
        }

        /// <summary>
        /// Check if the file names in the oto.ini are the same as the file names in the file system.
        /// </summary>
        /// <param name="otoSet">otoSet to be checked</param>
        /// <returns></returns>
        bool CheckCaseMatchForFileReference(OtoSet otoSet) {
            return CheckCaseMatchForFileReference(
                Directory.GetParent(otoSet.File).FullName,
                otoSet.Otos
                    .Select(oto => oto.Wav)
                    .Append(otoSet.File)//oto.ini itself
                    .ToHashSet());
        }

        bool CheckCaseMatchForFileReference(string folder, IEnumerable<string> correctFileNames) {
            bool valid = true;
            Dictionary<string, string> fileNamesLowerToActual = Directory.GetFiles(folder)
                .Select(Path.GetFileName)
                .ToDictionary(x => x.ToLower(), x => x);
            foreach (string fileName in correctFileNames) {
                if (string.IsNullOrWhiteSpace(fileName) || !fileNamesLowerToActual.ContainsKey(fileName.ToLower())) {
                    continue;
                }
                if (fileNamesLowerToActual[fileName.ToLower()] != fileName) {
                    valid = false;
                    Errors.Add(new VoicebankError() {
                        messageKey = "singererror.wrongcase",
                        strings = new string[] { Path.Join(folder, fileName), Path.Join(folder, fileNamesLowerToActual[fileName.ToLower()]) },
                    });
                }
            }
            return valid;
        }

        /// <summary>
        /// Check if the file names are duplicated when converted to lower case.
        /// </summary>
        /// <param name="otoSet">otoSet to be checked</param>
        /// <returns></returns>
        bool CheckDuplicatedNameIgnoringCase(OtoSet otoSet) {
            var wavNames = otoSet.Otos.Select(x => x.Wav).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
            var duplicatedGroups = wavNames.GroupBy(x => x.ToLower())
                .Where(group => group.Count() > 1)
                .ToList();
            foreach (var group in duplicatedGroups) {
                Errors.Add(new VoicebankError() {
                    messageKey = "singererror.duplicatename",
                    strings = new string[] { otoSet.Name, string.Join(", ", group.Select(x => $"\"{x}\"")) },
                });
            }
            return duplicatedGroups.Count == 0;
        }
    }
}
