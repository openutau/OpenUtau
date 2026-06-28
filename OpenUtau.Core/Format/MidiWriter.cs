using Melanchall.DryWetMidi.Common;
using System.Collections.Generic;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using System.Text;
using System.IO;
using UtfUnknown;
using System.Linq;

namespace OpenUtau.Core.Format {
    public class EncodingDetector {

        MemoryStream stream = new MemoryStream();

        public void ReadFile(string file) {
            var ReadingSettings = MidiWriter.BaseReadingSettings();
            
            ReadingSettings.DecodeTextCallback = new DecodeTextCallback(AddText);
            var midi = MidiFile.Read(file,ReadingSettings);
        }

        string AddText(byte[] bytes, ReadingSettings settings) {
            stream.Write(bytes);
            return "";
        }

        public Encoding Detect() {
            stream.Seek(0, SeekOrigin.Begin);
            var detectionResult = CharsetDetector.DetectFromStream(stream);
            if (detectionResult.Detected != null && detectionResult.Detected.Confidence > 0.5) {
                return detectionResult.Detected.Encoding;
            } else {
                return null;
            }
        }
    }

    public static class MidiWriter {
        //Create a blank new project and import data from midi files
        //Including tempo
        static public UProject LoadProject(string file) {
            UProject project = new UProject();
            Ustx.AddDefaultExpressions(project);
            project.FilePath = file;
            // Detects lyric encoding
            Encoding lyricEncoding = Encoding.UTF8;
            var encodingDetector = new EncodingDetector();
            encodingDetector.ReadFile(file);
            var encodingResult = encodingDetector.Detect();
            if (encodingResult != null) {
                lyricEncoding = encodingResult;
            }
            //Get midifile resolution
            var ReadingSettings = BaseReadingSettings();
            ReadingSettings.TextEncoding = lyricEncoding;
            var midi = MidiFile.Read(file, ReadingSettings);
            TicksPerQuarterNoteTimeDivision timeDivision = midi.TimeDivision as TicksPerQuarterNoteTimeDivision;
            var PPQ = timeDivision.TicksPerQuarterNote;
            //Parse tempo
            var tempoMap = midi.GetTempoMap();
            project.timeSignatures = ParseTimeSignatures(tempoMap, PPQ);
            project.tempos = ParseTempos(tempoMap, PPQ);

            //Parse tracks
            project.tracks = new List<UTrack>();

            var parts = ParseParts(midi, PPQ, project);
            foreach (var part in parts) {
                var track = new UTrack(project) {
                    TrackNo = project.tracks.Count
                };
                part.trackNo = track.TrackNo;
                if(part.name != "New Part"){
                    track.TrackName = part.name;
                }
                part.AfterLoad(project, track);
                project.tracks.Add(track);
                project.parts.Add(part);
            }
            project.ValidateFull();
            return project;
        }

        public static ReadingSettings BaseReadingSettings() {
            return new ReadingSettings {
                InvalidChannelEventParameterValuePolicy = InvalidChannelEventParameterValuePolicy.ReadValid,
                InvalidChunkSizePolicy = InvalidChunkSizePolicy.Ignore,
                InvalidMetaEventParameterValuePolicy = InvalidMetaEventParameterValuePolicy.SnapToLimits,
                MissedEndOfTrackPolicy = MissedEndOfTrackPolicy.Ignore,
                NoHeaderChunkPolicy = NoHeaderChunkPolicy.Ignore,
                NotEnoughBytesPolicy = NotEnoughBytesPolicy.Ignore,
                UnexpectedTrackChunksCountPolicy = UnexpectedTrackChunksCountPolicy.Ignore,
                UnknownChannelEventPolicy = UnknownChannelEventPolicy.SkipStatusByteAndOneDataByte,
                UnknownChunkIdPolicy = UnknownChunkIdPolicy.ReadAsUnknownChunk,
                UnknownFileFormatPolicy = UnknownFileFormatPolicy.Ignore
            };
        }

        
        //Import tracks to an existing project
        static public List<UVoicePart> Load(string file, UProject project) {
            // Detects lyric encoding
            Encoding lyricEncoding = Encoding.UTF8;
            var encodingDetector = new EncodingDetector();
            encodingDetector.ReadFile(file);
            var encodingResult = encodingDetector.Detect();
            if(encodingResult != null) {
                lyricEncoding = encodingResult;
            }
            //Get midifile resolution
            var ReadingSettings = BaseReadingSettings();
            ReadingSettings.TextEncoding = lyricEncoding;
            var midi = MidiFile.Read(file, ReadingSettings);
            TicksPerQuarterNoteTimeDivision timeDivision = midi.TimeDivision as TicksPerQuarterNoteTimeDivision;
            var PPQ = timeDivision.TicksPerQuarterNote;
            return ParseParts(midi, PPQ, project);
        }

        public static List<UTempo> ParseTempos(TempoMap tempoMap, short PPQ) {
            List<UTempo> UTempoList = new List<UTempo>();
            var changes = tempoMap.GetTempoChanges();
            if (changes != null && changes.Count() > 0) {
                var firstTempoTime = changes.First().Time;
                if (firstTempoTime > 0) {
                    UTempoList.Add(new UTempo {
                        position = 0,
                        bpm = 120.0
                    });
                }
                foreach (var change in changes) {
                    var tempo = change.Value;
                    var time = change.Time * 480 / PPQ;
                    UTempoList.Add(new UTempo {
                        position = (int)time,
                        bpm = 60.0 / tempo.MicrosecondsPerQuarterNote * 1000000.0
                    });
                }
            } else {//Midi doesn't contain any tempo change
                UTempoList.Add(new UTempo {
                    position = 0,
                    bpm = 120.0
                });
            }
            return UTempoList;
        }

        public static List<UTimeSignature> ParseTimeSignatures(TempoMap tempoMap, short PPQ) {
            List<UTimeSignature> UTimeSignatureList = new List<UTimeSignature>();
            var lastUTimeSignature = new UTimeSignature {
                barPosition = 0,
                beatPerBar = 4,
                beatUnit = 4
            };
            var changes = tempoMap.GetTimeSignatureChanges();
            if (changes != null && changes.Count() > 0) {
                var firstTimeSignatureTime = changes.First().Time;
                if (firstTimeSignatureTime > 0) {
                    UTimeSignatureList.Add(lastUTimeSignature);
                }
                int lastTime = 0;
                foreach (var change in changes) {
                    var timeSignature = change.Value;
                    var time = (int)(change.Time) / PPQ;
                    lastUTimeSignature = new UTimeSignature {
                        barPosition = lastUTimeSignature.barPosition + (time - lastTime) * lastUTimeSignature.beatUnit / 4 / lastUTimeSignature.beatPerBar,
                        beatPerBar = timeSignature.Numerator,
                        beatUnit = timeSignature.Denominator
                    };
                    UTimeSignatureList.Add(lastUTimeSignature);
                    lastTime = time;
                }
            } else {
                UTimeSignatureList.Add(lastUTimeSignature);
            }
            return UTimeSignatureList;
        }

        static List<UVoicePart> ParseParts(MidiFile midi, short PPQ, UProject project) {
            string defaultLyric = NotePresets.Default.DefaultLyric;
            List<UVoicePart> resultParts = new List<UVoicePart>();
            foreach (TrackChunk trackChunk in midi.GetTrackChunks()) {
                var midiNoteList = trackChunk.GetNotes().ToList();
                if (midiNoteList.Count > 0) {
                    var part = new UVoicePart();
                    using (var objectsManager = new TimedObjectsManager<TimedEvent>(trackChunk.Events)) {
                        var events = objectsManager.Objects;
                        //{position of lyric: lyric text}
                        Dictionary<long, string> lyrics = events.Where(e => e.Event is LyricEvent)
                            .GroupBy(e => e.Time)
                            .ToDictionary(g=> g.Key, g => ((LyricEvent)g.First().Event).Text);
                        var trackName = events.Where(e => e.Event is SequenceTrackNameEvent)
                            .Select(e => ((SequenceTrackNameEvent)e.Event).Text).FirstOrDefault();
                        if (trackName != null) {
                            part.name = trackName;
                        }
                        foreach (Melanchall.DryWetMidi.Interaction.Note midiNote in midiNoteList) {
                            var note = project.CreateNote(
                                midiNote.NoteNumber,
                                (int)(midiNote.Time * project.resolution / PPQ),
                                (int)(midiNote.Length * project.resolution / PPQ)
                            );
                            //handle lyric import
                            bool hasLyric = lyrics.TryGetValue(midiNote.Time, out string lyric);
                            if (!hasLyric) {
                                lyric = defaultLyric;
                            }
                            if (lyric == "-") {
                                lyric = "+~";
                            }
                            note.lyric = lyric;
                            if (NotePresets.Default.AutoVibratoToggle && note.duration >= NotePresets.Default.AutoVibratoNoteDuration) {
                                note.vibrato.length = NotePresets.Default.DefaultVibrato.VibratoLength;
                            }
                            part.notes.Add(note);
                        }
                    }
                    resultParts.Add(part);
                }
            }
            return resultParts;
        }

        static public void Save(string filePath, UProject project) {
            var midiFile = new MidiFile();
            var trackChunks = new List<TrackChunk> { };

            //Project resolution
            midiFile.TimeDivision = new TicksPerQuarterNoteTimeDivision((short)project.resolution);
            //Tempo
            midiFile.Chunks.Add(new TrackChunk());
            using (TempoMapManager tempoMapManager = midiFile.ManageTempoMap()) {
                var lastUTimeSignature = new UTimeSignature {
                    barPosition = 0,
                    beatPerBar = 4,
                    beatUnit = 4
                };
                int lastTime = 0;
                foreach (UTimeSignature uTimeSignature in project.timeSignatures) {
                    var time = lastTime + (uTimeSignature.barPosition - lastUTimeSignature.barPosition) * lastUTimeSignature.beatPerBar * 4 / lastUTimeSignature.beatUnit * project.resolution;
                    tempoMapManager.SetTimeSignature(time, new TimeSignature(uTimeSignature.beatPerBar, uTimeSignature.beatUnit));
                    lastUTimeSignature = uTimeSignature;
                    lastTime = time;
                }
                foreach(UTempo uTempo in project.tempos){
                    tempoMapManager.SetTempo(uTempo.position, Tempo.FromBeatsPerMinute(uTempo.bpm));
                }
            }
            //Time Signature
            foreach (UTrack track in project.tracks) {
                var trackChunk = new TrackChunk();
                using (var objectsManager = new TimedObjectsManager<TimedEvent>(trackChunk.Events)) {
                    var events = objectsManager.Objects;
                    events.Add(new TimedEvent(new SequenceTrackNameEvent(track.TrackName), 0));
                }
                trackChunks.Add(trackChunk);
            }
            //voice tracks
            foreach (UPart part in project.parts) {
                if (part is UVoicePart voicePart) {
                    var trackChunk = trackChunks[voicePart.trackNo];
                    var partOffset = part.position;
                    using (var objectsManager = new TimedObjectsManager<TimedEvent>(trackChunk.Events)) {
                        var events = objectsManager.Objects;
                        foreach (UNote note in voicePart.notes) {
                            //Ignore notes whose pitch is out of midi range
                            if(note.tone < 0 || note.tone > 127){
                                continue;
                            }
                            string lyric = note.lyric;
                            if (lyric == "+~" || lyric == "+*") {
                                lyric = "-";
                            }
                            events.Add(new TimedEvent(new LyricEvent(lyric), note.position + partOffset));
                            events.Add(new TimedEvent(new NoteOnEvent((SevenBitNumber)(note.tone), (SevenBitNumber)45), note.position + partOffset));
                            events.Add(new TimedEvent(new NoteOffEvent((SevenBitNumber)(note.tone), (SevenBitNumber)45), note.position + partOffset + note.duration));
                        }
                    }
                }
            }
            
            foreach(TrackChunk trackChunk in trackChunks) {
                midiFile.Chunks.Add(trackChunk);
            }

            midiFile.Write(filePath,true,settings: new WritingSettings {
                TextEncoding = System.Text.Encoding.UTF8,
            }) ;
        }
    }
}
