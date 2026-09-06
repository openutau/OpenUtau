using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Core.Format {
    public static class SVP {
        public static UProject Load(string svpFilePath) => SVPImporter.Load(svpFilePath, isSv2: false);
    }

    public static class SVP2 {
        public static UProject Load(string svpFilePath) => SVPImporter.Load(svpFilePath, isSv2: true);
    }

    internal static class SVPImporter {
        public static UProject Load(string svpFilePath, bool isSv2) {
            try {
                var json = File.ReadAllText(svpFilePath).TrimEnd('\0');
                var svpProject = Json.Deserialize<SVPProject>(json);
                if (svpProject == null) {
                    throw new FileFormatException($"Failed to parse {(isSv2 ? "SV2" : "SVP")} file");
                }
                return ConvertToUstx(svpProject, svpFilePath, isSv2);
            } catch (Exception ex) {
                throw new FileFormatException($"Error loading {(isSv2 ? "SV2" : "SVP")} file: {ex.Message}", ex);
            }
        }

        private static double ParseVocalModeParam(JsonNode token) {
            if (token == null) return 0;
            if (token.GetValueKind() == JsonValueKind.Object) return token["timbre"]?.GetValue<double>() ?? 0;
            if (token.GetValueKind() == JsonValueKind.Number) return token.GetValue<double>();
            return 0;
        }

        private static UProject ConvertToUstx(SVPProject svpProject, string svpFilePath, bool isSv2) {
            var project = new UProject();
            Ustx.AddDefaultExpressions(project);

            project.RegisterExpression(new UExpressionDescriptor("tension (curve)", Ustx.TENC, -100, 100, 0) { type = UExpressionType.Curve });
            project.RegisterExpression(new UExpressionDescriptor("breathiness (curve)", Ustx.BREC, -100, 100, 0) { type = UExpressionType.Curve });
            project.RegisterExpression(new UExpressionDescriptor("gender (curve)", Ustx.GENC, -100, 100, 0) { type = UExpressionType.Curve });
            project.RegisterExpression(new UExpressionDescriptor("voicing (curve)", Ustx.VOIC, -100, 100, 0) { type = UExpressionType.Curve });
            project.RegisterExpression(new UExpressionDescriptor("tone shift (curve)", Ustx.SHFC, -100, 100, 0) { type = UExpressionType.Curve });
            if (isSv2) {
                project.RegisterExpression(new UExpressionDescriptor("mouth opening (curve)", "opec", -100, 100, 0) { type = UExpressionType.Curve });
            }

            double blicksPerTick = 705600000.0 / project.resolution;

            project.timeSignatures = svpProject.time?.meter?
                .Select(m => new UTimeSignature(Math.Max(0, m.index), Math.Max(1, m.numerator), Math.Max(1, m.denominator)))
                .OrderBy(m => m.barPosition)
                .ToList() ?? new List<UTimeSignature> { new UTimeSignature(0, 4, 4) };

            project.tempos = svpProject.time?.tempo?
                .Select(t => new UTempo(Math.Max(0, (int)Math.Round(t.position / blicksPerTick)), Math.Max(10.0, t.bpm)))
                .OrderBy(t => t.position)
                .ToList() ?? new List<UTempo> { new UTempo(0, 120) };

            if (!project.tempos.Any(t => t.position == 0)) {
                project.tempos.Insert(0, new UTempo(0, project.tempos.Count > 0 ? project.tempos[0].bpm : 120.0));
            }

            var timeAxis = new TimeAxis();
            timeAxis.BuildSegments(project);

            var libraryGroups = new Dictionary<string, SVPGroup>();
            void RegisterGroups(List<SVPGroup> list) {
                if (list == null) return;
                foreach (var grp in list) {
                    if (!string.IsNullOrEmpty(grp.uuid)) libraryGroups[grp.uuid] = grp;
                }
            }
            RegisterGroups(svpProject.library);
            RegisterGroups(svpProject.groups);

            foreach (var svpTrack in svpProject.tracks ?? new List<SVPTrack>()) {
                string singerName = "";
                bool trackHasContent = false;
                bool trackHasNotes = false;

                var track = new UTrack(project) {
                    TrackNo = project.tracks.Count,
                    TrackName = svpTrack.name ?? "Unnamed Track",
                    Singer = USinger.CreateMissing("")
                };

                var allRefs = new List<SVPMRef>();
                if (svpTrack.mainRef != null) allRefs.Add(svpTrack.mainRef);
                if (isSv2 && svpTrack.mainGroupSV2 != null && !allRefs.Contains(svpTrack.mainGroupSV2)) allRefs.Add(svpTrack.mainGroupSV2);
                if (svpTrack.groups != null) {
                    foreach (var r in svpTrack.groups) {
                        if (r != null && !allRefs.Contains(r)) allRefs.Add(r);
                    }
                }

                foreach (var reference in allRefs) {
                    if (reference == null) continue;

                    if (reference.isInstrumental) {
                        if (reference.audio == null || string.IsNullOrWhiteSpace(reference.audio.filename)) continue;

                        string audioFile = reference.audio.filename.Replace('\\', '/');
                        if (!Path.IsPathRooted(audioFile)) {
                            string dir = Path.GetDirectoryName(svpFilePath);
                            audioFile = string.IsNullOrEmpty(dir) ? Path.GetFullPath(audioFile) : Path.GetFullPath(Path.Combine(dir, audioFile));
                        }

                        if (!File.Exists(audioFile)) continue;

                        long startBlickAudio = reference.blickAbsoluteBegin > 0 ? reference.blickAbsoluteBegin : reference.blickOffset;
                        int startTick = Math.Max(0, (int)Math.Round(startBlickAudio / blicksPerTick));
                        double startMs = timeAxis.TickPosToMsPos(startTick);
                        int endTick = timeAxis.MsPosToTickPos(startMs + (reference.audio.duration * 1000.0));
                        int durTick = Math.Max(project.resolution * 4, endTick - startTick);

                        var wavePart = new UWavePart {
                            name = Path.GetFileName(audioFile),
                            FilePath = audioFile,
                            position = startTick,
                            Duration = durTick,
                            trackNo = track.TrackNo
                        };

                        project.parts.Add(wavePart);
                        trackHasContent = true;
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(singerName) && reference.database != null && !string.IsNullOrWhiteSpace(reference.database.name)) {
                        singerName = reference.database.name;
                    }

                    SVPGroup group = null;
                    if (!string.IsNullOrEmpty(reference.groupID) && libraryGroups.TryGetValue(reference.groupID, out var libGrp)) group = libGrp;
                    else if (svpTrack.mainGroup != null && (reference.groupID == svpTrack.mainGroup.uuid || string.IsNullOrEmpty(reference.groupID))) group = svpTrack.mainGroup;

                    if (group == null) continue;

                    long startBlick = reference.blickOffset;
                    int partPosTick = Math.Max(0, (int)Math.Round(startBlick / blicksPerTick));

                    var part = new UVoicePart {
                        name = group.name ?? svpTrack.name ?? "Part",
                        position = partPosTick,
                        trackNo = track.TrackNo
                    };

                    var grpPitch = new List<(double x, double y)>(); var trkPitch = new List<(double x, double y)>();
                    var aiPitchPoints = new List<(double x, double y)>();
                    var aiAbsolutePitchPoints = new List<(double x, double y)>();
                    var ouEffectivePitchPoints = new List<(double x, double y)>();
                    var manualNoteRanges = new List<(int start, int end)>();

                    var grpDyn = new List<(double x, double y)>(); var trkDyn = new List<(double x, double y)>();
                    var grpTen = new List<(double x, double y)>(); var trkTen = new List<(double x, double y)>();
                    var grpBre = new List<(double x, double y)>(); var trkBre = new List<(double x, double y)>();
                    var grpGen = new List<(double x, double y)>(); var trkGen = new List<(double x, double y)>();
                    var grpVoic = new List<(double x, double y)>(); var trkVoic = new List<(double x, double y)>();
                    var grpShft = new List<(double x, double y)>(); var trkShft = new List<(double x, double y)>();
                    var grpOpe = new List<(double x, double y)>(); var trkOpe = new List<(double x, double y)>();

                    var grpModes = new Dictionary<string, List<(double x, double y)>>();
                    var trkModes = new Dictionary<string, List<(double x, double y)>>();
                    var baseModes = new Dictionary<string, double>();
                    var phonemeQueue = new Queue<string>();

                    int maxNoteEnd = 0;
                    int pitchOffset = reference.pitchOffset;

                    if (group.notes != null) {
                        foreach (var svpNote in group.notes) {
                            if (!string.IsNullOrEmpty(svpNote.musicalType) && svpNote.musicalType != "singing" && svpNote.musicalType != "rap") continue;

                            int tickOn = Math.Max(0, (int)Math.Round(svpNote.onset / blicksPerTick));
                            int tickOff = (int)Math.Round((svpNote.onset + svpNote.duration) / blicksPerTick);
                            if (tickOff <= tickOn) continue;
                            int duration = tickOff - tickOn;

                            if (tickOff > maxNoteEnd) maxNoteEnd = tickOff;

                            var note = project.CreateNote(svpNote.pitch + pitchOffset, tickOn, duration);
                            note.lyric = string.IsNullOrEmpty(svpNote.lyrics) ? "a" : svpNote.lyrics;

                            if (svpNote.instantMode.HasValue && !svpNote.instantMode.Value) {
                                manualNoteRanges.Add((tickOn, tickOff));
                            }

                            if (note.lyric == "-") note.lyric = "+~";
                            else if (note.lyric.StartsWith(".")) note.lyric = $"[{note.lyric.Substring(1)}]";

                            if (!string.IsNullOrWhiteSpace(svpNote.phonemes)) {
                                phonemeQueue.Clear();
                                foreach (var syl in svpNote.phonemes.Split('+').Select(s => s.Trim())) {
                                    if (!string.IsNullOrWhiteSpace(syl)) phonemeQueue.Enqueue(syl);
                                }
                            } else if (note.lyric != "+" && note.lyric != "+~") {
                                phonemeQueue.Clear();
                            }

                            if (phonemeQueue.Count > 0) {
                                string currentSyl = phonemeQueue.Dequeue();
                                note.lyric = (note.lyric.StartsWith("[") && note.lyric.EndsWith("]")) ? $"[{currentSyl}]" : $"{note.lyric} [{currentSyl}]";
                            }

                            note.pitch.data.Clear();
                            note.vibrato.length = 0;

                            var activeAttrs = svpNote.attributes ?? svpNote.systemAttributes;
                            var voice = reference.voice;

                            double? dF0Vbr = activeAttrs?.dF0Vbr ?? voice?.dF0Vbr;
                            double? fF0Vbr = activeAttrs?.fF0Vbr ?? voice?.fF0Vbr;
                            double? tF0VbrStart = activeAttrs?.tF0VbrStart ?? voice?.tF0VbrStart;

                            if ((dF0Vbr ?? 0) > 0) {
                                note.vibrato.depth = (float)(dF0Vbr.Value * 100.0);
                                note.vibrato.period = (float)(1000.0 / (fF0Vbr ?? 5.5));
                                double noteMs = timeAxis.TickPosToMsPos(partPosTick + tickOff) - timeAxis.TickPosToMsPos(partPosTick + tickOn);
                                double noteSec = Math.Max(0.001, noteMs / 1000.0);
                                double vbrStart = tF0VbrStart ?? 0.25;
                                note.vibrato.length = (float)Math.Max(0, Math.Min(100.0, ((noteSec - vbrStart) / noteSec) * 100.0));
                            }

                            float msOffset = (float)((activeAttrs?.tF0Offset ?? voice?.tF0Offset ?? 0.0) * 1000.0);
                            float msLeft = (float)((activeAttrs?.tF0Left ?? voice?.tF0Left ?? 0.04) * 1000.0);
                            float msRight = (float)((activeAttrs?.tF0Right ?? voice?.tF0Right ?? 0.04) * 1000.0);
                            float yLeft = (float)((activeAttrs?.dF0Left ?? voice?.dF0Left ?? 0.0) * 10.0);
                            float yRight = (float)((activeAttrs?.dF0Right ?? voice?.dF0Right ?? 0.0) * 10.0);

                            note.pitch.AddPoint(new PitchPoint(-msLeft + msOffset, yLeft));
                            note.pitch.AddPoint(new PitchPoint(msRight + msOffset, yRight));

                            if (isSv2) {
                                double absoluteMsOnset = timeAxis.TickPosToMsPos(partPosTick + tickOn);
                                double absMinus = Math.Max(0, timeAxis.MsPosToTickPos(absoluteMsOnset - 10));
                                double absPlus = timeAxis.MsPosToTickPos(absoluteMsOnset + 40);

                                double previousPitch = svpNote.pitch + pitchOffset;
                                if (ouEffectivePitchPoints.Count > 0) previousPitch = ouEffectivePitchPoints.Last().y;

                                ouEffectivePitchPoints.Add((absMinus - partPosTick, previousPitch));
                                ouEffectivePitchPoints.Add((absPlus - partPosTick, svpNote.pitch + pitchOffset));
                                ouEffectivePitchPoints.Add((tickOff, svpNote.pitch + pitchOffset));
                            }

                            if (activeAttrs?.vocalModeParams != null) {
                                foreach (var kvp in activeAttrs.vocalModeParams) {
                                    string modeName = kvp.Key;
                                    if (!grpModes.ContainsKey(modeName)) grpModes[modeName] = new List<(double x, double y)>();
                                    double staticValue = ParseVocalModeParam(kvp.Value);
                                    grpModes[modeName].Add((tickOn, staticValue));
                                    grpModes[modeName].Add((tickOff, staticValue));
                                }
                            }

                            part.notes.Add(note);
                        }
                    }

                    if (isSv2 && group.pitchControls != null) {
                        foreach (var pt in group.pitchControls) {
                            aiAbsolutePitchPoints.Add((pt.pos / blicksPerTick, pt.pitch));
                        }
                    }

                    if (maxNoteEnd <= 0) maxNoteEnd = project.resolution;
                    long endBlick = startBlick + (long)(maxNoteEnd * blicksPerTick);
                    part.Duration = maxNoteEnd;

                    // Parse Group Parameters
                    ParseFlatCurve(group.parameters?.pitchDelta?.points, grpPitch, 0, blicksPerTick, 1f);
                    ParseFlatCurve(group.parameters?.loudness?.points, grpDyn, 0, blicksPerTick, 10f);
                    ParseFlatCurve(group.parameters?.tension?.points, grpTen, 0, blicksPerTick, 100f);
                    ParseFlatCurve(group.parameters?.breathiness?.points, grpBre, 0, blicksPerTick, 100f);
                    ParseFlatCurve(group.parameters?.gender?.points, grpGen, 0, blicksPerTick, 100f);
                    ParseFlatCurve(group.parameters?.voicing?.points, grpVoic, 0, blicksPerTick, 100f);
                    ParseFlatCurve(group.parameters?.toneShift?.points, grpShft, 0, blicksPerTick, 100f);
                    if (isSv2) ParseFlatCurve(group.parameters?.mouthOpening?.points, grpOpe, 0, blicksPerTick, 100f);

                    if (group.vocalModes != null) {
                        foreach (var kvp in group.vocalModes) {
                            string modeName = kvp.Key;
                            if (!grpModes.ContainsKey(modeName)) grpModes[modeName] = new List<(double x, double y)>();
                            if (kvp.Value.GetValueKind() == JsonValueKind.Object) {
                                ParseFlatCurve(Json.Deserialize<SVPCurve>(kvp.Value)?.points, grpModes[modeName], 0, blicksPerTick, 1f);
                            } else if (kvp.Value.GetValueKind() == JsonValueKind.Number) {
                                double val = kvp.Value.GetValue<double>();
                                grpModes[modeName].Add((0, val));
                                grpModes[modeName].Add((part.Duration, val));
                            }
                        }
                    }

                    // Parse Track Parameters
                    ParseFlatCurve(svpTrack.parameters?.pitchDelta?.points, trkPitch, -startBlick, blicksPerTick, 1f, startBlick, endBlick);
                    ParseFlatCurve(svpTrack.parameters?.loudness?.points, trkDyn, -startBlick, blicksPerTick, 10f, startBlick, endBlick);
                    ParseFlatCurve(svpTrack.parameters?.tension?.points, trkTen, -startBlick, blicksPerTick, 100f, startBlick, endBlick);
                    ParseFlatCurve(svpTrack.parameters?.breathiness?.points, trkBre, -startBlick, blicksPerTick, 100f, startBlick, endBlick);
                    ParseFlatCurve(svpTrack.parameters?.gender?.points, trkGen, -startBlick, blicksPerTick, 100f, startBlick, endBlick);
                    ParseFlatCurve(svpTrack.parameters?.voicing?.points, trkVoic, -startBlick, blicksPerTick, 100f, startBlick, endBlick);
                    ParseFlatCurve(svpTrack.parameters?.toneShift?.points, trkShft, -startBlick, blicksPerTick, 100f, startBlick, endBlick);
                    if (isSv2) ParseFlatCurve(svpTrack.parameters?.mouthOpening?.points, trkOpe, -startBlick, blicksPerTick, 100f, startBlick, endBlick);

                    if (reference.systemPitchDelta != null) {
                        ParseFlatCurve(reference.systemPitchDelta.points, aiPitchPoints, 0, blicksPerTick, 1f);
                    }

                    double baseTen = reference.voice?.tension ?? reference.voice?.paramTension ?? 0;
                    double baseBre = reference.voice?.breathiness ?? reference.voice?.paramBreathiness ?? 0;
                    double baseGen = reference.voice?.gender ?? reference.voice?.paramGender ?? 0;
                    double baseVoic = reference.voice?.voicing ?? reference.voice?.paramVoicing ?? 0;
                    double baseShft = reference.voice?.toneShift ?? reference.voice?.paramToneShift ?? 0;
                    double baseOpe = isSv2 ? (reference.voice?.mouthOpening ?? 0) : 0;

                    if (reference.voice?.vocalModeParams != null) {
                        foreach (var kvp in reference.voice.vocalModeParams) {
                            baseModes[kvp.Key] = ParseVocalModeParam(kvp.Value);
                        }
                    }

                    FinalizeMergedPitch(project, part, Ustx.PITD, grpPitch, trkPitch, aiPitchPoints, aiAbsolutePitchPoints, ouEffectivePitchPoints, manualNoteRanges);

                    CombineAndFinalizeCurve(project, part, Ustx.DYN, grpDyn, trkDyn, 0, 10f);
                    CombineAndFinalizeCurve(project, part, Ustx.TENC, grpTen, trkTen, baseTen, 100f);
                    CombineAndFinalizeCurve(project, part, Ustx.BREC, grpBre, trkBre, baseBre, 100f);
                    CombineAndFinalizeCurve(project, part, Ustx.GENC, grpGen, trkGen, baseGen, 100f);
                    CombineAndFinalizeCurve(project, part, Ustx.VOIC, grpVoic, trkVoic, baseVoic, 100f);
                    CombineAndFinalizeCurve(project, part, Ustx.SHFC, grpShft, trkShft, baseShft, 100f);
                    if (isSv2) CombineAndFinalizeCurve(project, part, "opec", grpOpe, trkOpe, baseOpe, 100f);

                    var allModeKeys = new HashSet<string>(grpModes.Keys.Concat(trkModes.Keys).Concat(baseModes.Keys));
                    foreach (var mode in allModeKeys) {
                        string abbr = mode.ToLowerInvariant();
                        if (!project.expressions.ContainsKey(abbr)) {
                            project.RegisterExpression(new UExpressionDescriptor(mode, abbr, -200, 200, 0) { type = UExpressionType.Curve });
                        }

                        var gPts = grpModes.ContainsKey(mode) ? grpModes[mode] : new List<(double x, double y)>();
                        var tPts = trkModes.ContainsKey(mode) ? trkModes[mode] : new List<(double x, double y)>();
                        double bVal = baseModes.ContainsKey(mode) ? baseModes[mode] : 0;

                        CombineAndFinalizeCurve(project, part, abbr, gPts, tPts, bVal, 1f);
                    }

                    if (part.notes.Count > 0) {
                        project.parts.Add(part);
                        trackHasContent = true;
                        trackHasNotes = true;
                    }
                }

                if (trackHasContent) {
                    string fallbackName = trackHasNotes ? "" : "Instrumental";
                    track.Singer = USinger.CreateMissing(string.IsNullOrWhiteSpace(singerName) ? fallbackName : singerName);
                    project.tracks.Add(track);
                }
            }

            if (project.tracks.Count == 0) {
                project.tracks.Add(new UTrack(project) { TrackNo = 0, Singer = USinger.CreateMissing("Unknown") });
            }

            project.ValidateFull();
            return project;
        }

        private static UCurve GetCurve(UProject uproject, UVoicePart upart, string abbr) {
            var curve = upart.curves.Find(c => c.abbr == abbr);
            if (curve == null && uproject.expressions.TryGetValue(abbr, out var desc)) {
                curve = new UCurve(desc);
                upart.curves.Add(curve);
            }
            return curve;
        }

        private static void ParseFlatCurve(List<double> points, List<(double x, double y)> outPoints, long offsetBlicks, double blicksPerTick, float multiplier, long minBlick = long.MinValue, long maxBlick = long.MaxValue) {
            if (points == null || points.Count < 2) return;
            for (int i = 0; i < points.Count - 1; i += 2) {
                long blick = (long)Math.Round(points[i]);
                if (blick < minBlick || blick > maxBlick) continue;
                double tick = (blick + offsetBlicks) / blicksPerTick;
                outPoints.Add((tick, points[i + 1] * multiplier));
            }
        }

        private static double GetY(List<(double x, double y)> pts, double targetX, ref int cursor) {
            if (pts == null || pts.Count == 0) return 0;
            if (pts.Count == 1 || targetX <= pts[0].x) return pts[0].y;
            if (targetX >= pts[pts.Count - 1].x) return pts[pts.Count - 1].y;

            while (cursor < pts.Count - 2 && pts[cursor + 1].x <= targetX) cursor++;
            int i = cursor;

            double x0 = pts[i].x, y0 = pts[i].y, x1 = pts[i + 1].x, y1 = pts[i + 1].y;
            double dx = x1 - x0;
            if (dx <= 0) return y1;

            double secant = (y1 - y0) / dx;
            double m0 = i == 0 ? secant : (Math.Sign((x0 > pts[i - 1].x ? (y0 - pts[i - 1].y) / (x0 - pts[i - 1].x) : 0) * secant > 0 ? ((x0 > pts[i - 1].x ? (y0 - pts[i - 1].y) / (x0 - pts[i - 1].x) : 0) + secant) * 0.5 : 0) * Math.Min(Math.Abs(((x0 > pts[i - 1].x ? (y0 - pts[i - 1].y) / (x0 - pts[i - 1].x) : 0) + secant) * 0.5), Math.Min(3.0 * Math.Abs((x0 > pts[i - 1].x ? (y0 - pts[i - 1].y) / (x0 - pts[i - 1].x) : 0)), 3.0 * Math.Abs(secant))));
            double m1 = i + 2 >= pts.Count ? secant : (Math.Sign(secant * (pts[i + 2].x > x1 ? (pts[i + 2].y - y1) / (pts[i + 2].x - x1) : 0) > 0 ? (secant + (pts[i + 2].x > x1 ? (pts[i + 2].y - y1) / (pts[i + 2].x - x1) : 0)) * 0.5 : 0) * Math.Min(Math.Abs((secant + (pts[i + 2].x > x1 ? (pts[i + 2].y - y1) / (pts[i + 2].x - x1) : 0)) * 0.5), Math.Min(3.0 * Math.Abs(secant), 3.0 * Math.Abs((pts[i + 2].x > x1 ? (pts[i + 2].y - y1) / (pts[i + 2].x - x1) : 0)))));
            double t = (targetX - x0) / dx, t2 = t * t, t3 = t2 * t;
            return (2 * t3 - 3 * t2 + 1) * y0 + (t3 - 2 * t2 + t) * dx * m0 + (-2 * t3 + 3 * t2) * y1 + (t3 - t2) * dx * m1;
        }

        private static double GetYRaw(List<(double x, double y)> pts, double targetX, ref int cursor) {
            if (pts == null || pts.Count == 0) return 0;
            if (pts.Count == 1 || targetX <= pts[0].x) return pts[0].y;
            if (targetX >= pts[pts.Count - 1].x) return pts[pts.Count - 1].y;

            while (cursor < pts.Count - 2 && pts[cursor + 1].x <= targetX) cursor++;
            int i = cursor;

            double x0 = pts[i].x, y0 = pts[i].y, x1 = pts[i + 1].x, y1 = pts[i + 1].y;
            double dx = x1 - x0;
            if (dx <= 0) return y1;
            return y0 + ((targetX - x0) / dx) * (y1 - y0);
        }

        private static void CombineAndFinalizeCurve(UProject project, UVoicePart part, string abbr, List<(double x, double y)> groupPts, List<(double x, double y)> trackPts, double baseVal, float multiplier) {
            if (groupPts.Count == 0 && trackPts.Count == 0 && Math.Abs(baseVal) < 1e-6) return;
            var curve = GetCurve(project, part, abbr);
            if (curve == null) return;

            var sortedGrp = groupPts.OrderBy(p => p.x).ToList();
            var sortedTrk = trackPts.OrderBy(p => p.x).ToList();

            int startTick = 0;
            int endTick = Math.Max(project.resolution, part.Duration);
            int min = (int)(curve.descriptor?.min ?? -1200);
            int max = (int)(curve.descriptor?.max ?? 1200);

            int cGrp = 0, cTrk = 0;
            const int sampleStep = 5;

            for (int x = startTick; x <= endTick; x += sampleStep) {
                double yGrp = sortedGrp.Count > 0 ? GetY(sortedGrp, x, ref cGrp) : 0;
                double yTrk = sortedTrk.Count > 0 ? GetY(sortedTrk, x, ref cTrk) : 0;
                double finalY = yGrp + yTrk + (baseVal * multiplier);

                int val = Math.Max(min, Math.Min(max, (int)Math.Round(finalY)));
                if (curve.xs.Count == 0 || x > curve.xs.Last()) {
                    curve.xs.Add(x);
                    curve.ys.Add(val);
                }
            }
        }

        private static void FinalizeMergedPitch(UProject project, UVoicePart part, string abbr,
            List<(double x, double y)> grpPt, List<(double x, double y)> trkPt,
            List<(double x, double y)> aiPt, List<(double x, double y)> aiAbsolutePt, List<(double x, double y)> ouEffectivePt,
            List<(int start, int end)> manualNoteRanges = null) {

            if (grpPt.Count == 0 && trkPt.Count == 0 && aiPt.Count == 0 && aiAbsolutePt.Count == 0) return;
            var curve = GetCurve(project, part, abbr);
            if (curve == null) return;

            var sortedGrp = grpPt.OrderBy(p => p.x).ToList();
            var sortedTrk = trkPt.OrderBy(p => p.x).ToList();
            var sortedAi = aiPt.OrderBy(p => p.x).ToList();
            var sortedAiAbs = aiAbsolutePt.OrderBy(p => p.x).ToList();
            var sortedOuEff = ouEffectivePt.OrderBy(p => p.x).ToList();

            int startTick = 0;
            int endTick = Math.Max(project.resolution, part.Duration);
            int minVal = (int)(curve.descriptor?.min ?? -1200);
            int maxVal = (int)(curve.descriptor?.max ?? 1200);

            int cGrp = 0, cTrk = 0, cAi = 0, cAiAbs = 0, cOuEff = 0;
            const int sampleStep = 5;

            for (int x = startTick; x <= endTick; x += sampleStep) {
                double yGrp = sortedGrp.Count > 0 ? GetY(sortedGrp, x, ref cGrp) : 0;
                double yTrk = sortedTrk.Count > 0 ? GetY(sortedTrk, x, ref cTrk) : 0;

                double yAiFinal = 0;
                if (sortedAiAbs.Count > 0) {
                    double macroPitch = sortedOuEff.Count > 0 ? GetYRaw(sortedOuEff, x, ref cOuEff) : 60;
                    yAiFinal = (GetY(sortedAiAbs, x, ref cAiAbs) - macroPitch) * 100.0;
                } else if (sortedAi.Count > 0) {
                    bool isManual = manualNoteRanges != null && manualNoteRanges.Any(r => x >= r.start && x <= r.end);
                    yAiFinal = isManual ? 0 : GetY(sortedAi, x, ref cAi);
                }

                int finalY = Math.Max(minVal, Math.Min(maxVal, (int)Math.Round(yGrp + yTrk + yAiFinal)));
                if (curve.xs.Count == 0 || x > curve.xs.Last()) {
                    curve.xs.Add(x);
                    curve.ys.Add(finalY);
                }
            }
        }

        private class SVPProject {
            public int version { get; set; }
            public SVPTime time { get; set; }
            public List<SVPGroup> library { get; set; }
            public List<SVPGroup> groups { get; set; }
            public List<SVPTrack> tracks { get; set; }
        }
        private class SVPTime { public List<SVPMeter> meter { get; set; } public List<SVPTempo> tempo { get; set; } }
        private class SVPMeter { public int index { get; set; } public int numerator { get; set; } public int denominator { get; set; } }
        private class SVPTempo { public long position { get; set; } public double bpm { get; set; } }
        private class SVPGroup {
            public string uuid { get; set; }
            public string name { get; set; }
            public List<SVPNote> notes { get; set; }
            public SVPParameters parameters { get; set; }
            public Dictionary<string, JsonNode> vocalModes { get; set; }
            public List<SVPPitchControl> pitchControls { get; set; }
        }
        private class SVPPitchControl { public long pos { get; set; } public double pitch { get; set; } }
        private class SVPDatabase { public string name { get; set; } }
        private class SVPParameters {
            public SVPCurve pitchDelta { get; set; }
            public SVPCurve loudness { get; set; }
            public SVPCurve tension { get; set; }
            public SVPCurve breathiness { get; set; }
            public SVPCurve gender { get; set; }
            public SVPCurve voicing { get; set; }
            public SVPCurve toneShift { get; set; }
            public SVPCurve mouthOpening { get; set; }
        }
        private class SVPCurve { public string mode { get; set; } public List<double> points { get; set; } }
        private class SVPTrack {
            public string name { get; set; }
            public SVPParameters parameters { get; set; }
            public SVPGroup mainGroup { get; set; }
            public SVPMRef mainRef { get; set; }
            public List<SVPMRef> groups { get; set; }
            public SVPMRef mainGroupSV2 { get; set; }
        }
        private class SVPMRef {
            public string groupID { get; set; }
            public long blickOffset { get; set; }
            public long blickAbsoluteBegin { get; set; }
            public int pitchOffset { get; set; }
            public SVPCurve systemPitchDelta { get; set; }
            public SVPDatabase database { get; set; }
            public bool isInstrumental { get; set; }
            public SVPAudio audio { get; set; }
            public SVPVoice voice { get; set; }
        }
        private class SVPVoice {
            public Dictionary<string, JsonNode> vocalModeParams { get; set; }
            public double? tF0Offset, tF0Left, tF0Right, dF0Left, dF0Right, dF0Vbr, fF0Vbr, tF0VbrStart;
            public double? tension, breathiness, gender, voicing, toneShift, mouthOpening;
            public double? paramTension, paramBreathiness, paramGender, paramVoicing, paramToneShift;
        }
        private class SVPAudio { public string filename { get; set; } public double duration { get; set; } }
        private class SVPNote {
            public string musicalType { get; set; }
            public long onset { get; set; }
            public long duration { get; set; }
            public string lyrics { get; set; }
            public string phonemes { get; set; }
            public int pitch { get; set; }
            public bool? instantMode { get; set; }
            public SVPAttributes attributes { get; set; }
            public SVPAttributes systemAttributes { get; set; }
        }
        private class SVPAttributes {
            public Dictionary<string, JsonNode> vocalModeParams { get; set; }
            public double? tF0Offset, tF0Left, tF0Right, dF0Left, dF0Right, dF0Vbr, fF0Vbr, tF0VbrStart;
            public double? tension, breathiness, gender, voicing, toneShift, mouthOpening;
        }
    }
}