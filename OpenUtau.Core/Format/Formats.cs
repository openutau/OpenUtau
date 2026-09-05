using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenUtau.Classic;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Core.Format {
    public enum ProjectFormats { Unknown, Vsq3, Vsq4, Ust, Ustx, Midi, Ufdata, Musicxml, Svp };

    public static class Formats {
        const string ustMatch = "[#SETTING]";
        const string ustxMatchJson = "\"ustxVersion\":";
        const string ustxMatchYaml = "ustx_version:";
        const string vsq3Match = VSQx.vsq3NameSpace;
        const string vsq4Match = VSQx.vsq4NameSpace;
        const string midiMatch = "MThd";
        const string ufdataMatch = "\"formatVersion\":";
        const string musicxmlMatch = "score-partwise";
        const string svpVersion = "\"version\":";
        const string svpdata = "\"database\":";
        const string svp2 = "\"mouthOpening\":";

        public static ProjectFormats DetectProjectFormat(string file) {
            var lines = new List<string>();
            using (var reader = new StreamReader(file)) {
                for (int i = 0; i < 10 && !reader.EndOfStream; ++i) {
                    lines.Add(reader.ReadLine());
                }
            }
            string contents = string.Join("\n", lines);
            if (contents.Contains(ustMatch)) {
                return ProjectFormats.Ust;
            } else if (contents.Contains(ustxMatchJson) || contents.Contains(ustxMatchYaml)) {
                return ProjectFormats.Ustx;
            } else if (contents.Contains(vsq3Match)) {
                return ProjectFormats.Vsq3;
            } else if (contents.Contains(vsq4Match)) {
                return ProjectFormats.Vsq4;
            } else if (contents.Contains(midiMatch)) {
                return ProjectFormats.Midi;
            } else if (contents.Contains(ufdataMatch)) {
                return ProjectFormats.Ufdata;
            } else if (contents.Contains(musicxmlMatch)) {
                return ProjectFormats.Musicxml;
            } else if (contents.Contains(svp2)) {
                return ProjectFormats.Svp;
            } else if (contents.Contains(svpVersion) || contents.Contains(svpdata)) {
                return ProjectFormats.Svp;
            } else {
                return ProjectFormats.Unknown;
            }
        }

        /// <summary>
        /// Read project from files to a new UProject object, used by LoadProject and ImportTracks.
        /// </summary>
        /// <param name="files">Names of the files to be loaded</param>
        public static UProject? ReadProject(string[] files){
            if (files.Length < 1) {
                return null;
            }
            ProjectFormats format = DetectProjectFormat(files[0]);
            UProject? project = null;
            var lines = new List<string>();
            using (var reader = new StreamReader(files[0])) {
                for (int i = 0; i < 10 && !reader.EndOfStream; ++i) {
                    lines.Add(reader.ReadLine());
                }
            }
            string contents = string.Join("\n", lines);
            switch (format) {
                case ProjectFormats.Ustx:
                    project = Ustx.Load(files[0]);
                    break;
                case ProjectFormats.Vsq3:
                case ProjectFormats.Vsq4:
                    project = VSQx.Load(files[0]);
                    break;
                case ProjectFormats.Ust:
                    project = Ust.Load(files);
                    break;
                case ProjectFormats.Midi:
                    project = MidiWriter.LoadProject(files[0]);
                    break;
                case ProjectFormats.Ufdata:
                    project = Ufdata.Load(files[0]);
                    break;
                case ProjectFormats.Musicxml:
                    project = MusicXML.LoadProject(files[0]);
                    break;
                case ProjectFormats.Svp:
                    if (contents.Contains(svp2)) {
                        project = SVP2.Load(files[0]);
                    } else {
                        project = SVP.Load(files[0]);
                    }
                    break;
                default:
                    throw new FileFormatException("Unknown file format");
            }
            return project;
        }

        /// <summary>
        /// Load project from files.
        /// </summary>
        /// <param name="files">Names of the files to be loaded</param>
        public static void LoadProject(string[] files) {
            UProject project = ReadProject(files);
            if (project != null) {
                DocManager.Inst.ExecuteCmd(new LoadProjectNotification(project));
            }
        }


        /// <summary>
        /// Read multiple projects for importing tracks
        /// </summary>
        /// <param name="files">Names of the files to be loaded</param>
        /// <returns></returns>
        public static UProject[] ReadProjects(string[] files){
            if (files == null || files.Length < 1) {
                return new UProject[0];
            }
            return files
                .Select(file => ReadProject(new string[] { file }))
                .Where(p => p != null)
                .Cast<UProject>()
                .ToArray();
        }

        /// <summary>
        /// Load project from backup file.
        /// </summary>
        /// <param name="files">Names of the files to be loaded</param>
        public static void RecoveryProject(string[] files) {
            UProject project = ReadProject(files);
            if (project != null) {
                string originalPath = project.FilePath.Replace("-autosave.ustx", ".ustx").Replace("-backup.ustx", ".ustx");
                if (File.Exists(originalPath)) {
                    project.FilePath = originalPath;
                } else {
                    project.FilePath = string.Empty;
                    project.Saved = false;
                }
                
                DocManager.Inst.ExecuteCmd(new LoadProjectNotification(project));
            }
        }

        /// <summary>
        /// Import tracks from files to the current existing editing project.
        /// </summary>
        /// <param name="project">The current existing editing project</param>
        /// <param name="loadedProjects">loaded project objects to be imported</param>
        /// <param name="importTempo">If set to true, the tempo of the imported project will be used</param>
        /// <exception cref="FileFormatException"></exception>
        public static void ImportTracks(UProject project, UProject[] loadedProjects, bool importTempo = true) {
            if (loadedProjects == null || loadedProjects.Length < 1) {
                return;
            }
            int initialTracks = project.tracks.Count;
            int initialParts = project.parts.Count;
            foreach (UProject loaded in loadedProjects) {
                int trackCount = project.tracks.Count;
                foreach (var (abbr, descriptor) in loaded.expressions) {
                    if (!project.expressions.ContainsKey(abbr)) {
                        project.expressions.Add(abbr, descriptor);
                    }
                }
                foreach (var track in loaded.tracks) {
                    track.TrackNo = project.tracks.Count;
                    project.tracks.Add(track);
                }
                foreach (var part in loaded.parts) {
                    project.parts.Add(part);
                    part.trackNo += trackCount;
                }
            }
            if (importTempo) {
                var loaded = loadedProjects[0];
                project.timeSignatures.Clear();
                project.timeSignatures.AddRange(loaded.timeSignatures);
                project.tempos.Clear();
                project.tempos.AddRange(loaded.tempos);
            }
            for (int i = initialTracks; i < project.tracks.Count; i++) {
                project.tracks[i].AfterLoad(project);
            }
            for (int i = initialParts; i < project.parts.Count; i++) {
                var part = project.parts[i];
                part.AfterLoad(project, project.tracks[part.trackNo]);
            }
            project.ValidateFull();
            DocManager.Inst.ExecuteCmd(new LoadProjectNotification(project));
        }

        /// <summary>
        /// Import tracks from files to the current existing editing project.
        /// </summary>
        /// <param name="project">The current existing editing project</param>
        /// <param name="files">Names of the files to be imported</param>
        /// <param name="importTempo">If set to true, the tempo of the imported project will be used</param>
        /// <exception cref="FileFormatException"></exception>
        public static void ImportTracks(UProject project, string[] files, bool importTempo = true) {
            if (files == null || files.Length < 1) {
                return;
            }
            UProject[] loadedProjects = files
                .Select(file => ReadProject(new string[] { file }))
                .Where(p => p != null)
                .Cast<UProject>()
                .ToArray();
            ImportTracks(project, loadedProjects, importTempo);
        }
    }
}
