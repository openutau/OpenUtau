using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using OpenUtau.Classic.Flags;
using OpenUtau.Core;
using OpenUtau.Core.Format;
using OpenUtau.Core.Ustx;
using SharpCompress;

namespace OpenUtau.Classic {

    public static class Ust {
        static readonly Encoding ShiftJIS = Encoding.GetEncoding("shift_jis");
        static List<string> undefinedFlags = new List<string>();
        static string tool2 = string.Empty;

        public static UProject Load(string[] files) {
            foreach (var file in files) {
                if (Formats.DetectProjectFormat(file) != ProjectFormats.Ust) {
                    throw new MessageCustomizableException("Multiple files must be all Ust files", "<translate:errors.failed.importustandothers>", new FileFormatException(), false);
                }
            }

            var projects = new List<UProject>();
            undefinedFlags.Clear();
            foreach (var file in files) {
                var encoding = DetectEncoding(file);
                using (var reader = new StreamReader(file, encoding)) {
                    projects.Add(Load(reader, file));
                }
            }

            var project = projects.First();
            project.name = "Merged Project";
            foreach (var p in projects) {
                if (p == project) {
                    continue;
                }
                var track = p.tracks[0];
                var part = p.parts[0];
                track.TrackNo = project.tracks.Count;
                part.trackNo = track.TrackNo;
                project.tracks.Add(track);
                project.parts.Add(part);
            }
            project.AfterLoad();
            project.ValidateFull();
            if (undefinedFlags.Count > 0) {
                var flags = string.Join(", ", undefinedFlags.Distinct());
                var customEx = new MessageCustomizableException("Unable to import undefined flags within the project", $"<translate:errors.failed.importustflags>\n{flags}", new FileLoadException(), false);
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(customEx));
            }
            return project;
        }

        public static Encoding DetectEncoding(string file) {
            using (var reader = new StreamReader(file, ShiftJIS)) {
                for (var i = 0; i < 10; i++) {
                    var line = reader.ReadLine();
                    if (line == null) {
                        break;
                    }
                    line = line.Trim();
                    if (line.StartsWith("Charset=")) {
                        return Encoding.GetEncoding(line.Replace("Charset=", ""));
                    }
                }
            }
            return ShiftJIS;
        }

        public static UProject Load(StreamReader reader, string file) {
            var project = new UProject() { FilePath = file, Saved = false };
            Ustx.AddDefaultExpressions(project);

            project.tracks.Clear();
            project.tracks.Add(new UTrack(project) {
                TrackNo = 0,
            });
            var part = new UVoicePart() {
                trackNo = 0,
                position = 0,
                name = Path.GetFileNameWithoutExtension(file),
            };
            project.parts.Add(part);

            var blocks = Ini.ReadBlocks(reader, file, @"\[#\w+\]");
            ParsePart(project, part, blocks);
            part.Duration = part.notes.LastOrDefault()?.End ?? 0 + project.resolution;

            return project;
        }

        private static void ParsePart(UProject project, UVoicePart part, List<IniBlock> blocks) {
            var lastNotePos = 0;
            var lastNoteEnd = 0;
            var settingsBlock = blocks.FirstOrDefault(b => b.header == "[#SETTING]");
            if (settingsBlock != null) {
                ParseSetting(project, settingsBlock.lines);
            }
            bool shouldFixTempo = project.tempos[0].bpm <= 0 || project.tempos[0].bpm > 1000; // Need to fix tempo=500k error or not.
            foreach (var block in blocks) {
                var header = block.header;
                try {
                    switch (header) {
                        case "[#VERSION]":
                            break;
                        case "[#SETTING]":
                            // Already processed
                            break;
                        case "[#TRACKEND]":
                            break;
                        default:
                            if (int.TryParse(header.Substring(2, header.Length - 3), out var noteIndex)) {
                                var note = project.CreateNote();
                                ParseNote(project, note, lastNotePos, lastNoteEnd, block.lines, out var noteTempo);
                                lastNotePos = note.position;
                                lastNoteEnd = note.End;
                                if (note.lyric.ToLowerInvariant() != "r") {
                                    part.notes.Add(note);
                                }
                                if (noteTempo != null) {
                                    if (shouldFixTempo) {
                                        project.tempos[0].bpm = noteTempo.Value;
                                        shouldFixTempo = false;
                                    } else {
                                        project.tempos.Add(new UTempo(note.position, noteTempo.Value));
                                    }
                                }
                            } else {
                                throw new FileFormatException($"Unexpected header\n{block.header}");
                            }
                            break;
                    }
                } catch (Exception e) when (!(e is FileFormatException)) {
                    throw new FileFormatException($"Failed to parse block\n{block.header}", e);
                }
            }
            SnapPitchPoints(part);
        }

        private static void SnapPitchPoints(UVoicePart part) {
            UNote lastNote = null;
            foreach (var note in part.notes) {
                if (lastNote == null || note.position > lastNote.End) {
                    note.pitch.snapFirst = false;
                }
                lastNote = note;
            }
        }

        private static void ParseSetting(UProject project, List<IniLine> lines) {
            const string format = "<param>=<value>";
            foreach (var iniLine in lines) {
                var line = iniLine.line;
                var parts = line.Split('=', 2);
                if (parts.Length != 2) {
                    throw new FileFormatException($"Line does not match format {format}.\n{iniLine}");
                }
                var param = parts[0].Trim();
                switch (param) {
                    case "Tempo":
                        if (ParseFloat(parts[1], out var temp)) {
                            project.tempos[0].bpm = temp;
                        }
                        break;
                    case "ProjectName":
                        project.name = parts[1].Trim();
                        break;
                    case "VoiceDir":
                        var singerpath = parts[1].Trim();
                        var singer = SingerManager.Inst.GetSinger(singerpath);
                        if (singer == null) {
                            singer = USinger.CreateMissing(Path.GetFileName(singerpath.Replace("%DATA%", "").Replace("%VOICE%", "")));
                        }
                        project.tracks[0].Singer = singer;
                        break;
                    case "Tool2":
                        tool2 = parts[1].Trim();
                        break;
                    case "Flags":
                        var parser = new UstFlagParser();
                        var track = project.tracks[0];
                        foreach (var flag in parser.Parse(parts[1].Trim())) {
                            var descriptor = project.expressions.Values.FirstOrDefault(exp => exp.flag == flag.Key)?.Clone();
                            if (descriptor == null) {
                                undefinedFlags.Add(flag.Key);
                            } else {
                                descriptor.CustomDefaultValue = flag.Value;
                                track.TrackExpressions.Add(descriptor);
                            }
                        }
                        break;
                }
            }
        }

        private static void ParseNote(UProject project, UNote note, int lastNotePos, int lastNoteEnd, List<IniLine> iniLines, out float? noteTempo) {
            var ustNote = new UstNote {
                lyric = note.lyric,
                position = note.position,
                duration = note.duration,
                noteNum = note.tone,
                pitch = note.pitch
            };
            ustNote.Parse(lastNotePos, lastNoteEnd, iniLines, out noteTempo);
            if (ustNote.lyric.StartsWith("!")) {
                note.lyric = $"[{ustNote.lyric.Substring(1)}]";
            } else {
                note.lyric = ustNote.lyric;
            }
            note.position = ustNote.position;
            note.duration = ustNote.duration;
            note.tone = ustNote.noteNum;
            if (ustNote.velocity != null) {
                SetExpression(project, note, Ustx.VEL, ustNote.velocity.Value);
            }
            if (ustNote.intensity != null) {
                SetExpression(project, note, Ustx.VOL, ustNote.intensity.Value);
            }
            if (ustNote.modulation != null) {
                SetExpression(project, note, Ustx.MOD, ustNote.modulation.Value);
            }
            if (ustNote.flags != null) {
                SetFlags(project, note, ustNote.flags);
            }
            if (ustNote.pitch != null) {
                note.pitch = ustNote.pitch;
            }
            if (ustNote.vibrato != null) {
                note.vibrato = ustNote.vibrato;
            }
        }

        private static void SetFlags(UProject project, UNote note, string flags) {
            var parser = new UstFlagParser();
            var list = parser.Parse(flags);
            list.ForEach((flag) => {
                if (flag.Key == "t") {
                    switch (tool2) {
                        case { } when tool2.StartsWith("resampler"):
                        case { } when tool2.StartsWith("doppeltler"):
                        case { } when tool2.StartsWith("f2resamp"):
                        case { } when tool2.StartsWith("moresampler"):
                            note.tuning = flag.Value;
                            break;
                        default: // fresamp11-14, model4, TIPS, tn_fnds, bkh01
                            note.tuning = flag.Value * 10;
                            break;
                    }
                    return;
                }
                var abbr = FindAbbrFromFlagKey(project.expressions, flag);
                if (string.IsNullOrEmpty(abbr)) {
                    undefinedFlags.Add(flag.Key);
                } else {
                    SetExpression(project, note, abbr, flag.Value);
                }
            });
        }

        private static string FindAbbrFromFlagKey(Dictionary<string, UExpressionDescriptor> expressions, UstFlag flag) {
            var exp = expressions.FirstOrDefault(exp => exp.Value.flag == flag.Key);
            return exp.Value != null ? exp.Value.abbr : String.Empty;
        }

        private static void SetExpression(UProject project, UNote note, string abbr, float value) {
            var track = project.tracks.First();
            if (track.TryGetExpDescriptor(project, abbr, out var descriptor) && descriptor.CustomDefaultValue != value) {
                note.SetExpression(project, track, abbr, new float?[] { value });
            }
        }

        static bool ParseFloat(string s, out float value) {
            if (string.IsNullOrEmpty(s)) {
                value = 0;
                return true;
            }
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        public static void SavePart(UProject project, UVoicePart part, string filePath) {
            var track = project.tracks[part.trackNo];
            var ustNotes = NotesToUstNotes(project, track, part, part.notes);
            using (var writer = new StreamWriter(filePath, false, ShiftJIS)) {
                WriteHeader(project, part, writer);
                for (var i = 0; i < ustNotes.Count; i++) {
                    writer.WriteLine($"[#{i:D4}]");
                    ustNotes[i].Write(writer);
                }
                WriteFooter(writer);
            }
        }

        static List<UstNote> NotesToUstNotes(UProject project, UTrack track, UVoicePart part, IEnumerable<UNote> notes) {
            var ustNotes = new List<UstNote>();
            var position = 0;
            foreach (var note in notes) {
                if (note.position < position) {
                    continue;
                }
                if (note.position > position) {
                    ustNotes.Add(new UstNote() {
                        position = position,
                        duration = note.position - position,
                        lyric = "R",
                        noteNum = 60,
                    });
                }
                ustNotes.Add(new UstNote(project, track, part, note));
                position = note.End;
            }
            // Insert tempo changes.
            int tempoIndex = 1;
            for (int i = 0; i < ustNotes.Count; ++i) {
                var ustNote = ustNotes[i];
                if (tempoIndex >= project.tempos.Count) {
                    break;
                }
                int pos = ustNote.position + part.position;
                int end = ustNote.position + ustNote.duration + part.position;
                var tempo = project.tempos[tempoIndex];
                if (pos <= tempo.position && tempo.position < end) {
                    if (pos == tempo.position || ustNote.lyric.ToLowerInvariant() != "r") {
                        // Does not break up the note even if the tempo change is in the middle.
                        ustNote.tempo = tempo.bpm;
                        tempoIndex++;
                    } else {
                        // Break up rest note to insert tempo.
                        ustNote.duration = tempo.position - pos;
                        var inserted = ustNote.Clone();
                        inserted.position = tempo.position - part.position;
                        inserted.duration = end - tempo.position;
                        inserted.tempo = tempo.bpm;
                        ustNotes.Insert(i + 1, inserted);
                        tempoIndex++;
                    }
                }
            }
            return ustNotes;
        }

        public static List<UNote> WritePlugin(UProject project, UVoicePart part, UNote first, UNote last, string filePath, string encoding = "shift_jis") {
            var prev = first.Prev;
            if (prev == null) {
                if (first.position > 0) {
                    prev = UNote.Create();
                    prev.duration = first.position;
                    prev.lyric = "R";
                    prev.tone = 60;
                }
            } else if (first.position > prev.End) {
                prev = UNote.Create();
                prev.duration = first.position - prev.End;
                prev.lyric = "R";
                prev.tone = 60;
            }
            var next = last.Next;
            if (next != null && next.position > last.End) {
                next = UNote.Create();
                next.duration = next.position - last.End;
                next.lyric = "R";
                next.tone = 60;
            }
            var sequence = new List<UNote>();
            var track = project.tracks[part.trackNo];
            using (var writer = new StreamWriter(filePath, false, Encoding.GetEncoding(encoding))) {
                WriteHeader(project, part, writer);
                var position = 0;
                if (prev != null) {
                    writer.WriteLine($"[#PREV]");
                    WriteNoteBody(project, track, part, prev, writer);
                    position = prev.End;
                }
                var note = first;
                while (note != last.Next) {
                    if (note.position < position) {
                        //Ignore current note if it is overlapped with previous note
                        note = note.Next;
                        continue;
                    }
                    if (note.position > position) {
                        //Insert R note if there is space between two notes
                        writer.WriteLine($"[#{sequence.Count:D4}]");
                        var spacer = UNote.Create();
                        spacer.position = position;
                        spacer.duration = note.position - position;
                        spacer.lyric = "R";
                        spacer.tone = 60;
                        sequence.Add(spacer);
                        WriteNoteBody(project, track, part, spacer, writer);
                    }
                    writer.WriteLine($"[#{sequence.Count:D4}]");
                    WriteNoteBody(project, track, part, note, writer, forPlugin: true);
                    position = note.End;
                    sequence.Add(note);
                    note = note.Next;
                }
                if (next != null) {
                    writer.WriteLine($"[#NEXT]");
                    WriteNoteBody(project, track, part, next, writer);
                }
            }
            return sequence;
        }

        static void WriteHeader(UProject project, UVoicePart part, StreamWriter writer) {
            writer.WriteLine("[#SETTING]");
            writer.WriteLine($"Tempo={project.timeAxis.GetBpmAtTick(part.position)}");
            writer.WriteLine("Tracks=1");
            if (project.Saved) {
                writer.WriteLine($"Project={project.FilePath.Replace(".ustx", ".ust")}");
            }
            var singer = project.tracks[part.trackNo].Singer;
            if (singer?.Id != null) {
                writer.WriteLine($"VoiceDir={singer.Location}");
            }
            writer.WriteLine($"CacheDir={PathManager.Inst.CachePath}");
            writer.WriteLine("Mode2=True");
        }

        static void WriteFooter(StreamWriter writer) {
            writer.WriteLine("[#TRACKEND]");
        }

        static void WriteNoteBody(UProject project, UTrack track, UVoicePart part, UNote note, StreamWriter writer, bool forPlugin = false) {
            var ustNote = new UstNote(project, track, part, note);
            ustNote.Write(writer, forPlugin);
        }

        public static (List<UNote>, List<UNote>) ParsePlugin(
            UProject project, UVoicePart part, UNote first, UNote last,
            List<UNote> sequence, string diffFile, string encoding = "shift_jis") {
            var toRemove = new List<UNote>();
            var toAdd = new List<UNote>();
            using (var reader = new StreamReader(diffFile, Encoding.GetEncoding(encoding))) {
                var blocks = Ini.ReadBlocks(reader, diffFile, @"\[#\w+\]");
                int index = 0;
                foreach (var block in blocks) {
                    var header = block.header;
                    switch (header) {
                        case "[#VERSION]":
                        case "[#SETTING]":
                        case "[#TRACKEND]":
                        case "[#PREV]":
                        case "[#NEXT]":
                            break;
                        case "[#INSERT]":
                            if (index <= sequence.Count) {
                                var newNote = project.CreateNote();
                                ParseNote(project, newNote, 0, 0, block.lines, out var _);
                                newNote.AfterLoad(project, project.tracks[part.trackNo], part);
                                sequence.Insert(index, newNote);
                                toAdd.Add(newNote);
                                index++;
                            }
                            break;
                        case "[#DELETE]":
                            if (index < sequence.Count) {
                                toRemove.Add(sequence[index]);
                                sequence.RemoveAt(index);
                            }
                            break;
                        default:
                            if (index < sequence.Count) {
                                toRemove.Add(sequence[index]);
                                var newNote = sequence[index].Clone();
                                ParseNote(project, newNote, 0, 0, block.lines, out var _);
                                newNote.AfterLoad(project, project.tracks[part.trackNo], part);
                                sequence[index] = newNote;
                                toAdd.Add(newNote);
                                index++;
                            }
                            break;
                    }
                }
            }
            int position = first.position;
            foreach (var note in sequence) {
                note.position = position;
                position += note.duration;
            }
            var rests = part.notes
                .Where(n => n.lyric.ToLowerInvariant() == "r")
                .Select(n => n.position)
                .ToHashSet();
            toAdd = toAdd
                .Where(n => n.duration > 0)
                .Where(n => n.lyric.ToLowerInvariant() != "r" || rests.Contains(n.position))
                .ToList();
            toRemove = toRemove
                .Where(n => part.notes.Contains(n))
                .ToList();
            return (toRemove, toAdd);
        }
        
        public static void WriteForSetParam(UProject project, string filePath, List<UOto> otos) {
            using (var writer = new StreamWriter(filePath, false, Encoding.GetEncoding("shift_jis"))) {
                writer.WriteLine("[#SETTING]");
                writer.WriteLine($"Tempo=120");
                writer.WriteLine("Tracks=1");
                if (project.Saved) {
                    writer.WriteLine($"Project={project.FilePath.Replace(".ustx", ".ust")}");
                }
                writer.WriteLine($"VoiceDir={Path.GetDirectoryName(otos[0].File)}");
                writer.WriteLine($"CacheDir={PathManager.Inst.CachePath}");
                writer.WriteLine("Mode2=True");

                for (int i = 0; i < otos.Count; i++) {
                    UOto oto = otos[i];
                    writer.WriteLine($"[#{i:D4}]");
                    writer.WriteLine($"Length=480");
                    writer.WriteLine($"Lyric={oto.Alias}");
                    writer.WriteLine($"NoteNum=60");
                }
            }
        }
    }
}
