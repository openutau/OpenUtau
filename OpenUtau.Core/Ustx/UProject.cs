using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Core.Util;
using SharpCompress;
using YamlDotNet.Serialization;

namespace OpenUtau.Core.Ustx {
    public class UTempo {
        public int position;
        public double bpm;

        public UTempo() { }
        public UTempo(int position, double bpm) {
            this.position = position;
            this.bpm = bpm;
        }
        public override string ToString() => $"{bpm}@{position}";
    }

    public class UTimeSignature {
        public int barPosition;
        public int beatPerBar;
        public int beatUnit;

        public UTimeSignature() { }
        public UTimeSignature(int barPosition, int beatPerBar, int beatUnit) {
            this.barPosition = barPosition;
            this.beatPerBar = beatPerBar;
            this.beatUnit = beatUnit;
        }
        public override string ToString() => $"{beatPerBar}/{beatUnit}@bar{barPosition}";
    }

    public class UProject {
        public string name = "New Project";
        public string comment = string.Empty;
        public string outputDir = "Vocal";
        public string cacheDir = "UCache";
        [YamlMember(SerializeAs = typeof(string))]
        public Version ustxVersion;
        [YamlIgnore] public int resolution => 480;

        [Obsolete("Since ustx v0.6")] public double bpm = 120;
        [Obsolete("Since ustx v0.6")] public int beatPerBar = 4;
        [Obsolete("Since ustx v0.6")] public int beatUnit = 4;

        public Dictionary<string, UExpressionDescriptor> expressions = new Dictionary<string, UExpressionDescriptor>();
        public string[] expSelectors = new string[] { Format.Ustx.DYN, Format.Ustx.PITD, Format.Ustx.CLR, Format.Ustx.ENG, Format.Ustx.VEL, Format.Ustx.VOL, Format.Ustx.ATK, Format.Ustx.DEC, Format.Ustx.GEN, Format.Ustx.BRE };
        public int expPrimary = 0;
        public int expSecondary = 1;
        public int key = 0;//Music key of the project, 0 = C, 1 = C#, 2 = D, ..., 11 = B
        public List<UTimeSignature> timeSignatures;
        public List<UTempo> tempos;
        public List<UTrack> tracks;
        [YamlIgnore] public List<UPart> parts;
        [YamlIgnore] public bool SoloTrackExist { get => tracks.Any(t => t.Solo); }

        /// <summary>
        /// Transient field used for serialization.
        /// </summary>
        public List<UVoicePart>? voiceParts;
        /// <summary>
        /// Transient field used for serialization.
        /// </summary>
        public List<UWavePart>? waveParts;

        [YamlIgnore] public string FilePath { get; set; } = string.Empty;
        [YamlIgnore] public bool Saved { get; set; } = false;
        [YamlIgnore] public int EndTick => parts.Count == 0 ? 0 : parts.Max(p => p.End);

        [YamlIgnore] public readonly TimeAxis timeAxis = new TimeAxis();

        public UProject() {
            timeSignatures = new List<UTimeSignature> { new UTimeSignature(0, 4, 4) };
            tempos = new List<UTempo> { new UTempo(0, 120) };
            tracks = new List<UTrack>() { new UTrack("Track1") };
            parts = new List<UPart>();
            timeAxis.BuildSegments(this);
        }

        public void RegisterExpression(UExpressionDescriptor descriptor) {
            if (!expressions.ContainsKey(descriptor.abbr)) {
                expressions.Add(descriptor.abbr, descriptor);
            }
        }

        public void MargeExpression(string oldAbbr, string newAbbr) {
            if (parts != null && parts.Count > 0) {
                parts.Where(p => p is UVoicePart)
                    .OfType<UVoicePart>()
                    .ForEach(p => p.notes.ForEach(n => ConvertNoteExp(n, tracks[p.trackNo])));
            } else if (voiceParts != null && voiceParts.Count > 0) {
                voiceParts.ForEach(p => p.notes.ForEach(n => ConvertNoteExp(n, tracks[p.trackNo])));
            }
            expressions.Remove(oldAbbr);

            void ConvertNoteExp(UNote note, UTrack track) {
                if (note.phonemeExpressions.Any(e => e.abbr == oldAbbr)) {
                    var toRemove = new List<UExpression>();
                    note.phonemeExpressions.Where(e => e.abbr == oldAbbr).ForEach(oldExp => {
                        if (!note.phonemeExpressions.Any(newExp => newExp.abbr == newAbbr && newExp.index == oldExp.index)) {
                            // When there is only old exp, convert it to new exp
                            oldExp.abbr = newAbbr;
                            if (track.TryGetExpDescriptor(this, newAbbr, out var descriptor)) {
                                oldExp.descriptor = descriptor;
                            }
                        } else {
                            // When both old and new exp exist, remove the old one
                            toRemove.Add(oldExp);
                        }
                    });
                    toRemove.ForEach(exp => note.phonemeExpressions.Remove(exp));
                }
            }
        }

        public UNote CreateNote() {
            UNote note = UNote.Create();
            int start = NotePresets.Default.DefaultPortamento.PortamentoStart;
            int length = NotePresets.Default.DefaultPortamento.PortamentoLength;
            var shape = NotePresets.Default.DefaultPitchShape;
            note.pitch.AddPoint(new PitchPoint(start, 0, shape));
            note.pitch.AddPoint(new PitchPoint(start + length, 0, shape));
            return note;
        }

        public UNote CreateNote(int noteNum, int posTick, int durTick) {
            var note = CreateNote();
            note.tone = noteNum;
            note.position = posTick;
            note.duration = durTick;
            return note;
        }

        public void BeforeSave() {
            foreach (var track in tracks) {
                track.BeforeSave();
            }
            foreach (var part in parts) {
                part.BeforeSave(this, tracks[part.trackNo]);
            }
            voiceParts = parts
                .Where(part => part is UVoicePart)
                .OfType<UVoicePart>()
                .OrderBy(part => part.trackNo)
                .ThenBy(part => part.position)
                .ToList();
            waveParts = parts
                .Where(part => part is UWavePart)
                .OfType<UWavePart>()
                .OrderBy(part => part.trackNo)
                .ThenBy(part => part.position)
                .ToList();
        }

        public UProject CloneAsTemplate() {
            var project = new UProject() {
                ustxVersion = ustxVersion,
            };
            foreach (var kv in expressions) {
                project.expressions.Add(kv.Key, kv.Value.Clone());
            }
            return project;
        }

        public void AfterSave() {
            voiceParts = null;
            waveParts = null;
        }

        public void AfterLoad() {
            foreach (var track in tracks) {
                track.AfterLoad(this);
            }
            if (voiceParts != null) {
                parts.AddRange(voiceParts);
                voiceParts = null;
            }
            if (waveParts != null) {
                parts.AddRange(waveParts);
                waveParts = null;
            }
            foreach (var part in parts) {
                part.AfterLoad(this, tracks[part.trackNo]);
            }
        }

        public void Validate(ValidateOptions options) {
            if (!options.SkipTiming) {
                timeSignatures.Sort((lhs, rhs) => lhs.barPosition.CompareTo(rhs.barPosition));
                tempos.Sort((lhs, rhs) => lhs.position.CompareTo(rhs.position));
                timeAxis.BuildSegments(this);
            }
            if (options.Part == null) {
                foreach (var track in tracks) {
                    track.Validate(options, this);
                }
            }
            foreach (var part in parts) {
                if (options.Part == null || options.Part == part) {
                    part.Validate(options, this, tracks[part.trackNo]);
                }
            }
        }

        public void ValidateFull() {
            Validate(new ValidateOptions());
        }
    }
}
