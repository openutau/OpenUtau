using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.Serialization;
using OpenUtau.Core.Format.MusicXMLSchema;
using OpenUtau.Core.Ustx;
using Serilog;
using UtfUnknown;

namespace OpenUtau.Core.Format
{
    public static class MusicXML
    {
        static StartStopContinue? NoteTieStatus(MusicXMLSchema.Note note)
        {
            if (note.Tie == null) {
                return null;
            }
            bool hasStart = false;
            bool hasStop = false;
            foreach (var tie in note.Tie) {
                if (tie.Type == StartStop.Start) {
                    hasStart = true;
                } else if (tie.Type == StartStop.Stop) {
                    hasStop = true;
                }
            }
            if (hasStart && hasStop) {
                return StartStopContinue.Continue;
            } else if (hasStart) {
                return StartStopContinue.Start;
            } else if (hasStop) {
                return StartStopContinue.Stop;
            }
            return null;
        }
        
        static StartStopContinue? NoteSlurStatus(MusicXMLSchema.Note note)
        {
            if (note.Notations == null) {
                return null;
            }
            bool hasStart = false;
            bool hasStop = false;
            foreach (var notation in note.Notations) {
                foreach (var slur in notation.Slur) {
                    if (slur.Type == StartStopContinue.Start) {
                        hasStart = true;
                    } else if (slur.Type == StartStopContinue.Stop) {
                        hasStop = true;
                    } else if (slur.Type == StartStopContinue.Continue) {
                        hasStart = true;
                        hasStop = true;
                    }
                }
            }
            if (hasStart && hasStop) {
                return StartStopContinue.Continue;
            } else if (hasStart) {
                return StartStopContinue.Start;
            } else if (hasStop) {
                return StartStopContinue.Stop;
            }
            return null;
        }

        static Syllabic SyllabicStatus(MusicXMLSchema.Lyric lyric)
        {
            if (lyric.Syllabic == null || lyric.Syllabic.Count == 0) {
                return Syllabic.Single;
            }
            return lyric.Syllabic[0];
        }

        static public UProject LoadProject(string file) {
            UProject uproject = new UProject();
            Ustx.AddDefaultExpressions(uproject);

            uproject.tracks.Clear();
            uproject.parts.Clear();
            uproject.tempos.Clear();
            uproject.timeSignatures.Clear();

            var score = ReadXMLScore(file);

            foreach (var part in score.Part) {
                var utrack = new UTrack(uproject);
                utrack.TrackNo = uproject.tracks.Count;
                uproject.tracks.Add(utrack);

                var upart = new UVoicePart();
                upart.trackNo = utrack.TrackNo;
                uproject.parts.Add(upart);

                int divisions = (int)part.Measure[0].Attributes[0].Divisions;
                int prevPosTick = 0;
                int currPosTick = 0;

                var tiedNotes = new Dictionary<int, UNote>();
                UNote? incompletedLyricNote = null;
                foreach (var measure in part.Measure) {
                    // BPM
                    double? bpm;
                    if ((bpm = MeasureBPM(measure)).HasValue) {
                        uproject.tempos.Add(new UTempo(currPosTick, bpm.Value));
                        Log.Information($"Measure {measure.Number} BPM: {bpm.ToString()}");
                    }

                    // Time Signature
                    foreach (var attributes in measure.Attributes) {
                        foreach (var time in attributes.Time) {
                            if (time.Beats.Count > 0 && time.BeatType.Count > 0) {
                                uproject.timeSignatures.Add(new UTimeSignature {
                                    barPosition = currPosTick,
                                    beatPerBar = Int32.Parse(time.Beats[0]),
                                    beatUnit = Int32.Parse(time.BeatType[0])
                                });
                                Log.Information($"Measure {measure.Number} Time Signature: {time.Beats[0]}/{time.BeatType[0]}");
                            }
                        }
                    }

                    // Note
                    foreach (var element in measure.Content) {
                        switch (element) {
                            case Note note: {
                                    int durTick = (int)note.Duration * uproject.resolution / divisions;
                                    //If it's a chord, the position is the same as the previous note.
                                    //Otherwise, it's the current position.
                                    int posTick = note.Chord == null ? currPosTick : prevPosTick;

                                    if (note.Rest != null) {
                                        // pass
                                    } else {
                                        var pitch = note.Pitch.Step.ToString() + note.Pitch.Octave.ToString();
                                        int tone = MusicMath.NameToTone(pitch) + (int)note.Pitch.Alter;
                                        var tieStatus = NoteTieStatus(note);
                                        var slurStatus = NoteSlurStatus(note);
                                        var syllabicStatus = note.Lyric.Count > 0 ? SyllabicStatus(note.Lyric[0]) : Syllabic.Single;
                                        UNote NewNote() {
                                            var unote = uproject.CreateNote(tone, posTick, durTick);
                                            if (note.Lyric.Count > 0) {
                                                if ((syllabicStatus == Syllabic.Middle
                                                    || syllabicStatus == Syllabic.End)
                                                    && incompletedLyricNote != null) {
                                                    // For multisyllable words, OpenUtau use + to place the following syllables.
                                                    incompletedLyricNote.lyric += note.Lyric[0].Text[0].Value;
                                                    unote.lyric = "+";
                                                } else {
                                                    unote.lyric = note.Lyric[0].Text[0].Value;
                                                    incompletedLyricNote = unote;
                                                }
                                                if(syllabicStatus == Syllabic.Single || syllabicStatus == Syllabic.End) {
                                                    incompletedLyricNote = null;
                                                }
                                            } else if (slurStatus == StartStopContinue.Continue || slurStatus == StartStopContinue.Stop) {
                                                // OpenUtau uses +~ for extending the current syllable,
                                                // which is represented in sheet music as slur.
                                                unote.lyric = "+~";
                                            }
                                            return unote;
                                        }

                                        if (tieStatus == StartStopContinue.Start) {
                                            var unote = NewNote();
                                            upart.notes.Add(unote);
                                            tiedNotes[tone] = unote;
                                        } else if (tieStatus == StartStopContinue.Continue) {
                                            if (tiedNotes.ContainsKey(tone)) {
                                                tiedNotes[tone].duration += durTick;
                                            } else {
                                                // If there's no previous tied note, create a new one.
                                                var unote = NewNote();
                                                upart.notes.Add(unote);
                                                tiedNotes[tone] = unote;
                                            }
                                        } else if (tieStatus == StartStopContinue.Stop) {
                                            if (tiedNotes.ContainsKey(tone)) {
                                                tiedNotes[tone].duration += durTick;
                                                tiedNotes.Remove(tone);
                                            } else {
                                                // If there's no previous tied note, create a new one.
                                                var unote = NewNote();
                                                upart.notes.Add(unote);
                                            }
                                        } else {
                                            // No tie
                                            UNote unote = NewNote();
                                            upart.notes.Add(unote);
                                        }
                                    }
                                    prevPosTick = posTick;
                                    currPosTick = posTick + durTick;
                                }
                                break;
                            case MusicXMLSchema.Backup backup: {
                                    int durTick = (int)backup.Duration * uproject.resolution / divisions;
                                    currPosTick -= durTick;
                                    prevPosTick = currPosTick;
                                }
                                break;
                            case MusicXMLSchema.Forward forward: {
                                    int durTick = (int)forward.Duration * uproject.resolution / divisions;
                                    currPosTick += durTick;
                                    prevPosTick = currPosTick;
                                }
                                break;
                        }
                    }
                }
                upart.Duration = upart.GetMinDurTick(uproject);
            }
            if (uproject.tempos.Count == 0) {
                uproject.tempos.Add(new UTempo(0, 120));
            }
            if (uproject.tempos[0].position > 0) {
                uproject.tempos[0].position = 0;
            }
            uproject.AfterLoad();
            uproject.ValidateFull();
            return uproject;
        }

        static public System.Text.Encoding DetectXMLEncoding(string file)
        {
            System.Text.Encoding xmlEncoding = System.Text.Encoding.UTF8;
            var detectionResult = CharsetDetector.DetectFromFile(file);

            if (detectionResult.Detected != null && detectionResult.Detected.Confidence > 0.5)
            {
                xmlEncoding = detectionResult.Detected.Encoding;
            }
            return xmlEncoding;
        }

        static public double? MeasureBPM(MusicXMLSchema.ScorePartwisePartMeasure measure)
        {
            foreach (var direction in measure.Directions) {
                if (direction.Sound != null) { return (double)direction.Sound.Tempo; }
            }
            return null;
        }

        static public MusicXMLSchema.ScorePartwise ReadXMLScore(string xmlFile)
        {
            Log.Information($"MusicXML Character Encoding: {DetectXMLEncoding(xmlFile)}");

            XmlReaderSettings settings = new XmlReaderSettings();
            settings.DtdProcessing = DtdProcessing.Parse;
            settings.MaxCharactersFromEntities = 1024;

            using (var fs = new FileStream(xmlFile, FileMode.Open))
            using (var xmlReader = XmlReader.Create(fs, settings))
            {
                XmlSerializer s = new XmlSerializer(typeof(MusicXMLSchema.ScorePartwise));

                var score = s.Deserialize(xmlReader) as MusicXMLSchema.ScorePartwise;
                return score;
            }
        }
    }
}
