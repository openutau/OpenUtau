using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

using OpenUtau.Core.Ustx;

//reference: https://github.com/sdercolin/utaformatix-data/blob/main/lib/csharp/UtaFormatix.Data

namespace OpenUtau.Core.Format
{
    /// <summary>Note model.</summary>
    ///
    /// <param name="key">Semitone value of the note's key (Center C = 60).</param>
    /// <param name="tickOn">Tick position of the note's start.</param>
    /// <param name="tickOff">Tick position of the note's end.</param>
    /// <param name="lyric">Lyric of the note.</param>
    /// <param name="phoneme">Phoneme of the note (if available).</param>
    public struct UfNote{
        public int key;
        public int tickOn;
        public int tickOff;
        public string lyric;
        public string? phoneme;
    }

    /// <summary>Pitch data model. 
    /// Only points with changed values are included.</summary>
    ///
    /// <param name="ticks">Tick positions of the data points.</param>
    /// <param name="values">Semitone values of the data points.
    /// Items could be `null` only when [isAbsolute] is true.
    /// In this case, it represents the end of the previous value's lasting.</param>
    /// <param name="isAbsolute">True if the semitone value is absolute,
    /// otherwise it's relative to the note's key.</param>
    public struct UfPitch{
        public int[] ticks;
        public double?[] values;
        public bool isAbsolute;
    }

    /// <summary>Tempo label model.</summary>
    ///
    /// <param name="tickPosition">Tick position of the tempo label.</param>
    /// <param name="bpm">Tempo in beats-per-minute.</param>
    public struct UfTempo{
        public int tickPosition;
        public double bpm;
    }

    /// <summary>Time signature model.</summary>
    ///
    /// <param name="measurePosition">Measure (bar) position of the time signature.</param>
    /// <param name="numerator">Beats per measure.</param>
    /// <param name="denominator">Note value per beat.</param>
    public struct UfTimeSignature{
        public int measurePosition; 
        public int numerator; 
        public int denominator;
    }

    /// <summary>Track model.</summary>
    ///
    /// <param name="name">Track name.</param>
    /// <param name="notes">Notes in the track.</param>
    /// <param name="pitch">Pitch data bound to the track (if any).</param>
    public struct UfTrack{
        public string name; 
        public UfNote[] notes; 
        public UfPitch? pitch;
    }

    /// <summary>Project model.</summary>
    ///
    /// <param name="name">Project name.</param>
    /// <param name="tracks">List of track models in the project.</param>
    /// <param name="timeSignatures">List of time signatures in the project.</param>
    /// <param name="tempos">List of tempo labels in the project.</param>
    /// <param name="measurePrefix">Count of measure prefixes (measures that cannot
    /// contain notes, restricted by some editors).</param>
    public struct UfProject{
        public string name; 
        public UfTrack[] tracks; 
        public UfTimeSignature[]? timeSignatures; 
        public UfTempo[] tempos; 
        public int measurePrefix;
    }

    public struct UfFile{
        public UfProject project;
        public int formatVersion;
    }

    public static class Ufdata
    {
        static UVoicePart ParsePart(UfTrack ufTrack, UProject project) {
            var part = new UVoicePart();
            part.name = ufTrack.name;
            part.position = 0;
            foreach(var ufNote in ufTrack.notes){
                var note = project.CreateNote(
                    ufNote.key,
                    ufNote.tickOn,
                    ufNote.tickOff - ufNote.tickOn
                );
                note.lyric = ufNote.lyric;
                if (note.lyric == "-") {
                    note.lyric = "+~";
                }
                part.notes.Add(note);
            }
            part.Duration = ufTrack.notes[^1].tickOff;
            return part;
        }

        public static UProject Load(string file){
            UProject project = new UProject();
            Ustx.AddDefaultExpressions(project);
            project.FilePath = file;

            var ufProject = JsonConvert.DeserializeObject<UfFile>(File.ReadAllText(file,Encoding.UTF8)).project;
            
            //parse tempo
            project.tempos=ufProject.tempos
                .Select(t => new UTempo(t.tickPosition, t.bpm))
                .ToList();
            //parse timeSignature
            project.timeSignatures=ufProject.timeSignatures
                .Select(t => new UTimeSignature(t.measurePosition, t.numerator, t.denominator))
                .ToList();
            //parse tracks
            var parts = ufProject.tracks
                .Where(tr=>tr.notes.Length>0)
                .Select(tr=>ParsePart(tr,project))
                .ToList();
            foreach (var part in parts) {
                var track = new UTrack(project);
                track.TrackNo = project.tracks.Count;
                part.trackNo = track.TrackNo;
                part.AfterLoad(project, track);
                project.tracks.Add(track);
                project.parts.Add(part);
            }
            
            project.ValidateFull();
            return project;
        }
    }
}
