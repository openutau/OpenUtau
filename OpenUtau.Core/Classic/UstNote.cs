using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using OpenUtau.Core.Format;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Classic {
    class UstNote {
        public string lyric;
        public int position;
        public int duration;
        public int noteNum;
        public double? tempo;
        public int? velocity;
        public int? intensity;
        public int? modulation;
        public int? decay;
        public string flags;
        public string filename;
        public string alias;
        public UPitch pitch;
        public UVibrato vibrato;

        public UstNote() { }

        public UstNote(UProject project, UTrack track, UVoicePart part, UNote note) {
            lyric = note.lyric;
            position = note.position;
            duration = note.duration;
            noteNum = note.tone;
            var phoneme = part.phonemes.FirstOrDefault(p => p.Parent == note);
            if (phoneme != null) {
                velocity = (int)phoneme.GetExpression(project, track, Ustx.VEL).Item1;
                intensity = (int)phoneme.GetExpression(project, track, Ustx.VOL).Item1;
                modulation = (int)phoneme.GetExpression(project, track, Ustx.MOD).Item1;
                flags = FlagsToString(phoneme.GetResamplerFlags(project, track));
                if (phoneme.oto != null && phoneme.oto.File != null) {
                    var relativePath = Path.GetRelativePath(track.Singer.Location, phoneme.oto.File);
                    filename = relativePath;
                    alias = phoneme.oto.Alias;
                }
            }
            pitch = note.pitch.Clone();
            vibrato = note.vibrato.Clone();
        }

        public UstNote Clone() {
            return new UstNote {
                lyric = lyric,
                position = position,
                duration = duration,
                noteNum = noteNum,
                tempo = tempo,
                velocity = velocity,
                intensity = intensity,
                modulation = modulation,
                decay = decay,
                flags = flags,
                filename = filename,
                alias = alias,
                pitch = pitch?.Clone(),
                vibrato = vibrato?.Clone(),
            };
        }

        public void Write(StreamWriter writer, bool forPlugin = false) {
            writer.WriteLine($"Length={duration}");
            writer.WriteLine($"Lyric={lyric}");
            writer.WriteLine($"NoteNum={noteNum}");
            writer.WriteLine("PreUtterance=");
            if (tempo != null) {
                writer.WriteLine($"Tempo={tempo.Value}");
            }
            if (velocity != null) {
                writer.WriteLine($"Velocity={velocity.Value}");
            }
            if (intensity != null) {
                writer.WriteLine($"Intensity={intensity.Value}");
            }
            if (modulation != null) {
                writer.WriteLine($"Modulation={modulation.Value}");
            }
            if (!string.IsNullOrEmpty(flags)) {
                writer.WriteLine($"Flags={flags}");
            }
            if (forPlugin) {
                if (!string.IsNullOrEmpty(filename)) {
                    writer.WriteLine($"@filename={filename}");
                }
                if (!string.IsNullOrEmpty(alias)) {
                    writer.WriteLine($"@alias={alias}");
                }
            }
            WritePitch(writer);
            WriteVibrato(writer);
        }

        void WritePitch(StreamWriter writer) {
            if (pitch == null) {
                return;
            }
            var points = pitch.data;
            if (points.Count >= 2) {
                writer.WriteLine($"PBS={points[0].X};{points[0].Y}");
                var pbw = new List<string>();
                var pby = new List<string>();
                var pbm = new List<string>();
                for (var i = 0; i < points.Count; ++i) {
                    switch (points[i].shape) {
                        case PitchPointShape.o:
                            pbm.Add("r");
                            break;
                        case PitchPointShape.l:
                            pbm.Add("s");
                            break;
                        case PitchPointShape.i:
                            pbm.Add("j");
                            break;
                        case PitchPointShape.io:
                            pbm.Add("");
                            break;
                    }
                }
                for (var i = 1; i < points.Count; ++i) {
                    var prev = points[i - 1];
                    var current = points[i];
                    pbw.Add((current.X - prev.X).ToString());
                    pby.Add(current.Y.ToString());
                }
                writer.WriteLine($"PBW={string.Join(",", pbw.ToArray())}");
                writer.WriteLine($"PBY={string.Join(",", pby.ToArray())}");
                writer.WriteLine($"PBM={string.Join(",", pbm.ToArray())}");
            }
        }

        void WriteVibrato(StreamWriter writer) {
            if ((vibrato?.length ?? 0) > 0) {
                writer.WriteLine($"VBR={vibrato.length},{vibrato.period},{vibrato.depth},{vibrato.@in},{vibrato.@out},{vibrato.shift},{vibrato.drift}");
            }
        }

        string FlagsToString(Tuple<string, int?, string>[] flags) {
            var builder = new StringBuilder();
            foreach (var flag in flags) {
                builder.Append(flag.Item1);
                if (flag.Item2.HasValue) {
                    builder.Append(flag.Item2.Value);
                }
            }
            return builder.ToString();
        }

        public void Parse(int lastNotePos, int lastNoteEnd, List<IniLine> iniLines, out float? noteTempo) {
            const string format = "<param>=<value>";
            noteTempo = null;
            string pbs = null, pbw = null, pby = null, pbm = null;
            int? delta = null;
            int? duration = null;
            int? length = null;
            foreach (var iniLine in iniLines) {
                var line = iniLine.line;
                var parts = line.Split('=', 2);
                if (parts.Length != 2) {
                    throw new FileFormatException($"Line does not match format {format}.\n{iniLine}");
                }
                var param = parts[0].Trim();
                var error = false;
                var isFloat = ParseFloat(parts[1], out var floatValue);
                switch (param) {
                    case "Length":
                        error |= !isFloat;
                        length = (int)floatValue;
                        break;
                    case "Delta":
                        error |= !isFloat;
                        delta = (int)floatValue;
                        break;
                    case "Duration":
                        error |= !isFloat;
                        duration = (int)floatValue;
                        break;
                    case "Lyric":
                        ParseLyric(parts[1]);
                        break;
                    case "NoteNum":
                        error |= !isFloat;
                        noteNum = (int)floatValue;
                        break;
                    case "Velocity":
                        error |= !isFloat;
                        velocity = (int)floatValue;
                        break;
                    case "Intensity":
                        error |= !isFloat;
                        intensity = (int)floatValue;
                        break;
                    case "Modulation":
                        error |= !isFloat;
                        modulation = (int)floatValue;
                        break;
                    case "VoiceOverlap":
                        error |= !isFloat;
                        //note.phonemes[0].overlap = floatValue;
                        break;
                    case "PreUtterance":
                        error |= !isFloat;
                        //note.phonemes[0].preutter = floatValue;
                        break;
                    case "Envelope":
                        ParseEnvelope(parts[1], iniLine);
                        break;
                    case "VBR":
                        ParseVibrato(parts[1], iniLine);
                        break;
                    case "PBS": pbs = parts[1]; break;
                    case "PBW": pbw = parts[1]; break;
                    case "PBY": pby = parts[1]; break;
                    case "PBM": pbm = parts[1]; break;
                    case "Tempo":
                        if (isFloat) {
                            noteTempo = floatValue;
                        }
                        break;
                    case "Flags":
                        flags = parts[1];
                        break;
                    default:
                        break;
                }
                if (error) {
                    throw new FileFormatException($"Invalid {param}\n${iniLine}");
                }
            }
            // UST Version < 2.0
            // | length       | length       |
            // | note1        | R            |
            // UST Version = 2.0
            // | length1      | length2      |
            // | dur1  |      | dur2         |
            // | note1 | R    | note2        |
            // | delta2       |
            if (delta != null && duration != null && length != null) {
                position = lastNotePos + delta.Value;
                this.duration = duration.Value;
            } else if (length != null) {
                position = lastNoteEnd;
                this.duration = length.Value;
            }
            ParsePitchBend(pbs, pbw, pby, pbm);
        }

        void ParseLyric(string ust) {
            if (ust.StartsWith("?")) {
                ust = ust.Substring(1);
            }
            lyric = ust;
        }

        void ParseEnvelope(string ust, IniLine ustLine) {
            // p1,p2,p3,v1,v2,v3,v4,%,p4,p5,v5 (0,5,35,0,100,100,0,%,0,0,100)
            try {
                var parts = ust.Split(new[] { ',' }).Select(s => float.TryParse(s, out var v) ? v : -1).ToArray();
                if (parts.Length < 7) {
                    return;
                }
                float p1 = parts[0], p2 = parts[1], p3 = parts[2], v1 = parts[3], v2 = parts[4], v3 = parts[5], v4 = parts[6];
                if (parts.Length == 11) {
                    float p4 = parts[8], p5 = parts[9], v5 = parts[10];
                }
                decay = 100 - (int)v3;
            } catch (Exception e) {
                throw new FileFormatException($"Invalid Envelope\n{ustLine}", e);
            }
        }

        void ParsePitchBend(string pbs, string pbw, string pby, string pbm) {
            var pitch = this.pitch != null ? this.pitch.Clone() : new UPitch() ;
            var points = pitch.data;

            // PBS
            if (!string.IsNullOrWhiteSpace(pbs)) {
                var parts = pbs.Contains(';') ? pbs.Split(';') : pbs.Split(',');
                float pbsX = parts.Length >= 1 && ParseFloat(parts[0], out pbsX) ? pbsX : 0;
                float pbsY = parts.Length >= 2 && ParseFloat(parts[1], out pbsY) ? pbsY : 0;
                if(points.Count > 0) {
                    points[0] = new PitchPoint(pbsX, pbsY);
                } else {
                    points.Add(new PitchPoint(pbsX, pbsY));
                }
            }
            if (points.Count == 0) {
                return;
            }
            // PBW, PBY
            var x = points.First().X;
            var w = new List<float>();
            var y = new List<float>();
            if (!string.IsNullOrWhiteSpace(pbw)) {
                w = pbw.Split(',').Select(s => ParseFloat(s, out var v) ? v : 0).ToList();
            }
            if (!string.IsNullOrWhiteSpace(pby)) {
                y = pby.Split(',').Select(s => ParseFloat(s, out var v) ? v : 0).ToList();
            }
            if (w.Count != 0 || y.Count != 0) {
                if (points.Count > 1 && points.Count - 1 == w.Count && y.Count == 0) { // replace w only
                    for (var i = 0; i < w.Count(); i++) {
                        x += w[i];
                        points[i + 1].X = x;
                    }
                } else if (points.Count > 1 && w.Count == 0 && points.Count - 1 == y.Count) { // replace y only
                    for (var i = 0; i < y.Count(); i++) {
                        points[i + 1].Y = y[i];
                    }
                } else {
                    while (w.Count > y.Count) {
                        y.Add(0);
                    }
                    for (var i = points.Count - 1; i > 0; i--) {
                        points.Remove(points[i]);
                    }
                    for (var i = 0; i < w.Count(); i++) {
                        x += w[i];
                        points.Add(new PitchPoint(x, y[i]));
                    }
                }
            }
            // PBM
            if (!string.IsNullOrWhiteSpace(pbm)) {
                var m = pbm.Split(new[] { ',' });
                for (var i = 0; i < m.Count() && i < points.Count; i++) {
                    switch (m[i]) {
                        case "r":
                            points[i].shape = PitchPointShape.o;
                            break;
                        case "s":
                            points[i].shape = PitchPointShape.l;
                            break;
                        case "j":
                            points[i].shape = PitchPointShape.i;
                            break;
                        default:
                            points[i].shape = PitchPointShape.io;
                            break;
                    }
                }
            }
            if (points.Count > 1) {
                this.pitch = pitch;
            }
        }

        void ParseVibrato(string ust, IniLine ustLine) {
            try {
                var vibrato = new UVibrato();
                var args = ust.Split(',').Select(s => float.TryParse(s, out var v) ? v : 0).ToArray();
                if (args.Length >= 1) {
                    vibrato.length = args[0];
                }
                if (args.Length >= 2) {
                    vibrato.period = args[1];
                }
                if (args.Length >= 3) {
                    vibrato.depth = args[2];
                }
                if (args.Length >= 4) {
                    vibrato.@in = args[3];
                }
                if (args.Length >= 5) {
                    vibrato.@out = args[4];
                }
                if (args.Length >= 6) {
                    vibrato.shift = args[5];
                }
                if (args.Length >= 7) {
                    vibrato.drift = args[6];
                }
                this.vibrato = vibrato;
            } catch {
                throw new FileFormatException($"Invalid VBR\n{ustLine}");
            }
        }

        static bool ParseFloat(string s, out float value) {
            if (string.IsNullOrEmpty(s)) {
                value = 0;
                return true;
            }
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }
}
